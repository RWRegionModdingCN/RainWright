using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Direct static-room loader used by Player Map baking. It deliberately does not construct Room,
/// RoomPreparer, ShortcutMapper, MiniMap, or call any of their Update methods.
/// </summary>
internal static class RoomMapSourceLoader
{
    internal static bool TryLoad(string roomName, out RoomMapSource source, out string sourcePath, out string error)
    {
        source = null;
        sourcePath = string.Empty;
        error = null;
        try
        {
            sourcePath = WorldLoader.FindRoomFile(roomName, includeRootDirectory: false, ".txt", showWarning: false);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "Room source file was not found.";
                return false;
            }

            source = Load(sourcePath);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.ToString();
            source = null;
            return false;
        }
    }

    // Paths must be resolved by the game thread before entering this CPU/file-only operation.
    internal static RoomMapSource Load(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length < 12) throw new InvalidDataException("Room source has fewer than 12 lines.");
        RoomPreprocessor.VersionFix(ref lines);
        return RoomMapTextDecoder.Parse(lines);
    }

}

internal sealed class RoomMapBakeCacheEntry
{
    internal int RoomIndex;
    internal string RoomName = string.Empty;
    internal RoomMapBakeStatus Status;
    internal RoomMapBake Bake;
    internal string Error = string.Empty;
    internal bool Queued;
    internal string LastSourcePath = string.Empty;
    internal long LastSourceLength;
    internal DateTime LastSourceWriteTimeUtc;
    internal int NextSourceAuditFrame;
}

internal static class RoomMapBakeCache
{
    private sealed class Work
    {
        internal RoomMapBakeCacheEntry Entry;
        internal int Index;
        internal string Name, Path;
        internal long Length;
        internal DateTime WriteTime;
        internal Task<RoomMapBake> Task;
    }

    private const int MaxWorkers = 2;
    private static readonly List<Work> Workers = new();
    private static readonly Dictionary<int, RoomMapBakeCacheEntry> Entries = new();
    private static readonly Queue<int> Pending = new();
    private static int revision = 1;

    internal static int Revision => revision;
    internal static int ActiveWorkers => Workers.Count;

    internal static void Request(int roomIndex, string roomName)
    {
        if (!Entries.TryGetValue(roomIndex, out RoomMapBakeCacheEntry entry))
        {
            entry = new RoomMapBakeCacheEntry
            {
                RoomIndex = roomIndex,
                RoomName = roomName ?? string.Empty,
                Status = RoomMapBakeStatus.Pending,
                Queued = true
            };
            Entries.Add(roomIndex, entry);
            Pending.Enqueue(roomIndex);
            revision++;
            return;
        }

        string nextName = roomName ?? entry.RoomName;
        if (!string.Equals(nextName, entry.RoomName, StringComparison.Ordinal))
        {
            entry.RoomName = nextName;
            entry.Status = RoomMapBakeStatus.Pending;
            if (!entry.Queued) { entry.Queued = true; Pending.Enqueue(roomIndex); }
            revision++;
            return;
        }
        int frame = Time.frameCount;
        if (entry.Status == RoomMapBakeStatus.Ready && frame >= entry.NextSourceAuditFrame)
        {
            entry.NextSourceAuditFrame = frame + 120 + Math.Abs(roomIndex % 31);
            if (SourceChanged(entry))
            {
                entry.Status = RoomMapBakeStatus.Pending;
                entry.Queued = true;
                Pending.Enqueue(roomIndex);
                revision++;
            }
        }
        else if ((entry.Status == RoomMapBakeStatus.Missing || entry.Status == RoomMapBakeStatus.Failed) &&
                 !entry.Queued && frame >= entry.NextSourceAuditFrame)
        {
            entry.NextSourceAuditFrame = frame + 120 + Math.Abs(roomIndex % 31);
            string path = WorldLoader.FindRoomFile(entry.RoomName, false, ".txt", false);
            if (!string.Equals(path ?? string.Empty, entry.LastSourcePath ?? string.Empty, StringComparison.OrdinalIgnoreCase) ||
                SourceInfoChanged(path, entry.LastSourceLength, entry.LastSourceWriteTimeUtc))
            {
                entry.Status = RoomMapBakeStatus.Pending;
                entry.Queued = true;
                Pending.Enqueue(roomIndex);
                revision++;
            }
        }
    }

