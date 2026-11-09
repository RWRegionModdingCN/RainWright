using System;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

// Detached viewport bounds, retained for exact mailbox round trips. Layer presentation never
// transforms these bounds or the author's coordinates.
internal readonly struct MapPlaneFrame : IEquatable<MapPlaneFrame>
{
    internal MapPlaneFrame(Num.Vector2 min, Num.Vector2 max)
    {
        Center = (min + max) * .5f;
        HalfSize = Num.Vector2.Max((max - min) * .5f, new Num.Vector2(128f));
    }
    // Mailbox deserialization copies exact components. Reconstructing min/max and computing
    // the centre a second time can lose float bits and reject every rendered surface as stale.
    internal MapPlaneFrame(float centerX, float centerY, float halfX, float halfY)
    { Center = new Num.Vector2(centerX, centerY); HalfSize = new Num.Vector2(halfX, halfY); }
    internal Num.Vector2 Center { get; }
    internal Num.Vector2 HalfSize { get; }
    internal Num.Vector2 Extent => HalfSize.Y > 0f ? HalfSize : new Num.Vector2(512f);
    internal Num.Vector2 Min => Center - Extent;
    internal Num.Vector2 Max => Center + Extent;
    public bool Equals(MapPlaneFrame other) => Center == other.Center && HalfSize == other.HalfSize;
    public override bool Equals(object obj) => obj is MapPlaneFrame other && Equals(other);
    public override int GetHashCode() => unchecked(Center.GetHashCode() * 397 ^ HalfSize.GetHashCode());
}

internal static class MapDepthProjection
{
    // One shared 2D canvas. The existing presentation entry points intentionally preserve
    // coordinates for rendering, sockets, picking, group dragging and spatial route queries.
    internal static Num.Vector2 Project(Num.Vector2 world, int layer, bool enabled, MapPlaneFrame frame = default) => world;

    internal static bool TryUnproject(Num.Vector2 view, int layer, bool enabled, MapPlaneFrame frame, out Num.Vector2 world)
    {
        world = view;
        return !float.IsNaN(world.X) && !float.IsInfinity(world.X) &&
            !float.IsNaN(world.Y) && !float.IsInfinity(world.Y);
    }

    internal static Num.Vector2 Unproject(Num.Vector2 view, int layer, bool enabled, MapPlaneFrame frame = default) =>
        TryUnproject(view, layer, enabled, frame, out var point) ? point : frame.Min;

    internal static void Corners(Num.Vector2 min, Num.Vector2 max, int layer, bool enabled, MapPlaneFrame frame,
        out Num.Vector2 a, out Num.Vector2 b, out Num.Vector2 c, out Num.Vector2 d)
    {
        a = Project(min, layer, enabled, frame);
        b = Project(new Num.Vector2(max.X, min.Y), layer, enabled, frame);
        c = Project(max, layer, enabled, frame);
        d = Project(new Num.Vector2(min.X, max.Y), layer, enabled, frame);
    }

    internal static void Bounds(Num.Vector2 min, Num.Vector2 max, int layer, bool enabled, MapPlaneFrame frame,
        out Num.Vector2 projectedMin, out Num.Vector2 projectedMax)
    {
        projectedMin = min;
        projectedMax = max;
    }

    internal static void VisibleBounds(Num.Vector2 min, Num.Vector2 max, bool enabled, MapPlaneFrame frame,
        out Num.Vector2 worldMin, out Num.Vector2 worldMax)
    {
        worldMin = min; worldMax = max;
    }

    // Layers use presentation colors, never opacity. Keep legacy render descriptors harmless
    // while the frontend and retained renderer exchange a previously enabled depth flag.
    internal static float Opacity(int layer, bool enabled, int focus = -1) => 1f;
    internal static uint Fade(uint color, float opacity) =>
        (color & 0x00FFFFFF) | ((uint)Math.Round((color >> 24) * opacity) << 24);

    internal static Num.Vector2[] ProjectRoute(Num.Vector2[] points, int from, int to, bool enabled, MapPlaneFrame frame = default)
        => points;
}
