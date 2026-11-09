using System;
using System.Threading;
namespace DryCycle.DevUI.DevTool.World;

/// <summary>Unique gameplay-owned topology author target. Absent owner means no writer.</summary>
internal static class WorldTopologyPathOwnership
{
    private sealed class Lease : IDisposable
    {
        private Func<bool, string> source;
        internal Lease(Func<bool, string> source) => this.source = source;
        public void Dispose()
        {
            var previous = Interlocked.Exchange(ref source, null);
            if (previous == null) return;
            lock (Gate)
                if (ReferenceEquals(current, previous))
                {
                    Volatile.Write(ref current, null);
                    Interlocked.Increment(ref revision);
                }
        }
    }
    private static readonly object Gate = new();
    private static Func<bool, string> current;
    private static int revision = 1;
    internal static int Revision => Volatile.Read(ref revision);
    internal static bool HasOwner => Volatile.Read(ref current) != null;
    internal static IDisposable Register(Func<bool, string> resolve)
    {
        if (resolve == null) throw new ArgumentNullException(nameof(resolve));
        lock (Gate)
        {
            if (current != null)
                throw new InvalidOperationException("WorldTopology already has a gameplay owner.");
            Volatile.Write(ref current, resolve);
            Interlocked.Increment(ref revision);
        }
        return new Lease(resolve);
    }
    internal static string Resolve(bool forSave)
    {
        var resolve = Volatile.Read(ref current);
        if (resolve == null) return null;
        try { return resolve(forSave); }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "WorldTopology gameplay author source failed; no unverified path will be used.", error);
            return null;
        }
    }
}
