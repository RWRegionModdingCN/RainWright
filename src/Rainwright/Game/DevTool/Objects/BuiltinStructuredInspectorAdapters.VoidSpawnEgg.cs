using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Objects;

internal static partial class BuiltinStructuredInspectorAdapters
{
    private sealed partial class StructuredArrayInspector
    {
        private const string VoidSpawnEggExitKey = "builtin.voidSpawnEgg.exit";
        private static readonly string VoidSpawnEggExitNativeKey = "native." + typeof(PlacedObject.VoidSpawnEggData).FullName + ".exit";

        private static int[] VoidSpawnEggExits(PlacedObject.VoidSpawnEggData data)
        {
            int count = DevToolSessionHub.Current?.Room?.abstractRoom?.connections?.Length ?? 0;
            var exits = new List<int>(count + 1);
            for (int i = 0; i < count; i++) exits.Add(i);
            // Keep authored references visible even outside a live room or after topology changes.
            if (!exits.Contains(data.exit)) exits.Add(data.exit);
            return exits.ToArray();
        }

        private static void AppendVoidSpawnEggExit(List<EditorPropertySnapshot> result, PlacedObject.VoidSpawnEggData data)
        {
            int[] exits = VoidSpawnEggExits(data);
            string[] options = new string[exits.Length];
            for (int i = 0; i < exits.Length; i++) options[i] = "Exit " + exits[i];
            result.Add(new EditorPropertySnapshot
            {
                Key = VoidSpawnEggExitKey, DisplayName = "Exit", Group = "Void Spawn Egg", Source = "Rain World model",
                Kind = EditorPropertyKind.Enum, Options = options, IntegerValue = Array.IndexOf(exits, data.exit),
                StringValue = "Exit " + data.exit
            });
        }

        private static bool TrySetVoidSpawnEggExit(PlacedObject.VoidSpawnEggData data, string key, EditorPropertyValue value)
        {
            if (key != VoidSpawnEggExitKey || value.Kind != EditorPropertyKind.Enum) return false;
            int[] exits = VoidSpawnEggExits(data);
            if (value.Integer < 0 || value.Integer >= exits.Length) return false;
            data.exit = exits[value.Integer];
            return true;
        }
    }
}
