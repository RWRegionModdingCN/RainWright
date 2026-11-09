using System;

namespace DryCycle.DevUI.DevTool.Compatibility;

public enum LegacyControlKind
{
    Button,
    Boolean,
    Slider,
    Cycler,
    ExtEnum,
    Integer,
    Select,
    PanelSelect,
    Text,
    Direction,
    Color,
    ReadOnly
}

/// <summary>
/// Detached semantic description of a legacy DevInterface control.
///
/// The bridge intentionally describes capabilities instead of concrete mod control classes.
/// Vanilla, RegionKit, DryCycle and third-party controls are handled by the same rules whenever
/// they expose the same DevInterface/base-class/reflection contract.
/// </summary>
public sealed class LegacyControlSnapshot
{
    public string Path { get; init; } = string.Empty;
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string RuntimeType { get; init; } = string.Empty;
    public string PanelPath { get; init; } = string.Empty;
    public string PanelTitle { get; init; } = string.Empty;
    public LegacyControlKind Kind { get; init; }
    public string ValueText { get; init; } = string.Empty;
    public float Factor { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public float W { get; init; }
    public bool BooleanValue { get; init; }
    public bool CanReset { get; init; }
    public int SelectedIndex { get; init; } = -1;
    public string[] Options { get; init; } = Array.Empty<string>();
}

