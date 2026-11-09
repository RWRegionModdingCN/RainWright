using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DevInterface;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.Dialog;
using DryCycle.DevUI.DevTool.Factories;
using DryCycle.DevUI.DevTool.History;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Objects;
using DryCycle.DevUI.DevTool.Relationships;
using DryCycle.DevUI.DevTool.Room;
using DryCycle.DevUI.DevTool.Sound;
using DryCycle.DevUI.DevTool.Triggers;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Owns the lifetime of the new editor model. Vanilla DevInterface remains alive as a
/// compatibility backend; presentation is supplied by the optional RWImGui frontend.
/// DevTool does not discover, reference or call third-party mod APIs.
/// </summary>
internal static class DevToolRuntime
{
    private static bool enabled;
    // Set before HookGen add: a partially installed handler is still owned.
    // Do not permit a second add while cleanup is unconfirmed.
    private static bool updateHookInstallAttempted;
    private static bool cleanupPending;
    private static readonly HashSet<string> FaultedDevUiStages =
        new(StringComparer.Ordinal);
    private static bool sessionSynchronizationFailureLogged;
    private static bool presentationFailureLogged;

    internal static EditorSession ActiveSession => DevToolSessionHub.Current;

    internal static void Enable()
    {
        if (enabled) return;
        if (updateHookInstallAttempted || cleanupPending)
            throw new InvalidOperationException(
                "DevTool core or optional services have pending cleanup; refusing duplicate installation.");

        // Record ownership before HookGen add. If it throws after a partial
        // subscription, the Rainwright lifecycle ledger must still call Disable.
        updateHookInstallAttempted = true;
        On.DevInterface.DevUI.Update += DevUI_Update;
        enabled = true;
        FaultedDevUiStages.Clear();
        sessionSynchronizationFailureLogged = false;
        presentationFailureLogged = false;

        TryEnableOptionalSubsystem(
            "NativeObjectInspectorBootstrap",
            NativeObjectInspectorBootstrap.Enable);

        TryEnableOptionalSubsystem(
            "EditorInputRouter",
            EditorInputRouter.Enable);


    }

    internal static void Disable()
    {
        if (!enabled && !updateHookInstallAttempted && !cleanupPending) return;

        // A failed HookGen removal is NOT success: preserve the producer,
        // report the original exception to the Rainwright service ledger and
        // retry cleanup before any later Enable.
        if (updateHookInstallAttempted)
        {
            try
            {
                On.DevInterface.DevUI.Update -= DevUI_Update;
            }
            catch (Exception error)
            {
                EditorDiagnostics.Error(
                    "DevTool core DevUI.Update hook removal failed; retained for retry.", error);
                throw;
            }
            updateHookInstallAttempted = false;
        }
        enabled = false;
        FaultedDevUiStages.Clear();
        sessionSynchronizationFailureLogged = false;
        presentationFailureLogged = false;

        bool complete = true;

        complete &= TryDisableOptionalSubsystem("LegacyUiPresentationController", LegacyUiPresentationController.Reset);
        complete &= TryDisableOptionalSubsystem("EditorInputRouter", EditorInputRouter.Disable);
        complete &= TryDisableOptionalSubsystem("AutoSaveCoordinator", AutoSaveCoordinator.Shutdown);
        complete &= TryDisableOptionalSubsystem("BackgroundEditor", Background.BackgroundEditorRuntime.Shutdown);
        complete &= TryDisableOptionalSubsystem("DevToolSubsystemCoordinator", DevToolSubsystemCoordinator.ResetRuntimeState);
        cleanupPending = !complete;
        if (cleanupPending)
            throw new InvalidOperationException(
                "DevTool optional editor cleanup remains incomplete; retained for retry.");
    }

