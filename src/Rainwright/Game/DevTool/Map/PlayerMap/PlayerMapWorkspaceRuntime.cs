using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.History;
using DryCycle.DevUI.DevTool.World;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

public readonly struct PlayerMapDefMaterialSnapshot
{
    public PlayerMapDefMaterialSnapshot(int id, Vector2 a, Vector2 b, bool air)
    {
        Id = id;
        A = a;
        B = b;
        Air = air;
    }

    public int Id { get; }
    public Vector2 A { get; }
    public Vector2 B { get; }
    public bool Air { get; }
    public float Left => Mathf.Min(A.x, B.x);
    public float Right => Mathf.Max(A.x, B.x);
    public float Bottom => Mathf.Min(A.y, B.y);
    public float Top => Mathf.Max(A.y, B.y);
}

public sealed class PlayerMapRoomSnapshot
{
    public int RoomIndex { get; init; }
    public string Name { get; init; } = string.Empty;
    public PlayerMapPlacementMode Mode { get; init; }
    public Vector2 WorldPosition { get; init; }
    public Vector2 DerivedBasePosition { get; init; }
    public Vector2 Offset { get; init; }
    public Vector2 AbsolutePosition { get; init; }
    public Vector2 EffectivePosition { get; init; }
    public int Layer { get; init; }
    public bool Disabled { get; init; }
    public bool Hidden { get; init; }
    public bool Selected { get; init; }
    public RoomMapBakeSnapshot Bake { get; init; } = RoomMapBakeSnapshot.Empty;
}

public sealed class PlayerMapPresentationSnapshot
{
    public static readonly PlayerMapPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string RegionName { get; init; } = string.Empty;
    public bool Dirty { get; init; }
    public long Revision { get; init; }
    public int SelectedRoomIndex { get; init; } = -1;
    public PlayerMapRoomSnapshot[] Rooms { get; init; } = Array.Empty<PlayerMapRoomSnapshot>();
    public PlayerMapDefMaterialSnapshot[] DefaultMaterials { get; init; } = Array.Empty<PlayerMapDefMaterialSnapshot>();
    public PlayerMapRenderReport RenderReport { get; init; } = PlayerMapRenderReport.Empty;
    public PlayerMapRenderPreview Preview { get; init; } = PlayerMapRenderPreview.Empty;
}

/// <summary>
/// Unity coordinate adapter. All scale/layer/pixel policy is owned by Rainwright;
/// gameplay code keeps its existing Vector2 API and exact numeric behavior.
/// </summary>
public static class PlayerMapCoordinateSystem
{
    public const float WorldLayoutPixelsPerTile = PlayerMapCoordinatePolicy.WorldLayoutPixelsPerTile;
    public const float CanonPixelsPerTile = PlayerMapCoordinatePolicy.CanonPixelsPerTile;
    public const float WorldToCanonScale = PlayerMapCoordinatePolicy.WorldToCanonScale;
    public const int OutputPadding = PlayerMapCoordinatePolicy.OutputPadding;
    public const int LayerCount = PlayerMapCoordinatePolicy.LayerCount;

    public static Vector2 WorldLayoutToCanon(Vector2 worldLayout) => new(
        PlayerMapCoordinatePolicy.WorldLayoutToCanon(worldLayout.x),
        PlayerMapCoordinatePolicy.WorldLayoutToCanon(worldLayout.y));

    public static Vector2 CanonToWorldLayout(Vector2 canon) => new(
        PlayerMapCoordinatePolicy.CanonToWorldLayout(canon.x),
        PlayerMapCoordinatePolicy.CanonToWorldLayout(canon.y));

    internal static int CanonDeltaToOutputPixel(float value) =>
        PlayerMapCoordinatePolicy.CanonDeltaToOutputPixel(value);
}

/// <summary>
/// Player Map renders physical room geometry only. Off-screen dens are topology/creature-storage
/// rooms with no physical room source and therefore must never enter the room bake/render pipeline.
/// AbstractRoom.offScreenDen is authoritative; the name check is a compatibility fallback for
/// partially constructed/third-party world data.
/// </summary>
internal static class PlayerMapRoomEligibility
{
    internal static bool IsRenderable(AbstractRoom room)
    {
        if (room == null || room.offScreenDen) return false;
        return !PlayerMapRoomNamePolicy.IsOffscreenDenName(room.name);
    }

    internal static bool IsRenderableName(string roomName) =>
        !PlayerMapRoomNamePolicy.IsOffscreenDenName(roomName);
}

internal sealed class PlayerMapRoomState
{
    internal int RoomIndex;
    internal string Name = string.Empty;
    internal PlayerMapPlacementMode Mode;
    internal Vector2 Offset;
    internal Vector2 AbsolutePosition;
    internal Vector2 LastMirroredCanonical;
    internal bool HasMirror;
    internal bool Hidden;
}

internal sealed class PlayerMapDefMaterialState
{
    internal int Id;
    internal Vector2 A;
    internal Vector2 B;
    internal Vector2 PanelPosition;
    internal bool Air;

    internal PlayerMapDefMaterialState Clone() => new()
    {
        Id = Id,
        A = A,
        B = B,
        PanelPosition = PanelPosition,
        Air = Air
    };
}

internal sealed class PlayerMapSessionState
{
    internal MapPage Page;
    internal string Region = string.Empty;
    internal readonly Dictionary<int, PlayerMapRoomState> Rooms = new();
    internal readonly List<PlayerMapDefMaterialState> DefaultMaterials = new();
    internal int NextDefMaterialId = 1;
    internal long Revision = 1;
    internal bool Dirty;
    internal int ObservedBakeRevision = -1;
    internal int ObservedTerrainRevision = -1;
    internal int ObservedSelectedRoom = int.MinValue;
    internal long ObservedAuthoringRevision = -1;
    internal bool Initialized;
    internal MapViewFileStamp ConfigSource;
    internal string VisibilityLoadError;
    internal PlayerMapPresentationSnapshot Presentation = PlayerMapPresentationSnapshot.Empty;
    internal PlayerMapRenderReport RenderReport = PlayerMapRenderReport.Empty;
    internal PlayerMapRenderPreview Preview = PlayerMapRenderPreview.Empty;
}

public readonly struct PlayerMapCommand
{
    public PlayerMapCommand(
        PlayerMapCommandKind kind,
        int roomIndex = -1,
        Vector2 value = default,
        Vector2 valueB = default,
        int integer = 0,
        bool flag = false)
    {
        Kind = kind;
        RoomIndex = roomIndex;
        Value = value;
        ValueB = valueB;
        Integer = integer;
        Flag = flag;
    }

    public PlayerMapCommandKind Kind { get; }
    public int RoomIndex { get; }
    public Vector2 Value { get; }
    public Vector2 ValueB { get; }
    public int Integer { get; }
    public bool Flag { get; }
}

