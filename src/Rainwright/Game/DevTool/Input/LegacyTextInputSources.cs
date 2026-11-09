using System;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Input;

/// <summary>
/// Version-neutral, scoped input ownership bridge. Legacy DevUI controls live in their
/// owning mod; the editor only asks whether any registered source currently owns text.
/// No gameplay/UI types and no dynamic lookup or allocation in the per-frame read path.
/// </summary>
public static class LegacyTextInputSources
{
    private sealed class Source
    {
        internal readonly Func<bool> IsFocused;
        internal readonly Action<Exception> ReportFailure;
        internal readonly Func<object> FocusedNode;

        internal Source(Func<bool> isFocused, Action<Exception> reportFailure, Func<object> focusedNode)
        {
            IsFocused = isFocused;
            ReportFailure = reportFailure;
            FocusedNode = focusedNode;
        }
    }

    private sealed class Lease : IDisposable
    {
        private Source source;
        internal Lease(Source source) => this.source = source;

        public void Dispose()
        {
            Source previous = Interlocked.Exchange(ref source, null);
            if (previous == null) return;
            lock (Sync)
            {
                Source[] old = sources;
                int index = Array.IndexOf(old, previous);
                if (index < 0) return;
                Source[] next = new Source[old.Length - 1];
                if (index > 0) Array.Copy(old, 0, next, 0, index);
                if (index + 1 < old.Length)
                    Array.Copy(old, index + 1, next, index, old.Length - index - 1);
                Volatile.Write(ref sources, next);
            }
        }
    }

    private static readonly object Sync = new();
    private static Source[] sources = Array.Empty<Source>();

    public static IDisposable Register(Func<bool> isFocused, Action<Exception> reportFailure = null,
        Func<object> focusedNode = null)
    {
        if (isFocused == null) throw new ArgumentNullException(nameof(isFocused));
        Source source = new(isFocused, reportFailure, focusedNode);
        lock (Sync)
        {
            Source[] old = sources;
            Source[] next = new Source[old.Length + 1];
            Array.Copy(old, next, old.Length);
            next[old.Length] = source;
            Volatile.Write(ref sources, next);
        }
        return new Lease(source);
    }

    public static bool HasTextFocus
    {
        get
        {
            Source[] snapshot = Volatile.Read(ref sources);
            for (int i = 0; i < snapshot.Length; i++)
            {
                Source source = snapshot[i];
                try
                {
                    if (source.IsFocused()) return true;
                }
                catch (Exception error)
                {
                    // A broken external input adapter must never crash RawUpdate.
                    // Retire it once so the same callback cannot throw every frame.
                    new Lease(source).Dispose();
                    try { source.ReportFailure?.Invoke(error); }
                    catch { /* Diagnostic callback is also untrusted. */ }
                }
            }
            return false;
        }
    }

    internal static object FocusedNode
    {
        get
        {
            foreach (Source source in Volatile.Read(ref sources))
            {
                if (source.FocusedNode == null) continue;
                try
                {
                    var node = source.FocusedNode();
                    if (node != null) return node;
                }
                catch (Exception error)
                {
                    new Lease(source).Dispose();
                    try { source.ReportFailure?.Invoke(error); } catch { }
                }
            }
            return null;
        }
    }
}
