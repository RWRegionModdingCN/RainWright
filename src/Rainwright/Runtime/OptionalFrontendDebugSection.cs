using System;
using System.Threading;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// A single optional diagnostics page registration. Editor UI owns the slot;
/// game-specific extensions supply only drawing and stop callbacks.
/// The registration retains its identity until Stop succeeds. An unrelated
/// or stale owner cannot release a newer page.
/// </summary>
internal sealed class OptionalFrontendDebugSection<TSnapshot>
{
    private sealed class Lease : IDisposable
    {
        internal readonly OptionalFrontendDebugSection<TSnapshot> Parent;
        internal readonly string Chinese, English;
        internal readonly Action<TSnapshot> Draw;
        internal readonly Action Stop;
        internal Lease(OptionalFrontendDebugSection<TSnapshot> parent, string chinese,
            string english, Action<TSnapshot> draw, Action stop)
        {
            Parent = parent;
            Chinese = chinese;
            English = english;
            Draw = draw;
            Stop = stop;
        }
        public void Dispose() => Parent.Release(this);
    }

    private Lease active;

    internal bool Available => Volatile.Read(ref active) != null;
    internal string ChineseTitle => Volatile.Read(ref active)?.Chinese ?? string.Empty;
    internal string EnglishTitle => Volatile.Read(ref active)?.English ?? string.Empty;

    internal IDisposable Register(string chinese, string english,
        Action<TSnapshot> draw, Action stop)
    {
        if (string.IsNullOrWhiteSpace(chinese))
            throw new ArgumentException("An extension diagnostics page needs a Chinese label.", nameof(chinese));
        if (string.IsNullOrWhiteSpace(english))
            throw new ArgumentException("An extension diagnostics page needs an English label.", nameof(english));
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        if (stop == null) throw new ArgumentNullException(nameof(stop));

        Lease next = new(this, chinese, english, draw, stop);
        if (Interlocked.CompareExchange(ref active, next, null) != null)
            throw new InvalidOperationException("An optional diagnostics page is already registered.");
        return next;
    }

    internal bool TryDraw(TSnapshot snapshot)
    {
        Lease current = Volatile.Read(ref active);
        if (current == null) return false;
        current.Draw(snapshot);
        return true;
    }

    internal void Stop()
    {
        Volatile.Read(ref active)?.Stop();
    }

    private void Release(Lease owner)
    {
        if (!ReferenceEquals(Volatile.Read(ref active), owner))
            return;

        // Stop may close a file or return a game subscription. If it fails,
        // the same owner must remain registered so the adapter can retry.
        owner.Stop();
        Interlocked.CompareExchange(ref active, null, owner);
    }
}
