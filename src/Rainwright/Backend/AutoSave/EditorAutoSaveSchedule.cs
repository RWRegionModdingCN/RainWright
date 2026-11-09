using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Main-thread, Rainwright-owned clock and per-document save scheduler.
/// No filesystem, Unity input, gameplay state or game plugin references.
/// The host decides what is dirty and invokes actual authoring saves; this
/// scheduler never marks a failed write as persisted.
/// </summary>
internal sealed class EditorAutoSaveSchedule<TDocument>
{
    internal sealed class State
    {
        internal long SavedHistoryRevision;
        internal DateTime NextSaveUtc;
        internal DateTime NextRecoveryUtc;
        internal bool Observed;
        internal int IntervalSeconds;
        internal long AttemptedHistoryRevision;
        internal int AttemptedWorldRevision;
        internal DateTime RetryAfterUtc;
        internal bool AttemptFailed;
    }

    private readonly Dictionary<TDocument, State> documents = new();
    private DateTime nextPollUtc;
    private const int MaximumTrackedDocuments = 256;

    internal bool TryPoll(DateTime now)
    {
        if (now < nextPollUtc) return false;
        nextPollUtc = now.AddSeconds(1);
        return true;
    }

    internal State Observe(TDocument document, long revision, int intervalSeconds, DateTime now)
    {
        if (!documents.TryGetValue(document, out State state))
        {
            if (documents.Count >= MaximumTrackedDocuments)
                documents.Clear();
            state = new State();
            documents[document] = state;
        }

        if (!state.Observed)
        {
            state.Observed = true;
            state.SavedHistoryRevision = revision;
            state.IntervalSeconds = intervalSeconds;
            state.NextSaveUtc = now.AddSeconds(intervalSeconds);
            state.NextRecoveryUtc = now.AddSeconds(60);
        }

        if (state.IntervalSeconds != intervalSeconds)
        {
            state.IntervalSeconds = intervalSeconds;
            state.NextSaveUtc = now.AddSeconds(intervalSeconds);
        }

        return state;
    }

    internal void ObserveIdle(State state, DateTime now)
    {
        if (state == null) return;
        // Idle time cannot count toward the next dirty-save timeout.
        state.NextSaveUtc = now.AddSeconds(state.IntervalSeconds);
        state.NextRecoveryUtc = now.AddSeconds(60);
    }

    internal bool RecoveryDue(State state, DateTime now)
    {
        if (state == null || now < state.NextRecoveryUtc) return false;
        state.NextRecoveryUtc = now.AddSeconds(60);
        return true;
    }

    internal bool SaveDue(State state, DateTime now, int intervalSeconds)
    {
        if (state == null || now < state.NextSaveUtc) return false;
        // This is a scheduling attempt, not a successful write. The saved
        // revision only changes after ObserveSuccessfulSave is called.
        state.NextSaveUtc = now.AddSeconds(Math.Max(30, intervalSeconds));
        return true;
    }

    internal void ObserveSuccessfulSave(
        TDocument document, long revision, int intervalSeconds, DateTime now)
    {
        if (!documents.TryGetValue(document, out State state))
            documents[document] = state = new State();

        state.Observed = true;
        state.SavedHistoryRevision = revision;
        state.IntervalSeconds = Math.Max(30, intervalSeconds);
        state.NextSaveUtc = now.AddSeconds(state.IntervalSeconds);
    }

    internal void Reset()
    {
        nextPollUtc = default;
        documents.Clear();
    }
}
