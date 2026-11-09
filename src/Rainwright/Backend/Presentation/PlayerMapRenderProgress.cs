using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

public enum PlayerMapRenderStage
{
    Idle = 0,
    PreflightRooms = 1,
    PreflightConnections = 2,
    PrepareOutput = 3,
    ClearOutput = 4,
    ComposeRooms = 5,
    BuildPreview = 6,
    PrepareExport = 7,
    EncodePng = 8,
    WriteMetadata = 9,
    CommitFiles = 10,
    Completed = 11,
    Cancelled = 12,
    Failed = 13
}

public sealed class PlayerMapRenderProgressSnapshot
{
    public static readonly PlayerMapRenderProgressSnapshot Idle = new();

    public bool Running { get; init; }
    public bool ExportRequested { get; init; }
    public bool CanCancel { get; init; }
    public PlayerMapRenderStage Stage { get; init; }
    public float Progress { get; init; }
    public float StageProgress { get; init; }
    public long CompletedUnits { get; init; }
    public long TotalUnits { get; init; }
    public string StageLabel { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

/// <summary>Rainwright-owned atomic Render Map progress view for native frontends.</summary>
public static class PlayerMapRenderProgressState
{
    private static PlayerMapRenderProgressSnapshot current = PlayerMapRenderProgressSnapshot.Idle;

    public static PlayerMapRenderProgressSnapshot Current =>
        System.Threading.Volatile.Read(ref current);

    internal static void Publish(PlayerMapRenderProgressSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? PlayerMapRenderProgressSnapshot.Idle);

    internal static void Clear() => Publish(PlayerMapRenderProgressSnapshot.Idle);
}
