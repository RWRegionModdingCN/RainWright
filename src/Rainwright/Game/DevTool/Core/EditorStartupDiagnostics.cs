using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Editor-owned startup breadcrumbs, independent of DryCycle's gameplay bootstrap.
/// Logs a BEGIN before each external hook/API call and keeps the full original
/// exception on failure. The diagnostic sink is best-effort and never owns startup.
/// </summary>
internal static class EditorStartupDiagnostics
{
    private static int sequence;

    internal static void Step(string source, Action action)
    {
        if (action == null) return;
        int id = Interlocked.Increment(ref sequence);
        LogInfo(Format(id, source, "BEGIN"));
        long started = Stopwatch.GetTimestamp();
        try
        {
            action();
            LogInfo(Format(id, source, "OK") + " " +
                ((Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency)
                .ToString("F2", CultureInfo.InvariantCulture) + " ms");
        }
        catch (Exception error)
        {
            Failure(id, source, error);
            throw;
        }
    }

    internal static void Marker(string source, string state, string detail = null)
    {
        int id = Interlocked.Increment(ref sequence);
        LogInfo(Format(id, source, state) +
            (string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail));
    }

    internal static void Failure(string source, Exception error) =>
        Failure(Interlocked.Increment(ref sequence), source, error);

    private static void Failure(int id, string source, Exception error)
    {
        LogError(Format(id, source, "FAIL"), error);
    }

    private static string Format(int id, string source, string state) =>
        "[Rainwright.Startup][" + id.ToString("D4") + "][" +
        (string.IsNullOrWhiteSpace(state) ? "STATE" : state) + "][" +
        (string.IsNullOrWhiteSpace(source) ? "<unknown>" : source) + "]";

    private static void LogInfo(string message)
    {
        try { EditorDiagnostics.Info(message); }
        catch (Exception) { /* A broken log listener must not prevent startup. */ }
    }

    private static void LogError(string message, Exception error)
    {
        try { EditorDiagnostics.Error(message, error); }
        catch (Exception) { /* Keep the startup exception as the caller's original exception. */ }
    }
}
