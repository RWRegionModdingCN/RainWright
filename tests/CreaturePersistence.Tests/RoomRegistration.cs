using System.Diagnostics;
using System.Reflection;
using DryCycle.DevUI.DevTool.World;

static class RoomRegistrationRegression
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RainWright-RoomRegistration-" + Guid.NewGuid().ToString("N"));
        string folder = Path.Combine(root, "world", "zz-rooms"), source = Path.Combine(root, "world", "zz", "world_zz.txt");
        var previousMods = ModManager.ActiveMods; string previousSource = AssetManager.SourceRoot;
        Directory.CreateDirectory(folder); Directory.CreateDirectory(Path.GetDirectoryName(source));
        string original = "// custom author directive\r\nROOMS\r\nZZ_A : DISCONNECTED : SHELTER\r\n(White)ZZ_SCOPED : DISCONNECTED\r\nEND ROOMS\r\n\r\nCREATURES\r\nZZ_A : 1-BlackLizard-{Mean:-0.5}\r\nEND CREATURES\r\nUNKNOWN\r\nkeep this exact text\r\nEND UNKNOWN\r\n";
        string Geometry(string name, string data = "0,4|0,5|0,4|0|") => name + "\n2*2|-1\n0\n0\n0\n0\n0\n0\n0\n0\n0\n" + data + "\n";
        try
        {
            File.WriteAllText(source, original);
            File.WriteAllText(Path.Combine(folder, "ZZ_A.txt"), Geometry("ZZ_A"));
            File.WriteAllText(Path.Combine(folder, "ZZ_B.txt"), Geometry("OLD_COPY_NAME"));
            File.WriteAllText(Path.Combine(folder, "ZZ_SCOPED.txt"), Geometry("ZZ_SCOPED"));
            File.WriteAllText(Path.Combine(folder, "ZZ_ZERO.txt"), Geometry("ZZ_ZERO", "0|0|0|0|"));
            File.WriteAllText(Path.Combine(folder, "ZZ_INCOMPLETE.txt"), Geometry("ZZ_INCOMPLETE", "0|"));
            File.WriteAllText(Path.Combine(folder, "ZZ_INVALID.txt"), "junk");
            File.WriteAllText(Path.Combine(folder, "ZZ_A_settings.txt"), Geometry("ZZ_A_settings"));
            File.WriteAllText(Path.Combine(folder, "ZZ_BACKUP.txt.bak"), Geometry("ZZ_BACKUP"));
            File.WriteAllText(Path.Combine(folder, "ZZ_BACKUP.bak.txt"), Geometry("ZZ_BACKUP"));
            File.WriteAllText(Path.Combine(folder, "YY_OTHER.txt"), Geometry("YY_OTHER"));
            File.WriteAllText(Path.Combine(folder, "GATE_ZZ_YY.txt"), Geometry("GATE_ZZ_YY"));
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "nested", "ZZ_NESTED.txt"), Geometry("ZZ_NESTED"));
            Directory.CreateDirectory(Path.Combine(root, "newest", "world", "zz-rooms"));
            File.WriteAllText(Path.Combine(root, "newest", "world", "zz-rooms", "ZZ_DLL_FOLDER.txt"), Geometry("ZZ_DLL_FOLDER"));
            AssetManager.SourceRoot = root;
            ModManager.ActiveMods = new() { new() { id = "own-region", path = root, NewestPath = Path.Combine(root, "newest"), hasNewestFolder = true } };
            WorldTextRegistry.Clear();
            check(WorldTextRegistry.EnsureLoaded("ZZ"), "Load the actual registry and its region owner before detached room discovery.");
            var taskField = typeof(WorldTextRegistry).GetField("roomRegistration", BindingFlags.NonPublic | BindingFlags.Static);
            var task = (Task<WorldRoomRegistration.Result>)taskField.GetValue(null);
            check(task != null, "One region load starts one background room-discovery task.");
            check(task.Wait(10000), "Small-room discovery completes without blocking editor initialization.");
            var found = task.GetAwaiter().GetResult();
            check(found.Rooms.Select(r => r.Name).SequenceEqual(new[] { "ZZ_B", "ZZ_ZERO" }), "Only valid, direct, unregistered region geometry is discovered; settings, scoped rooms, other regions, backups and version folders are excluded.");
            check(found.Rooms[0].Exits == 2 && found.Rooms[1].Exits == 0, "Count room exits through the production decoder, excluding den nodes and supporting zero-exit rooms.");
            check(File.ReadAllText(source) == original, "The discovery worker cannot write the author's world TXT.");
            check(WorldTextRegistry.EnsureLoaded("ZZ") && !WorldTextRegistry.Dirty, "Merge through the actual unified registry and commit boundary.");
            string saved = File.ReadAllText(source);
            check(saved.Replace("ZZ_B : DISCONNECTED, DISCONNECTED\r\n", "").Replace("ZZ_ZERO : \r\n", "") == original,
                "Registration only appends standard room records; existing connections, conditions, tags, creature text, unknown directives and CRLF stay exact.");
            check(File.ReadAllText(source + ".bak") == original,
                "Automatic registration keeps the unified original backup bytes.");
            if (OperatingSystem.IsWindows())
                check((File.GetAttributes(source + ".bak") & FileAttributes.Hidden) != 0,
                    "Automatic registration keeps the original backup hidden on Windows.");
            check(!Directory.Exists(Path.Combine(root, "modify")) && !Directory.Exists(Path.Combine(root, "world", "zz", "rainwright")), "Ordinary room registration cannot manufacture modify or extension files.");
            check(taskField.GetValue(null) == null, "Completed discovery is retired instead of polling the room folder every frame.");
            int revision = WorldTextRegistry.Revision;
            for (int i = 0; i < 10000; i++) WorldTextRegistry.EnsureLoaded("ZZ");
            check(WorldTextRegistry.Revision == revision && taskField.GetValue(null) == null && File.ReadAllText(source) == saved, "Ten thousand stable reads neither rescan nor rewrite room sources.");
            WorldTextRegistry.Clear(); WorldTextRegistry.EnsureLoaded("ZZ");
            task = (Task<WorldRoomRegistration.Result>)taskField.GetValue(null); task.GetAwaiter().GetResult();
            WorldTextRegistry.EnsureLoaded("ZZ");
            check(File.ReadAllText(source) == saved && !WorldTextRegistry.Dirty, "A fresh region load is idempotent and preserves all previously registered rooms.");

            // A room deleted after worker completion must not be registered from stale discovery.
            File.WriteAllText(Path.Combine(folder, "ZZ_LATE.txt"), Geometry("ZZ_LATE"));
            WorldTextRegistry.Clear(); WorldTextRegistry.EnsureLoaded("ZZ");
            task = (Task<WorldRoomRegistration.Result>)taskField.GetValue(null); task.GetAwaiter().GetResult();
            File.Delete(Path.Combine(folder, "ZZ_LATE.txt")); WorldTextRegistry.EnsureLoaded("ZZ");
            check(File.ReadAllText(source) == saved, "Deletion between discovery and commit cannot add a nonexistent room.");

            // External edits win over an automatic merge; pending author data remains recoverable.
            File.WriteAllText(Path.Combine(folder, "ZZ_CONFLICT.txt"), Geometry("ZZ_CONFLICT"));
            WorldTextRegistry.Clear(); WorldTextRegistry.EnsureLoaded("ZZ");
            task = (Task<WorldRoomRegistration.Result>)taskField.GetValue(null); task.GetAwaiter().GetResult();
            string external = saved + "// simultaneous external edit\r\n"; File.WriteAllText(source, external);
            WorldTextRegistry.EnsureLoaded("ZZ");
            check(File.ReadAllText(source) == external && WorldTextRegistry.Dirty && WorldTextRegistry.LoadError != null,
                "An external world-file conflict is reported and preserved instead of overwritten or silently treated as saved.");
        }
        finally
        {
            // Drop this fixture's deliberately conflicting document; never use this in production.
            typeof(WorldTextRegistry).GetField("document", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            WorldTextRegistry.Clear(); ModManager.ActiveMods = previousMods; AssetManager.SourceRoot = previousSource;
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe fixture path.");
            Directory.Delete(root, true);
        }
    }
}
