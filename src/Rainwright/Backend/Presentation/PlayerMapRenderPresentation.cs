using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>Detached preparation view. The Unity rendering scheduler is only a producer.</summary>
public sealed class PlayerMapRenderPreparationSnapshot
{
    public static readonly PlayerMapRenderPreparationSnapshot Idle = new();

    public bool Running { get; init; }
    public bool ExportRequested { get; init; }
    public bool CanCancel { get; init; }
    public int ReadyRooms { get; init; }
    public int TotalRooms { get; init; }
    public float Progress { get; init; }
    public string Detail { get; init; } = string.Empty;
}

/// <summary>Detached Render Map preflight result shared by game and independent frontends.</summary>
public sealed class PlayerMapPreflightSnapshot
{
    public static readonly PlayerMapPreflightSnapshot Empty = new();

    public bool Available { get; init; }
    public bool Computing { get; init; }
    public string RegionName { get; init; } = string.Empty;
    public int EnabledRooms { get; init; }
    public int ReadyRooms { get; init; }
    public int PendingRooms { get; init; }
    public int MissingRooms { get; init; }
    public int FailedRooms { get; init; }
    public int ExactConnections { get; init; }
    public int AmbiguousConnections { get; init; }
    public int InvalidEndpoints { get; init; }
    public int DuplicateEndpointClaims { get; init; }
    public int OverlapPairs { get; init; }
    public int[] OverlapRoomIndices { get; init; } = Array.Empty<int>();
    public int Width { get; init; }
    public int LayerHeight { get; init; }
    public int Height { get; init; }
    public bool OutputSafe { get; init; }
    public bool CanRender { get; init; }
    public string[] Errors { get; init; } = Array.Empty<string>();
    public string[] Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The one presentation mailbox for progressive Render Map preparation.
/// Clear and publication are explicit at editor lifecycle boundaries.
/// </summary>
public static class PlayerMapRenderPreparationState
{
    private static PlayerMapRenderPreparationSnapshot current = PlayerMapRenderPreparationSnapshot.Idle;

    public static PlayerMapRenderPreparationSnapshot Current =>
        System.Threading.Volatile.Read(ref current);

    internal static void Publish(PlayerMapRenderPreparationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? PlayerMapRenderPreparationSnapshot.Idle);

    internal static void Clear() => Publish(PlayerMapRenderPreparationSnapshot.Idle);
}
