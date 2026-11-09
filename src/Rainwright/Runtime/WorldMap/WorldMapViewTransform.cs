using System;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Pure view state for the retained World Map. Scene geometry stays in map-world coordinates;
/// pan/zoom/canvas changes are represented only here and must never dirty retained room/route data.
/// </summary>
internal readonly struct WorldMapViewTransform : IEquatable<WorldMapViewTransform>
{
    internal WorldMapViewTransform(Num.Vector2 canvasOrigin, Num.Vector2 canvasSize, Num.Vector2 pan, float zoom)
        : this(canvasOrigin, canvasSize, pan, zoom, false, -1) { }

    internal WorldMapViewTransform(
        Num.Vector2 canvasOrigin,
        Num.Vector2 canvasSize,
        Num.Vector2 pan,
        float zoom, bool depthEnabled, int focusLayer = -1, MapPlaneFrame planeFrame = default)
    {
        CanvasOrigin = canvasOrigin;
        CanvasSize = new Num.Vector2(
            Math.Max(0f, canvasSize.X),
            Math.Max(0f, canvasSize.Y));
        Pan = pan;
        Zoom = Math.Max(0.0001f, zoom);
        DepthEnabled = depthEnabled;
        FocusLayer = focusLayer;
        PlaneFrame = planeFrame;
    }

    internal Num.Vector2 CanvasOrigin { get; }
    internal Num.Vector2 CanvasSize { get; }
    internal Num.Vector2 Pan { get; }
    internal float Zoom { get; }
    internal bool DepthEnabled { get; }
    internal int FocusLayer { get; }
    internal MapPlaneFrame PlaneFrame { get; }

    internal Num.Vector2 WorldToCanvas(Num.Vector2 world) =>
        CanvasOrigin + Pan + world * Zoom;

    internal Num.Vector2 CanvasToWorld(Num.Vector2 canvas) =>
        (canvas - CanvasOrigin - Pan) / Zoom;

    internal Num.Vector2 VisibleWorldMin =>
        CanvasToWorld(CanvasOrigin);

    internal Num.Vector2 VisibleWorldMax =>
        CanvasToWorld(CanvasOrigin + CanvasSize);

    internal void GetVisibleWorldBounds(out Num.Vector2 min, out Num.Vector2 max)
    {
        Num.Vector2 a = VisibleWorldMin;
        Num.Vector2 b = VisibleWorldMax;
        min = Num.Vector2.Min(a, b);
        max = Num.Vector2.Max(a, b);
        MapDepthProjection.VisibleBounds(min, max, DepthEnabled, PlaneFrame, out min, out max);
    }

    public bool Equals(WorldMapViewTransform other) =>
        Approximately(CanvasOrigin, other.CanvasOrigin) &&
        Approximately(CanvasSize, other.CanvasSize) &&
        Approximately(Pan, other.Pan) &&
        DepthEnabled == other.DepthEnabled && FocusLayer == other.FocusLayer &&
        PlaneFrame.Equals(other.PlaneFrame) &&
        Math.Abs(Zoom - other.Zoom) <= 0.0001f;

    public override bool Equals(object obj) =>
        obj is WorldMapViewTransform other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = CanvasOrigin.GetHashCode();
            hash = hash * 397 ^ CanvasSize.GetHashCode();
            hash = hash * 397 ^ Pan.GetHashCode();
            hash = hash * 397 ^ DepthEnabled.GetHashCode();
            hash = hash * 397 ^ FocusLayer;
            hash = hash * 397 ^ PlaneFrame.GetHashCode();
            return hash * 397 ^ Zoom.GetHashCode();
        }
    }

    private static bool Approximately(Num.Vector2 a, Num.Vector2 b) =>
        Num.Vector2.DistanceSquared(a, b) <= 0.0001f;
}
