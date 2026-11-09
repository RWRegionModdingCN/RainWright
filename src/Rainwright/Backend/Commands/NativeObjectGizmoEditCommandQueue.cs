using System;
using System.Collections.Concurrent;

namespace DryCycle.DevUI.DevTool.Gizmos;

public enum NativeObjectGizmoEditKind
{
    Begin,
    Update,
    Commit,
    Cancel,
    InsertCurvePoint,
    RemoveHandle
}

public readonly struct NativeObjectGizmoEditCommand
{
    public NativeObjectGizmoEditCommand(
        NativeObjectGizmoEditKind kind,
        int objectIndex,
        string handleId = null,
        float x = 0f,
        float y = 0f,
        int segmentIndex = -1,
        float curveT = 0f,
        bool snap = false,
        long stableId = 0L)
    {
        Kind = kind;
        ObjectIndex = objectIndex;
        HandleId = handleId ?? string.Empty;
        X = x;
        Y = y;
        SegmentIndex = segmentIndex;
        CurveT = curveT;
        Snap = snap;
        StableId = stableId;
    }

    public NativeObjectGizmoEditKind Kind { get; }
    public int ObjectIndex { get; }
    public string HandleId { get; }
    public float X { get; }
    public float Y { get; }
    public int SegmentIndex { get; }
    public float CurveT { get; }
    public bool Snap { get; }
    public long StableId { get; }
}

/// <summary>
/// Unified command boundary for selected-object scene gizmos. Frontend commands contain only detached
/// IDs/world coordinates. All Rain World model semantics, snapping and history live in the backend.
/// </summary>

/// <summary>Rainwright-owned transaction transport. Preserve drag event order: Begin/Update/Commit/Cancel.</summary>
public static class NativeObjectGizmoEditCommandQueue
{
    private static readonly ConcurrentQueue<NativeObjectGizmoEditCommand> queue = new();
    public static void Enqueue(NativeObjectGizmoEditCommand command) => queue.Enqueue(command);
    internal static bool TryDequeue(out NativeObjectGizmoEditCommand command) => queue.TryDequeue(out command);
    internal static void Clear()
    {
        while (queue.TryDequeue(out _)) { }
    }
}
