using System;
using System.Collections.Generic;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Portable incremental world-space room spatial hash.
///
/// Static map geometry is sparse and rooms move individually while authoring, so a fixed world-space
/// hash gives predictable O(local-cells) pan/hover queries and cheaper incremental updates than
/// rebuilding a hierarchy during every room drag.
/// </summary>
/// <summary>Frozen room bounds captured by the host. No scene references.</summary>
internal readonly struct WorldMapRoomIndexUpdate
{
    internal readonly int RoomIndex;
    internal readonly int Layer;
    internal readonly Num.Vector2 Min, Max;
    internal readonly bool Exists, OnlyIfMissing;
    internal WorldMapRoomIndexUpdate(int roomIndex, int layer,
        Num.Vector2 min, Num.Vector2 max, bool exists = true, bool onlyIfMissing = false)
    {
        RoomIndex = roomIndex; Layer = layer; Min = min; Max = max;
        Exists = exists; OnlyIfMissing = onlyIfMissing;
    }
    internal static WorldMapRoomIndexUpdate Missing(int id, bool onlyIfMissing = false) =>
        new(id, 0, default, default, exists: false, onlyIfMissing: onlyIfMissing);
}

internal class WorldMapRoomSpatialCore
{
    private sealed class Entry
    {
        internal int RoomIndex;
        internal int Layer;
        internal Num.Vector2 Min;
        internal Num.Vector2 Max;
        internal long[] Cells = Array.Empty<long>();
    }

    private const float CellSize = 256f;

    private readonly object gate = new();
    private readonly Dictionary<int, Entry> entries = new();
    private readonly Dictionary<long, HashSet<int>> cells = new();
    private readonly HashSet<int> unindexed = new();
    private readonly HashSet<int> querySeen = new();

    internal int Count
    {
        get
        {
            lock (gate) return entries.Count;
        }
    }

    /// <summary>Commit complete room index deltas under one reader-safe lock.</summary>
    internal void ApplyBatch(bool fullRebuild, IEnumerable<int> removedRooms,
        IEnumerable<WorldMapRoomIndexUpdate> updates)
    {
        lock (gate)
        {
            if (fullRebuild) ClearLocked();
            else if (removedRooms != null)
                foreach (int id in removedRooms) RemoveLocked(id);
            if (updates == null) return;
            foreach (WorldMapRoomIndexUpdate update in updates)
            {
                if (update.OnlyIfMissing && entries.ContainsKey(update.RoomIndex))
                    continue;
                if (update.Exists) UpsertLocked(update);
                else RemoveLocked(update.RoomIndex);
            }
        }
    }

    internal bool TryHitRoom(
        Num.Vector2 point,
        int layerMask,
        out int roomIndex)
    {
        roomIndex = -1;
        lock (gate)
        {
            if (entries.Count == 0) return false;

            long key = CellKey(
                FloorToInt(point.X / CellSize),
                FloorToInt(point.Y / CellSize));
            cells.TryGetValue(key, out HashSet<int> ids);

            int bestLayer = int.MinValue;
            int bestRoom = -1;
            foreach (int id in unindexed)
                SelectHit(entries[id], point, layerMask, ref bestLayer, ref bestRoom);
            if (ids != null) foreach (int id in ids)
            {
                if (entries.TryGetValue(id, out Entry entry))
                    SelectHit(entry, point, layerMask, ref bestLayer, ref bestRoom);
            }

            roomIndex = bestRoom;
            return bestRoom >= 0;
        }
    }

    internal bool Query(
        Num.Vector2 min,
        Num.Vector2 max,
        int layerMask,
        List<int> output)
    {
        if (output == null) return false;
        output.Clear();

        lock (gate)
        {
            if (entries.Count == 0) return false;
            querySeen.Clear();
            Num.Vector2 low = Num.Vector2.Min(min, max);
            max = Num.Vector2.Max(min, max);
            min = low;

            int minX = FloorToInt(Math.Min(min.X, max.X) / CellSize);
            int maxX = FloorToInt(Math.Max(min.X, max.X) / CellSize);
            int minY = FloorToInt(Math.Min(min.Y, max.Y) / CellSize);
            int maxY = FloorToInt(Math.Max(min.Y, max.Y) / CellSize);

            bool scanEntries = WorldMapSpatialCellPolicy.CellCount(minX, minY, maxX, maxY) > Math.Max(16, entries.Count);
            if (scanEntries)
            {
                foreach (Entry entry in entries.Values)
                    if (LayerVisible(entry.Layer, layerMask) && Intersects(entry.Min, entry.Max, min, max))
                        output.Add(entry.RoomIndex);
            }
            else
            {
                foreach (int id in unindexed)
                {
                    Entry entry = entries[id];
                    if (LayerVisible(entry.Layer, layerMask) && Intersects(entry.Min, entry.Max, min, max)) output.Add(id);
                }
                for (long y = minY; y <= maxY; y++)
                {
                    for (long x = minX; x <= maxX; x++)
                    {
                        if (!cells.TryGetValue(CellKey((int)x, (int)y), out HashSet<int> ids))
                            continue;

                        foreach (int id in ids)
                        {
                            if (!querySeen.Add(id) ||
                                !entries.TryGetValue(id, out Entry entry) ||
                                !LayerVisible(entry.Layer, layerMask) ||
                                !Intersects(entry.Min, entry.Max, min, max))
                                continue;

                            output.Add(id);
                        }
                    }
                }
            }

            output.Sort((a, b) =>
            {
                int la = entries.TryGetValue(a, out Entry ea) ? ea.Layer : 0;
                int lb = entries.TryGetValue(b, out Entry eb) ? eb.Layer : 0;
                int layerCompare = la.CompareTo(lb);
                return layerCompare != 0 ? layerCompare : a.CompareTo(b);
            });
            return true;
        }
    }

