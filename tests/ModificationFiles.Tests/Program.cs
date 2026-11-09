using System;
using System.IO;
using DryCycle.DevUI.DevTool.World;
internal static class Program
{
    private static int checks;
    private static void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; }
    private static bool HiddenBackupOnWindows(string path) =>
        !OperatingSystem.IsWindows() || (File.GetAttributes(path) & FileAttributes.Hidden) != 0;
    private static void Main(string[] args)
    {
        foreach (string token in new[] { "SU_A02<3>", "<3>SU_A02" })
        {
            Check(WorldConnectionToken.TryParse(token, out string room, out int node) && room == "SU_A02" && node == 3, "Community and legacy exact endpoints read identically.");
            Check(WorldConnectionToken.Format(room, node) == "SU_A02<3>", "All exact endpoint writes use FloodForge's community suffix syntax.");
        }
        foreach (string malformed in new[] { "<3>", "SU_A02<-1>", "SU_A02<bad>", "SU_A02<3>extra", "SU_A02<3><4>", "<3>DISCONNECTED" })
            Check(!WorldConnectionToken.TryParse(malformed, out _, out _), "Malformed endpoints must not become room names: " + malformed);
        const string legacy = "ROOMS\r\nSU_A01 : <3>SU_A02, DISCONNECTED : SHELTER\r\nSU_A02 : SU_A01<0>\r\nEND ROOMS\r\nCREATURES\r\nSU_A01 : 2-Lizard-{custom:<3>data}\r\nEND CREATURES\r\n// <3>SU_A02 is a comment\r\n";
        var legacyDocument = WorldDocument.Parse(legacy);
        Check(!legacyDocument.Dirty && legacyDocument.Serialize() == legacy, "Opening old author text remains lossless and does not silently migrate or dirty it.");
        Check(legacyDocument.NormalizeLegacyConnectionTokens() == 1 && legacyDocument.Dirty, "Migration changes only legacy connection tokens.");
        Check(legacyDocument.Serialize() == legacy.Replace("SU_A01 : <3>SU_A02", "SU_A01 : SU_A02<3>"), "Migration preserves den data, comments, tags, newline style and existing community suffixes.");
        Check(legacyDocument.NormalizeLegacyConnectionTokens() == 0, "Migration is idempotent and cannot grow output on repeated saves.");
        Check(legacyDocument.TrySetConnection("SU_A01", 0, "<7>SU_A02") && legacyDocument.Serialize().Contains("SU_A02<7>"), "Even old callers cannot write the private prefix through the production document.");
        var ordinary = WorldDocument.Parse("ROOMS\nAA : <3>BB\nBB : DISCONNECTED, DISCONNECTED, DISCONNECTED, <0>AA\nEND ROOMS\n");
        Check(ordinary.NormalizeLegacyConnectionTokens() == 2 && ordinary.Serialize().Contains("AA : BB<3>\n") && ordinary.Serialize().Contains("DISCONNECTED, AA<0>\n"), "Reciprocal links retain the user's explicit endpoint indices in community format.");
        var exact = WorldDocument.Parse("ROOMS\nAA : <3>BB, <2>BB\nBB : DISCONNECTED, DISCONNECTED, <1>AA, <0>AA\nEND ROOMS\n");
        exact.NormalizeLegacyConnectionTokens();
        Check(exact.Serialize().Contains("AA : BB<3>, BB<2>"), "Ambiguous multi-pipe links retain their exact endpoint indices using the community suffix.");
        string root = Path.Combine(Path.GetTempPath(), "rainwright-modify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "base");
            string own = Path.Combine(root, "author");
            Directory.CreateDirectory(own); Directory.CreateDirectory(source);
            AssetManager.SourceRoot = source;
            string relative = "world/su/world_su.txt";
            string original = "ROOMS\nSU_A01 : DISCONNECTED, SU_A03\nSU_A02 : DISCONNECTED : SHELTER\nSU_A03 : SU_A01\nEND ROOMS\nCREATURES\n(White)SU_A01 : 2-Green\nEND CREATURES\n// third-party directive stays\n";
            string world = Path.Combine(source, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(world)); File.WriteAllText(world, original);
            ModManager.ActiveMods.Add(new() { id = "watcher", path = source });
            ModManager.ActiveMods.Add(new() { id = "my.regions", path = own });
            Directory.CreateDirectory(Path.Combine(own, "world", "su-rooms"));
            bool refused = false;
            Check(!WorldModificationWorkspace.BindRegion("unowned") && !WorldModificationWorkspace.HasRegionOwner, "An unrelated enabled Mod cannot become the automatic author destination.");
            string path = WorldAuthoringPathResolver.ResolveExistingSource(relative);
            Check(path == Path.Combine(own, "modify", "world", "su", "world_su.txt"), "Inherited vanilla world resolves to a standard modify file in its content Mod without manual selection.");
            Check(!File.Exists(path) && WorldAuthoringPathResolver.CanReadSource(path), "Opening a vanilla region does not create a full override or patch.");
            var doc = WorldDocument.Parse(WorldAuthoringPathResolver.ReadAllText(path), path);
            Check(doc.TrySetConnection("SU_A01", 0, "SU_A02") && doc.TrySetConnection("SU_A02", 0, "SU_A01"), "Real production document edits both ends of the connection.");
            WorldAuthoringPathResolver.AtomicWriteAllText(path, doc.Serialize());
            string first = File.ReadAllText(path);
            Check(first.Contains("[REPLACE_1]SU_A01 : SU_A02, SU_A03") && first.Contains("SU_A02 : SU_A01 : SHELTER"), "Native patch contains reciprocal changes and retains room flags.");
            Check(!first.Contains("SU_A03 : SU_A01") && !first.Contains("CREATURES") && !first.Contains("third-party"), "Unedited rooms, creatures and unknown sections are not exported as overrides.");
            Check(File.ReadAllText(world) == original && !Directory.Exists(Path.Combine(own, "world", "su")), "Original files are untouched and no full region copy is installed.");
            Check(WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(path, out string owner, out _) && owner == "my.regions", "Recovery binds to the same author's modify file.");
            doc.TrySetConnection("SU_A01", 0, "DISCONNECTED"); doc.TrySetConnection("SU_A02", 0, "DISCONNECTED");
            WorldAuthoringPathResolver.AtomicWriteAllText(path, doc.Serialize());
            Check(File.ReadAllText(path) == "", "Undo back to the opening baseline removes this session's native instructions.");
            string aux = WorldModificationWorkspace.AuxiliaryPath(path, "map_su.drycycle-layout.txt");
            Check(aux == Path.Combine(own, "world", "su", "map_su.drycycle-layout.txt"), "Existing editor sidecars never go inside modify.");
            string before = WorldAuthoringPathResolver.ReadAllText(path);
            File.WriteAllText(path, "// manual external change\n");
            refused = false;
            try { WorldAuthoringPathResolver.AtomicWriteAllText(path, before + "// change\n"); } catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(path) == "// manual external change\n", "An external patch edit is preserved on conflict.");

            string roomRelative = "world/su-rooms/su_a01_settings.txt";
            string roomFile = Path.Combine(source, roomRelative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(roomFile)); File.WriteAllText(roomFile, "RainIntensity: 1\nPalette: 0\n");
            var settings = new RoomSettings { filePath = roomFile, Text = "RainIntensity: 0.4\nPalette: 0\n" };
            WorldAuthoringPathResolver.SaveRoomSettings(settings);
            string roomPatch = Path.Combine(own, "modify", roomRelative.Replace('/', Path.DirectorySeparatorChar));
            Check(File.ReadAllText(roomPatch).Contains("[REPLACE_1]RainIntensity: 0.4") && File.ReadAllText(roomFile).Contains("RainIntensity: 1"), "Room settings also save as standard modify directives.");
            string read = WorldAuthoringPathResolver.ReadAllText(roomPatch);
            string ownedRoom = Path.Combine(own, "world", "su-rooms", "su_owned_settings.txt");
            File.WriteAllText(ownedRoom, "RainIntensity: 1\n");
            var normalSettings = new RoomSettings { filePath = ownedRoom, Text = "RainIntensity: 0.7\n" };
            WorldAuthoringPathResolver.SaveRoomSettings(normalSettings);
            Check(File.ReadAllText(ownedRoom) == normalSettings.Text && !File.Exists(Path.Combine(own, "modify", "world", "su-rooms", "su_owned_settings.txt")), "Normal room saves use the original author TXT and never create modify patches.");
            normalSettings.filePath = Path.Combine(own, "world", "su-rooms", "su_new_settings.txt");
            WorldAuthoringPathResolver.SaveRoomSettings(normalSettings);
            Check(File.ReadAllText(normalSettings.filePath) == normalSettings.Text && !File.Exists(Path.Combine(own, "modify", "world", "su-rooms", "su_new_settings.txt")), "A new custom-room file is created as normal source, not an ADD-only modification.");
            Check(WorldModificationWorkspace.AuthorRoot("assets/regionkit/backgrounds/new.txt", "su") == own,
                "Background output automatically uses the current region's content Mod without a selected Mod.");
            Check(WorldModificationWorkspace.Encode(roomPatch, read + "Grime: 0.3\n").Contains("[ADD]Grime: 0.3"), "Recovery payload is modification text, not a whole effective settings file.");
            string mapRelative = "world/su/map_su.txt";
            string mapRead = Path.Combine(source, mapRelative.Replace('/', Path.DirectorySeparatorChar));
            const string mapText = "SU_A01: 1><2><3><4><1><Outskirts><48><35\nSU_A02: 4><5><6><7><1><Outskirts><50><35\n";
            File.WriteAllText(mapRead, mapText);
            string mapPatch = Path.Combine(own, "modify", mapRelative.Replace('/', Path.DirectorySeparatorChar));
            string authored = "// author reference\n[MERGE]\n[REFERENCE]SU_A01: 1><2><3><4><1><Outskirts><48><35\n[ENDMERGE]\n";
            File.WriteAllText(mapPatch, authored);
            Check(WorldAuthoringPathResolver.TryResolveMapConfigSource("SU", mapRead, out string resolvedMap, out _) && resolvedMap == mapPatch,
                "Player Map resolves to the selected author's standard modify file.");
            WorldAuthoringPathResolver.AtomicWriteAllText(mapPatch, mapText.Replace("1><2><3><4", "10><20><3><4"));
            Check(File.ReadAllText(mapPatch).StartsWith(authored) && File.ReadAllText(mapRead) == mapText,
                "Saving Player Map coordinates preserves an existing native MERGE block and the read source.");
            string again = WorldAuthoringPathResolver.ReadAllText(mapPatch).Replace("10><20><3><4", "30><40><3><4");
            WorldAuthoringPathResolver.AtomicWriteAllText(mapPatch, again);
            Check(!File.ReadAllText(mapPatch).Contains("[REPLACE_1]SU_A01: 10><20") && File.ReadAllText(mapPatch).Contains("[REPLACE_1]SU_A01: 30><40"),
                "Repeated saves update this session's patch instead of accumulating stale replacement commands.");
            var authorMod = ModManager.ActiveMods[1];
            authorMod.hasNewestFolder = authorMod.hasTargetedVersionFolder = true;
            authorMod.NewestPath = Path.Combine(own, "newest"); authorMod.TargetedPath = Path.Combine(own, "v1.test");
            Directory.CreateDirectory(authorMod.TargetedPath);
            string custom = Path.Combine(authorMod.NewestPath, "world", "xy", "world_xy.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(custom)); File.WriteAllText(custom, "ROOMS\nXY_A01 : DISCONNECTED\nEND ROOMS\n");
            string baseCustom = Path.Combine(own, "world", "xy", "world_xy.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(baseCustom)); File.WriteAllText(baseCustom, File.ReadAllText(custom));
            Check(WorldAuthoringPathResolver.ResolveExistingSource("world/xy/world_xy.txt") == baseCustom,
                "Region author files resolve under the Mod root even when newest and targeted plugin folders exist.");
            string updatedCustom = File.ReadAllText(baseCustom) + "CREATURES\nXY_A01 : 2-DaddyLongLegs\nEND CREATURES\n";
            WorldAuthoringPathResolver.AtomicWriteAllText(baseCustom, updatedCustom);
            Check(File.ReadAllText(baseCustom) == updatedCustom && !File.ReadAllText(custom).Contains("DaddyLongLegs"),
                "Creature saves update root/world and preserve version-folder files.");
            Check(!WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(custom, out _, out _) &&
                  WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(baseCustom, out _, out _),
                "Recovery follows the root world source instead of redirecting into a plugin version folder.");
            string oldVersionPatch = Path.Combine(authorMod.NewestPath, "modify", "world", "xy", "world_xy.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(oldVersionPatch)); File.WriteAllText(oldVersionPatch, "old-version-patch");
            Check(!WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(oldVersionPatch, out _, out _) && File.ReadAllText(oldVersionPatch) == "old-version-patch",
                "Historical version-folder modify files are preserved and cannot regain write permission through recovery.");
            string baseSettings = Path.Combine(own, "world", "xy-rooms", "xy_a01_settings.txt");
            string versionSettings = Path.Combine(authorMod.NewestPath, "world", "xy-rooms", "xy_a01_settings.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(baseSettings)); Directory.CreateDirectory(Path.GetDirectoryName(versionSettings));
            File.WriteAllText(baseSettings, "Grime: 0.1\n"); File.WriteAllText(versionSettings, "Grime: 0.9\n");
            Check(WorldModificationWorkspace.ResolveRoomSettings(versionSettings) == baseSettings,
                "A runtime version-folder room path cannot bypass the root world author destination.");
            refused = false;
            try { WorldAuthoringPathResolver.AtomicWriteAllText(custom, "wrong destination"); } catch (IOException) { refused = true; }
            Check(refused && !File.ReadAllText(custom).Contains("wrong destination"), "Pinned old version-folder writers are refused without overwriting data.");
            string versionOnly = Path.Combine(authorMod.NewestPath, "world", "vv", "world_vv.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(versionOnly)); File.WriteAllText(versionOnly, "version-only-original");
            refused = false;
            try { refused = string.IsNullOrEmpty(WorldAuthoringPathResolver.ResolveExistingSource("world/vv/world_vv.txt")); } catch (InvalidOperationException) { refused = true; }
            Check(refused && File.ReadAllText(versionOnly) == "version-only-original" && !Directory.Exists(Path.Combine(own, "world", "vv")),
                "Version-only legacy files are preserved; ownership probing never migrates or invents an author region.");
            string freshAuthor = WorldAuthoringPathResolver.ResolveExistingSource("world/su/fresh-author.txt");
            Check(freshAuthor == Path.Combine(own, "world", "su", "fresh-author.txt"), "A new normal author TXT is created under root/world without a modify redirect.");
            File.WriteAllText(Path.Combine(source, "world", "su", "properties.txt"), "Subregion: source\n");
            string fresh = WorldAuthoringPathResolver.ResolveExistingSource("world/su/properties.txt");
            Check(fresh.StartsWith(Path.Combine(own, "modify", "world")), "New inherited world patches use the Mod root, independently of plugin version folders.");
            Check(WorldAuthoringPathResolver.ResolveExistingSource(mapRelative) == mapPatch,
                "An existing base-folder modification remains its source across version-folder fallbacks.");
            string bg = Path.Combine(authorMod.TargetedPath, "assets", "regionkit", "backgrounds", "author-new.txt");
            Check(WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(bg, out string bgOwner, out _) && bgOwner == "my.regions" && !File.Exists(bg),
                "A new background recovery path must remain inside the explicitly selected author's version folder without creating the file.");
            Check(!WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(Path.Combine(authorMod.TargetedPath, "unowned.txt"), out _, out _),
                "Background recovery must not authorize arbitrary new files in the Mod.");
            string sharedBackground = Path.Combine(source, "assets", "regionkit", "backgrounds", "shared.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(sharedBackground)); File.WriteAllText(sharedBackground, "source");
            Check(!WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(Path.Combine(authorMod.TargetedPath, "assets", "regionkit", "backgrounds", "shared.txt"), out _, out _),
                "Background recovery must not silently overlay another active Mod's asset.");
            string another = Path.Combine(root, "other-region");
            string anotherWorld = Path.Combine(another, "world", "zz", "world_zz.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(anotherWorld)); File.WriteAllText(anotherWorld, "ROOMS\nZZ_A : DISCONNECTED\nEND ROOMS\n");
            ModManager.ActiveMods.Add(new() { id = "other.region", path = another });
            Check(WorldAuthoringPathResolver.ResolveExistingSource("world/zz/world_zz.txt") == anotherWorld &&
                  WorldModificationWorkspace.AuthorRoot("assets/regionkit/backgrounds/test.txt", "zz") == another,
                "Changing region automatically changes the background author Mod without a selector or restart.");
            string inheritedTopology = Path.Combine(another, "world", "WorldTopology.json");
            File.WriteAllText(inheritedTopology, "{\"version\":1,\"regions\":{}}");
            WorldModificationWorkspace.BindRegion("su");
            string topologyTarget = WorldModificationWorkspace.TopologyPath(forSave: true);
            Check(!File.Exists(topologyTarget) && WorldModificationWorkspace.TopologyPath(forSave: false) == inheritedTopology,
                "Automatic ownership reads inherited topology without copying or creating an author file before a save.");
            WorldAuthoringPathResolver.AtomicWriteAllText(roomPatch, read + "Grime: 0.3\n");
            Check(File.ReadAllText(roomPatch).Contains("[ADD]Grime: 0.3") && !Directory.Exists(Path.Combine(another, "modify")),
                "A pending older-region patch stays bound to its own Mod after another region is observed.");
            ModManager.ActiveMods.Clear(); refused = false;
            try { WorldAuthoringPathResolver.AtomicWriteAllText(roomPatch, read); } catch (InvalidOperationException) { refused = true; }
            Check(refused, "Disabling the selected author Mod fails without writing to another Mod.");
            if (args.Length > 0)
            {
                string installed = Path.GetFullPath(args[0]);
                ModManager.ActiveMods.Add(new() { id = "installed-region-probe", path = installed });
                string installedWorld = Path.Combine(installed, "world", "b5", "world_b5.txt");
                string originalInstalled = File.ReadAllText(installedWorld);
                Check(WorldAuthoringPathResolver.ResolveExistingSource("world/b5/world_b5.txt") == installedWorld,
                    "The installed B5 region is automatically resolved to its actual normal world source.");
                Check(WorldModificationWorkspace.AuthorRoot("assets/regionkit/backgrounds/owner-probe.txt", "B5") == installed,
                    "The installed B5 background destination resolves to the owning content Mod without a manual selector.");
                string installedRoom = Path.Combine(installed, "world", "b5-rooms", "b5_s01_settings.txt");
                Check(WorldModificationWorkspace.ResolveRoomSettings(installedRoom) == installedRoom,
                    "The screenshot's B5_S01 settings retain their actual installed author path.");
                Check(File.ReadAllText(installedWorld) == originalInstalled,
                    "Installed-owner verification is read-only and never changes the user's region TXT.");
            }
            // Exercise the real Windows attributes and repeat-overwrite behavior.
            string backupSource = Path.Combine(root, "backup-source.txt");
            string backupPath = backupSource + ".bak";
            File.WriteAllText(backupSource, "original");
            DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Copy(backupSource, backupPath, true);
            Check(File.ReadAllText(backupPath) == "original" && HiddenBackupOnWindows(backupPath) && (File.GetAttributes(backupSource) & FileAttributes.Hidden) == 0,
                "Generated backups preserve original bytes and Windows hidden attributes while author files stay visible.");
            File.WriteAllText(backupSource, "second");
            DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Copy(backupSource, backupPath, true);
            Check(File.ReadAllText(backupPath) == "second" && HiddenBackupOnWindows(backupPath),
                "Repeated saves can overwrite hidden backups and keep them hidden.");
            string stagedBackup = backupSource + ".tmp"; File.WriteAllText(stagedBackup, "third");
            DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(stagedBackup, backupSource, backupPath);
            Check(File.ReadAllText(backupSource) == "third" && File.ReadAllText(backupPath) == "second" && HiddenBackupOnWindows(backupPath),
                "Atomic replacement keeps the previous bytes in a hidden backup.");
            File.WriteAllText(stagedBackup, "fourth");
            DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(stagedBackup, backupSource, backupPath);
            Check(File.ReadAllText(backupSource) == "fourth" && File.ReadAllText(backupPath) == "third" && (File.GetAttributes(backupSource) & FileAttributes.Hidden) == 0 && HiddenBackupOnWindows(backupPath),
                "Repeat atomic saves preserve visible author destinations and hidden backups.");
            string retained = "// author's own patch\n[MERGE]\nROOMS\nSU_NEW : SU_A01\nEND ROOMS\n";
            string composed = ModificationFileComposer.Compose("A: 1\n", "A: 2\n", retained);
            Check(composed.StartsWith(retained) && composed.Contains("[ENDMERGE]"), "Existing community patches are preserved and an open merge is closed before new directives.");
            Console.WriteLine("PASS: " + checks + " assertions; production documents, native modify output, recovery, conflicts and source preservation.");
        }
        finally { Directory.Delete(root, true); }
    }
}
