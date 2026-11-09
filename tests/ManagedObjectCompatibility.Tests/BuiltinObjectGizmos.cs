using System;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DryCycle.DevUI.DevTool.Gizmos;
using DryCycle.DevUI.DevTool.Objects;
using UnityEngine;

internal static partial class Program
{
    private static void VerifyBuiltinObjectGizmos(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var capture = typeof(ObjectInspectorRegistry).GetMethod("Capture", flags);
        var geometry = assembly.GetType("DryCycle.DevUI.DevTool.Objects.NativeObjectGizmoPresentation", true).GetMethod("Capture", flags);
        var apply = assembly.GetType("DryCycle.DevUI.DevTool.Gizmos.NativeObjectGizmoEditCommandProcessor", true).GetMethod("ApplyDrag", flags);
        // Minimum counts come from the original representations, including their inherited handles.
        var cases = new[] {
            ("LightSource", 1), ("InsectGroup", 1), ("DeepProcessing", 3), ("LightBeam", 3),
            ("Rainbow", 1), ("RainbowNoFade", 1), ("DeadTokenStalk", 1), ("PlayerPushback", 1),
            ("CellDistortion", 1), ("LocalBlizzard", 1), ("SnowSource", 1), ("HarmfulSteam", 1),
            ("SteamPipe", 1), ("SkyWhalePathfinding", 1), ("TerrainHandle", 3), ("WindRect", 3),
            ("LocalTerrain", 1), ("CurvedSlope", 1), ("SuperSlope", 2), ("WaterCurrent", 3),
            ("WaterCutoff", 1), ("AirPocket", 2), ("MudPit", 2), ("LobeTree", 2),
            ("PassiveCorruption", 1), ("CosmeticRipple", 1), ("TerrainGrassPatch", 8),
            ("WallLight", 3), ("AetherRainbow", 9), ("FlameJet", 1), ("UrbanLife", 4),
            ("UrbanLifePath", 2), ("UrbanCandles", 2), ("KarmaFlowerPatch", 2),
            ("DaemonEye", 1), ("DaemonCrown", 1), ("WeaverSpot", 1), ("FloatingDebris", 4)
        };
        bool originalWatcher = ModManager.Watcher;
        bool originalDevTools = ModManager.DevTools;
        bool registeredWatcher = Watcher.WatcherEnums.PlacedObjectType.WeaverSpot == null;
        if (registeredWatcher) Watcher.WatcherEnums.PlacedObjectType.RegisterValues();
        try
        {
            ModManager.Watcher = true;
            ModManager.DevTools = true;
            int handles = 0;
            foreach (var entry in cases)
            {
                var target = new PlacedObject(new PlacedObject.Type(entry.Item1), null) { pos = new Vector2(100, 200) };
                var properties = (EditorPropertySnapshot[])capture.Invoke(null, new object[] { target });
                var shape = (EditorObjectGizmoSnapshot)geometry.Invoke(null, new object[] { target, 0, properties });
                Check(shape.Handles.Length >= entry.Item2,
                    entry.Item1 + " preserves its original editable handles; expected at least " + entry.Item2 + ", got " + shape.Handles.Length);
                foreach (var handle in shape.Handles)
                {
                    var command = new NativeObjectGizmoEditCommand(NativeObjectGizmoEditKind.Update, 0,
                        handle.Id, handle.X + 13, handle.Y + 17);
                    Check((bool)apply.Invoke(null, new object[] { null, target, command }),
                        "The actual scene drag command is accepted for " + entry.Item1 + " / " + handle.Id);
                    handles++;
                }
            }
            Console.WriteLine("BUILTIN GIZMOS: " + handles + " draggable handles across " + cases.Length + " original/DLC object types.");

            var rainbow = new PlacedObject(Watcher.WatcherEnums.PlacedObjectType.AetherRainbow, null) { pos = new Vector2(100, 200) };
            var data = (Watcher.AetherRainbow.AetherRainbowData)rainbow.data;
            Check((bool)apply.Invoke(null, new object[] { null, rainbow,
                new NativeObjectGizmoEditCommand(NativeObjectGizmoEditKind.Update, 0, "aether:archDepth", 180, 270) }) && Math.Abs(data.archDepth - .8f) < .0001f,
                "An Aether Rainbow depth handle subtracts its own curve point and uses the native hundred-pixel scale.");
            rainbow.pos = new Vector2(150, 250);
            assembly.GetType("DryCycle.DevUI.DevTool.Commands.EditorActions", true)
                .GetMethod("TryRefresh", flags).Invoke(null, new object[] { null, rainbow });
            Check(data.pos == rainbow.pos, "Moving or duplicating Aether Rainbow synchronizes its serialized origin inside the author edit.");

            var room = (Room)FormatterServices.GetUninitializedObject(typeof(Room));
            room.updateList = new List<UpdatableAndDeletable>();
            var runtime = (Watcher.AetherRainbow)FormatterServices.GetUninitializedObject(typeof(Watcher.AetherRainbow));
            runtime.room = room;
            data.obj = runtime;
            room.updateList.Add(runtime);
            data.startDepth = .3f; data.endWidth = .7f;
            string authored = data.ToString();
            var refresh = assembly.GetType("DryCycle.DevUI.DevTool.Objects.BuiltinObjectRuntimeAdapters", true).GetMethod("Refresh", flags);
            refresh.Invoke(null, new object[] { room, rainbow });
            refresh.Invoke(null, new object[] { room, rainbow });
            Check(ReferenceEquals(data.obj, runtime) && room.updateList.Count == 1 && runtime.pos == rainbow.pos &&
                runtime.start == data.start && runtime.arch == data.arch && runtime.startDepth == .3f && runtime.endWidth == .7f &&
                runtime.edgeDirty && runtime.meshDirty && runtime.colDirty && data.ToString() == authored,
                "Aether Rainbow reuses its runtime, invalidates native meshes/colors and previews all authored values without rewriting author data.");
        }
        finally
        {
            ModManager.Watcher = originalWatcher;
            ModManager.DevTools = originalDevTools;
            if (registeredWatcher) Watcher.WatcherEnums.PlacedObjectType.UnregisterValues();
        }
    }
}
