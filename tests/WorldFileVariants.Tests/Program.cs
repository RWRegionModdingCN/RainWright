using DryCycle.DevUI.DevTool.World;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Map.PlayerMap;
using DryCycle.DevUI.DevTool.Extensions;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
void Refuse(Action action, string message) { try { action(); } catch { checks++; return; } throw new Exception(message); }
const string standard = "ROOMS\r\nAA : BB\r\nBB : AA\r\nEND ROOMS\r\nCREATURES\r\nAA : 2-Lizard\r\nEND CREATURES\r\n// author stays\r\n";
var ordinary = WorldFileVariants.Project(standard);
Check(!ordinary.RequiresExtension && ordinary.Standard == standard, "Untouched ordinary text stays byte-equivalent.");
var redundant = WorldFileVariants.Project(standard.Replace("AA : BB", "AA : BB<0>").Replace("BB : AA", "BB : AA<0>"));
Check(!redundant.RequiresExtension && redundant.Standard == standard, "A normal reciprocal pair needs no extension, including old redundant exact tokens.");
const string exact = "ROOMS\nAA : BB<1>, BB<0>\nBB : AA<1>, AA<0>\nEND ROOMS\nCREATURES\nAA : 2-Lizard\nEND CREATURES\n// author stays\n";
var special = WorldFileVariants.Project(exact);
Check(special.RequiresExtension && !special.Standard.Contains('<') && special.Extended == exact, "Repeated pairs retain exact endpoints only in the variant.");
string variant = WorldFileVariants.Encode(exact, "{\"version\":1,\"regions\":{}}");
Check(WorldFileVariants.Topology(variant).Contains("regions"), "Variant topology payload roundtrips.");
string newBase = special.Standard.Replace("2-Lizard", "2-DaddyLongLegs");
string overlay = WorldFileVariants.Overlay(newBase, variant);
Check(overlay.Contains("BB<1>") && overlay.Contains("DaddyLongLegs") && !overlay.Contains("2-Lizard"), "Reload overlays routes without restoring stale creatures from a variant.");
Refuse(() => WorldFileVariants.Overlay(newBase.Replace("AA : BB, BB", "AA : DISCONNECTED, BB"), variant), "External endpoint edits cannot be overwritten by stale variant data.");
Refuse(() => WorldFileVariants.Project(exact.Replace("BB<1>", "BB<99>")), "Invalid target indices are detected before any author write.");
string map = "AA: 1><2><3><4><1><Subregion><50><30\nBB: 5><6><7><8><0><><10><10\nDryCycle_PlayerMap_HiddenRooms: v1|\n// third party stays\n";
string layout = LegacyMapLayoutMigration.Header + "\nAA: 19.5><-20.25\n";
string migrated = LegacyMapLayoutMigration.Convert(map, layout, out int restored);
Check(restored == 1 && migrated.Contains("AA: 1><2><19.5><-20.25><1><Subregion><50><30"), "Layout migration changes only standard developer-coordinate fields.");
Check(!migrated.Contains("DryCycle_PlayerMap_HiddenRooms") && migrated.Contains("// third party stays"), "Empty old visibility records disappear while unknown comments survive.");
Refuse(() => LegacyMapLayoutMigration.Convert(map, layout + "ZZ: 0><1\n", out _), "Missing rooms cannot silently lose legacy positions.");
Refuse(() => LegacyMapLayoutMigration.Convert(map, layout.Replace("19.5", "NaN"), out _), "Nonfinite legacy positions require repair rather than silent defaults.");
Check(PlayerMapRoomVisibility.Write(Array.Empty<string>()) == null, "Ordinary saves emit no empty private marker.");
string offscreenMap = "OffscreenDenAA: 1><2><3><4><1><\n";
Check(LegacyMapLayoutMigration.Convert(offscreenMap, LegacyMapLayoutMigration.Header + "\nOffScreenDen_AA: 8><9\n", out int offscreenCount).Contains("><8><9><") && offscreenCount == 1, "Legacy offscreen aliases restore the standard existing record.");
var composed = PlayerMapConfigDocumentComposer.Compose(new[] { offscreenMap.TrimEnd(), "DryCycle_PlayerMap_HiddenRooms: v1|", "// retained" }, new Dictionary<string, string> { ["OffScreenDen_AA"] = "OffScreenDen_AA: 1><2><8><9><1><><0><0" }, null, null, null, false, null);
Check(composed.Count == 2 && composed[0].Contains("><8><9><") && composed[1] == "// retained", "Standard map composition accepts empty visibility and replaces offscreen aliases without duplicates.");
string hidden = PlayerMapRoomVisibility.Write(new[] { "AA" });
Check(hidden.StartsWith("// ") && PlayerMapRoomVisibility.Read(new[] { hidden }).Contains("AA"), "Hidden author state uses a standard-compatible comment.");

