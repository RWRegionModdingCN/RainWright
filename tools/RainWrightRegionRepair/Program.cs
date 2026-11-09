using System.Text.Json;
using DryCycle.DevUI.DevTool.World;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Persistence;

if (args.Length < 1) throw new ArgumentException("RainWrightRegionRepair <mod-root> [--apply]");
string modRoot = Path.GetFullPath(args[0]).TrimEnd(Path.DirectorySeparatorChar);
bool apply = args.Contains("--apply");
string worldRoot = Path.Combine(modRoot, "world");
if (!Directory.Exists(worldRoot)) throw new DirectoryNotFoundException(worldRoot);
if (System.Diagnostics.Process.GetProcessesByName("RainWorld").Length > 0 || System.Diagnostics.Process.GetProcessesByName("FloodForge").Length > 0)
    if (apply) throw new IOException("Close Rain World and FloodForge before repairing author sources; no files were changed.");
var report = new List<object>(); var migrations = new List<(string path, byte[] expected)>();
string topologyPath = Path.Combine(worldRoot, "WorldTopology.json");
byte[] topologyBytes = File.Exists(topologyPath) ? File.ReadAllBytes(topologyPath) : null;
string topology = topologyBytes == null ? null : new System.Text.UTF8Encoding(false, true).GetString(topologyBytes);
JsonDocument legacyTopology = topology == null ? null : JsonDocument.Parse(topology);
var migratedRegions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