    internal static void ProcessPending(int budget)
    {
        PlayerMapTerrainBakeBridge.ProcessPending();
        budget = Math.Max(1, budget);
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 2 / 1000;
        for (int i = Workers.Count - 1; i >= 0 && budget > 0 && Stopwatch.GetTimestamp() < deadline; i--)
        {
            Work work = Workers[i];
            if (!work.Task.IsCompleted) continue;
            Workers.RemoveAt(i);
            budget--;
            Exception error = work.Task.IsFaulted ? work.Task.Exception.GetBaseException() : null;
            bool current = Entries.TryGetValue(work.Entry.RoomIndex, out RoomMapBakeCacheEntry entry) &&
                           ReferenceEquals(entry, work.Entry) && string.Equals(entry.RoomName, work.Name, StringComparison.Ordinal);
            if (!current)
            {
                if (error != null) Core.EditorDiagnostics.Error("Retired Player Map room bake failed: " + work.Name, error);
                continue;
            }
            // A changed file/path wins over an in-flight bake, even before its next periodic audit.
            string resolved = WorldLoader.FindRoomFile(entry.RoomName, false, ".txt", false);
            if (!string.Equals(resolved, work.Path, StringComparison.OrdinalIgnoreCase) ||
                SourceInfoChanged(work.Path, work.Length, work.WriteTime))
            {
                if (error != null) Core.EditorDiagnostics.Warning("Player Map source changed; retrying room bake: " + work.Name, error);
                entry.Queued = true;
                Pending.Enqueue(entry.RoomIndex);
                continue;
            }
            if (error != null) { FailBuild(entry, work.Path, error); continue; }
            CommitBuild(entry, work.Task.Result);
        }

        while (budget-- > 0 && Pending.Count > 0 && Workers.Count < MaxWorkers && Stopwatch.GetTimestamp() < deadline)
        {
            int roomIndex = Pending.Dequeue();
            if (!Entries.TryGetValue(roomIndex, out RoomMapBakeCacheEntry entry) || !entry.Queued) continue;
            entry.Queued = false;
            StartBuild(entry);
        }
    }

    internal static RoomMapBakeSnapshot GetSnapshot(int roomIndex)
    {
        if (!Entries.TryGetValue(roomIndex, out RoomMapBakeCacheEntry entry))
            return new RoomMapBakeSnapshot { Status = RoomMapBakeStatus.Missing };
        RoomMapBake bake = entry.Bake;
        RoomMapBakeSnapshot source = new()
        {
            Status = entry.Status,
            Width = bake?.Width ?? 0,
            Height = bake?.Height ?? 0,
            Error = entry.Error ?? string.Empty,
            Runs = bake?.Runs ?? Array.Empty<RoomMapPreviewRun>(),
            NodeAnchors = bake?.NodeAnchors ?? Array.Empty<RoomMapNodeAnchorSnapshot>()
        };
        return PlayerMapTerrainBakeBridge.ProjectSnapshot(roomIndex, source, bake);
    }

    internal static bool TryGetReady(int roomIndex, out RoomMapBake bake)
    {
        bake = null;
        if (!Entries.TryGetValue(roomIndex, out RoomMapBakeCacheEntry entry) ||
            entry.Status != RoomMapBakeStatus.Ready ||
            entry.Bake == null)
            return false;

        return PlayerMapTerrainBakeBridge.TryEnhanceReady(roomIndex, entry.Bake, out bake);
    }

    internal static void Clear()
    {
        Entries.Clear();
        Pending.Clear();
        PlayerMapTerrainBakeBridge.Clear();
        // Keep retired workers until they finish. Reset never waits, and repeated region changes
        // cannot evade the two-worker limit or publish an old room into a reused room index.
        revision++;
    }

