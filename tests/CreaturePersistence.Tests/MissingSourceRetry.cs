using System.Diagnostics;
using System.Threading;
using DryCycle.DevUI.DevTool.World;

static partial class MissingSourceRegression
{
    internal static void ProfileReadOnly(string rootList, string sourceRoot)
    {
        AssetManager.SourceRoot = sourceRoot;
        ModManager.ActiveMods = new();
        foreach (string path in File.ReadAllLines(rootList))
            if (Directory.Exists(path))
                ModManager.ActiveMods.Add(new() {
                    id = Path.GetFileName(path), path = path,
                    NewestPath = Path.Combine(path, "newest"), hasNewestFolder = Directory.Exists(Path.Combine(path, "newest"))
                });
        Console.WriteLine("Read-only cold source discovery with " + ModManager.ActiveMods.Count + " local/workshop Mod directories.");
        var timer = Stopwatch.StartNew();
        var failures = new WorldSourceFailureRetry(); failures.Prepare("SU");
        Console.WriteLine("Source notification registration: " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
        failures.Clear();
        foreach (string region in new[] { "SU", "B5" })
        {
            timer.Restart(); bool bound = WorldModificationWorkspace.BindRegion(region);
            Console.WriteLine("Region ownership " + region + ": " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms, found=" + bound);
            timer.Restart(); string source = WorldAuthoringPathResolver.ResolveExistingSource("world/" + region + "/world_" + region + ".txt");
            Console.WriteLine("Author source " + region + ": " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms, found=" + !string.IsNullOrEmpty(source));
        }
    }

    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "RainWright-MissingSource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousMods = ModManager.ActiveMods; string previousSource = AssetManager.SourceRoot;
        try
        {
            AssetManager.SourceRoot = root;
            ModManager.ActiveMods = new();
            for (int i = 0; i < 100; i++)
                ModManager.ActiveMods.Add(new() { id = "missing-author-" + i, path = Path.Combine(root, "mod" + i) });
            Directory.CreateDirectory(ModManager.ActiveMods[99].path);
            WorldTextRegistry.Clear();
            check(!WorldTextRegistry.EnsureLoaded("ZZ"), "Missing writable world sources retain the original failure.");
            int revision = WorldTextRegistry.Revision, authorRevision = WorldTextRegistry.AuthoringRevision;
            int presenceReads = ModManager.FolderPresenceReads;
            long before = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
            for (int i = 0; i < 10000; i++)
                if (WorldTextRegistry.EnsureLoaded(i % 2 == 0 ? "ZZ" : "zz")) throw new Exception("A missing source was reported as loaded.");
            watch.Stop(); long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            check(WorldTextRegistry.Revision == revision && WorldTextRegistry.AuthoringRevision == authorRevision,
                "10000 missing-source map lookups must not retry Reload or advance author/save revisions.");
            check(ModManager.FolderPresenceReads == presenceReads,
                "Stable failure lookups must not rerun targeted/newest folder queries across all 100 Mods.");
            check(allocated < 65536, "Stable failure lookup must not allocate path strings per Mod per call; actual bytes " + allocated);
            Console.WriteLine("Missing-source hot path: 10000 lookups / 100 Mods, " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms, " + allocated + " main-thread bytes.");

            // Let any former one-second polling cadence elapse, with an unrelated Mod write.
            File.WriteAllText(Path.Combine(ModManager.ActiveMods[99].path, "unrelated.txt"), "no author change");
            Thread.Sleep(1300);
            check(!WorldTextRegistry.EnsureLoaded("ZZ") && WorldTextRegistry.Revision == revision && ModManager.FolderPresenceReads == presenceReads,
                "Elapsed time and unrelated Mod files must not trigger a periodic all-Mod retry.");

            string authorRoot = ModManager.ActiveMods[0].path;
            string file = Path.Combine(authorRoot, "world", "zz", "world_zz.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            const string text = "ROOMS\nZZ_A01 : DISCONNECTED\nEND ROOMS\nCREATURES\nEND CREATURES\n";
            File.WriteAllText(file, text);
            check(SpinWait.SpinUntil(() => { Thread.Sleep(10); return WorldTextRegistry.EnsureLoaded("ZZ"); }, 7000),
                "Creating the missing author source is detected by notifications and retried through the real resolver.");
            check(WorldTextRegistry.LoadError == null && WorldTextRegistry.HasRoom("ZZ", "ZZ_A01") && File.ReadAllText(file) == text,
                "Recovery loads the author source without modifying its contents or leaving the previous error.");

            WorldTextRegistry.Clear();
            using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                IOException lockedError = null;
                try { File.ReadAllText(file); }
                catch (IOException error) { lockedError = error; }
                check(lockedError != null && WorldSourceFailureRetry.IsSharingViolation(lockedError),
                    "The platform's actual sharing-lock error is classified for retry; HResult=" + lockedError?.HResult);
                check(!WorldTextRegistry.EnsureLoaded("ZZ") && WorldTextRegistry.LoadError != null,
                    "An exclusive author-file lock retains the actual read error.");
            }
            check(!WorldSourceFailureRetry.IsSharingViolation(new IOException("Bad file format", unchecked((int)0x8007000B))),
                "A non-sharing file error is never converted into a lock-retry loop.");
            check(SpinWait.SpinUntil(() => { Thread.Sleep(10); return WorldTextRegistry.EnsureLoaded("ZZ"); }, 7000),
                "A released sharing lock recovers through one known-file read probe even without a file notification.");
            check(File.ReadAllText(file) == text, "Sharing recovery must not write author data.");

            WorldTextRegistry.Clear();
            // A disposed region's notifications cannot invalidate a different missing region.
            check(!WorldTextRegistry.EnsureLoaded("RR"), "Prepare isolated source notification ownership.");
            int isolatedRevision = WorldTextRegistry.Revision;
            File.AppendAllText(file, "// previous region changed\n"); Thread.Sleep(350);
            check(!WorldTextRegistry.EnsureLoaded("RR") && WorldTextRegistry.Revision == isolatedRevision,
                "Unrelated and retired-region notifications cannot reopen source discovery.");
            WorldTextRegistry.Clear();
            check(!WorldTextRegistry.EnsureLoaded("QQ"), "Prepare a second missing region.");
            int missingRevision = WorldTextRegistry.Revision;
            check(!WorldTextRegistry.Reload("QQ") && WorldTextRegistry.Revision > missingRevision,
                "Explicit Reload bypasses the cached failure.");
            string changed = Path.Combine(root, "replacement"); string changedFile = Path.Combine(changed, "world", "qq", "world_qq.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(changedFile)); File.WriteAllText(changedFile, text.Replace("ZZ_", "QQ_"));
            ModManager.ActiveMods[0] = new() { id = "replacement", path = changed };
            WorldTextRegistry.InvalidateFailedSourceContext(); // Existing Remix OnModsInit boundary.
            check(WorldTextRegistry.EnsureLoaded("QQ") && WorldTextRegistry.HasRoom("QQ", "QQ_A01"),
                "Applying Mod configuration invalidates a missing-source result immediately, including same-count replacements.");
            WorldTextRegistry.Clear();

            // Exercise the portable exact-path fallback on Windows as well as on Linux CI.
            var fallback = new WorldSourceFailureRetry(); fallback.Prepare("FF");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
            object subscription = typeof(WorldSourceFailureRetry).GetField("subscription", flags).GetValue(fallback);
            var owners = (System.Collections.IList)subscription.GetType().GetField("Watchers", flags).GetValue(subscription);
            foreach (IDisposable owner in owners) owner.Dispose(); owners.Clear();
            typeof(WorldSourceFailureRetry).GetMethod("RegisterExactPaths", flags).Invoke(null, new[] { subscription, root });
            try
            {
                File.WriteAllText(Path.Combine(root, "unrelated-fallback.txt"), "unchanged author"); Thread.Sleep(350);
                check(!fallback.ShouldRetry("FF"), "Portable notifications ignore unrelated files without recursive directory traversal.");
                string portableFile = Path.Combine(changed, "world", "ff", "world_ff.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(portableFile)); File.WriteAllText(portableFile, text);
                check(SpinWait.SpinUntil(() => { Thread.Sleep(10); return fallback.ShouldRetry("FF"); }, 7000),
                    "Portable exact-path notifications discover a newly created source ancestor.");
            }
            finally { fallback.Clear(); }
        }
        finally
        {
            WorldTextRegistry.Clear(); ModManager.ActiveMods = previousMods; AssetManager.SourceRoot = previousSource;
            string cleanup = Path.GetFullPath(root), temporary = Path.GetFullPath(Path.GetTempPath());
            if (!cleanup.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(cleanup).StartsWith("RainWright-MissingSource-", StringComparison.Ordinal))
                throw new IOException("Refusing cleanup outside the dedicated temporary fixture.");
            Directory.Delete(cleanup, true);
        }
    }
}
