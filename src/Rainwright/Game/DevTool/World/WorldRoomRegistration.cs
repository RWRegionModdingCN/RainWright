using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DryCycle.DevUI.DevTool.Map.PlayerMap;

namespace DryCycle.DevUI.DevTool.World;

// Detached author-source discovery. The game thread alone merges and commits the result through
// WorldTextRegistry/WorldFileVariantStore; this worker never writes files or touches live rooms.
internal static class WorldRoomRegistration
{
    internal sealed class Candidate
    {
        internal string Name;
        internal int Exits;
        internal string Path;
        internal long Length;
        internal DateTime WriteTime;

        internal bool SourceUnchanged()
        {
            FileInfo info = new(Path);
            return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                info.Length == Length && info.LastWriteTimeUtc == WriteTime;
        }
    }
    internal sealed class Result
    {
        internal readonly List<Candidate> Rooms = new();
        internal readonly List<string> Warnings = new();
    }

    internal static Task<Result> Begin(string region, string worldPath, WorldDocument document)
    {
        string root = WorldModificationWorkspace.RegionModRoot;
        if (string.IsNullOrEmpty(root) || WorldModificationWorkspace.IsPatch(worldPath)) return null;
        string relative = "world/" + region.ToLowerInvariant() + "/world_" + region.ToLowerInvariant() + ".txt";
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string expected) ||
            !string.Equals(Path.GetFullPath(worldPath), expected, StringComparison.OrdinalIgnoreCase)) return null;
        string roomsRelative = "world/" + region.ToLowerInvariant() + "-rooms";
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, roomsRelative, out string folder) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, roomsRelative)) return null;
        HashSet<string> known = new(document.Rooms.Keys, StringComparer.OrdinalIgnoreCase);
        // Conditional ROOMS entries already register the geometry for another campaign. Never
        // manufacture an unconditional duplicate that bypasses the author's timeline rule.
        foreach (string key in document.Rooms.Keys)
        {
            int close = key.IndexOf(')');
            if (key.StartsWith("(", StringComparison.Ordinal) && close >= 0) known.Add(key.Substring(close + 1).Trim());
        }
        return Task.Run(() => FindMissing(region, folder, known));
    }

    internal static Result FindMissing(string region, string folder, HashSet<string> known)
    {
        Result result = new();
        if (!Directory.Exists(folder)) return result;
        string[] files = Directory.GetFiles(folder, "*.txt", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (string path in files)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (known.Contains(name) || !IsRoomName(region, name)) continue;
            try
            {
                FileInfo info = new(path);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > 32 * 1024 * 1024)
                    throw new InvalidDataException("Room source is redirected or exceeds 32 MiB.");
                string[] lines = File.ReadAllLines(path);
                RoomMapSource source = RoomMapTextDecoder.Parse(lines);
                int tiles = 0;
                foreach (char c in lines[11]) if (c == '|') tiles++;
                if (tiles != source.Tiles.Length) throw new InvalidDataException("Room tile data is incomplete or has extra tiles.");
                // This is the decoder used by map rendering and ShortcutMapper node ordering.
                // Exits precede dens; counting Exit terminal tiles does not mistake dens for exits.
                int exits = 0;
                foreach (SourceTile tile in source.Tiles) if (tile.Shortcut == 2) exits++;
                Candidate candidate = new() { Name = name, Exits = exits, Path = path, Length = info.Length, WriteTime = info.LastWriteTimeUtc };
                if (!candidate.SourceUnchanged()) throw new IOException("Room source changed while it was being read.");
                result.Rooms.Add(candidate);
                known.Add(name);
            }
            catch (Exception error) when (error is IOException || error is InvalidDataException || error is UnauthorizedAccessException || error is FormatException || error is OverflowException)
            { result.Warnings.Add("Room registration skipped '" + path + "': " + error); }
        }
        return result;
    }

    private static bool IsRoomName(string region, string name)
    {
        if (name.IndexOf("_settings", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        for (int i = 0; i < name.Length; i++)
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
        // WorldLoader.FindRoomFile derives the room directory from the prefix before '_'.
        // A differently prefixed room (including a gate) in this folder would register a room
        // the game cannot subsequently load. Gate sources belong in world/gates instead.
        return name.StartsWith(region + "_", StringComparison.OrdinalIgnoreCase);
    }
}
