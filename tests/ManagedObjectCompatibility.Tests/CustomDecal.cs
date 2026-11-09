using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DryCycle.DevUI.DevTool.Objects;
using UnityEngine;

internal static partial class Program
{
    private static void VerifyCustomDecal(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var registryCapture = typeof(ObjectInspectorRegistry).GetMethod("Capture", flags);
        var registryDrag = typeof(ObjectInspectorRegistry).GetMethod("TryBuildGizmoValue", flags);
        var registrySet = typeof(ObjectInspectorRegistry).GetMethod("TrySetValue", flags);
        var gizmoCapture = assembly.GetType("DryCycle.DevUI.DevTool.Objects.NativeObjectGizmoPresentation", true)
            .GetMethod("Capture", flags);
        var builtin = assembly.GetType("DryCycle.DevUI.DevTool.Objects.BuiltinStructuredInspectorAdapters", true);
        builtin.GetMethod("Enable", flags).Invoke(null, null);

        string root = Path.Combine(Path.GetTempPath(), "rainwright-decal-" + Guid.NewGuid().ToString("N"));
        string originalRoot = RWCustom.Custom.rootFolderDirectory;
        var originalMods = ModManager.ActiveMods;
        var target = Owner("CustomDecal");
        target.type = PlacedObject.Type.CustomDecal;
        var data = new PlacedObject.CustomDecalData(target) { imageName = "missing-author-image" };
        target.data = data;
        EditorPropertySnapshot[] Capture() => (EditorPropertySnapshot[])registryCapture.Invoke(null, new object[] { target });
        bool Set(string key, EditorPropertyValue value) => (bool)registrySet.Invoke(null, new object[] { target, key, value });
        try
        {
            // Real AssetManager enumeration against isolated fixtures: never write game assets.
            Directory.CreateDirectory(Path.Combine(root, "decals"));
            string modPath = Path.Combine(root, "test-mod");
            Directory.CreateDirectory(Path.Combine(modPath, "decals"));
            File.WriteAllText(Path.Combine(root, "decals", "base-image.PNG"), "");
            File.WriteAllText(Path.Combine(root, "decals", "ignored.txt"), "");
            File.WriteAllText(Path.Combine(modPath, "decals", "mod-image.png"), "");
            File.WriteAllText(Path.Combine(modPath, "decals", "base-image.png"), "");
            RWCustom.Custom.rootFolderDirectory = root;
            var mod = (ModManager.Mod)FormatterServices.GetUninitializedObject(typeof(ModManager.Mod));
            mod.path = modPath;
            ModManager.ActiveMods = new List<ModManager.Mod> { mod };

            string before = data.ToString();
            var properties = Capture();
            var image = properties.Single(p => p.Key == "builtin.customDecal.imageName");
            Check(image.Kind == EditorPropertyKind.Enum && image.Options.SequenceEqual(new[] {
                "base-image", "missing-author-image", "mod-image" }),
                "Custom Decal lists PNG assets from vanilla and active mods, without duplicate image names.");
            Check(image.Options[image.IntegerValue] == data.imageName && data.ToString() == before,
                "Capturing a missing authored image preserves its selected value and all author data.");
            Check(!properties.Any(p => p.Key.StartsWith("native.") && p.Key.EndsWith(".imageName")),
                "The resource picker replaces the raw image name row.");
            Check(Set(image.Key, new(EditorPropertyKind.Enum, integer: Array.IndexOf(image.Options, "mod-image"))) &&
                data.imageName == "mod-image", "Selecting a mod image edits the actual CustomDecalData through the registry.");
            Check(!Set(image.Key, new(EditorPropertyKind.Enum, integer: -1)) &&
                !Set(image.Key, new(EditorPropertyKind.Enum, integer: 9999)) && data.imageName == "mod-image",
                "Invalid decal selections cannot replace the authored image.");

            properties = Capture();
            var shape = (EditorObjectGizmoSnapshot)gizmoCapture.Invoke(null, new object[] { target, 0, properties });
            var corners = properties.Where(p => p.Key.Contains(".handles[")).ToArray();
            Check(corners.Length == 3 && shape.Handles.Length == 3 && shape.Lines.Length == 4,
                "Custom Decal retains three draggable corners and the four-sided outline.");
            var originalHandles = (Vector2[])data.handles.Clone();
            var sharedHandles = data.handles;
            for (int i = 0; i < 3; i++)
            {
                object[] arguments = { target, corners[i].Key, 70f + i, 90f + i, false, null };
                Check((bool)registryDrag.Invoke(null, arguments) && Set(corners[i].Key, (EditorPropertyValue)arguments[5]),
                    "Custom Decal corner drag reaches the winning structured inspector: " + i);
                Check(data.handles[i].x == 70f + i && data.handles[i].y == 90f + i &&
                    sharedHandles[i].x == originalHandles[i].x && sharedHandles[i].y == originalHandles[i].y,
                    "Dragging edits the relative corner and preserves the previous handle array for history: " + i);
            }
            var restored = new PlacedObject.CustomDecalData(target);
            restored.FromString(data.ToString());
            Check(restored.imageName == "mod-image" && restored.handles.SequenceEqual(data.handles),
                "The selected image and every moved corner survive the native serialization round trip.");

            RWCustom.Custom.rootFolderDirectory = null;
            properties = Capture();
            Check(properties.Any(p => p.Key == image.Key && p.Kind == EditorPropertyKind.ReadOnly) &&
                properties.Any(p => p.Key.EndsWith(".imageName") && p.Kind == EditorPropertyKind.String) &&
                properties.Count(p => p.Key.Contains(".handles[")) == 3,
                "An image enumeration failure remains visible and retains manual image editing and all geometry.");
        }
        finally
        {
            RWCustom.Custom.rootFolderDirectory = originalRoot;
            ModManager.ActiveMods = originalMods;
            // The absolute fixture root is owned by this test and cannot escape the temp directory.
            Directory.Delete(root, true);
        }
    }
}
