using System;
using System.Runtime.CompilerServices;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.History;

/// <summary>
/// Owns long-lived interactive edits such as native gizmo drags. Intermediate preview mutations are
/// applied directly by the owning authoring service and publish normal Revision hints, but History is
/// committed exactly once from the captured before-state to the final model state.
///
/// This deliberately does not suppress EditorHistoryService.Push globally. A continuous interaction
/// must use a preview mutation path that does not create history entries; unrelated commands remain
/// safe even if they execute while a pointer transaction is active.
/// </summary>
internal static class EditorContinuousTransactionHub
{
    private static readonly EditorContinuousEditRegistry<EditorSession, EditorDocumentKey, IEditorStateSnapshot>
        registry = new();

    internal static bool Begin(
        EditorSession session,
        string key,
        string label,
        IEditorStateSnapshot before)
    {
        return session != null &&
            registry.Begin(session, key, label, session.DocumentKey, before);
    }

    internal static bool IsActive(EditorSession session, string key = null)
    {
        return registry.IsActive(session, key);
    }

    internal static bool Commit(EditorSession session, string key)
    {
        if (!registry.TryTake(session, key, out string label,
                out EditorDocumentKey document, out IEditorStateSnapshot before))
            return false;

        if (!document.Equals(session.DocumentKey))
            return false;

        IEditorStateSnapshot after;
        try
        {
            after = before.CaptureCurrent(session);
        }
        catch (Exception error)
        {
            EditorDiagnostics.Warning("DevTool continuous transaction capture failed: " + error);
            return false;
        }

        if (!SnapshotHistoryEntry.TryCreate(label, before, after, out SnapshotHistoryEntry entry))
            return false;

        session.History.Push(entry);
        return true;
    }

    internal static bool Cancel(EditorSession session, string key)
    {
        if (!registry.TryTake(session, key, out _,
                out EditorDocumentKey document, out IEditorStateSnapshot before))
            return false;
        if (!document.Equals(session.DocumentKey))
            return false;

        try
        {
            return before.Restore(session);
        }
        catch (Exception error)
        {
            EditorDiagnostics.Warning("DevTool continuous transaction rollback failed: " + error);
            return false;
        }
    }

    internal static void CommitAny(EditorSession session)
    {
        string key = registry.ActiveKey(session);
        if (key != null) Commit(session, key);
    }

    internal static void CancelAny(EditorSession session)
    {
        string key = registry.ActiveKey(session);
        if (key != null)
            Cancel(session, key);
    }

    internal static void Reset() => registry.Reset();
}