/// <summary>
/// Unity compatibility adapter: published commands are transported as detached
/// Rainwright payloads. No Unity objects are retained by the shared inbox.
/// </summary>
public static class PlayerMapCommandQueue
{
    public static void Enqueue(PlayerMapCommand command) => TryEnqueue(command);

    public static bool TryEnqueue(PlayerMapCommand command) =>
        PlayerMapCommandTransport.TryEnqueue(new PlayerMapCommandEnvelope(
            command.Kind,
            command.RoomIndex,
            command.Value.x, command.Value.y,
            command.ValueB.x, command.ValueB.y,
            command.Integer,
            command.Flag));

    public static void Process(EditorSession session)
    {
        if (session == null)
        {
            Clear();
            PlayerMapGroupCommandRuntime.Process(null);
            PlayerMapMigrationCommandBridge.Process(null);
            return;
        }

        PlayerMapWorkspaceRuntime.Synchronize(session);
        for (int processed = 0;
             processed < PlayerMapCommandTransport.MaxCommandsPerFrame &&
             PlayerMapCommandTransport.TryDequeue(out PlayerMapCommandEnvelope next);
             processed++)
        {
            // Convert on the game thread, keeping Unity types outside RainWright.dll.
            PlayerMapCommand command = new(
                next.Kind, next.RoomIndex,
                new Vector2(next.X, next.Y), new Vector2(next.XB, next.YB),
                next.Integer, next.Flag);
            try
            {
                PlayerMapWorkspaceRuntime.Execute(session, command);
            }
            catch (Exception error)
            {
                EditorDiagnostics.Warning("Player Map command failed: " + error);
            }
        }

        int rejected = PlayerMapCommandTransport.ConsumeRejectedCount();
        if (rejected > 0)
            EditorDiagnostics.Warning(
                "Player Map command inbox rejected " + rejected +
                " edit(s); pending=" + PlayerMapCommandTransport.PendingCount);

        PlayerMapWorkspaceRuntime.Synchronize(session);

        bool groupChanged = PlayerMapGroupCommandRuntime.Process(session);
        if (groupChanged)
            PlayerMapWorkspaceRuntime.Synchronize(session);

        PlayerMapMigrationCommandBridge.Process(session);
    }

    public static void Clear() => PlayerMapCommandTransport.Clear();
}

/// <summary>
/// Authoritative backend for the rebuilt Player Map / Canon View.
/// It reads MapPage/RoomPanel only as compatibility data surfaces; it never drives their Update,
/// MiniMap, RoomRepresentation texture generation, or MapRenderOutput lifecycle.
/// </summary>
internal static class PlayerMapWorkspaceRuntime
{
    private static ConditionalWeakTable<EditorSession, PlayerMapSessionState> states = new();
    // A room change can replace DevUI/EditorSession while keeping the same World alive.
    // World lifetime retains the derived preview without keeping an unloaded world alive.
    private static ConditionalWeakTable<global::World, PlayerMapSessionState> worldStates = new();
    private static readonly Dictionary<string, PlayerMapSessionState> retainedDirtyStates =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool enabled;

    internal static void Enable()
    {
        if (enabled) return;
        On.DevInterface.MapPage.SaveMapConfig += MapPage_SaveMapConfig;
        enabled = true;
    }

    internal static void Disable()
    {
        if (!enabled) return;
        On.DevInterface.MapPage.SaveMapConfig -= MapPage_SaveMapConfig;
        states = new ConditionalWeakTable<EditorSession, PlayerMapSessionState>();
        worldStates = new ConditionalWeakTable<global::World, PlayerMapSessionState>();
        retainedDirtyStates.Clear();
        PlayerMapCommandQueue.Clear();
        RoomMapBakeCache.Clear();
        enabled = false;
    }

    internal static PlayerMapPresentationSnapshot GetPresentation(EditorSession session)
    {
        if (session == null) return PlayerMapPresentationSnapshot.Empty;
        PlayerMapSessionState state = GetOrCreateState(
            session,
            session.Owner?.activePage as MapPage);
        PlayerMapPresentationSnapshot snapshot = state.Presentation;
        snapshot = PlayerMapDerivedLayoutBridge.Project(session, snapshot);
        snapshot = PlayerMapIncrementalRenderHooks.ProjectPresentation(session, snapshot);
        snapshot = PlayerMapMigrationDirtyBridge.ProjectPresentation(session, snapshot);
        return snapshot;
    }

    internal static bool IsDirty(EditorSession session)
    {
        if (session == null) return false;
        PlayerMapSessionState state = GetOrCreateState(
            session,
            session.Owner?.activePage as MapPage);
        return state?.Dirty == true;
    }

    /// <summary>
    /// Build the exact would-be authoring map config without touching the original file.
    /// This shares the production writer's validated document pipeline.
    /// </summary>
    internal static bool TryGetRecoveryText(EditorSession session, out string path, out string text)
    {
        path = string.Empty;
        text = string.Empty;
        if (session?.Owner?.activePage is not MapPage page) return false;
        PlayerMapSessionState state = GetOrCreateState(session, page);
        if (state?.Dirty != true || !EnsureStateForPage(page, state)) return false;
        if (!PlayerMapConfigBuildPipeline.TryBuildSnapshot(
                page, state, out path, out IReadOnlyList<string> output, out string error))
        {
            EditorDiagnostics.Warning(
                "[DevTool.AutoSave] Player Map recovery snapshot build failed: " + error);
            return false;
        }
        text = string.Join(Environment.NewLine, output) + Environment.NewLine;
        return true;
    }

    internal static void MarkDirty(EditorSession session)
    {
        if (session?.Owner?.activePage is not MapPage page || page.world == null)
            return;

        PlayerMapSessionState state = GetOrCreateState(session, page);
        if (!EnsureStateForPage(page, state))
            return;

        Touch(state, dirty: true);
    }

    internal static void RetainDirtyState(EditorSession session)
    {
        if (session == null || !states.TryGetValue(session, out PlayerMapSessionState state))
            return;

        if (state.Dirty && state.Initialized && !string.IsNullOrWhiteSpace(state.Region))
        {
            state.Page = null;
            state.ObservedBakeRevision = -1;
            state.ObservedSelectedRoom = int.MinValue;
            foreach (PlayerMapRoomState roomState in state.Rooms.Values)
                roomState.HasMirror = false;

            retainedDirtyStates[state.Region] = state;
            EditorDiagnostics.Info(
                "Player Map retained unsaved authoring state for region " + state.Region +
                " across DevTool session shutdown.");
        }

        states.Remove(session);
    }

