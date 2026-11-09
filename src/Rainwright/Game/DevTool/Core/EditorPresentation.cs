using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Commands;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.History;
using DryCycle.DevUI.DevTool.Objects;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Core;

public static partial class EditorPresentationHub
{
    private static EditorPresentationSnapshot current
    {
        get => EditorPresentationState.Current;
        set => EditorPresentationState.Publish(value);
    }
    private static EditorObjectTypeSnapshot[] libraryCache = Array.Empty<EditorObjectTypeSnapshot>();
    private static int libraryTypeCount = -1;
    private static long libraryCatalogRevision = -1;

    private static EditorSession observedSession;
    private static global::Room observedRoom;
    private static global::DevInterface.Page observedPage;
    private static long observedShellRevision;
    private static long observedObjectRevision;
    private static long observedHistoryRevision;
    private static EditorHistoryService observedActiveHistory;
    private static long observedSelectionRevision;
    private static bool observedShellOnly;
    private static EditorToolMode observedToolMode;
    private static bool observedFocusMode;
    private static bool observedBrowserOpen;
    private static bool observedInspectorOpen;
    private static bool observedLegacyUiVisible;
    private static bool observedPlacementActive;
    private static string observedPlacementType = string.Empty;
    private static int observedObjectCount = -1;
    private static int observedSelectionCount = -1;
    private static PlacedObject observedPrimarySelection;
    private static long observedObjectCatalogRevision;
    private static int observedObjectTypeCount = -1;

    public static EditorPresentationSnapshot Current => current;
    internal static DevToolPresentationOutcome LastOutcome { get; private set; } = DevToolPresentationOutcome.FullRebuild;

