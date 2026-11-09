using System;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Portable, session-bound lifetime detector for retained UI projections.
/// A temporary loss of the current editor owner is not a terminal event:
/// only the host's positive close predicate may retire observed state.
/// </summary>
internal sealed class FrontendRetainedSessionGate<TGame> where TGame : class
{
    private bool observedLiveSession;
    private TGame observedGame;

    internal bool HasLiveSession => observedLiveSession;

    internal void Reset()
    {
        observedLiveSession = false;
        observedGame = null;
    }

    /// <summary>
    /// True means the previously live session is definitely closed and its
    /// retained view cache can be released now. This is a one-shot transition.
    /// The caller is responsible for actual game/UI cleanup side effects.
    /// </summary>
    internal bool Observe(bool liveSession, TGame currentGame,
        Func<TGame, bool> isDefinitelyClosed)
    {
        if (liveSession)
        {
            observedLiveSession = true;
            if (currentGame != null)
                observedGame = currentGame;
            return false;
        }

        if (!observedLiveSession)
            return false;
        if (isDefinitelyClosed == null)
            throw new ArgumentNullException(nameof(isDefinitelyClosed));
        if (!isDefinitelyClosed(observedGame))
            return false;

        Reset();
        return true;
    }
}
