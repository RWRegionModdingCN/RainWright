using System;
using System.Collections.Concurrent;
using System.Threading;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Core;

// Frontends publish immutable commands directly to Rainwright. Only the
// Unity/main-thread processor in the gameplay adapter consumes these records.
public enum EditorUiCommandKind
{
    Save,
    Undo,
    Redo,
    ToggleFocus,
    ToggleBrowser,
    ToggleInspector,
    ToggleLegacyUi,
    SetToolMode,
    SelectObject,
    ToggleObjectSelection,
    SelectObjectRange,
    DeleteObject,
    DeleteSelection,
    DuplicateSelection,
    CreateObject,
    BeginPlacement,
    PlaceObjectAtCursor,
    CancelPlacement,
    SetObjectPosition,
    SetSelectionPosition,
    SnapSelectionToGrid,
    AlignSelection,
    DistributeSelection,
    SetObjectProperty,
    SetSelectionProperty,
    InvokeLegacyButton,
    SetLegacySlider,
    ResetLegacySlider,
    SetLegacyText,
    SetLegacyDirection,
    SetLegacyColor
}

public readonly struct EditorUiCommand
{
    public EditorUiCommand(
        EditorUiCommandKind kind,
        int index = -1,
        int secondaryIndex = -1,
        string text = null,
        float x = 0f,
        float y = 0f,
        bool flag = false,
        EditorToolMode mode = EditorToolMode.Room,
        EditorPropertyValue propertyValue = default,
        long stableId = 0L,
        long secondaryStableId = 0L)
    {
        Kind = kind;
        Index = index;
        SecondaryIndex = secondaryIndex;
        Text = text;
        X = x;
        Y = y;
        Flag = flag;
        Mode = mode;
        PropertyValue = propertyValue;
        StableId = stableId;
        SecondaryStableId = secondaryStableId;
    }

    public EditorUiCommandKind Kind { get; }
    public int Index { get; }
    public int SecondaryIndex { get; }
    public string Text { get; }
    public float X { get; }
    public float Y { get; }
    public bool Flag { get; }
    public EditorToolMode Mode { get; }
    public EditorPropertyValue PropertyValue { get; }
    public long StableId { get; }
    public long SecondaryStableId { get; }
}

public static class EditorUiCommandQueue
{
    // Native rendering can enqueue concurrently, but cannot stall the game
    // update indefinitely or allocate an unbounded number of pending edits.
    internal const int MaxCommandsPerFrame = 128;
    internal const int MaxPendingCommands = 4096;

    private static readonly ConcurrentQueue<EditorUiCommand> queue = new();
    private static int reserved;
    private static int dropped;

    public static void Enqueue(EditorUiCommand command) => TryEnqueue(command);

    public static bool TryEnqueue(EditorUiCommand command)
    {
        if (Interlocked.Increment(ref reserved) > MaxPendingCommands)
        {
            Interlocked.Decrement(ref reserved);
            Interlocked.Increment(ref dropped);
            return false;
        }
        try
        {
            queue.Enqueue(command);
            return true;
        }
        catch
        {
            Interlocked.Decrement(ref reserved);
            throw;
        }
    }

    internal static bool TryDequeue(out EditorUiCommand command)
    {
        if (!queue.TryDequeue(out command))
            return false;
        Interlocked.Decrement(ref reserved);
        return true;
    }

    internal static int ConsumeDroppedCount() => Interlocked.Exchange(ref dropped, 0);
    internal static int PendingCount => Volatile.Read(ref reserved);

    internal static void Clear()
    {
        while (TryDequeue(out _)) { }
        Interlocked.Exchange(ref dropped, 0);
    }
}
