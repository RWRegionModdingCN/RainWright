namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Game-neutral command effects. Owned by Rainwright so the game adapter and
/// native frontends share the same object invalidation classification.
/// </summary>
internal enum EditorObjectMutationScope
{
    None,
    Member,
    SelectionMembers,
    Collection,
    Full
}

internal static class EditorUiCommandPolicy
{
    internal static bool IsObjectModelCommand(EditorUiCommandKind kind) =>
        ObjectMutationScope(kind) != EditorObjectMutationScope.None;

    internal static EditorObjectMutationScope ObjectMutationScope(EditorUiCommandKind kind) =>
        kind switch
        {
            EditorUiCommandKind.SetObjectPosition or
            EditorUiCommandKind.SetObjectProperty =>
                EditorObjectMutationScope.Member,
            EditorUiCommandKind.SetSelectionPosition or
            EditorUiCommandKind.SnapSelectionToGrid or
            EditorUiCommandKind.AlignSelection or
            EditorUiCommandKind.DistributeSelection or
            EditorUiCommandKind.SetSelectionProperty =>
                EditorObjectMutationScope.SelectionMembers,
            EditorUiCommandKind.DeleteObject or
            EditorUiCommandKind.DeleteSelection or
            EditorUiCommandKind.DuplicateSelection or
            EditorUiCommandKind.CreateObject or
            EditorUiCommandKind.PlaceObjectAtCursor =>
                EditorObjectMutationScope.Collection,
            EditorUiCommandKind.InvokeLegacyButton or
            EditorUiCommandKind.SetLegacySlider or
            EditorUiCommandKind.ResetLegacySlider or
            EditorUiCommandKind.SetLegacyText or
            EditorUiCommandKind.SetLegacyDirection or
            EditorUiCommandKind.SetLegacyColor =>
                EditorObjectMutationScope.Full,
            _ => EditorObjectMutationScope.None
        };
}