    private static void TryEnableOptionalSubsystem(string name, Action action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception error)
        {
            EditorDiagnostics.Error(
                "DevTool optional subsystem '" + name +
                "' failed during enable; core editor session/presentation remains active. " +
                error);
        }
    }

    private static bool TryDisableOptionalSubsystem(string name, Action action)
    {
        try
        {
            action?.Invoke();
            return true;
        }
        catch (Exception error)
        {
            EditorDiagnostics.Warning(
                "DevTool optional subsystem '" + name +
                "' failed during cleanup: " + error);
            return false;
        }
    }

    private static void ProcessEditorCommands(EditorSession session)
    {
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.CommandProcessing))
        {
            RunOptionalDevUiStage(
                "CommandProcessing",
                () =>
                {
                    DevToolSubsystemCoordinator.ProcessPendingCommands(session);
                    session?.SynchronizeSelectionValidity();

                });
        }

    }

    private static bool RunOptionalDevUiStage(string name, Action action)
    {
        if (FaultedDevUiStages.Contains(name))
            return false;

        try
        {
            action?.Invoke();
            return true;
        }
        catch (Exception error)
        {
            FaultedDevUiStages.Add(name);
            EditorDiagnostics.Error(
                "DevTool DevUI stage '" + name +
                "' failed and has been isolated until the DevTool runtime is restarted. " +
                error);
            return false;
        }
    }

    private static void DevUI_Update(On.DevInterface.DevUI.orig_Update orig, global::DevInterface.DevUI self)
    {
        if (self == null)
        {
            orig(self);
            return;
        }

        using DevToolPerformanceMonitor.Scope totalScope =
            DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.DevUiUpdateTotal);

        // SessionHub is the only hard prerequisite for rebuilt presentation. If it fails, preserve
        // vanilla DevUI and make the primary exception explicit instead of letting the hook abort
        // the original editor update.
        try
        {
            using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.SessionSynchronization))
                DevToolSessionHub.Synchronize(self);

            sessionSynchronizationFailureLogged = false;
        }
        catch (Exception error)
        {
            if (!sessionSynchronizationFailureLogged)
            {
                sessionSynchronizationFailureLogged = true;
                EditorDiagnostics.Error(
                    "DevTool core SessionHub synchronization failed; falling back to vanilla DevUI for this frame. " +
                    error);
            }

            orig(self);
            return;
        }

        EditorSession session = DevToolSessionHub.Current;



        RunOptionalDevUiStage(
            "NativeToolScheduler.SynchronizePresentationOwnership",
            () => NativeToolScheduler.SynchronizePresentationOwnership(session));

        bool restoredWorkspaceThisFrame = false;
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.DeferredWorkspaceRestore))
        {
            RunOptionalDevUiStage(
                "DeferredWorkspaceRestore",
                () => restoredWorkspaceThisFrame = session?.ApplyDeferredViewRestore() == true);
        }

        // Consume accepted New-UI edits before native button handlers, never over their result.
        // Queue budgets remain unchanged and no pending author edit/history is discarded.
        bool commandsBeforeLegacy = EditorUiModeState.UseVanilla || session?.LegacyUiVisible == true;
        if (commandsBeforeLegacy)
            ProcessEditorCommands(session);
        if (EditorUiModeState.UseVanilla || session?.LegacyUiVisible == true)
        {
            session?.CancelPlacement();
            RunOptionalDevUiStage("RestoreLegacyPresentation", () =>
            {
                LegacyUiPresentationController.Restore(self.activePage);
                WorldMapFrontendBridge.RestoreLegacyPresentation(session);
            });
        }

        bool legacyTransactionBeforeSucceeded;
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.LegacyTransactionBefore))
        {
            legacyTransactionBeforeSucceeded =
                RunOptionalDevUiStage(
                    "LegacyTransaction.BeforeLegacyUpdate",
                    () => session?.LegacyTransactions.BeforeLegacyUpdate(session));
        }

        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.InputShortcuts))
        {
            RunOptionalDevUiStage(
                "EditorInputRouter.UpdateShortcuts",
                () => EditorInputRouter.UpdateShortcuts(session));
        }

        // The native Sound/Trigger scheduler already fails open to vanilla internally.
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.VanillaDevUiUpdate))
        {
            if (!NativeSoundTriggerDevUiScheduler.TryRun(self))
                orig(self);
        }

        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.PostLegacySynchronization))
        {
            RunOptionalDevUiStage(
                "PostLegacySynchronization",
                () =>
                {
                    session?.SynchronizeSelectionFromLegacyNode(self.draggedNode);
                    session?.SynchronizeAfterLegacyUpdate(self);
                    if (legacyTransactionBeforeSucceeded)
                        session?.LegacyTransactions.AfterLegacyUpdate(session);
                });
        }

        if (!commandsBeforeLegacy)
            ProcessEditorCommands(session);

        RunOptionalDevUiStage("AutoSaveCoordinator.Tick", () => AutoSaveCoordinator.Tick(session));

        bool suppressMigratedLegacyUi =
            !EditorUiModeState.UseVanilla &&
            EditorInputRouter.FrontendAttached &&
            session != null &&
            session.LegacyUiVisible == false &&
            (NativeToolScheduler.IsVirtualToolActive(session) ||
             (session.ToolMode == EditorToolMode.Objects && self.activePage is ObjectsPage) ||
             (session.ToolMode == EditorToolMode.Room && self.activePage is RoomSettingsPage) ||
             (session.ToolMode == EditorToolMode.Sound && self.activePage is SoundPage) ||
             (session.ToolMode == EditorToolMode.Triggers && self.activePage is TriggersPage) ||
             (session.ToolMode == EditorToolMode.Map && self.activePage is MapPage) ||
             (session.ToolMode == EditorToolMode.Dialog && self.activePage is DialogPage) ||
             (session.ToolMode == EditorToolMode.Relationships && self.activePage is RelationshipPage));

        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.LegacyPresentation))
        {
            RunOptionalDevUiStage(
                "LegacyUiPresentationController.Apply",
                () => LegacyUiPresentationController.Apply(
                    self.activePage,
                    suppressMigratedLegacyUi));
        }

        bool shellOnly =
            session?.IsOpeningFrame == true ||
            restoredWorkspaceThisFrame;

        try
        {
            PublishPresentations(session, shellOnly);
            presentationFailureLogged = false;
        }
        catch (Exception error)
        {
            if (!presentationFailureLogged)
            {
                presentationFailureLogged = true;
                EditorDiagnostics.Error(
                    "DevTool presentation publication failed; RWImGui will keep the backend-waiting/core shell visible while publication retries. " +
                    error);
            }
        }
    }

    private static void PublishPresentations(EditorSession session, bool shellOnly)
    {
        EditorRevisionHub.ObservePresentationMode(session);

        if (session == null)
        {
            EditorPresentationHub.Clear();
            DevToolSubsystemCoordinator.ClearDetailPresentations();
            return;
        }

        if (!EditorRevisionHub.IsRebuiltPresentationActive(session))
            return;

        PublishCorePresentation(session, shellOnly);

        if (shellOnly)
        {
            DevToolSubsystemCoordinator.ClearDetailPresentations(session);
            return;
        }

        switch (session.ToolMode)
        {
            case EditorToolMode.Room:
                PublishRoomPresentation(session);
                break;
            case EditorToolMode.Sound:
                PublishSoundPresentation(session);
                break;
            case EditorToolMode.Triggers:
                PublishTriggerPresentation(session);
                break;
            case EditorToolMode.Map:
                PublishMapPresentation(session);
                break;
            case EditorToolMode.Dialog:
                PublishDialogPresentation(session);
                break;
            case EditorToolMode.Relationships:
                PublishRelationshipPresentation(session);
                break;
            case EditorToolMode.Objects:
                break;
        }
    }

    private static void PublishCorePresentation(EditorSession session, bool shellOnly)
    {
        bool monitor = DevToolPerformanceMonitor.Enabled;
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.CorePresentation))
            EditorPresentationHub.Publish(session, shellOnly);

        if (monitor && DevToolPerformanceMonitor.Enabled)
            DevToolPerformanceMonitor.RecordPresentation(
                DevToolPresentationChannel.Core,
                EditorPresentationHub.LastOutcome);
    }

    private static void PublishRoomPresentation(EditorSession session)
    {
        bool monitor = DevToolPerformanceMonitor.Enabled;
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.RoomPresentation))
            RoomEditorPresentationHub.Publish(session);
        if (monitor && DevToolPerformanceMonitor.Enabled)
            DevToolPerformanceMonitor.RecordPresentation(
                DevToolPresentationChannel.Room,
                RoomEditorPresentationHub.LastOutcome);
    }

    private static void PublishSoundPresentation(EditorSession session)
    {
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.SoundPresentation))
            SoundEditorPresentationHub.Publish(session);
        // SoundEditorPresentationHub owns Hit/Partial/Full classification because it can distinguish
        // selection-only and member-level patch paths that are invisible from snapshot identity.
    }

    private static void PublishTriggerPresentation(EditorSession session)
    {
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.TriggerPresentation))
            TriggerEditorPresentationHub.Publish(session);
        // TriggerEditorPresentationHub likewise reports its own granular outcome.
    }

    private static void PublishMapPresentation(EditorSession session)
    {
        using var slowTiming = EditorSlowOperation.Measure("Map.Open.Presentation");
        bool monitor = DevToolPerformanceMonitor.Enabled;
        EditorMapPresentationSnapshot before = monitor ? MapEditorPresentationHub.Current : null;

        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.MapPresentation))
            MapEditorPresentationHub.Publish(session);

        if (!monitor || !DevToolPerformanceMonitor.Enabled) return;
        EditorMapPresentationSnapshot after = MapEditorPresentationHub.Current;
        DevToolPresentationOutcome outcome;
        if (ReferenceEquals(before, after))
        {
            outcome = DevToolPresentationOutcome.CacheHit;
        }
        else if (before?.Available == true && after?.Available == true &&
                 ReferenceEquals(before.Connections, after.Connections))
        {
            outcome = DevToolPresentationOutcome.PartialRebuild;
        }
        else
        {
            outcome = DevToolPresentationOutcome.FullRebuild;
        }

        DevToolPerformanceMonitor.RecordPresentation(DevToolPresentationChannel.Map, outcome);
    }

    private static void PublishDialogPresentation(EditorSession session)
    {
        bool monitor = DevToolPerformanceMonitor.Enabled;
        EditorDialogPresentationSnapshot before = monitor ? DialogEditorPresentationHub.Current : null;
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.DialogPresentation))
            DialogEditorPresentationHub.Publish(session);
        if (monitor && DevToolPerformanceMonitor.Enabled)
            RecordSimplePresentation(DevToolPresentationChannel.Dialog, before, DialogEditorPresentationHub.Current);
    }

    private static void PublishRelationshipPresentation(EditorSession session)
    {
        bool monitor = DevToolPerformanceMonitor.Enabled;
        using (DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.RelationshipPresentation))
            RelationshipEditorPresentationHub.Publish(session);
        if (monitor && DevToolPerformanceMonitor.Enabled)
            DevToolPerformanceMonitor.RecordPresentation(
                DevToolPresentationChannel.Relationships,
                RelationshipEditorPresentationHub.LastOutcome);
    }

    private static void RecordSimplePresentation<T>(
        DevToolPresentationChannel channel,
        T before,
        T after)
        where T : class
    {
        DevToolPerformanceMonitor.RecordPresentation(
            channel,
            ReferenceEquals(before, after)
                ? DevToolPresentationOutcome.CacheHit
                : DevToolPresentationOutcome.FullRebuild);
    }


}

