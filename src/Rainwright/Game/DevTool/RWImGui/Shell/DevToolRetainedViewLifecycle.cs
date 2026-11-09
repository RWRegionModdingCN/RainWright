using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal sealed class DevToolRetainedViewLifecycle
{
    private readonly FrontendRetainedSessionGate<RainWorldGame> lifetime = new();

    internal void Enable() => lifetime.Reset();

    internal void LateUpdate()
    {
        bool live = DevToolSessionHub.IsCurrentSessionLive;
        EditorSession session = live ? DevToolSessionHub.Current : null;
        RainWorldGame game = session?.Owner?.game;

        if (live)
        {
            lifetime.Observe(true, game, IsDefinitelyClosed);

            // Page lifecycle follows the editor ToolMode even while RWImGui is temporarily hidden or
            // Vanilla UI is primary. The standalone debug workspace is different: it deliberately
            // replaces every normal page surface, so a normal page must stay deactivated for the
            // entire debug lifetime instead of being reactivated here from the stale ToolMode.
            if (DevToolOverlay.SuppressesSharedPageSurfaces)
                DevToolPageViewRegistry.DeactivateActive();
            else if (session != null)
                DevToolPageViewRegistry.SynchronizeActive(session.ToolMode);
            return;
        }

        // Missing owner/pages after Alt+Tab do not prove the game closed.
        // Rainwright retains the last valid RainWorldGame until positive
        // process closure is observed; never clear UI from a transient null.
        if (lifetime.Observe(false, null, IsDefinitelyClosed))
            ReleaseRetainedState();
    }

    internal void Disable()
    {
        ReleaseRetainedState();
        lifetime.Reset();
    }

    private static bool IsDefinitelyClosed(RainWorldGame game)
    {
        if (game == null) return true;
        if (!game.processActive || !game.devToolsActive) return true;

        return game.manager?.currentMainLoop != null &&
               !object.ReferenceEquals(game.manager.currentMainLoop, game);
    }

    private static void ReleaseRetainedState()
    {
        DevToolNumericWidgets.Reset();
        DevToolOverlay.ResetRetainedState();

        // Registered pages are the sole owners of page-specific retained projections. Resetting the
        // registry replaces the old frontend fan-out and guarantees newly registered pages cannot be
        // forgotten by this lifetime edge.
        DevToolPageViewRegistry.ResetAll();

        UniversalDevUiMirrorView.ResetRetainedState();
    }
}