    internal static void Publish(EditorSession session, bool shellOnly = false)
    {
        if (session?.Owner == null)
        {
            Clear();
            return;
        }

        // History can change through keyboard shortcuts and legacy transaction completion without
        // passing through EditorUiCommandQueue. Detect that authoritative revision before reading
        // presentation revisions. Known history snapshots may also have supplied a semantic Object
        // hint; unknown history remains safe because absence of a hint forces a full object capture.
        EditorHistoryService activeHistory = EditorActions.ActiveHistory(session);
        long historyRevision = activeHistory.Revision;
        if (ReferenceEquals(observedSession, session) &&
            observedHistoryRevision != 0L &&
            (observedHistoryRevision != historyRevision || !ReferenceEquals(observedActiveHistory, activeHistory)))
        {
            EditorRevisionHub.Mark(session, EditorRevisionKind.Shell);
            EditorRevisionHub.MarkWorkspace(session);
        }

        bool objectWorkspace = !shellOnly && session.ToolMode == EditorToolMode.Objects;
        if (objectWorkspace)
        {
            PlacedObject draggedObject = ResolveDraggedPlacedObject(session.Owner.draggedNode);
            if (draggedObject == null && session.Owner.activePage is global::DevInterface.ObjectsPage objectsPage)
                draggedObject = objectsPage.draggedObject?.pObj;

            if (draggedObject != null)
            {
                // A live world handle is a trusted one-member writer even though the legacy
                // transaction recorder is intentionally active around the gesture.
                EditorRevisionHub.Mark(session, EditorRevisionKind.Objects);
                ObjectPresentationChangeHintHub.MarkMember(session, draggedObject);
            }
            else if (EditorRevisionHub.RequiresLiveWorkspaceRefresh(session) || session.Owner.draggedNode != null)
            {
                EditorRevisionHub.Mark(session, EditorRevisionKind.Objects);
                ObjectPresentationChangeHintHub.MarkFull(session);
            }
        }

        long shellRevision = EditorRevisionHub.Get(session, EditorRevisionKind.Shell);
        long objectRevision = objectWorkspace
            ? EditorRevisionHub.Get(session, EditorRevisionKind.Objects)
            : 0L;
        long selectionRevision = objectWorkspace ? session.Selection.Revision : 0L;

        List<PlacedObject> live = objectWorkspace ? session.RoomSettings?.placedObjects : null;
        int objectCount = live?.Count ?? 0;
        int selectionCount = objectWorkspace ? session.Selection.Count : 0;
        PlacedObject primarySelection = objectWorkspace ? session.Selection.PrimaryPlacedObject : null;
        int typeCount = objectWorkspace ? ExtEnum<PlacedObject.Type>.values.Count : libraryTypeCount;
        long objectCatalogRevision = objectWorkspace ? ObjectCatalog.Revision : observedObjectCatalogRevision;
        bool libraryStale = objectWorkspace &&
            (libraryTypeCount != typeCount ||
             libraryCache.Length == 0 ||
             libraryCatalogRevision != objectCatalogRevision);
        bool catalogChanged = objectWorkspace &&
            (observedObjectCatalogRevision != objectCatalogRevision ||
             observedObjectTypeCount != typeCount);
        string placementType = session.PlacementType ?? string.Empty;

        bool sameIdentity =
            ReferenceEquals(observedSession, session) &&
            ReferenceEquals(observedRoom, session.Room) &&
            ReferenceEquals(observedPage, session.Owner.activePage) &&
            current.Available;

        if (!libraryStale &&
            sameIdentity &&
            observedShellOnly == shellOnly &&
            observedShellRevision == shellRevision &&
            observedObjectRevision == objectRevision &&
            observedHistoryRevision == historyRevision &&
            ReferenceEquals(observedActiveHistory, activeHistory) &&
            observedSelectionRevision == selectionRevision &&
            observedToolMode == session.ToolMode &&
            observedFocusMode == session.FocusMode &&
            observedBrowserOpen == session.BrowserOpen &&
            observedInspectorOpen == session.InspectorOpen &&
            observedLegacyUiVisible == session.LegacyUiVisible &&
            observedPlacementActive == session.PlacementActive &&
            string.Equals(observedPlacementType, placementType, StringComparison.Ordinal) &&
            string.Equals(current.PlacementError, session.PlacementError ?? string.Empty, StringComparison.Ordinal) &&
            observedObjectCount == objectCount &&
            observedSelectionCount == selectionCount &&
            ReferenceEquals(observedPrimarySelection, primarySelection))
        {
            LastOutcome = DevToolPresentationOutcome.CacheHit;
            return;
        }

        EditorObjectSnapshot[] scene = Array.Empty<EditorObjectSnapshot>();
        EditorObjectTypeSnapshot[] objectLibrary = Array.Empty<EditorObjectTypeSnapshot>();
        EditorInspectorSnapshot inspector = new();
        bool objectFullCapture = false;
        bool objectPartialCapture = false;

        if (objectWorkspace)
        {
            bool stableObjectContext =
                current.Available &&
                current.Hydrated &&
                current.ToolMode == EditorToolMode.Objects &&
                sameIdentity &&
                observedObjectCount == objectCount;

            bool modelChanged = observedObjectRevision != objectRevision;
            bool selectionChanged =
                observedSelectionRevision != selectionRevision ||
                observedSelectionCount != selectionCount ||
                !ReferenceEquals(observedPrimarySelection, primarySelection);
            bool legacyVisibilityChanged = observedLegacyUiVisible != session.LegacyUiVisible;

            bool objectPayloadStable =
                stableObjectContext &&
                !modelChanged &&
                !selectionChanged &&
                !legacyVisibilityChanged &&
                !catalogChanged;

            if (objectPayloadStable)
            {
                scene = current.SceneObjects ?? Array.Empty<EditorObjectSnapshot>();
                inspector = current.Inspector ?? new EditorInspectorSnapshot();
            }
            else if (catalogChanged)
            {
                // Descriptor metadata is presentation input, not RoomSettings model data. A catalog
                // revision can change labels/categories/source/semantic importance without changing
                // PlacedObject count or Objects revision, so refresh the detached payload explicitly.
                // Descriptor registration is rare; prefer one authoritative full capture over trying
                // to merge stale metadata with a simultaneous model/selection update.
                //
                // If the model also changed this frame, consume its semantic hint now. The full
                // capture already subsumes that change; leaving the hint queued would let a later
                // unrelated Objects revision consume stale member/collection semantics.
                if (modelChanged)
                    _ = ObjectPresentationChangeHintHub.Consume(session);

                scene = CaptureObjectScene(session, live);
                inspector = CaptureObjectInspector(session, live, selectionCount, primarySelection);
                objectFullCapture = true;
            }
            else
            {
                ObjectPresentationChangeHint hint = modelChanged
                    ? ObjectPresentationChangeHintHub.Consume(session)
                    : default;

                if (stableObjectContext &&
                    TryCaptureObjectPartial(
                        session,
                        live,
                        selectionCount,
                        primarySelection,
                        selectionRevision,
                        hint,
                        modelChanged,
                        selectionChanged,
                        legacyVisibilityChanged,
                        out scene,
                        out inspector))
                {
                    objectPartialCapture = true;
                }
                else
                {
                    scene = CaptureObjectScene(session, live);
                    inspector = CaptureObjectInspector(session, live, selectionCount, primarySelection);
                    objectFullCapture = true;
                }
            }

            if (libraryStale)
                RebuildLibraryCache(typeCount);
            objectLibrary = libraryCache;
        }

        current = new EditorPresentationSnapshot
        {
            Available = true,
            Hydrated = !shellOnly,
            DevToolsActive = session.Owner.game?.devToolsActive == true,
            Document = session.DocumentKey.ToString(),
            RoomName = session.Room?.abstractRoom?.name ?? session.RoomSettings?.name ?? string.Empty,
            ToolMode = session.ToolMode,
            FocusMode = session.FocusMode,
            BrowserOpen = session.BrowserOpen,
            InspectorOpen = session.InspectorOpen,
            CanUndo = EditorActions.ActiveHistory(session).CanUndo,
            CanRedo = EditorActions.ActiveHistory(session).CanRedo,
            UndoLabel = EditorActions.ActiveHistory(session).UndoLabel ?? string.Empty,
            RedoLabel = EditorActions.ActiveHistory(session).RedoLabel ?? string.Empty,
            PlacementActive = session.PlacementActive,
            PlacementType = placementType,
            PlacementError = session.PlacementError ?? string.Empty,
            SceneObjects = scene,
            ObjectLibrary = objectLibrary,
            Inspector = inspector
        };

        observedSession = session;
        observedRoom = session.Room;
        observedPage = session.Owner.activePage;
        observedShellRevision = shellRevision;
        observedObjectRevision = objectRevision;
        observedHistoryRevision = historyRevision;
        observedActiveHistory = activeHistory;
        observedSelectionRevision = selectionRevision;
        observedShellOnly = shellOnly;
        observedToolMode = session.ToolMode;
        observedFocusMode = session.FocusMode;
        observedBrowserOpen = session.BrowserOpen;
        observedInspectorOpen = session.InspectorOpen;
        observedLegacyUiVisible = session.LegacyUiVisible;
        observedPlacementActive = session.PlacementActive;
        observedPlacementType = placementType;
        observedObjectCount = objectCount;
        observedSelectionCount = selectionCount;
        observedPrimarySelection = primarySelection;
        observedObjectCatalogRevision = objectCatalogRevision;
        observedObjectTypeCount = typeCount;

        if (objectWorkspace)
        {
            LastOutcome = objectFullCapture
                ? DevToolPresentationOutcome.FullRebuild
                : DevToolPresentationOutcome.PartialRebuild;
        }
        else
        {
            // Core shell snapshots are intentionally light; once a session already exists, replacing
            // shell metadata is a partial rebuild even when no Objects payload is active.
            LastOutcome = sameIdentity || objectPartialCapture
                ? DevToolPresentationOutcome.PartialRebuild
                : DevToolPresentationOutcome.FullRebuild;
        }
    }

