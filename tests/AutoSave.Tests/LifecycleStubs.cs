using System;
using DryCycle.DevUI.DevTool.Core;

internal sealed class DevUI { internal RainWorldGame game; }
internal sealed class RainWorldGame { internal bool devToolsActive = true; internal DevUI devUI; internal bool hDown = false, oDown = false; }
namespace UnityEngine
{
    internal enum KeyCode { H, O }
    internal static class Application
    {
        internal static event Action quitting;
        internal static void QuitForTest() => quitting?.Invoke();
    }
}
namespace On
{
    internal static class RainWorldGame
    {
        internal delegate void orig_RawUpdate(global::RainWorldGame game, float dt);
        internal delegate void hook_RawUpdate(orig_RawUpdate orig, global::RainWorldGame game, float dt);
        internal delegate void orig_ShutDownProcess(global::RainWorldGame game);
        internal delegate void hook_ShutDownProcess(orig_ShutDownProcess orig, global::RainWorldGame game);
        internal static event hook_RawUpdate RawUpdate;
        internal static event hook_ShutDownProcess ShutDownProcess;
        internal static void CloseForTest(global::RainWorldGame game, Action original)
        { if (RawUpdate != null) RawUpdate((_, _) => original(), game, 0.025f); else original(); }
        internal static void ShutdownForTest(global::RainWorldGame game, Action original)
        { if (ShutDownProcess != null) ShutDownProcess(_ => original(), game); else original(); }
    }
}
namespace DryCycle.DevUI.DevTool.Core
{
    internal static class DevToolSubsystemCoordinator
    {
        internal static int Drains;
        internal static void ProcessPendingCommands(EditorSession session) => Drains++;
    }
}
