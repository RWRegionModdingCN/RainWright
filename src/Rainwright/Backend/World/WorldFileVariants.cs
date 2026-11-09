using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>Lossless projections: ordinary author text is always independently usable by community tools.</summary>
internal sealed class WorldFileVariants
{
    internal const string Header = "// RainWright world variant v1";
    private const string TopologyKey = "// RainWright topology v1: ";
    private const string DryCycleKey = "// RainWright DryCycle v1: ";
    internal string Standard, Extended;
    internal bool RequiresExtension => Standard != Extended;

    internal static WorldFileVariants Project(string text)
    {
        var full = WorldDocument.Parse(text);
        foreach (var pair in full.Rooms)
        {
            var room = pair.Value;
            for (int node = 0; node < room.Connections.Count; node++)
            {
                if (!WorldConnectionToken.TryParse(room.Connections[node], out string target, out int exit) || exit < 0) continue;
                if (!full.TryGetRoom(target, out var other) || exit >= other.Connections.Count)
                    throw new InvalidDataException("Invalid exact connection: " + room.Name + ":" + node + " -> " + target + ":" + exit);
                // Exactly one reciprocal exit on either side is representable without an extension.
                if (CountTargets(room, target) == 1 && CountTargets(other, room.Name) == 1 &&
                    WorldConnectionToken.TryParse(other.Connections[exit], out string back, out int backExit) &&
                    back.Equals(room.Name, StringComparison.OrdinalIgnoreCase) && (backExit < 0 || backExit == node))
                    full.TrySetConnection(room.Name, node, target);
                else full.TrySetConnection(room.Name, node, WorldConnectionToken.Format(target, exit));
            }
        }
        string extended = full.Serialize();
        var standard = WorldDocument.Parse(extended);
        foreach (var pair in standard.Rooms)
            for (int node = 0; node < pair.Value.Connections.Count; node++)
                if (WorldConnectionToken.TryParse(pair.Value.Connections[node], out string target, out int exit) && exit >= 0)
                    standard.TrySetConnection(pair.Key, node, target);
        return new WorldFileVariants { Standard = standard.Serialize(), Extended = extended };
    }

    private static int CountTargets(WorldRoomRecord room, string name)
    {
        int count = 0;
        foreach (string token in room.Connections)
            if (WorldConnectionToken.TryParse(token, out string target, out _) && target.Equals(name, StringComparison.OrdinalIgnoreCase)) count++;
        return count;
    }

