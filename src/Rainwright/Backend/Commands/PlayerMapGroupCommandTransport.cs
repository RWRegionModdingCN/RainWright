using System;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

public enum PlayerMapGroupPlacementOperation
{
    SetDerived = 0,
    SetAbsolute = 1,
    ResetOffset = 2
}

/// <summary>Coordinates and room identities for a grouped drag, detached from Unity.</summary>
internal readonly struct PlayerMapGroupMoveEnvelope
{
    internal PlayerMapGroupMoveEnvelope(int[] roomIndices, float[] xs, float[] ys, string label)
    {
        if (roomIndices == null || xs == null || ys == null ||
            roomIndices.Length == 0 || roomIndices.Length != xs.Length || xs.Length != ys.Length)
            throw new ArgumentException("Grouped positions must match room indices.");
        RoomIndices = (int[])roomIndices.Clone();
        Xs = (float[])xs.Clone();
        Ys = (float[])ys.Clone();
        Label = label ?? string.Empty;
    }

    internal int[] RoomIndices { get; }
    internal float[] Xs { get; }
    internal float[] Ys { get; }
    internal string Label { get; }
}

internal readonly struct PlayerMapGroupLayerEnvelope
{
    internal PlayerMapGroupLayerEnvelope(int[] roomIndices, int layer, string label)
    {
        RoomIndices = roomIndices == null ? Array.Empty<int>() : (int[])roomIndices.Clone();
        Layer = layer;
        Label = label ?? string.Empty;
    }

    internal int[] RoomIndices { get; }
    internal int Layer { get; }
    internal string Label { get; }
}

internal readonly struct PlayerMapGroupPlacementEnvelope
{
    internal PlayerMapGroupPlacementEnvelope(
        int[] roomIndices, PlayerMapGroupPlacementOperation operation, string label)
    {
        RoomIndices = roomIndices == null ? Array.Empty<int>() : (int[])roomIndices.Clone();
        Operation = operation;
        Label = label ?? string.Empty;
    }

    internal int[] RoomIndices { get; }
    internal PlayerMapGroupPlacementOperation Operation { get; }
    internal string Label { get; }
}

/// <summary>
/// Three bounded lanes retain the legacy move -> layer -> placement drain order.
/// Individual groups remain indivisible to preserve a single undo/redo batch.
/// </summary>
internal static class PlayerMapGroupCommandTransport
{
    internal const int MaxPendingCommands = 256;
    internal const int MaxCommandsPerFrame = 16;

    private static readonly EditorCommandInbox<PlayerMapGroupMoveEnvelope> moves =
        new(MaxPendingCommands);
    private static readonly EditorCommandInbox<PlayerMapGroupLayerEnvelope> layers =
        new(MaxPendingCommands);
    private static readonly EditorCommandInbox<PlayerMapGroupPlacementEnvelope> placements =
        new(MaxPendingCommands);

    internal static bool TryEnqueue(PlayerMapGroupMoveEnvelope command) => moves.TryEnqueue(command);
    internal static bool TryEnqueue(PlayerMapGroupLayerEnvelope command) => layers.TryEnqueue(command);
    internal static bool TryEnqueue(PlayerMapGroupPlacementEnvelope command) => placements.TryEnqueue(command);
    internal static bool TryDequeue(out PlayerMapGroupMoveEnvelope command) => moves.TryDequeue(out command);
    internal static bool TryDequeue(out PlayerMapGroupLayerEnvelope command) => layers.TryDequeue(out command);
    internal static bool TryDequeue(out PlayerMapGroupPlacementEnvelope command) => placements.TryDequeue(out command);

    internal static int PendingCount => moves.PendingCount + layers.PendingCount + placements.PendingCount;
    internal static int ConsumeRejectedCount() =>
        moves.ConsumeRejectedCount() + layers.ConsumeRejectedCount() + placements.ConsumeRejectedCount();

    internal static void Clear()
    {
        moves.Clear();
        layers.Clear();
        placements.Clear();
    }
}
