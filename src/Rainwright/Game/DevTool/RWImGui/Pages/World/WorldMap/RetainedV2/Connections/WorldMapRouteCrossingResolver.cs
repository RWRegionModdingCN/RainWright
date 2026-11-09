using System.Collections.Generic;
using System.Threading;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// The game/frontend knows how to read a retained connection resource;
/// route crossing detection, stable LOD and candidate budgets live only in
/// RainWright.dll. The static lambda avoids per-rebuild allocations.
/// </summary>
internal static class WorldMapRouteCrossingResolver
{
    internal static WorldMapCrossingResolveResult Build(
        IReadOnlyDictionary<string, ConnectionRouteResource> routes, CancellationToken cancellation = default)
    {
        if (routes == null) return WorldMapCrossingResolveResult.Empty;
        var marks = new List<WorldMapCrossingMark>();
        bool limited = false;
        int checks = 0;
        for (int layer = 0; layer < 3; layer++)
        {
            cancellation.ThrowIfCancellationRequested();
            var planar = new Dictionary<string, ConnectionRouteResource>();
            foreach (var pair in routes)
                if (pair.Value.FromLayer == layer && pair.Value.ToLayer == layer &&
                    pair.Value.Kind != WorldMapOrthogonalRouter.RouteKind.Obstructed)
                    planar.Add(pair.Key, pair.Value);
            if (planar.Count < 2) continue;
            var result = WorldMapRouteCrossingCore.Build(planar, static route => route?.Points, cancellation);
            marks.AddRange(result.Marks); limited |= result.BudgetLimited; checks += result.CandidateChecks;
        }
        return new WorldMapCrossingResolveResult(marks.ToArray(), limited, checks);
    }
}