    internal void Reset()
    {
        lock (gate)
            ClearLocked();
    }

    private void UpsertLocked(WorldMapRoomIndexUpdate update)
    {
        Num.Vector2 min = update.Min;
        Num.Vector2 max = update.Max;

        if (entries.TryGetValue(update.RoomIndex, out Entry existing) &&
            existing.Layer == update.Layer &&
            Approximately(existing.Min, min) &&
            Approximately(existing.Max, max))
            return;

        RemoveLocked(update.RoomIndex);

        List<long> occupied = new();
        int minX = FloorToInt(min.X / CellSize);
        int maxX = FloorToInt(max.X / CellSize);
        int minY = FloorToInt(min.Y / CellSize);
        int maxY = FloorToInt(max.Y / CellSize);

        if (WorldMapSpatialCellPolicy.CellCount(minX, minY, maxX, maxY) > 256)
            unindexed.Add(update.RoomIndex);
        else for (long y = minY; y <= maxY; y++)
        {
            for (long x = minX; x <= maxX; x++)
            {
                long key = CellKey((int)x, (int)y);
                occupied.Add(key);
                if (!cells.TryGetValue(key, out HashSet<int> ids))
                {
                    ids = new HashSet<int>();
                    cells.Add(key, ids);
                }
                ids.Add(update.RoomIndex);
            }
        }

        entries[update.RoomIndex] = new Entry
        {
            RoomIndex = update.RoomIndex,
            Layer = update.Layer,
            Min = min,
            Max = max,
            Cells = occupied.ToArray()
        };
    }

    private void RemoveLocked(int roomIndex)
    {
        if (!entries.TryGetValue(roomIndex, out Entry entry))
            return;

        entries.Remove(roomIndex);
        unindexed.Remove(roomIndex);
        for (int i = 0; i < entry.Cells.Length; i++)
        {
            long key = entry.Cells[i];
            if (!cells.TryGetValue(key, out HashSet<int> ids)) continue;
            ids.Remove(roomIndex);
            if (ids.Count == 0) cells.Remove(key);
        }
    }

    private void ClearLocked()
    {
        entries.Clear();
        cells.Clear();
        unindexed.Clear();
        querySeen.Clear();
    }

    private static bool LayerVisible(int layer, int mask) =>
        layer >= 0 && layer < 31 && (mask & (1 << layer)) != 0;

    private static bool Intersects(
        Num.Vector2 aMin,
        Num.Vector2 aMax,
        Num.Vector2 bMin,
        Num.Vector2 bMax) =>
        aMax.X >= bMin.X && aMin.X <= bMax.X &&
        aMax.Y >= bMin.Y && aMin.Y <= bMax.Y;

    private static bool Approximately(Num.Vector2 a, Num.Vector2 b) =>
        Num.Vector2.DistanceSquared(a, b) <= 0.0001f;

    private static int FloorToInt(float value) => WorldMapSpatialCellPolicy.ToCell(value);

    private static void SelectHit(Entry entry, Num.Vector2 point, int layerMask, ref int bestLayer, ref int bestRoom)
    {
        if (!LayerVisible(entry.Layer, layerMask) || point.X < entry.Min.X || point.X > entry.Max.X ||
            point.Y < entry.Min.Y || point.Y > entry.Max.Y || entry.Layer < bestLayer) return;
        bestLayer = entry.Layer;
        bestRoom = entry.RoomIndex;
    }

    private static long CellKey(int x, int y) =>
        ((long)(uint)x << 32) | (uint)y;
}
