using System;
using System.Runtime.CompilerServices;
using DevInterface;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.Input;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Logical data channels for the rebuilt DevTool. A revision changes only when the semantic data
/// consumed by that presentation channel may have changed. Presentation hubs can therefore skip
/// rebuilding immutable snapshots on stable frames without polling every model field.
/// </summary>
/// <summary>
/// Associates revision clocks with EditorSession without expanding the public session surface.
/// All mutation calls happen on Rain World's main thread; the ConditionalWeakTable only provides
/// lifetime ownership so closing DevUI cannot leave revision state rooted forever.
/// </summary>
internal static class EditorRevisionHub
{
    private static ConditionalWeakTable<EditorSession, EditorRevisionTracker> trackers = new();

    internal static long Get(EditorSession session, EditorRevisionKind kind) =>
        session == null ? 0L : Tracker(session).Get(kind);

    internal static void Mark(EditorSession session, EditorRevisionKind kind)
    {
        if (session == null) return;
        Tracker(session).Mark(kind);
    }

    /// <summary>
    /// Invalidates every workspace that can be mutated by the active document's shared history
    /// stack. Room, Objects, Sound and Triggers deliberately share one Room document history; an
    /// Undo executed while another one of those tools is visible can therefore restore data owned by
    /// an inactive presentation hub. Advancing the whole document family keeps those retained caches
    /// correct without rebuilding them until the user actually returns to that tool.
    /// </summary>
    internal static void MarkWorkspace(EditorSession session)
    {
        if (session == null) return;

        EditorRevisionTracker tracker = Tracker(session);
        switch (session.DocumentKey.Kind)
        {
            case EditorDocumentKind.Room:
                tracker.Mark(EditorRevisionKind.Objects);
                tracker.Mark(EditorRevisionKind.Room);
                tracker.Mark(EditorRevisionKind.Sound);
                tracker.Mark(EditorRevisionKind.Triggers);
                break;
            case EditorDocumentKind.RegionMap:
                tracker.Mark(EditorRevisionKind.Map);
                break;
            case EditorDocumentKind.Relationships:
                tracker.Mark(EditorRevisionKind.Relationships);
                break;
            default:
                tracker.MarkWorkspace(session.ToolMode);
                break;
        }
    }

    /// <summary>
    /// Narrow invalidation for a caller that explicitly owns one workspace channel rather than a
    /// document-wide History.Revision edge.
    /// </summary>
    internal static void MarkWorkspace(EditorSession session, EditorToolMode mode)
    {
        if (session == null) return;
        Tracker(session).MarkWorkspace(mode);
    }

    internal static void MarkShellAndWorkspace(EditorSession session) =>
        MarkShellAndWorkspace(session, session?.ToolMode ?? EditorToolMode.Room);

    internal static void MarkShellAndWorkspace(EditorSession session, EditorToolMode mode)
    {
        if (session == null) return;
        Tracker(session).MarkShellAndWorkspace(mode);
    }

    internal static void MarkAll(EditorSession session)
    {
        if (session == null) return;
        Tracker(session).MarkAll();
    }

    /// <summary>
    /// True only while the rebuilt surface is actually drawable. The session must still be the live
    /// RainWorldGame DevUI owner; toggling DevTools off leaves the DevUI object allocated in vanilla,
    /// so Owner != null alone is not a sufficient lifetime check. Full Vanilla mode, a detached
    /// RWImGui bridge and Escape-hidden overlay periods likewise put immutable snapshot production
    /// to sleep. Re-entry is handled by ObservePresentationMode on the main thread.
    /// </summary>
    internal static bool IsRebuiltPresentationActive(EditorSession session) =>
        session != null &&
        ReferenceEquals(DevToolSessionHub.Current, session) &&
        DevToolSessionHub.IsCurrentSessionLive &&
        EditorInputRouter.FrontendAttached &&
        !EditorUiModeState.UseVanilla &&
        !EditorUiModeState.OverlayHidden;

    /// <summary>
    /// Main-thread ownership observation. Call once per DevUI update before any presentation hub
    /// reads revisions.
    /// </summary>
    internal static void ObservePresentationMode(EditorSession session)
    {
        if (session == null) return;
        Tracker(session).ObservePresentationOwnership(IsRebuiltPresentationActive(session));
    }

    /// <summary>
    /// Reports whether the currently visible rebuilt workspace must be treated as an opaque live
    /// writer. Dormant presentation periods are excluded because all channels are invalidated once
    /// when rebuilt ownership returns. Active pointer/text transactions stay live. An explicitly
    /// visible legacy panel on one of the exact migrated vanilla pages can remain cached while idle
    /// only when no opaque third-party subtree has been observed on that page. Unknown/custom pages
    /// and migrated pages containing foreign controls remain conservative continuous writers.
    /// </summary>
    internal static bool RequiresLiveWorkspaceRefresh(EditorSession session)
    {
        if (!IsRebuiltPresentationActive(session))
            return false;

        if (session.LegacyTransactions.HasPendingTransaction)
            return true;

        // NativeToolAnchorPage contains no legacy authoring controls. Treating it as an unknown,
        // non-quiescent page would dirty the workspace every frame and continuously invalidate
        // retained native presentations, including the relationship matrix.
        if (NativeToolScheduler.IsVirtualToolActive(session))
            return false;

        Page page = session.Owner?.activePage;
        if (session.LegacyUiVisible &&
            IsExactMigratedPage(session) &&
            !LegacyDevUiQuiescenceController.HasExternalCompatibilityNodes(page))
            return false;

        return !LegacyDevUiQuiescenceController.IsQuiescent(session.Owner);
    }

    private static bool IsExactMigratedPage(EditorSession session)
    {
        Page page = session?.Owner?.activePage;
        if (page == null) return false;

        Type runtimeType = page.GetType();
        return session.ToolMode switch
        {
            EditorToolMode.Room => runtimeType == typeof(RoomSettingsPage),
            EditorToolMode.Objects => runtimeType == typeof(ObjectsPage),
            EditorToolMode.Sound => runtimeType == typeof(SoundPage),
            EditorToolMode.Triggers => runtimeType == typeof(TriggersPage),
            EditorToolMode.Map => runtimeType == typeof(MapPage),
            EditorToolMode.Dialog => runtimeType == typeof(DialogPage),
            EditorToolMode.Relationships => runtimeType == typeof(RelationshipPage),
            _ => false
        };
    }

    internal static void Reset() =>
        trackers = new ConditionalWeakTable<EditorSession, EditorRevisionTracker>();

    private static EditorRevisionTracker Tracker(EditorSession session) =>
        trackers.GetValue(session, _ => new EditorRevisionTracker());
}
