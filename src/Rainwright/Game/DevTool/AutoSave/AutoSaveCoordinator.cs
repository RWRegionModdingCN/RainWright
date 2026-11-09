using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DryCycle.DevUI.DevTool.Commands;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Extensions;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.History;
using DryCycle.DevUI.DevTool.Map.Cartography;
using DryCycle.DevUI.DevTool.Map.PlayerMap;
using DryCycle.DevUI.DevTool.World;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Persists committed edits on the next main-thread update. Stable frames check cached revisions
/// and dirty flags only. Failed writes retain author state and retry without a per-frame IO loop.
/// </summary>
internal static class AutoSaveCoordinator
{
    // The document clock and save revisions are owned by RainWright.dll.
    private static ConditionalWeakTable<EditorSession, EditorAutoSaveSchedule<EditorDocumentKey>> schedules = new();
    private static EditorSession retainedSession;
    private static bool saving;
    private static readonly HashSet<(EditorAutoSaveSchedule<EditorDocumentKey>, EditorDocumentKey)> failedDocuments = new();
    internal static bool LastSaveFailed { get; private set; }
    internal static string LastFailure { get; private set; } = "";
    private static bool initialized;
    private static string lastStatus = string.Empty;

    internal static string LastStatus => lastStatus;

    internal static void Tick(EditorSession session)
    {
        // UI configuration is not a document save: service these requests
        // even if the session is momentarily closed during a room/process
        // transition. RWImGUI's native callback never performs file IO.
        EditorAutoSavePreferences.ProcessPendingOwnedWrites();
        EditorShortcutGameplayPolicy.ProcessPendingOwnedWrites();
        if (session == null || !DevToolSessionHub.IsCurrentSessionLive) return;
        retainedSession = session;
        if (!initialized)
        {
            RecoverySnapshotStore.Begin();
            initialized = true;
        }

        DateTime now = DateTime.UtcNow;
        var schedule = Schedule(session);
        if (schedule.TryPoll(now)) RecoverySnapshotStore.ProcessRequests();
        if (RecoverySnapshotStore.RestoreRequiresReload || !EditorAutoSavePreferences.Enabled)
            return;

        // A committed text field may retain keyboard focus. Only unfinished model transactions
        // and pointer gestures defer persistence; ImGui focus is not itself an unsaved edit.
        if (session.LegacyTransactions.HasPendingTransaction ||
            EditorContinuousTransactionHub.IsActive(session) ||
            UnityEngine.Input.GetMouseButton(0))
            return;
        SavePending(session, now, false, "committed edit");
    }

    private static EditorAutoSaveSchedule<EditorDocumentKey> Schedule(EditorSession session) =>
        schedules.GetValue(session, _ => new EditorAutoSaveSchedule<EditorDocumentKey>());

    private static bool SavePending(EditorSession session, DateTime now, bool force, string reason)
    {
        if (session == null || saving || RecoverySnapshotStore.RestoreRequiresReload) return failedDocuments.Count == 0;
        var schedule = Schedule(session);
        var state = schedule.Observe(session.DocumentKey, 0, EditorAutoSavePreferences.IntervalSeconds, now);
        long authoringRevision = session.History.AuthoringRevision;
        bool includeActiveDocument = state.SavedHistoryRevision != authoringRevision || HasActiveUnsavedState(session);
        if (!includeActiveDocument && !HasWorldUnsavedState()) return true;
        int worldRevision = WorldTextRegistry.AuthoringRevision;
        if (!force && state.AttemptFailed && now < state.RetryAfterUtc &&
            state.AttemptedHistoryRevision == authoringRevision && state.AttemptedWorldRevision == worldRevision) return false;
        state.AttemptedHistoryRevision = authoringRevision;
        state.AttemptedWorldRevision = worldRevision;
        bool success = false;
        saving = true;
        try
        {
            // Room persistence resolves the real author destination itself, including when the
            // read path is a merged cache. Never require a global, manually selected Mod here.
            success = EditorActions.SavePending(session, includeActiveDocument);
            lastStatus = (success ? "Saved " : "Save failed: ") + session.DocumentKey + " (" + reason + ")";
        }
        catch (Exception error)
        { lastStatus = "Save failed: " + error.Message; EditorDiagnostics.Error("[DevTool.AutoSave] " + reason, error); }
        finally { saving = false; }
        state.AttemptFailed = !success;
        state.RetryAfterUtc = now.AddSeconds(2);
        if (!success)
        {
            failedDocuments.Add((schedule, session.DocumentKey));
            LastSaveFailed = true;
            LastFailure = lastStatus;
            Capture(session);
            EditorDiagnostics.Warning("[DevTool.AutoSave] " + lastStatus);
        }
        return success;
    }

