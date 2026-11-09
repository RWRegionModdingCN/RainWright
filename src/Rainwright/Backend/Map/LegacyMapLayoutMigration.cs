using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using DryCycle.DevUI.DevTool.World;
using DryCycle.DevUI.DevTool.Persistence;

namespace DryCycle.DevUI.DevTool.Map;

/// <summary>Converts the retired layout into ordinary developer-coordinate columns, preserving other records.</summary>
internal static class LegacyMapLayoutMigration
{
    internal const string Header = "# DryCycle DevTool map position overrides v1";
    internal static Dictionary<string, string[]> ReadPositions(string layout)
    {
        var positions = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (layout.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith("<", StringComparison.Ordinal))
        {
            using var text = new StringReader(layout.TrimStart('\uFEFF'));
            using var reader = XmlReader.Create(text, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            XElement root = XDocument.Load(reader).Root;
            if (root.Name != "RainWrightMapLayout" || (string)root.Attribute("version") != "1" || root.Elements().Any(e => e.Name != "Room") || root.Attributes().Any(a => a.Name != "version"))
                throw new InvalidDataException("Unrecognized map-layout XML schema.");
            foreach (XElement room in root.Elements("Room"))
            {
                string name = (string)room.Attribute("name");
                string x = (string)room.Attribute("x"), y = (string)room.Attribute("y");
                if (string.IsNullOrWhiteSpace(name) || !Finite(x) || !Finite(y) || positions.ContainsKey(name) || room.HasElements || room.Attributes().Any(a => a.Name != "name" && a.Name != "x" && a.Name != "y"))
                    throw new InvalidDataException("Invalid or duplicate XML layout room: " + name);
                positions.Add(name, new[] { x, y });
            }
            return positions;
        }
        using (var input = new StringReader(layout))
        {
            if (input.ReadLine() != Header) throw new InvalidDataException("Unrecognized legacy layout header.");
            for (string line; (line = input.ReadLine()) != null;)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                int split = line.IndexOf(": ", StringComparison.Ordinal);
                string[] values = split > 0 ? line.Substring(split + 2).Split(new[] { "><" }, StringSplitOptions.None) : Array.Empty<string>();
                if (values.Length != 2 || !Finite(values[0]) || !Finite(values[1])) throw new InvalidDataException("Invalid legacy layout record: " + line);
                if (positions.ContainsKey(line.Substring(0, split))) throw new InvalidDataException("Duplicate legacy layout room: " + line.Substring(0, split));
                positions.Add(line.Substring(0, split), values);
            }
        }
        return positions;
    }

    internal static string XmlPath(string mapPath) => Path.Combine(Path.GetDirectoryName(mapPath), "rainwright", "layouts", Path.GetFileNameWithoutExtension(mapPath) + "_rainwright_layout.xml");

