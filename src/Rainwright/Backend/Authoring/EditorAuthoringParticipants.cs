using System;
using System.Collections.Generic;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Extensions;

/// <summary>
/// A provider for one optional author-data store; no DryCycle gameplay or ImGui types.
/// Providers are registered and disposed on the game's main thread.
/// </summary>
public delegate bool EditorRecoveryTextProvider(out string path, out string content);

public static class EditorAuthoringParticipants
{
    private sealed class Entry
    {
        internal readonly string Id;
        internal readonly Func<bool> Dirty;
        internal readonly Func<bool> Save;
        internal readonly EditorRecoveryTextProvider Capture;
        internal readonly Action<string, Exception> Report;
        internal Entry(string id, Func<bool> dirty, Func<bool> save,
            EditorRecoveryTextProvider capture, Action<string, Exception> report)
        {
            Id = id; Dirty = dirty; Save = save; Capture = capture; Report = report;
        }
    }

    private sealed class Lease : IDisposable
    {
        private Entry entry;
        internal Lease(Entry entry) => this.entry = entry;
        public void Dispose()
        {
            Entry oldEntry = Interlocked.Exchange(ref entry, null);
            if (oldEntry == null) return;
            lock (Sync)
            {
                Entry[] old = entries;
                int index = Array.IndexOf(old, oldEntry);
                if (index < 0) return;
                Entry[] next = new Entry[old.Length - 1];
                if (index > 0) Array.Copy(old, 0, next, 0, index);
                if (index + 1 < old.Length)
                    Array.Copy(old, index + 1, next, index, old.Length - index - 1);
                Volatile.Write(ref entries, next);
            }
        }
    }

    private static readonly object Sync = new();
    private static Entry[] entries = Array.Empty<Entry>();
    private static string lastError = string.Empty;

    public static string LastError => Volatile.Read(ref lastError);

    public static IDisposable Register(string id, Func<bool> isDirty, Func<bool> save,
        EditorRecoveryTextProvider capture, Action<string, Exception> report = null)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Participant ID is required.", nameof(id));
        if (isDirty == null) throw new ArgumentNullException(nameof(isDirty));
        if (save == null) throw new ArgumentNullException(nameof(save));
        string normalized = id.Trim();
        Entry nextEntry = new(normalized, isDirty, save, capture, report);
        lock (Sync)
        {
            Entry[] old = entries;
            for (int i = 0; i < old.Length; i++)
                if (string.Equals(old[i].Id, normalized, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Duplicate authoring participant: " + normalized);
            Entry[] next = new Entry[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = nextEntry;
            Volatile.Write(ref entries, next);
        }
        return new Lease(nextEntry);
    }

    public static bool HasDirty
    {
        get
        {
            Entry[] snapshot = Volatile.Read(ref entries);
            for (int i = 0; i < snapshot.Length; i++)
            {
                try { if (snapshot[i].Dirty()) return true; }
                catch (Exception error)
                {
                    // Treat an unreadable store as unsaved; never silently mark it clean.
                    Fail(snapshot[i], "read dirty state", error);
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Attempt every dirty store. A false result leaves the editor save unsuccessful.</summary>
    public static bool SaveDirty()
    {
        Entry[] snapshot = Volatile.Read(ref entries);
        bool allOk = true;
        for (int i = 0; i < snapshot.Length; i++)
        {
            Entry entry = snapshot[i];
            try
            {
                if (!entry.Dirty()) continue;
                if (!entry.Save() || entry.Dirty())
                {
                    allOk = false;
                    Fail(entry, "save", new InvalidOperationException(
                        "Provider returned failure or remained dirty after save."));
                }
            }
            catch (Exception error)
            {
                allOk = false;
                Fail(entry, "save", error);
            }
        }
        return allOk;
    }

    /// <summary>Capture all dirty optional stores without changing their dirty state.</summary>
    public static bool CaptureDirty(Action<string, string, string> persist)
    {
        if (persist == null) throw new ArgumentNullException(nameof(persist));
        Entry[] snapshot = Volatile.Read(ref entries);
        bool allOk = true;
        for (int i = 0; i < snapshot.Length; i++)
        {
            Entry entry = snapshot[i];
            try
            {
                if (!entry.Dirty()) continue;
                if (entry.Capture == null || !entry.Capture(out string path, out string text) ||
                    string.IsNullOrWhiteSpace(path))
                {
                    allOk = false;
                    Fail(entry, "capture", new InvalidOperationException("Dirty author data has no recovery source."));
                    continue;
                }
                persist(entry.Id, path, text ?? string.Empty);
            }
            catch (Exception error)
            {
                allOk = false;
                Fail(entry, "capture", error);
            }
        }
        return allOk;
    }

    private static void Fail(Entry source, string step, Exception error)
    {
        string message = source.Id + " " + step + " failed: " + error;
        Volatile.Write(ref lastError, message);
        try { source.Report?.Invoke(step, error); }
        catch { /* A third-party reporter must not abort remaining stores. */ }
    }
}