    internal static bool FlushOnExit(EditorSession session = null, string reason = "editor exit")
    {
        session ??= retainedSession ?? DevToolSessionHub.Current;
        if (session == null) return true;
        try
        {
            if (!initialized) { RecoverySnapshotStore.Begin(); initialized = true; }
            session.LegacyTransactions.CommitPending(session);
            EditorContinuousTransactionHub.CommitAny(session);
            bool success = SavePending(session, DateTime.UtcNow, true, reason);
            if (success && ReferenceEquals(retainedSession, session)) retainedSession = null;
            return success;
        }
        catch (Exception error)
        {
            failedDocuments.Add((Schedule(session), session.DocumentKey));
            LastSaveFailed = true;
            LastFailure = "Exit save failed: " + error.Message;
            Capture(session);
            EditorDiagnostics.Error("[DevTool.AutoSave] Exit save failed: " + reason, error);
            return false;
        }
    }

    private static bool HasWorldUnsavedState() => WorldTextRegistry.Dirty ||
               WorldRoomAttractionRegistry.Dirty ||
               WorldTopologyRegistry.Dirty ||
               EditorAuthoringParticipants.HasDirty;

    private static bool HasActiveUnsavedState(EditorSession session) =>
               session.DocumentKey.Kind == EditorDocumentKind.Room && Background.BackgroundEditorRuntime.HasPendingChanges(session) ||
               session.DocumentKey.Kind == EditorDocumentKind.RegionMap && (PlayerMapWorkspaceRuntime.IsDirty(session) || Map.NativeMapAuthoringStateHub.HasPendingPositionSave(session)) ||
               CartographyRuntime.ActiveFor(session) && CartographyRuntime.HasPendingAuthorChanges(session);

    private static void Capture(EditorSession session)
    {
        string document = session.DocumentKey.ToString();
        try
        {
            if (session.DocumentKey.Kind == EditorDocumentKind.Room)
            {
                if (!Background.BackgroundEditorRuntime.CaptureRecovery(session, document))
                    RecoverySnapshotStore.CaptureRoom(document, session.RoomSettings);
            }
            else if (CartographyRuntime.ActiveFor(session))
            {
                if (CartographyRuntime.TryGetRecoveryText(session, out string path, out string content))
                    RecoverySnapshotStore.CaptureText(document + "/Cartography", path, content);
            }
            else if (session.DocumentKey.Kind == EditorDocumentKind.Relationships)
            {
                if (RelationshipSaveAdapter.TryGetText(out string path, out string text))
                    RecoverySnapshotStore.CaptureText(document + "/relationships", path, text);
            }
            // World author stores can remain dirty after switching away from the Map page.
            {
                if (WorldTextRegistry.TryGetRecoveryText(out string path, out string text))
                    WorldFileVariantStore.CaptureRecovery(document, path, text, RecoverySnapshotStore.CaptureText);
                if (WorldRoomAttractionRegistry.TryGetRecoveryText(out path, out text))
                    RecoverySnapshotStore.CaptureText(document + "/Properties.txt", path, text);
                if (WorldTopologyRegistry.TryGetRecoveryText(out path, out text))
                    RecoverySnapshotStore.CaptureText(document + "/WorldTopology", path, text);
                if (!EditorAuthoringParticipants.CaptureDirty((id, sourcePath, content) =>
                        RecoverySnapshotStore.CaptureText(document + "/" + id, sourcePath, content)))
                    EditorDiagnostics.Error(
                        "[DevTool.AutoSave] Optional author data recovery is incomplete: " +
                        EditorAuthoringParticipants.LastError);
                if (session.DocumentKey.Kind == EditorDocumentKind.RegionMap && PlayerMapWorkspaceRuntime.TryGetRecoveryText(session, out path, out text))
                    RecoverySnapshotStore.CaptureText(document + "/PlayerMap", path, text);
            }
        }
        catch (Exception error)
        {
            EditorDiagnostics.Error("[DevTool.AutoSave] Capture failed for " + document, error);
        }
    }

    internal static void ObserveSaveResult(EditorSession session, bool success)
    {
        if (session == null) return;
        LastSaveFailed = !success;
        lastStatus = (success ? "Saved " : "Save failed: ") + session.DocumentKey;
        if (!success) { LastFailure = lastStatus; return; }
        failedDocuments.Remove((Schedule(session), session.DocumentKey));
        LastSaveFailed = failedDocuments.Count > 0;
        if (!LastSaveFailed) LastFailure = "";
        Schedule(session).ObserveSuccessfulSave(
            session.DocumentKey, session.History.AuthoringRevision,
            EditorAutoSavePreferences.IntervalSeconds, DateTime.UtcNow);
    }

    internal static void Shutdown()
    {
        FlushOnExit(reason: "backend shutdown");
        // Last managed-main-thread opportunity to persist a settings request
        // submitted immediately before DevTools was closed. Do not carry it
        // into a later session with a potentially different config root.
        EditorAutoSavePreferences.ProcessPendingOwnedWrites();
        EditorShortcutGameplayPolicy.ProcessPendingOwnedWrites();
        RecoverySnapshotStore.End(failedDocuments.Count > 0);
        initialized = false;
        lastStatus = string.Empty;
        retainedSession = null;
        failedDocuments.Clear();
        LastSaveFailed = false;
        LastFailure = "";
        schedules = new();
    }
}
