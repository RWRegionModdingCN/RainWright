using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.History;

/// <summary>
/// Rainwright-owned, editor-document-scoped undo/redo journal. Game-side
/// history entries and UI invalidation are supplied as delegates; this class
/// never imports Unity, a gameplay plugin or any editor session type.
/// Batch scopes, stack mutations and document isolation are kept together
/// to prevent two assemblies owning divergent history revisions.
/// </summary>
internal sealed class EditorHistoryJournal<TDocument, TEntry> where TEntry : class
{
    private sealed class DocumentHistory
    {
        internal readonly List<TEntry> Undo = new();
        internal readonly List<TEntry> Redo = new();
        internal long AuthoringRevision;
    }

    private sealed class BatchScope : IDisposable
    {
        private EditorHistoryJournal<TDocument, TEntry> journal;
        internal BatchScope(EditorHistoryJournal<TDocument, TEntry> journal) =>
            this.journal = journal;
        public void Dispose()
        {
            var value = journal;
            journal = null;
            value?.EndBatch();
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        internal static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }

    private readonly Dictionary<TDocument, DocumentHistory> documents = new();
    private readonly Func<TEntry, string> labelFor;
    private readonly Func<string, IReadOnlyList<TEntry>, TEntry> compose;
    private readonly Action<bool> changed;
    private readonly int capacity;
    private TDocument activeDocument;
    private bool hasActiveDocument;
    private long revision = 1L;
    private int batchDepth;
    private string batchLabel = string.Empty;
    private List<TEntry> batchEntries;

    internal EditorHistoryJournal(
        int capacity,
        Func<TEntry, string> labelFor,
        Func<string, IReadOnlyList<TEntry>, TEntry> compose,
        Action<bool> changed = null)
    {
        this.capacity = Math.Max(1, capacity);
        this.labelFor = labelFor ?? throw new ArgumentNullException(nameof(labelFor));
        this.compose = compose ?? throw new ArgumentNullException(nameof(compose));
        this.changed = changed;
    }

    internal long Revision => revision;
    // Presentation revision also advances on document activation. Persistence must observe only
    // commits/undo/redo for the active document, so browsing cannot rewrite untouched author files.
    internal long AuthoringRevision => Current?.AuthoringRevision ?? 0;
    internal bool CanUndo => Current?.Undo.Count > 0;
    internal bool CanRedo => Current?.Redo.Count > 0;
    internal string UndoLabel => CanUndo ? labelFor(Current.Undo[Current.Undo.Count - 1]) : null;
    internal string RedoLabel => CanRedo ? labelFor(Current.Redo[Current.Redo.Count - 1]) : null;

    internal void ActivateDocument(TDocument document)
    {
        bool switching = !hasActiveDocument ||
            !EqualityComparer<TDocument>.Default.Equals(activeDocument, document);
        // Finish edits under the document that actually received the changes.
        if (switching && batchDepth > 0 && hasActiveDocument)
            CommitOpenBatch();

        activeDocument = document;
        hasActiveDocument = true;
        if (!documents.ContainsKey(document))
            documents.Add(document, new DocumentHistory());
        if (switching)
            BumpRevision(false);
    }

    internal IDisposable BeginBatch(string label)
    {
        if (!hasActiveDocument) return EmptyScope.Instance;
        if (batchDepth == 0)
        {
            batchLabel = string.IsNullOrEmpty(label) ? "Edit group" : label;
            batchEntries = new List<TEntry>();
        }
        batchDepth++;
        return new BatchScope(this);
    }

    internal void Push(TEntry entry)
    {
        if (entry == null || !hasActiveDocument) return;
        if (batchDepth > 0)
        {
            batchEntries ??= new List<TEntry>();
            batchEntries.Add(entry);
            return;
        }
        CommitEntry(entry);
    }

    // Calls are performed in the game adapter, but successful stack transfers
    // are atomic: a false/throwing entry never leaves the opposite stack moved.
    internal bool Undo(Func<TEntry, bool> execute, Action<TEntry, Exception> onError = null)
    {
        if (batchDepth > 0) return false;
        DocumentHistory history = Current;
        if (history == null || history.Undo.Count == 0) return false;
        int index = history.Undo.Count - 1;
        TEntry entry = history.Undo[index];
        try
        {
            if (!execute(entry)) return false;
        }
        catch (Exception error)
        {
            onError?.Invoke(entry, error);
            return false;
        }
        history.Undo.RemoveAt(index);
        history.Redo.Add(entry);
        BumpRevision(true);
        return true;
    }

    internal bool Redo(Func<TEntry, bool> execute, Action<TEntry, Exception> onError = null)
    {
        if (batchDepth > 0) return false;
        DocumentHistory history = Current;
        if (history == null || history.Redo.Count == 0) return false;
        int index = history.Redo.Count - 1;
        TEntry entry = history.Redo[index];
        try
        {
            if (!execute(entry)) return false;
        }
        catch (Exception error)
        {
            onError?.Invoke(entry, error);
            return false;
        }
        history.Redo.RemoveAt(index);
        history.Undo.Add(entry);
        if (history.Undo.Count > capacity)
            history.Undo.RemoveAt(0);
        BumpRevision(true);
        return true;
    }

    internal void ClearActive()
    {
        // The retired model can be captured by entries in an open transaction.
        if (batchDepth > 0)
            DiscardOpenBatch();
        DocumentHistory history = Current;
        if (history == null || (history.Undo.Count == 0 && history.Redo.Count == 0))
            return;
        history.Undo.Clear();
        history.Redo.Clear();
        BumpRevision(false);
    }

    private void CommitOpenBatch()
    {
        if (batchDepth <= 0) return;
        batchDepth = 0;
        FinishBatch();
    }

    private void DiscardOpenBatch()
    {
        batchDepth = 0;
        batchEntries = null;
        batchLabel = string.Empty;
    }

    private void EndBatch()
    {
        if (batchDepth <= 0) return;
        batchDepth--;
        if (batchDepth != 0) return;
        FinishBatch();
    }

    private void FinishBatch()
    {
        List<TEntry> entries = batchEntries;
        string label = batchLabel;
        batchEntries = null;
        batchLabel = string.Empty;
        if (entries == null || entries.Count == 0) return;
        CommitEntry(entries.Count == 1 && string.IsNullOrEmpty(label)
            ? entries[0]
            : compose(label, entries));
    }

    private void CommitEntry(TEntry entry)
    {
        if (entry == null || !hasActiveDocument) return;
        DocumentHistory history = Current;
        history.Undo.Add(entry);
        if (history.Undo.Count > capacity)
            history.Undo.RemoveAt(0);
        history.Redo.Clear();
        BumpRevision(true);
    }

    private void BumpRevision(bool modelMayHaveChanged)
    {
        revision = revision >= long.MaxValue ? 1L : revision + 1L;
        if (modelMayHaveChanged && Current is { } history)
            history.AuthoringRevision = history.AuthoringRevision >= long.MaxValue ? 1L : history.AuthoringRevision + 1L;
        changed?.Invoke(modelMayHaveChanged);
    }

    private DocumentHistory Current =>
        hasActiveDocument && documents.TryGetValue(activeDocument, out DocumentHistory value)
            ? value : null;
}
