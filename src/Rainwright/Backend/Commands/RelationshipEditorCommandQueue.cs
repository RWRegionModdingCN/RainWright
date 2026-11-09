using System;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Relationships;

public enum RelationshipEditorCommandKind
{
    SelectPrimary,
    SelectPair,
    SetRelationshipType,
    SetRelationshipIntensity,
    ResetRelationship
}

public readonly struct RelationshipEditorCommand
{
    public RelationshipEditorCommand(
        RelationshipEditorCommandKind kind,
        string primary = null,
        string other = null,
        string text = null,
        float value = 0f,
        EditorRelationshipDirection direction = EditorRelationshipDirection.PrimaryToOther)
    {
        Kind = kind;
        Primary = primary;
        Other = other;
        Text = text;
        Value = value;
        Direction = direction;
    }

    public RelationshipEditorCommandKind Kind { get; }
    public string Primary { get; }
    public string Other { get; }
    public string Text { get; }
    public float Value { get; }
    public EditorRelationshipDirection Direction { get; }
}

/// <summary>Rainwright's bounded native-input transport; Unity actions run only in the adapter.</summary>
public static class RelationshipEditorCommandQueue
{
    internal const int MaxCommandsPerFrame = 32;
    internal const int MaxPendingCommands = 2048;
    private static readonly EditorCommandInbox<RelationshipEditorCommand> inbox = new(MaxPendingCommands);

    public static void Enqueue(RelationshipEditorCommand command) => inbox.TryEnqueue(command);
    public static bool TryEnqueue(RelationshipEditorCommand command) => inbox.TryEnqueue(command);
    internal static bool TryDequeue(out RelationshipEditorCommand command) => inbox.TryDequeue(out command);
    internal static int ConsumeRejectedCount() => inbox.ConsumeRejectedCount();
    internal static int PendingCount => inbox.PendingCount;
    internal static void Clear() => inbox.Clear();
}