string root = Path.Combine(Path.GetTempPath(), "RainWright-variant-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
try
{
    AssetManager.SourceRoot = root;
    ModManager.ActiveMods.Add(new() { id = "variants.author", path = root });
    string path = Path.Combine(root, "world", "aa", "world_aa.txt"); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllText(path, standard);
    WorldFileVariantStore.Load("aa", path, standard); WorldFileVariantStore.Save("aa", path, standard.Replace("2-Lizard", "2-PinkLizard"));
    Check(!Directory.Exists(Path.Combine(root, "world", "aa", "rainwright")), "Ordinary authoring creates no extension directory.");
    string pure = special.Standard; File.WriteAllText(path, pure); WorldFileVariantStore.Load("aa", path, pure);
    string extra = WorldModificationWorkspace.VariantPath("aa"), combined = WorldModificationWorkspace.VariantPath("aa", true);
    WorldFileVariantStore.Save("aa", path, exact);
    Check(File.ReadAllText(path) == pure && File.Exists(extra) && !File.Exists(combined), "Special authoring creates exactly the standard and RainWright versions.");
    Check(Path.GetFileName(extra) == "world_aa_rainwright.xml" && Path.GetDirectoryName(extra).EndsWith("rainwright"), "Extension names are explicitly marked XML and outside importer scans.");
    using (EditorRegionVariantContributors.RegisterDryCycle(region => "<DryCycleRegion region=\"" + region + "\"/>")) WorldFileVariantStore.Save("aa", path, exact);
    Check(File.Exists(combined) && WorldFileVariants.DryCycle(File.ReadAllText(combined)).Contains("DryCycleRegion"), "Actual optional gameplay data creates the third combined XML version.");
    WorldFileVariantStore.Save("aa", path, exact);
    Check(File.Exists(combined) && WorldFileVariants.DryCycle(File.ReadAllText(combined)).Contains("DryCycleRegion"), "Disabling an optional provider preserves existing authored combined data.");
    string expected = File.ReadAllText(extra); File.WriteAllText(extra, "external-change");
    Refuse(() => WorldFileVariantStore.Save("aa", path, exact.Replace("2-Lizard", "2-Scavenger")), "Concurrent variant edits fail without overwriting either author source.");
    Check(File.ReadAllText(path) == pure && File.ReadAllText(extra) == "external-change", "Failed bundled writes preserve all current author data.");
    File.WriteAllText(extra, expected); WorldFileVariantStore.Load("aa", path, pure);
    WorldFileVariantStore.Save("aa", path, pure);
    Check(!File.Exists(extra) && !File.Exists(combined) && File.Exists(extra + ".bak") && File.Exists(combined + ".bak"), "Removing extension use clears generated variants with exact recoverable backups.");
    string one = Path.Combine(root, "one.txt"), two = Path.Combine(root, "two.txt"); File.WriteAllText(one, "one"); File.WriteAllText(two, "two");
    int authorizations = 0;
    Refuse(() => WorldVariantFileTransaction.Commit(new[] {
        new WorldVariantFileWrite { Path = one, Text = "updated-one", Expected = WorldVariantFileTransaction.Bytes("one") },
        new WorldVariantFileWrite { Path = two, Text = "updated-two", Expected = WorldVariantFileTransaction.Bytes("two") } }, p => { if (++authorizations == 4) throw new IOException("injected second commit failure"); }), "A failure after the first commit must be reported.");
    Check(File.ReadAllText(one) == "one" && File.ReadAllText(two) == "two", "A partial multi-file commit rolls back the first author file.");
}
finally
{
    if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path.");
    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
    Directory.Delete(root, true);
}
Console.WriteLine($"PASS {checks} standard/optional/combined variants, migration, ownership, conflict and rollback assertions.");
FormatCompatibility.Run(Check, Refuse);
Console.WriteLine($"PASS {checks} total XML schema, FloodForge enumeration, author preservation and repeat-load assertions.");

namespace DryCycle.DevUI.DevTool.World
{
    internal static class WorldTopologyRegistry
    {
        internal static string SerializeAuthoringState() => "{\"version\":1,\"regions\":{}}";
        internal static string OverlayLegacyRoutes(string region, string text) => text;
        internal static string[] RegionNames() => Array.Empty<string>();
    }
    internal static class WorldTextRegistry
    {
        internal static string AuthorText = "", LoadedPath = "", LoadError = "", LoadedRegion = "";
        internal static bool EnsureLoaded(string region) => false;
    }
}
