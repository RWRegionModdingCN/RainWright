using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;
using UnityEngine;

internal static partial class Program
{
    private static void VerifyBuiltinObjectInspectors(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var capture = typeof(ObjectInspectorRegistry).GetMethod("Capture", flags);
        var set = typeof(ObjectInspectorRegistry).GetMethod("TrySetValue", flags);
        var drag = typeof(ObjectInspectorRegistry).GetMethod("TryBuildGizmoValue", flags);
        var geometry = assembly.GetType("DryCycle.DevUI.DevTool.Objects.NativeObjectGizmoPresentation", true).GetMethod("Capture", flags);
        EditorPropertySnapshot[] Capture(PlacedObject target) => (EditorPropertySnapshot[])capture.Invoke(null, new object[] { target });
        bool Set(PlacedObject target, string key, EditorPropertyValue value) => (bool)set.Invoke(null, new object[] { target, key, value });
        bool Drag(PlacedObject target, string key, float x, float y)
        {
            object[] args = { target, key, x, y, false, null };
            return (bool)drag.Invoke(null, args) && Set(target, key, (EditorPropertyValue)args[5]);
        }
        EditorObjectGizmoSnapshot Shape(PlacedObject target) => (EditorObjectGizmoSnapshot)geometry.Invoke(null,
            new object[] { target, 0, Capture(target) });

        var late = Owner("LateNativeChoices");
        var lateData = new LateChoiceData(late);
        late.data = lateData;
        var choice = Capture(late).Single(p => p.Key.EndsWith(".choice"));
        Check(choice.Kind == EditorPropertyKind.Enum && choice.Options.Length == 0 && choice.IntegerValue == -1,
            "An unregistered native choice remains a selector that can refresh, without pretending the first entry is selected.");
        var registered = new LateChoice("RegisteredAfterCapture", true);
        choice = Capture(late).Single(p => p.Key == choice.Key);
        Check(choice.Options.SequenceEqual(new[] { registered.value }) && Set(late, choice.Key, new(EditorPropertyKind.Enum, integer: 0)) &&
            lateData.choice == registered, "Native choices refresh after registration without rebuilding the schema and edit the real enum member.");
        lateData.choice = new LateChoice("MissingAuthorChoice");
        choice = Capture(late).Single(p => p.Key == choice.Key);
        Check(choice.Options[choice.IntegerValue] == "MissingAuthorChoice" && lateData.choice.value == "MissingAuthorChoice",
            "A missing native choice remains displayed and is never silently mapped to the first registered option.");
        Check(!Set(late, choice.Key, new(EditorPropertyKind.Enum, integer: -1)) && lateData.choice.value == "MissingAuthorChoice",
            "An invalid enum selection cannot null out a reference-valued author field.");
        Check(Capture(late).Single(p => p.Key.EndsWith(".Computed")).Kind == EditorPropertyKind.ReadOnly,
            "A computed native scalar is not presented as a slider that cannot accept edits.");

        var extended = Owner("ExtendedRainbow");
        var extendedData = new ExtendedRainbowData(extended);
        extended.data = extendedData;
        var extendedProperties = Capture(extended);
        var depth = extendedProperties.Single(p => p.Key.EndsWith(".depth"));
        Check(extendedProperties.Any(p => p.Key.EndsWith(".update")) &&
            extendedProperties.Count(p => p.Key.StartsWith("builtin.rainbow.")) == 6 &&
            Set(extended, depth.Key, new(EditorPropertyKind.Float, x: .75f)) && extendedData.depth == .75f,
            "Structured replacement preserves independent same-named extension fields alongside all rainbow controls.");

        var cases = new[] {
            (PlacedObject.Type.LightSource, "colorType"), (PlacedObject.Type.LightFixture, "type"),
            (PlacedObject.Type.InsectGroup, "insectType"), (PlacedObject.Type.MultiplayerItem, "type"),
            (PlacedObject.Type.DataPearl, "pearlType"), (PlacedObject.Type.EnergySwirl, "colorType"),
            (PlacedObject.Type.LightBeam, "blinkType"), (PlacedObject.Type.FairyParticleSettings, "spriteType"),
            (PlacedObject.Type.PassiveCorruption, "eyeType"), (PlacedObject.Type.SpawnMigrationStream, "type"),
            (PlacedObject.Type.HideVoidSpawn, "type")
        };
        foreach (var entry in cases)
        {
            var target = new PlacedObject(entry.Item1, null) { pos = new Vector2(100, 200) };
            var property = Capture(target).Single(p => p.Key.EndsWith("." + entry.Item2));
            Check(property.Kind == EditorPropertyKind.Enum && property.Options.Length > 0,
                "The native " + entry.Item1 + " exposes its actual " + entry.Item2 + " choices.");
            Check(Set(target, property.Key, new(EditorPropertyKind.Enum, integer: property.Options.Length - 1)),
                "The native " + entry.Item1 + " accepts its selection through the registry.");
        }

        _ = SlugcatStats.Name.White;
        var tokens = new[] {
            new CollectToken.CollectTokenData(Owner("GoldToken"), false),
            new CollectToken.CollectTokenData(Owner("BlueToken"), true),
            new CollectToken.CollectTokenData(Owner("GreenToken"), false) { isGreen = true },
            new CollectToken.CollectTokenData(Owner("WhiteToken"), false) { isWhite = true },
            new CollectToken.CollectTokenData(Owner("RedToken"), false) { isRed = true }
        };
        _ = new MoreSlugcats.ChatlogData.ChatlogID("RainWrightTestChatlog", true);
        _ = new MultiplayerUnlocks.SlugcatUnlockID("RainWrightTestSlugcat", true);
        _ = new MultiplayerUnlocks.SafariUnlockID("RainWrightTestSafari", true);
        foreach (var token in tokens)
        {
            var target = Owner("TokenSelection"); target.data = token;
            var properties = Capture(target);
            var unlock = properties.Single(p => p.Key == "builtin.token.unlock");
            Check(unlock.Options.Length > 0 && !properties.Any(p => p.Key.EndsWith(".tokenString") || p.Key.EndsWith(".LevelUnlock")),
                "Each token exposes one applicable unlock selector, replacing inactive aliases.");
            int selected = unlock.Options.Length - 1;
            Check(Set(target, unlock.Key, new(EditorPropertyKind.Enum, integer: selected)) && token.tokenString == unlock.Options[selected],
                "The token-specific selector writes the native unlock property: " + unlock.DisplayName);
        }

        var sessionCurrent = typeof(DevToolSessionHub).GetField("current", flags);
        object originalCurrent = sessionCurrent.GetValue(null);
        try
        {
            var room = (Room)FormatterServices.GetUninitializedObject(typeof(Room));
            room.abstractRoom = (AbstractRoom)FormatterServices.GetUninitializedObject(typeof(AbstractRoom));
            room.abstractRoom.connections = new[] { 5, 8, 13 };
            var camera = (RoomCamera)FormatterServices.GetUninitializedObject(typeof(RoomCamera)); camera.room = room;
            var gameState = (RainWorldGame)FormatterServices.GetUninitializedObject(typeof(RainWorldGame)); gameState.cameras = new[] { camera };
            var owner = (DevInterface.DevUI)FormatterServices.GetUninitializedObject(typeof(DevInterface.DevUI)); owner.game = gameState;
            var session = (EditorSession)FormatterServices.GetUninitializedObject(typeof(EditorSession));
            typeof(EditorSession).GetField("<Owner>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, owner);
            sessionCurrent.SetValue(null, new WeakReference<EditorSession>(session));
            var egg = new PlacedObject(PlacedObject.Type.VoidSpawnEgg, null);
            var data = (PlacedObject.VoidSpawnEggData)egg.data; data.exit = 1;
            var properties = Capture(egg);
            var exit = properties.Single(p => p.Key == "builtin.voidSpawnEgg.exit");
            Check(exit.Options.SequenceEqual(new[] { "Exit 0", "Exit 1", "Exit 2" }) && exit.IntegerValue == 1 &&
                properties.Any(p => p.Key.EndsWith(".minRegen")) && properties.Any(p => p.Key.EndsWith(".maxRegen")),
                "Void Spawn Egg offers the current room's exits while retaining its inherited regeneration controls.");
            Check(Set(egg, exit.Key, new(EditorPropertyKind.Enum, integer: 2)) && data.exit == 2,
                "Selecting a room exit edits the native egg destination.");
            data.exit = 9;
            exit = Capture(egg).Single(p => p.Key == exit.Key);
            Check(exit.Options[exit.IntegerValue] == "Exit 9" && data.exit == 9,
                "An exit that vanished from the topology remains visible without rewriting its authored reference.");
        }
        finally { sessionCurrent.SetValue(null, originalCurrent); }

        bool originalWatcher = ModManager.Watcher;
        try
        {
            ModManager.Watcher = true;
            var target = Owner("FloatingDebris");
            var data = new Watcher.FloatingDebrisData(target);
            target.data = data;
            data.AddControlPointRight();
            var oldX = data.controlPointPosX;
            var oldDepth = data.controlPointDepthOffset;
            var properties = Capture(target);
            const string point = "builtin.floatingDebris.point.0.";
            Check(properties.Count(p => p.Key.StartsWith("builtin.floatingDebris.point.")) == 8 &&
                Shape(target).Handles.Length == 8 && Shape(target).Lines.Length == 1,
                "Floating debris retains both path points, scatter/depth/scale handles and the connecting line.");
            Check(Drag(target, point + "position", 80, 120) && data.controlPointPosX[0] == 80 && data.controlPointPosY[0] == 120 &&
                oldX[0] == -50, "A debris path point moves relative to its object without mutating a previous list.");
            Check(Drag(target, point + "scatter", 80, 195) && data.controlPointOffsetAmount[0] == .75f &&
                Drag(target, point + "depth", 130, 90) && data.controlPointDepthOffset[0] == .5f && oldDepth[0] == 0,
                "Debris offset handles convert from scene coordinates to the native hundred-pixel scale.");
            var oldExtra = data.controlPointExtraOffset;
            Check(Drag(target, point + "extra3", 105, 0) && data.controlPointExtraOffset[0].w == .25f && oldExtra[0].w == 0,
                "Debris extra offset handles edit the corresponding vector component without changing a previous list.");
            var shape = Shape(target);
            Check(shape.Handles.Single(h => h.Id == "property:" + point + "depth").X == 230 &&
                shape.Handles.Single(h => h.Id == "property:" + point + "depth").Y == 290,
                "A moved debris offset is reprojected around the moved control point, including the axis row offset.");
            var restored = new Watcher.FloatingDebrisData(target);
            restored.FromString(data.ToString());
            Check(restored.controlPointPosX[0] == 80 && restored.controlPointPosY[0] == 120 && restored.controlPointDepthOffset[0] == .5f,
                "Debris point and offset changes survive native serialization.");
        }
        finally { ModManager.Watcher = originalWatcher; }

        var broken = new BrokenInspector();
        ObjectInspectorRegistry.Register(broken, 2000);
        try
        {
            var method = typeof(ObjectInspectorRegistry).GetMethod("CaptureWithCoverage", flags);
            object[] args = { late, null };
            var properties = (EditorPropertySnapshot[])method.Invoke(null, args);
            Check(properties.Single().Key == "__compatibilityFailure" && properties[0].StringValue == "original inspector failure" &&
                !((ObjectInspectorCoverage)args[1]).IsComplete,
                "A failing inspector publishes a visible failure and cannot pretend its missing controls are complete.");
            Check(!((ObjectInspectorCoverage)typeof(ObjectInspectorRegistry).GetMethod("GetCoverage", flags)
                .Invoke(null, new object[] { late })).IsComplete,
                "The compatibility UI request remains available when an explicit inspector fails to capture.");
        }
        finally { ObjectInspectorRegistry.Unregister(broken); }
    }

    private sealed class LateChoice : ExtEnum<LateChoice>
    { public LateChoice(string value, bool register = false) : base(value, register) { } }
    private sealed class LateChoiceData : PlacedObject.Data
    {
        public LateChoice choice;
        public float Computed => .5f;
        public LateChoiceData(PlacedObject owner) : base(owner) { }
    }
    private sealed class ExtendedRainbowData : Rainbow.RainbowData
    {
        public float depth = 0f;
        public bool update = false;
        public ExtendedRainbowData(PlacedObject owner) : base(owner) { }
    }
    private sealed class BrokenInspector : IObjectInspectorAdapter
    {
        public bool CanInspect(PlacedObject target) => target?.data is LateChoiceData;
        public IReadOnlyList<EditorPropertySnapshot> Capture(PlacedObject target) => throw new InvalidOperationException("original inspector failure");
        public bool TrySetValue(PlacedObject target, string key, EditorPropertyValue value) => false;
    }
}