    internal static void Synchronize(EditorSession session)
    {
        if (session?.Owner?.activePage is not MapPage page || page.world == null)
            return;

        PlayerMapSessionState state = GetOrCreateState(session, page);
        if (!EnsureStateForPage(page, state))
            return;

        RoomMapBakeCache.ProcessPending(4);

        bool changed = SynchronizeRooms(page, state);
        int selectedRoom = MapEditorPresentationHub.Current?.SelectedRoomIndex ?? -1;
        int bakeRevision = RoomMapBakeCache.Revision;
        long authoringRevision = NativeMapAuthoringStateHub.GetRevision(session);
        // Authored terrain finishes separately from the room-text bake. Observe its readiness on
        // the main thread so pending thumbnails advance without selection changes or UI polling.
        PlayerMapTerrainSemanticRevision.Audit(state.Region, state.Presentation.Rooms, 12);
        int terrainRevision = PlayerMapTerrainSemanticRevision.Revision;
        bool authoringChanged = state.ObservedAuthoringRevision != authoringRevision;
        bool bakeChanged = state.ObservedBakeRevision != bakeRevision || state.ObservedTerrainRevision != terrainRevision;
        // Placement can remain unchanged while layers or thumbnail readiness change. Publish a new
        // cache revision in all of those cases without treating background preparation as an edit.
        if ((authoringChanged || bakeChanged) && !changed) Touch(state, dirty: false);
        if (changed || bakeChanged || state.ObservedSelectedRoom != selectedRoom || authoringChanged ||
            !state.Presentation.Available)
        {
            state.ObservedBakeRevision = bakeRevision;
            state.ObservedTerrainRevision = terrainRevision;
            state.ObservedSelectedRoom = selectedRoom;
            state.ObservedAuthoringRevision = authoringRevision;
            Publish(page, state, selectedRoom);
        }

        PlayerMapIncrementalRenderHooks.AfterSynchronize(session);
        PlayerMapRenderPreparationController.AfterSynchronize(session);
    }

    internal static void Execute(EditorSession session, PlayerMapCommand command)
    {
        if (session?.Owner?.activePage is not MapPage page || page.world == null) return;
        if (PlayerMapLayerMutationFilter.ShouldSkip(session, command)) return;
        if (PlayerMapIncrementalRenderHooks.TryHandleExecute(session, command)) return;

        PlayerMapSessionState state = GetOrCreateState(session, page);
        if (!EnsureStateForPage(page, state))
            return;

        switch (command.Kind)
        {
            case PlayerMapCommandKind.SetRoomHidden:
                SetRoomHidden(session, state, command.RoomIndex, command.Flag, pushHistory: true);
                break;
            case PlayerMapCommandKind.SetEffectivePosition:
                SetEffectivePosition(session, page, state, command.RoomIndex, command.Value, pushHistory: true);
                break;
            case PlayerMapCommandKind.SetOffset:
                SetOffset(session, page, state, command.RoomIndex, command.Value, pushHistory: true);
                break;
            case PlayerMapCommandKind.SetPlacementMode:
                SetPlacementMode(session, page, state, command.RoomIndex,
                    command.Integer == 0 ? PlayerMapPlacementMode.Derived : PlayerMapPlacementMode.Absolute,
                    pushHistory: true);
                break;
            case PlayerMapCommandKind.ResetOffset:
                SetOffset(session, page, state, command.RoomIndex, Vector2.zero, pushHistory: true);
                break;
            case PlayerMapCommandKind.SetLayer:
                MapEditorActions.SetRoomLayer(session, command.RoomIndex, command.Integer);
                Touch(state, dirty: true);
                break;
            case PlayerMapCommandKind.CreateDefaultMaterial:
                CreateDefaultMaterial(session, page, state, command.Value, command.ValueB, command.Flag);
                break;
            case PlayerMapCommandKind.SetDefaultMaterialRect:
                SetDefaultMaterialRect(session, state, command.Integer, command.Value, command.ValueB);
                break;
            case PlayerMapCommandKind.SetDefaultMaterialAir:
                SetDefaultMaterialAir(session, state, command.Integer, command.Flag);
                break;
            case PlayerMapCommandKind.DeleteDefaultMaterial:
                DeleteDefaultMaterial(session, state, command.Integer);
                break;
            case PlayerMapCommandKind.BuildPreview:
                Render(page, state, export: false);
                break;
            case PlayerMapCommandKind.RenderAndExport:
                Render(page, state, export: true);
                break;
        }
    }

    private static MapPage probedSavePage;
    private static bool? probedSaveSucceeded;

    internal static void BeginSaveProbe(MapPage page)
    {
        probedSavePage = page;
        probedSaveSucceeded = null;
    }

    internal static bool SaveProbeSucceeded(MapPage page) =>
        ReferenceEquals(probedSavePage, page) && probedSaveSucceeded == true;

    private static void MapPage_SaveMapConfig(On.DevInterface.MapPage.orig_SaveMapConfig orig, MapPage self)
    {
        EditorSession session = DevToolSessionHub.Current;
        bool rebuiltOwner =
            self != null &&
            session != null &&
            ReferenceEquals(session.Owner?.activePage, self) &&
            !EditorUiModeState.UseVanilla && !session.LegacyUiVisible;

        if (!rebuiltOwner)
        {
            SaveVanillaMapConfigToAuthoringSource(orig, self);
            return;
        }

        PlayerMapSessionState state = GetOrCreateState(session, self);
        if (!EnsureStateForPage(self, state))
        {
            if (ReferenceEquals(probedSavePage, self)) probedSaveSucceeded = false;
            EditorDiagnostics.Warning(
                "Player Map save was blocked because unsaved authoring state belongs to region " +
                state.Region + ".");
            return;
        }
        if (!PlayerMapConfigBuildPipeline.Save(self, state, out string error))
        {
            if (ReferenceEquals(probedSavePage, self)) probedSaveSucceeded = false;
            state.RenderReport = PlayerMapRenderReport.Failure("Map config save failed", error);
            Touch(state, dirty: true);
            EditorDiagnostics.Warning("Player Map config save failed: " + error);
            return;
        }

        if (ReferenceEquals(probedSavePage, self)) probedSaveSucceeded = true;
        state.Dirty = false;
        retainedDirtyStates.Remove(state.Region);
        state.ConfigSource = MapViewFileStamp.Capture(self.filePath);
        PlayerMapMigrationDirtyBridge.OnSaveSucceeded(self);
        Touch(state, dirty: false);
    }