    private static void StartBuild(RoomMapBakeCacheEntry entry)
    {
        string path = string.Empty;
        try
        {
            path = WorldLoader.FindRoomFile(entry.RoomName, false, ".txt", false);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("Room source file was not found.", path);
            FileInfo info = new(path);
            Work work = new() { Entry = entry, Index = entry.RoomIndex, Name = entry.RoomName, Path = path, Length = info.Length, WriteTime = info.LastWriteTimeUtc };
            if (MapRoomGeometryPresentationHub.TryGetDecodedRoomSource(entry.RoomIndex, out WorldMapRoomSource shared) &&
                string.Equals(shared.Bake.SourcePath, path, StringComparison.OrdinalIgnoreCase) &&
                shared.Bake.SourceLength == work.Length && shared.Bake.SourceWriteTimeUtc == work.WriteTime)
            {
                CommitBuild(entry, Rebind(shared.Bake, entry.RoomIndex, entry.RoomName));
                return;
            }
            work.Task = Task.Run(() =>
            {
                RoomMapSource source = RoomMapSourceLoader.Load(work.Path);
                RoomMapBake bake = RoomMapSemanticCompiler.Compile(work.Index, work.Name, source, work.Path);
                if (SourceInfoChanged(work.Path, work.Length, work.WriteTime))
                    throw new IOException("Room source changed during Player Map baking: " + work.Name);
                return bake;
            });
            Workers.Add(work);
        }
        catch (Exception exception)
        {
            FailBuild(entry, path, exception);
        }
    }

    private static void CommitBuild(RoomMapBakeCacheEntry entry, RoomMapBake bake)
    {
        entry.Bake = bake;
        entry.Status = RoomMapBakeStatus.Ready;
        entry.Error = string.Empty;
        entry.LastSourcePath = bake.SourcePath;
        entry.LastSourceLength = bake.SourceLength;
        entry.LastSourceWriteTimeUtc = bake.SourceWriteTimeUtc;
        entry.NextSourceAuditFrame = Time.frameCount + 120 + Math.Abs(entry.RoomIndex % 31);
        revision++;
    }

    internal static void NotifyTerrainCompletion() => revision++;

    private static void FailBuild(RoomMapBakeCacheEntry entry, string path, Exception error)
    {
        entry.Bake = null;
        entry.Status = error is FileNotFoundException ? RoomMapBakeStatus.Missing : RoomMapBakeStatus.Failed;
        entry.Error = error.ToString();
        Core.EditorDiagnostics.Error("Player Map room bake failed: " + entry.RoomName, error);
        try { UpdateSourceEvidence(entry, path); }
        catch (Exception evidenceError)
        {
            entry.Error += "\nSource evidence could not be captured: " + evidenceError;
            Core.EditorDiagnostics.Error("Player Map failure evidence could not be captured: " + entry.RoomName, evidenceError);
        }
        entry.NextSourceAuditFrame = Time.frameCount + 120 + Math.Abs(entry.RoomIndex % 31);
        revision++;
    }

    private static RoomMapBake Rebind(RoomMapBake source, int index, string name)
    {
        RoomMapBake result = new()
        {
            RoomIndex = index, RoomName = name, Width = source.Width, Height = source.Height,
            Pixels = source.Pixels, Runs = source.Runs, NodeAnchors = source.NodeAnchors,
            SourcePath = source.SourcePath, SourceLength = source.SourceLength, SourceWriteTimeUtc = source.SourceWriteTimeUtc
        };
        foreach (RoomMapNodeAnchorSnapshot anchor in result.NodeAnchors) result.NodeAnchorByIndex[anchor.NodeIndex] = anchor;
        return result;
    }

    private static bool SourceChanged(RoomMapBakeCacheEntry entry)
    {
        string path = WorldLoader.FindRoomFile(entry.RoomName, false, ".txt", false);
        if (!string.Equals(path ?? string.Empty, entry.LastSourcePath ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            return true;
        return SourceInfoChanged(path, entry.LastSourceLength, entry.LastSourceWriteTimeUtc);
    }

    private static bool SourceInfoChanged(string path, long length, DateTime writeTimeUtc)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return length != 0L || writeTimeUtc != DateTime.MinValue;
        FileInfo info = new(path);
        return info.Length != length || info.LastWriteTimeUtc != writeTimeUtc;
    }

    private static void UpdateSourceEvidence(RoomMapBakeCacheEntry entry, string path)
    {
        entry.LastSourcePath = path ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            entry.LastSourceLength = 0L;
            entry.LastSourceWriteTimeUtc = DateTime.MinValue;
            return;
        }
        FileInfo info = new(path);
        entry.LastSourceLength = info.Length;
        entry.LastSourceWriteTimeUtc = info.LastWriteTimeUtc;
    }
}
