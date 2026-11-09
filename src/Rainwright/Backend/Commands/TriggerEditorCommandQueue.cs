using System;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Triggers;

public enum TriggerEditorCommandKind
{
    Select,
    Create,
    Delete,
    SetValue,
    ToggleSlugcat,
    SetSlugcats,
    SetEventType,
    ClearEvent,
    SetEventValue
}

public readonly struct TriggerEditorCommand
{
    public TriggerEditorCommand(
        TriggerEditorCommandKind kind,
        int index = -1,
        string key = null,
        string text = null,
        EditorPropertyValue value = default,
        string[] texts = null,
        string name = null)
    {
        Kind = kind;
        Index = index;
        Key = key;
        Text = text;
        Value = value;
        Texts = texts ?? Array.Empty<string>();
        Name = name ?? string.Empty;
    }

    public TriggerEditorCommandKind Kind { get; }
    public int Index { get; }
    public string Key { get; }
    public string Text { get; }
    public EditorPropertyValue Value { get; }
    public string[] Texts { get; }
    public string Name { get; }
}

/// <summary>Rainwright's bounded native-input transport; Unity actions run only in the adapter.</summary>
public static class TriggerEditorCommandQueue
{
    internal const int MaxCommandsPerFrame = 64;
    internal const int MaxPendingCommands = 2048;
    private static readonly EditorCommandInbox<TriggerEditorCommand> inbox = new(MaxPendingCommands);

    public static void Enqueue(TriggerEditorCommand command) => inbox.TryEnqueue(command);
    public static bool TryEnqueue(TriggerEditorCommand command) => inbox.TryEnqueue(command);
    internal static bool TryDequeue(out TriggerEditorCommand command) => inbox.TryDequeue(out command);
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static int PendingCount => inbox.PendingCount;
    internal static void Clear() => inbox.Clear();
}
