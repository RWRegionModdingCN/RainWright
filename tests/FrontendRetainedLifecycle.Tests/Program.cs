using System;
using DryCycle.DevUI.DevTool.RWImGui;

internal sealed class RainWorldGame
{
    internal bool processActive = true, devToolsActive = true;
    internal GameManager manager = new();
}
internal sealed class GameManager
{
    internal object currentMainLoop;
}

namespace DryCycle.DevUI.DevTool.Core
{
    internal sealed class EditorSession
    {
        internal GameOwner Owner;
        internal int ToolMode;
    }
    internal sealed class GameOwner
    {
        internal RainWorldGame game;
    }
    internal static class DevToolSessionHub
    {
        internal static bool IsCurrentSessionLive;
        internal static EditorSession Current;
    }
}

namespace DryCycle.DevUI.DevTool.RWImGui
{
    internal static class DevToolOverlay
    {
        internal static bool SuppressesSharedPageSurfaces;
        internal static int Clears;
        internal static void ResetRetainedState() => Clears++;
    }
    internal static class DevToolNumericWidgets
    {
        internal static int Clears;
        internal static void Reset() => Clears++;
    }
    internal static class DevToolPageViewRegistry
    {
        internal static int Deactivations, Synchronizations, Resets, LastMode;
        internal static void DeactivateActive() => Deactivations++;
        internal static void SynchronizeActive(int mode)
        {
            Synchronizations++; LastMode = mode;
        }
        internal static void ResetAll() => Resets++;
    }
    internal static class UniversalDevUiMirrorView
    {
        internal static int Clears;
        internal static void ResetRetainedState() => Clears++;
    }
}

internal static class Program
{
    private static void Check(bool success, string description)
    {
        if (!success) throw new InvalidOperationException(description);
    }

    private static void Main()
    {
        Check(typeof(FrontendRetainedSessionGate<RainWorldGame>).Assembly.GetName().Name == "RainWright" &&
              typeof(FrontendRetainedSessionGate<RainWorldGame>).Assembly !=
              typeof(DevToolRetainedViewLifecycle).Assembly,
            "Retained frontend lifetime state must be owned by Rainwright.dll.");
        var game = new RainWorldGame();
        game.manager.currentMainLoop = game;
        var lifecycle = new DevToolRetainedViewLifecycle();
        var session = new DryCycle.DevUI.DevTool.Core.EditorSession
        {
            Owner = new DryCycle.DevUI.DevTool.Core.GameOwner { game = game },
            ToolMode = 42
        };
        lifecycle.Enable();
        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.IsCurrentSessionLive = true;
        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.Current = session;
        lifecycle.LateUpdate();
        Check(DevToolPageViewRegistry.Synchronizations == 1 &&
              DevToolPageViewRegistry.LastMode == 42 &&
              DevToolPageViewRegistry.Resets == 0,
            "A live game must synchronize the selected editor page.");

        DevToolOverlay.SuppressesSharedPageSurfaces = true;
        lifecycle.LateUpdate();
        Check(DevToolPageViewRegistry.Deactivations == 1 &&
              DevToolPageViewRegistry.Synchronizations == 1,
            "An active debug overlay must deactivate ordinary page surfaces.");

        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.IsCurrentSessionLive = false;
        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.Current = null;
        lifecycle.LateUpdate();
        Check(DevToolPageViewRegistry.Resets == 0,
            "Alt+Tab or a transient session mismatch must retain cached page state.");
        game.manager.currentMainLoop = new object();
        lifecycle.LateUpdate();
        Check(DevToolPageViewRegistry.Resets == 1 &&
              DevToolNumericWidgets.Clears == 1 &&
              DevToolOverlay.Clears == 1 &&
              UniversalDevUiMirrorView.Clears == 1,
            "A confirmed game transition must release all retained pages exactly once.");
        lifecycle.LateUpdate();
        Check(DevToolPageViewRegistry.Resets == 1,
            "Closed state must be one-shot, not reset on every frame.");

        var newGame = new RainWorldGame();
        newGame.manager.currentMainLoop = newGame;
        session.Owner.game = newGame;
        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.IsCurrentSessionLive = true;
        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.Current = session;
        DevToolOverlay.SuppressesSharedPageSurfaces = false;
        lifecycle.LateUpdate();
        DryCycle.DevUI.DevTool.Core.DevToolSessionHub.IsCurrentSessionLive = false;
        newGame.devToolsActive = false;
        lifecycle.LateUpdate();
        Check(DevToolPageViewRegistry.Resets == 2,
            "Disabling native DevTools must also release a retained view lifetime.");

        lifecycle.Enable();
        lifecycle.LateUpdate();
        lifecycle.Disable();
        Check(DevToolPageViewRegistry.Resets == 3,
            "Plugin Disable must unconditionally release stale native views.");

        var portable = new FrontendRetainedSessionGate<object>();
        var owner = new object();
        portable.Observe(true, owner, _ => false);
        Check(!portable.Observe(false, null, _ => false) && portable.HasLiveSession,
            "A temporary missing editor owner must not expire the observed session.");
        Check(portable.Observe(false, null, x => ReferenceEquals(x, owner)) &&
              !portable.Observe(false, null, _ => true),
            "An actual closure should retire the previous session once.");

        Console.WriteLine("PASS: Rainwright retained-session ownership and real frontend cleanup adapter.");
    }
}
