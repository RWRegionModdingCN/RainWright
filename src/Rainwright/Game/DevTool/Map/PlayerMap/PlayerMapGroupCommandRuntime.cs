using System;
using BepInEx;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

public readonly struct PlayerMapGroupMoveCommand
{
    public PlayerMapGroupMoveCommand(int[] roomIndices, Vector2[] effectivePositions, string label)
    {
        RoomIndices = roomIndices == null ? Array.Empty<int>() : (int[])roomIndices.Clone();
        EffectivePositions = effectivePositions == null ? Array.Empty<Vector2>() : (Vector2[])effectivePositions.Clone();
        Label = string.IsNullOrWhiteSpace(label) ? "Move player-map rooms" : label;
    }

    public int[] RoomIndices { get; }
    public Vector2[] EffectivePositions { get; }
    public string Label { get; }
}

/// <summary>
/// Unity-side group editor compatibility layer. Rainwright owns the detached
/// transport, while snapping transforms and the history executor stay here.
/// </summary>
public static class PlayerMapGroupCommandQueue
{
    private static Func<PlayerMapGroupMoveCommand, PlayerMapGroupMoveCommand> moveTransformer;

    public static void Enqueue(PlayerMapGroupMoveCommand command)
    {
        if (!Valid(command)) return;
        if (moveTransformer != null)
            command = moveTransformer(command);
        if (Valid(command)) QueueMove(command);
    }

    /// <summary>Independent layout tools bypass interactive drag snapping.</summary>
    public static void EnqueueExact(PlayerMapGroupMoveCommand command)
    {
        if (Valid(command)) QueueMove(command);
    }

    private static void QueueMove(PlayerMapGroupMoveCommand command)
    {
        int n = command.RoomIndices.Length;
        float[] xs = new float[n];
        float[] ys = new float[n];
        for (int i = 0; i < n; i++)
        {
            xs[i] = command.EffectivePositions[i].x;
            ys[i] = command.EffectivePositions[i].y;
        }
        PlayerMapGroupCommandTransport.TryEnqueue(new PlayerMapGroupMoveEnvelope(
            command.RoomIndices, xs, ys, command.Label));
    }

    public static void Enqueue(PlayerMapGroupLayerCommand command)
    {
        if (command.RoomIndices == null || command.RoomIndices.Length == 0) return;
        PlayerMapGroupCommandTransport.TryEnqueue(new PlayerMapGroupLayerEnvelope(
            command.RoomIndices, command.Layer, command.Label));
    }

    public static void Enqueue(PlayerMapGroupPlacementCommand command)
    {
        if (command.RoomIndices == null || command.RoomIndices.Length == 0) return;
        PlayerMapGroupCommandTransport.TryEnqueue(new PlayerMapGroupPlacementEnvelope(
            command.RoomIndices, command.Operation, command.Label));
    }

    internal static bool TryDequeue(out PlayerMapGroupMoveCommand command)
    {
        if (!PlayerMapGroupCommandTransport.TryDequeue(out PlayerMapGroupMoveEnvelope item))
        {
            command = default;
            return false;
        }
        Vector2[] positions = new Vector2[item.Xs.Length];
        for (int i = 0; i < positions.Length; i++)
            positions[i] = new Vector2(item.Xs[i], item.Ys[i]);
        command = new PlayerMapGroupMoveCommand(item.RoomIndices, positions, item.Label);
        return true;
    }

    internal static bool TryDequeue(out PlayerMapGroupLayerCommand command)
    {
        if (!PlayerMapGroupCommandTransport.TryDequeue(out PlayerMapGroupLayerEnvelope item))
        {
            command = default;
            return false;
        }
        command = new PlayerMapGroupLayerCommand(item.RoomIndices, item.Layer, item.Label);
        return true;
    }

    internal static bool TryDequeue(out PlayerMapGroupPlacementCommand command)
    {
        if (!PlayerMapGroupCommandTransport.TryDequeue(out PlayerMapGroupPlacementEnvelope item))
        {
            command = default;
            return false;
        }
        command = new PlayerMapGroupPlacementCommand(item.RoomIndices, item.Operation, item.Label);
        return true;
    }

    internal static void RegisterMoveTransformer(
        Func<PlayerMapGroupMoveCommand, PlayerMapGroupMoveCommand> transformer) =>
        moveTransformer = transformer;

    internal static void UnregisterMoveTransformer(
        Func<PlayerMapGroupMoveCommand, PlayerMapGroupMoveCommand> transformer)
    {
        if (Delegate.Equals(moveTransformer, transformer))
            moveTransformer = null;
    }

