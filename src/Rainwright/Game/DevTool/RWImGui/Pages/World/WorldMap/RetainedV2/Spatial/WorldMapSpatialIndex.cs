using System;
using System.Collections.Generic;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Scene-side adapter. Rainwright owns the spatial hash and reader lock;
/// only source-room bounds resolution stays here.
/// </summary>
internal sealed class WorldMapSpatialIndex : WorldMapRoomSpatialCore
{
    internal void ApplyDirty(WorldMapScene scene,
        WorldMapRoomResourceStore resources, WorldMapDirtySet dirty)
    {
        if (scene == null || dirty == null) return;
        List<WorldMapRoomIndexUpdate> updates = new();
        if (dirty.FullRebuild)
        {
            foreach (WorldMapScene.RoomNode room in scene.Rooms.Values)
                updates.Add(Capture(room, resources));
            ApplyBatch(true, null, updates);
            return;
        }
        foreach (int id in dirty.RoomTransforms)
            updates.Add(Capture(scene, resources, id, false));
        foreach (int id in dirty.RoomMetadata)
            updates.Add(Capture(scene, resources, id, false));
        foreach (int id in dirty.RoomPorts)
            updates.Add(Capture(scene, resources, id, true));
        ApplyBatch(false, dirty.RemovedRooms, updates);
    }

    internal void InvalidateRooms(WorldMapScene scene,
        WorldMapRoomResourceStore resources, IEnumerable<int> roomIndices)
    {
        if (scene == null || roomIndices == null) return;
        List<WorldMapRoomIndexUpdate> updates = new();
        foreach (int id in roomIndices)
            updates.Add(Capture(scene, resources, id, false));
        ApplyBatch(false, null, updates);
    }

    private static WorldMapRoomIndexUpdate Capture(
        WorldMapScene scene, WorldMapRoomResourceStore resources,
        int roomIndex, bool onlyIfMissing)
    {
        return scene.TryGetRoom(roomIndex, out WorldMapScene.RoomNode room)
            ? Capture(room, resources, onlyIfMissing)
            : WorldMapRoomIndexUpdate.Missing(roomIndex, onlyIfMissing);
    }

    private static WorldMapRoomIndexUpdate Capture(WorldMapScene.RoomNode room,
        WorldMapRoomResourceStore resources, bool onlyIfMissing = false)
    {
        WorldMapWorldSpaceRouter.GetRoomBounds(room, resources,
            out Num.Vector2 min, out Num.Vector2 max);
        return new WorldMapRoomIndexUpdate(
            room.RoomIndex, room.Layer, min, max,
            onlyIfMissing: onlyIfMissing);
    }
}