    internal static void Clear()
    {
        ObjectPresentationChangeHintHub.Clear(observedSession);
        current = EditorPresentationSnapshot.Empty;
        observedSession = null;
        observedRoom = null;
        observedPage = null;
        observedShellRevision = 0L;
        observedObjectRevision = 0L;
        observedHistoryRevision = 0L;
        observedActiveHistory = null;
        observedSelectionRevision = 0L;
        observedShellOnly = false;
        observedToolMode = EditorToolMode.Room;
        observedFocusMode = false;
        observedBrowserOpen = false;
        observedInspectorOpen = false;
        observedLegacyUiVisible = false;
        observedPlacementActive = false;
        observedPlacementType = string.Empty;
        observedObjectCount = -1;
        observedSelectionCount = -1;
        observedPrimarySelection = null;
        observedObjectCatalogRevision = 0L;
        observedObjectTypeCount = -1;
        LastOutcome = DevToolPresentationOutcome.FullRebuild;
    }

    internal static void InvalidateObjectLibrary()
    {
        libraryTypeCount = -1;
        libraryCatalogRevision = -1;
        libraryCache = Array.Empty<EditorObjectTypeSnapshot>();
    }

    private static void RebuildLibraryCache(int typeCount)
    {
        IReadOnlyList<ObjectDescriptor> descriptors = ObjectCatalog.GetAll();
        EditorObjectTypeSnapshot[] next = new EditorObjectTypeSnapshot[descriptors.Count];
        for (int i = 0; i < descriptors.Count; i++)
        {
            ObjectDescriptor descriptor = descriptors[i];
            string[] tags = new string[descriptor.Tags.Count];
            for (int t = 0; t < tags.Length; t++) tags[t] = descriptor.Tags[t];
            next[i] = new EditorObjectTypeSnapshot
            {
                Type = descriptor.Type?.value ?? string.Empty,
                DisplayName = descriptor.DisplayName,
                Category = descriptor.Category,
                Source = descriptor.Source,
                Tags = tags,
                PresentationKind = descriptor.PresentationKind,
                Importance = descriptor.Importance
            };
        }
        libraryCache = next;
        libraryTypeCount = typeCount;
        libraryCatalogRevision = ObjectCatalog.Revision;
    }
}

