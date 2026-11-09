using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// A missing author source is a stable read result. Filesystem notifications invalidate that
/// result; map queries never poll Mod folders. Notifications only request the normal resolver,
/// which still owns all source selection and write authorization on the game thread.
/// </summary>
internal sealed class WorldSourceFailureRetry
{
    private static readonly StringComparer Comparer = Path.DirectorySeparatorChar == '\\'
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private sealed class Subscription : IDisposable
    {
        internal readonly List<IDisposable> Watchers = new();
        internal readonly HashSet<string> Candidates = new(Comparer);
        internal readonly HashSet<string> Ancestors = new(Comparer);
        internal volatile bool Stopped;
        internal int Version;
        internal long LastChange;
        internal Exception Error;

        internal void Changed(object sender, FileSystemEventArgs args)
        {
            if (Stopped) return;
            bool relevant = Candidates.Contains(args.FullPath) ||
                args.ChangeType != WatcherChangeTypes.Changed && Ancestors.Contains(args.FullPath);
            if (args is RenamedEventArgs rename)
                relevant |= Candidates.Contains(rename.OldFullPath) || Ancestors.Contains(rename.OldFullPath);
            if (relevant) Invalidate();
        }

        internal void Failed(object sender, ErrorEventArgs args)
        {
            if (Stopped) return;
            Interlocked.CompareExchange(ref Error, args.GetException(), null);
            Invalidate();
        }

        private void Invalidate()
        {
            Interlocked.Exchange(ref LastChange, Stopwatch.GetTimestamp());
            Interlocked.Increment(ref Version);
        }

        public void Dispose()
        {
            Stopped = true;
            foreach (var watcher in Watchers)
                try { watcher.Dispose(); }
                catch (Exception error) { Core.EditorDiagnostics.Warning("World source notification cleanup failed.", error); }
            Watchers.Clear();
        }
    }

    private string region;
    private object activeMods;
    private int modCount, observedVersion;
    private Subscription subscription;
    private string sharingPath;
    private long nextSharingProbe;
    private Task<bool> sharingProbe;
    private Subscription probeOwner;

    internal void Clear()
    {
        subscription?.Dispose(); subscription = null;
        region = null; activeMods = null; modCount = 0;
        sharingPath = null; nextSharingProbe = 0;
    }