public sealed class EditorSession
{
    // Unknown third-party code can mutate RoomSettings.placedObjects without going through DryCycle
    // invalidation. Known collection writes are caught immediately by the semantic collection
    // revision below; this low-frequency audit only exists for same-count opaque replacements.
    private const int SelectionAuditIntervalUpdates = 120;

    private EditorDocumentKey documentKey;
    private Page observedLegacyPage;
    private global::Room observedRoom;
    private global::World observedWorld;
    private RoomSettings observedRoomSettings;
    private int activationUpdateCount;
    private bool deferredViewRestorePending;
    private EditorToolMode deferredRestoreMode;
    private bool deferredRestoreLegacyUi;
    private List<PlacedObject> observedPlacedObjectList;
    private int observedPlacedObjectCount = -1;
    private long observedObjectCollectionRevision;
    private int selectionAuditCountdown;

    internal EditorSession(global::DevInterface.DevUI owner)
    {
        Owner = owner;
        Selection = new EditorSelection();
        History = new EditorHistoryService(64);
        LegacyTransactions = new LegacyTransactionRecorder();
        observedLegacyPage = owner?.activePage;
        ToolMode = ResolveToolMode(observedLegacyPage);
        Synchronize(owner);
        if (Enum.TryParse(RainWrightPersonalSettings.DefaultPage, out EditorToolMode initialMode) && initialMode != ToolMode)
        {
            deferredRestoreMode = initialMode;
            deferredViewRestorePending = true;
        }
    }

