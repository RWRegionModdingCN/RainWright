using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Final map-config room-record contract filter.
///
/// Vanilla MapPage.SaveMapConfig rewrites room records from the current RoomPanel set and omits every
/// World.DisabledMapRooms entry. The rebuilt config pipeline intentionally preserves unknown extension
/// lines, so an old room record could otherwise survive after that room becomes conditionally hidden,
/// is renamed, or is removed from the region. Immediately before the single atomic write, recognize
/// only the exact vanilla 8-field room-record shape and retain it only for a current visible RoomPanel.
/// Unknown/third-party records are left untouched.
/// </summary>
internal static class PlayerMapDisabledConfigFilter
{
    private static bool enabled;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        logger?.LogInfo("Player Map active room-record config contract filter enabled through direct save calls; no self-detour attached.");
    }

    internal static void Disable() => enabled = false;

    internal static IReadOnlyList<string> Filter(string target, IReadOnlyList<string> lines)
    {
        if (!enabled || lines == null || !TryGetActiveMap(target, out MapPage page))
            return lines;

        HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);
        if (page.world?.DisabledMapRooms != null)
        {
            for (int i = 0; i < page.world.DisabledMapRooms.Count; i++)
            {
                string name = page.world.DisabledMapRooms[i];
                if (!string.IsNullOrWhiteSpace(name)) disabled.Add(name.Trim());
            }
        }

        HashSet<string> visibleRooms = new(StringComparer.OrdinalIgnoreCase);
        if (page.subNodes != null)
        {
            for (int i = 0; i < page.subNodes.Count; i++)
            {
                if (page.subNodes[i] is not RoomPanel panel || panel.roomRep?.room == null) continue;
                string name = panel.roomRep.room.name;
                if (!string.IsNullOrWhiteSpace(name) && !disabled.Contains(name))
                    visibleRooms.Add(name);
            }
        }

        return PlayerMapRoomRecordPolicy.FilterVanillaRoomRecords(lines, visibleRooms);
    }

    private static bool TryGetActiveMap(string target, out MapPage page)
    {
        page = DevToolSessionHub.Current?.Owner?.activePage as MapPage;
        if (page == null || string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(page.filePath))
            return false;
        return string.Equals(
            NormalizePath(target),
            NormalizePath(page.filePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string value) =>
        (value ?? string.Empty).Replace('\\', '/').Trim();

}
