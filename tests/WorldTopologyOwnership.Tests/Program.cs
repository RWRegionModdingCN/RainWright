using System;
using System.IO;
using DryCycle.DevUI.DevTool.World;
internal static class Program
{
    private static int count;
    private static void Check(bool ok, string message)
    {
        count++;
        if (!ok) throw new InvalidOperationException(message);
    }
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "rainwright-path-owner-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            string b5 = Path.Combine(root, "B5");
            string foreign = Path.Combine(root, "Rainwright");
            string authored = Path.Combine(b5, "world", "WorldTopology.json");
            string foreignAuthored = Path.Combine(foreign, "world", "WorldTopology.json");
            Directory.CreateDirectory(Path.GetDirectoryName(authored));
            Directory.CreateDirectory(Path.GetDirectoryName(foreignAuthored));
            ModManager.ActiveMods = new()
            {
                new() { id = "rainwright", path = b5 },
                new() { id = "other", path = foreign }
            };
            Check(WorldTopologyPathOwnership.Resolve(true) == null,
                "Without trusted gameplay owner Rainwright has no topology target.");
            int before = WorldTopologyPathOwnership.Revision;
            using (IDisposable lease = WorldTopologyPathOwnership.Register(RainWrightWorldTopologyPaths.Resolve))
            {
                Check(WorldTopologyPathOwnership.HasOwner && WorldTopologyPathOwnership.Revision > before,
                    "Permanent gameplay registers the owner.");
                Check(WorldTopologyPathOwnership.Resolve(true) == authored,
                    "Writable target is inside the active RainWright Mod.");
                Check(WorldTopologyPathOwnership.Resolve(false) == null,
                    "Read requests reject missing topology source.");
                bool rejected = false;
                try { WorldTopologyPathOwnership.Register(_ => foreignAuthored); }
                catch (InvalidOperationException) { rejected = true; }
                Check(rejected && WorldTopologyPathOwnership.Resolve(true) == authored,
                    "Duplicate editor registration cannot override game source.");
                File.WriteAllText(authored, "owned topology");
                Check(WorldTopologyPathOwnership.Resolve(false) == authored,
                    "Existing RainWright source remains readable.");
                ModManager.ActiveMods = new() { new() { id = "other", path = foreign } };
                Check(WorldTopologyPathOwnership.Resolve(true) == null,
                    "A foreign Mod without a source cannot claim a new RainWright document.");
                Check(RainWrightWorldTopologyPaths.Resolve(false) == null,
                    "With a populated ActiveMods list, a disabled RainWright cannot fall back to stale assembly paths.");
                ModManager.ActiveMods = new();
                Check(WorldTopologyPathOwnership.Resolve(true) == null &&
                      RainWrightWorldTopologyPaths.Resolve(true) == null,
                    "An initialized but empty ActiveMods list cannot claim an on-disk gameplay DLL.");
                ModManager.ActiveMods = new() { new() { id = "rainwright", path = b5 } };
                Check(WorldTopologyPathOwnership.Resolve(true) == authored,
                    "Mod reload restores game author target.");
                // A forged RainWright folder may contain links pointing into another
                // Mod. Verified Mod ID alone must not authorize that write.
                File.WriteAllText(foreignAuthored, "foreign data unchanged");
                string linkedMod = Path.Combine(root, "B5Linked");
                Directory.CreateDirectory(linkedMod);
                try
                {
                Directory.CreateSymbolicLink(Path.Combine(linkedMod, "world"),
                    Path.GetDirectoryName(foreignAuthored));
                ModManager.ActiveMods = new() { new() { id = "rainwright", path = linkedMod } };
                Check(WorldTopologyPathOwnership.Resolve(true) == null &&
                      WorldTopologyPathOwnership.Resolve(false) == null,
                    "Gameplay topology path refuses a world/ symlink into another Mod.");

                string linkedFileMod = Path.Combine(root, "B5LinkedFile");
                string linkedFileDir = Path.Combine(linkedFileMod, "world");
                Directory.CreateDirectory(linkedFileDir);
                File.CreateSymbolicLink(
                    Path.Combine(linkedFileDir, "WorldTopology.json"), foreignAuthored);
                ModManager.ActiveMods = new() { new() { id = "rainwright", path = linkedFileMod } };
                Check(WorldTopologyPathOwnership.Resolve(true) == null,
                    "Gameplay topology path refuses a symlinked author file.");
                }
                catch (IOException error) when (OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
                {
                    Console.WriteLine("SKIP: two symbolic-link checks require Windows symlink privilege; CI runs them on Linux.");
                }
                ModManager.ActiveMods = new() { new() { id = "rainwright", path = b5 } };
                Check(WorldTopologyPathOwnership.Resolve(true) == authored,
                    "A valid gameplay Mod path remains available after a rejected linked source.");
                lease.Dispose();
                Check(!WorldTopologyPathOwnership.HasOwner && WorldTopologyPathOwnership.Resolve(true) == null,
                    "Disposal releases topology identity.");
                lease.Dispose();
            }
            using (WorldTopologyPathOwnership.Register(_ => throw new IOException("injected failure")))
                Check(WorldTopologyPathOwnership.Resolve(true) == null &&
                    DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Errors.Count == 1,
                    "Faulting owner fails closed and logs full exception.");
            Check(!WorldTopologyPathOwnership.HasOwner &&
                  File.ReadAllText(foreignAuthored) == "foreign data unchanged",
                "Owner removal never modifies third-party author source.");
            ModManager.ActiveMods = new() { new() { id = "ancient-site", path = foreign }, new() { id = "rainwright", path = b5 } };
            File.Delete(authored);
            Check(RainWrightWorldTopologyPaths.Resolve(true) == foreignAuthored &&
                  RainWrightWorldTopologyPaths.Resolve(false) == foreignAuthored,
                "Moving the runtime preserves an existing active Mod author source.");
            string approved;
            string reason;
            int ownerRevision = WorldTopologyPathOwnership.Revision;
            Check(!WorldTopologyAuthorWriteGuard.TryAuthorize(authored, ownerRevision,
                    null, ownerRevision, out approved, out reason) &&
                  approved == null && reason.Contains("no verified"),
                "A stale LoadedPath cannot bypass a detached gameplay owner.");
            Check(!WorldTopologyAuthorWriteGuard.TryAuthorize(authored, ownerRevision,
                    foreignAuthored, ownerRevision, out approved, out reason) &&
                  approved == null && reason.Contains("original author"),
                "A source Mod switch cannot redirect unsaved changes to a foreign author file.");
            Check(!WorldTopologyAuthorWriteGuard.TryAuthorize(authored, ownerRevision - 1,
                    authored, ownerRevision, out approved, out reason) &&
                  approved == null && reason.Contains("owner changed"),
                "Even an identical file target requires the same lifecycle owner revision.");
            Check(WorldTopologyAuthorWriteGuard.TryAuthorize(authored, ownerRevision,
                    authored, ownerRevision, out approved, out reason) &&
                  approved == authored && reason == null,
                "Unchanged verified gameplay owner can save its in-memory topology.");
            Check(WorldTopologyAuthorWriteGuard.TryAuthorize("", ownerRevision,
                    authored, ownerRevision, out approved, out reason) &&
                  approved == authored,
                "A new topology document may use the current verified path if ownership is stable.");
            Console.WriteLine("PASS: " + count + " assertions; production topology path ownership.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { } }
    }
}
