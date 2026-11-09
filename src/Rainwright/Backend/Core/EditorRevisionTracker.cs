using System;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Rainwright-owned semantic revision channels; EditorRevisionHub remains a
/// game-side session adapter until its session owner migrates. All frontends
/// consume one identity and one invalidation clock implementation.
/// </summary>
internal enum EditorRevisionKind
{
    Shell,
    Objects,
    Room,
    Sound,
    Triggers,
    Map,
    Dialog,
    Relationships,
    Count
}

/// <summary>
/// Main-thread revision clock owned by one EditorSession.
///
/// Revisions are intentionally session-local rather than global. Reopening DevUI creates a fresh
/// session with a fresh set of revisions, so presentation caches can use session identity plus one
/// channel number as a complete invalidation key. Values never use zero: zero is reserved by
/// consumers as the "not observed" sentinel.
/// </summary>
internal sealed class EditorRevisionTracker
{
    private readonly long[] revisions = new long[(int)EditorRevisionKind.Count];
    private bool presentationOwnershipObserved;
    private bool rebuiltPresentationWasActive;
    private bool suppressMarksUntilFirstRead;

    internal EditorRevisionTracker()
    {
        for (int i = 0; i < revisions.Length; i++)
            revisions[i] = 1L;
    }

    internal long Get(EditorRevisionKind kind)
    {
        // Returning from Vanilla/hidden/detached presentation already advances every channel once.
        // CorePresentation may immediately notice a History.Revision edge from the dormant period
        // and attempt to mark Shell + workspace again before it reads either clock. Keep that tiny
        // post-observation window idempotent, then reopen normal marking on the first revision read.
        suppressMarksUntilFirstRead = false;

        int index = (int)kind;
        return index >= 0 && index < revisions.Length ? revisions[index] : 0L;
    }

    internal void Mark(EditorRevisionKind kind)
    {
        if (suppressMarksUntilFirstRead)
            return;

        int index = (int)kind;
        if (index < 0 || index >= revisions.Length)
            return;

        revisions[index] = Next(revisions[index]);
    }

    internal void MarkWorkspace(EditorToolMode mode) => Mark(WorkspaceKind(mode));

    internal void MarkShellAndWorkspace(EditorToolMode mode)
    {
        Mark(EditorRevisionKind.Shell);
        MarkWorkspace(mode);
    }

    internal void MarkAll()
    {
        for (int i = 0; i < revisions.Length; i++)
            revisions[i] = Next(revisions[i]);
    }

    /// <summary>
    /// Called only from DevUI.Update. Frontend attachment / UI-mode / overlay-visibility flags may be
    /// written from the RWImGui side, but revision mutation remains on Rain World's main thread.
    /// Any dormant -> active transition invalidates every channel once, making edits performed while
    /// rebuilt snapshots were intentionally sleeping visible on the first returned frame.
    /// </summary>
    internal void ObservePresentationOwnership(bool rebuiltPresentationActive)
    {
        bool returningToRebuilt =
            presentationOwnershipObserved && !rebuiltPresentationWasActive && rebuiltPresentationActive;

        presentationOwnershipObserved = true;
        rebuiltPresentationWasActive = rebuiltPresentationActive;

        if (returningToRebuilt)
        {
            MarkAll();

            // ObservePresentationMode runs immediately before CorePresentation. Until that first
            // consumer reads a revision, MarkAll is the authoritative invalidation for this return
            // frame. This prevents dormant History changes from double-bumping the same channels.
            suppressMarksUntilFirstRead = true;
        }
    }

    internal static EditorRevisionKind WorkspaceKind(EditorToolMode mode) => mode switch
    {
        EditorToolMode.Objects => EditorRevisionKind.Objects,
        EditorToolMode.Room => EditorRevisionKind.Room,
        EditorToolMode.Sound => EditorRevisionKind.Sound,
        EditorToolMode.Triggers => EditorRevisionKind.Triggers,
        EditorToolMode.Map => EditorRevisionKind.Map,
        EditorToolMode.Dialog => EditorRevisionKind.Dialog,
        EditorToolMode.Relationships => EditorRevisionKind.Relationships,
        _ => EditorRevisionKind.Shell
    };

    private static long Next(long value) => value >= long.MaxValue ? 1L : value + 1L;
}