    public global::DevInterface.DevUI Owner { get; private set; }
    public EditorDocumentKey DocumentKey => documentKey;
    public EditorToolMode ToolMode { get; private set; }
    public EditorSelection Selection { get; }
    public EditorHistoryService History { get; }
    public LegacyTransactionRecorder LegacyTransactions { get; }
    public bool FocusMode { get; private set; }
    public bool BrowserOpen { get; private set; } = true;
    public bool InspectorOpen { get; private set; } = true;
    public bool LegacyUiVisible { get; private set; }
    public bool PlacementActive => !string.IsNullOrEmpty(PlacementType);
    public string PlacementType { get; private set; } = string.Empty;
    public string PlacementError { get; internal set; } = string.Empty;
    public string ObjectSearch { get; set; } = string.Empty;
    internal bool IsOpeningFrame => activationUpdateCount <= 1;

    public global::Room Room => Owner?.room;
    public RoomSettings RoomSettings => Room?.roomSettings;
    public global::World World => Owner?.game?.world;

    internal void Synchronize(global::DevInterface.DevUI owner)
    {
        Owner = owner;
        global::Room nextRoom = owner?.room;
        global::World nextWorld = owner?.game?.world;
        RoomSettings nextRoomSettings = nextRoom?.roomSettings;

        EditorDocumentKey next = ResolveDocument(owner);
        bool documentIdentityChanged = !next.Equals(documentKey);
        bool documentInstanceReplaced =
            !documentIdentityChanged &&
            ((next.Kind == EditorDocumentKind.Room &&
              (!ReferenceEquals(observedWorld, nextWorld) ||
               !ReferenceEquals(observedRoomSettings, nextRoomSettings))) ||
             (next.Kind == EditorDocumentKind.RegionMap &&
              !ReferenceEquals(observedWorld, nextWorld)));

        if (documentIdentityChanged)
        {
            documentKey = next;
            Selection.Clear();
            History.ActivateDocument(next);
            LegacyTransactions.Reset();
            LegacyUiVisible = false;
            CancelPlacement();
            ResetSelectionValidation();
        }
        else if (documentInstanceReplaced)
        {
            // Same logical name, new runtime model. Existing history entries may capture direct
            // PlacedObject/RoomSettings/World identities from the retired instance and must never
            // replay into the replacement object graph.
            Selection.Clear();
            History.ClearActive();
            LegacyTransactions.Reset();
            LegacyUiVisible = false;
            CancelPlacement();
            ResetSelectionValidation();
        }

        Page nextPage = owner?.activePage;
        if (!ReferenceEquals(observedLegacyPage, nextPage))
        {
            // Quiescence caches are deliberately page-keyed and use HashSet<Page> on their hottest
            // membership checks. Explicitly release the retired page before dropping our last normal
            // reference so long mapping sessions cannot retain every Page ever visited.
            LegacyDevUiQuiescenceController.ReleasePage(observedLegacyPage);
            observedLegacyPage = nextPage;
            ToolMode = ResolveToolMode(observedLegacyPage);
            LegacyTransactions.Reset();
            LegacyUiVisible = false;
            if (ToolMode != EditorToolMode.Objects) CancelPlacement();
        }

        observedRoom = nextRoom;
        observedWorld = nextWorld;
        observedRoomSettings = nextRoomSettings;
        SynchronizeSelectionValidity();
    }

