using System;
using BepInEx.Logging;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Editor-owned BepInEx log source. Diagnostic paths must not require DryCycle.Plugin to
/// have initialized, and future RainWright.dll must not import DryCycle.Plugin.Logger.
/// Always retain the complete exception, not just Message, for recoverable failures.
/// </summary>
internal static class EditorDiagnostics
{
    private static readonly ManualLogSource Source =
        BepInEx.Logging.Logger.CreateLogSource("Rainwright.Editor");

    // Only editor-owned runtime services obtain this sink. The DryCycle
    // gameplay bootstrap is not required to initialize it first.
    internal static ManualLogSource LogSource => Source;

    internal static void Info(string message)
    {
        Source.LogInfo(message ?? string.Empty);
    }

    internal static void Warning(string message, Exception error = null)
    {
        Source.LogWarning(Format(message, error));
    }

    internal static void Error(string message, Exception error = null)
    {
        Source.LogError(Format(message, error));
    }

    private static string Format(string message, Exception error)
    {
        return error == null
            ? message ?? string.Empty
            : (message ?? string.Empty) + Environment.NewLine + error;
    }
}
