using System;

namespace DryCycle.DevUI.DevTool.Objects;

/// <summary>
/// Detached scene-space gizmo primitives for the selected object. The frontend consumes only world
/// coordinates, stable handle IDs and cubic Bezier segments; it never knows PlacedObject/Data types.
/// </summary>
public sealed class EditorObjectGizmoHandleSnapshot
{
    public string Id { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float AnchorX { get; init; }
    public float AnchorY { get; init; }
    public bool DrawAnchorLine { get; init; }
    public bool Removable { get; init; }
}

public sealed class EditorObjectLineSegmentSnapshot
{
    public bool HasColor { get; init; }
    public float Red { get; init; }
    public float Green { get; init; }
    public float Blue { get; init; }
    public float Alpha { get; init; } = 1f;
    public float Thickness { get; init; } = 1.2f;
    public float X0 { get; init; }
    public float Y0 { get; init; }
    public float X1 { get; init; }
    public float Y1 { get; init; }
}

public sealed class EditorObjectCircleSnapshot
{
    public float CenterX { get; init; }
    public float CenterY { get; init; }
    public float Radius { get; init; }
}

public sealed class EditorObjectBezierSegmentSnapshot
{
    public int SegmentIndex { get; init; }
    public float X0 { get; init; }
    public float Y0 { get; init; }
    public float C0X { get; init; }
    public float C0Y { get; init; }
    public float X1 { get; init; }
    public float Y1 { get; init; }
    public float C1X { get; init; }
    public float C1Y { get; init; }
}

public sealed class EditorObjectGizmoSnapshot
{
    public static readonly EditorObjectGizmoSnapshot Empty = new();

    public int ObjectIndex { get; init; } = -1;
    public long ObjectStableId { get; init; }
    public EditorObjectGizmoHandleSnapshot[] Handles { get; init; } = Array.Empty<EditorObjectGizmoHandleSnapshot>();
    public EditorObjectLineSegmentSnapshot[] Lines { get; init; } = Array.Empty<EditorObjectLineSegmentSnapshot>();
    public EditorObjectCircleSnapshot[] Circles { get; init; } = Array.Empty<EditorObjectCircleSnapshot>();
    public EditorObjectBezierSegmentSnapshot[] BezierSegments { get; init; } = Array.Empty<EditorObjectBezierSegmentSnapshot>();
}