foreach (string regionDirectory in Directory.EnumerateDirectories(worldRoot))
{
    string region = Path.GetFileName(regionDirectory).ToLowerInvariant();
    string source = Path.Combine(regionDirectory, "world_" + region + ".txt");
    if (!File.Exists(source)) continue;
    try
    {
        Authorize(source);
        byte[] sourceBytes = File.ReadAllBytes(source); string text = File.ReadAllText(source);
        var document = WorldDocument.Parse(text, source);
        if (document.Rooms.Count == 0 || !text.Contains("END ROOMS")) throw new InvalidDataException("No complete ROOMS section.");
        string variantPath = Path.Combine(regionDirectory, "rainwright", "world_" + region + "_rainwright.xml");
        string combinedPath = Path.Combine(regionDirectory, "rainwright", "world_" + region + "_rainwright_drycycle.xml");
        if (apply)
        {
            foreach (string target in new[] { variantPath, combinedPath })
                WorldVariantFormatMigration.Migrate(text, target, new[] { Path.ChangeExtension(target, ".txt"), Path.Combine(regionDirectory, Path.GetFileNameWithoutExtension(target) + ".txt") }, Authorize);
            LegacyMapLayoutMigration.MigrateRegion(regionDirectory, region, Authorize);
        }
        byte[] extraBytes = WorldVariantFileTransaction.ReadExisting(variantPath), combinedBytes = WorldVariantFileTransaction.ReadExisting(combinedPath);
        string ReadVariant(string target)
        {
            string[] candidates = { target, Path.ChangeExtension(target, ".txt"), Path.Combine(regionDirectory, Path.GetFileNameWithoutExtension(target) + ".txt") };
            string selected = candidates.FirstOrDefault(File.Exists);
            return selected == null ? null : File.ReadAllText(selected);
        }
        string variant = ReadVariant(variantPath) ?? ReadVariant(combinedPath);
        string currentTopology = variant == null ? topology : WorldFileVariants.Topology(variant) ?? topology;
        if (variant != null) document = WorldDocument.Parse(WorldFileVariants.Overlay(text, variant));
        // Recover legacy endpoint identities only while the room destinations still agree.
        int mappings = 0, staleMappings = 0;
        if (legacyTopology != null && legacyTopology.RootElement.TryGetProperty("regions", out var regions))
            foreach (var pair in regions.EnumerateObject())
                if (pair.Name.Equals(region, StringComparison.OrdinalIgnoreCase) && pair.Value.TryGetProperty("connections", out var connections))
                    foreach (var edge in connections.EnumerateArray())
                    {
                        var a = edge.GetProperty("a"); var b = edge.GetProperty("b");
                        string aRoom = a.GetProperty("room").GetString(), bRoom = b.GetProperty("room").GetString();
                        int aNode = a.GetProperty("node").GetInt32(), bNode = b.GetProperty("node").GetInt32();
                        string direction = edge.TryGetProperty("direction", out var d) ? d.GetString() : "Bidirectional";
                        if (direction != "BToA") Restore(aRoom, aNode, bRoom, bNode);
                        if (direction != "AToB") Restore(bRoom, bNode, aRoom, aNode);
                    }
        void Restore(string from, int node, string to, int exit)
        {
            if (document.TryGetConnection(from, node, out string token) && WorldConnectionToken.TryParse(token, out string target, out int current) &&
                target.Equals(to, StringComparison.OrdinalIgnoreCase) && document.TryGetRoom(to, out var other) && exit >= 0 && exit < other.Connections.Count && (current < 0 || current == exit))
            { document.TrySetConnection(from, node, WorldConnectionToken.Format(to, exit)); mappings++; }
            else staleMappings++;
        }
        if (staleMappings > 0) throw new InvalidDataException("Legacy endpoint mappings conflict with current author data; all region files were preserved.");
        var projection = WorldFileVariants.Project(document.Serialize());
        var writes = new List<WorldVariantFileWrite>();
        string mapPath = Path.Combine(regionDirectory, "map_" + region + ".txt");
        Add(source, projection.Standard, sourceBytes);
        Add(variantPath, projection.RequiresExtension ? WorldFileVariants.Encode(projection.Extended, currentTopology) : null, extraBytes);
        string combined = ReadVariant(combinedPath);
        if (combined != null && !WorldFileVariants.IsOwned(combined)) throw new InvalidDataException("Unrecognized combined variant; original file retained.");
        string dryCycle = combined == null ? null : WorldFileVariants.DryCycle(combined);
        var dryCycleData = new System.Xml.Linq.XElement("DryCycleRegion", new System.Xml.Linq.XAttribute("region", region));
        foreach (string kind in new[] { "TemperatureSets", "WeatherSpatial" })
        {
            string author = Path.Combine(worldRoot, kind + ".json"); if (!File.Exists(author)) continue;
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(author))?.AsObject();
            if (root?["regions"] is not System.Text.Json.Nodes.JsonObject all) continue;
            var match = all.FirstOrDefault(p => p.Key.Equals(region, StringComparison.OrdinalIgnoreCase));
            if (match.Value is not System.Text.Json.Nodes.JsonObject settings || settings.Count == 0) continue;
            root["regions"] = new System.Text.Json.Nodes.JsonObject { [match.Key] = match.Value.DeepClone() };
            dryCycleData.Add(new System.Xml.Linq.XElement(kind, root.ToJsonString()));
        }
        if (dryCycleData.HasElements) dryCycle = dryCycleData.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        if (combined != null || !string.IsNullOrEmpty(dryCycle)) Add(combinedPath, projection.RequiresExtension && !string.IsNullOrEmpty(dryCycle) ? WorldFileVariants.Encode(projection.Extended, currentTopology, dryCycle) : null, combinedBytes);
        string layoutPath = Path.Combine(regionDirectory, "map_" + region + ".drycycle-layout.txt");
        byte[] layoutBytes = WorldVariantFileTransaction.ReadExisting(layoutPath);
        int positions = 0;
        if (File.Exists(mapPath))
        {
            string map = File.ReadAllText(mapPath);
            string layout = layoutBytes == null ? LegacyMapLayoutMigration.Header + "\n" : File.ReadAllText(layoutPath);
            string migrated = LegacyMapLayoutMigration.Convert(map, layout, out positions);
            Add(mapPath, migrated, File.ReadAllBytes(mapPath));
        }
        else if (layoutBytes != null) throw new InvalidDataException("Legacy layout exists without a standard map; retained for manual recovery.");
        if (apply)
        {
            WorldVariantFileTransaction.Commit(writes, Authorize);
            if (layoutBytes != null) migrations.Add((layoutPath, layoutBytes));
        }
        // Detect invalid creature den references without changing author intent or deleting spawns.
        var denWarnings = new List<string>(); bool creatures = false;
        foreach (string line in projection.Standard.Replace("\r", "").Split('\n'))
        {
            if (line == "CREATURES") { creatures = true; continue; }
            if (line == "END CREATURES") { creatures = false; continue; }
            if (!creatures || line.StartsWith("//") || line.Contains("LINEAGE", StringComparison.OrdinalIgnoreCase)) continue;
            int separator = line.IndexOf(" : ", StringComparison.Ordinal); if (separator < 0) continue;
            string room = line[..separator]; if (room.Contains(')')) room = room[(room.LastIndexOf(')') + 1)..];
            string roomPath = Path.Combine(worldRoot, region + "-rooms", room + ".txt");
            if (!File.Exists(roomPath)) continue;
            using var input = File.OpenText(roomPath); string conn = null;
            for (int i = 0; i < 10; i++) conn = input.ReadLine();
            if (conn == null) continue; string[] nodes = conn.Split('|');
            if (nodes.Length < 3 || !int.TryParse(nodes[1], out int count)) continue;
            foreach (string item in line[(separator + 3)..].Split(','))
            {
                if (!int.TryParse(item.Trim().Split('-')[0], out int den)) continue;
                if (den < 0 || den >= count || nodes.Length <= den + 3 || nodes[den + 3].Split(',')[0] != "1") denWarnings.Add(room + ": " + item.Trim() + " (not a normal Den)");
            }
        }
        migratedRegions.Add(region);
        report.Add(new { region, applied = apply, writes = writes.Select(w => w.Path).ToArray(), restoredLayoutPositions = positions,
            extension = projection.RequiresExtension, recoveredEndpointMappings = mappings, staleLegacyMappings = staleMappings, creatureWarnings = denWarnings });
        void Add(string path, string content, byte[] previous)
        {
            if (content == null && previous == null) return;
            if (content != null && previous != null && WorldVariantFileTransaction.Bytes(content).SequenceEqual(previous)) return;
            if (path != source && path != mapPath && previous != null && !WorldFileVariants.IsOwned(new System.Text.UTF8Encoding(false, true).GetString(previous))) throw new InvalidDataException("Unrecognized variant file: " + path);
            writes.Add(new WorldVariantFileWrite { Path = path, Text = content, Expected = previous });
        }
    }
    catch (Exception error) { report.Add(new { region, error = error.ToString(), applied = false }); }
}
if (apply)
{
    foreach (var migration in migrations) Archive(migration.path, migration.expected);
    if (topologyBytes != null && legacyTopology.RootElement.TryGetProperty("regions", out var oldRegions) && oldRegions.EnumerateObject().All(r => migratedRegions.Contains(r.Name)))
        Archive(topologyPath, topologyBytes);
}
string output = JsonSerializer.Serialize(new { modRoot, apply, results = report }, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(output);

void Authorize(string path)
{
    string full = Path.GetFullPath(path);
    if (!full.StartsWith(modRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new IOException("Repair path leaves the specified Mod: " + full);
    string cursor = modRoot;
    if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Mod root redirects through a link.");
    foreach (string part in full[(modRoot.Length + 1)..].Split(Path.DirectorySeparatorChar))
    {
        cursor = Path.Combine(cursor, part);
        if ((File.Exists(cursor) || Directory.Exists(cursor)) && (File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Repair target redirects through a link: " + cursor);
    }
}
void Archive(string path, byte[] expected)
{
    Authorize(path); WorldVariantFileTransaction.Verify(path, expected);
    string backup = path + ".rainwright-migrated-" + Guid.NewGuid().ToString("N") + ".bak";
    Authorize(backup); File.Move(path, backup); EditorBackupFiles.Hide(backup);
}
