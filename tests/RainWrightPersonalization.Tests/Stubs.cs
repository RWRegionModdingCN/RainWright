namespace DryCycle.DevUI.DevTool.Core
{
    internal static class EditorDiagnostics
    {
        internal static readonly BepInEx.Logging.ManualLogSource LogSource = new();
        internal static readonly List<string> Errors = new();
        internal static void Info(string message) { }
        internal static void Warning(string message, Exception error = null) => Errors.Add(message + error);
        internal static void Error(string message, Exception error = null) => Errors.Add(message + error);
    }
}
namespace DryCycle.DevUI.DevTool.RWImGui
{
    internal static class FloatingWindowSnap { internal static void ReloadPersistedLayout() { } }
    internal static class DevToolOverlay { internal static void ReloadPersistedPresentationState() { } }
    internal static class DevToolFrontend
    {
        internal static int Rebuilds;
        internal static void RequestContextRebuildForLanguageChange() { Rebuilds++; }
    }
}
namespace UnityEngine
{
    public enum KeyCode { A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12, Tab, Delete, Space,
        LeftControl, RightControl, LeftCommand, RightCommand, LeftShift, RightShift, LeftAlt, RightAlt }
    public static class Input
    {
        internal static readonly HashSet<KeyCode> Held = new(), Down = new();
        public static bool GetKey(KeyCode key) => Held.Contains(key);
        public static bool GetKeyDown(KeyCode key) => Down.Contains(key);
    }
}