    /// <summary>
    /// Vanilla DevUI can switch Page or room/world ownership inside its own Update. The old path ran
    /// a second complete Synchronize every frame solely to catch that edge. Stable frames now compare
    /// only the structural roots that can change the editor document; a real boundary still falls
    /// back to the authoritative full synchronization immediately in the same frame.
    /// </summary>
    internal void SynchronizeAfterLegacyUpdate(global::DevInterface.DevUI owner)
    {
        if (owner == null) return;

        global::Room nextRoom = owner.room;
        global::World nextWorld = owner.game?.world;
        RoomSettings nextRoomSettings = nextRoom?.roomSettings;
        Page nextPage = owner.activePage;

        bool structuralBoundary =
            !ReferenceEquals(Owner, owner) ||
            !ReferenceEquals(observedLegacyPage, nextPage) ||
            !ReferenceEquals(observedRoom, nextRoom) ||
            !ReferenceEquals(observedWorld, nextWorld) ||
            !ReferenceEquals(observedRoomSettings, nextRoomSettings);

        if (structuralBoundary)
        {
            Synchronize(owner);
            return;
        }

        // Legacy controls can still mutate placedObjects without changing any structural root.
        // Preserve immediate selection membership validation while keeping the stable path O(1).
        SynchronizeSelectionValidity();
    }

    /// <summary>
    /// Validates selected PlacedObject identities only when membership may have changed. Normal
    /// stable frames compare one list reference, one count and one semantic revision, then return.
    /// Opaque third-party same-count replacement is covered by the infrequent audit countdown.
    /// </summary>
    internal void SynchronizeSelectionValidity()
    {
        List<PlacedObject> live = RoomSettings?.placedObjects;
        int liveCount = live?.Count ?? -1;
        long collectionRevision = ObjectPresentationChangeHintHub.GetCollectionRevision(this);

        bool collectionChanged =
            !ReferenceEquals(observedPlacedObjectList, live) ||
            observedPlacedObjectCount != liveCount ||
            observedObjectCollectionRevision != collectionRevision;
        bool auditDue = selectionAuditCountdown <= 0;

        if (!collectionChanged && !auditDue)
            return;

        Selection.RemoveMissing(live);
        observedPlacedObjectList = live;
        observedPlacedObjectCount = liveCount;
        observedObjectCollectionRevision = collectionRevision;
        selectionAuditCountdown = SelectionAuditIntervalUpdates;
    }

    private void ResetSelectionValidation()
    {
        observedPlacedObjectList = null;
        observedPlacedObjectCount = -1;
        observedObjectCollectionRevision = 0L;
        selectionAuditCountdown = 0;
    }

    internal void MarkUpdateStarted()
    {
        if (activationUpdateCount < int.MaxValue)
            activationUpdateCount++;
        if (selectionAuditCountdown > 0)
            selectionAuditCountdown--;
    }

    internal void RestoreViewStateFrom(EditorSession previous)
    {
        if (previous == null) return;

        FocusMode = previous.FocusMode;
        BrowserOpen = previous.BrowserOpen;
        InspectorOpen = previous.InspectorOpen;
        ObjectSearch = previous.ObjectSearch ?? string.Empty;
        CancelPlacement();

        deferredRestoreMode = previous.ToolMode;
        deferredRestoreLegacyUi = previous.LegacyUiVisible;
        deferredViewRestorePending = deferredRestoreMode != ToolMode || deferredRestoreLegacyUi;
    }

