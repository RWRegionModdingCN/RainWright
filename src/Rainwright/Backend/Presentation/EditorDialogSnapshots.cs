using System;

namespace DryCycle.DevUI.DevTool.Dialog;

public enum EditorDialogEventKind
{
    Text,
    Wait,
    Special,
    Chatlog,
    Unknown
}

public sealed class EditorDialogEventSnapshot
{
    public EditorDialogEventKind Kind { get; init; }
    public int InitialWait { get; init; }
    public int Linger { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class EditorDialogPresentationSnapshot
{
    public static readonly EditorDialogPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string Language { get; init; } = string.Empty;
    public string SelectedPath { get; init; } = string.Empty;
    public string SelectedFileName { get; init; } = string.Empty;
    public string[] DialogPaths { get; init; } = Array.Empty<string>();
    public EditorDialogEventSnapshot[] Events { get; init; } = Array.Empty<EditorDialogEventSnapshot>();
}



/// <summary>Shared editor view state owned by Rainwright rather than the game adapter.</summary>
public static class EditorDialogPresentationState
{
    private static EditorDialogPresentationSnapshot current = EditorDialogPresentationSnapshot.Empty;
    public static EditorDialogPresentationSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorDialogPresentationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorDialogPresentationSnapshot.Empty);
    internal static void Clear() => Publish(EditorDialogPresentationSnapshot.Empty);
}
