using System;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Dialog;

public enum DialogEditorCommandKind
{
    SelectDialog
}

public readonly struct DialogEditorCommand
{
    public DialogEditorCommand(DialogEditorCommandKind kind, string path = null)
    {
        Kind = kind;
        Path = path;
    }

    public DialogEditorCommandKind Kind { get; }
    public string Path { get; }
}

/// <summary>Rainwright's bounded native-input transport; Unity actions run only in the adapter.</summary>
public static class DialogEditorCommandQueue
{
    internal const int MaxCommandsPerFrame = 32;
    internal const int MaxPendingCommands = 2048;
    private static readonly EditorCommandInbox<DialogEditorCommand> inbox = new(MaxPendingCommands);

    public static void Enqueue(DialogEditorCommand command) => inbox.TryEnqueue(command);
    public static bool TryEnqueue(DialogEditorCommand command) => inbox.TryEnqueue(command);
    internal static bool TryDequeue(out DialogEditorCommand command) => inbox.TryDequeue(out command);
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static int PendingCount => inbox.PendingCount;
    internal static void Clear() => inbox.Clear();
}
