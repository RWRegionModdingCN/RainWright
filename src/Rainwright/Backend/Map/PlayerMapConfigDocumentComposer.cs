using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Pure Map Config author-document composition. The Rain World adapter freezes
/// game state and produces known serialized blocks; this composer merges them
/// without touching disk or losing unknown third-party author records.
/// </summary>
internal static class PlayerMapConfigDocumentComposer
{
    internal static IReadOnlyList<string> Compose(
        IReadOnlyList<string> source,
        IReadOnlyDictionary<string, string> rooms,
        IReadOnlyList<string> defaultMaterials,
        IReadOnlyList<string> connections,
        IReadOnlyList<string> streams,
        bool replaceStreams,
        string visibilityRecord,
        bool replaceConnections = true)
    {
        if (rooms == null) throw new ArgumentNullException(nameof(rooms));
        if (visibilityRecord != null && !PlayerMapRoomVisibility.IsRecord(visibilityRecord))
            throw new ArgumentException("A valid hidden-room record is required.", nameof(visibilityRecord));

        source ??= Array.Empty<string>();
        defaultMaterials ??= Array.Empty<string>();
        connections ??= Array.Empty<string>();
        streams ??= Array.Empty<string>();

        List<string> output = new(source.Count + rooms.Count + defaultMaterials.Count +
                                  connections.Count + streams.Count + 2);
        HashSet<string> writtenRooms = new(StringComparer.OrdinalIgnoreCase);
        int lastRoomOutputIndex = -1;

        for (int i = 0; i < source.Count; i++)
        {
            string line = source[i] ?? string.Empty;
            string trimmed = line.TrimStart();

            // These editor-owned records are regenerated from frozen authoring
            // state. Unknown/extended records remain byte-for-byte unchanged.
            if (PlayerMapRoomVisibility.IsRecord(line) ||
                trimmed.StartsWith("Def_Mat:", StringComparison.OrdinalIgnoreCase) ||
                (replaceConnections && trimmed.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)) ||
                (replaceStreams &&
                 (trimmed.StartsWith("SpawnMigrationStream:", StringComparison.OrdinalIgnoreCase) ||
                  trimmed.StartsWith("SpawnMigrationStreamMidpoint:", StringComparison.OrdinalIgnoreCase))))
                continue;

            // Never overwrite a third-party extension row merely because its
            // key happens to match a room. Only exact vanilla records are ours.
            bool isRoom = PlayerMapRoomRecordPolicy.TryParseVanillaRoomRecord(line, out string name);
            if (!isRoom && trimmed.StartsWith("OffscreenDen", StringComparison.OrdinalIgnoreCase))
                isRoom = PlayerMapRoomRecordPolicy.TryParseVanillaRoomRecord(line + "><0><0", out name);
            string replacement = null;
            if (isRoom && !rooms.TryGetValue(name, out replacement) && name.StartsWith("OffscreenDen", StringComparison.OrdinalIgnoreCase))
                foreach (var room in rooms) if (PlayerMapRoomNamePolicy.EquivalentOffscreenFileKeys(name, room.Key)) { name = room.Key; replacement = room.Value; break; }
            if (isRoom && replacement != null)
            {
                // Author files can contain duplicate case-insensitive vanilla
                // rows after third-party edits or interrupted migrations.
                // Emit exactly one authoritative replacement for each room;
                // retain separate third-party extension records untouched.
                if (writtenRooms.Add(name))
                {
                    output.Add(replacement);
                    lastRoomOutputIndex = output.Count - 1;
                }
                continue;
            }

            output.Add(line);
        }

        List<string> missingRooms = new();
        foreach (KeyValuePair<string, string> item in rooms)
            if (!writtenRooms.Contains(item.Key))
                missingRooms.Add(item.Value);
        missingRooms.Sort(StringComparer.Ordinal);

        int insertion = lastRoomOutputIndex >= 0 ? lastRoomOutputIndex + 1 : 0;
        InsertBlock(output, ref insertion, missingRooms);
        InsertBlock(output, ref insertion, defaultMaterials);
        InsertBlock(output, ref insertion, connections);
        if (replaceStreams && streams.Count > 0)
        {
            output.Insert(insertion++, string.Empty);
            InsertBlock(output, ref insertion, streams);
        }

        if (!string.IsNullOrEmpty(visibilityRecord)) output.Add(visibilityRecord);
        return output;
    }

    private static void InsertBlock(
        List<string> output, ref int insertion, IReadOnlyList<string> block)
    {
        if (block == null || block.Count == 0) return;
        // A single InsertRange shifts the suffix once; repeated Insert would be
        // quadratic with thousands of rooms, connections or stream midpoints.
        output.InsertRange(insertion, block);
        insertion += block.Count;
    }
}
