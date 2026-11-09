using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Map;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Frontend lifetime owner for the retained V2 World Map.
/// Render-thread scene projection, main-thread retained resources and off-screen presentation meet
/// here without making authoring data or the legacy MapPage presentation authoritative.
/// </summary>
internal static class WorldMapRetainedV2Runtime
{
    private static readonly WorldMapScene RenderSceneState = new();
    private static readonly WorldMapScene MainSceneState = new();
    private static readonly WorldMapSceneSynchronizer Synchronizer = new();
    private static readonly WorldMapSceneTransfer SceneTransfer = new();
    private static readonly WorldMapViewTransformMailbox ViewMailbox = new();
    private static readonly WorldMapRoomResourceStore RoomResources = new();
    private static readonly WorldMapConnectionResourceStore ConnectionResources = new();
    private static readonly WorldMapRenderTextureSurface Surface = new();
    private static readonly WorldMapRetainedRoomRenderer RoomRenderer = new();
    private static readonly WorldMapRetainedConnectionRenderer ConnectionRenderer = new();
    private static readonly WorldMapSpatialIndex SpatialIndex = new();
    private static readonly WorldMapRouteSpatialIndex RouteSpatialIndex = new();
    private static readonly WorldMapDirtySet mainThreadDirty = new();
    private static readonly List<int> geometryChangedRooms = new();
    private static readonly List<string> routeChanged = new();
    private static readonly List<int> sourcePriorityRooms = new();
    private static readonly List<int> visibleRooms = new();
    private static readonly List<string> visibleRoutes = new();

    private sealed class PresentedRouteSnapshot
    {
        internal readonly HashSet<string> Ids;
        internal readonly WorldMapRouteSpatialIndex SpatialIndex;
        internal readonly Dictionary<string, WorldMapCrossingMark[]> CrossingsByRoute;

        internal PresentedRouteSnapshot(
            HashSet<string> ids,
            WorldMapRouteSpatialIndex spatialIndex,
            Dictionary<string, WorldMapCrossingMark[]> crossingsByRoute)
        {
            Ids = ids ?? new HashSet<string>(StringComparer.Ordinal);
            SpatialIndex = spatialIndex ?? new WorldMapRouteSpatialIndex();
            CrossingsByRoute =
                crossingsByRoute ??
                new Dictionary<string, WorldMapCrossingMark[]>(
                    StringComparer.Ordinal);
        }

        internal static PresentedRouteSnapshot Empty() =>
            new(
                new HashSet<string>(StringComparer.Ordinal),
                new WorldMapRouteSpatialIndex(),
                new Dictionary<string, WorldMapCrossingMark[]>(
                    StringComparer.Ordinal));
    }

    private static PresentedRouteSnapshot presentedRoutes =
        PresentedRouteSnapshot.Empty();
    private static long presentedRouteRevision = long.MinValue;
    private static Dictionary<string, WorldMapCrossingMark[]> liveCrossingsByRoute =
        new(StringComparer.Ordinal);
    private static long liveCrossingRevision = long.MinValue;
    private static bool liveCrossingsCurrent;
    private static WorldMapDirtySet lastDirty = new();
    private static ManualLogSource log;
    private static volatile bool enabled;
    // Any partially initialized retained service still owns resources until
    // *both* the persistent cache callback and GPU/scene cleanup succeed.
    private static bool cleanupPending;
    private static string resourceRegion = string.Empty;
    private static int activeLayerMask = 7;
    private static int activeShowConnections = 1;
    private static float activeZoom = 1f;
    private static long canvasSeenAt;
    internal static bool CanvasVisible => enabled && !EditorUiModeState.OverlayHidden &&
        Stopwatch.GetTimestamp() - Interlocked.Read(ref canvasSeenAt) < Stopwatch.Frequency / 4;
    private static int retainedConnectionsReady;
    private static long lastRenderedViewRevision = long.MinValue;
    private static long lastRenderedSceneRevision = long.MinValue;
    private static long lastRenderedRoomResourceRevision = long.MinValue;
    private static long lastRenderedRouteRevision = long.MinValue;
    private static int lastRenderedLayerMask = int.MinValue;
    private static int lastRenderedShowConnections = int.MinValue;

    private static string readinessRegion = string.Empty;
    private static long readinessStartedTicks;
    private static long firstSurfaceReadyTicks;
    private static long readinessCompletedTicks;
    private static int readinessExpectedRooms;
    private static int readinessExpectedConnections;
    private static bool readinessComplete;

