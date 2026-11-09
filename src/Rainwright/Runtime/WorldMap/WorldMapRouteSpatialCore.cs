using System;
using System.Collections.Generic;
using System.Threading;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Rainwright-owned cross-thread-readable world-space route spatial hash.
/// The ImGui frontend supplies only immutable route points. No Unity or game assembly references.
/// </summary>
internal class WorldMapRouteSpatialCore
{
    private sealed class Entry
    {
        internal string Id = string.Empty;
        internal Num.Vector2 Min;
        internal Num.Vector2 Max;
        internal Num.Vector2[] Points = Array.Empty<Num.Vector2>();
        internal long[] Cells = Array.Empty<long>();
    }

    private const float CellSize = 256f;

    private readonly ReaderWriterLockSlim gate =
        new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, Entry> entries =
        new(StringComparer.Ordinal);
    private readonly Dictionary<long, HashSet<string>> cells = new();
    private readonly HashSet<string> unindexed = new(StringComparer.Ordinal);

    [ThreadStatic] private static HashSet<string> querySeen;

    internal int Count
    {
        get
        {
            gate.EnterReadLock();
            try { return entries.Count; }
            finally { gate.ExitReadLock(); }
        }
    }

    internal void Upsert(
        string connectionId,
        Num.Vector2[] points)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        points ??= Array.Empty<Num.Vector2>();

