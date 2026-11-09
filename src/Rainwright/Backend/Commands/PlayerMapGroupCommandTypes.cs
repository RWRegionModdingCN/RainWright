using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>Canonical three-layer map config coordinates, free of Unity/game assembly types.</summary>
public static class PlayerMapLayerPolicy
{
    public const int Count = 3;
}

public readonly struct PlayerMapGroupLayerCommand
{
    public PlayerMapGroupLayerCommand(int[] roomIndices, int layer, string label)
    {
        RoomIndices = roomIndices == null ? Array.Empty<int>() : (int[])roomIndices.Clone();
        Layer = Math.Max(0, Math.Min(PlayerMapLayerPolicy.Count - 1, layer));
        Label = string.IsNullOrWhiteSpace(label) ? "Change player-map room layers" : label;
    }

    public int[] RoomIndices { get; }
    public int Layer { get; }
    public string Label { get; }
}

public readonly struct PlayerMapGroupPlacementCommand
{
    public PlayerMapGroupPlacementCommand(int[] roomIndices, PlayerMapGroupPlacementOperation operation, string label)
    {
        RoomIndices = roomIndices == null ? Array.Empty<int>() : (int[])roomIndices.Clone();
        Operation = operation;
        Label = string.IsNullOrWhiteSpace(label) ? "Change player-map room placement" : label;
    }

    public int[] RoomIndices { get; }
    public PlayerMapGroupPlacementOperation Operation { get; }
    public string Label { get; }
}
