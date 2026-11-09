using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map;

/// <summary>Read-only boundary: vanilla reads standard TXT, then owned layout XML overrides developer positions.</summary>
internal static class NativeMapLayoutCompatibility
{
    private static bool enabled;

    internal static void Enable()
    {
        if (enabled) return;
        enabled = true;
        On.DevInterface.MapPage.LoadMapConfig += MapPage_LoadMapConfig;
    }

    internal static void Disable()
    {
        if (!enabled) return;
        On.DevInterface.MapPage.LoadMapConfig -= MapPage_LoadMapConfig;
        enabled = false;
    }

    internal static string ResolvePath(string region, string mapPath)
    {
        if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(mapPath)) return null;
        string stem = Path.GetFileNameWithoutExtension(mapPath);
        if (!stem.StartsWith("map_", StringComparison.OrdinalIgnoreCase)) return null;
        // Resolve only the requested region/map. AssetManager owns active-Mod ordering;
        // there is no directory enumeration or editor dependency at this read boundary.
        string path = AssetManager.ResolveFilePath("world/" + region.ToLowerInvariant() +
            "/rainwright/layouts/" + stem + "_rainwright_layout.xml");
        return File.Exists(path) ? path : null;
    }

    private static void MapPage_LoadMapConfig(On.DevInterface.MapPage.orig_LoadMapConfig orig, MapPage self)
    {
        orig(self);
        try
        {
            string path = ResolvePath(self?.world?.name, self?.filePath);
            if (path == null || self.subNodes == null) return;
            var positions = LegacyMapLayoutMigration.ReadPositions(File.ReadAllText(path));
            var byName = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            string Key(string name) => name.StartsWith("OffscreenDen", StringComparison.OrdinalIgnoreCase) ? name.Replace("_", "") : name;
            foreach (var entry in positions)
            {
                string key = Key(entry.Key);
                if (byName.ContainsKey(key)) throw new InvalidDataException("Duplicate map layout alias: " + entry.Key);
                byName.Add(key, entry.Value);
            }
            var changes = new Dictionary<RoomPanel, Vector2>();
            foreach (var node in self.subNodes)
            {
                if (node is not RoomPanel panel || string.IsNullOrWhiteSpace(panel.roomRep?.room?.name)) continue;
                if (byName.TryGetValue(Key(panel.roomRep.room.name), out var coordinates)) changes.Add(panel, new Vector2(
                    float.Parse(coordinates[0], CultureInfo.InvariantCulture), float.Parse(coordinates[1], CultureInfo.InvariantCulture)));
            }
            // Validate the complete overlay before touching a panel. Unknown/deleted room records
            // cannot prevent existing rooms from loading, and the author file is never rewritten.
            foreach (var change in changes) change.Key.devPos = change.Value;
        }
        catch (Exception error)
        {
            EditorDiagnostics.Warning("Map layout XML could not be loaded; standard map remains active.", error);
        }
    }
}