    internal static void Clear() => PlayerMapGroupCommandTransport.Clear();

    private static bool Valid(PlayerMapGroupMoveCommand command) =>
        command.RoomIndices != null && command.EffectivePositions != null &&
        command.RoomIndices.Length > 0 && command.RoomIndices.Length == command.EffectivePositions.Length;
}

/// <summary>
/// Executes Player Map group mutations on the same main-thread command boundary as existing map
/// edits. Child commands retain their proven per-room undo semantics; EditorHistoryService.BeginBatch
/// folds an entire group move/layer/placement change into one atomic user-visible history entry.
/// </summary>


internal static class PlayerMapGroupCommandRuntime
{
    private static ManualLogSource log;
    private static bool enabled;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map grouped placement/layer command runtime enabled through direct queue processing; no self-detour attached.");
    }

    internal static void Disable()
    {
        PlayerMapGroupCommandQueue.Clear();
        enabled = false;
        log = null;
    }

    internal static bool Process(EditorSession session)
    {
        if (!enabled || session == null)
        {
            if (session == null) PlayerMapGroupCommandQueue.Clear();
            return false;
        }

        bool processed = false;
        for (int handled = 0;
             handled < PlayerMapGroupCommandTransport.MaxCommandsPerFrame &&
             PlayerMapGroupCommandQueue.TryDequeue(out PlayerMapGroupMoveCommand group);
             handled++)
        {
            int count = Math.Min(group.RoomIndices?.Length ?? 0, group.EffectivePositions?.Length ?? 0);
            if (count <= 0) continue;

            using (session.History?.BeginBatch(group.Label))
            {
                for (int i = 0; i < count; i++)
                {
                    PlayerMapWorkspaceRuntime.Execute(session, new PlayerMapCommand(
                        PlayerMapCommandKind.SetEffectivePosition,
                        roomIndex: group.RoomIndices[i],
                        value: group.EffectivePositions[i]));
                }
            }
            processed = true;
        }

        for (int handled = 0;
             handled < PlayerMapGroupCommandTransport.MaxCommandsPerFrame &&
             PlayerMapGroupCommandQueue.TryDequeue(out PlayerMapGroupLayerCommand group);
             handled++)
        {
            int count = group.RoomIndices?.Length ?? 0;
            if (count <= 0) continue;

            using (session.History?.BeginBatch(group.Label))
            {
                for (int i = 0; i < count; i++)
                {
                    PlayerMapWorkspaceRuntime.Execute(session, new PlayerMapCommand(
                        PlayerMapCommandKind.SetLayer,
                        roomIndex: group.RoomIndices[i],
                        integer: group.Layer));
                }
            }
            processed = true;
        }

        for (int handled = 0;
             handled < PlayerMapGroupCommandTransport.MaxCommandsPerFrame &&
             PlayerMapGroupCommandQueue.TryDequeue(out PlayerMapGroupPlacementCommand group);
             handled++)
        {
            int count = group.RoomIndices?.Length ?? 0;
            if (count <= 0) continue;

            using (session.History?.BeginBatch(group.Label))
            {
                for (int i = 0; i < count; i++)
                {
                    int roomIndex = group.RoomIndices[i];
                    switch (group.Operation)
                    {
                        case PlayerMapGroupPlacementOperation.SetDerived:
                            PlayerMapWorkspaceRuntime.Execute(session, new PlayerMapCommand(
                                PlayerMapCommandKind.SetPlacementMode,
                                roomIndex: roomIndex,
                                integer: (int)PlayerMapPlacementMode.Derived));
                            break;
                        case PlayerMapGroupPlacementOperation.SetAbsolute:
                            PlayerMapWorkspaceRuntime.Execute(session, new PlayerMapCommand(
                                PlayerMapCommandKind.SetPlacementMode,
                                roomIndex: roomIndex,
                                integer: (int)PlayerMapPlacementMode.Absolute));
                            break;
                        case PlayerMapGroupPlacementOperation.ResetOffset:
                            PlayerMapWorkspaceRuntime.Execute(session, new PlayerMapCommand(
                                PlayerMapCommandKind.ResetOffset,
                                roomIndex: roomIndex));
                            break;
                    }
                }
            }
            processed = true;
        }

        int rejected = PlayerMapGroupCommandTransport.ConsumeRejectedCount();
        if (rejected > 0)
            EditorDiagnostics.Warning(
                "Player Map group command inbox rejected " + rejected +
                " authoring action(s); pending=" + PlayerMapGroupCommandTransport.PendingCount);
        return processed;
    }
}
