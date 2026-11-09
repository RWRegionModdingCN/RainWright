using System;
using System.Collections.Concurrent;

namespace DryCycle.DevUI.DevTool.Gizmos;

public enum NativeGizmoTargetKind
{
    ObjectPosition = 0,
    SoundPosition = 1,
    SoundRadius = 2,
    SoundDirection = 3,
    TriggerPosition = 4,
    TriggerRadius = 5
}

public enum NativeGizmoCommandKind
{
    Select = 0,
    Begin = 1,
    Update = 2,
    Commit = 3,
    Cancel = 4
}

/// <summary>
/// Detached command emitted by scene-space frontends. Coordinates are Rain World world-space values,
/// never ImGui screen coordinates. Begin/Update/Commit form one history transaction regardless of
/// how many preview updates occur during the drag.
/// </summary>
public readonly struct NativeGizmoCommand
{
    public NativeGizmoCommand(
        NativeGizmoCommandKind kind,
        NativeGizmoTargetKind target,
        int index,
        float x = 0f,
        float y = 0f,
        long stableId = 0L)
    {
        Kind = kind;
        Target = target;
        Index = index;
        X = x;
        Y = y;
        StableId = stableId;
    }

    public NativeGizmoCommandKind Kind { get; }
    public NativeGizmoTargetKind Target { get; }
    public int Index { get; }
    public float X { get; }
    public float Y { get; }
    public long StableId { get; }
}

/// <summary>Rainwright-owned transaction transport. Preserve drag event order: Begin/Update/Commit/Cancel.</summary>
public static class NativeGizmoCommandQueue
{
    private static readonly ConcurrentQueue<NativeGizmoCommand> queue = new();
    public static void Enqueue(NativeGizmoCommand command) => queue.Enqueue(command);
    internal static bool TryDequeue(out NativeGizmoCommand command) => queue.TryDequeue(out command);
    internal static void Clear()
    {
        while (queue.TryDequeue(out _)) { }
    }
}
