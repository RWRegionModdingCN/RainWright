using System;

namespace DryCycle.DevUI.DevTool.Compatibility;

/// <summary>
/// Detached mirror of the currently active DevInterface page. The frontend consumes only this
/// semantic model; it never needs to reference a RegionKit/DryCycle/custom control type directly.
/// </summary>
public sealed class UniversalDevUiPresentationSnapshot
{
    public static readonly UniversalDevUiPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string PageType { get; init; } = string.Empty;
    public string PageId { get; init; } = string.Empty;
    public LegacyControlSnapshot[] Controls { get; init; } = Array.Empty<LegacyControlSnapshot>();
    public int UnmappedProtocolCount { get; init; }
}