    private static void SaveVanillaMapConfigToAuthoringSource(
        On.DevInterface.MapPage.orig_SaveMapConfig orig,
        MapPage page)
    {
        if (page?.world == null || string.IsNullOrWhiteSpace(page.filePath))
        {
            orig(page);
            return;
        }

        if (!WorldAuthoringPathResolver.TryResolveMapConfigSource(
                page.world.name,
                page.filePath,
                out string authoringPath,
                out string error))
        {
            EditorDiagnostics.Warning(
                "Vanilla MapPage save was blocked to protect generated mergedmods data: " + error);
            return;
        }

        string readPath = page.filePath;
        string staged = null;
        try
        {
            if (WorldModificationWorkspace.IsPatch(authoringPath))
            {
                staged = Path.Combine(Path.GetTempPath(), "rainwright-map-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(staged, WorldAuthoringPathResolver.ReadAllText(authoringPath));
            }
            page.filePath = staged ?? authoringPath;
            orig(page);
            if (staged != null)
                WorldAuthoringPathResolver.AtomicWriteAllText(authoringPath, File.ReadAllText(staged));
        }
        catch (Exception saveError)
        {
            EditorDiagnostics.Warning(
                "Vanilla MapPage save failed against authoring source: " + saveError);
            throw;
        }
        finally
        {
            page.filePath = readPath;
            if (staged != null && File.Exists(staged)) File.Delete(staged);
        }
    }

    private static PlayerMapSessionState GetOrCreateState(
        EditorSession session,
        MapPage page)
    {
        if (session == null)
            return null;

        string targetRegion = page?.world?.name ?? string.Empty;
        if (states.TryGetValue(session, out PlayerMapSessionState existing))
        {
            if (!existing.Dirty &&
                !string.IsNullOrWhiteSpace(targetRegion) &&
                retainedDirtyStates.TryGetValue(targetRegion, out PlayerMapSessionState retained) &&
                !ReferenceEquals(existing, retained))
            {
                states.Remove(session);
                retainedDirtyStates.Remove(targetRegion);
                states.Add(session, retained);
                EditorDiagnostics.Info(
                    "Player Map restored retained unsaved authoring state for region " +
                    targetRegion + " into the new DevTool session.");
                RememberWorldState(page, retained);
                return retained;
            }

            RememberWorldState(page, existing);
            return existing;
        }

        if (!string.IsNullOrWhiteSpace(targetRegion) &&
            retainedDirtyStates.TryGetValue(targetRegion, out PlayerMapSessionState recovered))
        {
            retainedDirtyStates.Remove(targetRegion);
            states.Add(session, recovered);
            EditorDiagnostics.Info(
                "Player Map restored retained unsaved authoring state for region " +
                targetRegion + " into the new DevTool session.");
            RememberWorldState(page, recovered);
            return recovered;
        }

        PlayerMapSessionState created;
        if (page?.world != null)
        {
            if (worldStates.TryGetValue(page.world, out PlayerMapSessionState warm) &&
                warm.Initialized && !ReferenceEquals(warm.Page?.world, page.world))
                worldStates.Remove(page.world);
            created = worldStates.GetValue(page.world, _ => new PlayerMapSessionState());
        }
        else created = new PlayerMapSessionState();
        states.Add(session, created);
        return created;
    }

    private static void RememberWorldState(MapPage page, PlayerMapSessionState state)
    {
        if (page?.world == null ||
            (worldStates.TryGetValue(page.world, out PlayerMapSessionState warm) && ReferenceEquals(warm, state)))
            return;
        worldStates.Remove(page.world);
        worldStates.Add(page.world, state);
    }

    private static bool EnsureStateForPage(MapPage page, PlayerMapSessionState state)
    {
        if (page?.world == null || state == null)
            return false;

        string region = page.world.name ?? string.Empty;
        if (!state.Initialized)
        {
            InitializeState(page, state);
            return true;
        }

        if (!string.Equals(state.Region, region, StringComparison.OrdinalIgnoreCase))
        {
            if (state.Dirty)
            {
                string message =
                    "Unsaved authoring state for " + state.Region +
                    " is being retained. Return to that region and save or undo it before editing " +
                    region + ".";
                state.RenderReport = PlayerMapRenderReport.Failure(
                    "Unsaved Player Map belongs to another region",
                    message);

                if (state.Presentation.Available ||
                    !string.Equals(state.Presentation.RegionName, region, StringComparison.OrdinalIgnoreCase))
                {
                    state.Revision = state.Revision >= long.MaxValue ? 1L : state.Revision + 1L;
                    state.Presentation = new PlayerMapPresentationSnapshot
                    {
                        Available = false,
                        RegionName = region,
                        Dirty = true,
                        Revision = state.Revision,
                        SelectedRoomIndex = -1,
                        Rooms = Array.Empty<PlayerMapRoomSnapshot>(),
                        DefaultMaterials = Array.Empty<PlayerMapDefMaterialSnapshot>(),
                        RenderReport = state.RenderReport,
                        Preview = PlayerMapRenderPreview.Empty
                    };
                }
                return false;
            }

            InitializeState(page, state);
            return true;
        }

        if (ReferenceEquals(state.Page, page))
            return true;

        if (!state.Dirty &&
            (!ReferenceEquals(state.Page?.world, page.world) ||
             !state.ConfigSource.Matches(MapViewFileStamp.Capture(page.filePath))))
        {
            InitializeState(page, state);
            return true;
        }

        // The DevUI/MapPage instance can be rebuilt while the logical region remains the same.
        // Reuse clean previews/reports when the world and authoring file are unchanged. Rebuilt
        // panels are not external edits: reapply our model before observing subsequent native edits.
        state.Page = page;
        state.Region = region;
        if (state.Dirty) state.ObservedBakeRevision = -1;
        state.ObservedSelectedRoom = int.MinValue;
        foreach (PlayerMapRoomState roomState in state.Rooms.Values)
            roomState.HasMirror = false;

        SynchronizeRooms(page, state);
        Publish(page, state, MapEditorPresentationHub.Current?.SelectedRoomIndex ?? -1);
        EditorDiagnostics.Info(
            "Player Map reused cached authoring state on a replacement MapPage for region " +
            region + ".");
        return true;
    }

    private static void InitializeState(MapPage page, PlayerMapSessionState state)
    {
        state.Page = page;
        state.Region = page.world?.name ?? string.Empty;
        state.Rooms.Clear();
        state.DefaultMaterials.Clear();
        state.NextDefMaterialId = 1;
        state.Revision = state.Revision >= long.MaxValue ? 1L : state.Revision + 1L;
        state.Dirty = false;
        state.RenderReport = PlayerMapRenderReport.Empty;
        state.Preview = PlayerMapRenderPreview.Empty;
        state.ObservedBakeRevision = -1;
        state.ObservedSelectedRoom = int.MinValue;
        state.Initialized = true;

        LoadDefaultMaterials(page, state);
        SynchronizeRooms(page, state);
        PlayerMapPlacementBootstrap.AfterInitialize(page, state);
        state.VisibilityLoadError = null;
        try
        {
            HashSet<string> hidden = File.Exists(page.filePath)
                ? PlayerMapRoomVisibility.Read(File.ReadAllLines(page.filePath))
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerMapRoomState room in state.Rooms.Values) room.Hidden = hidden.Contains(room.Name);
            state.ConfigSource = MapViewFileStamp.Capture(page.filePath);
        }
        catch (Exception error)
        {
            state.VisibilityLoadError = error.ToString();
            state.RenderReport = PlayerMapRenderReport.Failure("Room visibility could not be loaded; map save is blocked.", error.ToString());
            EditorDiagnostics.Error("Player Map room visibility load failed: " + error);
        }
        Publish(page, state, MapEditorPresentationHub.Current?.SelectedRoomIndex ?? -1);
    }

