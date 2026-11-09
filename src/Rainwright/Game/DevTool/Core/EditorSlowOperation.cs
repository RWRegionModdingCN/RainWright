using System;
using System.Diagnostics;
using System.Globalization;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>Logs slow initialization boundaries without enabling the interactive profiler.</summary>
internal readonly struct EditorSlowOperation : IDisposable
{
    private readonly string stage;
    private readonly long started;
    private readonly double threshold;

    private EditorSlowOperation(string stage, double threshold, bool enabled)
    {
        this.stage = stage;
        this.threshold = threshold;
        started = enabled ? Stopwatch.GetTimestamp() : 0L;
    }

    internal static EditorSlowOperation Measure(string stage, double threshold = 50d, bool enabled = true) =>
        new(stage, threshold, enabled);

    public void Dispose()
    {
        if (started == 0L) return;
        double elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
        if (elapsed >= threshold)
            EditorDiagnostics.Info("Slow editor operation | stage=" + stage + " | elapsedMs=" +
                elapsed.ToString("F2", CultureInfo.InvariantCulture) + " | utc=" + DateTime.UtcNow.ToString("O"));
    }
}
