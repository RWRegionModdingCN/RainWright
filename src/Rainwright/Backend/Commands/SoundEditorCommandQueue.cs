using System;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Sound;

public enum SoundEditorCommandKind
{
    Select = 0,
    Create = 1,
    Delete = 2,
    SetRoomValue = 3,
    SetSoundValue = 4,
    ReloadGroups = 5,
    SetGroupDirectory = 6,
    ResetGroupDirectory = 7,
    CreateGroup = 8,
    DeleteGroup = 9,
    AddSoundToGroup = 10,
    ApplyGroup = 11,
    CreateFromLibrary = 12,
    AddSoundsToGroup = 13,
    CreateGroupFromSounds = 14
}

public readonly struct SoundEditorCommand
{
    public SoundEditorCommand(
        SoundEditorCommandKind kind,
        int index = -1,
        string key = null,
        string text = null,
        int secondaryIndex = -1,
        EditorPropertyValue value = default)
    {
        Kind = kind;
        Index = index;
        Key = key;
        Text = text;
        SecondaryIndex = secondaryIndex;
        Value = value;
        Indices = Array.Empty<int>();
    }

    public SoundEditorCommand(
        SoundEditorCommandKind kind,
        int[] indices,
        int index = -1,
        string key = null,
        string text = null,
        int secondaryIndex = -1,
        EditorPropertyValue value = default)
    {
        Kind = kind;
        Index = index;
        Key = key;
        Text = text;
        SecondaryIndex = secondaryIndex;
        Value = value;
        Indices = indices ?? Array.Empty<int>();
    }

    public SoundEditorCommandKind Kind { get; }
    public int Index { get; }
    public string Key { get; }
    public string Text { get; }
    public int SecondaryIndex { get; }
    public EditorPropertyValue Value { get; }
    public int[] Indices { get; }
}

/// <summary>Rainwright's bounded native-input transport; Unity actions run only in the adapter.</summary>
public static class SoundEditorCommandQueue
{
    internal const int MaxCommandsPerFrame = 64;
    internal const int MaxPendingCommands = 2048;
    private static readonly EditorCommandInbox<SoundEditorCommand> inbox = new(MaxPendingCommands);

    public static void Enqueue(SoundEditorCommand command) => inbox.TryEnqueue(command);
    public static bool TryEnqueue(SoundEditorCommand command) => inbox.TryEnqueue(command);
    internal static bool TryDequeue(out SoundEditorCommand command) => inbox.TryDequeue(out command);
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static int PendingCount => inbox.PendingCount;
    internal static void Clear() => inbox.Clear();
}
