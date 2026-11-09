using System;
using System.Runtime.CompilerServices;

namespace DryCycle.DevUI.DevTool.History;

/// <summary>
/// Rainwright-owned per-session transaction identity. Preview mutations are
/// performed by a game adapter, but the before-state, document and active
/// gesture key must have one owner across the complete drag lifetime.
/// </summary>
internal sealed class EditorContinuousEditRegistry<TSession, TDocument, TSnapshot>
    where TSession : class
    where TSnapshot : class
{
    private sealed class State
    {
        internal string Key = string.Empty;
        internal string Label = string.Empty;
        internal TSnapshot Before;
        internal TDocument Document;
        internal bool Active;
    }

    private ConditionalWeakTable<TSession, State> states = new();

    internal bool Begin(TSession session, string key, string label, TDocument document, TSnapshot before)
    {
        if (session == null || before == null || string.IsNullOrWhiteSpace(key))
            return false;
        State state = states.GetValue(session, _ => new State());
        if (state.Active)
            return string.Equals(state.Key, key, StringComparison.Ordinal);

        state.Key = key;
        state.Label = string.IsNullOrWhiteSpace(label) ? "Interactive edit" : label;
        state.Before = before;
        state.Document = document;
        state.Active = true;
        return true;
    }

    internal bool IsActive(TSession session, string key = null)
    {
        if (session == null || !states.TryGetValue(session, out State state) || !state.Active)
            return false;
        return string.IsNullOrEmpty(key) || string.Equals(state.Key, key, StringComparison.Ordinal);
    }

    internal string ActiveKey(TSession session)
    {
        if (session == null || !states.TryGetValue(session, out State state) || !state.Active)
            return null;
        return state.Key;
    }

    /// <summary>
    /// The transaction is detached before an external capture/restore call.
    /// A false result or exception from those calls must not leave an invisible
    /// active gesture that could later be committed to a different document.
    /// </summary>
    internal bool TryTake(
        TSession session, string key, out string label,
        out TDocument document, out TSnapshot before)
    {
        label = null;
        document = default;
        before = null;
        if (session == null || !states.TryGetValue(session, out State state) || !state.Active ||
            !string.Equals(state.Key, key, StringComparison.Ordinal))
            return false;

        label = state.Label;
        document = state.Document;
        before = state.Before;
        state.Key = string.Empty;
        state.Label = string.Empty;
        state.Before = null;
        state.Document = default;
        state.Active = false;
        return true;
    }

    internal void Reset() => states = new ConditionalWeakTable<TSession, State>();
}
