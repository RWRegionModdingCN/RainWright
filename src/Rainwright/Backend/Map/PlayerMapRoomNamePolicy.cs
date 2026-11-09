using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Source-room naming compatibility fallback. Rain World offScreenDen on the
/// actual AbstractRoom remains authoritative in the game adapter.
/// </summary>
internal static class PlayerMapRoomNamePolicy
{
    internal static bool EquivalentOffscreenFileKeys(string a, string b) =>
        a?.StartsWith("OffscreenDen", StringComparison.OrdinalIgnoreCase) == true &&
        b?.StartsWith("OffscreenDen", StringComparison.OrdinalIgnoreCase) == true &&
        string.Equals(a.Replace("_", ""), b.Replace("_", ""), StringComparison.OrdinalIgnoreCase);
    internal static bool IsOffscreenDenName(string roomName)
    {
        if (string.IsNullOrWhiteSpace(roomName)) return false;
        string value = roomName.Trim();
        return value.Equals("OffscreenDen", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("OffscreenDen_", StringComparison.OrdinalIgnoreCase);
    }
}