    private static bool SynchronizeRooms(MapPage page, PlayerMapSessionState state)
    {
        bool changed = false;
        HashSet<int> alive = new();
        if (page.subNodes == null) return false;

        // Indexes belong to a World instance, not to an authored room. Build the replacement
        // lookup in two phases so inserts/permutations cannot steal another room's dirty state.
        bool reindex = false;
        for (int i = 0; i < page.subNodes.Count; i++)
        {
            if (page.subNodes[i] is not RoomPanel panel || panel.roomRep?.room == null) continue;
            AbstractRoom room = panel.roomRep.room;
            if (!PlayerMapRoomEligibility.IsRenderable(room)) continue;
            if (!state.Rooms.TryGetValue(room.index, out PlayerMapRoomState prior) ||
                !string.Equals(prior.Name, room.name, StringComparison.OrdinalIgnoreCase))
            { reindex = true; break; }
        }
        if (reindex)
        {
            Dictionary<string, PlayerMapRoomState> byName = new(StringComparer.OrdinalIgnoreCase);
            foreach (PlayerMapRoomState prior in state.Rooms.Values)
                if (!string.IsNullOrEmpty(prior.Name)) byName[prior.Name] = prior;
            state.Rooms.Clear();
            for (int i = 0; i < page.subNodes.Count; i++)
            {
                if (page.subNodes[i] is not RoomPanel panel || panel.roomRep?.room == null) continue;
                AbstractRoom room = panel.roomRep.room;
                if (!PlayerMapRoomEligibility.IsRenderable(room)) continue;
                if (!byName.TryGetValue(room.name ?? string.Empty, out PlayerMapRoomState prior)) continue;
                prior.RoomIndex = room.index;
                prior.HasMirror = false;
                state.Rooms.Add(room.index, prior);
            }
            changed = true;
        }

        for (int i = 0; i < page.subNodes.Count; i++)
        {
            if (page.subNodes[i] is not RoomPanel panel || panel.roomRep?.room == null) continue;
            AbstractRoom room = panel.roomRep.room;
            if (!PlayerMapRoomEligibility.IsRenderable(room)) continue;

            alive.Add(room.index);
            Vector2 derivedBase = PlayerMapCoordinateSystem.WorldLayoutToCanon(panel.devPos);

            if (!state.Rooms.TryGetValue(room.index, out PlayerMapRoomState roomState))
            {
                string roomName = room.name ?? string.Empty;
                roomState = new PlayerMapRoomState
                {
                    RoomIndex = room.index,
                    Name = roomName,
                    Mode = PlayerMapPlacementMode.Derived,
                    Offset = panel.pos - derivedBase,
                    AbsolutePosition = panel.pos,
                    LastMirroredCanonical = panel.pos,
                    HasMirror = true
                };
                state.Rooms.Add(room.index, roomState);
                changed = true;
            }
            else
            {
                roomState.Name = room.name ?? roomState.Name;
                if (roomState.HasMirror && (panel.pos - roomState.LastMirroredCanonical).sqrMagnitude > 0.0001f)
                {
                    // A third-party/vanilla writer changed Canon Position directly. Adopt the value
                    // without destroying the derived relation selected by the new editor.
                    if (roomState.Mode == PlayerMapPlacementMode.Derived)
                        roomState.Offset = panel.pos - derivedBase;
                    else
                        roomState.AbsolutePosition = panel.pos;
                    changed = true;
                }
            }

            Vector2 effective = Effective(roomState, panel.devPos);
            if ((panel.pos - effective).sqrMagnitude > 0.000001f)
                panel.pos = effective;
            roomState.LastMirroredCanonical = effective;
            roomState.HasMirror = true;
            if (!roomState.Hidden) RoomMapBakeCache.Request(room.index, room.name ?? string.Empty);
        }

        if (state.Rooms.Count != alive.Count)
        {
            List<int> stale = new();
            foreach (int key in state.Rooms.Keys)
                if (!alive.Contains(key)) stale.Add(key);
            for (int i = 0; i < stale.Count; i++) state.Rooms.Remove(stale[i]);
            changed |= stale.Count > 0;
        }

        if (changed) Touch(state, dirty: false);
        return changed;
    }

    private static void Publish(MapPage page, PlayerMapSessionState state, int selectedRoom)
    {
        EditorSession session = DevToolSessionHub.Current;
        bool native = session != null && ReferenceEquals(session.World, page.world) && ReferenceEquals(session.Owner?.activePage, page);
        HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);
        if (page.world?.DisabledMapRooms != null)
        {
            for (int i = 0; i < page.world.DisabledMapRooms.Count; i++)
                if (!string.IsNullOrWhiteSpace(page.world.DisabledMapRooms[i]))
                    disabled.Add(page.world.DisabledMapRooms[i]);
        }

        List<PlayerMapRoomSnapshot> rooms = new(state.Rooms.Count);
        bool selectedRenderable = false;
        if (page.subNodes != null)
        {
            for (int i = 0; i < page.subNodes.Count; i++)
            {
                if (page.subNodes[i] is not RoomPanel panel || panel.roomRep?.room == null) continue;
                AbstractRoom room = panel.roomRep.room;
                if (!PlayerMapRoomEligibility.IsRenderable(room)) continue;
                if (!state.Rooms.TryGetValue(room.index, out PlayerMapRoomState roomState)) continue;
                Vector2 derivedBase = PlayerMapCoordinateSystem.WorldLayoutToCanon(panel.devPos);
                bool selected = room.index == selectedRoom;
                if (selected) selectedRenderable = true;
                bool excluded = roomState.Hidden || disabled.Contains(room.name ?? string.Empty);
                rooms.Add(new PlayerMapRoomSnapshot
                {
                    RoomIndex = room.index,
                    Name = room.name ?? string.Empty,
                    Mode = roomState.Mode,
                    WorldPosition = panel.devPos,
                    DerivedBasePosition = derivedBase,
                    Offset = roomState.Offset,
                    AbsolutePosition = roomState.AbsolutePosition,
                    EffectivePosition = Effective(roomState, panel.devPos),
                    Layer = native && NativeMapAuthoringStateHub.TryGet(session, room.index, out NativeMapRoomAuthoringValue authoring)
                        ? authoring.Layer : panel.layer,
                    Disabled = excluded,
                    Hidden = roomState.Hidden,
                    Selected = selected,
                    Bake = excluded ? RoomMapBakeSnapshot.Empty : RoomMapBakeCache.GetSnapshot(room.index)
                });
            }
        }
        rooms.Sort((a, b) => a.RoomIndex.CompareTo(b.RoomIndex));

