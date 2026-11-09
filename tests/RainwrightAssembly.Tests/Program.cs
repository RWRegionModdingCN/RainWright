using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Text;
using Num = System.Numerics;
using DryCycle.DevUI.DevTool.Extensions;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.World;
using DryCycle.DevUI.DevTool.Sound;
using DryCycle.DevUI.DevTool.Map.Cartography;

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var owner = typeof(EditorAuthoringParticipants).Assembly;
        Check(owner.GetName().Name == "RainWright", "Authoring registry was compiled outside Rainwright.dll.");
        foreach (string name in new[]
        {
            "DryCycle.DevUI.DevTool.Sound.SoundGroupConfigPaths",
            "DryCycle.DevUI.DevTool.AutoSave.RelationshipExportPaths",
            "DryCycle.DevUI.DevTool.AutoSave.RainwrightAutoSaveSettingsStore",
            "DryCycle.DevUI.DevTool.Input.RainwrightShortcutInputSettingsStore",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyDocument",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyStorage",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyProjectPaths",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyJson",
            "DryCycle.DevUI.DevTool.World.WorldDocument",
            "DryCycle.DevUI.DevTool.Sound.SoundGroupLibrarySnapshot",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyResourceLocator",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyCommand",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyEditEngine",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyRouteGeometry",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyRegionFiles",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyRegionOption",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographySourceFingerprintCache\u00601",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyPngStreamEncoder",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographyRenderLayerPlanner",
            "DryCycle.DevUI.DevTool.Map.Cartography.CartographySceneRetentionCache\u00602",
            "DryCycle.DevUI.DevTool.RWImGui.WorldMapOrthogonalRouter",
            "DryCycle.DevUI.DevTool.RWImGui.WorldMapDirectionMarkerGeometry",
            "DryCycle.DevUI.DevTool.RWImGui.WorldMapJunctionWeavePlanner"
        })
            Check(owner.GetType(name, throwOnError: false) != null, "Missing Rainwright-owned service: " + name);


        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapServiceLifetimeLedger).Assembly == owner,
            "Player Map service/generation ledger must ship in Rainwright.dll.");
        var mapServices = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapServiceLifetimeLedger();
        int installs = 0, stops = 0, resetBeforeStop = 0;
        mapServices.Start("Workspace", () => installs++,
            () => { stops++; Check(resetBeforeStop == 1, "Workspace detached before Player Map cache reset."); });
        mapServices.Start("Render", () => installs++, () => stops++);
        Check(mapServices.Contains("Workspace") && mapServices.Count == 2 &&
              mapServices.Generation("Workspace") == 1 && installs == 2,
            "Player Map service ledger lost live owner or generation.");
        bool duplicateMapService = false;
        try { mapServices.Start("Workspace", () => installs++, () => stops++); }
        catch (InvalidOperationException) { duplicateMapService = true; }
        Check(duplicateMapService && mapServices.Generation("Workspace") == 1,
            "An active Player Map hook incorrectly permitted a duplicate registration.");
        Check(mapServices.ReleaseAll((name, detach) =>
              {
                  if (name == "Workspace") resetBeforeStop++;
                  detach();
              }, null) && stops == 2 && mapServices.Count == 0,
            "Player Map ledger failed reverse service cleanup.");
        bool partialInstallFailed = false;
        try { mapServices.Start("Render", () => throw new InvalidOperationException("injected"), () => stops++); }
        catch (InvalidOperationException) { partialInstallFailed = true; }
        Check(partialInstallFailed && mapServices.Count == 1 && mapServices.Generation("Render") == 2,
            "A partly installed Player Map service was lost without a retry token.");
        mapServices.ReleaseAll(null, null);
        Check(mapServices.Count == 0 && mapServices.Generation("Render") == 2,
            "Cleanup must release the failed service without resetting its generation history.");
        Check(typeof(EditorBackendServiceLedger).Assembly == owner,
            "Editor backend lifetime state was not moved into Rainwright.dll.");
        var backend = new EditorBackendServiceLedger();
        int attachCalls = 0, detachCalls = 0;
        backend.Start("editor", () => attachCalls++, () => detachCalls++);
        bool duplicateBlocked = false;
        try { backend.Start("editor", () => attachCalls++, () => detachCalls++); }
        catch (InvalidOperationException) { duplicateBlocked = true; }
        Check(duplicateBlocked && attachCalls == 1,
            "Duplicate editor service registration was admitted.");
        Check(backend.ReleaseAll(null, null) && detachCalls == 1 && backend.Count == 0,
            "Backend owner failed to release installed services.");
        bool attachFailed = false;
        int partialStopCalls = 0;
        try
        {
            backend.Start("partial", () => throw new InvalidOperationException("attach"),
                () => {
                    partialStopCalls++;
                    if (partialStopCalls == 1) throw new InvalidOperationException("detach");
                });
        }
        catch (InvalidOperationException) { attachFailed = true; }
        Check(attachFailed && backend.PendingCleanup && backend.Contains("partial"),
            "Failed attach was not retained as a cleanup obligation.");
        Check(!backend.ReleaseAll(null, null) && backend.Contains("partial"),
            "Failed detach was incorrectly marked released.");
        Check(backend.ReleaseAll(null, null) && !backend.PendingCleanup && partialStopCalls == 2,
            "Pending detach retry did not resolve the retained service.");

        Check(typeof(EditorRecoveryFileTransaction).Assembly == owner,
            "Recovery author-file transaction must reside in Rainwright.dll.");
        string recoverySandbox = Path.Combine(Path.GetTempPath(),
            "Rainwright-Recovery-Core-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(recoverySandbox);
        try
        {
            string authorTarget = Path.Combine(recoverySandbox, "author.txt");
            string snapshotPayload = Path.Combine(recoverySandbox, "snapshot.data");
            File.WriteAllText(authorTarget, "original source");
            File.WriteAllText(snapshotPayload, "recoverable");
            string originalHash = EditorRecoveryFileTransaction.HashFile(authorTarget);
            string payloadHash = EditorRecoveryFileTransaction.HashFile(snapshotPayload);
            File.WriteAllText(authorTarget, "external edit");
            bool conflictRejected = false;
            try
            {
                EditorRecoveryFileTransaction.Restore(
                    snapshotPayload, payloadHash, authorTarget, true, originalHash);
            }
            catch (IOException) { conflictRejected = true; }
            Check(conflictRejected && File.ReadAllText(authorTarget) == "external edit",
                "Recovery overwrote a source modified after the candidate was listed.");

            string liveHash = EditorRecoveryFileTransaction.HashFile(authorTarget);
            EditorRecoveryFileTransaction.Restore(
                snapshotPayload, payloadHash, authorTarget, true, liveHash);
            Check(File.ReadAllText(authorTarget) == "recoverable" &&
                File.ReadAllText(authorTarget + ".Rainwright-before-recovery.bak") == "external edit",
                "Recovery did not commit the source and preserve its byte-for-byte backup.");

            string untouched = Path.Combine(recoverySandbox, "new-author.txt");
            EditorRecoveryFileTransaction.Restore(
                snapshotPayload, payloadHash, untouched, false, null);
            Check(File.ReadAllText(untouched) == "recoverable",
                "Recovery of a previously absent author file did not use an exclusive create.");
        }
        finally
        {
            Directory.Delete(recoverySandbox, recursive: true);
        }

        Check(typeof(DryCycle.DevUI.DevTool.History.EditorHistoryJournal<string, string>).Assembly == owner,
            "Document undo/redo journal is not physically owned by Rainwright.dll.");
        var historyJournal = new DryCycle.DevUI.DevTool.History.EditorHistoryJournal<string, string>(
            2, value => value,
            (label, values) => label + ":" + string.Join(",", values));
        historyJournal.ActivateDocument("room");
        historyJournal.Push("first");
        historyJournal.Push("second");
        historyJournal.Push("third");
        Check(historyJournal.UndoLabel == "third" && historyJournal.Revision > 1,
            "History journal did not enforce capacity and revision changes.");
        Check(historyJournal.Undo(_ => true) && historyJournal.RedoLabel == "third",
            "Rainwright journal failed to transfer undo state.");
        Check(!historyJournal.Redo(_ => false) && historyJournal.CanRedo,
            "Rejected redo improperly mutated the journal.");
        historyJournal.ActivateDocument("map");
        using (historyJournal.BeginBatch("batch"))
        {
            historyJournal.Push("one");
            historyJournal.Push("two");
            historyJournal.ActivateDocument("room");
        }
        Check(historyJournal.UndoLabel == "second",
            "Old document batch was attached to the new document.");
        historyJournal.ActivateDocument("map");
        Check(historyJournal.UndoLabel == "batch:one,two" && historyJournal.CanUndo,
            "Rainwright journal did not commit the open batch to its original document.");
        historyJournal.ClearActive();
        Check(!historyJournal.CanUndo && !historyJournal.CanRedo,
            "Clearing the current document left history entries behind.");

        Check(typeof(EditorAutoSaveSchedule<string>).Assembly == owner,
            "AutoSave scheduling and document state are not owned by Rainwright.dll.");
        var autoSchedule = new EditorAutoSaveSchedule<string>();
        DateTime clock = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
        Check(autoSchedule.TryPoll(clock) && !autoSchedule.TryPoll(clock.AddMilliseconds(200)),
            "Autosave schedule should throttle stable frames to a one-second poll.");
        var state = autoSchedule.Observe("room", 0, 90, clock);
        Check(!autoSchedule.SaveDue(state, clock.AddSeconds(89), 90) &&
            autoSchedule.SaveDue(state, clock.AddSeconds(90), 90),
            "Autosave schedule lost the configured document clock.");
        Check(state.SavedHistoryRevision == 0 &&
            autoSchedule.RecoveryDue(state, clock.AddSeconds(91)),
            "A scheduling attempt must not falsely acknowledge an author save.");
        autoSchedule.ObserveSuccessfulSave("room", 3, 90, clock.AddSeconds(90));
        Check(state.SavedHistoryRevision == 3,
            "Saved history revision must advance only after explicit success.");
        autoSchedule.Observe("map", 7, 90, clock);
        Check(!autoSchedule.SaveDue(state, clock.AddSeconds(95), 90),
            "A different document must not borrow another document's due time.");
        autoSchedule.Reset();
        Check(autoSchedule.TryPoll(clock) &&
            autoSchedule.Observe("room", 11, 90, clock).SavedHistoryRevision == 11,
            "Autosave shutdown did not clear scheduling state.");

        Check(typeof(DryCycle.DevUI.DevTool.History.EditorContinuousEditRegistry<object, string, object>)
            .Assembly == owner,
            "Continuous pointer gesture state must live in Rainwright.dll.");
        var gestures = new DryCycle.DevUI.DevTool.History.EditorContinuousEditRegistry<object, string, object>();
        object gestureSession = new(), beforeState = new();
        Check(gestures.Begin(gestureSession, "drag", "Drag", "room", beforeState) &&
            gestures.Begin(gestureSession, "drag", "Ignored", "room", new object()) &&
            !gestures.Begin(gestureSession, "rotate", "Rotate", "room", beforeState),
            "Gesture owner must be idempotent for one key and exclusive for other keys.");
        Check(gestures.TryTake(gestureSession, "drag", out string gestureLabel,
                out string gestureDoc, out object originalBefore) &&
            gestureLabel == "Drag" && gestureDoc == "room" &&
            ReferenceEquals(beforeState, originalBefore) &&
            !gestures.IsActive(gestureSession),
            "Committed gesture must return the original before-state and clear ownership.");
        Check(!gestures.TryTake(gestureSession, "drag", out _, out _, out _) &&
            gestures.Begin(gestureSession, "rotate", null, "map", beforeState) &&
            gestures.ActiveKey(gestureSession) == "rotate",
            "New gesture must be legal after commit without leaking the old snapshot.");
        gestures.Reset();
        Check(!gestures.IsActive(gestureSession),
            "Editor restart must release every outstanding continuous transaction.");

        Check(typeof(DryCycle.DevUI.DevTool.History.EditorCompositeHistoryExecutor).Assembly == owner,
            "Composite Undo/Redo compensation must execute inside Rainwright.dll.");
        int compositeState = 2;
        var compositeSteps = new[] { 1, 2 };
        bool atomicUndo = DryCycle.DevUI.DevTool.History.EditorCompositeHistoryExecutor.Undo(
            compositeSteps,
            step => { if (step == 1) return false; compositeState = 1; return true; },
            step => { compositeState = 2; return true; });
        Check(!atomicUndo && compositeState == 2,
            "Partial undo must replay successful later entries to restore the initial state.");
        compositeState = 0;
        bool atomicRedo = DryCycle.DevUI.DevTool.History.EditorCompositeHistoryExecutor.Redo(
            compositeSteps,
            step => { if (step == 2) throw new InvalidOperationException("injected"); compositeState = 1; return true; },
            step => { compositeState = 0; return true; });
        Check(!atomicRedo && compositeState == 0,
            "Failed composite redo must undo successfully applied earlier entries.");

        // The gameplay host and native UI must observe the same process-wide
        // editor state; these types may not be recompiled into either plugin.
        Check(typeof(DryCycle.DevUI.DevTool.Core.EditorUiModeState).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorShortcutFeedback).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor).Assembly == owner,
            "Shared mode, feedback and performance state must reside in Rainwright.dll.");

        DryCycle.DevUI.DevTool.Core.EditorUiModeState.SetVanilla(true);
        DryCycle.DevUI.DevTool.Core.EditorUiModeState.SetOverlayHidden(true);
        Check(DryCycle.DevUI.DevTool.Core.EditorUiModeState.UseVanilla &&
              DryCycle.DevUI.DevTool.Core.EditorUiModeState.OverlayHidden,
            "Shared presentation state did not publish the vanilla/hidden selection.");
        DryCycle.DevUI.DevTool.Core.EditorUiModeState.SetVanilla(false);
        Check(!DryCycle.DevUI.DevTool.Core.EditorUiModeState.UseVanilla &&
              !DryCycle.DevUI.DevTool.Core.EditorUiModeState.OverlayHidden,
            "Switching back to the new UI must clear the temporary hide.");

        int feedbackRevision = DryCycle.DevUI.DevTool.Core.EditorShortcutFeedback.Revision;
        DryCycle.DevUI.DevTool.Core.EditorShortcutFeedback.PublishCustom(
            "已保存", "Saved", "Ctrl+S", true,
            DryCycle.DevUI.DevTool.Core.EditorShortcutFeedbackVisual.Save);
        var shortcutSnapshot = DryCycle.DevUI.DevTool.Core.EditorShortcutFeedback.Current;
        Check(DryCycle.DevUI.DevTool.Core.EditorShortcutFeedback.Revision > feedbackRevision &&
              shortcutSnapshot.EnglishTitle == "Saved" &&
              shortcutSnapshot.Visual == DryCycle.DevUI.DevTool.Core.EditorShortcutFeedbackVisual.Save &&
              DryCycle.DevUI.DevTool.Core.EditorShortcutFeedback.PresentationHoldActive,
            "Rainwright feedback channel did not publish one immutable shortcut event.");

        DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor.SetEnabled(true);
        using (DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor.Measure(
            DryCycle.DevUI.DevTool.Core.DevToolPerformanceMetric.CorePresentation))
        {
            DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor.RecordPresentation(
                DryCycle.DevUI.DevTool.Core.DevToolPresentationChannel.Core,
                DryCycle.DevUI.DevTool.Core.DevToolPresentationOutcome.CacheHit);
        }
        var performance = DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor.GetStats(
            DryCycle.DevUI.DevTool.Core.DevToolPerformanceMetric.CorePresentation);
        var cacheCounts = DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor.GetPresentationCounters(
            DryCycle.DevUI.DevTool.Core.DevToolPresentationChannel.Core);
        Check(performance.SampleCount >= 1 && cacheCounts.CacheHits == 1,
            "Shared frontend performance samples or cache counters were not recorded.");
        DryCycle.DevUI.DevTool.Core.DevToolPerformanceMonitor.SetEnabled(false);


        Check(typeof(DryCycle.DevUI.DevTool.Core.EditorToolMode).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorDocumentKind).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorDocumentKey).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry).Assembly == owner,
            "Shared tool/document identity or shortcut catalog was compiled outside Rainwright.dll.");
        var documentA = new DryCycle.DevUI.DevTool.Core.EditorDocumentKey(
            DryCycle.DevUI.DevTool.Core.EditorDocumentKind.RegionMap, "SU");
        var documentCopy = new DryCycle.DevUI.DevTool.Core.EditorDocumentKey(
            DryCycle.DevUI.DevTool.Core.EditorDocumentKind.RegionMap, "SU");
        var documentDifferent = new DryCycle.DevUI.DevTool.Core.EditorDocumentKey(
            DryCycle.DevUI.DevTool.Core.EditorDocumentKind.RegionMap, "su");
        Check(documentA.Equals(documentCopy) &&
              documentA.GetHashCode() == documentCopy.GetHashCode() &&
              !documentA.Equals(documentDifferent),
            "Document identity lost case-sensitive region ownership semantics.");
        long shortcutCatalogRevision = DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry.Revision;
        var shortcutDescription = new DryCycle.DevUI.DevTool.Core.DevToolShortcutDescriptor(
            "rainwright-test", "Ctrl+F9", "测试", "Test shortcut", 4);
        DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry.RegisterMode(
            DryCycle.DevUI.DevTool.Core.EditorToolMode.Map, shortcutDescription);
        var shortcutRows = DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry.GetMode(
            DryCycle.DevUI.DevTool.Core.EditorToolMode.Map);
        Check(shortcutRows.Any(row => row.Id == "rainwright-test") &&
              DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry.Revision > shortcutCatalogRevision,
            "Shared shortcut catalog failed to publish a view-specific registration.");
        Check(DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry.UnregisterMode(
                  DryCycle.DevUI.DevTool.Core.EditorToolMode.Map, "RAINWRIGHT-TEST") &&
              !DryCycle.DevUI.DevTool.Core.DevToolShortcutRegistry.GetMode(
                  DryCycle.DevUI.DevTool.Core.EditorToolMode.Map)
                .Any(row => row.Id == "rainwright-test"),
            "Case-insensitive plugin shortcut unregister failed to restore catalog state.");

        Check(typeof(DryCycle.DevUI.DevTool.Core.EditorRevisionTracker).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorRevisionKind).Assembly == owner,
            "Editor workspace revision clocks must be Rainwright-owned.");
        var revisionTracker = new DryCycle.DevUI.DevTool.Core.EditorRevisionTracker();
        Check(revisionTracker.Get(DryCycle.DevUI.DevTool.Core.EditorRevisionKind.Shell) == 1,
            "New editor revision tracker should start with a nonzero shell revision.");
        revisionTracker.MarkWorkspace(DryCycle.DevUI.DevTool.Core.EditorToolMode.Map);
        Check(revisionTracker.Get(DryCycle.DevUI.DevTool.Core.EditorRevisionKind.Map) == 2,
            "A map edit did not invalidate the map revision exactly once.");
        revisionTracker.ObservePresentationOwnership(true);
        revisionTracker.ObservePresentationOwnership(false);
        revisionTracker.ObservePresentationOwnership(true);
        revisionTracker.Mark(DryCycle.DevUI.DevTool.Core.EditorRevisionKind.Map);
        Check(revisionTracker.Get(DryCycle.DevUI.DevTool.Core.EditorRevisionKind.Map) == 3,
            "Resume after dormant UI must invalidate all channels once, not double bump them.");
        revisionTracker.Mark(DryCycle.DevUI.DevTool.Core.EditorRevisionKind.Map);
        Check(revisionTracker.Get(DryCycle.DevUI.DevTool.Core.EditorRevisionKind.Map) == 4,
            "Normal edit invalidation must resume immediately after the first revision read.");

        Check(typeof(DryCycle.DevUI.DevTool.Objects.EditorPropertyValue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Objects.EditorPropertySnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorUiCommand).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorUiCommandQueue).Assembly == owner,
            "Object inspector properties or editor command transport are not Rainwright-owned.");
        DryCycle.DevUI.DevTool.Core.EditorUiCommandQueue.Clear();
        var propertyPayload = new DryCycle.DevUI.DevTool.Objects.EditorPropertyValue(
            DryCycle.DevUI.DevTool.Objects.EditorPropertyKind.Vector2,
            x: 24f, y: -8f);
        DryCycle.DevUI.DevTool.Core.EditorUiCommandQueue.Enqueue(
            new DryCycle.DevUI.DevTool.Core.EditorUiCommand(
                DryCycle.DevUI.DevTool.Core.EditorUiCommandKind.SetObjectProperty,
                index: 12, text: "position", propertyValue: propertyPayload,
                stableId: 941));
        Check(DryCycle.DevUI.DevTool.Core.EditorUiCommandQueue.TryDequeue(out var delivered) &&
              delivered.Kind == DryCycle.DevUI.DevTool.Core.EditorUiCommandKind.SetObjectProperty &&
              delivered.Index == 12 && delivered.StableId == 941 &&
              delivered.PropertyValue.X == 24f &&
              delivered.PropertyValue.Y == -8f &&
              !DryCycle.DevUI.DevTool.Core.EditorUiCommandQueue.TryDequeue(out _),
            "The frontend-to-game command inbox lost stable IDs or object property data.");

        Check(typeof(DryCycle.DevUI.DevTool.Core.EditorViewportSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub).Assembly == owner,
            "Native UI viewport state is still compiled into the gameplay adapter.");
        object cameraIdentity = new();
        DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub.Publish(
            cameraIdentity, 12f, -31f, 1366f, 768f);
        var viewportSnapshot = DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub.Current;
        Check(viewportSnapshot.Available &&
              viewportSnapshot.CameraX == 12f &&
              viewportSnapshot.CameraY == -31f &&
              viewportSnapshot.Width == 1366f,
            "Rainwright viewport projection changed its detached camera coordinates.");
        DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub.Publish(
            cameraIdentity, 12f, -31f, 1366f, 768f);
        Check(ReferenceEquals(viewportSnapshot,
                DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub.Current),
            "Stable viewport inputs must not reallocate snapshots on every frame.");
        DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub.Clear();
        Check(!DryCycle.DevUI.DevTool.Core.EditorViewportPresentationHub.Current.Available,
            "Closed editor camera must release a retained viewport snapshot.");

        Check(typeof(EditorUiCommandPolicy).Assembly == owner,
            "Detached UI command classification must execute in Rainwright.dll.");
        Check(EditorUiCommandPolicy.ObjectMutationScope(EditorUiCommandKind.Save) ==
                  EditorObjectMutationScope.None &&
              EditorUiCommandPolicy.ObjectMutationScope(EditorUiCommandKind.SetObjectProperty) ==
                  EditorObjectMutationScope.Member &&
              EditorUiCommandPolicy.ObjectMutationScope(EditorUiCommandKind.SetSelectionPosition) ==
                  EditorObjectMutationScope.SelectionMembers &&
              EditorUiCommandPolicy.ObjectMutationScope(EditorUiCommandKind.DeleteSelection) ==
                  EditorObjectMutationScope.Collection &&
              EditorUiCommandPolicy.ObjectMutationScope(EditorUiCommandKind.InvokeLegacyButton) ==
                  EditorObjectMutationScope.Full,
            "Editor object edit classification lost mutation scope.");
        EditorUiCommandQueue.Clear();
        int refused = 0;
        for (int i = 0; i < EditorUiCommandQueue.MaxPendingCommands + 3; i++)
            if (!EditorUiCommandQueue.TryEnqueue(new EditorUiCommand(EditorUiCommandKind.ToggleFocus, index:i)))
                refused++;
        Check(refused == 3 && EditorUiCommandQueue.ConsumeDroppedCount() == 3 &&
              EditorUiCommandQueue.PendingCount == EditorUiCommandQueue.MaxPendingCommands,
            "Bounded native command inbox did not reject overflow safely.");
        for (int i = 0; i < EditorUiCommandQueue.MaxCommandsPerFrame; i++)
            Check(EditorUiCommandQueue.TryDequeue(out _), "A frame's commands must remain available.");
        Check(EditorUiCommandQueue.PendingCount ==
                  EditorUiCommandQueue.MaxPendingCommands - EditorUiCommandQueue.MaxCommandsPerFrame,
            "Frame budget removed too many queued commands.");
        EditorUiCommandQueue.Clear();
        Check(EditorUiCommandQueue.PendingCount == 0 && !EditorUiCommandQueue.TryDequeue(out _),
            "Runtime shutdown retained stale commands.");

        Check(typeof(DryCycle.DevUI.DevTool.Objects.ObjectPresentationKind).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Objects.EditorObjectGizmoSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Compatibility.LegacyControlSnapshot).Assembly == owner,
            "Native object and third-party legacy snapshots must be Rainwright-owned.");
        var nativeGizmo = new DryCycle.DevUI.DevTool.Objects.EditorObjectGizmoSnapshot
        {
            ObjectIndex = 2,
            Handles = new[] { new DryCycle.DevUI.DevTool.Objects.EditorObjectGizmoHandleSnapshot
                { Id = "handle-a", X = 8f, Y = -5f } }
        };
        var thirdPartyControl = new DryCycle.DevUI.DevTool.Compatibility.LegacyControlSnapshot
        {
            Kind = DryCycle.DevUI.DevTool.Compatibility.LegacyControlKind.Slider,
            Path = "third-party/slider", Factor = .75f
        };
        Check(nativeGizmo.Handles[0].Id == "handle-a" &&
              thirdPartyControl.Factor == .75f &&
              thirdPartyControl.Path == "third-party/slider",
            "Detached generic editor DTOs lost authored fields.");
        Check(typeof(DryCycle.DevUI.DevTool.Core.EditorPresentationSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Core.EditorInspectorSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiPresentationSnapshot).Assembly == owner,
            "Frontend document and universal DevUI snapshots must share Rainwright identity.");
        var detachedView = new DryCycle.DevUI.DevTool.Core.EditorPresentationSnapshot
        {
            Available = true,
            SceneObjects = new[] { new DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot
            {
                Index = 2, StableId = 987,
                Type = "ThirdPartyObject",
                PresentationKind = DryCycle.DevUI.DevTool.Objects.ObjectPresentationKind.Path
            }},
            Inspector = new DryCycle.DevUI.DevTool.Core.EditorInspectorSnapshot
            {
                LegacyControls = new[] { new DryCycle.DevUI.DevTool.Compatibility.LegacyControlSnapshot
                {
                    Kind = DryCycle.DevUI.DevTool.Compatibility.LegacyControlKind.Text,
                    Path = "mod/field", ValueText = "custom"
                }}
            }
        };
        Check(detachedView.SceneObjects[0].StableId == 987 &&
              detachedView.Inspector.LegacyControls[0].ValueText == "custom",
            "Detached cross-plugin object/inspector DTO failed its payload contract.");
        Check(typeof(DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommand).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue).Assembly == owner,
            "Third-party DevUI edit commands must be transported by Rainwright.dll.");
        DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.Clear();
        var universalCommand = new DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommand(
            DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandKind.SetDirection,
            "third-party/panel/slider", x: 8f, y: -3f);
        Check(DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.TryEnqueue(universalCommand) &&
              DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.TryDequeue(out var translatedCommand) &&
              translatedCommand.Path == "third-party/panel/slider" &&
              translatedCommand.X == 8f && translatedCommand.Y == -3f,
            "Cross-plugin third-party control command payload was lost.");
        int compatibilityRejected = 0;
        for (int i = 0; i < DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.MaxPendingCommands + 2; i++)
            if (!DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.TryEnqueue(universalCommand))
                compatibilityRejected++;
        Check(compatibilityRejected == 2 &&
              DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.ConsumeRejectedCount() == 2,
            "Third-party command transport does not bound opaque native UI producers.");
        DryCycle.DevUI.DevTool.Compatibility.UniversalDevUiCommandQueue.Clear();
        Check(typeof(DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationSnapshot).Assembly == owner,
            "Trigger, Dialog and Relationship frontend view models must be Rainwright-owned.");

        var triggerView = new DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationSnapshot
        {
            Available = true,
            Triggers = new[]
            {
                new DryCycle.DevUI.DevTool.Triggers.EditorTriggerSnapshot
                {
                    Name = "spot", Event = new DryCycle.DevUI.DevTool.Triggers.EditorTriggeredEventSnapshot
                    { HasEvent = true, SongName = "music-test" }
                }
            }
        };
        var dialogView = new DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationSnapshot
        {
            Available = true,
            Events = new[] { new DryCycle.DevUI.DevTool.Dialog.EditorDialogEventSnapshot
            {
                Kind = DryCycle.DevUI.DevTool.Dialog.EditorDialogEventKind.Text,
                Text = "你好"
            }}
        };
        var relationshipView = new DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationSnapshot
        {
            Available = true,
            Rows = new[] { new DryCycle.DevUI.DevTool.Relationships.EditorRelationshipRowSnapshot
            {
                CreatureType = "Lizard",
                PrimaryToOther = new DryCycle.DevUI.DevTool.Relationships.EditorRelationshipValueSnapshot
                { Type = "Eats", Intensity = 0.8f }
            }}
        };
        Check(triggerView.Triggers[0].Event.SongName == "music-test" &&
              dialogView.Events[0].Text == "你好" &&
              relationshipView.Rows[0].PrimaryToOther.Intensity == .8f,
            "Detached editor view snapshots did not preserve command-visible data.");
        Check(typeof(DryCycle.DevUI.DevTool.Room.EditorRoomSettingsSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Room.EditorRoomEffectSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Sound.EditorSoundSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Sound.EditorSoundPresentationSnapshot).Assembly == owner,
            "Room Settings and Sound presentation DTOs must have Rainwright assembly identity.");

        var roomView = new DryCycle.DevUI.DevTool.Room.EditorRoomSettingsSnapshot
        {
            Available = true,
            LocalSettingKeys = new[] { "rainIntensity" },
            TemplateOverrideKeys = new[] { "grime" },
            Effects = new[] { new DryCycle.DevUI.DevTool.Room.EditorRoomEffectSnapshot
            {
                Type = "Snow", Values = new[] { 0.3f }
            }}
        };
        var soundView = new DryCycle.DevUI.DevTool.Sound.EditorSoundPresentationSnapshot
        {
            Available = true,
            Sounds = new[] { new DryCycle.DevUI.DevTool.Sound.EditorSoundSnapshot
            {
                Sample = "ambient-sample",
                ResourceSourceKind = DryCycle.DevUI.DevTool.Sound.EditorSoundSourceKind.Mod,
                ResourceAvailable = true
            }}
        };
        Check(roomView.IsLocal("rainIntensity") &&
              roomView.InheritedFromTemplate("grime") &&
              roomView.Effects[0].Values[0] == 0.3f &&
              soundView.Sounds[0].ResourceSourceKind ==
                DryCycle.DevUI.DevTool.Sound.EditorSoundSourceKind.Mod,
            "Room/template and Sound resource DTO contracts changed across assemblies.");

        Check(typeof(DryCycle.DevUI.DevTool.Room.EditorRoomPresentationState).Assembly == owner &&
              typeof(EditorSoundPresentationState).Assembly == owner,
            "Published Room/Sound state must reside in Rainwright.dll.");
        DryCycle.DevUI.DevTool.Room.EditorRoomPresentationState.Publish(roomView);
        EditorSoundPresentationState.Publish(soundView);
        Check(ReferenceEquals(DryCycle.DevUI.DevTool.Room.EditorRoomPresentationState.Current, roomView) &&
              ReferenceEquals(EditorSoundPresentationState.Current, soundView),
            "Shared Room/Sound presentation state did not publish across assemblies.");
        var selectedSound = EditorSoundPresentationState.WithSelection(soundView, 0);
        Check(!soundView.Sounds[0].Selected && selectedSound.Sounds[0].Selected &&
              selectedSound.Sounds[0].ResourceSourceKind == soundView.Sounds[0].ResourceSourceKind &&
              ReferenceEquals(selectedSound.SampleEntries, soundView.SampleEntries),
            "Detached Sound selection must preserve original data and resource provenance.");
        var selectedAgain = EditorSoundPresentationState.WithSelection(selectedSound, 0);
        Check(ReferenceEquals(selectedAgain.Sounds, selectedSound.Sounds),
            "Unchanged Sound selection needlessly cloned its retained payload.");
        DryCycle.DevUI.DevTool.Room.EditorRoomPresentationState.Clear();
        EditorSoundPresentationState.Clear();
        Check(!DryCycle.DevUI.DevTool.Room.EditorRoomPresentationState.Current.Available &&
              !EditorSoundPresentationState.Current.Available,
            "Room/Sound state leaked across a session reset.");

        // Rainwright publishes detached page state; game adapters only capture data.
        Check(typeof(DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationState).Assembly == owner, "EditorTriggerPresentationState identity left Rainwright.");
        DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationState.Publish(new DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationSnapshot { Available = true });
        Check(DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationState.Current.Available, "EditorTriggerPresentationState failed to publish.");
        DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationState.Clear();
        Check(!DryCycle.DevUI.DevTool.Triggers.EditorTriggerPresentationState.Current.Available, "EditorTriggerPresentationState leaked state after Clear.");
        Check(typeof(DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationState).Assembly == owner, "EditorDialogPresentationState identity left Rainwright.");
        DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationState.Publish(new DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationSnapshot { Available = true });
        Check(DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationState.Current.Available, "EditorDialogPresentationState failed to publish.");
        DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationState.Clear();
        Check(!DryCycle.DevUI.DevTool.Dialog.EditorDialogPresentationState.Current.Available, "EditorDialogPresentationState leaked state after Clear.");

        // A page switch releases its shared view without retaining gameplay sessions.
        Check(typeof(DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationState).Assembly == owner, "EditorRelationshipPresentationState identity left Rainwright.");
        DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationState.Publish(new DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationSnapshot { Available = true });
        Check(DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationState.Current.Available, "EditorRelationshipPresentationState failed to publish.");
        DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationState.Clear();
        Check(!DryCycle.DevUI.DevTool.Relationships.EditorRelationshipPresentationState.Current.Available, "EditorRelationshipPresentationState leaked state after Clear.");
        Check(typeof(DryCycle.DevUI.DevTool.Map.EditorMapPresentationState).Assembly == owner, "EditorMapPresentationState identity left Rainwright.");
        DryCycle.DevUI.DevTool.Map.EditorMapPresentationState.Publish(new DryCycle.DevUI.DevTool.Map.EditorMapPresentationSnapshot { Available = true });
        Check(DryCycle.DevUI.DevTool.Map.EditorMapPresentationState.Current.Available, "EditorMapPresentationState failed to publish.");
        DryCycle.DevUI.DevTool.Map.EditorMapPresentationState.Clear();
        Check(!DryCycle.DevUI.DevTool.Map.EditorMapPresentationState.Current.Available, "EditorMapPresentationState leaked state after Clear.");

        Check(typeof(EditorPresentationState).Assembly == owner,
            "Shell/Objects published view must be owned by Rainwright.");
        var shellView = new EditorPresentationSnapshot
        {
            Available = true,
            Document = "test-world:SU",
            SceneObjects = new[] { new EditorObjectSnapshot { StableId = 42L, Selected = true } }
        };
        EditorPresentationState.Publish(shellView);
        Check(ReferenceEquals(EditorPresentationState.Current, shellView) &&
              EditorPresentationState.Current.SceneObjects[0].StableId == 42L,
            "Rainwright shell publication lost the retained Object view.");
        EditorPresentationState.Clear();
        Check(!EditorPresentationState.Current.Available,
            "Closing DevTools did not release the shared Shell view.");
        Check(typeof(DryCycle.DevUI.DevTool.Map.MapRoomLayer).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPlacementMode).Assembly == owner,
            "Map layer and player-map placement contracts must compile into Rainwright.dll.");
        Check(DryCycle.DevUI.DevTool.Map.MapRoomLayer.Count == 3 &&
              DryCycle.DevUI.DevTool.Map.MapRoomLayer.Default == 1 &&
              DryCycle.DevUI.DevTool.Map.MapRoomLayer.Label(2) == "L2" &&
              (int)DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPlacementMode.Derived == 0 &&
              (int)DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPlacementMode.Absolute == 1,
            "Shared map layer or Player Map placement ABI was changed.");
        bool invalidLayerRejected = false;
        try { DryCycle.DevUI.DevTool.Map.MapRoomLayer.Validate(3); }
        catch (ArgumentOutOfRangeException) { invalidLayerRejected = true; }
        Check(invalidLayerRejected, "An invalid map layer was silently accepted.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy).Assembly == owner,
            "Player Map conversion rules must be owned by Rainwright.dll.");
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.LayerCount ==
                  DryCycle.DevUI.DevTool.Map.MapRoomLayer.Count &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.OutputPadding == 10 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.WorldToCanonScale == 1.5f,
            "The portable Player Map scale/layer/output contract regressed.");
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.WorldLayoutToCanon(8f) == 12f &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.CanonToWorldLayout(12f) == 8f &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.WorldLayoutToCanon(-6f) == -9f &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCoordinatePolicy.CanonDeltaToOutputPixel(-4f) == -1,
            "Player Map Canon coordinate translation or signed pixel truncation changed.");

        Check(typeof(DryCycle.DevUI.DevTool.World.WorldConnectionDirection).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.EditorMapPresentationSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.EditorMapRoomSnapshot).Assembly == owner,
            "Map room/node/connection frontend identity must be Rainwright-owned.");
        var regionMapView = new DryCycle.DevUI.DevTool.Map.EditorMapPresentationSnapshot
        {
            Available = true,
            RegionName = "SU",
            Rooms = new[] { new DryCycle.DevUI.DevTool.Map.EditorMapRoomSnapshot
            {
                Name = "SU_A01", Layer = 2, Nodes = new[]
                {
                    new DryCycle.DevUI.DevTool.Map.EditorMapRoomNodeSnapshot
                    { NodeIndex = 3, Exit = true, ConnectedRoomIndex = 4 }
                }
            }},
            Connections = new[] { new DryCycle.DevUI.DevTool.Map.EditorMapConnectionSnapshot
            {
                ConnectionId = "SU_A01:3/SU_A02:1",
                Direction = DryCycle.DevUI.DevTool.World.WorldConnectionDirection.BToA
            }}
        };
        Check(regionMapView.Rooms[0].Layer == 2 &&
              regionMapView.Rooms[0].Nodes[0].NodeIndex == 3 &&
              regionMapView.Connections[0].Direction ==
                  DryCycle.DevUI.DevTool.World.WorldConnectionDirection.BToA,
            "Native Map snapshot failed to preserve layer/node/one-way topology.");
        Check(typeof(DryCycle.DevUI.DevTool.Room.RoomEditorCommandQueue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.MapEditorCommandQueue).Assembly == owner &&
              typeof(EditorCommandInbox<int>).Assembly == owner,
            "Room and Map native command inboxes must be owned by Rainwright.dll.");
        DryCycle.DevUI.DevTool.Room.RoomEditorCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Map.MapEditorCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Room.RoomEditorCommandQueue.Enqueue(
            new DryCycle.DevUI.DevTool.Room.RoomEditorCommand(
                DryCycle.DevUI.DevTool.Room.RoomEditorCommandKind.SetSetting,
                key: "rainIntensity", value: new DryCycle.DevUI.DevTool.Objects.EditorPropertyValue(
                    DryCycle.DevUI.DevTool.Objects.EditorPropertyKind.Float, x:0.75f)));
        DryCycle.DevUI.DevTool.Map.MapEditorCommandQueue.Enqueue(
            new DryCycle.DevUI.DevTool.Map.MapEditorCommand(
                DryCycle.DevUI.DevTool.Map.MapEditorCommandKind.SetRoomLayer,
                roomIndex: 9,
                value: new DryCycle.DevUI.DevTool.Objects.EditorPropertyValue(
                    DryCycle.DevUI.DevTool.Objects.EditorPropertyKind.Integer, integer:2)));
        Check(DryCycle.DevUI.DevTool.Room.RoomEditorCommandQueue.TryDequeue(out var roomEdit) &&
              roomEdit.Value.X == 0.75f &&
              DryCycle.DevUI.DevTool.Map.MapEditorCommandQueue.TryDequeue(out var mapEdit) &&
              mapEdit.RoomIndex == 9 && mapEdit.Value.Integer == 2,
            "Rainwright native Room/Map inbox lost detached authoring command payload.");

        // Sound, Trigger, Dialog and Relationship editing share one bounded Rainwright command owner.
        Check(typeof(DryCycle.DevUI.DevTool.Sound.SoundEditorCommandQueue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommandQueue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommandQueue).Assembly == owner,
            "Feature command queues must be compiled into Rainwright.dll only.");
        DryCycle.DevUI.DevTool.Sound.SoundEditorCommandQueue.Clear();
        Check(DryCycle.DevUI.DevTool.Sound.SoundEditorCommandQueue.TryEnqueue(
                  new DryCycle.DevUI.DevTool.Sound.SoundEditorCommand(
                      DryCycle.DevUI.DevTool.Sound.SoundEditorCommandKind.AddSoundsToGroup,
                      new[] { 2, 6 }, key: "weather")) &&
              DryCycle.DevUI.DevTool.Sound.SoundEditorCommandQueue.TryDequeue(out var soundEdit) &&
              soundEdit.Indices[1] == 6 && soundEdit.Key == "weather",
            "Sound command lost selected sound indices.");
        DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommandQueue.Clear();
        Check(DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommandQueue.TryEnqueue(
                  new DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommand(
                      DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommandKind.SetSlugcats,
                      index: 3, texts: new[] { "White", "Yellow" })) &&
              DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommandQueue.TryDequeue(out var triggerEdit) &&
              triggerEdit.Index == 3 && triggerEdit.Texts[1] == "Yellow",
            "Trigger command lost multi-slugcat payload.");
        DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommandQueue.Clear();
        Check(DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommandQueue.TryEnqueue(
                  new DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommand(
                      DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommandKind.SetRelationshipIntensity,
                      primary: "Vulture", other: "Scavenger", value: .4f,
                      direction: DryCycle.DevUI.DevTool.Relationships.EditorRelationshipDirection.OtherToPrimary)) &&
              DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommandQueue.TryDequeue(out var relationshipEdit) &&
              relationshipEdit.Value == .4f &&
              relationshipEdit.Direction == DryCycle.DevUI.DevTool.Relationships.EditorRelationshipDirection.OtherToPrimary,
            "Relationship command lost authoring direction/intensity.");
        DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.Clear();
        int dialogRejects = 0;
        for (int i = 0; i < DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.MaxPendingCommands + 2; i++)
            if (!DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.TryEnqueue(
                  new DryCycle.DevUI.DevTool.Dialog.DialogEditorCommand(
                      DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandKind.SelectDialog, "dialog" + i)))
                dialogRejects++;
        Check(dialogRejects == 2 &&
              DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.ConsumeRejectedCount() == 2 &&
              DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.PendingCount ==
                  DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.MaxPendingCommands,
            "Dialog frontend queue did not bound producer overload.");
        for (int i = 0; i < DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.MaxCommandsPerFrame; i++)
            Check(DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.TryDequeue(out _),
                  "Dialog queue failed to deliver a frame's edit budget.");
        Check(DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.PendingCount ==
                  DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.MaxPendingCommands -
                  DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.MaxCommandsPerFrame,
            "Dialog frame drain discarded beyond its batch budget.");
        DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.Clear();
        Check(DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.PendingCount == 0 &&
              DryCycle.DevUI.DevTool.Dialog.DialogEditorCommandQueue.ConsumeRejectedCount() == 0,
            "Dialog session cleanup leaked pending commands.");
        DryCycle.DevUI.DevTool.Sound.SoundEditorCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Triggers.TriggerEditorCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Relationships.RelationshipEditorCommandQueue.Clear();

        Check(typeof(DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommand).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommandQueue).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommand).Assembly == owner,
            "Native scene gizmo transport must be owned by Rainwright.dll.");
        DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue.Enqueue(
            new DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommand(
                DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandKind.Begin,
                DryCycle.DevUI.DevTool.Gizmos.NativeGizmoTargetKind.ObjectPosition, 2, stableId: 357));
        DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue.Enqueue(
            new DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommand(
                DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandKind.Commit,
                DryCycle.DevUI.DevTool.Gizmos.NativeGizmoTargetKind.ObjectPosition, 2, stableId: 357));
        Check(DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue.TryDequeue(out var beginDrag) &&
              DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue.TryDequeue(out var endDrag) &&
              beginDrag.Kind == DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandKind.Begin &&
              endDrag.Kind == DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandKind.Commit &&
              beginDrag.StableId == endDrag.StableId,
            "Gizmo drag begin/commit event ordering or object identity changed.");
        DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommandQueue.Enqueue(
            new DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommand(
                DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditKind.Update, 5,
                handleId: "curve/1", x: 7f, y: 9f, stableId: 812));
        Check(DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommandQueue.TryDequeue(out var curveDrag) &&
              curveDrag.HandleId == "curve/1" && curveDrag.StableId == 812 &&
              curveDrag.X == 7f && curveDrag.Y == 9f,
            "Object gizmo transport dropped detached handle coordinates.");
        DryCycle.DevUI.DevTool.Gizmos.NativeGizmoCommandQueue.Clear();
        DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommandQueue.Clear();

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandKind).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandEnvelope).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport).Assembly == owner,
            "Player Map command ABI/transport must have Rainwright.dll assembly identity.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.Clear();
        var mapTransport = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandEnvelope(
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandKind.SetDefaultMaterialRect,
            roomIndex: 13, x: 17.5f, y: -8.25f, xb: 90f, yb: 24f, integer: 3, flag: true);
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.TryEnqueue(mapTransport) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.TryDequeue(out var mapDelivered) &&
              mapDelivered.Kind == mapTransport.Kind &&
              mapDelivered.RoomIndex == 13 && mapDelivered.X == 17.5f &&
              mapDelivered.Y == -8.25f && mapDelivered.XB == 90f &&
              mapDelivered.YB == 24f && mapDelivered.Integer == 3 && mapDelivered.Flag,
            "Detached Player Map envelope lost its coordinates or authoring flags.");
        int playerMapOverflow = 0;
        for (int i = 0; i < DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.MaxPendingCommands + 2; i++)
            if (!DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.TryEnqueue(mapTransport))
                playerMapOverflow++;
        Check(playerMapOverflow == 2 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.ConsumeRejectedCount() == 2 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.PendingCount ==
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.MaxPendingCommands,
            "Detached Player Map command inbox failed to bound pending UI input.");
        for (int i = 0; i < DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.MaxCommandsPerFrame; i++)
            Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.TryDequeue(out _),
                "Player Map bounded command processing lost an enqueued command.");
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.PendingCount ==
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.MaxPendingCommands -
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.MaxCommandsPerFrame,
            "Player Map budget consumed more than a frame of commands.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.Clear();
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.PendingCount == 0 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapCommandTransport.ConsumeRejectedCount() == 0,
            "Player Map transport leaked pending commands across editor shutdown.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementOperation).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport).Assembly == owner,
            "Player Map grouped editing transport must reside in Rainwright.dll.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.Clear();
        int[] groupRoomIndices = { 4, 9 };
        float[] groupXs = { 15f, 80f };
        float[] groupYs = { -3f, 22f };
        var groupMove = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupMoveEnvelope(
            groupRoomIndices, groupXs, groupYs, "move two rooms");
        groupRoomIndices[0] = 999;
        groupXs[0] = 999f;
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryEnqueue(groupMove) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryDequeue(out DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupMoveEnvelope groupMoved) &&
              groupMoved.RoomIndices[0] == 4 && groupMoved.Xs[0] == 15f &&
              groupMoved.Ys[1] == 22f && groupMoved.Label == "move two rooms",
            "Detached Player Map group must copy source room arrays and world coordinates.");
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryEnqueue(
                  new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupLayerEnvelope(
                      new[] { 4, 9 }, 2, "layer")) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryEnqueue(
                  new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementEnvelope(
                      new[] { 4 }, DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementOperation.ResetOffset, "reset")) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryDequeue(out DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupLayerEnvelope groupedLayer) &&
              groupedLayer.Layer == 2 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryDequeue(out DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementEnvelope groupedPlacement) &&
              groupedPlacement.Operation == DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementOperation.ResetOffset,
            "Grouped Player Map layer/placement command queues lost semantic payload.");
        int groupRejected = 0;
        for (int i = 0; i < DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.MaxPendingCommands + 1; i++)
            if (!DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.TryEnqueue(groupMove))
                groupRejected++;
        Check(groupRejected == 1 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.ConsumeRejectedCount() == 1 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.PendingCount ==
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.MaxPendingCommands,
            "Player Map group inbox exceeded its bounded lane capacity.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.Clear();
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.PendingCount == 0 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupCommandTransport.ConsumeRejectedCount() == 0,
            "Grouped Player Map state survived the editor cleanup boundary.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandKind).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport).Assembly == owner,
            "Player Map migration-stream command transport must reside in Rainwright.dll.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.Clear();
        var migrationEnvelope = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandEnvelope(
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandKind.SetStartHandle,
            "streamA", 16.5f, -7.25f, .3f, 2, true, "next-stream");
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.TryEnqueue(migrationEnvelope) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.TryDequeue(out var migrationDelivered) &&
              migrationDelivered.Kind == migrationEnvelope.Kind &&
              migrationDelivered.StreamName == "streamA" &&
              migrationDelivered.X == 16.5f && migrationDelivered.Y == -7.25f &&
              migrationDelivered.Number == .3f && migrationDelivered.Integer == 2 &&
              migrationDelivered.Flag && migrationDelivered.Text == "next-stream",
            "Detached migration transport lost spline handle or link values.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.Clear();
        int migrationRejected = 0;
        for (int i = 0; i < DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.MaxPendingCommands + 1; i++)
            if (!DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.TryEnqueue(migrationEnvelope))
                migrationRejected++;
        Check(migrationRejected == 1 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.ConsumeRejectedCount() == 1 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.PendingCount ==
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.MaxPendingCommands,
            "Player Map migration stream inbox allowed unbounded edit commands.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.Clear();
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.PendingCount == 0 &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapMigrationCommandTransport.ConsumeRejectedCount() == 0,
            "Player Map migration command queue survived reset.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupLayerCommand).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementCommand).Assembly == owner &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapLayerPolicy.Count == 3,
            "Portable Player Map group command DTOs and canonical three-layer model must be Rainwright-owned.");
        var normalizedLayer = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupLayerCommand(
            new[] { 1, 2 }, 99, "clamped layer");
        Check(normalizedLayer.Layer == 2 &&
              normalizedLayer.RoomIndices.Length == 2 &&
              normalizedLayer.Label == "clamped layer",
            "Grouped Player Map layer limits changed during assembly extraction.");
        var placementDto = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementCommand(
            new[] { 1 }, DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementOperation.ResetOffset, "reset");
        Check(placementDto.Operation ==
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapGroupPlacementOperation.ResetOffset,
            "Portable group placement command lost its authoring operation.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomVisibility).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy).Assembly == owner,
            "Player Map authoring visibility/room record policies must ship in Rainwright.");
        string hiddenRooms = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomVisibility.Write(
            new[] { "SU_A02", "su_a01", "SU_A02" });
        var hiddenRoundTrip = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomVisibility.Read(
            new[] { "custom third-party payload", hiddenRooms });
        Check(hiddenRoundTrip.Count == 2 &&
              hiddenRoundTrip.Contains("SU_A01") && hiddenRoundTrip.Contains("SU_A02") &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomVisibility.IsRecord("  " + hiddenRooms),
            "Hidden room record migration lost deterministic case-insensitive names.");
        bool rejectedUnknownVisibilityVersion = false;
        try
        {
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomVisibility.Read(
                new[] { "DryCycle_PlayerMap_HiddenRooms: v2|test" });
        }
        catch (FormatException) { rejectedUnknownVisibilityVersion = true; }
        Check(rejectedUnknownVisibilityVersion,
            "Unknown Player Map hidden-room schema should fail closed rather than corrupt author data.");
        const string vanillaRoomLine = "SU_A01: 0><2.5><-3><0><1><NONE><96><105";
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy.TryParseVanillaRoomRecord(
                  vanillaRoomLine, out string vanillaRoomName) && vanillaRoomName == "SU_A01" &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy.TryParseVanillaRoomRecord(
                  vanillaRoomLine + "><extensionValue", out _) &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy.TryParseVanillaRoomRecord(
                  "SU_A01: 0><0><0><0><9><NONE><96><105", out _) &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy.TryParseVanillaRoomRecord(
                  "SU_A01: bad><0><0><0><1><NONE><96><105", out _),
            "Room config filter would mistake a third-party/invalid payload for a vanilla room record.");

        const string staleRoomLine = "SU_A02: 0><0><1><1><1><NONE><80><90";
        const string pluginRoomLine = "SU_A02: 0><0><1><1><1><NONE><80><90><addon";
        var authorMapLines = new[] { "THIRD_PARTY: keep", vanillaRoomLine, staleRoomLine, pluginRoomLine };
        var visibleAuthorRooms = new System.Collections.Generic.HashSet<string>(
            StringComparer.OrdinalIgnoreCase) { "su_a01" };
        var filteredAuthorMap = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy
            .FilterVanillaRoomRecords(authorMapLines, visibleAuthorRooms);
        Check(filteredAuthorMap.Count == 3 &&
              filteredAuthorMap[0] == "THIRD_PARTY: keep" &&
              filteredAuthorMap[1] == vanillaRoomLine &&
              filteredAuthorMap[2] == pluginRoomLine,
            "Player Map config filter removed a visible room or third-party author payload.");
        var noOpFilteredMap = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy
            .FilterVanillaRoomRecords(new[] { vanillaRoomLine, pluginRoomLine }, visibleAuthorRooms);
        Check(noOpFilteredMap[0] == vanillaRoomLine && noOpFilteredMap[1] == pluginRoomLine,
            "No-op Player Map filter changed retained authoring data.");
        bool nullVisibleSetRejected = false;
        try
        {
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomRecordPolicy
                .FilterVanillaRoomRecords(authorMapLines, null);
        }
        catch (ArgumentNullException) { nullVisibleSetRejected = true; }
        Check(nullVisibleSetRejected,
            "Player Map filter must fail closed if its live-room source is missing.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapStagedOutputValidator).Assembly == owner,
            "Player Map staged output validation must be owned by Rainwright.dll.");
        string stagedRoot = Path.Combine(Path.GetTempPath(), "Rainwright-PlayerMap-Staging-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagedRoot);
        try
        {
            string png = Path.Combine(stagedRoot, "map.png");
            string metadata = Path.Combine(stagedRoot, "map_image.txt");
            using (var stream = File.Create(png))
            using (var writer = new BinaryWriter(stream))
            {
                // Minimal PNG chunk structure. The validator deliberately checks structure
                // and metadata bounds, not image decompression or CRC recomputation.
                writer.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                void WritePngChunk(string name, byte[] data)
                {
                    int count = data.Length;
                    writer.Write(new byte[] { (byte)(count >> 24), (byte)(count >> 16),
                                              (byte)(count >> 8), (byte)count });
                    writer.Write(System.Text.Encoding.ASCII.GetBytes(name));
                    writer.Write(data);
                    writer.Write(new byte[4]);
                }
                WritePngChunk("IHDR", new byte[] { 0, 0, 0, 4, 0, 0, 0, 4, 8, 6, 0, 0, 0 });
                WritePngChunk("IDAT", new byte[] { 1 });
                WritePngChunk("IEND", Array.Empty<byte>());
            }
            File.WriteAllText(metadata, "SU_A01: 0,0,4,4");
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapStagedOutputValidator.ValidatePair(png, metadata);

            File.WriteAllText(metadata, "SU_A01: 2147483647,0,2,2");
            bool overflowBlocked = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapStagedOutputValidator.ValidatePair(png, metadata);
            }
            catch (InvalidDataException) { overflowBlocked = true; }
            Check(overflowBlocked,
                "A huge map_image coordinate bypassed the staged rectangle bounds guard.");

            File.WriteAllText(metadata, "SU_A01: 0,0,4,4" + Environment.NewLine +
                "su_a01: 0,0,1,1");
            bool stagedDuplicateBlocked = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapStagedOutputValidator.ValidatePair(png, metadata);
            }
            catch (InvalidDataException) { stagedDuplicateBlocked = true; }
            Check(stagedDuplicateBlocked, "Duplicate case-insensitive Player Map room metadata was accepted.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction).Assembly == owner,
            "Rainwright must own the validated two-file Render Map commit transaction.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderRollbackIncompleteException).Assembly == owner,
            "Incomplete Player Map output recovery must use a precise Rainwright-owned exception contract.");
        var incompleteMapRollback = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderRollbackIncompleteException(
            "Recovery required", new AggregateException(new IOException("Filesystem refused rollback")));
        Check(incompleteMapRollback is IOException &&
              incompleteMapRollback.InnerException is AggregateException &&
              incompleteMapRollback.ToString().Contains("Filesystem refused rollback"),
            "Incomplete two-file recovery diagnostics lost the original rollback failure.");

        string firstRenderStagingPath =
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.StagingPath("map_SU.png");
        string secondRenderStagingPath =
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.StagingPath("map_SU.png");
        string metadataRenderStagingPath =
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.StagingPath("map_image_SU.txt");
        Check(firstRenderStagingPath.StartsWith("map_SU.png.rainwright.", StringComparison.Ordinal) &&
              firstRenderStagingPath.EndsWith(".tmp", StringComparison.Ordinal) &&
              metadataRenderStagingPath.StartsWith("map_image_SU.txt.rainwright.", StringComparison.Ordinal) &&
              firstRenderStagingPath != secondRenderStagingPath &&
              !firstRenderStagingPath.Contains(".drycycle."),
            "A new Render Map job reused a previous or legacy staging file name.");
        bool emptyRenderStagingTargetRejected = false;
        try { DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.StagingPath("  "); }
        catch (ArgumentException) { emptyRenderStagingTargetRejected = true; }
        Check(emptyRenderStagingTargetRejected,
            "Render Map must reject an empty output target before creating staging files.");
        string renderTransactionRoot = Path.Combine(stagedRoot, "transaction");
        Directory.CreateDirectory(renderTransactionRoot);
        try
        {
            string goodPng = Path.Combine(renderTransactionRoot, "staged.png");
            string goodMeta = Path.Combine(renderTransactionRoot, "staged-meta.txt");
            string targetPng = Path.Combine(renderTransactionRoot, "map.png");
            string targetMeta = Path.Combine(renderTransactionRoot, "map_image.txt");

            // Use the structurally valid 4x4 PNG already exercised by the
            // staged-output validator above; never fabricate game dependencies.
            File.Copy(png, goodPng);
            File.WriteAllText(goodMeta, "SU_A01: 0,0,4,4");
            File.WriteAllText(targetPng, "previous image");
            File.WriteAllText(targetMeta, "previous metadata");
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.CommitValidatedPair(
                goodPng, targetPng, goodMeta, targetMeta);
            Check(!File.Exists(goodPng) && !File.Exists(goodMeta) &&
                  !File.Exists(targetPng + ".rainwright.bak") &&
                  !File.Exists(targetMeta + ".rainwright.bak") &&
                  File.ReadAllText(targetMeta) == "SU_A01: 0,0,4,4",
                "Render Map pair transaction failed to atomically install valid author output.");

            File.Copy(png, goodPng);
            File.WriteAllText(goodMeta, "SU_A01: 2147483647,0,4,4");
            byte[] priorPng = File.ReadAllBytes(targetPng);
            string priorMeta = File.ReadAllText(targetMeta);
            bool malformedPairRejected = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.CommitValidatedPair(
                    goodPng, targetPng, goodMeta, targetMeta);
            }
            catch (InvalidDataException) { malformedPairRejected = true; }
            Check(malformedPairRejected &&
                  File.ReadAllBytes(targetPng).SequenceEqual(priorPng) &&
                  File.ReadAllText(targetMeta) == priorMeta,
                "Invalid staged map pair replaced known-good author files.");

            File.WriteAllText(goodMeta, "SU_A01: 0,0,4,4");
            File.WriteAllText(targetMeta + ".rainwright.bak", "earlier recoverable data");
            bool orphanPairBlocked = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.CommitValidatedPair(
                    goodPng, targetPng, goodMeta, targetMeta);
            }
            catch (IOException) { orphanPairBlocked = true; }
            Check(orphanPairBlocked &&
                  File.ReadAllText(targetMeta + ".rainwright.bak") == "earlier recoverable data" &&
                  File.ReadAllBytes(targetPng).SequenceEqual(priorPng) &&
                  File.ReadAllText(targetMeta) == priorMeta,
                "Render Map transaction overwrote a prior recovery backup.");
            File.Delete(targetMeta + ".rainwright.bak");

            // Force the second install to fail after the first staged PNG was
            // moved. Verify that the first output is restored.
            File.Delete(targetMeta);
            Directory.CreateDirectory(targetMeta);
            bool secondInstallFailed = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderFileTransaction.CommitValidatedPair(
                    goodPng, targetPng, goodMeta, targetMeta);
            }
            catch (IOException) { secondInstallFailed = true; }
            Check(secondInstallFailed &&
                  File.ReadAllBytes(targetPng).SequenceEqual(priorPng) &&
                  !File.Exists(targetPng + ".rainwright.bak") &&
                  Directory.Exists(targetMeta),
                "Partial Render Map pair commit failed to restore the prior PNG.");
        }
        finally
        {
            Directory.Delete(renderTransactionRoot, recursive: true);
        }

        }
        finally
        {
            Directory.Delete(stagedRoot, recursive: true);
        }

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState).Assembly == owner,
            "Player Map preflight and render preparation views must be Rainwright-owned.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Clear();
        var pendingRender = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationSnapshot
        {
            Running = true,
            ExportRequested = true,
            CanCancel = true,
            ReadyRooms = 3,
            TotalRooms = 4,
            Progress = .75f,
            Detail = "One room pending"
        };
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Publish(pendingRender);
        Check(ReferenceEquals(
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Current,
                  pendingRender) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Current.Progress == .75f,
            "Render preparation mailbox lost progress shared with its frontend.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Clear();
        Check(!DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Current.Running &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Current.CanCancel &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderPreparationState.Current.Detail.Length == 0,
            "Render preparation mailbox left cancellable state after editor shutdown.");
        var preflightView = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightSnapshot
        {
            Available = true, RegionName = "SU", ReadyRooms = 2, FailedRooms = 1,
            Errors = new[] { "One room is not ready" }, CanRender = false
        };
        Check(preflightView.ReadyRooms == 2 &&
              preflightView.FailedRooms == 1 && !preflightView.CanRender &&
              preflightView.Errors[0] == "One room is not ready",
            "Detached Player Map preflight diagnostics dropped the authoring safety payload.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapNodeAnchorSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPreviewRun).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPixelKind).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus).Assembly == owner,
            "Player Map bake status, geometry preview and node anchor ABI must be Rainwright-owned.");
        var transferredPlayerMapNodeAnchor = new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapNodeAnchorSnapshot(
            8, 14.25f, 9.5f, 20, 10,
            DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPixelKind.RoomExit);
        var transferredPlayerMapBakeView = new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeSnapshot
        {
            Status = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready,
            Width = 64, Height = 32,
            Runs = new[]
            {
                new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPreviewRun(
                    2, 4, 6, DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPixelKind.Solid, false)
            },
            NodeAnchors = new[] { transferredPlayerMapNodeAnchor }
        };
        Check(transferredPlayerMapBakeView.TryGetNodeAnchor(8, out var transferredPlayerMapFoundAnchor) &&
              transferredPlayerMapFoundAnchor.EntranceX == 14.25f &&
              transferredPlayerMapFoundAnchor.Kind == DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPixelKind.RoomExit &&
              transferredPlayerMapBakeView.Runs[0].Length == 6 &&
              transferredPlayerMapBakeView.Status == DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready &&
              !transferredPlayerMapBakeView.TryGetNodeAnchor(9, out _),
            "Rainwright bake result lost pixel-run or authored room node identity.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightCore).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightRoom).Assembly == owner,
            "Render Map preflight calculation must reside in Rainwright.dll.");
        var rainwrightPreflightRooms = new[]
        {
            new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightRoom
            {
                RoomIndex = 1, Name = "SU_A01", X = 0, Y = 0, Layer = 1,
                Bake = new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeSnapshot
                {
                    Status = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready,
                    Width = 16, Height = 12,
                    NodeAnchors = new[]
                    {
                        new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapNodeAnchorSnapshot(
                            0, 2, 3, 1, 1,
                            DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPixelKind.RoomExit)
                    }
                }
            },
            new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightRoom
            {
                RoomIndex = 2, Name = "SU_A02", X = 120, Y = 0, Layer = 1,
                Bake = new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeSnapshot
                {
                    Status = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready,
                    Width = 16, Height = 12,
                    NodeAnchors = new[]
                    {
                        new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapNodeAnchorSnapshot(
                            1, 2, 3, 1, 1,
                            DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapPixelKind.RoomExit)
                    }
                }
            }
        };
        var rainwrightPreflightConnections = new[]
        {
            new DryCycle.DevUI.DevTool.Map.EditorMapConnectionSnapshot
            {
                FromRoomIndex = 1, FromNodeIndex = 0, ToRoomIndex = 2, ToNodeIndex = 1
            }
        };
        var rainwrightPreflightResult = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightCore.Build(
            "SU", rainwrightPreflightRooms, rainwrightPreflightConnections);
        Check(rainwrightPreflightResult.CanRender && rainwrightPreflightResult.OutputSafe &&
              rainwrightPreflightResult.ReadyRooms == 2 &&
              rainwrightPreflightResult.ExactConnections == 1 &&
              rainwrightPreflightResult.InvalidEndpoints == 0 &&
              rainwrightPreflightResult.Width > 0 && rainwrightPreflightResult.Height > 0,
            "Rainwright preflight rejected two ready rooms with valid RoomExit endpoints.");
        var rainwrightBadEndpoint = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightCore.Build(
            "SU", rainwrightPreflightRooms, new[]
            {
                new DryCycle.DevUI.DevTool.Map.EditorMapConnectionSnapshot
                {
                    FromRoomIndex = 1, FromNodeIndex = 4,
                    ToRoomIndex = 2, ToNodeIndex = 1
                }
            });
        Check(!rainwrightBadEndpoint.CanRender && rainwrightBadEndpoint.InvalidEndpoints == 1,
            "Preflight failed to reject a missing baked RoomExit node.");

        var rainwrightHugeRoomBounds = new[]
        {
            new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightRoom
            {
                RoomIndex = 1, Name = "SU_HUGE", X = -float.MaxValue, Y = 0, Layer = 0,
                Bake = new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeSnapshot
                {
                    Status = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready,
                    Width = 4, Height = 4
                }
            },
            new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightRoom
            {
                RoomIndex = 2, Name = "SU_HUGE2", X = float.MaxValue, Y = 0, Layer = 0,
                Bake = new DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeSnapshot
                {
                    Status = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready,
                    Width = 4, Height = 4
                }
            }
        };
        var rainwrightHugePreflight = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPreflightCore.Build(
            "SU", rainwrightHugeRoomBounds, Array.Empty<DryCycle.DevUI.DevTool.Map.EditorMapConnectionSnapshot>());
        Check(!rainwrightHugePreflight.CanRender && !rainwrightHugePreflight.OutputSafe &&
              rainwrightHugePreflight.Errors.Length > 0,
            "Extreme but finite Player Map coordinates bypassed the PNG size guard.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapBakeReadiness).Assembly == owner,
            "Player Map bake readiness policy must ship in Rainwright.dll.");
        var rainwrightBakeReadiness = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapBakeReadiness();
        Check(rainwrightBakeReadiness.HardFailure && !rainwrightBakeReadiness.NeedsPreparation,
            "Empty map preparation must fail closed.");
        rainwrightBakeReadiness.Observe(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Ready);
        rainwrightBakeReadiness.Observe(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Pending);
        Check(!rainwrightBakeReadiness.HardFailure && rainwrightBakeReadiness.NeedsPreparation &&
              rainwrightBakeReadiness.ReadyRooms == 1 &&
              rainwrightBakeReadiness.PendingRooms == 1,
            "Ready+pending rooms must wait without rejecting the render.");
        rainwrightBakeReadiness.Observe(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBakeStatus.Failed);
        Check(rainwrightBakeReadiness.HardFailure && !rainwrightBakeReadiness.NeedsPreparation,
            "A failed room bake must cancel pending Player Map render preparation.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapTextDecoder).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapSemanticCompiler).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapBake).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapSource).Assembly == owner,
            "Player Map room-text decoding and semantic tile compiler must be Rainwright-owned.");
        var rainwrightRoomLines = new string[12];
        rainwrightRoomLines[1] = "2*2|-1";
        rainwrightRoomLines[11] = "0|0|0|0|";
        var rainwrightRoomSource = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapTextDecoder.Parse(
            rainwrightRoomLines);
        var rainwrightRoomBake = DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapSemanticCompiler.Compile(
            21, "SU_TEST", rainwrightRoomSource, "synthetic-room.txt");
        Check(rainwrightRoomSource.Width == 2 && rainwrightRoomSource.Height == 2 &&
              rainwrightRoomBake.RoomIndex == 21 && rainwrightRoomBake.Width == 2 &&
              rainwrightRoomBake.Height == 2,
            "Detached Rainwright room text decoder/geometry compiler lost its room dimensions.");
        rainwrightRoomLines[1] = "3000*2000|-1";
        bool rainwrightOversizeRoomRejected = false;
        try { DryCycle.DevUI.DevTool.Map.PlayerMap.RoomMapTextDecoder.Parse(rainwrightRoomLines); }
        catch (InvalidDataException) { rainwrightOversizeRoomRejected = true; }
        Check(rainwrightOversizeRoomRejected,
            "Room geometry source exceeding four million tiles escaped validation.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomNamePolicy).Assembly == owner,
            "Player Map room eligibility compatibility policy must be Rainwright-owned.");
        Check(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomNamePolicy.IsOffscreenDenName(" offscreenDEN_2 ") &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomNamePolicy.IsOffscreenDenName("OffscreenDen") &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomNamePolicy.IsOffscreenDenName("SU_A01") &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRoomNamePolicy.IsOffscreenDenName("OffscreenDen-Visible"),
            "OffscreenDen fallback classification silently changed for real room names.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapAuthoringFingerprint).Assembly == owner,
            "Player Map render preparation's author identity must live in Rainwright.dll.");
        var rainwrightAuthorIdentity = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapAuthoringFingerprint.Begin();
        rainwrightAuthorIdentity.ObserveRoom(7, 3.5f, -2f, 1, false,
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPlacementMode.Derived);
        rainwrightAuthorIdentity.ObserveMaterial(12, 1f, 2f, 3f, 4f, true);
        ulong oldAuthorIdentity = 1469598103934665603UL;
        void LegacyAuthorMix(int number)
        {
            unchecked
            {
                oldAuthorIdentity ^= (uint)number;
                oldAuthorIdentity *= 1099511628211UL;
            }
        }
        LegacyAuthorMix(7);
        LegacyAuthorMix(3.5f.GetHashCode());
        LegacyAuthorMix((-2f).GetHashCode());
        LegacyAuthorMix(1);
        LegacyAuthorMix(0);
        LegacyAuthorMix(0);
        LegacyAuthorMix(12);
        LegacyAuthorMix(1f.GetHashCode());
        LegacyAuthorMix(2f.GetHashCode());
        LegacyAuthorMix(3f.GetHashCode());
        LegacyAuthorMix(4f.GetHashCode());
        LegacyAuthorMix(1);
        Check(rainwrightAuthorIdentity.Value == oldAuthorIdentity,
            "Rainwright author fingerprint changed existing render-preparation invalidation semantics.");
        var changedAuthorIdentity = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapAuthoringFingerprint.Begin();
        changedAuthorIdentity.ObserveRoom(7, 3.5f, -2f, 1, false,
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapPlacementMode.Derived);
        changedAuthorIdentity.ObserveMaterial(12, 1f, 2f, 3f, 4f, false);
        Check(changedAuthorIdentity.Value != rainwrightAuthorIdentity.Value,
            "Changing authored default material must invalidate pending render preparation.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigDocumentComposer).Assembly == owner,
            "Map config merge transaction must belong to Rainwright.dll.");
        var composedRoomRecords = new System.Collections.Generic.Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["SU_A01"] = "SU_A01: 10><20><1><2><1><NONE><96><105",
            ["SU_A02"] = "SU_A02: 40><50><3><4><2><NONE><80><90"
        };
        string oldRoom = "su_a01: 0><0><0><0><0><NONE><96><105";
        string thirdPartyRoomExtension = "SU_A01: 0><0><0><0><0><NONE><96><105><foreign";
        string roomAttrs = "Room_Attr: special><kept";
        var composed = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigDocumentComposer.Compose(
            new[]
            {
                "# preserve comment", oldRoom, thirdPartyRoomExtension, roomAttrs,
                "Def_Mat: old material", "Connection: old connection",
                "SpawnMigrationStream: obsolete",
                "plugin_flag: literal payload",
                "DryCycle_PlayerMap_HiddenRooms: v1|obsolete"
            },
            composedRoomRecords, new[] { "Def_Mat: new" }, new[] { "Connection: new" },
            new[] { "SpawnMigrationStream: new" }, true,
            "DryCycle_PlayerMap_HiddenRooms: v1|");
        Check(composed.Count(x => x == composedRoomRecords["SU_A01"]) == 1 &&
              composed.Count(x => x == composedRoomRecords["SU_A02"]) == 1 &&
              composed.Contains(thirdPartyRoomExtension) &&
              composed.Contains(roomAttrs) &&
              composed.Contains("plugin_flag: literal payload") &&
              composed.Contains("# preserve comment") &&
              !composed.Contains(oldRoom) &&
              !composed.Contains("Connection: old connection") &&
              !composed.Contains("SpawnMigrationStream: obsolete") &&
              composed[composed.Count - 1] == "DryCycle_PlayerMap_HiddenRooms: v1|",
            "Rainwright Map Config merge lost extension data or duplicated editor-managed blocks.");
        var keptStreams = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigDocumentComposer.Compose(
            new[] { "SpawnMigrationStream: foreign", thirdPartyRoomExtension },
            composedRoomRecords, Array.Empty<string>(), Array.Empty<string>(),
            Array.Empty<string>(), false, "DryCycle_PlayerMap_HiddenRooms: v1|");
        Check(keptStreams.Contains("SpawnMigrationStream: foreign") &&
              keptStreams.Contains(thirdPartyRoomExtension),
            "Map Config merge removed migration streams without an authoritative stream snapshot.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderStage).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressSnapshot).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState).Assembly == owner,
            "Render Map progress/phase ABI must be Rainwright-owned.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Clear();
        var renderMapProgress = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressSnapshot
        {
            Running = true,
            CanCancel = true,
            ExportRequested = true,
            Stage = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderStage.ComposeRooms,
            Progress = .3f,
            StageProgress = .5f,
            CompletedUnits = 10,
            TotalUnits = 20,
            StageLabel = "Compose rooms",
            Detail = "SU"
        };
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Publish(renderMapProgress);
        Check(ReferenceEquals(
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Current,
                  renderMapProgress) &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Current.Stage ==
                  DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderStage.ComposeRooms &&
              DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Current.StageProgress == .5f,
            "Render progress state lost a coherent published frame.");
        DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Clear();
        Check(!DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Current.Running &&
              !DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderProgressState.Current.CanCancel,
            "Render progress leaked beyond editor shutdown.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigFileTransaction).Assembly == owner,
            "Rainwright must own the Player Map author file transaction and backup suffix.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigRollbackIncompleteException).Assembly == owner,
            "Config authoring recovery failures must keep one Rainwright-owned exception contract.");
        var incompleteConfigRollback = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigRollbackIncompleteException(
            "Inspect staged config", new AggregateException(new IOException("Source write failed"),
                new IOException("Recovery rename failed")));
        Check(incompleteConfigRollback is IOException &&
              incompleteConfigRollback.InnerException is AggregateException &&
              incompleteConfigRollback.ToString().Contains("Recovery rename failed"),
            "Player Map config recovery exception omitted the original rollback error.");

        string rwMapTransactionRoot = Path.Combine(Path.GetTempPath(),
            "Rainwright-Map-Transaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rwMapTransactionRoot);
        try
        {
            string mapTarget = Path.Combine(rwMapTransactionRoot, "map_SU.txt");
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigFileTransaction.WriteAllLines(
                mapTarget, new[] { "custom: intact", "SU_A01: 0><0><0><0><1><NONE><96><105" });
            Check(File.ReadAllLines(mapTarget).Length == 2 &&
                  !File.Exists(mapTarget + ".rainwright.tmp") &&
                  !File.Exists(mapTarget + ".drycycle.tmp"),
                "First Rainwright Player Map author file save staged into the wrong owner.");
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigFileTransaction.WriteAllLines(
                mapTarget, new[] { "updated", null });
            Check(File.ReadAllLines(mapTarget).SequenceEqual(new[] { "updated", "" }) &&
                  !File.Exists(mapTarget + ".rainwright.bak") &&
                  !File.Exists(mapTarget + ".rainwright.tmp"),
                "Rainwright replacement left stale backups or corrupted the new map content.");


            File.WriteAllText(mapTarget + ".rainwright.tmp", "earlier interrupted staging");
            File.WriteAllText(mapTarget + ".drycycle.tmp", "legacy staging must survive");
            DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigFileTransaction.WriteAllLines(
                mapTarget, new[] { "non-destructive new save" });
            Check(File.ReadAllText(mapTarget + ".rainwright.tmp") == "earlier interrupted staging" &&
                  File.ReadAllText(mapTarget + ".drycycle.tmp") == "legacy staging must survive" &&
                  File.ReadAllText(mapTarget).Trim() == "non-destructive new save",
                "New Player Map save destroyed a prior Rainwright/legacy staging file.");
            File.Delete(mapTarget + ".rainwright.tmp");
            File.Delete(mapTarget + ".drycycle.tmp");

            File.WriteAllText(mapTarget + ".rainwright.bak", "old recoverable version");
            bool existingBackupBlocked = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigFileTransaction.WriteAllLines(
                    mapTarget, new[] { "must not overwrite" });
            }
            catch (IOException) { existingBackupBlocked = true; }
            Check(existingBackupBlocked &&
                  File.ReadAllText(mapTarget + ".rainwright.bak") == "old recoverable version" &&
                  File.ReadAllText(mapTarget).Trim() == "non-destructive new save",
                "Map config save discarded an earlier recoverable backup while output existed.");
            File.Delete(mapTarget + ".rainwright.bak");

            File.Delete(mapTarget);
            File.WriteAllText(mapTarget + ".rainwright.bak", "recovery data");
            bool orphanBackupBlocked = false;
            try
            {
                DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigFileTransaction.WriteAllLines(
                    mapTarget, new[] { "new map" });
            }
            catch (IOException) { orphanBackupBlocked = true; }
            Check(orphanBackupBlocked && !File.Exists(mapTarget) &&
                  File.ReadAllText(mapTarget + ".rainwright.bak") == "recovery data",
                "Interrupted Player Map transaction overwrote the only recoverable author backup.");
        }
        finally
        {
            Directory.Delete(rwMapTransactionRoot, recursive: true);
        }

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderReport).Assembly == owner,
            "Render Map success/failure reports must live in Rainwright.dll.");
        var rainwrightMapFailure = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderReport.Failure(
            "Render stopped", "Source room is unavailable");
        Check(rainwrightMapFailure.Attempted && !rainwrightMapFailure.Success &&
              rainwrightMapFailure.Errors.Length == 1 &&
              rainwrightMapFailure.Errors[0] == "Source room is unavailable",
            "Render Map error details were lost in the independent result contract.");

        var fallbackConfig = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigDocumentComposer.Compose(
            new[]
            {
                "Connection: legacy author kept",
                "SpawnMigrationStream: legacy world kept",
                "SU_A01: 0><0><0><0><1><NONE><96><105",
                thirdPartyRoomExtension
            },
            composedRoomRecords,
            new[] { "Def_Mat: safe fallback" }, Array.Empty<string>(),
            Array.Empty<string>(), false, "DryCycle_PlayerMap_HiddenRooms: v1|",
            replaceConnections: false);
        Check(fallbackConfig.Contains("Connection: legacy author kept") &&
              fallbackConfig.Contains("SpawnMigrationStream: legacy world kept") &&
              fallbackConfig.Contains(thirdPartyRoomExtension) &&
              fallbackConfig.Contains("Def_Mat: safe fallback") &&
              !fallbackConfig.Contains("Connection: new"),
            "Legacy Player Map save fallback discarded unrelated connection/migration author data.");

        var duplicateRoomSource = DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapConfigDocumentComposer.Compose(
            new[]
            {
                "SU_A01: 0><0><0><0><0><NONE><96><105",
                "su_a01: 1><1><1><1><1><NONE><96><105",
                thirdPartyRoomExtension,
                "third_party_data: preserve"
            },
            composedRoomRecords,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
            false, "DryCycle_PlayerMap_HiddenRooms: v1|", replaceConnections: false);
        Check(duplicateRoomSource.Count(x => x == composedRoomRecords["SU_A01"]) == 1 &&
              duplicateRoomSource.Count(x => x == composedRoomRecords["SU_A02"]) == 1 &&
              duplicateRoomSource.Contains(thirdPartyRoomExtension) &&
              duplicateRoomSource.Contains("third_party_data: preserve"),
            "Player Map authoring merge duplicated a vanilla room or removed a third-party extension.");

        Check(typeof(DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderRevisionSnapshotGuard).Assembly == owner,
            "Render Map frozen-revision safety state must be owned by Rainwright.dll.");
        var renderRevisionGuard = new DryCycle.DevUI.DevTool.Map.PlayerMap.PlayerMapRenderRevisionSnapshotGuard();
        Check(renderRevisionGuard.HasChanged(1, 2, 3),
            "Uncaptured Render Map revision must not appear stable.");
        renderRevisionGuard.Capture(1, 2, 3);
        Check(renderRevisionGuard.Captured && !renderRevisionGuard.HasChanged(1, 2, 3) &&
              renderRevisionGuard.HasChanged(2, 2, 3) &&
              renderRevisionGuard.HasChanged(1, 3, 3) &&
              renderRevisionGuard.HasChanged(1, 2, 4),
            "Render Map revision guard failed to detect authoring, bake or terrain changes.");
        renderRevisionGuard.Clear();
        Check(!renderRevisionGuard.Captured && renderRevisionGuard.HasChanged(1, 2, 3),
            "A previous Render Map job revision survived editor shutdown.");
        var commonInbox = new EditorCommandInbox<int>(2);
        Check(commonInbox.TryEnqueue(1) && commonInbox.TryEnqueue(2) &&
              !commonInbox.TryEnqueue(3) && commonInbox.ConsumeRejectedCount() == 1 &&
              commonInbox.PendingCount == 2,
            "Shared native editor inbox failed to enforce bounded input.");
        commonInbox.Clear();
        Check(commonInbox.PendingCount == 0, "Shared inbox did not release queued edits.");
        // Exercise the public cross-assembly API, not source files linked into this test.
        bool dirty = true;
        int saves = 0;
        using (EditorAuthoringParticipants.Register(
            "assembly-ownership-smoke",
            () => dirty,
            () => { saves++; dirty = false; return true; },
            (out string path, out string content) => { path = "smoke.txt"; content = "contents"; return true; }))
        {
            Check(EditorAuthoringParticipants.HasDirty, "Active provider not visible through Rainwright.");
            bool captured = false;
            Check(EditorAuthoringParticipants.CaptureDirty((id, path, content) =>
            {
                Check(id == "assembly-ownership-smoke" && path == "smoke.txt" && content == "contents",
                    "Provider capture payload changed across the assembly boundary.");
                captured = true;
            }) && captured, "Dirty recovery capture failed.");
            Check(EditorAuthoringParticipants.SaveDirty() && saves == 1 && !dirty,
                "Authoring save was not routed to the registered provider.");
        }

        using (EditorAuthoringParticipants.Register("reuse-id", () => false, () => true, null))
        {
            bool rejected = false;
            try { using var duplicate = EditorAuthoringParticipants.Register("REUSE-ID", () => false, () => true, null); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Duplicate case-insensitive Provider IDs were accepted.");
        }
        using (EditorAuthoringParticipants.Register("reuse-id", () => false, () => true, null)) { }
        Check(!EditorAuthoringParticipants.HasDirty, "Disposed Provider leaked static registry state.");
        // The independent core may be consumed by a DryCycle adapter, but must
        // not silently gain a reverse assembly reference to that gameplay host.
        foreach (var reference in owner.GetReferencedAssemblies())
            Check(reference.Name != "DryCycle" &&
                  !reference.Name.StartsWith("DryCycle.", StringComparison.Ordinal),
                "Rainwright.dll must not depend on a DryCycle host assembly: " + reference.Name);
        Check(typeof(WorldDocument).Assembly == owner, "World document has a duplicate assembly identity.");
        Check(typeof(SoundGroupLibrarySnapshot).Assembly == owner, "Sound snapshots must live in Rainwright.");
        Check(ReferenceEquals(SoundGroupLibrarySnapshot.Empty, SoundGroupLibrarySnapshot.Empty),
            "Sound snapshot singleton identity split unexpectedly.");

        const string source =
            "ROOMS\r\nSU_A01 : SU_A02 : SHELTER\r\nSU_A02 : SU_A01\r\nEND ROOMS\r\n" +
            "UNKNOWN MOD SECTION\r\nkeep \u672a\u77e5 data\r\n";
        WorldDocument world = WorldDocument.Parse(source, "world_SU.txt");
        Check(world.Serialize() == source, "Lossless world.txt parse altered unknown source text.");
        Check(world.TryGetConnection("SU_A01", 0, out string initial) && initial == "SU_A02",
            "World route parser lost the existing room endpoint.");
        Check(!world.TrySetConnection("SU_A01", 0, "SU_A02") && !world.Dirty,
            "No-op world route edit must not mark the author file dirty.");
        Check(world.TrySetConnection("SU_A01", 0, "SU_A03") && world.Dirty,
            "World route authoring mutation was not registered.");
        string updated = world.Serialize();
        Check(updated.Contains("SU_A01 : SU_A03 : SHELTER") &&
              updated.Contains("UNKNOWN MOD SECTION\r\nkeep \u672a\u77e5 data\r\n"),
            "World route edit lost tags or opaque third-party content.");
        world.MarkSaved();
        Check(!world.Dirty && world.Serialize() == updated,
            "MarkSaved must preserve the persisted world authoring result.");

        // Resource lookup resolves from the actual DLL containing Cartography's
        // embedded PNG bytes rather than from DryCycle's gameplay assembly.
        Check(typeof(CartographyResourceLocator).Assembly == owner,
            "Cartography resource locator has a duplicate assembly identity.");
        using (var spriteSheet = CartographyResourceLocator.Open(owner, "Objects"))
        {
            Check(spriteSheet != null, "Cartography sprite sheet was not packed into Rainwright.");
            byte[] png = new byte[8];
            Check(spriteSheet.Read(png, 0, png.Length) == png.Length &&
                  png[0] == 137 && png[1] == 80 && png[2] == 78 && png[3] == 71,
                "Rainwright sprite sheet is not a readable PNG.");
        }
        bool traversalRejected = false;
        try { using var invalid = CartographyResourceLocator.Open(owner, "../Objects"); }
        catch (ArgumentException) { traversalRejected = true; }
        Check(traversalRejected, "Cartography sprite lookup accepted an unsafe name.");

        // Real assembly-bound editor command execution. No linked production
        // editing sources and no game runtime, so these prove Rainwright ownership.
        var author = new CartographyDocument { Identity = "smoke|SU", Region = "SU" };
        author.Layers.Add(new CartographyLayer { Id = "links", Name = "Connections" });
        author.Layers.Add(new CartographyLayer { Id = "notes", Name = "Annotations" });
        author.Items.Add(new CartographyItem
        {
            Id = "note-1", Kind = CartographyItemKind.Text,
            LayerId = "notes", Text = "Original", X = 12, Y = 30
        });
        Check(typeof(CartographyCommand).Assembly == owner &&
              typeof(CartographyEditEngine).Assembly == owner,
            "Cartography commands were not extracted to Rainwright.dll.");
        var move = new CartographyCommand
        {
            Kind = CartographyCommandKind.Move, Ids = new[] { "note-1" }, X = 9, Y = -4
        };
        var edited = CartographyEditEngine.Apply(author, move);
        Check(author.Items[0].X == 12 && author.Items[0].Y == 30 &&
              edited.Items[0].X == 21 && edited.Items[0].Y == 26,
            "Rainwright edit engine mutated its input or lost the move delta.");
        var aligned = CartographyEditEngine.Apply(edited,
            new CartographyCommand { Kind = CartographyCommandKind.Align,
                Ids = new[] { "note-1", "note-2" } },
            bounds: i => new CartographyRect(i.X, i.Y, i.Width, i.Height));
        Check(aligned.Items[0].X == edited.Items[0].X,
            "A single-item align should be a no-op.");
        bool inspectorRejected = false;
        try
        {
            CartographyEditEngine.Apply(edited,
                new CartographyCommand { Kind = CartographyCommandKind.UpdateItem,
                    Item = new CartographyItem { Id = "note-1", Kind = CartographyItemKind.Room,
                        LayerId = "notes", Room = "SU_A01" } });
        }
        catch (InvalidOperationException) { inspectorRejected = true; }
        Check(inspectorRejected && edited.Items[0].Kind == CartographyItemKind.Text,
            "Invalid inspector mutations must fail atomically.");
        var generated = CartographyEditEngine.Apply(edited,
            new CartographyCommand { Kind = CartographyCommandKind.AddMissingRooms },
            addMissingRooms: d => d.Items.Add(new CartographyItem
            {
                Id = "room:SU_A01", Kind = CartographyItemKind.Room,
                Room = "SU_A01", LayerId = "notes"
            }));
        Check(generated.Items.Count == 2 && edited.Items.Count == 1,
            "Room source callbacks must only change the next author revision.");
        Check(!CartographyEditEngine.SameItem(edited.Items[0], generated.Items[1]),
            "Rainwright editor item identity comparison regressed.");

        // Route selection/handle math executes against Rainwright.dll, not
        // scene or game source. A rendered path owns its terminal ports.
        Check(typeof(CartographyRouteGeometry).Assembly == owner,
            "Route gesture geometry must belong to Rainwright.");
        CartographyPoint[] path =
        {
            new() { X = 0, Y = 0 }, new() { X = 10, Y = 0 },
            new() { X = 10, Y = 10 }, new() { X = 20, Y = 10 }
        };
        int segment = CartographyRouteGeometry.NearestSegment(path, 6, 2, 4, out var nearest);
        Check(segment == 0 && nearest.X == 6 && nearest.Y == 0,
            "Route nearest-segment search lost its geometric projection.");
        Check(CartographyRouteGeometry.NearestSegment(path, 80, 80, 3, out _) == -1,
            "Route hit testing accepted a distant segment.");
        Check(CartographyRouteGeometry.NearestHandle(path, 10, 0, 2) == 0 &&
              CartographyRouteGeometry.NearestHandle(path, 0, 0, 2) == -1 &&
              CartographyRouteGeometry.NearestHandle(path, 20, 10, 2) == -1,
            "Room port endpoints must never become editable route handles.");
        var manualRoute = new CartographyItem
        {
            Kind = CartographyItemKind.Connection,
            Appearance = new CartographyAppearance { Route = CartographyRouteMode.Straight }
        };
        var manual = CartographyRouteGeometry.Manual(manualRoute, path);
        Check(manual.Appearance.Route == CartographyRouteMode.Manual &&
              manual.Points.Count == 2 && manual.Points[0].X == 10 &&
              manual.Points[1].Y == 10 && manualRoute.Points.Count == 0,
            "Converting rendered connections to manual must clone only internal handles.");
        var constrained = CartographyRouteGeometry.Drag(7, 3,
            new CartographyPoint { X = 0, Y = 0 }, true);
        Check(constrained.X == 7 && constrained.Y == 0,
            "Axis-constrained author gestures regressed.");
        // File catalog and author source fingerprint run in Rainwright.dll.
        // Only game-side loaders may turn these paths into world geometry.
        Check(typeof(CartographyRegionFiles).Assembly == owner &&
              typeof(CartographyRegionOption).Assembly == owner,
            "Cartography source path catalog must have one Rainwright owner.");
        string tmp = Path.Combine(Path.GetTempPath(), "rainwright-region-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            string file = Path.Combine(tmp, "world_su.txt");
            File.WriteAllText(file, "ROOMS\\nSU_A01 : DISCONNECTED\\nEND ROOMS");
            var captured = CartographyRegionFiles.Capture("SU", directory =>
                directory == "world/su" ? new[] { file } : Array.Empty<string>());
            Check(captured.Region == "SU" &&
                  captured.Resolve("world/su/world_su.txt") == Path.GetFullPath(file) &&
                  captured.Read("world/su/world_su.txt").StartsWith("ROOMS"),
                "Rainwright source resolver lost the captured author file.");
            string beforeFileEdit = captured.Fingerprint();
            File.AppendAllText(file, "\\n// changed author data");
            Check(captured.Fingerprint() != beforeFileEdit,
                "Resolved source fingerprint failed to invalidate when the file changed.");
            var found = CartographyRegionFiles.Catalog(new[] { "SU", "DS" },
                relative => relative.Equals("world/SU/world_SU.txt", StringComparison.OrdinalIgnoreCase)
                    ? file : Path.Combine(tmp, "missing.txt"),
                code => "Region " + code, name => "Localized " + name);
            Check(found.Length == 1 && found[0].Code == "SU" &&
                  found[0].Label(true).Contains("Localized"),
                "Rainwright region catalog must filter absent worlds and preserve translations.");

            // Exercise cache-hit, explicit refresh, LRU eviction and source
            // mutation detection using the production Rainwright assembly.
            Check(typeof(CartographySourceFingerprintCache<string>).Assembly == owner,
                "Region fingerprint cache must reside in Rainwright.");
            var cache = new CartographySourceFingerprintCache<string>(2);
            int reads = 0;
            Func<CartographyRegionFiles, int, string> decoder = (snapshot, _) =>
            {
                reads++;
                return snapshot.Read("world/su/world_su.txt");
            };
            string first = cache.Load("a", captured, false, decoder, 0, out bool cacheHit);
            Check(!cacheHit && reads == 1, "First fingerprinted source lookup must decode.");
            string again = cache.Load("a", captured, false, decoder, 0, out cacheHit);
            Check(cacheHit && reads == 1 && ReferenceEquals(first, again),
                "Cache hits must preserve the same decoded snapshot without reloading.");
            cache.Load("a", captured, true, decoder, 0, out cacheHit);
            Check(!cacheHit && reads == 2, "Explicit refresh must bypass the cache.");
            File.AppendAllText(file, "\\ncache invalidate");
            cache.Load("a", captured, false, decoder, 0, out cacheHit);
            Check(!cacheHit && reads == 3, "Changed source files must invalidate decoded cache.");
            cache.Load("b", captured, false, decoder, 0, out cacheHit);
            cache.Load("a", captured, false, decoder, 0, out cacheHit);
            cache.Load("c", captured, false, decoder, 0, out cacheHit);
            Check(cache.Count == 2 && reads == 5,
                "LRU must evict the least recently touched identity within the capacity.");
            cache.Load("b", captured, false, decoder, 0, out cacheHit);
            Check(!cacheHit && reads == 6 && cache.Count == 2,
                "An evicted identity must be decoded again.");
            bool unstableRejected = false;
            try
            {
                cache.Load("unstable", captured, false,
                    (snapshot, _) => { File.AppendAllText(file, "x"); return "uncommitted"; },
                    0, out cacheHit);
            }
            catch (IOException) { unstableRejected = true; }
            Check(unstableRejected && cache.Count == 2,
                "Files changing during decode must reject the snapshot without cache poisoning.");
        }
        finally { Directory.Delete(tmp, recursive: true); }

        // Decode the real output of the production PNG encoder on a modern
        // runtime without loading System.Drawing or the game renderer.
        Check(typeof(CartographyPngStreamEncoder).Assembly == owner,
            "PNG stream encoding must be owned by Rainwright.");
        using var encodedPngStream = new MemoryStream();
        CartographyPngStreamEncoder.Write(encodedPngStream, 2, 2, (top, rows, consume) =>
        {
            for (int y = top; y < top + rows; y++)
                consume(new[] { unchecked((int)0x11223344), unchecked((int)0xFFEEDDCC) });
        });
        byte[] encoded = encodedPngStream.ToArray();
        Check(encoded.Length > 50 && encoded[0] == 137 && encoded[1] == 80 &&
              encoded[2] == 78 && encoded[3] == 71,
            "Rainwright PNG encoder emitted an invalid signature.");
        using var compressed = new MemoryStream();
        int position = 8;
        bool sawHeader = false, sawEnd = false;
        while (position + 12 <= encoded.Length)
        {
            int size = (encoded[position] << 24) | (encoded[position + 1] << 16) |
                       (encoded[position + 2] << 8) | encoded[position + 3];
            string kind = Encoding.ASCII.GetString(encoded, position + 4, 4);
            if (size < 0 || position + 12 + size > encoded.Length)
                throw new InvalidDataException("Invalid output PNG chunk size.");
            if (kind == "IHDR")
            {
                sawHeader = size == 13 && encoded[position + 11] == 2 &&
                            encoded[position + 12] == 0;
            }
            if (kind == "IDAT") compressed.Write(encoded, position + 8, size);
            if (kind == "IEND") sawEnd = true;
            position += 12 + size;
        }
        Check(sawHeader && sawEnd && compressed.Length > 2,
            "PNG headers, data chunks or trailer missing.");
        compressed.Position = 0;
        using var inflate = new ZLibStream(compressed, CompressionMode.Decompress, true);
        byte[] rgba = new byte[18];
        int received = 0;
        while (received < rgba.Length)
        {
            int n = inflate.Read(rgba, received, rgba.Length - received);
            if (n == 0) break;
            received += n;
        }
        Check(received == 18 && rgba[0] == 0 && rgba[1] == 0x22 &&
              rgba[2] == 0x33 && rgba[3] == 0x44 && rgba[4] == 0x11 &&
              rgba[5] == 0xEE && rgba[8] == 0xFF && rgba[9] == 0,
            "PNG RGBA channel ordering, scanlines, compression or alpha changed.");
        bool invalidRows = false;
        try
        {
            using var missing = new MemoryStream();
            CartographyPngStreamEncoder.Write(missing, 2, 1, (top, rows, consume) => { });
        }
        catch (InvalidDataException) { invalidRows = true; }
        Check(invalidRows, "Missing raster scanlines must not silently create a truncated PNG.");

        // Render plan must be identical for canvas, flattened exports and
        // layered export. Legacy links render above rooms without changing
        // persisted layer ordering or losing guide/under-room geometry.
        Check(typeof(CartographyRenderLayerPlanner).Assembly == owner,
            "Render pass planner is not owned by Rainwright.dll.");
        var layered = new CartographyDocument { Identity = "planner|SU", Region = "SU" };
        layered.Layers.Add(new CartographyLayer { Id = "links", Name = "Connections" });
        layered.Layers.Add(new CartographyLayer { Id = "rooms", Name = "Rooms" });
        layered.Layers.Add(new CartographyLayer { Id = "notes", Name = "Notes", Visible = false });
        var nodePlans = new[]
        {
            new CartographyRenderNode<string>("route", "links", false, true, false, false, false),
            new CartographyRenderNode<string>("terrain", "rooms", true, false, false, true, false),
            new CartographyRenderNode<string>("outline", "rooms", false, false, true, false, false),
            new CartographyRenderNode<string>("marker", "rooms", false, false, false, true, false),
            new CartographyRenderNode<string>("guide", "rooms", false, false, false, false, true),
            new CartographyRenderNode<string>("hidden", "notes", false, false, false, true, false)
        };
        var batches = CartographyRenderLayerPlanner.Build(layered, nodePlans);
        Check(batches.Length == 6 &&
              batches[0].Pass == CartographyRenderPass.UnderRooms &&
              batches[0].LayerId == "rooms" && batches[0].Nodes[0] == "outline" &&
              batches[1].LayerId == "links" && batches[1].Nodes[0] == "route" &&
              batches[2].Pass == CartographyRenderPass.Content &&
              batches[2].LayerId == "rooms" && batches[2].Nodes.Length == 2 &&
              batches[3].LayerId == "links" && batches[4].LayerId == "rooms" &&
              batches[5].LayerId == "links",
            "Room/link stacking, all route passes, visibility or guide order regressed.");
        Check(layered.Layers[0].Id == "links" &&
              batches[0].Name.EndsWith(" / Borders"),
            "Legacy link reordering must not mutate authored layers.");

        // Real Rainwright.dll cache identity, scene style invalidation and
        // prune are tested without compiling the host room/scene types.
        var retainedCache = new CartographySceneRetentionCache<object, string>();
        Check(typeof(CartographySceneRetentionCache<object, string>).Assembly == owner,
            "Retained scene policy must live in the Rainwright assembly.");
        var sourceRoom = new object();
        var cachedItem = new CartographyItem
        {
            Id = "room:SU_A01", LayerId = "rooms", Kind = CartographyItemKind.Room,
            Room = "SU_A01", X = 32
        };
        var cachedDocument = layered.Clone();
        cachedDocument.Items.Add(cachedItem);
        var cachedLayer = cachedDocument.Layer("rooms");
        retainedCache.Store(cachedDocument, cachedItem, cachedLayer,
            sourceRoom, "immutable-node", "author warning");
        Check(retainedCache.TryGet(cachedDocument, cachedItem, cachedLayer,
                  sourceRoom, out string retained, out string retainedWarning) &&
              retained == "immutable-node" && retainedWarning == "author warning",
            "Cache hit must return the retained node and its diagnostic text.");
        var changedName = cachedDocument.Clone();
        changedName.ShowRoomNames = !changedName.ShowRoomNames;
        Check(!retainedCache.TryGet(changedName, cachedItem, cachedLayer,
                  sourceRoom, out _, out _),
            "Room-name changes must invalidate retained geometry.");
        var changedTerrain = cachedDocument.Clone();
        changedTerrain.SolidTerrain = !changedTerrain.SolidTerrain;
        Check(!retainedCache.TryGet(changedTerrain, cachedItem, cachedLayer,
                  sourceRoom, out _, out _),
            "Solid terrain changes must invalidate scene cache.");
        var movedRoom = cachedItem.Clone(); movedRoom.X++;
        Check(!retainedCache.TryGet(cachedDocument, movedRoom, cachedLayer,
                  sourceRoom, out _, out _),
            "Scene cache returned a stale node after an author room move.");
        Check(!retainedCache.TryGet(cachedDocument, cachedItem, cachedLayer,
                  new object(), out _, out _),
            "Scene cache ignored source snapshot replacement.");
        var changedStyle = cachedDocument.Clone();
        changedStyle.Options.Borders = !changedStyle.Options.Borders;
        Check(!retainedCache.TryGet(changedStyle, cachedItem, cachedLayer,
                  sourceRoom, out _, out _),
            "Scene cache failed style fingerprint invalidation.");
        cachedDocument.Items.Clear();
        retainedCache.Prune(cachedDocument);
        Check(retainedCache.Count == 0,
            "Deleted author nodes must be removed from the retained cache.");

        // This hits the compiled Rainwright route index, not source-linked
        // test logic. The query and hit-test paths must remain thread-safe.
        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.WorldMapRouteSpatialCore).Assembly == owner,
            "World map route spatial index must be owned by Rainwright.dll.");
        var routeIndex = new DryCycle.DevUI.DevTool.RWImGui.WorldMapRouteSpatialCore();
        routeIndex.Upsert("world:link", new[] { new Num.Vector2(0, 0), new Num.Vector2(120, 0) });
        Check(routeIndex.Count == 1, "World map route was not inserted.");
        var regionHits = new System.Collections.Generic.List<string>();
        Check(routeIndex.Query(new Num.Vector2(20, -5), new Num.Vector2(25, 5), regionHits) &&
              regionHits.Count == 1 && regionHits[0] == "world:link",
            "Rainwright route spatial bucket failed to find an intersecting segment.");
        Check(routeIndex.TryHit(new Num.Vector2(40, 3), 5, null, out string hitId, out float distance2) &&
              hitId == "world:link" && Math.Abs(distance2 - 9f) < 0.01f,
            "Route hover hit testing failed its nearest segment distance.");
        Check(routeIndex.TryGetPoints("world:link", out var retainedPoints) && retainedPoints.Length == 2,
            "Route drawing lost its retained immutable point array.");
        routeIndex.Remove("world:link");
        Check(routeIndex.Count == 0 && !routeIndex.TryHit(new Num.Vector2(40, 3), 5,
              null, out _, out _), "Route removal leaked spatial entries.");
        routeIndex.Upsert("world:second", new[] { new Num.Vector2(0, 0), new Num.Vector2(5, 5) });
        routeIndex.Reset();
        Check(routeIndex.Count == 0, "Route index reset failed to release its routes.");

        // The full orthogonal router and marker geometry must be present
        // in the runtime DLL; production-linked functional route suites
        // exercise short paths, blockers, cache and lane selection separately.
        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.WorldMapOrthogonalRouter).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.RWImGui.WorldMapDirectionMarkerGeometry).Assembly == owner,
            "World Map route solving and direction geometry must ship inside Rainwright.dll.");
        var inferredPort = DryCycle.DevUI.DevTool.RWImGui.WorldMapOrthogonalRouter.InferPortDirection(
            new Num.Vector2(0f, 30f), new Num.Vector2(0f, 0f), new Num.Vector2(100f, 100f));
        Check(inferredPort == -Num.Vector2.UnitX,
            "Rainwright World Map physical port direction inference regressed.");

        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.WorldMapJunctionWeavePlanner).Assembly == owner,
            "Junction lane-transition geometry must be compiled into Rainwright.");
        Check(!DryCycle.DevUI.DevTool.RWImGui.WorldMapJunctionWeavePlanner.NeedsTransition(
                 new[] { 0f, 0f }, new[] { true, true }),
            "Identical lane offsets must not produce spurious junction weaving.");
        Check(!DryCycle.DevUI.DevTool.RWImGui.WorldMapJunctionWeavePlanner.NeedsTransition(
                 new[] { 0f, 20f }, new[] { false, false }),
            "Unassigned junction offsets must not produce ghost connectors.");
        Check(DryCycle.DevUI.DevTool.RWImGui.WorldMapJunctionWeavePlanner.Build(
                 new[] { Num.Vector2.Zero, Num.Vector2.UnitX },
                 new[] { 0f }, new[] { true }) == null,
            "Undersized junction route must not fabricate transition geometry.");

        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.WorldMapRoomSpatialCore).Assembly == owner,
            "World Map room spatial hash must be owned by Rainwright.dll.");
        var roomSpatial = new DryCycle.DevUI.DevTool.RWImGui.WorldMapRoomSpatialCore();
        roomSpatial.ApplyBatch(true, null, new[]
        {
            new DryCycle.DevUI.DevTool.RWImGui.WorldMapRoomIndexUpdate(
                11, 0, new Num.Vector2(0, 0), new Num.Vector2(40, 40)),
            new DryCycle.DevUI.DevTool.RWImGui.WorldMapRoomIndexUpdate(
                12, 1, new Num.Vector2(0, 0), new Num.Vector2(40, 40))
        });
        Check(roomSpatial.Count == 2 &&
              roomSpatial.TryHitRoom(new Num.Vector2(10, 10), 1 << 1, out int hitRoom) &&
              hitRoom == 12,
            "Room index failed highest-visible-layer hit testing.");
        var nearbyRooms = new System.Collections.Generic.List<int>();
        Check(roomSpatial.Query(new Num.Vector2(-4, -4), new Num.Vector2(50, 50),
              int.MaxValue, nearbyRooms) &&
              nearbyRooms.Count == 2 && nearbyRooms[0] == 11 && nearbyRooms[1] == 12,
            "Room spatial range query must preserve deterministic layer order.");
        roomSpatial.ApplyBatch(false, null, new[]
        {
            new DryCycle.DevUI.DevTool.RWImGui.WorldMapRoomIndexUpdate(
                12, 1, new Num.Vector2(500, 500), new Num.Vector2(540, 540),
                onlyIfMissing: true)
        });
        Check(roomSpatial.TryHitRoom(new Num.Vector2(10, 10), 1 << 1, out hitRoom) &&
              hitRoom == 12,
            "Port-only refresh must not replace existing room bounds.");
        roomSpatial.ApplyBatch(false, new[] { 12 }, null);
        Check(roomSpatial.Count == 1 && !roomSpatial.TryHitRoom(
              new Num.Vector2(10, 10), 1 << 1, out _),
            "Room index deletion must release stale spatial cells.");
        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.WorldMapRouteCrossingCore).Assembly == owner,
            "Crossing solver must be compiled into Rainwright.dll.");
        var crossingPaths = new System.Collections.Generic.Dictionary<string, Num.Vector2[]>
        {
            ["horizontal"] = new[]
            {
                new Num.Vector2(0, 80), new Num.Vector2(20, 80),
                new Num.Vector2(160, 80), new Num.Vector2(180, 80)
            },
            ["vertical"] = new[]
            {
                new Num.Vector2(80, 0), new Num.Vector2(80, 20),
                new Num.Vector2(80, 160), new Num.Vector2(80, 180)
            }
        };
        var crossingOutput = DryCycle.DevUI.DevTool.RWImGui.WorldMapRouteCrossingCore.Build(
            crossingPaths, static points => points);
        Check(crossingOutput.CandidateChecks > 0 && crossingOutput.Marks.Length > 0,
            "Rainwright route-crossing math lost valid internal crossing marks.");
        // Atlas cleanup is an editor-owned retry contract, not a native
        // plugin callback. Only successfully detached atlas names may leave
        // the ownership set after a failed unload.
        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.FrontendOwnedAtlasCleanup).Assembly == owner,
            "Frontend atlas cleanup must compile into Rainwright.dll.");
        var loadedAtlases = new System.Collections.Generic.HashSet<string>(
            StringComparer.Ordinal) { "ok", "pending", "already-gone" };
        int attempts = 0, diagnostics = 0;
        bool firstRelease = DryCycle.DevUI.DevTool.RWImGui.FrontendOwnedAtlasCleanup.Release(
            loadedAtlases,
            name => name != "already-gone",
            name =>
            {
                attempts++;
                if (name == "pending")
                    throw new InvalidOperationException("renderer busy");
            },
            (name, error) => diagnostics++);
        Check(!firstRelease && loadedAtlases.Count == 1 &&
              loadedAtlases.Contains("pending") && attempts == 2 &&
              diagnostics == 1,
            "Atlas cleanup must retain only failed unloads and report failures.");
        bool secondRelease = DryCycle.DevUI.DevTool.RWImGui.FrontendOwnedAtlasCleanup.Release(
            loadedAtlases, _ => true, _ => attempts++);
        Check(secondRelease && loadedAtlases.Count == 0 && attempts == 3,
            "Retry must clear ownership after the native unload succeeds.");
        // Real Rainwright DLL owns page chrome geometry and status snapshots.
        Check(typeof(DryCycle.DevUI.DevTool.RWImGui.DevToolPageStatusState).Assembly == owner &&
              typeof(DryCycle.DevUI.DevTool.RWImGui.DevToolWorkspaceLayout).Assembly == owner,
            "Shared page status/layout must be Rainwright-owned.");
        var key = new DryCycle.DevUI.DevTool.RWImGui.DevToolPageStatusState(
            3, 7, true, "room", "selected");
        var equal = new DryCycle.DevUI.DevTool.RWImGui.DevToolPageStatusState(
            3, 7, true, "room", "selected");
        var different = new DryCycle.DevUI.DevTool.RWImGui.DevToolPageStatusState(
            3, 7, false, "room", "selected");
        Check(key.Equals(equal) && key.GetHashCode() == equal.GetHashCode() &&
              !key.Equals(different),
            "Status hash and equality must detect page state changes.");
        DryCycle.DevUI.DevTool.RWImGui.DevToolWorkspaceLayout.GetCentralRect(
            new Num.Vector2(1920, 1080), out Num.Vector2 workspacePosition, out Num.Vector2 workspaceSize);
        Check(workspaceSize.X == 900f && workspaceSize.Y == 620f && workspacePosition.X == 510f && workspacePosition.Y == 230f,
            "Desktop central workspace changed its capped geometry.");
        DryCycle.DevUI.DevTool.RWImGui.DevToolWorkspaceLayout.GetCentralRect(
            new Num.Vector2(800, 600), out workspacePosition, out workspaceSize);
        Check(workspaceSize.X == 520f && workspaceSize.Y == 384f && workspacePosition.X == 180f && workspacePosition.Y == 108f,
            "Compact central workspace changed its minimum layout.");
        Console.WriteLine("Rainwright assembly ownership, UI layout/status and atlas release passed.");
    }
}
