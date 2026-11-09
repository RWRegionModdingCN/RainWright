using System;
using System.Collections.Generic;
using System.Globalization;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Fail-closed recognition of vanilla's exactly eight-field map-config room record.
/// Extra fields can belong to third-party authoring extensions and must never be stripped
/// by the Player Map disabled-room filter.
/// </summary>
internal static class PlayerMapRoomRecordPolicy
{
    internal static bool TryParseVanillaRoomRecord(string line, out string roomName)
    {
        roomName = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        int colon = line.IndexOf(':');
        if (colon <= 0) return false;

        string key = line.Substring(0, colon).Trim();
        if (key.Length == 0) return false;
        string payload = line.Substring(colon + 1).Trim();
        string[] fields = payload.Split(new[] { "><" }, StringSplitOptions.None);
        if (fields.Length != 8) return false;

        // Exactly MapPage.SaveMapConfig's canonX/canonY/devX/devY,
        // layer, subregion, roomWidth and roomHeight.
        if (!IsFloat(fields[0]) || !IsFloat(fields[1]) ||
            !IsFloat(fields[2]) || !IsFloat(fields[3]) ||
            !int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int layer) ||
            layer < 0 || layer >= DryCycle.DevUI.DevTool.Map.MapRoomLayer.Count ||
            !int.TryParse(fields[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out _) ||
            !int.TryParse(fields[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return false;

        roomName = key;
        return true;
    }

    /// <summary>
    /// Filters only obsolete vanilla room rows. Caller supplies the live, case-insensitive
    /// room-name set from Rain World. Third-party extension lines are preserved verbatim.
    /// If no record is removed the original list is reused without allocating.
    /// </summary>
    internal static IReadOnlyList<string> FilterVanillaRoomRecords(
        IReadOnlyList<string> lines,
        ISet<string> visibleRoomNames)
    {
        if (lines == null) return null;
        if (visibleRoomNames == null)
            throw new ArgumentNullException(nameof(visibleRoomNames));

        List<string> result = null;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i] ?? string.Empty;
            bool remove = TryParseVanillaRoomRecord(line, out string name) &&
                          !visibleRoomNames.Contains(name);
            if (!remove)
            {
                result?.Add(line);
                continue;
            }

            if (result == null)
            {
                result = new List<string>(Math.Max(0, lines.Count - 1));
                for (int earlier = 0; earlier < i; earlier++)
                    result.Add(lines[earlier] ?? string.Empty);
            }
        }
        return result ?? lines;
    }

    private static bool IsFloat(string text) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
