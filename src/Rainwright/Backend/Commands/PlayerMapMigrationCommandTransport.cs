using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

public enum PlayerMapMigrationCommandKind
{
    Create,
    SetStart,
    SetStartHandle,
    SetEnd,
    SetEndHandle,
    SetWidth,
    SetRate,
    SetLayerEnabled,
    SetNextStream,
    SetDestinationRoom
}

/// <summary>Detached migration-stream authoring command, containing no Unity objects.</summary>
internal readonly struct PlayerMapMigrationCommandEnvelope
{
    internal PlayerMapMigrationCommandEnvelope(
        PlayerMapMigrationCommandKind kind,
        string streamName, float x, float y, float number,
        int integer, bool flag, string text)
    {
        Kind = kind;
        StreamName = streamName ?? string.Empty;
        X = x;
        Y = y;
        Number = number;
        Integer = integer;
        Flag = flag;
        Text = text ?? string.Empty;
    }

    internal PlayerMapMigrationCommandKind Kind { get; }
    internal string StreamName { get; }
    internal float X { get; }
    internal float Y { get; }
    internal float Number { get; }
    internal int Integer { get; }
    internal bool Flag { get; }
    internal string Text { get; }
}

/// <summary>One bounded Rainwright-owned inbox for native Player Map migration editing.</summary>
internal static class PlayerMapMigrationCommandTransport
{
    internal const int MaxPendingCommands = 2048;
    internal const int MaxCommandsPerFrame = 64;
    private static readonly EditorCommandInbox<PlayerMapMigrationCommandEnvelope> inbox =
        new(MaxPendingCommands);

    internal static bool TryEnqueue(PlayerMapMigrationCommandEnvelope command) =>
        inbox.TryEnqueue(command);
    internal static bool TryDequeue(out PlayerMapMigrationCommandEnvelope command) =>
        inbox.TryDequeue(out command);
    internal static int PendingCount => inbox.PendingCount;
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static void Clear() => inbox.Clear();
}
