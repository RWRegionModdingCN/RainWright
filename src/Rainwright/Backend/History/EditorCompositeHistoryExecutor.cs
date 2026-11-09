using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.History;

/// <summary>
/// Rainwright-owned compensation engine for grouped authoring commands.
/// The game layer supplies concrete commands; partially succeeded groups
/// must be returned to their starting state in the reverse operation order.
/// </summary>
internal static class EditorCompositeHistoryExecutor
{
    internal static bool Undo<TEntry>(
        IReadOnlyList<TEntry> entries,
        Func<TEntry, bool> undo,
        Func<TEntry, bool> redo,
        Action<TEntry, Exception> onUndoError = null,
        Action<TEntry, Exception> onRollbackError = null)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        if (undo == null) throw new ArgumentNullException(nameof(undo));
        if (redo == null) throw new ArgumentNullException(nameof(redo));

        int undoneFrom = entries.Count;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            bool succeeded;
            try { succeeded = undo(entries[i]); }
            catch (Exception error)
            {
                Report(onUndoError, entries[i], error);
                succeeded = false;
            }
            if (succeeded)
            {
                undoneFrom = i;
                continue;
            }

            // Undo succeeded on the tail. Replay exactly that tail in
            // original forward order to avoid leaving a half-reverted batch.
            for (int restore = undoneFrom; restore < entries.Count; restore++)
            {
                try { redo(entries[restore]); }
                catch (Exception error) { Report(onRollbackError, entries[restore], error); }
            }
            return false;
        }
        return true;
    }

    internal static bool Redo<TEntry>(
        IReadOnlyList<TEntry> entries,
        Func<TEntry, bool> redo,
        Func<TEntry, bool> undo,
        Action<TEntry, Exception> onRedoError = null,
        Action<TEntry, Exception> onRollbackError = null)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));
        if (redo == null) throw new ArgumentNullException(nameof(redo));
        if (undo == null) throw new ArgumentNullException(nameof(undo));

        int redoneThrough = -1;
        for (int i = 0; i < entries.Count; i++)
        {
            bool succeeded;
            try { succeeded = redo(entries[i]); }
            catch (Exception error)
            {
                Report(onRedoError, entries[i], error);
                succeeded = false;
            }
            if (succeeded)
            {
                redoneThrough = i;
                continue;
            }

            // Redo succeeded on the prefix. Restore that prefix in the
            // reverse order so the previous model state is recovered.
            for (int restore = redoneThrough; restore >= 0; restore--)
            {
                try { undo(entries[restore]); }
                catch (Exception error) { Report(onRollbackError, entries[restore], error); }
            }
            return false;
        }
        return true;
    }

    private static void Report<TEntry>(
        Action<TEntry, Exception> report, TEntry entry, Exception error)
    {
        try { report?.Invoke(entry, error); }
        catch (Exception) { /* Preserve rollback progress even if diagnostics fault. */ }
    }
}