internal static class EditorUiCommandProcessor
{
    internal static void Process(EditorSession session)
    {
        if (session == null) return;

        long historyBeforeBatch = session.History.Revision;
        bool nonHistoryObjectDirty = false;

        // Bound game-thread work even when a render-thread producer floods the inbox.
        for (int processed = 0;
             processed < EditorUiCommandQueue.MaxCommandsPerFrame &&
             EditorUiCommandQueue.TryDequeue(out EditorUiCommand command);
             processed++)
        {
            try
            {
                long historyBeforeCommand = session.History.Revision;
                long selectionBefore = session.Selection.Revision;
                int objectCountBefore = session.RoomSettings?.placedObjects?.Count ?? 0;
                PlacedObject commandTarget = ResolveObject(session, command.Index, command.StableId);
                int selectionCountBefore = session.Selection.Count;
                PlacedObject primaryBefore = session.Selection.PrimaryPlacedObject;

                bool objectCommandSucceeded = Execute(session, command);

                long historyAfterCommand = session.History.Revision;
                long selectionAfter = session.Selection.Revision;
                int objectCountAfter = session.RoomSettings?.placedObjects?.Count ?? 0;
                bool historyChanged = historyAfterCommand != historyBeforeCommand;
                bool membershipChanged = objectCountBefore != objectCountAfter;
                bool selectionChanged = selectionBefore != selectionAfter;

                bool objectModelCommand = EditorUiCommandPolicy.IsObjectModelCommand(command.Kind);
                bool observableModelChange = objectModelCommand &&
                    (historyChanged || membershipChanged || objectCommandSucceeded);

                if (observableModelChange)
                {
                    MarkObjectPresentationChange(
                        session,
                        command,
                        commandTarget,
                        membershipChanged,
                        selectionCountBefore,
                        primaryBefore);

                    if (!historyChanged)
                        nonHistoryObjectDirty = true;
                }

                // Selection has its own revision and never needs to bump Objects model revision.
                _ = selectionChanged;
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool UI command failed: " + error.Message);
            }
        }

        // History mutations are converted into Shell + active-workspace invalidation by the core
        // presentation hub. Direct compatibility writes without history retain one explicit Objects
        // revision for the whole queue batch.
        if (nonHistoryObjectDirty && session.History.Revision == historyBeforeBatch)
            EditorRevisionHub.Mark(session, EditorRevisionKind.Objects);

        int dropped = EditorUiCommandQueue.ConsumeDroppedCount();
        if (dropped > 0)
            EditorDiagnostics.Warning(
                "DevTool UI command inbox rejected " + dropped +
                " command(s) while full; remaining queued: " +
                EditorUiCommandQueue.PendingCount);
    }

