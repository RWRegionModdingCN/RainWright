using System;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>Immutable main-thread visibility decision; no engine resources.</summary>
internal readonly struct FrontendSessionVisibilityDecision
{
    internal readonly bool Visible, Paused, Returned, ResumedFromPause, DefinitelyClosed;
    internal FrontendSessionVisibilityDecision(bool visible, bool paused,
        bool returned, bool resumedFromPause, bool definitelyClosed)
    {
        Visible = visible; Paused = paused; Returned = returned;
        ResumedFromPause = resumedFromPause; DefinitelyClosed = definitelyClosed;
    }

    internal bool ShouldUnhideOverlay => Visible && (Returned || ResumedFromPause);
    internal bool FrontendVisible(bool overlayHidden, bool feedbackHold) =>
        Visible && (!overlayHidden || feedbackHold);
}

/// <summary>
/// Retain last positively observed game when session sources are temporarily
/// unavailable after focus changes. Only a real process/DevTools/main-loop
/// shutdown can retire an established native frontend lifetime.
/// </summary>
internal sealed class FrontendSessionVisibilityPolicy<TGame> where TGame : class
{
    private TGame lastLiveGame;
    private bool wasVisible, wasPaused, focusTransitionActive, applicationFocused = true;

    internal bool WasVisible => wasVisible;
    internal bool FocusTransitionActive => focusTransitionActive;

    internal void Reset(bool focused = true)
    {
        lastLiveGame = null;
        wasVisible = wasPaused = focusTransitionActive = false;
        applicationFocused = focused;
    }

    internal void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        if (!focused && wasVisible) focusTransitionActive = true;
    }

    internal FrontendSessionVisibilityDecision Observe(
        bool live, TGame currentGame, bool paused, Func<TGame, bool> isDefinitelyClosed)
    {
        if (isDefinitelyClosed == null)
            throw new ArgumentNullException(nameof(isDefinitelyClosed));

        // A new game object under a still-live session hub is also a new
        // editor lifetime; it must re-enable faulted optional work.
        bool changedGame = live && currentGame != null &&
            lastLiveGame != null && !ReferenceEquals(lastLiveGame, currentGame);
        if (live && currentGame != null)
            lastLiveGame = currentGame;

        // The hub itself can remain stale for one or more frames after the
        // game exits. Always honor positive close evidence from lastLiveGame.
        bool definitelyClosed = lastLiveGame == null
            ? !live
            : isDefinitelyClosed(lastLiveGame);
        bool effectiveLive = live && !definitelyClosed;

        if (effectiveLive || definitelyClosed)
            focusTransitionActive = false;
        else if (!applicationFocused && wasVisible)
            focusTransitionActive = true;

        bool visible = effectiveLive ||
            (wasVisible && focusTransitionActive && !definitelyClosed);
        // A missing game reference must not look like an unpause.
        bool actuallyPaused = visible && (effectiveLive ? paused : wasPaused);
        bool returned = visible && (!wasVisible || changedGame);
        bool resumed = visible && !changedGame && wasPaused && !actuallyPaused;

        wasVisible = visible;
        wasPaused = actuallyPaused;
        if (definitelyClosed) lastLiveGame = null;
        return new FrontendSessionVisibilityDecision(
            visible, actuallyPaused, returned, resumed, definitelyClosed);
    }
}
