using System;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>Dash intervals on one segment, with phase carried across path corners.</summary>
internal struct WorldMapDashSegmentEnumerator
{
    internal const int MaxPatternCycles = 8192;

    internal static double ScaleForLength(double length, double dashLength, double gap)
    {
        // This is presentation LOD, not a truncated path: preserve the dash/gap
        // ratio and both endpoints while bounding repeated geometry on huge routes.
        if (double.IsNaN(length) || double.IsInfinity(length) || length < 0d)
            throw new ArgumentOutOfRangeException(nameof(length));
        double cycle = dashLength + gap;
        if (dashLength <= 0d || gap < 0d || double.IsNaN(cycle) || double.IsInfinity(cycle))
            throw new ArgumentOutOfRangeException(nameof(dashLength));
        return Math.Max(1d, length / cycle / MaxPatternCycles);
    }

    private readonly double length;
    private readonly double dashLength;
    private readonly double cycle;
    private readonly double firstStart;
    private readonly int count;
    private int index;

    internal double Start { get; private set; }
    internal double End { get; private set; }
    internal double NextPhase { get; }

    internal WorldMapDashSegmentEnumerator(double length, double phase, double dashLength, double gap)
    {
        if (double.IsNaN(length) || double.IsInfinity(length) || length < 0d)
            throw new ArgumentOutOfRangeException(nameof(length));
        if (double.IsNaN(phase) || double.IsInfinity(phase))
            throw new ArgumentOutOfRangeException(nameof(phase));
        if (double.IsNaN(dashLength) || double.IsInfinity(dashLength) || dashLength <= 0d)
            throw new ArgumentOutOfRangeException(nameof(dashLength));
        if (double.IsNaN(gap) || double.IsInfinity(gap) || gap < 0d)
            throw new ArgumentOutOfRangeException(nameof(gap));

        this.length = length;
        this.dashLength = dashLength;
        cycle = dashLength + gap;
        if (double.IsInfinity(cycle)) throw new ArgumentOutOfRangeException(nameof(gap));
        double normalizedPhase = phase % cycle;
        if (normalizedPhase < 0d) normalizedPhase += cycle;
        firstStart = -normalizedPhase;
        if (normalizedPhase >= dashLength) firstStart += cycle;
        double intervals = length == 0d ? 0d : Math.Max(0d, Math.Ceiling((length - firstStart) / cycle));
        // More intervals cannot be represented by the renderer's indexed mesh/list buffers.
        if (intervals > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(length));
        count = (int)intervals;
        index = 0;
        Start = End = 0d;
        NextPhase = (normalizedPhase + length) % cycle;
    }

    internal bool MoveNext()
    {
        if (index >= count) return false;
        // Count whole cycles instead of advancing a float by the residual gap. Near a
        // cycle boundary Mono can retain a sub-ULP remainder: adding it to the float
        // cursor rounds back to the same cursor forever, even on short normal routes.
        double start = firstStart + (double)index++ * cycle;
        Start = Math.Max(0d, start);
        End = Math.Min(length, start + dashLength);
        return true;
    }
}
