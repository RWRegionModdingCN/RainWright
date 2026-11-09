using System;
using DryCycle.DevUI.DevTool.World;

namespace DryCycle.DevUI.DevTool.Map;

public sealed class EditorMapRoomNodeSnapshot
{
    public int NodeIndex { get; init; }
    public string Type { get; init; } = string.Empty;
    public bool Exit { get; init; }
    public int ConnectedRoomIndex { get; init; } = -1;
}

public sealed class EditorMapRoomSnapshot
{
    public int RoomIndex { get; init; }
    public string Name { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    /// <summary>Authoritative map-config index (0, 1 or 2), shared by all map views.</summary>
    public int Layer { get; init; }
    public string Subregion { get; init; } = string.Empty;
    public bool OffScreenDen { get; init; }
    public bool Disabled { get; init; }
    public bool CurrentRoom { get; init; }
    public bool Selected { get; init; }
    public EditorMapRoomNodeSnapshot[] Nodes { get; init; } = Array.Empty<EditorMapRoomNodeSnapshot>();
}

public sealed class EditorMapConnectionSnapshot
{
    public string ConnectionId { get; init; } = string.Empty;
    public int FromRoomIndex { get; init; }
    public int FromNodeIndex { get; init; } = -1;
    public int ToRoomIndex { get; init; }
    public int ToNodeIndex { get; init; } = -1;
    public WorldConnectionDirection Direction { get; init; } = WorldConnectionDirection.Bidirectional;
    public bool Explicit { get; init; }
    public bool Ambiguous { get; init; }
}

public sealed class EditorMapPresentationSnapshot
{
    public static readonly EditorMapPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string RegionName { get; init; } = string.Empty;
    public int SelectedRoomIndex { get; init; } = -1;
    public EditorMapRoomSnapshot[] Rooms { get; init; } = Array.Empty<EditorMapRoomSnapshot>();
    public EditorMapConnectionSnapshot[] Connections { get; init; } = Array.Empty<EditorMapConnectionSnapshot>();
}



/// <summary>Published authoring view identity and lifecycle owned by Rainwright.</summary>
public static class EditorMapPresentationState
{
    private static EditorMapPresentationSnapshot current = EditorMapPresentationSnapshot.Empty;
    public static EditorMapPresentationSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorMapPresentationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorMapPresentationSnapshot.Empty);
    internal static void Clear() => Publish(EditorMapPresentationSnapshot.Empty);
}
