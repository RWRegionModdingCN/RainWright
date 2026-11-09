using System;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Map;

public enum MapEditorCommandKind
{
    SelectRoom,
    SwitchCurrentRoom,
    SetRoomPosition,
    SetRoomLayer,
    SetRoomSubregion,
    SetRoomAttraction
}

public readonly struct MapEditorCommand
{
    public MapEditorCommand(
        MapEditorCommandKind kind,
        int roomIndex = -1,
        string text = null,
        EditorPropertyValue value = default,
        string key = null)
    {
        Kind = kind;
        RoomIndex = roomIndex;
        Text = text;
        Value = value;
        Key = key;
    }

    public MapEditorCommandKind Kind { get; }
    public int RoomIndex { get; }
    public string Text { get; }
    public EditorPropertyValue Value { get; }
    public string Key { get; }
}

public static class MapEditorCommandQueue
{
    internal const int MaxCommandsPerFrame = 64;
    private static readonly EditorCommandInbox<MapEditorCommand> inbox = new(2048);

    public static void Enqueue(MapEditorCommand command) => inbox.TryEnqueue(command);
    public static bool TryEnqueue(MapEditorCommand command) => inbox.TryEnqueue(command);
    internal static bool TryDequeue(out MapEditorCommand command) => inbox.TryDequeue(out command);
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static int PendingCount => inbox.PendingCount;
    internal static void Clear() => inbox.Clear();
}