        PlayerMapDefMaterialSnapshot[] defs = new PlayerMapDefMaterialSnapshot[state.DefaultMaterials.Count];
        for (int i = 0; i < state.DefaultMaterials.Count; i++)
        {
            PlayerMapDefMaterialState item = state.DefaultMaterials[i];
            defs[i] = new PlayerMapDefMaterialSnapshot(item.Id, item.A, item.B, item.Air);
        }

        state.Presentation = new PlayerMapPresentationSnapshot
        {
            Available = true,
            RegionName = state.Region,
            Dirty = state.Dirty,
            Revision = state.Revision,
            SelectedRoomIndex = selectedRenderable ? selectedRoom : -1,
            Rooms = rooms.ToArray(),
            DefaultMaterials = defs,
            RenderReport = state.RenderReport,
            Preview = state.Preview
        };
    }

    private static bool SetRoomHidden(EditorSession session, PlayerMapSessionState state, int roomIndex, bool hidden, bool pushHistory)
    {
        if (state.VisibilityLoadError != null || !state.Rooms.TryGetValue(roomIndex, out PlayerMapRoomState room)) return false;
        if (room.Hidden == hidden) return true;
        bool before = room.Hidden;
        string name = room.Name;
        string region = state.Region;
        room.Hidden = hidden;
        PlayerMapRenderPreparationController.RequestCancel();
        PlayerMapRenderScheduler.InvalidateResult(session, "Room visibility changed.");
        state.Preview = PlayerMapRenderPreview.Empty;
        state.RenderReport = PlayerMapRenderReport.Empty;
        Touch(state, dirty: true);
        Publish(state.Page, state, MapEditorPresentationHub.Current?.SelectedRoomIndex ?? -1);
        if (pushHistory && session?.History != null)
            session.History.Push(new DelegateHistoryEntry(hidden ? "Hide Player Map room" : "Show Player Map room",
                s => RestoreRoomVisibility(s, region, name, before),
                s => RestoreRoomVisibility(s, region, name, hidden)));
        return true;
    }

    private static bool RestoreRoomVisibility(EditorSession session, string region, string name, bool hidden)
    {
        PlayerMapSessionState state = GetLiveState(session);
        if (state == null || !string.Equals(state.Region, region, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (PlayerMapRoomState room in state.Rooms.Values)
            if (string.Equals(room.Name, name, StringComparison.OrdinalIgnoreCase))
                return SetRoomHidden(session, state, room.RoomIndex, hidden, pushHistory: false);
        return false;
    }

    private readonly struct PlacementValue
    {
        internal PlacementValue(PlayerMapPlacementMode mode, Vector2 offset, Vector2 absolute)
        {
            Mode = mode;
            Offset = offset;
            Absolute = absolute;
        }

        internal PlayerMapPlacementMode Mode { get; }
        internal Vector2 Offset { get; }
        internal Vector2 Absolute { get; }
    }

    private static bool SetEffectivePosition(
        EditorSession session,
        MapPage page,
        PlayerMapSessionState state,
        int roomIndex,
        Vector2 effective,
        bool pushHistory)
    {
        RoomPanel panel = FindRoomPanel(page, roomIndex);
        if (panel == null || !state.Rooms.TryGetValue(roomIndex, out PlayerMapRoomState roomState)) return false;
        PlacementValue before = Capture(roomState);
        if (roomState.Mode == PlayerMapPlacementMode.Derived)
            roomState.Offset = effective - PlayerMapCoordinateSystem.WorldLayoutToCanon(panel.devPos);
        else
            roomState.AbsolutePosition = effective;
        return FinishPlacementMutation(session, panel, state, roomState, before, "Move player-map room", pushHistory);
    }

    private static bool SetOffset(
        EditorSession session,
        MapPage page,
        PlayerMapSessionState state,
        int roomIndex,
        Vector2 offset,
        bool pushHistory)
    {
        RoomPanel panel = FindRoomPanel(page, roomIndex);
        if (panel == null || !state.Rooms.TryGetValue(roomIndex, out PlayerMapRoomState roomState)) return false;
        PlacementValue before = Capture(roomState);
        Vector2 current = Effective(roomState, panel.devPos);
        roomState.Mode = PlayerMapPlacementMode.Derived;
        roomState.Offset = offset;
        roomState.AbsolutePosition = current;
        return FinishPlacementMutation(session, panel, state, roomState, before, "Adjust player-map offset", pushHistory);
    }

    private static bool SetPlacementMode(
        EditorSession session,
        MapPage page,
        PlayerMapSessionState state,
        int roomIndex,
        PlayerMapPlacementMode mode,
        bool pushHistory)
    {
        RoomPanel panel = FindRoomPanel(page, roomIndex);
        if (panel == null || !state.Rooms.TryGetValue(roomIndex, out PlayerMapRoomState roomState) || roomState.Mode == mode)
            return false;
        PlacementValue before = Capture(roomState);
        Vector2 current = Effective(roomState, panel.devPos);
        if (mode == PlayerMapPlacementMode.Absolute)
            roomState.AbsolutePosition = current;
        else
            roomState.Offset = current - PlayerMapCoordinateSystem.WorldLayoutToCanon(panel.devPos);
        roomState.Mode = mode;
        return FinishPlacementMutation(session, panel, state, roomState, before, "Change player-map placement mode", pushHistory);
    }

    private static bool FinishPlacementMutation(
        EditorSession session,
        RoomPanel panel,
        PlayerMapSessionState state,
        PlayerMapRoomState roomState,
        PlacementValue before,
        string label,
        bool pushHistory)
    {
        PlacementValue after = Capture(roomState);
        if (Same(before, after)) return false;
        Mirror(panel, roomState);
        Touch(state, dirty: true);
        if (pushHistory && session?.History != null)
        {
            string roomName = roomState.Name;
            string region = state.Region;
            session.History.Push(new DelegateHistoryEntry(
                label,
                s => RestorePlacement(s, region, roomName, before),
                s => RestorePlacement(s, region, roomName, after)));
        }
        return true;
    }

    private static bool RestorePlacement(EditorSession session, string region, string roomName, PlacementValue value)
    {
        if (session?.Owner?.activePage is not MapPage page) return false;
        PlayerMapSessionState state = GetOrCreateState(session, page);
        if (!EnsureStateForPage(page, state))
            return false;
        if (!string.Equals(state.Region, region, StringComparison.OrdinalIgnoreCase)) return false;
        SynchronizeRooms(page, state);
        PlayerMapRoomState roomState = null;
        foreach (PlayerMapRoomState candidate in state.Rooms.Values)
            if (string.Equals(candidate.Name, roomName, StringComparison.OrdinalIgnoreCase))
            { roomState = candidate; break; }
        if (roomState == null) return false;
        int roomIndex = roomState.RoomIndex;
        RoomPanel panel = FindRoomPanel(page, roomIndex);
        if (panel == null) return false;
        roomState.Mode = value.Mode;
        roomState.Offset = value.Offset;
        roomState.AbsolutePosition = value.Absolute;
        Mirror(panel, roomState);
        Touch(state, dirty: true);
        return true;
    }

    private static PlacementValue Capture(PlayerMapRoomState state) =>
        new(state.Mode, state.Offset, state.AbsolutePosition);

    private static bool Same(PlacementValue a, PlacementValue b) =>
        a.Mode == b.Mode &&
        (a.Offset - b.Offset).sqrMagnitude <= 0.000001f &&
        (a.Absolute - b.Absolute).sqrMagnitude <= 0.000001f;

    private static void Mirror(RoomPanel panel, PlayerMapRoomState roomState)
    {
        Vector2 effective = Effective(roomState, panel.devPos);
        panel.pos = effective;
        roomState.LastMirroredCanonical = effective;
        roomState.HasMirror = true;
    }

    private static Vector2 Effective(PlayerMapRoomState state, Vector2 worldPosition) =>
        state.Mode == PlayerMapPlacementMode.Absolute
            ? state.AbsolutePosition
            : PlayerMapCoordinateSystem.WorldLayoutToCanon(worldPosition) + state.Offset;

    private static void CreateDefaultMaterial(
        EditorSession session,
        MapPage page,
        PlayerMapSessionState state,
        Vector2 a,
        Vector2 b,
        bool air)
    {
        if ((a - b).sqrMagnitude < 0.001f)
        {
            a -= new Vector2(45f, 30f);
            b += new Vector2(45f, 30f);
        }
        PlayerMapDefMaterialState created = new()
        {
            Id = state.NextDefMaterialId++,
            A = a,
            B = b,
            PanelPosition = a + new Vector2(-10f, -60f),
            Air = air
        };
        state.DefaultMaterials.Add(created);
        Touch(state, dirty: true);
        int id = created.Id;
        session?.History.Push(new DelegateHistoryEntry(
            "Create default material region",
            s => RemoveDefNoHistory(s, id),
            s => AddDefNoHistory(s, created.Clone())));
    }

    private static void SetDefaultMaterialRect(
        EditorSession session,
        PlayerMapSessionState state,
        int id,
        Vector2 a,
        Vector2 b)
    {
        PlayerMapDefMaterialState item = FindDef(state, id);
        if (item == null || ((item.A - a).sqrMagnitude <= 0.000001f && (item.B - b).sqrMagnitude <= 0.000001f)) return;
        Vector2 beforeA = item.A;
        Vector2 beforeB = item.B;
        item.A = a;
        item.B = b;
        Touch(state, dirty: true);
        session?.History.Push(new DelegateHistoryEntry(
            "Resize default material region",
            s => RestoreDefRect(s, id, beforeA, beforeB),
            s => RestoreDefRect(s, id, a, b)));
    }

    private static void SetDefaultMaterialAir(EditorSession session, PlayerMapSessionState state, int id, bool air)
    {
        PlayerMapDefMaterialState item = FindDef(state, id);
        if (item == null || item.Air == air) return;
        bool before = item.Air;
        item.Air = air;
        Touch(state, dirty: true);
        session?.History.Push(new DelegateHistoryEntry(
            "Change default material region",
            s => RestoreDefAir(s, id, before),
            s => RestoreDefAir(s, id, air)));
    }

    private static void DeleteDefaultMaterial(EditorSession session, PlayerMapSessionState state, int id)
    {
        int index = state.DefaultMaterials.FindIndex(x => x.Id == id);
        if (index < 0) return;
        PlayerMapDefMaterialState removed = state.DefaultMaterials[index].Clone();
        state.DefaultMaterials.RemoveAt(index);
        Touch(state, dirty: true);
        session?.History.Push(new DelegateHistoryEntry(
            "Delete default material region",
            s => AddDefNoHistory(s, removed.Clone()),
            s => RemoveDefNoHistory(s, id)));
    }

    private static bool RestoreDefRect(EditorSession session, int id, Vector2 a, Vector2 b)
    {
        PlayerMapSessionState state = GetLiveState(session);
        PlayerMapDefMaterialState item = FindDef(state, id);
        if (item == null) return false;
        item.A = a;
        item.B = b;
        Touch(state, dirty: true);
        return true;
    }

    private static bool RestoreDefAir(EditorSession session, int id, bool air)
    {
        PlayerMapSessionState state = GetLiveState(session);
        PlayerMapDefMaterialState item = FindDef(state, id);
        if (item == null) return false;
        item.Air = air;
        Touch(state, dirty: true);
        return true;
    }

    private static bool AddDefNoHistory(EditorSession session, PlayerMapDefMaterialState item)
    {
        PlayerMapSessionState state = GetLiveState(session);
        if (state == null || item == null || FindDef(state, item.Id) != null) return false;
        state.DefaultMaterials.Add(item);
        state.NextDefMaterialId = Math.Max(state.NextDefMaterialId, item.Id + 1);
        Touch(state, dirty: true);
        return true;
    }

    private static bool RemoveDefNoHistory(EditorSession session, int id)
    {
        PlayerMapSessionState state = GetLiveState(session);
        if (state == null) return false;
        int index = state.DefaultMaterials.FindIndex(x => x.Id == id);
        if (index < 0) return false;
        state.DefaultMaterials.RemoveAt(index);
        Touch(state, dirty: true);
        return true;
    }

    private static PlayerMapSessionState GetLiveState(EditorSession session)
    {
        if (session?.Owner?.activePage is not MapPage page) return null;
        PlayerMapSessionState state = GetOrCreateState(session, page);
        return EnsureStateForPage(page, state) ? state : null;
    }

    private static PlayerMapDefMaterialState FindDef(PlayerMapSessionState state, int id) =>
        state?.DefaultMaterials.Find(x => x.Id == id);

    private static void Render(MapPage page, PlayerMapSessionState state, bool export)
    {
        PlayerMapRenderOutcome outcome = PlayerMapRenderPipeline.Build(page, state, export);
        state.RenderReport = outcome.Report;
        state.Preview = outcome.Preview;
        Touch(state, dirty: false);
    }

    private static RoomPanel FindRoomPanel(MapPage page, int roomIndex)
    {
        if (page?.subNodes == null) return null;
        for (int i = 0; i < page.subNodes.Count; i++)
            if (page.subNodes[i] is RoomPanel panel && panel.roomRep?.room?.index == roomIndex)
                return panel;
        return null;
    }

    internal static bool TryGetRoomState(MapPage page, PlayerMapSessionState state, int roomIndex,
        out PlayerMapRoomState roomState, out RoomPanel panel)
    {
        roomState = null;
        panel = FindRoomPanel(page, roomIndex);
        return panel != null && state.Rooms.TryGetValue(roomIndex, out roomState);
    }

    internal static Vector2 Effective(PlayerMapRoomState state, RoomPanel panel) =>
        Effective(state, panel.devPos);

    private static void Touch(PlayerMapSessionState state, bool dirty)
    {
        if (state == null) return;
        state.Revision = state.Revision >= long.MaxValue ? 1L : state.Revision + 1L;
        if (dirty) state.Dirty = true;
        state.ObservedBakeRevision = -1;
    }

    private static void LoadDefaultMaterials(MapPage page, PlayerMapSessionState state)
    {
        string path = page?.filePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        string[] lines;
        try { lines = File.ReadAllLines(path); }
        catch { return; }

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i]?.Trim();
            if (string.IsNullOrEmpty(line) || !line.StartsWith("Def_Mat:", StringComparison.OrdinalIgnoreCase)) continue;
            string payload = line.Substring(line.IndexOf(':') + 1).Trim();
            string[] parts = payload.Split(',');
            if (parts.Length < 7) continue;
            if (!TryFloat(parts[0], out float ax) || !TryFloat(parts[1], out float ay) ||
                !TryFloat(parts[2], out float bx) || !TryFloat(parts[3], out float by) ||
                !TryFloat(parts[4], out float px) || !TryFloat(parts[5], out float py) ||
                !int.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int air))
                continue;
            state.DefaultMaterials.Add(new PlayerMapDefMaterialState
            {
                Id = state.NextDefMaterialId++,
                A = new Vector2(ax, ay),
                B = new Vector2(bx, by),
                PanelPosition = new Vector2(px, py),
                Air = air == 1
            });
        }
    }

    private static bool TryFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}

