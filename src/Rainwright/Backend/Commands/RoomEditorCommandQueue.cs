using System;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Room;

public enum RoomEditorCommandKind
{
    SetSetting,
    ResetSetting,
    SetPaletteFade,
    SetTerrainPaletteFade,
    SetTemplate,
    SaveAsTemplate,
    AddEffect,
    DeleteEffect,
    SetEffectAmount
}

public readonly struct RoomEditorCommand
{
    public RoomEditorCommand(
        RoomEditorCommandKind kind,
        string key = null,
        int index = -1,
        int secondaryIndex = -1,
        EditorPropertyValue value = default)
    {
        Kind = kind;
        Key = key;
        Index = index;
        SecondaryIndex = secondaryIndex;
        Value = value;
    }

    public RoomEditorCommandKind Kind { get; }
    public string Key { get; }
    public int Index { get; }
    public int SecondaryIndex { get; }
    public EditorPropertyValue Value { get; }
}

public static class RoomEditorCommandQueue
{
    internal const int MaxCommandsPerFrame = 64;
    private static readonly EditorCommandInbox<RoomEditorCommand> inbox = new(2048);

    public static void Enqueue(RoomEditorCommand command) => inbox.TryEnqueue(command);
    public static bool TryEnqueue(RoomEditorCommand command) => inbox.TryEnqueue(command);
    internal static bool TryDequeue(out RoomEditorCommand command) => inbox.TryDequeue(out command);
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static int PendingCount => inbox.PendingCount;
    internal static void Clear() => inbox.Clear();
}
