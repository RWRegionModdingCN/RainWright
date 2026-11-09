using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.World;

static class Program
{
    static int checks;
    static void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
    static void Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--profile-discovery")
        {
            MissingSourceRegression.ProfileReadOnly(args[1], args[2]);
            return;
        }
        MissingSourceRegression.Run(Check);
        RoomRegistrationRegression.Run(Check);
        if (args.Length > 0)
        {
            string source = File.ReadAllText(args[0]);
            var document = WorldDocument.Parse(source);
            Check(document.Serialize() == source, "The installed B5 author file must roundtrip without changes.");
            Check(document.TryAddCreatureSpawn("B5_BDF01", 6, "DaddyLongLegs", 1, "", "", false, out _), "The real B5 file must accept the screenshot's den/species through the production document.");
            string edited = document.Serialize();
            int spawn = edited.IndexOf("6-DaddyLongLegs", StringComparison.Ordinal);
            Check(spawn > edited.IndexOf("CREATURES", StringComparison.Ordinal) && spawn < edited.IndexOf("END CREATURES", StringComparison.Ordinal), "The real B5 projection must put the spawn inside CREATURES.");
            Check(File.ReadAllText(args[0]) == source, "Read-only installed-source verification must not alter the real Mod.");
        }
        string root = Path.Combine(Path.GetTempPath(), "RainWright-Creature-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            AssetManager.SourceRoot = root;
            ModManager.ActiveMods.Add(new() { id = "creature-author", path = root });
            string path = Path.Combine(root, "World", "B5", "world_B5.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string original = "ROOMS\r\nB5_BDF01 : <3>B5_BDF01, DISCONNECTED, DISCONNECTED, DISCONNECTED\r\nEND ROOMS\r\nCREATURES\r\n(X-Artificer,Inv,Spear)B5_BDF01 : 2-RedLizard\r\nEND CREATURES\r\n// custom directive stays\r\n";
            File.WriteAllText(path, original);
            var room = new AbstractRoom { name = "B5_BDF01", index = 0 };
            var world = new World { abstractRooms = new[] { room } };
            DevToolRuntime.ActiveSession = new() { World = world };
            WorldCreatureAuthoringHooks.Enable(new());
            Check(WorldTextRegistry.EnsureLoaded("B5"), "Open the actual world author document.");
            string repairedOpening = original.Replace("<3>B5_BDF01", "B5_BDF01");
            Check(File.ReadAllText(path) == repairedOpening && !WorldTextRegistry.Dirty, "Loading a legacy author document repairs it immediately without depending on the autosave timer.");
            string migrationBackup = Directory.GetFiles(Path.GetDirectoryName(path), Path.GetFileName(path) + ".rainwright-legacy-*.bak").Single();
            Check(File.ReadAllText(migrationBackup) == original, "Automatic migration preserves a separate exact original backup.");
            WorldCreatureLiveReload.ReloadRoom("B5", room.name);
            Check(WorldCreatureLiveReload.LastSucceeded && room.entitiesInDens.Count == 0, "Inv exclusions must not spawn in an ordinary Inv timeline.");
            int before = WorldTextRegistry.AuthoringRevision, graphRevision = WorldTextRegistry.Revision;
            Check(WorldTextRegistry.TryAddCreatureSpawn("B5", room.name, 6, "DaddyLongLegs", 1, "", "", false, out int id, out _), "Apply the exact screenshot's DaddyLongLegs spawn.");
            Check(WorldTextRegistry.AuthoringRevision != before && WorldTextRegistry.Dirty && WorldTextRegistry.Revision == graphRevision, "Spawn edits must publish an authoring revision without invalidating the whole connection graph.");
            Check(room.entitiesInDens.Count == 1 && ((AbstractCreature)room.entitiesInDens[0]).Template.type.value == "DaddyLongLegs", "An unfiltered spawn must appear in the bounded preview.");
            Check(File.ReadAllText(path) == repairedOpening, "Applying a creature edit is distinct from the unified disk commit boundary after load migration.");
            Check(WorldTextRegistry.Save(), "Persist the production registry, not a test serializer.");
            string saved = File.ReadAllText(path);
            Check(saved.Contains("B5_BDF01 : B5_BDF01,") && !saved.Contains("<3>") && WorldFileVariants.WorldText(File.ReadAllText(WorldModificationWorkspace.VariantPath("b5"))).Contains("B5_BDF01<3>"), "Unified author save keeps standard text plain and preserves exact endpoints in the opt-in XML variant.");
            Check(saved.Contains("B5_BDF01 : 6-DaddyLongLegs") && saved.Contains("// custom directive stays"), "The persisted TXT must contain DaddyLongLegs in CREATURES and retain unknown author text.");
            WorldTextRegistry.Clear(); Check(WorldTextRegistry.EnsureLoaded("B5") && WorldTextRegistry.GetCreatureSpawns("B5", room.name).Any(s => s.Creature == "DaddyLongLegs" && s.DenNode == 6), "A fresh parse must recover the saved spawn.");
            var spawn = WorldTextRegistry.GetCreatureSpawns("B5", room.name).Single(s => s.Creature == "DaddyLongLegs");
            Check(WorldTextRegistry.TryUpdateCreatureSpawn("B5", spawn.Id, 6, "DaddyLongLegs", 1, "", "Inv", true, out _), "Apply an Inv exclusion to the existing preview.");
            Check(room.entitiesInDens.Count == 0, "Changing to an excluded scope must remove the old preview as well as skip new creation.");
            Check(WorldTextRegistry.Save(), "Persist the excluded condition.");
            Check(File.ReadAllText(path).Contains("(X-Inv)B5_BDF01 : 6-DaddyLongLegs"), "Excluded Inv syntax must survive persistence.");

            world.game.TimelinePoint = new("White");
            WorldCreatureLiveReload.ReloadRoom("B5", room.name);
            Check(room.entitiesInDens.Count == 0, "The current Inv campaign must remain excluded even when a tool changes the world's timeline point.");
            Check(WorldTextRegistry.TryAddCreatureSpawn("B5", room.name, 4, "PinkLizard", 1, "", "White", false, out int allowed, out _), "Commit a different native world-time condition.");
            Check(room.entitiesInDens.Count == 1 && ((AbstractCreature)room.entitiesInDens[0]).Template.type.value == "PinkLizard", "The Inv exclusion safeguard must preserve other native world-time conditions.");
            Check(WorldTextRegistry.TryDeleteCreatureSpawn("B5", allowed, out _), "Remove the scope probe before subsequent lineage checks.");

            Check(WorldLineageRegistry.TryAdd("B5", room.name, 5, new[] { new WorldLineageStageRecord { Creature = "DaddyLongLegs", Chance = 0 } }, "Inv", true, false, out _, out _), "Add an Inv-excluded lineage using the production registry.");
            Check(room.entitiesInDens.Count == 0, "The same Inv campaign guard must apply to lineage previews.");
            Check(WorldTextRegistry.TryAddCreatureSpawn("B5", room.name, 3, "DaddyLongLegs", 2, "", "", false, out _, out _), "Commit an ordinary spawn while lineage edits remain pending.");
            Check(WorldTextRegistry.TryGetRecoveryText(out _, out string recovery) && recovery.Contains("3-DaddyLongLegs-2") && recovery.Contains("(X-Inv)LINEAGE : B5_BDF01 : 5 : DaddyLongLegs-0") && WorldLineageRegistry.Dirty,
                "Recovery must combine unsaved ordinary and lineage spawns without marking either author model saved.");
            Check(WorldTextRegistry.Save(), "Mixed ordinary and lineage edits must save together.");
            string mixed = File.ReadAllText(path);
            Check(mixed.Contains("3-DaddyLongLegs-2") && mixed.Contains("(X-Inv)LINEAGE : B5_BDF01 : 5 : DaddyLongLegs-0"), "A lineage write must preserve ordinary spawns from the same commit.");

            WorldTextRegistry.Clear(); WorldLineageRegistry.Clear();
            string failedRepairSource = mixed.Replace("B5_BDF01 : B5_BDF01,", "B5_BDF01 : <3>B5_BDF01,");
            File.WriteAllText(path, failedRepairSource);
            string variantDirectory = Path.GetDirectoryName(WorldModificationWorkspace.VariantPath("b5"));
            Directory.Move(variantDirectory, variantDirectory + "-prior-fixture");
            File.WriteAllText(variantDirectory, "blocked variant directory");
            Check(!WorldTextRegistry.EnsureLoaded("B5") && WorldTextRegistry.Dirty && WorldTextRegistry.LoadError.Contains("repair"), "A blocked migration reports failure and retains the repaired author document for retry.");
            Check(File.ReadAllText(path) == failedRepairSource, "Failed migration leaves the original author source unchanged.");
            File.Delete(variantDirectory);
            Check(WorldTextRegistry.Save() && File.ReadAllText(path) == mixed, "Retrying after a migration write failure persists the repaired content without losing creatures or lineage.");
            Directory.Delete(variantDirectory, true);

            // Exercise both real preview paths for every campaign, with a displaced world time.
            foreach (string campaignCondition in new[] { "White", "Yellow", "Red", "Gourmand", "Artificer", "Spear", "Rivulet", "Saint", "Sofanthiel", "Watcher", "CustomCampaign", "White|0", "Yellow|1", "Red|2" })
            {
                string[] parts = campaignCondition.Split('|');
                string campaign = parts[0];
                string scope = parts.Length > 1 ? parts[1] : campaign == "Sofanthiel" ? "Inv" : campaign;
                WorldTextRegistry.Clear(); WorldLineageRegistry.Clear();
                File.WriteAllText(path, "ROOMS\nB5_BDF01 : DISCONNECTED\nEND ROOMS\nCREATURES\n(X-" + scope + ",Unrelated)B5_BDF01 : 2-RedLizard\n(X-" + scope + ")LINEAGE : B5_BDF01 : 5 : DaddyLongLegs-0\nEND CREATURES\n");
                world.game.StoryCharacter = new(campaign); world.game.TimelinePoint = new("OtherTime");
                Check(WorldTextRegistry.EnsureLoaded("B5"), "Load campaign exclusion probe: " + campaign);
                WorldCreatureLiveReload.ReloadRoom("B5", room.name);
                Check(WorldCreatureLiveReload.LastSucceeded && room.entitiesInDens.Count == 0, "Ordinary and lineage exclusion must survive shifted world time: " + campaign);
                world.game.StoryCharacter = new(campaign == "White" ? "Yellow" : "White");
                WorldCreatureLiveReload.ReloadRoom("B5", room.name);
                Check(WorldCreatureLiveReload.LastSucceeded && room.entitiesInDens.Count == 2, "Exclusions must still allow unrelated campaigns: " + campaign);
            }
            string selectedSource = mixed.Replace("B5_BDF01<3>", "<3>B5_BDF01");
            File.WriteAllText(path, selectedSource);
            world.game.StoryCharacter = new("Sofanthiel"); world.game.TimelinePoint = new("Inv");
            WorldTextRegistry.Clear(); WorldLineageRegistry.Clear();
            string author = Path.Combine(root, "author-output"); Directory.CreateDirectory(author);
            ModManager.ActiveMods[0].id = "read-source";
            ModManager.ActiveMods.Add(new() { id = "creature-author", path = author });
            Check(WorldTextRegistry.EnsureLoaded("B5") && !File.ReadAllText(path).Contains("<3>"), "Automatic ownership repairs the region's actual source into a standard file without selecting a destination Mod.");
            Check(WorldTextRegistry.TryAddCreatureSpawn("B5", room.name, 7, "DaddyLongLegs", 1, "RotType:3", "", false, out _, out _) && WorldTextRegistry.Save(), "The automatically identified region owner must persist another real spawn.");
            string patch = Path.Combine(author, "modify", "world", "b5", "world_b5.txt");
            Check(!File.Exists(patch) && File.ReadAllText(path).Contains("7-DaddyLongLegs-{RotType:3}"), "An unrelated enabled Mod cannot receive the region's edits, and custom world TXT is not turned into a modify patch.");
            Check(!File.ReadAllText(path).Contains("<3>") && !Directory.Exists(Path.Combine(root, "modify")), "Ordinary source saving retains a standard base without inventing a modify directory.");
            Console.WriteLine($"PASS: {checks} production creature conditions, hot-reload cleanup, revisions and actual TXT persistence checks.");
        }
        finally
        {
            // Only remove this test's exact generated temporary tree.
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Invalid test cleanup root.");
            Directory.Delete(root, true);
        }
    }
}