internal static class PlayerMapConfigSerializer
{
    internal static bool Save(MapPage page, PlayerMapSessionState state, out string error)
    {
        if (PlayerMapConfigBuildPipeline.Enabled)
            return PlayerMapConfigBuildPipeline.Save(page, state, out error);

        error = null;
        if (page?.world == null || state == null)
        {
            error = "MapPage or Player Map state is unavailable.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(page.filePath))
        {
            error = "MapPage.filePath is empty.";
            return false;
        }
        if (!WorldAuthoringPathResolver.TryResolveMapConfigSource(
                page.world.name,
                page.filePath,
                out string authoringPath,
                out error))
            return false;

        try
        {
            if (state.VisibilityLoadError != null) throw new InvalidOperationException(state.VisibilityLoadError);
            Dictionary<string, string> roomLines = BuildRoomLines(page, state);
            List<string> source = WorldAuthoringPathResolver.CanReadSource(authoringPath)
                ? new List<string>(WorldAuthoringPathResolver.ReadAllLines(authoringPath))
                : new List<string>();
            // The legacy/no-extras path intentionally retains existing connection
            // and migration-stream metadata. Only room and default material rows
            // are authored here; Rainwright performs the lossless document merge.
            List<string> defLines = new(state.DefaultMaterials.Count);
            for (int i = 0; i < state.DefaultMaterials.Count; i++)
            {
                PlayerMapDefMaterialState item = state.DefaultMaterials[i];
                defLines.Add("Def_Mat: " + F(item.A.x) + "," + F(item.A.y) + "," +
                             F(item.B.x) + "," + F(item.B.y) + "," +
                             F(item.PanelPosition.x) + "," + F(item.PanelPosition.y) + "," +
                             (item.Air ? "1" : "0"));
            }

            string hidden = PlayerMapRoomVisibility.Write(
                state.Rooms.Values.Where(room => room.Hidden).Select(room => room.Name));
            IReadOnlyList<string> output = PlayerMapConfigDocumentComposer.Compose(
                source, roomLines, defLines,
                Array.Empty<string>(), Array.Empty<string>(),
                replaceStreams: false, visibilityRecord: hidden, replaceConnections: false);
            if (WorldModificationWorkspace.IsPatch(authoringPath))
                WorldAuthoringPathResolver.AtomicWriteAllLines(authoringPath, output);
            else PlayerMapConfigFileTransaction.WriteAllLines(authoringPath, output);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static Dictionary<string, string> BuildRoomLines(MapPage page, PlayerMapSessionState state)
    {
        Dictionary<string, string> lines = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);
        if (page.world.DisabledMapRooms != null)
            for (int i = 0; i < page.world.DisabledMapRooms.Count; i++)
                if (!string.IsNullOrWhiteSpace(page.world.DisabledMapRooms[i])) disabled.Add(page.world.DisabledMapRooms[i]);

        if (page.subNodes == null) return lines;
        for (int i = 0; i < page.subNodes.Count; i++)
        {
            if (page.subNodes[i] is not RoomPanel panel || panel.roomRep?.room == null) continue;
            AbstractRoom room = panel.roomRep.room;
            if (!PlayerMapRoomEligibility.IsRenderable(room)) continue;
            if (disabled.Contains(room.name ?? string.Empty)) continue;
            if (!state.Rooms.TryGetValue(room.index, out PlayerMapRoomState roomState)) continue;
            Vector2 canonical = PlayerMapWorkspaceRuntime.Effective(roomState, panel);
            string subregion = room.subregionName ?? string.Empty;
            lines[room.name] = room.name + ": " +
                               F(canonical.x) + "><" + F(canonical.y) + "><" +
                               F(panel.devPos.x) + "><" + F(panel.devPos.y) + "><" +
                               Mathf.Clamp(panel.layer, 0, 2).ToString(CultureInfo.InvariantCulture) + "><" +
                               subregion + "><" + room.size.x.ToString(CultureInfo.InvariantCulture) + "><" +
                               room.size.y.ToString(CultureInfo.InvariantCulture);
        }
        return lines;
    }

    private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    // Keep the historical internal method identity for legacy callers;
    // the single writer is now Rainwright-owned and uses its backup contract.
    internal static void AtomicReplaceSingle(string temp, string target) =>
        PlayerMapConfigFileTransaction.CommitStaged(temp, target);

}
