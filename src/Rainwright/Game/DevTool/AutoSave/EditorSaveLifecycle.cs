using System;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>Saves before native game teardown and at the real H/O close edge, including Vanilla UI.</summary>
internal static class EditorSaveLifecycle
{
    private static readonly EditorBackendServiceLedger hooks = new();
    internal static void Enable()
    {
        if (hooks.Count != 0) throw new InvalidOperationException("Editor save hooks are already installed or awaiting cleanup.");
        hooks.Start("close", () => On.RainWorldGame.RawUpdate += RawUpdate, () => On.RainWorldGame.RawUpdate -= RawUpdate);
        hooks.Start("process exit", () => On.RainWorldGame.ShutDownProcess += ShutDownProcess, () => On.RainWorldGame.ShutDownProcess -= ShutDownProcess);
        hooks.Start("application exit", () => UnityEngine.Application.quitting += ApplicationQuit, () => UnityEngine.Application.quitting -= ApplicationQuit);
    }
    internal static void Disable()
    {
        AutoSaveCoordinator.FlushOnExit(reason: "editor backend disabled");
        if (!hooks.ReleaseAll((_, stop) => stop(), (name, error) => EditorDiagnostics.Error("Save lifecycle cleanup failed: " + name, error)))
            throw new InvalidOperationException("Editor save hooks remain installed; cleanup must be retried.");
    }
    private static void RawUpdate(On.RainWorldGame.orig_RawUpdate orig, RainWorldGame game, float dt)
    {
        var session = DevToolSessionHub.Current;
        bool wasOpen = game != null && game.devToolsActive && session?.Owner != null && ReferenceEquals(session.Owner.game, game) && ReferenceEquals(game.devUI, session.Owner);
        bool prepareClose = wasOpen && session.RoomSettings != null && !Input.EditorInputRouter.CapturesNativeDevUiKeys(game) &&
            (UnityEngine.Input.GetKey(UnityEngine.KeyCode.H) && !game.hDown || UnityEngine.Input.GetKey(UnityEngine.KeyCode.O) && !game.oDown);
        if (prepareClose) PrepareAndFlush(game, "H/O close requested");
        orig(game, dt);
        if (!prepareClose && wasOpen && (!game.devToolsActive || !ReferenceEquals(game.devUI, session.Owner)))
            AutoSaveCoordinator.FlushOnExit(session, "H/O closed UI");
    }
    private static void ShutDownProcess(On.RainWorldGame.orig_ShutDownProcess orig, RainWorldGame game)
    {
        PrepareAndFlush(game, "game process exit");
        orig(game);
    }
    private static void ApplicationQuit() => PrepareAndFlush(null, "application quit");
    private static void PrepareAndFlush(RainWorldGame game, string reason)
    {
        var session = DevToolSessionHub.Current;
        if (game != null && session?.Owner?.game != game) session = null;
        if (session != null && DevToolSessionHub.IsCurrentSessionLive)
        {
            try { DevToolSubsystemCoordinator.ProcessPendingCommands(session); }
            catch (Exception error) { EditorDiagnostics.Error("Could not drain committed commands before " + reason, error); }
        }
        AutoSaveCoordinator.FlushOnExit(session, reason);
    }
}