    internal bool ApplyDeferredViewRestore()
    {
        if (EditorUiModeState.UseVanilla) { deferredViewRestorePending = false; return false; }
        if (!deferredViewRestorePending || activationUpdateCount < 2)
            return false;

        deferredViewRestorePending = false;

        if (deferredRestoreLegacyUi && NativeToolScheduler.Supports(deferredRestoreMode))
        {
            if (NativeToolScheduler.ShowExplicitLegacyTool(
                    this,
                    deferredRestoreMode))
                return true;

            EditorDiagnostics.Error(
                "DevTool deferred legacy workspace restore failed for " + deferredRestoreMode +
                "; keeping the current workspace ownership.");
            return false;
        }

        SetToolMode(deferredRestoreMode);
        LegacyUiVisible = deferredRestoreLegacyUi;
        if (LegacyUiVisible)
            LegacyUiPresentationController.Restore(Owner?.activePage);
        return true;
    }

    public void SetToolMode(EditorToolMode mode)
    {
        using var openTiming = EditorSlowOperation.Measure("Map.Open.SwitchTool", enabled: mode == EditorToolMode.Map);
        using var timing = DevToolPerformanceMonitor.Measure(DevToolPerformanceMetric.WorkspaceSwitch);
        if (Owner != null && mode != ToolMode && EditorAutoSavePreferences.Enabled)
            AutoSaveCoordinator.FlushOnExit(this, "workspace switch");
        if (mode != EditorToolMode.Objects) CancelPlacement();

        if (Owner == null)
        {
            ToolMode = mode;
            return;
        }

        // Selecting a different workspace exits per-page Legacy presentation just like the old
        // SwitchPage path did. Same-tool selection preserves an explicitly visible legacy page.
        if (ToolMode != mode && LegacyUiVisible)
            LegacyUiVisible = false;

        long soundSwitchStarted = mode == EditorToolMode.Sound
            ? System.Diagnostics.Stopwatch.GetTimestamp()
            : 0L;

        if (NativeToolScheduler.SwitchTool(this, mode))
        {
            RecordSoundWorkspaceSwitch(soundSwitchStarted);
            return;
        }

        // Objects/Sound/Triggers are lifecycle-owned by NativeToolScheduler. If its transition
        // fails, never bypass that ownership with a raw SwitchPage: doing so would reintroduce an
        // untracked concrete legacy page and can desynchronize selection/history/backend state.
        if (NativeToolScheduler.Supports(mode))
        {
            EditorDiagnostics.Error(
                "DevTool native workspace transition failed for " + mode +
                "; refusing unmanaged legacy page materialization.");
            RecordSoundWorkspaceSwitch(soundSwitchStarted);
            return;
        }

        int pageIndex = PageIndex(mode);
        if (pageIndex < 0)
        {
            ToolMode = mode;
            RecordSoundWorkspaceSwitch(soundSwitchStarted);
            return;
        }

        if (ResolveToolMode(Owner.activePage) == mode)
        {
            ToolMode = mode;
            RecordSoundWorkspaceSwitch(soundSwitchStarted);
            return;
        }

        LegacyUiPresentationController.Restore(Owner.activePage);
        LegacyTransactions.Reset();
        LegacyUiVisible = false;

        using (EditorSlowOperation.Measure("Map.Open.NativePage", enabled: mode == EditorToolMode.Map))
            Owner.SwitchPage(pageIndex);
        RecordSoundWorkspaceSwitch(soundSwitchStarted);

        // SwitchPage constructs a new concrete Page immediately and can cross document boundaries
        // (Room <-> RegionMap <-> Relationships). Reconcile now so any later command in this same
        // queue batch writes to the correct History document and presentation identity. This keeps
        // stable frames cheap because the structural pass only happens on an actual page switch.
        Synchronize(Owner);
    }

    internal void AdoptVirtualToolMode(EditorToolMode mode)
    {
        ToolMode = mode;
        LegacyUiVisible = false;
        LegacyTransactions.Reset();
        CancelPlacement();
    }

    internal void AdoptMaterializedToolMode(EditorToolMode mode, bool legacyUiVisible)
    {
        ToolMode = mode;
        LegacyUiVisible = legacyUiVisible;
        LegacyTransactions.Reset();
        if (mode != EditorToolMode.Objects)
            CancelPlacement();
    }

