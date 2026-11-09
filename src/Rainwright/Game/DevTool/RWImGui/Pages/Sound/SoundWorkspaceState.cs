using System;
using DryCycle.DevUI.DevTool.Sound;

namespace DryCycle.DevUI.DevTool.RWImGui;

// Sound workspace bridge: the group model and selection machine both live
// in Rainwright; the old frontend still supplies its live library snapshot.
internal static class SoundWorkspaceState
{
    private static readonly SoundSceneSelectionCore SceneSelection = new();
    private static readonly SoundGroupWorkspaceCore GroupWorkspace = new();

    private static SoundGroupSnapshot[] Groups =>
        SoundGroupLibrary.Current.Groups ?? Array.Empty<SoundGroupSnapshot>();

    internal static string ActiveGroupId => GroupWorkspace.ActiveGroupId;
    internal static SoundLibraryDestination LibraryDestination
    {
        get => GroupWorkspace.LibraryDestination;
        set => GroupWorkspace.LibraryDestination = value;
    }
    internal static int SelectionCount => SceneSelection.SelectionCount;

    internal static void SynchronizeGroups() => GroupWorkspace.SynchronizeGroups(Groups);
    internal static bool TryGetActiveLocalGroup(out SoundGroupSnapshot group) =>
        GroupWorkspace.TryGetActiveLocalGroup(Groups, out group);
    internal static void SetActiveGroup(string groupId) =>
        GroupWorkspace.SetActiveGroup(groupId);
    internal static bool GroupIdExists(string groupId) =>
        GroupWorkspace.GroupIdExists(groupId, Groups);
    internal static string SuggestUniqueGroupId(string displayName) =>
        GroupWorkspace.SuggestUniqueGroupId(displayName, Groups);
    internal static bool IsValidGroupId(string groupId) =>
        GroupWorkspace.IsValidGroupId(groupId);

    // Legacy UI-facing methods forward into the Rainwright-owned selection
    // state machine. Only source snapshot conversion remains in this host.
    internal static void SynchronizeScene(EditorSoundPresentationSnapshot snapshot) =>
        SceneSelection.SynchronizeScene(snapshot?.RoomKey ?? string.Empty,
            snapshot?.Sounds?.Length ?? 0, snapshot?.SelectedIndex ?? -1);

    internal static bool IsSceneSelected(int index) => SceneSelection.IsSceneSelected(index);
    internal static void HandleSceneClick(int index, bool ctrl, bool shift) =>
        SceneSelection.HandleSceneClick(index, ctrl, shift);
    internal static void SelectAll(int count) => SceneSelection.SelectAll(count);
    internal static int[] SelectedIndices() => SceneSelection.SelectedIndices();
    internal static void ClearSelection() => SceneSelection.ClearSelection();
    internal static void ResetSelectionFromLegacy() => SceneSelection.ResetSelectionFromLegacy();

}
