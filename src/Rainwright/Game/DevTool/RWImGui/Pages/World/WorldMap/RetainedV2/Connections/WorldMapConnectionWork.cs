using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DryCycle.DevUI.DevTool.Map;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal sealed partial class WorldMapConnectionResourceStore
{
    private sealed class WorkResult
    {
        internal Dictionary<string, ConnectionRouteResource> Routes;
        internal bool Layout;
        internal WorldMapCorridorLaneApplyResult Lanes;
        internal WorldMapCrossingResolveResult Crossings;
        internal long BuildTicks, LaneTicks, CrossingTicks;
    }
    private Task<WorkResult> routeWork;
    private CancellationTokenSource routeWorkCancellation;
    private WorldMapOrthogonalRouter.CacheContext routingContext = new();
    private long workRevision, pendingWorkRevision, pendingRouteRevision;
    private int workGeneration, pendingWorkGeneration;
    private string[] pendingRouteIds = Array.Empty<string>();
    private long failedWorkRevision = long.MinValue, failedRouteRevision = long.MinValue;
    private int failedWorkGeneration = -1;

    private bool WorkBlocked => failedWorkGeneration == workGeneration &&
        failedWorkRevision == workRevision && failedRouteRevision == revision;

    private void InvalidateRouteWork()
    {
        unchecked { workRevision++; }
        routeWorkCancellation?.Cancel();
    }

    private CancellationToken BeginRouteWork()
    {
        routeWorkCancellation = new CancellationTokenSource();
        return routeWorkCancellation.Token;
    }

    private void StartRouteWork(WorldMapWorldSpaceRouter.BuildInput input)
    {
        pendingWorkGeneration = workGeneration; pendingWorkRevision = workRevision;
        pendingRouteRevision = revision;
        pendingRouteIds = new string[input.Accepted.Count];
        for (int i = 0; i < pendingRouteIds.Length; i++) pendingRouteIds[i] = input.Accepted[i].Id;
        var context = routingContext;
        CancellationToken cancellation = BeginRouteWork();
        routeWork = Task.Run(() =>
        {
            long started = Stopwatch.GetTimestamp();
            var result = WorldMapWorldSpaceRouter.Execute(input, context, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new WorkResult { Routes = result, BuildTicks = Stopwatch.GetTimestamp() - started };
        }, cancellation);
    }

    private void StartLayoutWork()
    {
        if (routeWork != null) return;
        var snapshot = new Dictionary<string, ConnectionRouteResource>(StringComparer.Ordinal);
        foreach (var pair in routes)
        {
            ConnectionRouteResource copy = pair.Value.CloneForWorker();
            endpointDensityTiers.TryGetValue(pair.Key, out copy.BaseDensityTier);
            snapshot.Add(pair.Key, copy);
        }
        var obstacles = Array.Empty<WorldMapOrthogonalRouter.Obstacle>();
        bool applyLanes = corridorLayoutDirty;
        CancellationToken cancellation = BeginRouteWork();
        pendingWorkGeneration = workGeneration; pendingWorkRevision = workRevision; pendingRouteRevision = revision;
        pendingRouteIds = Array.Empty<string>();
        routeWork = Task.Run(() =>
        {
            long started = Stopwatch.GetTimestamp(), localRevision = 0;
            cancellation.ThrowIfCancellationRequested();
            var lanes = applyLanes ? WorldMapCorridorLaneAllocator.Apply(snapshot, obstacles, new HashSet<string>(), ref localRevision, cancellation) :
                WorldMapCorridorLaneApplyResult.Empty;
            cancellation.ThrowIfCancellationRequested();
            long laneTicks = Stopwatch.GetTimestamp() - started;
            started = Stopwatch.GetTimestamp();
            var crossings = WorldMapRouteCrossingResolver.Build(snapshot, cancellation);
            cancellation.ThrowIfCancellationRequested();
            return new WorkResult { Routes = snapshot, Layout = true, Lanes = lanes, Crossings = crossings,
                LaneTicks = laneTicks, CrossingTicks = Stopwatch.GetTimestamp() - started };
        }, cancellation);
    }

    private void DrainRouteWork(WorldMapScene scene)
    {
        if (routeWork == null || !routeWork.IsCompleted) return;
        Task<WorkResult> finished = routeWork; routeWork = null;
        routeWorkCancellation?.Dispose();
        routeWorkCancellation = null;
        bool current = workGeneration == pendingWorkGeneration &&
            workRevision == pendingWorkRevision && revision == pendingRouteRevision;
        if (finished.IsFaulted)
        {
            RainwrightFrontendDiagnostics.Error("WorldMap background routing failed; previous routes retained. " +
                (current ? "Waiting for input changes before retrying. " : "Obsolete work discarded. ") + finished.Exception);
            if (current)
            {
                failedWorkGeneration = workGeneration;
                failedWorkRevision = workRevision;
                failedRouteRevision = revision;
            }
        }
        if (finished.IsCanceled || finished.IsFaulted || !current)
        {
            if (workGeneration == pendingWorkGeneration)
                RequeuePendingRoutes(scene);
            return;
        }
        WorkResult result = finished.Result;
        bool routesChanged = false;
        foreach (var pair in result.Routes)
        {
            // Lane derivation increments only the resources whose path/density changed.
            // A crossings-only job must not invalidate every retained GPU mesh.
            if (result.Layout && routes.TryGetValue(pair.Key, out var existing) &&
                pair.Value.Revision == existing.Revision)
                continue;
            if (!result.Layout && routes.TryGetValue(pair.Key, out existing) &&
                existing.HasSameSource(pair.Value))
                continue; // Keep the already-derived lanes until a changed source needs new geometry.
            pair.Value.Revision = routes.TryGetValue(pair.Key, out var old) ? old.Revision + 1 : 1;
            routes[pair.Key] = pair.Value;
            routeChanged.Add(pair.Key);
            routesChanged = true;
        }
        if (routesChanged)
        {
            unchecked { revision++; }
            MapRoomGeometryPresentationHub.MarkPersistentFrontendDirty();
        }
        if (result.Layout)
        {
            corridorLayoutDirty = false; crossingLayoutDirty = false;
            crossings = result.Crossings.Marks;
            crossingBudgetLimited = result.Crossings.BudgetLimited; crossingCandidateChecks = result.Crossings.CandidateChecks;
            crossingRevision++;
            corridorPerfCount++; corridorPerfTotalTicks += result.LaneTicks;
            corridorPerfPeakTicks = Math.Max(corridorPerfPeakTicks, result.LaneTicks);
            crossingPerfCount++; crossingPerfTotalTicks += result.CrossingTicks;
            crossingPerfPeakTicks = Math.Max(crossingPerfPeakTicks, result.CrossingTicks);
            // Compressed lanes use density LOD. Re-routing for soft congestion can turn a valid
            // short link into an enormous loop and adds another whole-region layout pass.
        }
        else
        {
            corridorLayoutDirty = crossingLayoutDirty = true;
            routeBuildPerfTotalTicks += result.BuildTicks;
            routeBuildPerfPeakTicks = Math.Max(routeBuildPerfPeakTicks, result.BuildTicks);
            routeBuildPerfBatches++; routeBuildPerfRoutes += result.Routes.Count;
        }
        UpdateRouteLoadSession();
    }

    private void RequeuePendingRoutes(WorldMapScene scene)
    {
        foreach (string id in pendingRouteIds)
            if (scene?.Connections.ContainsKey(id) == true && queued.Add(id))
                queue.Enqueue(id);
    }
}
