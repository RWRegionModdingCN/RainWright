using System;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Small semantic key used by a page to describe its Control Center session status.
/// The shared base class caches the formatted text against this key plus language so stable frames
/// do not rebuild status strings just because the Control Center is visible.
/// </summary>
internal readonly struct DevToolPageStatusState : IEquatable<DevToolPageStatusState>
{
    internal DevToolPageStatusState(
        int countA = 0,
        int countB = 0,
        bool flag = false,
        string textA = null,
        string textB = null)
    {
        CountA = countA;
        CountB = countB;
        Flag = flag;
        TextA = textA ?? string.Empty;
        TextB = textB ?? string.Empty;
    }

    internal int CountA { get; }
    internal int CountB { get; }
    internal bool Flag { get; }
    internal string TextA { get; }
    internal string TextB { get; }

    public bool Equals(DevToolPageStatusState other) =>
        CountA == other.CountA &&
        CountB == other.CountB &&
        Flag == other.Flag &&
        string.Equals(TextA, other.TextA, StringComparison.Ordinal) &&
        string.Equals(TextB, other.TextB, StringComparison.Ordinal);

    public override bool Equals(object obj) =>
        obj is DevToolPageStatusState other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = CountA;
            hash = hash * 397 ^ CountB;
            hash = hash * 397 ^ (Flag ? 1 : 0);
            hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(TextA);
            hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(TextB);
            return hash;
        }
    }
}

