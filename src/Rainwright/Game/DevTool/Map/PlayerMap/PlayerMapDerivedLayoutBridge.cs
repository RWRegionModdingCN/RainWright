using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Projects the latest World Layout and authored-terrain semantics into immutable Player Map
/// snapshots. Derived rooms remain Convert(WorldLayoutPosition) + PlayerMapOffset; Absolute rooms
/// remain independent.
///
/// The backend audits terrain in bounded batches and republishes readiness changes. This projection
/// consumes that revision; drawing the UI does not drive semantic discovery.
/// </summary>


internal static class PlayerMapDerivedLayoutBridge
{
    private static ManualLogSource log;
    private static bool enabled;

    private static PlayerMapRoomSnapshot[] cachedSourceRooms;
    private static EditorMapRoomSnapshot[] cachedWorldRooms;
    private static PlayerMapRoomSnapshot[] cachedProjectedRooms;
    private static int cachedTerrainRevision;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map live derived-layout/terrain projection enabled through direct runtime calls; no self-detour attached.");
    }

    internal static void Disable()
    {
        cachedSourceRooms = null;
        cachedWorldRooms = null;
        cachedProjectedRooms = null;
        cachedTerrainRevision = 0;
        PlayerMapTerrainSemanticRevision.Reset();
        enabled = false;
        log = null;
    }

    internal static PlayerMapPresentationSnapshot Project(EditorSession session, PlayerMapPresentationSnapshot source)
    {
        if (!enabled || source?.Available != true) return source;

        EditorMapPresentationSnapshot world = MapEditorPresentationHub.Current;
        if (world?.Available != true ||
            !string.Equals(source.RegionName ?? string.Empty, world.RegionName ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            return source;

        int terrainRevision = PlayerMapTerrainSemanticRevision.Revision;
        PlayerMapRoomSnapshot[] projected = ProjectRooms(source.Rooms, world.Rooms, terrainRevision);
        if (ReferenceEquals(projected, source.Rooms)) return source;

        long mapRevision = session == null ? 0L : EditorRevisionHub.Get(session, EditorRevisionKind.Map);
        long projectedRevision;
        unchecked
        {
            projectedRevision = source.Revision * 397L + mapRevision;
            projectedRevision = projectedRevision * 397L + terrainRevision;
        }

        return new PlayerMapPresentationSnapshot
        {
            Available = source.Available,
            RegionName = source.RegionName,
            Dirty = source.Dirty,
            Revision = projectedRevision,
            SelectedRoomIndex = world.SelectedRoomIndex,
            Rooms = projected,
            DefaultMaterials = source.DefaultMaterials,
            RenderReport = source.RenderReport,
            Preview = source.Preview
        };
    }

    private static PlayerMapRoomSnapshot[] ProjectRooms(
        PlayerMapRoomSnapshot[] sourceRooms,
        EditorMapRoomSnapshot[] worldRooms,
        int terrainRevision)
    {
        sourceRooms ??= Array.Empty<PlayerMapRoomSnapshot>();
        worldRooms ??= Array.Empty<EditorMapRoomSnapshot>();
        if (ReferenceEquals(cachedSourceRooms, sourceRooms) &&
            ReferenceEquals(cachedWorldRooms, worldRooms) &&
            cachedTerrainRevision == terrainRevision &&
            cachedProjectedRooms != null)
            return cachedProjectedRooms;

        Dictionary<int, EditorMapRoomSnapshot> worldByIndex = new(worldRooms.Length);
        for (int i = 0; i < worldRooms.Length; i++)
        {
            EditorMapRoomSnapshot room = worldRooms[i];
            if (room != null) worldByIndex[room.RoomIndex] = room;
        }

        PlayerMapRoomSnapshot[] result = null;
        for (int i = 0; i < sourceRooms.Length; i++)
        {
            PlayerMapRoomSnapshot room = sourceRooms[i];
            if (room == null || !worldByIndex.TryGetValue(room.RoomIndex, out EditorMapRoomSnapshot world))
                continue;

            Vector2 worldPosition = new(world.X, world.Y);
            Vector2 derivedBase = PlayerMapCoordinateSystem.WorldLayoutToCanon(worldPosition);
            Vector2 effective = room.Mode == PlayerMapPlacementMode.Derived
                ? derivedBase + room.Offset
                : room.AbsolutePosition;
            bool disabled = world.Disabled || room.Hidden;
            // This projection also runs during Present. Consume the backend's published bake;
            // never initiate terrain merges or read its mutable cache from the drawing thread.
            RoomMapBakeSnapshot currentBake = disabled ? RoomMapBakeSnapshot.Empty : room.Bake;

            bool changed =
                (room.WorldPosition - worldPosition).sqrMagnitude > 0.000001f ||
                (room.DerivedBasePosition - derivedBase).sqrMagnitude > 0.000001f ||
                (room.EffectivePosition - effective).sqrMagnitude > 0.000001f ||
                room.Layer != world.Layer ||
                room.Disabled != disabled ||
                room.Selected != world.Selected ||
                !BakeEquivalent(room.Bake, currentBake);
            if (!changed) continue;

            result ??= (PlayerMapRoomSnapshot[])sourceRooms.Clone();
            result[i] = new PlayerMapRoomSnapshot
            {
                RoomIndex = room.RoomIndex,
                Name = room.Name,
                Mode = room.Mode,
                WorldPosition = worldPosition,
                DerivedBasePosition = derivedBase,
                Offset = room.Offset,
                AbsolutePosition = room.AbsolutePosition,
                EffectivePosition = effective,
                Layer = world.Layer,
                Disabled = disabled,
                Hidden = room.Hidden,
                Selected = world.Selected,
                Bake = currentBake
            };
        }

        cachedSourceRooms = sourceRooms;
        cachedWorldRooms = worldRooms;
        cachedTerrainRevision = terrainRevision;
        cachedProjectedRooms = result ?? sourceRooms;
        return cachedProjectedRooms;
    }

    private static bool BakeEquivalent(RoomMapBakeSnapshot a, RoomMapBakeSnapshot b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;
        return a.Status == b.Status &&
               a.Width == b.Width &&
               a.Height == b.Height &&
               string.Equals(a.Error ?? string.Empty, b.Error ?? string.Empty, StringComparison.Ordinal) &&
               ReferenceEquals(a.Runs, b.Runs) &&
               ReferenceEquals(a.NodeAnchors, b.NodeAnchors);
    }

}
