using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Shared, bounded render-to-game command transport. Producers never retain
/// a game object; the game adapter drains at most one frame budget at a time.
/// </summary>
internal sealed class EditorCommandInbox<T>
{
    private readonly ConcurrentQueue<T> queue = new();
    private readonly int capacity;
    private int pending;
    private int rejected;

    internal EditorCommandInbox(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    internal int PendingCount => Volatile.Read(ref pending);
    internal int ConsumeRejectedCount() => Interlocked.Exchange(ref rejected, 0);

    internal bool TryEnqueue(T command)
    {
        if (Interlocked.Increment(ref pending) > capacity)
        {
            Interlocked.Decrement(ref pending);
            Interlocked.Increment(ref rejected);
            return false;
        }

        try
        {
            queue.Enqueue(command);
            return true;
        }
        catch
        {
            Interlocked.Decrement(ref pending);
            throw;
        }
    }

    internal bool TryDequeue(out T command)
    {
        if (!queue.TryDequeue(out command)) return false;
        Interlocked.Decrement(ref pending);
        return true;
    }

    internal void Clear()
    {
        while (TryDequeue(out _)) { }
        Interlocked.Exchange(ref rejected, 0);
    }
}
