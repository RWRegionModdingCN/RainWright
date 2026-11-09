using System;
using System.Linq;
using DryCycle.DevUI.DevTool.RWImGui;
using DryCycle.DevUI.DevTool.Sound;

namespace DryCycle.DevUI.DevTool.Sound
{
    // Resource-library host is deliberately stubbed; shared snapshot types
    // are resolved from the actual Rainwright.dll assembly.
    internal static class SoundGroupLibrary
    {
        internal static SoundGroupLibrarySnapshot Current { get; set; } = SoundGroupLibrarySnapshot.Empty;
    }
}

internal static class Program
{
    private static int assertions;
    private static void Check(bool valid, string message)
    {
        assertions++;
        if (!valid) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        Check(typeof(EditorSoundPresentationSnapshot).Assembly.GetName().Name == "RainWright",
            "The Sound adapter test must use the real immutable Rainwright frontend snapshot.");
        Check(typeof(SoundSceneSelectionCore).Assembly.GetName().Name == "RainWright" &&
              typeof(SoundSceneSelectionCore).Assembly != typeof(SoundWorkspaceState).Assembly,
            "Sound multiselection must be compiled into one Rainwright-owned DLL.");

        var current = new EditorSoundPresentationSnapshot
        {
            RoomKey = "SU_A01", Sounds = new EditorSoundSnapshot[5], SelectedIndex = 2
        };
        SoundWorkspaceState.SynchronizeScene(current);
        Check(SoundWorkspaceState.SelectionCount == 1 &&
              SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 2 }),
            "Legacy single selection must seed the new editor multi-select.");
        int[] immutable = SoundWorkspaceState.SelectedIndices();
        Check(ReferenceEquals(immutable, SoundWorkspaceState.SelectedIndices()),
            "Stable selection should reuse its immutable published snapshot.");
        SoundWorkspaceState.HandleSceneClick(4, ctrl: true, shift: false);
        Check(SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 2, 4 }) &&
              immutable.SequenceEqual(new[] { 2 }),
            "Ctrl multiselection must publish a new snapshot without mutating queued commands.");
        current = new EditorSoundPresentationSnapshot
        {
            RoomKey = "SU_A01", Sounds = current.Sounds, SelectedIndex = 4
        };
        SoundWorkspaceState.SynchronizeScene(current);
        Check(SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 2, 4 }),
            "Expected legacy single-index acknowledgement must preserve multi-select.");
        SoundWorkspaceState.HandleSceneClick(1, ctrl: false, shift: true);
        Check(SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 1, 2, 3, 4 }),
            "Shift selection must replace the visible contiguous anchor range.");
        SoundWorkspaceState.SelectAll(5);
        Check(SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 0, 1, 2, 3, 4 }),
            "Select-all must select the complete current room, sorted.");
        current = new EditorSoundPresentationSnapshot
        {
            RoomKey = "SU_A01", Sounds = new EditorSoundSnapshot[2], SelectedIndex = 4
        };
        SoundWorkspaceState.SynchronizeScene(current);
        Check(SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 0, 1 }),
            "Shorter room sounds must prune out-of-range selected indices.");
        SoundWorkspaceState.ClearSelection();
        Check(SoundWorkspaceState.SelectionCount == 0,
            "Clear must release all retained selected sounds.");

        var next = new EditorSoundPresentationSnapshot
        {
            RoomKey = "DS_A02", Sounds = new EditorSoundSnapshot[3], SelectedIndex = -1
        };
        SoundWorkspaceState.SynchronizeScene(next);
        Check(SoundWorkspaceState.SelectionCount == 0,
            "Room changes must not retain selected indices from another room.");
        SoundWorkspaceState.ResetSelectionFromLegacy();
        next = new EditorSoundPresentationSnapshot
        {
            RoomKey = "DS_A02", Sounds = next.Sounds, SelectedIndex = 1
        };
        SoundWorkspaceState.SynchronizeScene(next);
        Check(SoundWorkspaceState.SelectedIndices().SequenceEqual(new[] { 1 }),
            "Explicit legacy reset must resynchronize the current single selection.");

        Check(typeof(SoundGroupWorkspaceCore).Assembly.GetName().Name == "RainWright" &&
              typeof(SoundGroupWorkspaceCore).Assembly != typeof(SoundWorkspaceState).Assembly,
            "Group workspace should belong to independently built Rainwright.dll.");
        var localOne = new SoundGroupSnapshot
        {
            Id = "LocalOne", Name = "My First Group", IsLocal = true
        };
        var imported = new SoundGroupSnapshot
        {
            Id = "Imported", Name = "Read-only", IsLocal = false
        };
        SoundGroupLibrary.Current = new SoundGroupLibrarySnapshot
        {
            Groups = new[] { imported, localOne }
        };
        SoundWorkspaceState.SynchronizeGroups();
        Check(SoundWorkspaceState.ActiveGroupId == "LocalOne" &&
              SoundWorkspaceState.TryGetActiveLocalGroup(out var selectedGroup) &&
              ReferenceEquals(selectedGroup, localOne),
            "Group sync should skip read-only/imported groups and choose first local.");
        Check(SoundWorkspaceState.GroupIdExists("LOCALONE") &&
              SoundWorkspaceState.GroupIdExists("imported") &&
              !SoundWorkspaceState.GroupIdExists("missing"),
            "Sound group ID matching must be case-insensitive across all sources.");
        Check(SoundWorkspaceState.IsValidGroupId("group_name-2.4") &&
              !SoundWorkspaceState.IsValidGroupId("group name") &&
              !SoundWorkspaceState.IsValidGroupId("中文") &&
              !SoundWorkspaceState.IsValidGroupId(" "),
            "Sound group ID syntax validation regressed.");
        Check(SoundWorkspaceState.SuggestUniqueGroupId("Local One") == "LocalOne2" &&
              SoundWorkspaceState.SuggestUniqueGroupId("123") == "SoundGroup",
            "Group ID normalization/collision resolution regressed.");
        var localTwo = new SoundGroupSnapshot
        {
            Id = "LocalTwo", Name = "Newer Group", IsLocal = true
        };
        SoundGroupLibrary.Current = new SoundGroupLibrarySnapshot
        {
            Groups = new[] { imported, localOne, localTwo }
        };
        SoundWorkspaceState.SetActiveGroup(" localtwo ");
        Check(SoundWorkspaceState.TryGetActiveLocalGroup(out selectedGroup) &&
              ReferenceEquals(selectedGroup, localTwo),
            "Selected group ID should resolve against the refreshed immutable library.");
        SoundWorkspaceState.LibraryDestination = SoundLibraryDestination.WorkingGroup;
        SoundGroupLibrary.Current = SoundGroupLibrarySnapshot.Empty;
        SoundWorkspaceState.SynchronizeGroups();
        Check(SoundWorkspaceState.ActiveGroupId.Length == 0 &&
              SoundWorkspaceState.LibraryDestination == SoundLibraryDestination.Scene,
            "Removing all local groups must return the workspace to Scene destination.");

        Console.WriteLine("PASS: " + assertions +
            " Sound Scene selection assertions through the production frontend adapter and Rainwright.dll.");
    }
}