    internal static WorldMapScene Scene => RenderSceneState;
    internal static WorldMapScene MainSceneForPersistence => MainSceneState;
    internal static WorldMapDirtySet LastDirty => lastDirty;
    internal static WorldMapRoomResourceStore Resources => RoomResources;
    internal static WorldMapConnectionResourceStore Routes => ConnectionResources;
    internal static float LatestZoom
    {
        get
        {
            float value = Volatile.Read(ref activeZoom);
            return enabled && value > 0.0001f ? value : 1f;
        }
    }

    internal static bool ViewReadinessComplete => readinessComplete;
    internal static bool IsViewReadyForRegion(string region) => readinessComplete &&
        string.Equals(readinessRegion, region, StringComparison.OrdinalIgnoreCase);
    internal static double FirstSurfaceReadyMilliseconds =>
        ElapsedMilliseconds(readinessStartedTicks, firstSurfaceReadyTicks);
    internal static double ViewReadinessMilliseconds =>
        ElapsedMilliseconds(readinessStartedTicks, readinessCompletedTicks);

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        if (cleanupPending)
            throw new InvalidOperationException(
                "Retained World Map cleanup is incomplete; duplicate activation refused.");
        // Initialization may publish GPU/cache resources and then throw.
        cleanupPending = true;
        enabled = true;
        log = logger;
        RoomResources.Initialize(logger);
        WorldMapPersistentRetainedCache.Enable(logger);
    }

    internal static void Disable()
    {
        enabled = false;
        if (!cleanupPending) return;

        // These are separate owners. If cache callback detach fails, room and
        // GPU resources still need their own best-effort cleanup. A failure in
        // either part retains this runtime's cleanup obligation for a retry.
        List<Exception> failures = new();
        try { WorldMapPersistentRetainedCache.Disable(); }
        catch (Exception error) { failures.Add(error); }
        try { ResetRetainedState(); }
        catch (Exception error) { failures.Add(error); }

        if (failures.Count != 0)
            throw new AggregateException(
                "Retained World Map shutdown is incomplete; cleanup remains owned.", failures);

        cleanupPending = false;
        log = null;
    }

    internal static void Synchronize(
        EditorMapPresentationSnapshot snapshot,
        IReadOnlyDictionary<int, Num.Vector2> localPositions,
        long layoutRevision,
        int interactiveRoom,
        int layerMask,
        bool showConnections,
        WorldMapViewTransform viewTransform)
    {
        if (!enabled) return;

        long now = Stopwatch.GetTimestamp();
        Interlocked.Exchange(ref canvasSeenAt, now);
        TrackViewReadinessStart(
            snapshot,
            now);

        Volatile.Write(ref activeLayerMask, layerMask);
        Volatile.Write(ref activeShowConnections, showConnections ? 1 : 0);
        Volatile.Write(ref activeZoom, viewTransform.Zoom > 0.0001f ? viewTransform.Zoom : 1f);

        lastDirty = Synchronizer.Synchronize(
            RenderSceneState,
            snapshot,
            localPositions,
            layoutRevision,
            interactiveRoom,
            viewTransform);

        ViewMailbox.Publish(viewTransform, RenderSceneState.ViewRevision);
        SceneTransfer.Publish(
            WorldMapSceneDelta.Capture(RenderSceneState, lastDirty));
    }

    internal static void UpdateMainThread()
    {
        if (!enabled) return;

        EditorSession session = DevToolRuntime.ActiveSession;
        EditorMapPresentationSnapshot snapshot = MapEditorPresentationHub.Current;

        if (ViewMailbox.TryRead(
                out WorldMapViewTransform latestView,
                out long _))
            MainSceneState.SetViewTransform(latestView);

        SceneTransfer.Drain(MainSceneState, mainThreadDirty);

        // Warp publishes the game snapshot and UI scene on different threads. Keep deltas pending
        // until they refer to the same world; never decode HI sources into the old B5 room indices.
        if (snapshot?.Available != true ||
            !string.Equals(MainSceneState.Region, snapshot.RegionName, StringComparison.OrdinalIgnoreCase))
            return;

        if (!string.Equals(resourceRegion, MainSceneState.Region, StringComparison.OrdinalIgnoreCase))
        {
            ResetMainThreadResources();
            Volatile.Write(ref resourceRegion, MainSceneState.Region);
            mainThreadDirty.FullRebuild = true;
        }

        if (!mainThreadDirty.IsEmpty)
        {
            RoomResources.ApplyDirty(MainSceneState, mainThreadDirty);
            ConnectionResources.ApplyDirty(
                MainSceneState,
                RoomResources,
                mainThreadDirty);
            SpatialIndex.ApplyDirty(MainSceneState, RoomResources, mainThreadDirty);
            RoomRenderer.ApplyDirty(mainThreadDirty);
            ConnectionRenderer.ApplyDirty(mainThreadDirty);

            foreach (string id in mainThreadDirty.RemovedConnections)
                RouteSpatialIndex.Remove(id);

            if (mainThreadDirty.TopologyChanged || mainThreadDirty.FullRebuild)
                Volatile.Write(ref retainedConnectionsReady, 0);
        }
        mainThreadDirty.Clear();

        if (WorldMapBackgroundSchedulingPolicy.CanRenderRetainedSurface(
                CanvasVisible,
                session?.ToolMode == EditorToolMode.Map,
                snapshot?.Available == true,
                MainSceneState.ViewTransform.CanvasSize.X,
                MainSceneState.ViewTransform.CanvasSize.Y))
        {
            // Promote guard-band rooms ahead of the region-wide initial queue. This changes only
            // processing order; the retained store remains the single owner of room resources.
            Surface.Initialize(log);
            Surface.GetRenderWorldBounds(
                MainSceneState.ViewTransform,
                out Num.Vector2 priorityMin,
                out Num.Vector2 priorityMax);
            SpatialIndex.Query(
                priorityMin,
                priorityMax,
                Volatile.Read(ref activeLayerMask),
                sourcePriorityRooms);
        }
        else
        {
            sourcePriorityRooms.Clear();
        }

        MapRoomGeometryPresentationHub.PrioritizeRooms(sourcePriorityRooms);

        RoomResources.UpdateMainThread(
            session,
            snapshot,
            MainSceneState,
            sourcePriorityRooms);
        RoomResources.DrainGeometryChanges(geometryChangedRooms);
        if (geometryChangedRooms.Count > 0)
        {
            ConnectionResources.InvalidateRooms(
                MainSceneState,
                RoomResources,
                geometryChangedRooms);
            SpatialIndex.InvalidateRooms(
                MainSceneState,
                RoomResources,
                geometryChangedRooms);
        }
        ConnectionResources.Update(MainSceneState, RoomResources);
        PublishLiveCrossingSnapshotIfNeeded();
        ConnectionResources.DrainRouteChanges(routeChanged);
        if (routeChanged.Count > 0)
        {
            for (int i = 0; i < routeChanged.Count; i++)
            {
                string id = routeChanged[i];
                if (ConnectionResources.TryGet(id, out ConnectionRouteResource route))
                    RouteSpatialIndex.Upsert(route);
                else
                    RouteSpatialIndex.Remove(id);
            }
        }

        Volatile.Write(
            ref retainedConnectionsReady,
            ConnectionResources.HasCompleteRoutes(MainSceneState) ? 1 : 0);

        if (WorldMapBackgroundSchedulingPolicy.CanRenderRetainedSurface(
                CanvasVisible,
                session?.ToolMode == EditorToolMode.Map,
                snapshot?.Available == true,
                MainSceneState.ViewTransform.CanvasSize.X,
                MainSceneState.ViewTransform.CanvasSize.Y))
        {
            WorldMapViewTransform view = MainSceneState.ViewTransform;

            int layerMask = Volatile.Read(ref activeLayerMask);
            bool showConnections = Volatile.Read(ref activeShowConnections) != 0;
            int showConnectionsValue = showConnections ? 1 : 0;

            // Pump surface feedback/cleanup every Map main-thread frame. Stable frames do not
            // Camera.Render; they only process short handle swaps and deferred Unity releases.
            Surface.Initialize(log);

            long viewRevision = MainSceneState.ViewRevision;
            long sceneRevision = MainSceneState.SceneRevision;
            long roomResourceRevision = RoomResources.Revision;
            long routeRevision = ConnectionResources.Revision;
            bool viewChanged =
                viewRevision != lastRenderedViewRevision;
            bool nonViewDirty =
                RoomRenderer.HasPendingUploads ||
                ConnectionRenderer.HasPendingUploads ||
                Surface.NeedsRender ||
                sceneRevision != lastRenderedSceneRevision ||
                roomResourceRevision != lastRenderedRoomResourceRevision ||
                routeRevision != lastRenderedRouteRevision ||
                layerMask != lastRenderedLayerMask ||
                showConnectionsValue != lastRenderedShowConnections;
            bool deferViewOnlyRender =
                viewChanged &&
                !nonViewDirty &&
                WorldMapBackgroundBudget.ViewportInteractionActive &&
                Surface.CoversView(view);
            bool needsRender =
                nonViewDirty ||
                (viewChanged && !deferViewOnlyRender);

            if (needsRender)
            {
                Surface.GetRenderWorldBounds(
                    view,
                    out Num.Vector2 visibleMin,
                    out Num.Vector2 visibleMax);

                SpatialIndex.Query(
                    visibleMin,
                    visibleMax,
                    layerMask,
                    visibleRooms);

                if (showConnections)
                {
                    RouteSpatialIndex.Query(
                        visibleMin,
                        visibleMax,
                        visibleRoutes);
                    FilterRoutesByLayer(
                        MainSceneState,
                        layerMask,
                        visibleRoutes);
                }
                else
                {
                    visibleRoutes.Clear();
                }

                if (Surface.Render(
                        view,
                        renderScene =>
                        {
                            RoomRenderer.SynchronizeVisible(
                                MainSceneState,
                                RoomResources,
                                visibleRooms,
                                renderScene);
                            ConnectionRenderer.SynchronizeVisible(
                                ConnectionResources,
                                visibleRoutes,
                                showConnections,
                                renderScene, MainSceneState.ViewTransform);
                        }))
                {
                    lastRenderedViewRevision = viewRevision;
                    lastRenderedSceneRevision = sceneRevision;
                    lastRenderedRoomResourceRevision = roomResourceRevision;
                    lastRenderedRouteRevision = routeRevision;
                    lastRenderedLayerMask = layerMask;
                    lastRenderedShowConnections = showConnectionsValue;
                    PublishPresentedRoutes(
                        showConnections
                            ? visibleRoutes
                            : null,
                        ConnectionRenderer.Revision);
                }
            }
        }

        UpdateViewReadiness();
    }

    internal static bool PreparingSurface => enabled && string.IsNullOrEmpty(Surface.Error);

    internal static bool TryPresentSurface(
        ImDrawListPtr draw,
        Num.Vector2 min,
        Num.Vector2 max) =>
        enabled &&
        string.Equals(RenderSceneState.Region, Volatile.Read(ref resourceRegion), StringComparison.OrdinalIgnoreCase) &&
        Surface.TryPresent(
            draw,
            min,
            max,
            RenderSceneState.ViewTransform);

    internal static bool TryHitRoom(
        Num.Vector2 worldPoint,
        int layerMask,
        out int roomIndex)
    {
        roomIndex = -1;
        return enabled && SpatialIndex.TryHitRoom(worldPoint, layerMask, out roomIndex);
    }

    internal static bool RetainedConnectionsReady =>
        enabled && Volatile.Read(ref retainedConnectionsReady) != 0;

    internal static bool IsConnectionRetainedOnSurface(string connectionId)
    {
        if (!enabled || string.IsNullOrEmpty(connectionId))
            return false;

        PresentedRouteSnapshot snapshot =
            Volatile.Read(ref presentedRoutes);
        return snapshot?.Ids != null && snapshot.Ids.Contains(connectionId) &&
               ConnectionResources.TryGet(connectionId, out var current) &&
               snapshot.SpatialIndex.TryGetPoints(connectionId, out var points) &&
               SameSocketAnchors(current.Points, points);
    }

    private static bool SameSocketAnchors(Num.Vector2[] a, Num.Vector2[] b) =>
        a?.Length >= 2 && b?.Length >= 2 && a[0] == b[0] && a[a.Length - 1] == b[b.Length - 1];

    internal static bool IsConnectionObstructed(string id) => enabled &&
        ConnectionResources.TryGet(id, out var route) && route.Kind == WorldMapOrthogonalRouter.RouteKind.Obstructed;

    internal static int PresentedRouteCount =>
        Volatile.Read(ref presentedRoutes)?.Ids?.Count ?? 0;

    internal static bool TryGetConnectionCrossings(
        string connectionId,
        out WorldMapCrossingMark[] marks)
    {
        marks = null;
        if (!enabled ||
            string.IsNullOrEmpty(connectionId))
            return false;

        // A route that has already been painted to the retained surface must use the crossing
        // grammar captured in the exact same immutable publication. Live crossing rebuilds can
        // finish before the next Camera.Render; exposing them early would put hover/selection
        // bridge arcs on a different route frame from the pixels underneath.
        PresentedRouteSnapshot presented =
            Volatile.Read(ref presentedRoutes);
        if (presented?.Ids?.Contains(connectionId) == true)
        {
            return presented.CrossingsByRoute != null &&
                   presented.CrossingsByRoute.TryGetValue(
                       connectionId,
                       out marks) &&
                   marks != null &&
                   marks.Length > 0;
        }

        if (!Volatile.Read(ref liveCrossingsCurrent))
            return false;

        Dictionary<string, WorldMapCrossingMark[]> live =
            Volatile.Read(ref liveCrossingsByRoute);
        return live != null &&
               live.TryGetValue(
                   connectionId,
                   out marks) &&
               marks != null &&
               marks.Length > 0;
    }

    internal static bool TryGetConnectionRoutePoints(
        string connectionId,
        out Num.Vector2[] points)
    {
        points = null;
        if (!enabled || string.IsNullOrEmpty(connectionId))
            return false;

        if (!ConnectionResources.TryGet(connectionId, out var live)) return false;

        // When the route is already on the retained surface, marker placement must use the exact
        // immutable point set that produced those pixels, not a newer live route waiting for the
        // next surface redraw. Non-presented routes may still use the live spatial index.
        PresentedRouteSnapshot presented =
            Volatile.Read(ref presentedRoutes);
        if (presented?.Ids?.Contains(connectionId) == true &&
            presented.SpatialIndex.TryGetPoints(
                connectionId,
                out points) && SameSocketAnchors(live.Points, points))
        {
            return true;
        }

        return RouteSpatialIndex.TryGetPoints(
            connectionId,
            out points);
    }

    internal static bool TryHitConnection(
        Num.Vector2 worldPoint,
        float worldRadius,
        out string connectionId,
        out float distanceSquared)
    {
        connectionId = string.Empty;
        distanceSquared = worldRadius * worldRadius;
        if (!enabled)
            return false;

        // Hit testing follows the same immutable route snapshot as the pixels currently displayed
        // by the retained surface. This removes the one-frame mismatch where a newly rebuilt live
        // route could be clickable before the offscreen surface had redrawn it.
        PresentedRouteSnapshot presented =
            Volatile.Read(ref presentedRoutes);
        return presented?.Ids != null &&
               presented.Ids.Count > 0 &&
               presented.SpatialIndex.TryHit(
                   worldPoint,
                   worldRadius,
                   allowedIds: null,
                   out connectionId,
                   out distanceSquared);
    }

    private static void PublishLiveCrossingSnapshotIfNeeded()
    {
        if (!ConnectionResources.CrossingsCurrent)
        {
            if (Volatile.Read(ref liveCrossingsCurrent))
            {
                Volatile.Write(
                    ref liveCrossingsByRoute,
                    new Dictionary<string, WorldMapCrossingMark[]>(
                        StringComparer.Ordinal));
                Volatile.Write(
                    ref liveCrossingsCurrent,
                    false);
                liveCrossingRevision =
                    long.MinValue;
            }

            return;
        }

        long revision =
            ConnectionResources.CrossingRevision;
        if (Volatile.Read(ref liveCrossingsCurrent) &&
            revision == liveCrossingRevision)
            return;

        liveCrossingRevision = revision;
        Volatile.Write(
            ref liveCrossingsByRoute,
            BuildCrossingSnapshot(
                allowedRouteIds: null));
        Volatile.Write(
            ref liveCrossingsCurrent,
            true);
    }

    private static Dictionary<string, WorldMapCrossingMark[]> BuildCrossingSnapshot(
        HashSet<string> allowedRouteIds)
    {
        Dictionary<string, List<WorldMapCrossingMark>> staging =
            new(StringComparer.Ordinal);

        if (ConnectionResources.CrossingsCurrent)
        {
            for (int i = 0;
                 i < ConnectionResources.Crossings.Count;
                 i++)
            {
                WorldMapCrossingMark mark =
                    ConnectionResources.Crossings[i];

                bool overAllowed =
                    allowedRouteIds == null ||
                    allowedRouteIds.Contains(
                        mark.OverRouteId);
                bool underAllowed =
                    allowedRouteIds == null ||
                    allowedRouteIds.Contains(
                        mark.UnderRouteId);

                if (overAllowed)
                {
                    AddCrossingToSnapshot(
                        staging,
                        mark.OverRouteId,
                        mark);
                }

                if (underAllowed &&
                    !string.Equals(
                        mark.UnderRouteId,
                        mark.OverRouteId,
                        StringComparison.Ordinal))
                {
                    AddCrossingToSnapshot(
                        staging,
                        mark.UnderRouteId,
                        mark);
                }
            }
        }

        Dictionary<string, WorldMapCrossingMark[]> next =
            new(StringComparer.Ordinal);

        foreach (KeyValuePair<string, List<WorldMapCrossingMark>> pair
                 in staging)
        {
            next[pair.Key] =
                pair.Value.ToArray();
        }

        return next;
    }

    private static void AddCrossingToSnapshot(
        Dictionary<string, List<WorldMapCrossingMark>> staging,
        string routeId,
        WorldMapCrossingMark mark)
    {
        if (string.IsNullOrEmpty(routeId))
            return;

        if (!staging.TryGetValue(
                routeId,
                out List<WorldMapCrossingMark> marks))
        {
            marks =
                new List<WorldMapCrossingMark>();
            staging.Add(
                routeId,
                marks);
        }

        marks.Add(mark);
    }

    private static void FilterRoutesByLayer(
        WorldMapScene scene,
        int layerMask,
        List<string> routeIds)
    {
        if (scene == null ||
            routeIds == null ||
            routeIds.Count == 0)
            return;

        for (int i = routeIds.Count - 1;
             i >= 0;
             i--)
        {
            string id = routeIds[i];
            if (!scene.TryGetConnection(
                    id,
                    out WorldMapScene.ConnectionNode connection) ||
                !scene.TryGetRoom(
                    connection.FromRoomIndex,
                    out WorldMapScene.RoomNode fromRoom) ||
                !scene.TryGetRoom(
                    connection.ToRoomIndex,
                    out WorldMapScene.RoomNode toRoom) ||
                !LayerVisible(
                    fromRoom.Layer,
                    layerMask) ||
                !LayerVisible(
                    toRoom.Layer,
                    layerMask))
            {
                routeIds.RemoveAt(i);
            }
        }
    }

    private static bool LayerVisible(
        int layer,
        int layerMask)
    {
        // WorldMapView exposes exactly L1/L2/L3. Keep its fallback semantics for any extension or
        // malformed layer value: values outside the three UI layers remain visible.
        if (layer < 0 ||
            layer >= 3)
            return true;

        return (layerMask &
                (1 << layer)) != 0;
    }

    private static void PublishPresentedRoutes(
        IReadOnlyList<string> routeIds,
        long routeRevision)
    {
        PresentedRouteSnapshot current =
            Volatile.Read(ref presentedRoutes);
        if (!ConnectionRenderer.HasPendingUploads &&
            routeRevision == presentedRouteRevision &&
            PresentedRouteSetMatches(
                current?.Ids,
                routeIds))
        {
            return;
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        WorldMapRouteSpatialIndex index = new();

        if (routeIds != null)
        {
            for (int i = 0; i < routeIds.Count; i++)
            {
                string id = routeIds[i];
                if (string.IsNullOrEmpty(id) ||
                    !ConnectionRenderer.TryGetPresentedRoute(
                        id,
                        out ConnectionRouteResource route) ||
                    route?.Points == null ||
                    route.Points.Length < 2)
                {
                    continue;
                }

                // Surface publication is a frame boundary. Route point arrays are immutable
                // after publication (updates replace the array rather than mutating it), so keeping
                // this exact reference preserves the rendered frame without cloning every visible
                // path on each pan/zoom redraw.
                index.Upsert(
                    id,
                    route.Points);
                ids.Add(id);
            }
        }

        presentedRouteRevision = routeRevision;
        Dictionary<string, WorldMapCrossingMark[]> crossings =
            ConnectionRenderer.HasPendingUploads
                ? new Dictionary<string, WorldMapCrossingMark[]>(StringComparer.Ordinal)
                : BuildCrossingSnapshot(ids);
        Volatile.Write(
            ref presentedRoutes,
            new PresentedRouteSnapshot(
                ids,
                index,
                crossings));
    }

    private static bool PresentedRouteSetMatches(
        HashSet<string> current,
        IReadOnlyList<string> routeIds)
    {
        int expected = 0;
        if (routeIds != null)
        {
            for (int i = 0; i < routeIds.Count; i++)
                if (!string.IsNullOrEmpty(routeIds[i]))
                    expected++;
        }

        if (current == null ||
            current.Count != expected)
            return false;

        if (routeIds == null)
            return current.Count == 0;

        for (int i = 0; i < routeIds.Count; i++)
        {
            string id = routeIds[i];
            if (!string.IsNullOrEmpty(id) &&
                !current.Contains(id))
                return false;
        }

        return true;
    }

    internal static bool QueryRooms(
        Num.Vector2 worldMin,
        Num.Vector2 worldMax,
        int layerMask,
        List<int> output) =>
        enabled && SpatialIndex.Query(worldMin, worldMax, layerMask, output);

    internal static bool QueryVisibleRooms(int layerMask, List<int> output)
    {
        if (!enabled || output == null) return false;
        RenderSceneState.ViewTransform.GetVisibleWorldBounds(
            out Num.Vector2 min,
            out Num.Vector2 max);
        return SpatialIndex.Query(min, max, layerMask, output);
    }

    internal static void ResetRetainedState()
    {
        // Every CPU scene and GPU resource owner needs its own teardown
        // attempt. A failing Synchronizer.Reset must not skip GPU release.
        try
        {
            RetainedCleanupBatch.Run(
                new RetainedCleanupBatch.Step("synchronizer", Synchronizer.Reset),
                new RetainedCleanupBatch.Step("render scene", () => RenderSceneState.Reset()),
                new RetainedCleanupBatch.Step("main-thread scene", () => MainSceneState.Reset()),
                new RetainedCleanupBatch.Step("scene transfer mailbox", SceneTransfer.Clear),
                new RetainedCleanupBatch.Step("GPU and room resources", ResetMainThreadResources));
        }
        finally
        {
            // This presentation state must not remain visible even if one
            // retained GPU resource failed to release.
            mainThreadDirty.Clear();
            Interlocked.Exchange(ref canvasSeenAt, 0L);
            Volatile.Write(ref activeZoom, 1f);
            readinessRegion = string.Empty;
            readinessStartedTicks = 0L;
            firstSurfaceReadyTicks = 0L;
            readinessCompletedTicks = 0L;
            readinessExpectedRooms = 0;
            readinessExpectedConnections = 0;
            readinessComplete = false;
            lastDirty = new WorldMapDirtySet();
        }
    }

    private static void ResetMainThreadResources()
    {
        Volatile.Write(ref resourceRegion, string.Empty);
        List<Exception> errors = new();
        void ReleaseResource(string name, Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error)
            {
                errors.Add(new InvalidOperationException(
                    "Retained World Map resource cleanup failed for " + name, error));
                try { log?.LogWarning("Retained World Map cleanup failed for " + name + ": " + error); }
                catch { /* Reporting failures cannot prevent other resources being released. */ }
            }
        }

        // Always attempt every resource: RoomResources may throw without
        // releasing the render texture, and texture cleanup may throw without
        // clearing the route and thumbnail references.
        ReleaseResource("room resources", RoomResources.Reset);
        ReleaseResource("legacy room source", WorldMapLegacyRoomSourceService.Reset);
        ReleaseResource("connection resources", ConnectionResources.Reset);
        ReleaseResource("room spatial index", SpatialIndex.Reset);
        ReleaseResource("route spatial index", RouteSpatialIndex.Reset);
        ReleaseResource("room renderer", RoomRenderer.Reset);
        ReleaseResource("connection renderer", ConnectionRenderer.Reset);
        ReleaseResource("render texture surface", Surface.Reset);
        geometryChangedRooms.Clear();
        routeChanged.Clear();
        sourcePriorityRooms.Clear();
        ReleaseResource("thumbnail priorities",
            () => MapRoomGeometryPresentationHub.PrioritizeRooms(null));
        visibleRooms.Clear();
        visibleRoutes.Clear();
        presentedRouteRevision = long.MinValue;
        Volatile.Write(ref presentedRoutes, PresentedRouteSnapshot.Empty());
        Volatile.Write(ref liveCrossingsByRoute,
            new Dictionary<string, WorldMapCrossingMark[]>(StringComparer.Ordinal));
        Volatile.Write(ref liveCrossingsCurrent, false);
        liveCrossingRevision = long.MinValue;
        Volatile.Write(ref retainedConnectionsReady, 0);
        lastRenderedViewRevision = long.MinValue;
        lastRenderedSceneRevision = long.MinValue;
        lastRenderedRoomResourceRevision = long.MinValue;
        lastRenderedRouteRevision = long.MinValue;
        lastRenderedLayerMask = int.MinValue;
        lastRenderedShowConnections = int.MinValue;

        if (errors.Count > 0)
            throw new AggregateException(
                "Retained World Map resources did not all release.", errors);
    }

    private static void TrackViewReadinessStart(
        EditorMapPresentationSnapshot snapshot,
        long now)
    {
        string nextRegion =
            snapshot?.RegionName ?? string.Empty;
        int roomCount =
            snapshot?.Rooms?.Length ?? 0;
        int connectionCount =
            snapshot?.Connections?.Length ?? 0;

        bool newRegion =
            readinessStartedTicks <= 0L ||
            !string.Equals(
                readinessRegion,
                nextRegion,
                StringComparison.OrdinalIgnoreCase);

        if (newRegion)
        {
            readinessRegion = nextRegion;
            readinessStartedTicks = now;
            firstSurfaceReadyTicks = 0L;
            readinessCompletedTicks = 0L;
            readinessExpectedRooms = roomCount;
            readinessExpectedConnections = connectionCount;
            readinessComplete = false;
            return;
        }

        if (roomCount > readinessExpectedRooms ||
            connectionCount > readinessExpectedConnections)
        {
            readinessExpectedRooms =
                Math.Max(readinessExpectedRooms, roomCount);
            readinessExpectedConnections =
                Math.Max(readinessExpectedConnections, connectionCount);

            if (readinessComplete)
            {
                readinessComplete = false;
                readinessCompletedTicks = 0L;
            }
        }
    }

    private static void UpdateViewReadiness()
    {
        if (readinessStartedTicks <= 0L)
            return;

        long now = Stopwatch.GetTimestamp();
        if (firstSurfaceReadyTicks <= 0L &&
            Surface.Ready)
        {
            firstSurfaceReadyTicks = now;
        }

        if (readinessComplete)
            return;

        bool roomsReady =
            readinessExpectedRooms <= 0 ||
            (RoomResources.ThumbnailLoadComplete &&
             RoomResources.ThumbnailLoadCommitted >= readinessExpectedRooms);
        bool routesReady =
            readinessExpectedConnections <= 0 ||
            (ConnectionResources.RouteSessionComplete &&
             ConnectionResources.Count >= readinessExpectedConnections);

        if (!roomsReady ||
            !routesReady ||
            !Surface.Ready)
            return;

        readinessComplete = true;
        readinessCompletedTicks = now;

        log?.LogInfo(
            "WorldMap view ready: first surface " +
            FirstSurfaceReadyMilliseconds.ToString("F0") +
            " ms, full retained readiness " +
            ViewReadinessMilliseconds.ToString("F0") +
            " ms, rooms " +
            RoomResources.ThumbnailLoadCommitted + "/" +
            readinessExpectedRooms +
            ", routes " +
            ConnectionResources.Count + "/" +
            readinessExpectedConnections + ".");

        LogReadinessProfile();
    }

    private static void LogReadinessProfile()
    {
        string startupMode =
            WorldMapPersistentRetainedCache.RestoreSnapshotLoaded
                ? "warm-cache"
                : "cold/no-cache";

        log?.LogInfo(
            "WorldMap profile | mode=" + startupMode +
            " | firstSurface=" +
            FirstSurfaceReadyMilliseconds.ToString("F0") + "ms" +
            " | fullReady=" +
            ViewReadinessMilliseconds.ToString("F0") + "ms" +
            " | sourceRecovery=" +
            MapRoomGeometryPresentationHub.SourceRecoverySessionElapsedMilliseconds.ToString("F0") + "ms" +
            " | retainedThumbs=" +
            RoomResources.ThumbnailLoadElapsedMilliseconds.ToString("F0") + "ms" +
            " | routes=" +
            ConnectionResources.RouteSessionElapsedMilliseconds.ToString("F0") + "ms" +
            " | persistentThumbHits=" +
            RoomResources.ThumbnailPersistentHits +
            " | persistentRouteHits=" +
            WorldMapPersistentRetainedCache.RestoredRouteCount +
            " | rasterReadbacks=" +
            MapRoomGeometryPresentationHub.RasterReadbackCount +
            " avg=" +
            MapRoomGeometryPresentationHub.RasterReadbackAverageMilliseconds.ToString("F2") + "ms" +
            " peak=" +
            MapRoomGeometryPresentationHub.RasterReadbackPeakMilliseconds.ToString("F2") + "ms" +
            " | curveLoads=" +
            MapRoomGeometryPresentationHub.CurveLoadCount +
            " avg=" +
            MapRoomGeometryPresentationHub.CurveLoadAverageMilliseconds.ToString("F2") + "ms" +
            " peak=" +
            MapRoomGeometryPresentationHub.CurveLoadPeakMilliseconds.ToString("F2") + "ms" +
            " | retainedPumpAvg=" +
            RoomResources.MainThreadAverageMilliseconds.ToString("F2") + "ms" +
            " peak=" +
            RoomResources.MainThreadPeakMilliseconds.ToString("F2") + "ms" +
            " | surfaceRenders=" +
            Surface.RenderCount +
            " avg=" +
            Surface.AverageRenderMilliseconds.ToString("F2") + "ms" +
            " peak=" +
            Surface.PeakRenderMilliseconds.ToString("F2") + "ms");
    }

    private static double ElapsedMilliseconds(
        long start,
        long end)
    {
        if (start <= 0L ||
            end <= 0L ||
            end < start)
            return 0d;

        return (end - start) *
               1000d /
               Stopwatch.Frequency;
    }



}
