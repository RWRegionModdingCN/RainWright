using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

// An editor-owned extension in the existing atomic map-config save. Keep placement records:
// hiding affects rendered output, and must not discard the positions needed to restore a room.
internal static class PlayerMapRoomVisibility
{
    private const string Key = "DryCycle_PlayerMap_HiddenRooms:";
    private const string CompatibleKey = "// RainWright_PlayerMap_HiddenRooms:";
    internal static bool IsRecord(string line) =>
        (line ?? string.Empty).TrimStart().StartsWith(Key, StringComparison.Ordinal) ||
        (line ?? string.Empty).TrimStart().StartsWith(CompatibleKey, StringComparison.Ordinal);

    internal static HashSet<string> Read(IEnumerable<string> lines)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (string line in lines)
        {
            if (!IsRecord(line)) continue;
            string trimmed = line.TrimStart();
            string value = trimmed.Substring(trimmed.StartsWith(Key, StringComparison.Ordinal) ? Key.Length : CompatibleKey.Length).Trim();
            if (!value.StartsWith("v1|", StringComparison.Ordinal))
                throw new FormatException("Unsupported Player Map room visibility record.");
            foreach (string encoded in value.Substring(3).Split(','))
            {
                if (encoded.Length == 0) continue;
                string name = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded));
                if (string.IsNullOrWhiteSpace(name))
                    throw new FormatException("A hidden Player Map room has no name.");
                names.Add(name);
            }
        }
        return names;
    }

    internal static string Write(IEnumerable<string> names)
    {
        string[] encoded = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => Convert.ToBase64String(Encoding.UTF8.GetBytes(name))).ToArray();
        return encoded.Length == 0 ? null : CompatibleKey + " v1|" + string.Join(",", encoded);
    }
}