    internal static string EncodeXml(string text)
    {
        var root = new XElement("RainWrightMapLayout", new XAttribute("version", "1"));
        foreach (var position in ReadPositions(text).OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
            root.Add(new XElement("Room", new XAttribute("name", position.Key), new XAttribute("x", position.Value[0]), new XAttribute("y", position.Value[1])));
        return root.ToString();
    }

    internal static void MigrateFile(string legacyPath, string target, Action<string> authorize)
    {
        if (!File.Exists(legacyPath)) return;
        byte[] bytes = File.ReadAllBytes(legacyPath);
        string legacy = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (!legacy.StartsWith(Header + "\n", StringComparison.Ordinal) && !legacy.StartsWith(Header + "\r\n", StringComparison.Ordinal))
            throw new InvalidDataException("Unknown layout file was preserved: " + legacyPath);
        string converted = EncodeXml(legacy);
        byte[] expected = WorldVariantFileTransaction.ReadExisting(target);
        if (expected != null && EncodeXml(new UTF8Encoding(false, true).GetString(expected)) != converted)
            throw new InvalidDataException("Historical and XML layouts conflict; both were preserved: " + legacyPath);
        var writes = new List<WorldVariantFileWrite>();
        if (expected == null) writes.Add(new WorldVariantFileWrite { Path = target, Text = converted, Expected = null });
        writes.Add(new WorldVariantFileWrite { Path = legacyPath, Text = null, Expected = bytes,
            BackupPath = legacyPath + ".rainwright-migrated-" + Guid.NewGuid().ToString("N") + ".bak" });
        WorldVariantFileTransaction.Commit(writes, authorize);
        EditorBackupFiles.Unhide(target);
    }

    internal static void MigrateRegion(string directory, string region, Action<string> authorize, Action<Exception> onError = null)
    {
        foreach (string file in Directory.GetFiles(directory, "*.txt", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            string stem = "map_" + region;
            if (!name.StartsWith(stem, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (name.EndsWith(".drycycle-layout.txt", StringComparison.OrdinalIgnoreCase))
                {
                    string map = file.Substring(0, file.Length - ".drycycle-layout.txt".Length) + ".txt";
                    MigrateFile(file, XmlPath(map), authorize);
                }
                else if (name.Equals(stem + ".txt", StringComparison.OrdinalIgnoreCase) || name.StartsWith(stem + "-", StringComparison.OrdinalIgnoreCase))
                {
                    // Only rewrite our known private visibility records, retaining every standard
                    // room coordinate and unknown author directive unchanged.
                    byte[] sourceBytes = File.ReadAllBytes(file);
                    string source = new UTF8Encoding(false, true).GetString(sourceBytes).TrimStart('\uFEFF');
                    if (!source.Split('\n').Any(line => line.TrimStart().StartsWith("DryCycle_PlayerMap_HiddenRooms:", StringComparison.Ordinal))) continue;
                    string converted = Convert(source, Header + "\n", out _);
                    if (source != converted) WorldVariantFileTransaction.Commit(new[] { new WorldVariantFileWrite { Path = file, Text = converted, Expected = sourceBytes } }, authorize);
                }
            }
            catch (Exception error) { if (onError == null) throw; onError(error); }
        }
        ApplyGeneratedFileVisibility(directory, authorize, onError);
        ApplyGeneratedFileVisibility(Path.GetDirectoryName(directory), authorize, onError);
        string rooms = Path.Combine(Path.GetDirectoryName(directory), region.ToLowerInvariant() + "-rooms");
        if (Directory.Exists(rooms) && (File.GetAttributes(rooms) & FileAttributes.ReparsePoint) == 0) ApplyGeneratedFileVisibility(rooms, authorize, onError);
    }

    private static void ApplyGeneratedFileVisibility(string directory, Action<string> authorize, Action<Exception> onError)
    {
        foreach (string file in Directory.GetFiles(directory, "*.bak", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(file) & FileAttributes.Hidden) != 0) continue;
            try { authorize(file); EditorBackupFiles.Hide(file); }
            catch (Exception error) { if (onError == null) throw; onError(error); }
        }
        string owned = Path.Combine(directory, "rainwright");
        if (Directory.Exists(owned)) RestoreOwnedDirectory(owned, authorize, onError);
    }

    private static void RestoreOwnedDirectory(string directory, Action<string> authorize, Action<Exception> onError)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
        foreach (string file in Directory.GetFiles(directory))
        {
            bool backup = file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
            if (!backup && !file.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) continue;
            bool hidden = (File.GetAttributes(file) & FileAttributes.Hidden) != 0;
            if (hidden == backup) continue;
            try
            {
                authorize(file);
                if (backup) EditorBackupFiles.Hide(file);
                else EditorBackupFiles.Unhide(file);
            }
            catch (Exception error) { if (onError == null) throw; onError(error); }
        }
        foreach (string child in Directory.GetDirectories(directory)) RestoreOwnedDirectory(child, authorize, onError);
    }

    internal static string Convert(string map, string layout, out int restored)
    {
        var positions = ReadPositions(layout);
        string newline = map.Contains("\r\n") ? "\r\n" : "\n";
        bool terminal = map.EndsWith("\n", StringComparison.Ordinal);
        var output = new List<string>(); restored = 0;
        using (var input = new StringReader(map))
            for (string line; (line = input.ReadLine()) != null;)
            {
                if (PlayerMap.PlayerMapRoomVisibility.IsRecord(line))
                {
                    var hidden = PlayerMap.PlayerMapRoomVisibility.Read(new[] { line });
                    string compatible = PlayerMap.PlayerMapRoomVisibility.Write(hidden);
                    if (compatible != null) output.Add(compatible);
                    continue;
                }
                int split = line.IndexOf(": ", StringComparison.Ordinal);
                string key = split > 0 ? line.Substring(0, split) : "";
                string matched = positions.ContainsKey(key) ? key : null;
                if (matched == null && key.StartsWith("OffscreenDen", StringComparison.OrdinalIgnoreCase))
                    foreach (string candidate in positions.Keys)
                        if (PlayerMap.PlayerMapRoomNamePolicy.EquivalentOffscreenFileKeys(key, candidate))
                        { if (matched != null) throw new InvalidDataException("Ambiguous offscreen layout aliases."); matched = candidate; }
                if (matched != null && positions.TryGetValue(matched, out string[] position))
                {
                    string[] fields = line.Substring(split + 2).Split(new[] { "><" }, StringSplitOptions.None);
                    if (fields.Length < 6 || !Finite(fields[0]) || !Finite(fields[1])) throw new InvalidDataException("Cannot migrate layout into a nonstandard map room: " + line.Substring(0, split));
                    fields[2] = position[0]; fields[3] = position[1];
                    line = line.Substring(0, split + 2) + string.Join("><", fields); restored++;
                    positions.Remove(matched);
                }
                output.Add(line);
            }
        // An omitted room is not assumed deleted: keep the original auxiliary file for manual recovery.
        if (positions.Count > 0) throw new InvalidDataException("Map lacks layout rooms: " + string.Join(", ", positions.Keys));
        return string.Join(newline, output) + (terminal ? newline : "");
    }
    private static bool Finite(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && !float.IsNaN(value) && !float.IsInfinity(value);
}
