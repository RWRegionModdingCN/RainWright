using System;
using DryCycle.DevUI.DevTool.Objects;
using DryCycle.DevUI.DevTool.Compatibility;

namespace DryCycle.DevUI.DevTool.Core;

public sealed class EditorObjectSnapshot
{
    public int Index { get; init; }
    public long StableId { get; init; }
    public string Type { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public ObjectPresentationKind PresentationKind { get; init; }
    public int Importance { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public bool Selected { get; init; }
}

public sealed class EditorObjectTypeSnapshot
{
    public string Type { get; init; }
    public string DisplayName { get; init; }
    public string Category { get; init; }
    public string Source { get; init; }
    public string[] Tags { get; init; }
    public ObjectPresentationKind PresentationKind { get; init; }
    public int Importance { get; init; }
}

public sealed class EditorInspectorSnapshot
{
    public bool HasSelection { get; init; }
    public int ObjectIndex { get; init; } = -1;
    public long ObjectStableId { get; init; }
    public int SelectionCount { get; init; }
    public string Type { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public string DataType { get; init; } = string.Empty;
    public bool LegacyUiAvailable { get; init; }
    public bool LegacyUiVisible { get; init; }
    public EditorPropertySnapshot[] Properties { get; init; } = Array.Empty<EditorPropertySnapshot>();
    public EditorObjectGizmoSnapshot ObjectGizmo { get; init; } = EditorObjectGizmoSnapshot.Empty;
    public string[] MixedPropertyKeys { get; init; } = Array.Empty<string>();
    public LegacyControlSnapshot[] LegacyControls { get; init; } = Array.Empty<LegacyControlSnapshot>();
}

public sealed class EditorPresentationSnapshot
{
    public static readonly EditorPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public bool Hydrated { get; init; }
    public bool DevToolsActive { get; init; }
    public string Document { get; init; } = string.Empty;
    public string RoomName { get; init; } = string.Empty;
    public EditorToolMode ToolMode { get; init; }
    public bool FocusMode { get; init; }
    public bool BrowserOpen { get; init; }
    public bool InspectorOpen { get; init; }
    public bool CanUndo { get; init; }
    public bool CanRedo { get; init; }
    public string UndoLabel { get; init; } = string.Empty;
    public string RedoLabel { get; init; } = string.Empty;
    public bool PlacementActive { get; init; }
    public string PlacementType { get; init; } = string.Empty;
    public string PlacementError { get; init; } = string.Empty;
    public EditorObjectSnapshot[] SceneObjects { get; init; } = Array.Empty<EditorObjectSnapshot>();
    public EditorObjectTypeSnapshot[] ObjectLibrary { get; init; } = Array.Empty<EditorObjectTypeSnapshot>();
    public EditorInspectorSnapshot Inspector { get; init; } = new();
}



/// <summary>
/// Single Rainwright-owned shell/Objects view visible to all native frontends.
/// The game-specific capture adapter publishes immutable snapshots, not live
/// Room/DevUINode references. Closing the editor clears the shared state.
/// </summary>
public static class EditorPresentationState
{
    private static EditorPresentationSnapshot current = EditorPresentationSnapshot.Empty;
    public static EditorPresentationSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorPresentationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorPresentationSnapshot.Empty);
    internal static void Clear() => Publish(EditorPresentationSnapshot.Empty);
}