    internal static string Encode(string world, string topology, string dryCycle = null)
    {
        var root = new XElement("RainWrightWorld", new XAttribute("version", "2"), new XElement("World", world));
        if (!string.IsNullOrEmpty(topology)) root.Add(new XElement("Topology", topology));
        if (!string.IsNullOrEmpty(dryCycle)) root.Add(new XElement("DryCycle", dryCycle));
        var output = new StringBuilder();
        // Entitize CR instead of normalizing it: embedded author text and optional payloads must
        // roundtrip exactly, including CRLF, '<', '&' and non-ASCII names.
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings { OmitXmlDeclaration = true, Indent = true, NewLineHandling = NewLineHandling.Entitize }))
            root.WriteTo(writer);
        return output.ToString();
    }

    internal static bool IsLegacy(string text) => text?.TrimStart('\uFEFF').StartsWith(Header + "\n", StringComparison.Ordinal) == true || text?.TrimStart('\uFEFF').StartsWith(Header + "\r\n", StringComparison.Ordinal) == true;
    internal static bool IsOwned(string text)
    {
        if (IsLegacy(text)) return true;
        if (string.IsNullOrWhiteSpace(text) || !text.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith("<", StringComparison.Ordinal)) return false;
        try { ReadXml(text); return true; }
        catch (XmlException) { return false; }
        catch (InvalidDataException) { return false; }
    }
    private static XElement ReadXml(string text)
    {
        using var input = new StringReader(text.TrimStart('\uFEFF'));
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        XElement root = XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root;
        if (root.Name != "RainWrightWorld" || (string)root.Attribute("version") != "2" || root.Elements("World").Count() != 1 ||
            root.Attributes().Any(a => a.Name != "version") ||
            root.Elements().Any(e => e.Name != "World" && e.Name != "Topology" && e.Name != "DryCycle") ||
            root.Elements("Topology").Count() > 1 || root.Elements("DryCycle").Count() > 1 || root.Elements().Any(e => e.HasElements || e.HasAttributes))
            throw new InvalidDataException("Unsupported RainWright XML variant; original file was preserved.");
        return root;
    }
    internal static string WorldText(string text)
    {
        if (!IsLegacy(text)) return ReadXml(text).Element("World").Value;
        text = text.TrimStart('\uFEFF');
        string newline = WorldDocument.Parse(text).NewLine;
        using var input = new StringReader(text); input.ReadLine();
        var lines = new List<string>();
        for (string line; (line = input.ReadLine()) != null;)
            if (!line.StartsWith(TopologyKey, StringComparison.Ordinal) && !line.StartsWith(DryCycleKey, StringComparison.Ordinal)) lines.Add(line);
        return string.Join(newline, lines) + (text.EndsWith("\n", StringComparison.Ordinal) ? newline : "");
    }
    internal static string Reencode(string text) => Encode(WorldText(text), Topology(text), DryCycle(text));
    internal static string Topology(string text) => IsLegacy(text) ? Metadata(text, TopologyKey) : ReadXml(text).Element("Topology")?.Value;
    internal static string DryCycle(string text) => IsLegacy(text) ? Metadata(text, DryCycleKey) : ReadXml(text).Element("DryCycle")?.Value;
    private static string Metadata(string text, string prefix)
    {
        if (!IsOwned(text)) throw new InvalidDataException("Unrecognized RainWright variant header; original file was preserved.");
        using var input = new StringReader(text);
        for (string line; (line = input.ReadLine()) != null;)
            if (line.StartsWith(prefix, StringComparison.Ordinal)) return new UTF8Encoding(false, true).GetString(Convert.FromBase64String(line.Substring(prefix.Length)));
        return null;
    }

    internal static string Overlay(string standardText, string variant)
    {
        if (!IsOwned(variant)) throw new InvalidDataException("Unrecognized RainWright variant; original files were preserved.");
        var standard = WorldDocument.Parse(standardText);
        var extra = WorldDocument.Parse(WorldText(variant));
        foreach (var pair in extra.Rooms)
            for (int node = 0; node < pair.Value.Connections.Count; node++)
            {
                if (!WorldConnectionToken.TryParse(pair.Value.Connections[node], out string room, out int exit) || exit < 0) continue;
                if (!standard.TryGetConnection(pair.Key, node, out string current) ||
                    !WorldConnectionToken.TryParse(current, out string target, out int currentExit) || !target.Equals(room, StringComparison.OrdinalIgnoreCase) || currentExit >= 0 && currentExit != exit ||
                    !standard.TryGetRoom(room, out var destination) || exit >= destination.Connections.Count)
                    throw new InvalidDataException("RainWright extension conflicts with the current world source at " + pair.Key + ":" + node + ". Both files were preserved.");
                standard.TrySetConnection(pair.Key, node, WorldConnectionToken.Format(room, exit));
            }
        // Creature data, room tags, comments and every other standard edit always come from the current base.
        return standard.Serialize();
    }
}

/// <summary>Verified content conversion, followed by backed-up retirement of old TXT names.</summary>
internal static class WorldVariantFormatMigration
{
    internal static void Migrate(string standard, string target, IEnumerable<string> legacyPaths, Action<string> authorize)
    {
        byte[] original = WorldVariantFileTransaction.ReadExisting(target);
        string selected = original == null ? null : new UTF8Encoding(false, true).GetString(original);
        if (selected != null && !WorldFileVariants.IsOwned(selected)) throw new InvalidDataException("Unrecognized variant: " + target);
        var legacy = new List<WorldVariantFileWrite>();
        foreach (string path in legacyPaths)
        {
            byte[] bytes = WorldVariantFileTransaction.ReadExisting(path);
            if (bytes == null) continue;
            string text = new UTF8Encoding(false, true).GetString(bytes);
            if (!WorldFileVariants.IsOwned(text)) throw new InvalidDataException("Unrecognized historical variant: " + path);
            if (selected != null && (WorldFileVariants.Overlay(standard, selected) != WorldFileVariants.Overlay(standard, text) ||
                WorldFileVariants.Topology(selected) != WorldFileVariants.Topology(text) || WorldFileVariants.DryCycle(selected) != WorldFileVariants.DryCycle(text)))
                throw new InvalidDataException("Historical and current variants conflict; both were preserved: " + path);
            selected ??= text;
            legacy.Add(new WorldVariantFileWrite { Path = path, Text = null, Expected = bytes,
                BackupPath = path + ".rainwright-migrated-" + Guid.NewGuid().ToString("N") + ".bak" });
        }
        if (selected == null) return;
        WorldFileVariants.Overlay(standard, selected); // Validate before changing any filename.
        string encoded = original != null && !WorldFileVariants.IsLegacy(new UTF8Encoding(false, true).GetString(original))
            ? new UTF8Encoding(false, true).GetString(original) : WorldFileVariants.Reencode(selected);
        var writes = new List<WorldVariantFileWrite>();
        if (original == null || new UTF8Encoding(false, true).GetString(original) != encoded)
            writes.Add(new WorldVariantFileWrite { Path = target, Text = encoded, Expected = original });
        writes.AddRange(legacy);
        WorldVariantFileTransaction.Commit(writes, authorize);
        DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Unhide(target);
    }
}
