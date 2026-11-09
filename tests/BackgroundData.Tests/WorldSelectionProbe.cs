using System.Reflection;

// Execute the built frontend's selection boundary without starting Rain World or native ImGui.
internal static class WorldSelectionProbe
{
    internal static int Run(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static;
        var view = assembly.GetType("DryCycle.DevUI.DevTool.RWImGui.WorldWorkspaceView", true)!;
        var map = assembly.GetType("DryCycle.DevUI.DevTool.RWImGui.WorldMapView", true)!;
        var snapshotType = assembly.GetType("DryCycle.DevUI.DevTool.Map.EditorMapPresentationSnapshot", true)!;
        var roomType = assembly.GetType("DryCycle.DevUI.DevTool.Map.EditorMapRoomSnapshot", true)!;
        object Call(string name, params object[] args) => view.GetMethod(name, flags)!.Invoke(null, args);
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        var rooms = Array.CreateInstance(roomType, 2);
        for (int i = 0; i < 2; i++)
        {
            var room = Activator.CreateInstance(roomType)!;
            roomType.GetProperty("RoomIndex")!.SetValue(room, i == 0 ? 5 : 8);
            roomType.GetProperty("Name")!.SetValue(room, "TEST_" + i);
            rooms.SetValue(room, i);
        }
        object Snapshot(int selected)
        {
            var value = Activator.CreateInstance(snapshotType)!;
            snapshotType.GetProperty("SelectedRoomIndex")!.SetValue(value, selected);
            snapshotType.GetProperty("Rooms")!.SetValue(value, rooms);
            return value;
        }
        var currentRoom = Snapshot(5);
        Call("EnsureRoomIndex", currentRoom);
        Call("SynchronizeSelection", currentRoom);
        Check(!(bool)Call("HasDetailInspector", currentRoom), "Initial region view has no room inspector.");
        Call("SelectCanvasRoom", 5, false);
        Check((bool)Call("HasDetailInspector", currentRoom), "First click on the backend's default room immediately opens its inspector.");
        Check((bool)Call("IsCanvasRoomSelected", 5), "Explicit click selects the current room without requiring a different room first.");
        Call("SynchronizeCanvasSelection", currentRoom);
        Call("SelectCanvasRoom", 5, true);
        Call("SynchronizeCanvasSelection", currentRoom);
        Check(!(bool)Call("HasDetailInspector", currentRoom) && (int)Call("SelectedRoomIndex", currentRoom) == -1,
            "Repeated click hides the inspector even while the old selected snapshot is still published.");
        var empty = Snapshot(-1);
        Call("SynchronizeCanvasSelection", empty);
        Call("SelectCanvasRoom", 8, false);
        Call("SynchronizeCanvasSelection", empty);
        Check((bool)Call("HasDetailInspector", empty) && (int)Call("SelectedRoomIndex", empty) == 8,
            "Selecting another room projects the clicked room before the queued command is acknowledged.");
        Call("SelectCanvasRoom", 5, false);
        Call("SynchronizeCanvasSelection", Snapshot(8));
        Check((int)Call("SelectedRoomIndex", empty) == 5, "A stale intermediate snapshot cannot overwrite a newer rapid click.");
        Call("SynchronizeCanvasSelection", currentRoom);
        Check(view.GetField("pendingCanvasRoomIndex", flags)!.GetValue(null) == null, "Matching snapshots release pending click state.");
        Call("SelectCanvasRoom", -1, false);
        Check(!(bool)Call("HasDetailInspector", currentRoom), "Empty-canvas click clears the inspector.");
        map.GetMethod("SelectConnection", flags)!.Invoke(null, new object[] { "old-link" });
        Call("SelectCanvasRoom", 8, false);
        Check((string)map.GetProperty("SelectedConnectionId", flags)!.GetValue(null) == "", "Room selection clears the previous connection selection.");
        Console.WriteLine($"PASS: {checks} compiled world-map single-click, toggle, blank-canvas and stale-snapshot checks.");
        return checks;
    }
}