    internal bool ShouldRetry(string requestedRegion)
    {
        var active = ModManager.ActiveMods;
        if (!string.Equals(region, requestedRegion, StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(activeMods, active) || modCount != (active?.Count ?? 0)) return true;
        var captured = subscription;
        if (captured == null || Volatile.Read(ref captured.Version) == observedVersion) return RetryReleasedSharingLock();
        // Coalesce a folder creation/rename and its file writes into one real resolution.
        if (Stopwatch.GetTimestamp() - Interlocked.Read(ref captured.LastChange) < Stopwatch.Frequency / 5) return false;
        Exception error = Interlocked.Exchange(ref captured.Error, null);
        if (error != null) Core.EditorDiagnostics.Warning("World source notifications lost changes; resolving once and rearming notifications.", error);
        return true;
    }

    internal void RememberSharingFailure(string path)
    {
        sharingPath = path;
        nextSharingProbe = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
    }

    private bool RetryReleasedSharingLock()
    {
        // Closing another program's exclusive read/write handle emits no filesystem change.
        // Only this known failed file is probed off-thread; never enumerate Mods to retry a lock.
        if (sharingProbe?.IsCompleted == true)
        {
            var completed = sharingProbe; sharingProbe = null;
            try
            {
                if (completed.GetAwaiter().GetResult() && ReferenceEquals(probeOwner, subscription)) return true;
            }
            catch (Exception error)
            {
                if (ReferenceEquals(probeOwner, subscription))
                {
                    sharingPath = null;
                    Core.EditorDiagnostics.Warning("World source sharing recovery probe failed; original load error retained.", error);
                }
            }
        }
        if (sharingPath == null || sharingProbe != null || Stopwatch.GetTimestamp() < nextSharingProbe) return false;
        string path = sharingPath; probeOwner = subscription;
        nextSharingProbe = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        sharingProbe = StartSharingProbe(path);
        return false;
    }

    private static Task<bool> StartSharingProbe(string path) => Task.Run(() => {
            try { using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)) { } return true; }
            catch (IOException error) when (IsSharingViolation(error)) { return false; }
        });

    internal static bool IsSharingViolation(IOException error)
    {
        int code = error.HResult & 0xFFFF;
        // .NET on Linux exposes EWOULDBLOCK as raw errno 11 rather than a Windows HRESULT.
        // Keep the full-value/platform check so a bad-format HRESULT is not retried as a lock.
        return code == 32 || code == 33 || (Path.DirectorySeparatorChar == '/' && error.HResult == 11);
    }

    // Install before resolution so a source created during a failed read cannot be missed.
    // Explicit Reload and the existing OnModsInit boundary refresh the in-memory Mod context.
    internal void Prepare(string requestedRegion)
    {
        Clear(); region = requestedRegion;
        var active = ModManager.ActiveMods; activeMods = active; modCount = active?.Count ?? 0;
        var captured = new Subscription(); subscription = captured; observedVersion = 0;
        try
        {
            var potentialRoots = new HashSet<string>(Comparer);
            string normalized = requestedRegion.ToLowerInvariant();
            if (active != null)
                foreach (var mod in active)
                {
                    if (!WorldModificationWorkspace.IsContentMod(mod)) continue;
                    // Potential paths only: the hasNewestFolder/hasTargetedVersionFolder
                    // getters query disk. Missing version folders can appear later.
                    foreach (string root in new[] { mod.TargetedPath, mod.NewestPath, mod.path })
                    {
                        if (string.IsNullOrWhiteSpace(root)) continue;
                        string full = Path.GetFullPath(root);
                        AddCandidate(captured, Path.Combine(full, "world", normalized, "world_" + normalized + ".txt"));
                        AddCandidate(captured, Path.Combine(full, "modify", "world", normalized, "world_" + normalized + ".txt"));
                        AddCandidate(captured, Path.Combine(full, "world", normalized + "-rooms"));
                        // Usually two shared parents: workshop/312520 and StreamingAssets/mods.
                        string parent = Path.GetDirectoryName(full);
                        if (!string.IsNullOrEmpty(parent)) potentialRoots.Add(parent);
                    }
                }
            // Collapse nested/version roots in memory before probing disk. Hundreds of Mod
            // paths normally reduce to two directories; don't stat each Mod's version folders.
            var roots = new HashSet<string>(Comparer);
            foreach (string candidate in potentialRoots)
            {
                bool covered = false;
                foreach (string other in potentialRoots)
                    if (other.Length < candidate.Length && IsWithin(candidate, other)) { covered = true; break; }
                if (covered) continue;
                string parent = candidate;
                while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent)) parent = Path.GetDirectoryName(parent);
                if (!string.IsNullOrEmpty(parent) && !Comparer.Equals(parent, Path.GetPathRoot(parent))) roots.Add(parent);
            }
            foreach (string root in roots)
            {
                bool covered = false;
                foreach (string other in roots)
                    if (other.Length < root.Length && IsWithin(root, other)) { covered = true; break; }
                if (covered) continue;
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    try
                    {
                        captured.Watchers.Add(new WorldSourceDirectoryWatcher(root, captured.Changed, captured.Failed));
                        continue;
                    }
                    catch (Exception error) { Core.EditorDiagnostics.Warning("Native world source notifications failed; using exact-path notifications: " + root, error); }
                }
                RegisterExactPaths(captured, root);
            }
        }
        catch (Exception error)
        {
            // Monitoring is optional. Preserve the actual resolver's original result/error.
            Core.EditorDiagnostics.Warning("World source notification setup failed; explicit Reload remains available.", error);
        }
    }

    private static void RegisterExactPaths(Subscription captured, string root)
    {
        // Mono's fallback watcher polls. An exact filename with no subtree limits it to that
        // single path; it must never enumerate a Mod's pictures, translations or other assets.
        var registrations = new HashSet<string>(Comparer);
        foreach (string candidate in captured.Candidates)
        {
            if (!IsWithin(candidate, root)) continue;
            string parent = Path.GetDirectoryName(candidate), leaf = Path.GetFileName(candidate);
            while (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
            { leaf = Path.GetFileName(parent); parent = Path.GetDirectoryName(parent); }
            if (string.IsNullOrEmpty(parent) || !registrations.Add(Path.Combine(parent, leaf))) continue;
            try
            {
                var watcher = new FileSystemWatcher(parent, leaf) {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                        NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.Security
                };
                captured.Watchers.Add(watcher);
                watcher.Created += captured.Changed; watcher.Deleted += captured.Changed;
                watcher.Changed += captured.Changed; watcher.Renamed += captured.Changed;
                watcher.Error += captured.Failed;
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception error) { Core.EditorDiagnostics.Warning("World source notification registration failed; explicit Reload remains available: " + candidate, error); }
        }
    }

    private static void AddCandidate(Subscription captured, string path)
    {
        captured.Candidates.Add(path);
        for (string parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            captured.Ancestors.Add(parent);
    }

    private static bool IsWithin(string path, string root) =>
        path.Length > root.Length && path[root.Length] == Path.DirectorySeparatorChar &&
        path.StartsWith(root, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