    private static void RecordSoundWorkspaceSwitch(long startedTimestamp)
    {
        if (startedTimestamp == 0L) return;

        double milliseconds =
            (System.Diagnostics.Stopwatch.GetTimestamp() - startedTimestamp) * 1000d /
            System.Diagnostics.Stopwatch.Frequency;
        SoundActivationPipeline.RecordPageSwitch(milliseconds);
    }

    public void BeginPlacement(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return;
        if (ToolMode != EditorToolMode.Objects) SetToolMode(EditorToolMode.Objects);
        if (Owner?.draggedNode is Handle dragged) dragged.dragged = false;
        Owner.draggedNode = null;
        PlacementType = type;
        PlacementError = string.Empty;
    }

    public void CancelPlacement() { PlacementType = string.Empty; PlacementError = string.Empty; }

    internal void SynchronizeSelectionFromLegacyNode(DevUINode node)
    {
        DevUINode current = node;
        while (current != null)
        {
            if (current is PlacedObjectRepresentation representation && representation.pObj != null)
            {
                Selection.SelectOnly(representation.pObj);
                return;
            }
            current = current.parentNode;
        }
    }

    public void ToggleFocusMode() => FocusMode = !FocusMode;
    public void SetFocusMode(bool value) => FocusMode = value;
    public void ToggleBrowser() => BrowserOpen = !BrowserOpen;
    public void ToggleInspector() => InspectorOpen = !InspectorOpen;

    public void ToggleLegacyUi()
    {
        if (ToolMode != EditorToolMode.Objects && ToolMode != EditorToolMode.Room &&
            ToolMode != EditorToolMode.Sound && ToolMode != EditorToolMode.Triggers &&
            ToolMode != EditorToolMode.Map && ToolMode != EditorToolMode.Relationships) return;

        // Objects legacy presentation is an escape hatch for a proven compatibility gap, not a
        // second authoring mode. Enforce that contract behind the command queue as well as in the
        // frontend so stale UI, extensions, or direct command injection cannot materialize a full
        // ObjectsPage for an object already covered by the rebuilt inspector/gizmo pipeline.
        // Closing an already-visible legacy page must always remain possible.
        if (ToolMode == EditorToolMode.Objects &&
            !LegacyUiVisible &&
            !CanOpenObjectLegacyUi())
            return;

        if (NativeToolScheduler.Supports(ToolMode))
        {
            bool transitioned = LegacyUiVisible
                ? NativeToolScheduler.ReturnToNativeTool(this)
                : NativeToolScheduler.ShowExplicitLegacyTool(this, ToolMode);

            if (!transitioned)
            {
                EditorDiagnostics.Error(
                    "DevTool legacy presentation transition failed for " + ToolMode +
                    "; keeping the current workspace ownership.");
            }
            return;
        }

        LegacyUiVisible = !LegacyUiVisible;
        if (LegacyUiVisible)
            LegacyUiPresentationController.Restore(Owner?.activePage);
    }

    private bool CanOpenObjectLegacyUi()
    {
        if (Selection?.Count != 1)
            return false;

        PlacedObject selected = Selection.PrimaryPlacedObject;
        if (selected?.type == null)
            return false;

        ObjectInspectorCoverage coverage = ObjectInspectorRegistry.GetCoverage(selected);
        if (!coverage.IsComplete || !coverage.GizmoComplete)
            return true;

        return !GameDefinedExtEnumCatalog.Contains(typeof(PlacedObject.Type), selected.type.value) &&
            !ManagedObjectRegistration.HasStandardRepresentation(selected);
    }

    private static int PageIndex(EditorToolMode mode)
    {
        return mode switch
        {
            EditorToolMode.Room => 0,
            EditorToolMode.Objects => 1,
            EditorToolMode.Sound => 2,
            EditorToolMode.Map => 3,
            EditorToolMode.Triggers => 4,
            EditorToolMode.Dialog => 5,
            EditorToolMode.Relationships => 6,
            _ => -1
        };
    }

    private static EditorDocumentKey ResolveDocument(global::DevInterface.DevUI ui)
    {
        if (ui?.activePage is NativeToolAnchorPage { ToolMode: EditorToolMode.Palette })
            return new EditorDocumentKey(EditorDocumentKind.Palette, "workspace");
        if (ui?.activePage is MapPage)
            return new EditorDocumentKey(EditorDocumentKind.RegionMap, ui.game?.world?.name ?? "<world>");
        if (ui?.activePage is RelationshipPage ||
            ui?.activePage is NativeToolAnchorPage
            {
                ToolMode: EditorToolMode.Relationships
            })
            return new EditorDocumentKey(EditorDocumentKind.Relationships, "global");

        global::Room room = ui?.room;
        string worldName = ui?.game?.world?.name ?? "<world>";
        string roomName = room?.abstractRoom?.name ?? room?.roomSettings?.name ?? "<room>";
        return new EditorDocumentKey(
            EditorDocumentKind.Room,
            worldName + "/" + roomName);
    }

