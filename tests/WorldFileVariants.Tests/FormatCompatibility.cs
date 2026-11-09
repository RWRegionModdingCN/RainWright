using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DryCycle.DevUI.DevTool.World;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Map.PlayerMap;

static class FormatCompatibility
{
    internal static void Run(Action<bool, string> check, Action<Action, string> refuse)
    {
        const string exact = "ROOMS\r\nAA : BB<1>, BB<0>\r\nBB : AA<1>, AA<0>\r\nEND ROOMS\r\nCREATURES\r\nAA : 2-Lizard\r\nEND CREATURES\r\n// 汉字 & <author>\r\n";
        string standard = WorldFileVariants.Project(exact).Standard;
        const string topology = "{\r\n\"version\":1,\"regions\":{}\r\n}";
        const string optional = "<DryCycleRegion>\r\n&lt;weather&amp;光&gt;\r\n</DryCycleRegion>";
        string xml = WorldFileVariants.Encode(exact, topology, optional);
        check(XDocument.Parse(xml).Root.Name == "RainWrightWorld" && (string)XDocument.Parse(xml).Root.Attribute("version") == "2", "Extension output is real, versioned XML.");
        check(WorldFileVariants.WorldText(xml) == exact && WorldFileVariants.Topology(xml) == topology && WorldFileVariants.DryCycle(xml) == optional,
            "XML roundtrips exact CRLF, Unicode, XML punctuation and optional payloads.");
        refuse(() => WorldFileVariants.WorldText(xml.Replace("version=\"2\"", "version=\"99\"")), "Unknown schema versions cannot be silently read as current data.");
        refuse(() => WorldFileVariants.WorldText(xml + "<extra/>"), "Multiple XML roots are refused.");
        refuse(() => WorldFileVariants.WorldText("<!DOCTYPE RainWrightWorld [<!ENTITY x SYSTEM 'file:///ignored'>]><RainWrightWorld version=\"2\"><World>&x;</World></RainWrightWorld>"), "DTD and external entity expansion are disabled.");
        refuse(() => WorldFileVariants.WorldText("<RainWrightWorld version=\"2\"><World>a</World><World>b</World></RainWrightWorld>"), "Duplicate payload nodes are refused.");
        string legacy = WorldFileVariants.Header + "\r\n" + exact + "// RainWright topology v1: " + Convert.ToBase64String(Encoding.UTF8.GetBytes(topology)) + "\r\n// RainWright DryCycle v1: " + Convert.ToBase64String(Encoding.UTF8.GetBytes(optional)) + "\r\n";
        check(WorldFileVariants.IsLegacy(legacy) && WorldFileVariants.Overlay(standard, legacy) == exact, "Old owned TXT remains readable before migration.");
        check(WorldFileVariants.Overlay(standard, WorldFileVariants.Reencode(legacy)) == exact && WorldFileVariants.DryCycle(WorldFileVariants.Reencode(legacy)) == optional,
            "Old TXT converts to XML without losing exact routes or optional author data.");

        string root = Path.Combine(Path.GetTempPath(), "RainWright-XmlCompatibility-" + Guid.NewGuid().ToString("N"));
        string directory = Path.Combine(root, "world", "aa"); Directory.CreateDirectory(directory);
        var oldMods = ModManager.ActiveMods; string oldRoot = AssetManager.SourceRoot;
        try
        {
            ModManager.ActiveMods = new() { new() { id = "xml-author", path = root } }; AssetManager.SourceRoot = root;
            string source = Path.Combine(directory, "world_aa.txt"); File.WriteAllText(source, standard);
            string legacyExtra = Path.Combine(directory, "rainwright", "world_aa_rainwright.txt"); Directory.CreateDirectory(Path.GetDirectoryName(legacyExtra));
            File.WriteAllText(legacyExtra, legacy);
            string legacyCombined = Path.Combine(directory, "world_aa_rainwright_drycycle.txt"); File.WriteAllText(legacyCombined, legacy);
            string map = Path.Combine(directory, "map_aa.txt"), role = Path.Combine(directory, "map_aa-White.txt");
            const string rows = "AA: 12><24><36><48><1><><20><10\r\nBB: 5><6><7><8><0><><20><10\r\n";
            File.WriteAllText(map, rows + "DryCycle_PlayerMap_HiddenRooms: v1|QUE=\r\n"); File.WriteAllText(role, rows);
            string layout = Path.Combine(directory, "map_aa.drycycle-layout.txt"), roleLayout = Path.Combine(directory, "map_aa-White.drycycle-layout.txt");
            string oldLayout = LegacyMapLayoutMigration.Header + "\nAA: 19.5><-20.25\n";
            File.WriteAllText(layout, oldLayout); File.WriteAllText(roleLayout, oldLayout);
            string oldBackup = Path.Combine(directory, "map_aa.txt.rainwright-legacy-example.bak"); File.WriteAllText(oldBackup, "backup");
            string cut = map[..map.LastIndexOf('.')];
            string[] Candidates() => Directory.GetFiles(directory).Where(alternate => alternate.StartsWith(cut) && alternate.EndsWith(".txt") && alternate != map).ToArray();
            check(Candidates().Contains(layout) && Candidates().Contains(roleLayout), "Reproduce FloodForge's exact candidate predicate against both old layout filenames.");
            string loaded = WorldFileVariantStore.Load("AA", source, standard);
            check(loaded == exact && File.ReadAllText(source) == standard, "The real store migrates format without changing the standard author world.");
            string extra = WorldModificationWorkspace.VariantPath("aa"), combined = WorldModificationWorkspace.VariantPath("aa", true);
            check(extra.EndsWith("_rainwright.xml") && combined.EndsWith("_rainwright_drycycle.xml") && XDocument.Load(extra).Root.Name == "RainWrightWorld", "Both required extension versions use XML filenames and schema.");
            string ExtraBackup() => Directory.GetFiles(Path.GetDirectoryName(legacyExtra), Path.GetFileName(legacyExtra) + ".rainwright-migrated-*.bak").Single();
            string CombinedBackup() => Directory.GetFiles(directory, Path.GetFileName(legacyCombined) + ".rainwright-migrated-*.bak").Single();
            check(!File.Exists(legacyExtra) && !File.Exists(legacyCombined) && File.ReadAllText(ExtraBackup()) == legacy && File.ReadAllText(CombinedBackup()) == legacy,
                "Retired owned TXT files retain exact backups after successful XML commits.");
            check(!File.Exists(layout) && !File.Exists(roleLayout) && File.Exists(LegacyMapLayoutMigration.XmlPath(map)) && File.Exists(LegacyMapLayoutMigration.XmlPath(role)), "Every role's old layout filename leaves the importer candidate set.");
            check(LegacyMapLayoutMigration.ReadPositions(File.ReadAllText(LegacyMapLayoutMigration.XmlPath(map)))["AA"][0] == "19.5", "Native map compatibility can read the structured XML layout.");
            check(Candidates().SequenceEqual(new[] { role }), "After migration FloodForge discovers only the genuine alternate-role map.");
            check(File.ReadAllText(map).StartsWith(rows) && !File.ReadAllText(map).Contains("\nDryCycle_PlayerMap_HiddenRooms:") && PlayerMapRoomVisibility.Read(File.ReadAllLines(map)).SetEquals(new[] { "AA" }),
                "Standard map coordinates stay exact; old private visibility converts to a supported comment without losing hidden rooms.");
            foreach (string candidate in new[] { map }.Concat(Candidates()))
            {
                foreach (string line in File.ReadAllLines(candidate))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("//")) continue;
                    string[] fields = line[(line.IndexOf(':') + 1)..].Split('>').Select(s => s.Replace("<", "").Trim()).ToArray();
                    check(fields.Length >= 6 && fields.Take(4).All(s => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) && int.TryParse(fields[4], out _),
                        "Every discovered map row reaches the numeric fields expected by FloodForge and original MapPage.");
                }
            }
            check((File.GetAttributes(oldBackup) & FileAttributes.Hidden) != 0 && (File.GetAttributes(ExtraBackup()) & FileAttributes.Hidden) != 0 &&
                (File.GetAttributes(extra) & FileAttributes.Hidden) == 0 && (File.GetAttributes(LegacyMapLayoutMigration.XmlPath(map)) & FileAttributes.Hidden) == 0,
                "Old and new backups are hidden while active world and map XML files remain visible.");
            string savedExtra = File.ReadAllText(extra), savedCombined = File.ReadAllText(combined), savedMap = File.ReadAllText(map);
            for (int round = 1; round <= 3; round++)
            {
                check(WorldFileVariantStore.Load("AA", source, standard) == exact && File.ReadAllText(extra) == savedExtra && File.ReadAllText(combined) == savedCombined && File.ReadAllText(map) == savedMap,
                    "Repeated migration/read round " + round + " preserves all current author bytes and both extension payloads.");
                check(Candidates().SequenceEqual(new[] { role }), "FloodForge candidates stay clean in repeat round " + round + ".");
            }
            File.WriteAllText(layout, oldLayout.Replace("19.5", "999"));
            refuse(() => LegacyMapLayoutMigration.MigrateFile(layout, LegacyMapLayoutMigration.XmlPath(map), p => { }), "Conflicting old and current layouts cannot overwrite either copy.");
            check(File.Exists(layout) && File.ReadAllText(LegacyMapLayoutMigration.XmlPath(map)).Contains("19.5"), "A conflicting author layout is retained for recovery.");
            File.Delete(layout);
            string arbitrary = Path.Combine(directory, "world_aa_rainwright.txt"); File.WriteAllText(arbitrary, "author file, not a known RainWright envelope");
            refuse(() => WorldFileVariantStore.Load("AA", source, standard), "Matching names without a known content signature are refused, not renamed.");
            check(File.ReadAllText(arbitrary).StartsWith("author file") && File.ReadAllText(extra) == savedExtra, "Unknown user files and current XML are preserved on refusal.");
        }
        finally
        {
            ModManager.ActiveMods = oldMods; AssetManager.SourceRoot = oldRoot;
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture cleanup.");
            foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
