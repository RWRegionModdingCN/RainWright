using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Game/frontend adapter only. The retained geometry index, thread-safe
/// lookup and hit-testing implementation live in RainWright.dll.
/// </summary>
internal sealed class WorldMapRouteSpatialIndex : WorldMapRouteSpatialCore
{
    internal void Upsert(ConnectionRouteResource route)
    {
        if (route == null) return;
        Upsert(route.ConnectionId, route.Points);
    }
}