        gate.EnterWriteLock();
        try
        {
            RemoveLocked(connectionId);
            if (points.Length < 2) return;

            Num.Vector2 min = points[0];
            Num.Vector2 max = points[0];
            for (int i = 1; i < points.Length; i++)
            {
                min = Num.Vector2.Min(min, points[i]);
                max = Num.Vector2.Max(max, points[i]);
            }

            List<long> occupied = new();
            if (EnumerateCells(min, max, occupied)) unindexed.Add(connectionId);
            Entry entry = new()
            {
                Id = connectionId,
                Min = min,
                Max = max,
                Points = points,
                Cells = occupied.ToArray()
            };
            entries.Add(entry.Id, entry);

            for (int i = 0; i < entry.Cells.Length; i++)
            {
                long key = entry.Cells[i];
                if (!cells.TryGetValue(key, out HashSet<string> ids))
                {
                    ids = new HashSet<string>(StringComparer.Ordinal);
                    cells.Add(key, ids);
                }
                ids.Add(entry.Id);
            }
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    internal void Remove(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        gate.EnterWriteLock();
        try { RemoveLocked(id); }
        finally { gate.ExitWriteLock(); }
    }

    internal bool Query(
        Num.Vector2 min,
        Num.Vector2 max,
        List<string> output)
    {
        if (output == null) return false;
        output.Clear();

        HashSet<string> seen =
            querySeen ??= new HashSet<string>(StringComparer.Ordinal);
        seen.Clear();

        gate.EnterReadLock();
        try
        {
            if (entries.Count == 0) return false;
            Num.Vector2 low = Num.Vector2.Min(min, max);
            max = Num.Vector2.Max(min, max);
            min = low;
            GetCellRange(min, max, out int minX, out int minY, out int maxX, out int maxY);

            bool scanEntries = WorldMapSpatialCellPolicy.CellCount(minX, minY, maxX, maxY) > Math.Max(16, entries.Count);
            if (scanEntries)
            {
                foreach (Entry entry in entries.Values)
                    if (Intersects(entry.Min, entry.Max, min, max)) output.Add(entry.Id);
            }
            else
            {
                foreach (string id in unindexed)
                    if (entries.TryGetValue(id, out Entry entry) && Intersects(entry.Min, entry.Max, min, max))
                        output.Add(id);
                for (long y = minY; y <= maxY; y++)
                {
                    for (long x = minX; x <= maxX; x++)
                    {
                        if (!cells.TryGetValue(CellKey((int)x, (int)y), out HashSet<string> ids))
                            continue;

                        foreach (string id in ids)
                        {
                            if (!seen.Add(id) ||
                                !entries.TryGetValue(id, out Entry entry) ||
                                !Intersects(entry.Min, entry.Max, min, max))
                                continue;

                            output.Add(id);
                        }
                    }
                }
            }

            output.Sort(StringComparer.Ordinal);
            return true;
        }
        finally
        {
            gate.ExitReadLock();
            seen.Clear();
        }
    }

    internal bool TryGetPoints(
        string id,
        out Num.Vector2[] points)
    {
        points = null;
        if (string.IsNullOrEmpty(id))
            return false;

        gate.EnterReadLock();
        try
        {
            if (!entries.TryGetValue(id, out Entry entry) ||
                entry?.Points == null ||
                entry.Points.Length < 2)
                return false;

            // Route point arrays are immutable after Upsert. Returning the retained reference avoids
            // per-frame allocations on the ImGui Draw path.
            points = entry.Points;
            return true;
        }
        finally
        {
            gate.ExitReadLock();
        }
    }

    internal bool TryHit(
        Num.Vector2 point,
        float radius,
        HashSet<string> allowedIds,
        out string id,
        out float distanceSquared)
    {
        id = string.Empty;
        distanceSquared = radius * radius;
        Num.Vector2 extent = new(radius, radius);
        Num.Vector2 min = point - extent;
        Num.Vector2 max = point + extent;

        HashSet<string> seen =
            querySeen ??= new HashSet<string>(StringComparer.Ordinal);
        seen.Clear();

        gate.EnterReadLock();
        try
        {
            if (entries.Count == 0) return false;
            GetCellRange(min, max, out int minX, out int minY, out int maxX, out int maxY);

            // A very wide pick radius must cost O(routes), never O(empty grid area).
            bool scanEntries = WorldMapSpatialCellPolicy.CellCount(minX, minY, maxX, maxY) > Math.Max(16, entries.Count);
            IEnumerable<string> broadCandidates = scanEntries ? entries.Keys : unindexed;
            foreach (string candidateId in broadCandidates)
            {
                if (allowedIds != null && !allowedIds.Contains(candidateId)) continue;
                if (!entries.TryGetValue(candidateId, out Entry entry)) continue;
                TestHit(entry, point, extent, min, max, ref id, ref distanceSquared);
            }
            if (scanEntries) return id.Length > 0;
            for (long y = minY; y <= maxY; y++)
            {
                for (long x = minX; x <= maxX; x++)
                {
                    if (!cells.TryGetValue(CellKey((int)x, (int)y), out HashSet<string> ids))
                        continue;

                    foreach (string candidateId in ids)
                    {
                        if (allowedIds != null &&
                            !allowedIds.Contains(candidateId))
                            continue;

                        if (!seen.Add(candidateId) ||
                            !entries.TryGetValue(candidateId, out Entry entry) ||
                            !Intersects(
                                entry.Min - extent,
                                entry.Max + extent,
                                min,
                                max))
                            continue;

                        TestHit(entry, point, extent, min, max, ref id, ref distanceSquared);
                    }
                }
            }

            return id.Length > 0;
        }
        finally
        {
            gate.ExitReadLock();
            seen.Clear();
        }
    }

    internal void Reset()
    {
        gate.EnterWriteLock();
        try
        {
            entries.Clear();
            cells.Clear();
            unindexed.Clear();
        }
        finally
        {
            gate.ExitWriteLock();
        }
    }

    private void RemoveLocked(string id)
    {
        if (!entries.TryGetValue(id, out Entry entry))
            return;

        entries.Remove(id);
        unindexed.Remove(id);
        for (int i = 0; i < entry.Cells.Length; i++)
        {
            long key = entry.Cells[i];
            if (!cells.TryGetValue(key, out HashSet<string> ids)) continue;
            ids.Remove(id);
            if (ids.Count == 0) cells.Remove(key);
        }
    }

    private static bool EnumerateCells(
        Num.Vector2 min,
        Num.Vector2 max,
        List<long> output)
    {
        GetCellRange(min, max, out int minX, out int minY, out int maxX, out int maxY);
        // Large bounds retain exact geometry in the direct-scan set; no cells/data are discarded.
        if (WorldMapSpatialCellPolicy.CellCount(minX, minY, maxX, maxY) > 256) return true;
        for (long y = minY; y <= maxY; y++)
            for (long x = minX; x <= maxX; x++)
                output.Add(CellKey((int)x, (int)y));
        return false;
    }

    private static void GetCellRange(
        Num.Vector2 min,
        Num.Vector2 max,
        out int minX,
        out int minY,
        out int maxX,
        out int maxY)
    {
        float x0 = Math.Min(min.X, max.X);
        float x1 = Math.Max(min.X, max.X);
        float y0 = Math.Min(min.Y, max.Y);
        float y1 = Math.Max(min.Y, max.Y);
        minX = WorldMapSpatialCellPolicy.ToCell(x0 / CellSize);
        maxX = WorldMapSpatialCellPolicy.ToCell(x1 / CellSize);
        minY = WorldMapSpatialCellPolicy.ToCell(y0 / CellSize);
        maxY = WorldMapSpatialCellPolicy.ToCell(y1 / CellSize);
    }

    private static void TestHit(Entry entry, Num.Vector2 point, Num.Vector2 extent,
        Num.Vector2 min, Num.Vector2 max, ref string id, ref float distanceSquared)
    {
        if (!Intersects(entry.Min - extent, entry.Max + extent, min, max)) return;
        for (int p = 0; p < entry.Points.Length - 1; p++)
        {
            float candidateDistance = DistanceToSegmentSquared(point, entry.Points[p], entry.Points[p + 1]);
            if (candidateDistance >= distanceSquared) continue;
            distanceSquared = candidateDistance;
            id = entry.Id;
        }
    }

    private static long CellKey(int x, int y) =>
        ((long)(uint)x << 32) | (uint)y;

    private static bool Intersects(
        Num.Vector2 aMin,
        Num.Vector2 aMax,
        Num.Vector2 bMin,
        Num.Vector2 bMax) =>
        aMax.X >= bMin.X && aMin.X <= bMax.X &&
        aMax.Y >= bMin.Y && aMin.Y <= bMax.Y;

    private static float DistanceToSegmentSquared(
        Num.Vector2 p,
        Num.Vector2 a,
        Num.Vector2 b)
    {
        Num.Vector2 ab = b - a;
        float lengthSquared = ab.LengthSquared();
        if (lengthSquared <= 0.0001f)
            return Num.Vector2.DistanceSquared(p, a);

        float t = Num.Vector2.Dot(p - a, ab) / lengthSquared;
        if (t < 0f) t = 0f;
        else if (t > 1f) t = 1f;
        return Num.Vector2.DistanceSquared(p, a + ab * t);
    }
}

internal static class WorldMapSpatialCellPolicy
{
    internal static double CellCount(int minX, int minY, int maxX, int maxY) =>
        ((double)maxX - minX + 1d) * ((double)maxY - minY + 1d);

    internal static int ToCell(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentOutOfRangeException(nameof(value), "Spatial coordinates must be finite.");
        return value <= int.MinValue ? int.MinValue : value >= int.MaxValue ? int.MaxValue : (int)Math.Floor(value);
    }
}