    private static void MarkObjectPresentationChange(
        EditorSession session,
        EditorUiCommand command,
        PlacedObject commandTarget,
        bool membershipChanged,
        int selectionCountBefore,
        PlacedObject primaryBefore)
    {
        if (membershipChanged)
        {
            ObjectPresentationChangeHintHub.MarkCollection(session);
            return;
        }

        switch (EditorUiCommandPolicy.ObjectMutationScope(command.Kind))
        {
            case EditorObjectMutationScope.Member:
                ObjectPresentationChangeHintHub.MarkMember(session, commandTarget);
                break;
            case EditorObjectMutationScope.SelectionMembers:
                if (selectionCountBefore == 1)
                    ObjectPresentationChangeHintHub.MarkMember(session, primaryBefore);
                else
                    ObjectPresentationChangeHintHub.MarkAllMembers(session);
                break;
            case EditorObjectMutationScope.Collection:
                ObjectPresentationChangeHintHub.MarkCollection(session);
                break;
            default:
                // Third-party legacy controls are conservative full writers.
                ObjectPresentationChangeHintHub.MarkFull(session);
                break;
        }
    }

    private static bool Execute(EditorSession session, EditorUiCommand command)
    {
        switch (command.Kind)
        {
            case EditorUiCommandKind.Save:
                EditorActions.Save(session);
                return false;
            case EditorUiCommandKind.Undo:
                EditorActions.Undo(session);
                return false;
            case EditorUiCommandKind.Redo:
                EditorActions.Redo(session);
                return false;
            case EditorUiCommandKind.ToggleFocus:
                session.ToggleFocusMode();
                return false;
            case EditorUiCommandKind.ToggleBrowser:
                session.ToggleBrowser();
                return false;
            case EditorUiCommandKind.ToggleInspector:
                session.ToggleInspector();
                return false;
            case EditorUiCommandKind.ToggleLegacyUi:
                session.ToggleLegacyUi();
                return false;
            case EditorUiCommandKind.SetToolMode:
                session.SetToolMode(command.Mode);
                return false;
            case EditorUiCommandKind.SelectObject:
                session.Selection.SelectOnly(ResolveObject(session, command.Index, command.StableId));
                return false;
            case EditorUiCommandKind.ToggleObjectSelection:
                session.Selection.Toggle(ResolveObject(session, command.Index, command.StableId));
                return false;
            case EditorUiCommandKind.SelectObjectRange:
                session.Selection.SelectRange(
                    session.RoomSettings?.placedObjects,
                    ResolveObjectIndex(session, command.SecondaryIndex, command.SecondaryStableId),
                    ResolveObjectIndex(session, command.Index, command.StableId),
                    command.Flag);
                return false;
            case EditorUiCommandKind.DeleteObject:
                return EditorActions.DeleteObject(session, ResolveObject(session, command.Index, command.StableId));
            case EditorUiCommandKind.DeleteSelection:
                return EditorActions.DeleteSelection(session);
            case EditorUiCommandKind.DuplicateSelection:
                return EditorActions.DuplicateSelection(session);
            case EditorUiCommandKind.CreateObject:
                return !string.IsNullOrEmpty(command.Text) &&
                       EditorActions.CreateObject(
                           session,
                           new PlacedObject.Type(command.Text, false),
                           new Vector2(command.X, command.Y)) != null;
            case EditorUiCommandKind.BeginPlacement:
                session.BeginPlacement(command.Text);
                return false;
            case EditorUiCommandKind.PlaceObjectAtCursor:
                return EditorActions.PlaceObjectAtCursor(session, command.Flag);
            case EditorUiCommandKind.CancelPlacement:
                session.CancelPlacement();
                return false;
            case EditorUiCommandKind.SetObjectPosition:
                return EditorActions.SetObjectPosition(session, ResolveObject(session, command.Index, command.StableId), new Vector2(command.X, command.Y));
            case EditorUiCommandKind.SetSelectionPosition:
                return EditorActions.SetSelectionPrimaryPosition(session, new Vector2(command.X, command.Y));
            case EditorUiCommandKind.SnapSelectionToGrid:
                return EditorActions.SnapSelectionToGrid(session, command.X);
            case EditorUiCommandKind.AlignSelection:
                return Enum.IsDefined(typeof(ObjectSelectionAlignment), command.Index) &&
                       EditorActions.AlignSelection(session, (ObjectSelectionAlignment)command.Index);
            case EditorUiCommandKind.DistributeSelection:
                return Enum.IsDefined(typeof(ObjectSelectionDistribution), command.Index) &&
                       EditorActions.DistributeSelection(session, (ObjectSelectionDistribution)command.Index);
            case EditorUiCommandKind.SetObjectProperty:
                return EditorActions.SetObjectProperty(session, ResolveObject(session, command.Index, command.StableId), command.Text, command.PropertyValue);
            case EditorUiCommandKind.SetSelectionProperty:
                return EditorActions.SetSelectionProperty(session, command.Text, command.PropertyValue);
            case EditorUiCommandKind.InvokeLegacyButton:
                return EditorActions.InvokeLegacyButton(session, ResolveObject(session, command.Index, command.StableId), command.Text);
            case EditorUiCommandKind.SetLegacySlider:
                return EditorActions.SetLegacySlider(session, ResolveObject(session, command.Index, command.StableId), command.Text, command.X);
            case EditorUiCommandKind.ResetLegacySlider:
                return EditorActions.ResetLegacySlider(session, ResolveObject(session, command.Index, command.StableId), command.Text);
            case EditorUiCommandKind.SetLegacyText:
                return EditorActions.SetLegacyText(session, ResolveObject(session, command.Index, command.StableId), command.Text, command.PropertyValue.Text);
            case EditorUiCommandKind.SetLegacyDirection:
                return EditorActions.SetLegacyDirection(session, ResolveObject(session, command.Index, command.StableId), command.Text, command.X, command.Y);
            case EditorUiCommandKind.SetLegacyColor:
                return EditorActions.SetLegacyColor(
                    session,
                    ResolveObject(session, command.Index, command.StableId),
                    command.Text,
                    command.PropertyValue.X,
                    command.PropertyValue.Y,
                    command.PropertyValue.Z,
                    command.PropertyValue.W);
            default:
                return false;
        }
    }

    private static PlacedObject ResolveObject(EditorSession session, int index, long stableId = 0L)
    {
        return ObjectPresentationIdentity.Resolve(session?.RoomSettings?.placedObjects, index, stableId);
    }

    private static int ResolveObjectIndex(EditorSession session, int index, long stableId = 0L)
    {
        return ObjectPresentationIdentity.ResolveIndex(session?.RoomSettings?.placedObjects, index, stableId);
    }
}
