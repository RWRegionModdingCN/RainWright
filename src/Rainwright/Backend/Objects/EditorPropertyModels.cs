using System;

namespace DryCycle.DevUI.DevTool.Objects;

// Detached object inspector property types are shared with native UI and
// editor commands, and must not be emitted by the gameplay assembly.
public enum EditorPropertyKind
{
    ReadOnly,
    Float,
    Integer,
    Boolean,
    String,
    Vector2,
    Color,
    Enum,
    Action
}

public enum EditorPropertyGizmoHint
{
    None,
    RelativePoint,
    VerticalDistance
}

public enum EditorPropertyGizmoShape
{
    None,
    Line,
    Circle,
    Rectangle,
    Tile,
    Direction4,
    Direction8
}

public readonly struct ObjectInspectorCoverage
{
    public ObjectInspectorCoverage(
        bool inspectorComplete,
        bool gizmoComplete,
        int supportedFields = 0,
        int unsupportedFields = 0)
    {
        InspectorComplete = inspectorComplete;
        GizmoComplete = gizmoComplete;
        SupportedFields = Math.Max(0, supportedFields);
        UnsupportedFields = Math.Max(0, unsupportedFields);
    }

    public bool InspectorComplete { get; }
    public bool GizmoComplete { get; }
    public int SupportedFields { get; }
    public int UnsupportedFields { get; }
    public bool IsComplete => InspectorComplete && GizmoComplete;

    public static ObjectInspectorCoverage Complete(int fields = 0) =>
        new(true, true, fields, 0);

    public static ObjectInspectorCoverage Conservative(int supportedFields = 0, int unsupportedFields = 1) =>
        new(false, false, supportedFields, Math.Max(1, unsupportedFields));
}

/// <summary>
/// Detached property description consumed by the optional ImGui frontend. It contains
/// only scalar/string data so the render callback never dereferences Rain World objects.
/// </summary>
public sealed class EditorPropertySnapshot
{
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Group { get; init; } = "Properties";
    public string Source { get; init; } = string.Empty;
    public EditorPropertyKind Kind { get; init; }
    public EditorPropertyGizmoHint GizmoHint { get; init; }
    public EditorPropertyGizmoShape GizmoShape { get; init; }
    public float GizmoScale { get; init; } = 1f;
    public string SerializedValue { get; init; } = string.Empty;
    public string StringValue { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float W { get; init; }
    public int IntegerValue { get; init; }
    public bool BooleanValue { get; init; }
    public float Min { get; init; }
    public float Max { get; init; }
    public float Step { get; init; }
    public bool HasRange { get; init; }
    public string[] Options { get; init; } = Array.Empty<string>();
}

public readonly struct EditorPropertyValue
{
    public EditorPropertyValue(
        EditorPropertyKind kind,
        string text = null,
        float x = 0f,
        float y = 0f,
        float z = 0f,
        float w = 0f,
        int integer = 0,
        bool boolean = false)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        X = x;
        Y = y;
        Z = z;
        W = w;
        Integer = integer;
        Boolean = boolean;
    }

    public EditorPropertyKind Kind { get; }
    public string Text { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }
    public int Integer { get; }
    public bool Boolean { get; }
}

