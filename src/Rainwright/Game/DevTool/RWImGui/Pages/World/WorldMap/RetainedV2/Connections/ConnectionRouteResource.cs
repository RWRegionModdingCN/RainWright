using System;
using DryCycle.DevUI.DevTool.World;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Retained world-space route for one map connection. Points never depend on canvas pan or zoom.
/// </summary>
internal sealed class ConnectionRouteResource
{
    internal string ConnectionId = string.Empty;
    internal int FromRoomIndex;
    internal int ToRoomIndex;
    internal int FromLayer = 1, ToLayer = 1;
    internal Exception RoutingFailure;
    internal WorldConnectionDirection Direction;
    internal bool Ambiguous;
    internal WorldMapOrthogonalRouter.RouteKind Kind;

    // BasePoints is the immutable single-route result from the orthogonal router. Points is the
    // retained presentation path after global corridor-lane derivation. Keeping both prevents lane
    // offsets from accumulating across incremental rebuilds or persistent-cache restores.
    internal Num.Vector2[] BasePoints = Array.Empty<Num.Vector2>();
    internal Num.Vector2[] Points = Array.Empty<Num.Vector2>();

    internal Num.Vector2 StartDirection;
    internal Num.Vector2 EndDirection;

    // Derived presentation density only. 0 = normal, 1 = dense, 2 = extreme.
    // BaseDensityTier comes from endpoint crowding; DensityTier is the final max after corridor
    // analysis. Neither value is authoring/persistent data.
    internal byte BaseDensityTier;
    internal byte DensityTier;

    internal long Revision;

    internal ConnectionRouteResource CloneForWorker()
    {
        ConnectionRouteResource copy = (ConnectionRouteResource)MemberwiseClone();
        copy.BasePoints = (Num.Vector2[])BasePoints.Clone();
        copy.Points = (Num.Vector2[])Points.Clone();
        return copy;
    }

    internal bool HasSameSource(ConnectionRouteResource other)
    {
        if (other == null || ConnectionId != other.ConnectionId ||
            FromRoomIndex != other.FromRoomIndex || ToRoomIndex != other.ToRoomIndex ||
            FromLayer != other.FromLayer || ToLayer != other.ToLayer ||
            Direction != other.Direction || Ambiguous != other.Ambiguous || Kind != other.Kind ||
            StartDirection != other.StartDirection || EndDirection != other.EndDirection ||
            BasePoints.Length != other.BasePoints.Length)
            return false;
        for (int i = 0; i < BasePoints.Length; i++)
            if (BasePoints[i] != other.BasePoints[i]) return false;
        return true;
    }
}
