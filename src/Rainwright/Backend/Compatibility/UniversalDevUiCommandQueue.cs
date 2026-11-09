using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Compatibility;

// Detached third-party control commands cross from RWImGui to the authoring
// thread without carrying DevUINode, PlacedObject or gameplay plugin references.
public enum UniversalDevUiCommandKind
{
    Click,
    SetBoolean,
    SetSlider,
    ResetSlider,
    SetChoice,
    IncrementInteger,
    SetText,
    SetDirection,
    SetColor
}

public readonly struct UniversalDevUiCommand
{
    public UniversalDevUiCommand(
        UniversalDevUiCommandKind kind,
        string path,
        int integer = 0,
        float x = 0f,
        float y = 0f,
        float z = 0f,
        float w = 0f,
        bool boolean = false,
        string text = null)
    {
        Kind = kind;
        Path = path ?? string.Empty;
        Integer = integer;
        X = x;
        Y = y;
        Z = z;
        W = w;
        Boolean = boolean;
        Text = text ?? string.Empty;
    }

    public UniversalDevUiCommandKind Kind { get; }
    public string Path { get; }
    public int Integer { get; }
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }
    public bool Boolean { get; }
    public string Text { get; }
}

/// <summary>
/// Page-agnostic mutation queue used by the RWImGui frontend. Every command resolves its target
/// against the active DevInterface tree during the backend command phase, so dynamic panels can
/// appear/disappear without the frontend retaining raw DevUINode references.
/// </summary>
public static class UniversalDevUiCommandQueue
{
    internal const int MaxPendingCommands = 2048;
    internal const int MaxCommandsPerFrame = 64;

    private static readonly ConcurrentQueue<UniversalDevUiCommand> Queue = new();
    private static int reserved;
    private static int rejected;

    public static void Enqueue(UniversalDevUiCommand command) => TryEnqueue(command);

    public static bool TryEnqueue(UniversalDevUiCommand command)
    {
        if (Interlocked.Increment(ref reserved) > MaxPendingCommands)
        {
            Interlocked.Decrement(ref reserved);
            Interlocked.Increment(ref rejected);
            return false;
        }

        try
        {
            Queue.Enqueue(command);
            return true;
        }
        catch
        {
            Interlocked.Decrement(ref reserved);
            throw;
        }
    }

    internal static bool TryDequeue(out UniversalDevUiCommand command)
    {
        if (!Queue.TryDequeue(out command)) return false;
        Interlocked.Decrement(ref reserved);
        return true;
    }

    internal static int ConsumeRejectedCount() => Interlocked.Exchange(ref rejected, 0);
    internal static int PendingCount => Volatile.Read(ref reserved);

    internal static void Clear()
    {
        while (TryDequeue(out _)) { }
        Interlocked.Exchange(ref rejected, 0);
    }
}
