using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.History;

public interface IEditorHistoryEntry
{
    string Label { get; }
    bool Undo(EditorSession session);
    bool Redo(EditorSession session);
}

public sealed class DelegateHistoryEntry : IEditorHistoryEntry
{
    private readonly Func<EditorSession, bool> undo;
    private readonly Func<EditorSession, bool> redo;

    public DelegateHistoryEntry(string label, Func<EditorSession, bool> undo, Func<EditorSession, bool> redo)
    {
        Label = string.IsNullOrEmpty(label) ? "Edit" : label;
        this.undo = undo ?? throw new ArgumentNullException(nameof(undo));
        this.redo = redo ?? throw new ArgumentNullException(nameof(redo));
    }

    public string Label { get; }
    public bool Undo(EditorSession session) => undo(session);
    public bool Redo(EditorSession session) => redo(session);
}

/// <summary>
/// One logical history item composed from several already-reversible edits. Undo runs children in
/// reverse mutation order; Redo runs them forward. If a child fails, operations already applied in
/// this call are rolled back best-effort so a grouped edit does not intentionally leave half-state.
/// </summary>
public sealed class CompositeHistoryEntry : IEditorHistoryEntry
{
    private readonly IEditorHistoryEntry[] entries;

    public CompositeHistoryEntry(string label, IReadOnlyList<IEditorHistoryEntry> entries)
    {
        Label = string.IsNullOrEmpty(label) ? "Edit group" : label;
        if (entries == null || entries.Count == 0)
            throw new ArgumentException("Composite history requires at least one entry.", nameof(entries));
        this.entries = new IEditorHistoryEntry[entries.Count];
        for (int i = 0; i < entries.Count; i++)
            this.entries[i] = entries[i] ?? throw new ArgumentException(
                "Composite history cannot contain null entries.", nameof(entries));
    }

    public string Label { get; }

    public bool Undo(EditorSession session) =>
        EditorCompositeHistoryExecutor.Undo(
            entries,
            entry => entry.Undo(session),
            entry => entry.Redo(session),
            (entry, error) => EditorDiagnostics.Warning(
                "Composite history undo failed for '" + entry.Label + "': " + error),
            (entry, error) => EditorDiagnostics.Warning(
                "Composite history undo rollback failed for '" + entry.Label + "': " + error));

    public bool Redo(EditorSession session) =>
        EditorCompositeHistoryExecutor.Redo(
            entries,
            entry => entry.Redo(session),
            entry => entry.Undo(session),
            (entry, error) => EditorDiagnostics.Warning(
                "Composite history redo failed for '" + entry.Label + "': " + error),
            (entry, error) => EditorDiagnostics.Warning(
                "Composite history redo rollback failed for '" + entry.Label + "': " + error));
}

public sealed class EditorHistoryService
{
    // The actual journal is a single RainWright.dll service. Only game-side
    // reconciliation and presentation invalidation remain in the adapter.
    private readonly EditorHistoryJournal<EditorDocumentKey, IEditorHistoryEntry> journal;
    private int lastPublishedInvalidationFrame = int.MinValue;

    public EditorHistoryService(int capacity)
    {
        journal = new EditorHistoryJournal<EditorDocumentKey, IEditorHistoryEntry>(
            capacity,
            entry => entry.Label,
            (label, entries) => new CompositeHistoryEntry(label, entries),
            PublishChange);
    }

    public bool CanUndo => journal.CanUndo;
    public bool CanRedo => journal.CanRedo;
    public string UndoLabel => journal.UndoLabel;
    public string RedoLabel => journal.RedoLabel;
    public long Revision => journal.Revision;
    internal long AuthoringRevision => journal.AuthoringRevision;

    public void ActivateDocument(EditorDocumentKey document) => journal.ActivateDocument(document);
    public IDisposable BeginBatch(string label) => journal.BeginBatch(label);
    public void Push(IEditorHistoryEntry entry) => journal.Push(entry);

    public bool Undo(EditorSession session)
    {
        if (!journal.Undo(
            entry => entry.Undo(session),
            (entry, error) => EditorDiagnostics.Warning(
                "DevTool undo failed for '" + entry.Label +
                "'; history stack was left unchanged. " + error)))
            return false;
        DevToolSubsystemCoordinator.ReconcileRuntimeAfterHistoryRestore(session);
        return true;
    }

    public bool Redo(EditorSession session)
    {
        if (!journal.Redo(
            entry => entry.Redo(session),
            (entry, error) => EditorDiagnostics.Warning(
                "DevTool redo failed for '" + entry.Label +
                "'; history stack was left unchanged. " + error)))
            return false;
        DevToolSubsystemCoordinator.ReconcileRuntimeAfterHistoryRestore(session);
        return true;
    }

    public void ClearActive() => journal.ClearActive();

    private void PublishChange(bool modelMayHaveChanged)
    {
        // History used to be converted into presentation revisions later, inside CorePresentation.
        // That was too late for the legacy-visual suppression pass, which intentionally runs before
        // snapshot publication. Undo/Redo or a completed legacy transaction could therefore create
        // or refresh vanilla nodes in this frame while suppression still saw the previous workspace
        // revision. Publish the document-wide invalidation at the authoritative mutation point.
        // Constructor/document bootstrap bumps are ignored until this history service is the live
        // session's service; the first presentation is a full capture anyway.
        EditorSession session = DevToolSessionHub.Current;
        if (session == null || !ReferenceEquals(session.History, this))
            return;

        // A command batch can legitimately push several small history entries before presentation
        // runs once at the end of the frame. One Shell/document-family invalidation is sufficient for
        // all of them. Keep History.Revision exact for stack/UI semantics, but coalesce only the
        // downstream presentation clocks so the revision layer does not reintroduce duplicate dirties.
        int frame = Time.frameCount;
        if (lastPublishedInvalidationFrame != frame)
        {
            EditorRevisionHub.Mark(session, EditorRevisionKind.Shell);
            EditorRevisionHub.MarkWorkspace(session);
            lastPublishedInvalidationFrame = frame;
        }

        // Always advance the retained History key to the exact final stack revision, even when the
        // presentation invalidation above was coalesced. Otherwise CorePresentation would see the
        // second/third push as a new edge and publish the same semantic dirty again later this frame.
        EditorPresentationHub.ObservePublishedHistoryRevision(session, journal.Revision);

        // A successful model-history mutation also means that any retained vanilla screen-space
        // representation may now be stale. Exact migrated/quiescent pages register one deferred full
        // refresh. If defer is refused because Sound/Trigger is currently in explicit Vanilla mode or
        // owns opaque third-party DevUI, refresh that compatibility presentation immediately instead.
        if (modelMayHaveChanged &&
            !LegacyDevUiQuiescenceController.TryDeferRefresh(session))
        {
            NativeLegacyPresentationInvalidation.RefreshCurrentSoundOrTriggerFallback(session);
        }
    }

}
