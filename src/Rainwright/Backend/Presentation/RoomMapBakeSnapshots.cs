using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Source-room geometry bake results belong to Rainwright's presentation contract.
/// No Unity, game runtime, BepInEx or optional frontend types are required.
/// </summary>
public enum RoomMapBakeStatus
{
    Missing = 0,
    Pending = 1,
    Ready = 2,
    Failed = 3
}

public enum RoomMapPixelKind : byte
{
    Air = 0,
    BackWall = 1,
    Solid = 2,
    Structure = 3,
    RoomExit = 4,
    CreatureHole = 5,
    NormalShortcut = 6,
    NpcTransport = 7,
    RegionTransport = 8,
    UnknownShortcut = 9
}

/// <summary>
/// Exact authored node anchor recovered directly from the room text shortcut graph.
/// NodeIndex follows Rain World's ShortcutMapper ordering (RoomExit, CreatureHole,
/// RegionTransport; each scanned top-to-bottom and left-to-right). EntranceX/Y is the visible
/// shortcut mouth and is therefore the coordinate used by World/Player Map pipe rendering.
/// </summary>
public readonly struct RoomMapNodeAnchorSnapshot
{
    public RoomMapNodeAnchorSnapshot(
        int nodeIndex,
        float entranceX,
        float entranceY,
        int terminalX,
        int terminalY,
        RoomMapPixelKind kind)
    {
        NodeIndex = nodeIndex;
        EntranceX = entranceX;
        EntranceY = entranceY;
        TerminalX = terminalX;
        TerminalY = terminalY;
        Kind = kind;
    }

    public int NodeIndex { get; }
    public float EntranceX { get; }
    public float EntranceY { get; }
    public int TerminalX { get; }
    public int TerminalY { get; }
    public RoomMapPixelKind Kind { get; }
}

public readonly struct RoomMapPreviewRun
{
    public RoomMapPreviewRun(int x, int y, int length, RoomMapPixelKind kind, bool water)
    {
        X = x;
        Y = y;
        Length = length;
        Kind = kind;
        Water = water;
    }

    public int X { get; }
    public int Y { get; }
    public int Length { get; }
    public RoomMapPixelKind Kind { get; }
    public bool Water { get; }
}

public sealed class RoomMapBakeSnapshot
{
    public static readonly RoomMapBakeSnapshot Empty = new();

    public RoomMapBakeStatus Status { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public string Error { get; init; } = string.Empty;
    public RoomMapPreviewRun[] Runs { get; init; } = Array.Empty<RoomMapPreviewRun>();
    public RoomMapNodeAnchorSnapshot[] NodeAnchors { get; init; } = Array.Empty<RoomMapNodeAnchorSnapshot>();

    public bool TryGetNodeAnchor(int nodeIndex, out RoomMapNodeAnchorSnapshot anchor)
    {
        RoomMapNodeAnchorSnapshot[] anchors = NodeAnchors ?? Array.Empty<RoomMapNodeAnchorSnapshot>();
        for (int i = 0; i < anchors.Length; i++)
        {
            if (anchors[i].NodeIndex != nodeIndex) continue;
            anchor = anchors[i];
            return true;
        }
        anchor = default;
        return false;
    }
}
