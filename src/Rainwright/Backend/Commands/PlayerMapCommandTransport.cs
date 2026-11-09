using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

public enum PlayerMapCommandKind
{
    SetEffectivePosition,
    SetOffset,
    SetPlacementMode,
    ResetOffset,
    SetLayer,
    CreateDefaultMaterial,
    SetDefaultMaterialRect,
    SetDefaultMaterialAir,
    DeleteDefaultMaterial,
    BuildPreview,
    RenderAndExport,
    SetRoomHidden
}

/// <summary>
/// Pure authoring command data. Coordinates are the same Rain World world-space
/// values as the Unity adapter uses, with no Unity/Game/BepInEx type identity.
/// </summary>
internal readonly struct PlayerMapCommandEnvelope
{
    internal PlayerMapCommandEnvelope(
        PlayerMapCommandKind kind, int roomIndex,
        float x, float y, float xb, float yb, int integer, bool flag)
    {
        Kind = kind;
        RoomIndex = roomIndex;
        X = x;
        Y = y;
        XB = xb;
        YB = yb;
        Integer = integer;
        Flag = flag;
    }

    internal PlayerMapCommandKind Kind { get; }
    internal int RoomIndex { get; }
    internal float X { get; }
    internal float Y { get; }
    internal float XB { get; }
    internal float YB { get; }
    internal int Integer { get; }
    internal bool Flag { get; }
}

/// <summary>
/// One process-wide Rainwright-owned input inbox. The game adapter only
/// serializes Unity Vector2 and executes commands within a bounded frame budget.
/// </summary>
internal static class PlayerMapCommandTransport
{
    internal const int MaxPendingCommands = 2048;
    internal const int MaxCommandsPerFrame = 64;

    private static readonly EditorCommandInbox<PlayerMapCommandEnvelope> inbox =
        new(MaxPendingCommands);

    internal static bool TryEnqueue(PlayerMapCommandEnvelope command) =>
        inbox.TryEnqueue(command);

    internal static bool TryDequeue(out PlayerMapCommandEnvelope command) =>
        inbox.TryDequeue(out command);

    internal static int PendingCount => inbox.PendingCount;

    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();

    internal static void Clear() => inbox.Clear();
}