    private static EditorToolMode ResolveToolMode(Page page)
    {
        if (page is NativeToolAnchorPage nativeAnchor)
            return nativeAnchor.ToolMode;
        if (page is ObjectsPage) return EditorToolMode.Objects;
        if (page is SoundPage) return EditorToolMode.Sound;
        if (page is TriggersPage) return EditorToolMode.Triggers;
        if (page is MapPage) return EditorToolMode.Map;
        if (page is DialogPage) return EditorToolMode.Dialog;
        if (page is RelationshipPage) return EditorToolMode.Relationships;
        if (page?.GetType().FullName == "RegionKit.Modules.BackgroundBuilder.BackgroundPage") return EditorToolMode.Background;
        return EditorToolMode.Room;
    }
}

public static class DevToolSessionHub
{
    private static ConditionalWeakTable<global::DevInterface.DevUI, EditorSession> sessions = new();
    private static WeakReference<EditorSession> current = new(null);

    public static EditorSession Current
    {
        get
        {
            current.TryGetTarget(out EditorSession session);
            return session;
        }
    }

    public static bool IsCurrentSessionLive
    {
        get
        {
            current.TryGetTarget(out EditorSession session);
            global::DevInterface.DevUI owner = session?.Owner;
            RainWorldGame game = owner?.game;
            if (game == null || !game.processActive || !game.devToolsActive)
                return false;
            if (game.devUI == null || !ReferenceEquals(game.devUI, owner))
                return false;
            if (game.manager == null || !ReferenceEquals(game.manager.currentMainLoop, game))
                return false;
            if (owner.room == null)
                return false;
            return true;
        }
    }

    internal static void Synchronize(global::DevInterface.DevUI ui)
    {
        if (ui == null) return;

        bool created = false;
        if (!sessions.TryGetValue(ui, out EditorSession session))
        {
            current.TryGetTarget(out EditorSession previous);

            // A new DevUI owner retires the previous owner's current Page even when view-state
            // restoration is not allowed (room/game transition). Release page-keyed compatibility
            // caches before constructing the replacement session so those HashSets cannot root the
            // old DevUI graph.
            if (previous != null && !ReferenceEquals(previous.Owner, ui))
            {
                LegacyDevUiQuiescenceController.ReleasePage(previous.Owner?.activePage);
            }

            bool restoreViewState = CanRestoreViewState(previous, ui);
            int previousMapRoom = -1;
            int previousUiCurrentRoom = -1;
            if (restoreViewState)
            {
                MapEditorState previousMapState = MapEditorStateHub.Get(previous);
                previousMapRoom = previousMapState?.SelectedRoomIndex ?? -1;
                previousUiCurrentRoom = previousMapState?.UiCurrentRoomIndex ?? -1;
            }

            session = new EditorSession(ui);
            sessions.Add(ui, session);
            created = true;

            if (restoreViewState)
            {
                session.RestoreViewStateFrom(previous);
                MapEditorState mapState = MapEditorStateHub.Get(session);
                if (mapState != null)
                {
                    mapState.SelectedRoomIndex = previousMapRoom;
                    mapState.UiCurrentRoomIndex = previousUiCurrentRoom;
                }
            }
        }

        // EditorSession's constructor already synchronized a freshly-created session. Repeating the
        // same structural pass here added a third full sync on the activation frame for no semantic
        // benefit. Existing sessions still synchronize once before vanilla update as normal.
        if (!created)
            session.Synchronize(ui);
        session.MarkUpdateStarted();
        current.SetTarget(session);
    }

    private static bool CanRestoreViewState(EditorSession previous, global::DevInterface.DevUI ui)
    {
        if (previous == null || ui == null || ReferenceEquals(previous.Owner, ui)) return false;

        RainWorldGame previousGame = previous.Owner?.game;
        RainWorldGame nextGame = ui.game;
        if (previousGame == null || nextGame == null || !ReferenceEquals(previousGame, nextGame))
            return false;
        if (!nextGame.processActive || !nextGame.devToolsActive || !ReferenceEquals(nextGame.devUI, ui))
            return false;
        if (nextGame.manager?.currentMainLoop != null && !ReferenceEquals(nextGame.manager.currentMainLoop, nextGame))
            return false;

        return true;
    }

    internal static void Reset()
    {
        current.TryGetTarget(out EditorSession session);
        LegacyDevUiQuiescenceController.ReleasePage(session?.Owner?.activePage);
        sessions = new ConditionalWeakTable<global::DevInterface.DevUI, EditorSession>();
        current = new WeakReference<EditorSession>(null);
    }
}
