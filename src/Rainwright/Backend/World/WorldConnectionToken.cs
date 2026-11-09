using System;
using System.Globalization;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>Community ConnectionExtensions suffix syntax, with read support for old RainWright prefixes.</summary>
internal static class WorldConnectionToken
{
    internal static bool TryParse(string token, out string roomName, out int targetNode)
    {
        roomName = token?.Trim() ?? string.Empty;
        targetNode = -1;
        if (roomName.Length == 0) return false;
        if (string.Equals(roomName, "DISCONNECTED", StringComparison.OrdinalIgnoreCase)) return true;
        int open = roomName.IndexOf('<');
        int close = roomName.IndexOf('>');
        if (open < 0 && close < 0) return true;
        // Accept a single complete suffix, or the legacy prefix, without swallowing malformed tokens.
        if (open < 0 || close <= open + 1 || roomName.IndexOf('<', open + 1) >= 0 || roomName.IndexOf('>', close + 1) >= 0 ||
            !int.TryParse(roomName.Substring(open + 1, close - open - 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int node) || node < 0) return false;
        string room;
        if (open == 0 && close < roomName.Length - 1) room = roomName.Substring(close + 1).Trim();
        else if (open > 0 && close == roomName.Length - 1) room = roomName.Substring(0, open).Trim();
        else return false;
        if (room.Length == 0 || string.Equals(room, "DISCONNECTED", StringComparison.OrdinalIgnoreCase)) return false;
        roomName = room;
        targetNode = node;
        return true;
    }

    internal static string Format(string roomName, int targetNode)
    {
        string room = string.IsNullOrWhiteSpace(roomName) ? "DISCONNECTED" : roomName.Trim();
        return targetNode < 0 || string.Equals(room, "DISCONNECTED", StringComparison.OrdinalIgnoreCase)
            ? room : room + "<" + targetNode.ToString(CultureInfo.InvariantCulture) + ">";
    }

    internal static string NormalizeLegacy(string token) =>
        token?.TrimStart().StartsWith("<", StringComparison.Ordinal) == true && TryParse(token, out string room, out int node)
            ? Format(room, node) : token;
}
