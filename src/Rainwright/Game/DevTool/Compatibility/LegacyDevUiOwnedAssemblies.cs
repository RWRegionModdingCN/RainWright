using System;
using System.Reflection;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Compatibility;

/// <summary>
/// Explicit owners of legacy DevUI controls that are already represented by
/// rebuilt authoring surfaces. Foreign assemblies stay opaque and retain their
/// full vanilla update lifecycle. Registration is only done during bootstrap;
/// checks in the recursive DevUI tree are lock-free and allocation-free.
/// </summary>
internal static class LegacyDevUiOwnedAssemblies
{
    private sealed class Entry
    {
        internal readonly Assembly Owner;
        internal Entry(Assembly owner) => Owner = owner;
    }

    private sealed class Lease : IDisposable
    {
        private Entry entry;
        internal Lease(Entry value) => entry = value;

        public void Dispose()
        {
            Entry old = Interlocked.Exchange(ref entry, null);
            if (old == null) return;
            lock (Sync)
            {
                Entry[] previous = entries;
                int index = Array.IndexOf(previous, old);
                if (index < 0) return;
                Entry[] next = new Entry[previous.Length - 1];
                if (index > 0) Array.Copy(previous, 0, next, 0, index);
                if (index + 1 < previous.Length)
                    Array.Copy(previous, index + 1, next, index, previous.Length - index - 1);
                Volatile.Write(ref entries, next);
            }
        }
    }

    private static readonly object Sync = new();
    private static Entry[] entries = Array.Empty<Entry>();

    internal static IDisposable Register(Assembly assembly)
    {
        if (assembly == null) throw new ArgumentNullException(nameof(assembly));
        lock (Sync)
        {
            Entry[] previous = entries;
            for (int i = 0; i < previous.Length; i++)
                if (ReferenceEquals(previous[i].Owner, assembly))
                    throw new InvalidOperationException(
                        "Legacy DevUI assembly is already registered: " + assembly.FullName);
            Entry entry = new(assembly);
            Entry[] next = new Entry[previous.Length + 1];
            Array.Copy(previous, next, previous.Length);
            next[previous.Length] = entry;
            Volatile.Write(ref entries, next);
            return new Lease(entry);
        }
    }

    internal static bool Contains(Assembly assembly)
    {
        if (assembly == null) return false;
        Entry[] snapshot = Volatile.Read(ref entries);
        for (int i = 0; i < snapshot.Length; i++)
            if (ReferenceEquals(snapshot[i].Owner, assembly))
                return true;
        return false;
    }
}
