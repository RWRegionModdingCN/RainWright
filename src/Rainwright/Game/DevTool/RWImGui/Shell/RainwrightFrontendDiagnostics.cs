using System;
using BepInEx.Logging;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Frontend-owned diagnostic sink. Retained map workers and overlay fallbacks
/// must never resolve DryCycle.Plugin.Logger: Rainwright-only boot and
/// diagnostic reporting must work even without the gameplay plugin.
/// </summary>
internal static class RainwrightFrontendDiagnostics
{
    private static readonly ManualLogSource Source =
        BepInEx.Logging.Logger.CreateLogSource("Rainwright.Frontend");

    internal static void Debug(string message) => Source.LogDebug(message ?? string.Empty);

    internal static void Info(string message) => Source.LogInfo(message ?? string.Empty);

    internal static void Warning(string message, Exception error = null) =>
        Source.LogWarning(Format(message, error));

    internal static void Error(string message, Exception error = null) =>
        Source.LogError(Format(message, error));

    private static string Format(string message, Exception error) =>
        error == null
            ? message ?? string.Empty
            : (message ?? string.Empty) + Environment.NewLine + error;
}
