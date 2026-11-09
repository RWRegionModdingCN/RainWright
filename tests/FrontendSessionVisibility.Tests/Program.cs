using System;
using DryCycle.DevUI.DevTool.RWImGui;

internal sealed class FakeGame
{
    internal bool ProcessActive = true, ToolsActive = true, LoopActive = true;
    internal bool IsClosed => !ProcessActive || !ToolsActive || !LoopActive;
}

internal static class Program
{
    private static int assertions;
    private static void Check(bool ok, string reason)
    {
        assertions++;
        if (!ok) throw new InvalidOperationException(reason);
    }

    private static void Main()
    {
        Check(typeof(FrontendSessionVisibilityPolicy<FakeGame>).Assembly.GetName().Name == "RainWright",
            "The independent DLL must own the focus policy.");
        var core = new FrontendSessionVisibilityPolicy<FakeGame>();
        var game = new FakeGame();
        var cold = core.Observe(false, null, false, g => g.IsClosed);
        Check(!cold.Visible && cold.DefinitelyClosed, "Cold session should be closed.");
        var active = core.Observe(true, game, false, g => g.IsClosed);
        Check(active.Visible && active.Returned && active.FrontendVisible(false, false),
            "New game session must show frontend once.");
        core.OnApplicationFocus(false);
        Check(core.FocusTransitionActive, "Focus callback must latch before Update.");
        var missing = core.Observe(false, null, false, g => g.IsClosed);
        Check(missing.Visible && !missing.Returned && !missing.DefinitelyClosed,
            "Lost session hub during Alt+Tab must not destroy native UI.");
        core.OnApplicationFocus(true);
        var regained = core.Observe(false, null, false, g => g.IsClosed);
        Check(regained.Visible, "Restored focus must wait for session hub.");
        var same = core.Observe(true, game, false, g => g.IsClosed);
        Check(same.Visible && !same.Returned && !core.FocusTransitionActive,
            "Same game resuming must not initialize optional features again.");
        var paused = core.Observe(true, game, true, g => g.IsClosed);
        Check(paused.Paused && !paused.ShouldUnhideOverlay,
            "Entering pause must not reveal hidden windows.");
        core.OnApplicationFocus(false);
        var missingPaused = core.Observe(false, null, false, g => g.IsClosed);
        Check(missingPaused.Paused && !missingPaused.ResumedFromPause &&
              !missingPaused.ShouldUnhideOverlay,
            "Transient null game must not be treated as unpause.");
        core.OnApplicationFocus(true);
        var unpaused = core.Observe(true, game, false, g => g.IsClosed);
        Check(unpaused.ResumedFromPause && unpaused.ShouldUnhideOverlay &&
              unpaused.FrontendVisible(true, true) && !unpaused.FrontendVisible(true, false),
            "Real unpause and feedback hold must respect overlay visibility.");
        core.OnApplicationFocus(false);
        game.LoopActive = false;
        var ended = core.Observe(false, null, false, g => g.IsClosed);
        Check(ended.DefinitelyClosed && !ended.Visible && !core.WasVisible,
            "Genuine game close must release preserved context.");
        Check(!core.Observe(false, null, false, g => g.IsClosed).Returned,
            "Close transition must not repeat.");
        // The session hub can still say live when the underlying game ends.
        var staleGame = new FakeGame();
        core.Observe(true, staleGame, false, g => g.IsClosed);
        staleGame.ProcessActive = false;
        var staleHub = core.Observe(true, staleGame, false, g => g.IsClosed);
        Check(staleHub.DefinitelyClosed && !staleHub.Visible,
            "Positive process exit must beat stale IsCurrentSessionLive=true.");

        var newGame = new FakeGame();
        Check(core.Observe(true, newGame, false, g => g.IsClosed).Returned,
            "New game after exit must report returned session.");
        // Game replacement without an intermediate hub reset is a new
        // lifetime, not the old game's optional feature state.
        var switchedGame = new FakeGame();
        var switched = core.Observe(true, switchedGame, false, g => g.IsClosed);
        Check(switched.Visible && switched.Returned,
            "New game identity under a live hub must trigger frontend reset.");
        newGame.ToolsActive = false;
        Check(core.Observe(true, switchedGame, false, g => g.IsClosed).Visible,
            "Closed old game must not tear down a replacement game.");
        switchedGame.ToolsActive = false;
        Check(core.Observe(false, null, false, g => g.IsClosed).DefinitelyClosed,
            "Closing DevTools must retire session.");
        core.Reset();
        Check(!core.WasVisible && !core.FocusTransitionActive,
            "Disabling frontend must reset retained focus/session state.");
        bool threw = false;
        try { core.Observe(false, null, false, null); }
        catch (ArgumentNullException) { threw = true; }
        Check(threw, "Missing closure callback must be rejected.");
        Console.WriteLine("PASS: " + assertions + " Rainwright session focus/visibility invariants.");
    }
}
