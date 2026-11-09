using System;
using System.Collections.Generic;
using System.IO;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>Authoring adapter for community-standard modify/world files. Game merging is unchanged.</summary>
internal static class WorldModificationWorkspace
{
    private sealed class Document
    {
        internal string ModId, Region, Root, Relative, Source, Patch, Baseline, Current, OriginalPatch, ExpectedPatch;
        internal bool Existed;
    }
    private static readonly StringComparer PathComparer = Path.DirectorySeparatorChar == '\\'
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly Dictionary<string, Document> documents = new(PathComparer);
    private static ModManager.Mod regionMod;
    private static string regionContext = "";
    internal static bool HasRegionOwner => regionMod != null;
    internal static string RegionContext => regionContext;
    internal static string RegionModRoot => regionMod?.path;

    internal static string VariantPath(string region, bool dryCycle = false)
    {
        region = (region ?? "").Trim().ToLowerInvariant();
        string relative = "world/" + region + "/rainwright/world_" + region + (dryCycle ? "_rainwright_drycycle.xml" : "_rainwright.xml");
        string root = Root("world/" + region + "/world_" + region + ".txt", region);
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string path) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative)) throw new IOException("Unsafe region variant path.");
        return path;
    }

    internal static string[] LegacyVariantPaths(string region, bool dryCycle)
    {
        string current = VariantPath(region, dryCycle);
        return new[] { Path.ChangeExtension(current, ".txt"), Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(current)), Path.GetFileNameWithoutExtension(current) + ".txt") };
    }

    internal static void AuthorizeRegionMigration(string region, string path)
    {
        string relativeWorld = "world/" + region.ToLowerInvariant() + "/world_" + region.ToLowerInvariant() + ".txt";
        if (regionMod == null || !string.Equals(regionContext, region, StringComparison.OrdinalIgnoreCase) || ModManager.ActiveMods?.Contains(regionMod) != true)
            throw new IOException("Region migration owner is no longer active.");
        // The load boundary already selected the owner. Never repeat all-Mod discovery for each
        // backup or migration staging file in this same bounded transaction.
        string root = Root(relativeWorld, region, regionAlreadyBound: true);
        string prefix = Path.GetFullPath(Path.Combine(root, "world", region.ToLowerInvariant())).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(path);
        string roomsPrefix = Path.GetFullPath(Path.Combine(root, "world", region.ToLowerInvariant() + "-rooms")).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        bool worldBackup = string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Path.Combine(root, "world")), comparison) && full.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
        if (!full.StartsWith(prefix, comparison) && !full.StartsWith(roomsPrefix, comparison) && !worldBackup)
            throw new IOException("Migration path left the current author region: " + path);
        string relative = full.Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length + 1);
        if (!global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative)) throw new IOException("Redirected migration path: " + path);
    }

    internal static void AuthorizeVariant(string region, string path)
    {
        if (!PathComparer.Equals(path, VariantPath(region)) && !PathComparer.Equals(path, VariantPath(region, true)))
            throw new IOException("Region variant owner changed; the pending data was preserved.");
    }

    internal static bool TryAuthorizeVariant(string path, out string owner)
    {
        owner = "";
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || ModManager.ActiveMods == null) return false;
        string full = Path.GetFullPath(path);
        foreach (var mod in ModManager.ActiveMods)
        {
            if (!IsContentMod(mod) || string.IsNullOrWhiteSpace(mod.path)) continue;
            string prefix = Path.GetFullPath(mod.path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            string[] parts = full.Substring(prefix.Length).Replace('\\', '/').Split('/');
            if (parts.Length != 4 || !parts[0].Equals("world", StringComparison.OrdinalIgnoreCase) || !parts[2].Equals("rainwright", StringComparison.OrdinalIgnoreCase)) return false;
            string region = parts[1];
            if (!parts[3].Equals("world_" + region + "_rainwright.xml", StringComparison.OrdinalIgnoreCase) && !parts[3].Equals("world_" + region + "_rainwright_drycycle.xml", StringComparison.OrdinalIgnoreCase)) return false;
            if (!BindRegion(region) || !ReferenceEquals(regionMod, mod)) return false;
            if (!PathComparer.Equals(full, VariantPath(region)) && !PathComparer.Equals(full, VariantPath(region, true))) return false;
            owner = mod.id; return true;
        }
        return false;
    }

    internal static void ArchiveLegacy(string path, byte[] expected)
    {
        if (!File.Exists(path)) return;
        byte[] actual = File.ReadAllBytes(path);
        if (actual.Length != expected.Length) throw new IOException("Legacy file changed during migration: " + path);
        for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) throw new IOException("Legacy file changed during migration: " + path);
        string backup = path + ".rainwright-migrated-" + Guid.NewGuid().ToString("N") + ".bak";
        // A rename preserves every original byte and removes the misleading .txt filename from importers.
        File.Move(path, backup);
        Persistence.EditorBackupFiles.Hide(backup);
    }
    internal static int BindingRevision { get; private set; }
    internal static string AuthorRoot(string relative, string region) => Root(relative, region);

    internal static bool BindRegion(string region)
    {
        region = region?.Trim().ToLowerInvariant() ?? "";
        ModManager.Mod found = null;
        var mods = ModManager.ActiveMods;
        // A complete region is the primary ownership evidence. A content Mod with a patch or
        // region room directory is the fallback for a region inherited from the base game.
        if (mods != null && region.Length > 0)
            for (int kind = 0; kind < 3 && found == null; kind++)
            for (int i = mods.Count - 1; i >= 0; i--)
            {
                var mod = mods[i];
                if (!IsContentMod(mod)) continue;
                foreach (string root in new[] { mod.path })
                {
                    string relative = kind == 0 ? "world/" + region + "/world_" + region + ".txt"
                        : kind == 1 ? "modify/world/" + region + "/world_" + region + ".txt" : "world/" + region + "-rooms";
                    if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string anchor) ||
                        !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative)) continue;
                    if (kind < 2 ? File.Exists(anchor) : Directory.Exists(anchor)) { found = mod; break; }
                }
                if (found != null) break;
            }
        if (regionContext != region || !ReferenceEquals(regionMod, found))
        { regionContext = region; regionMod = found; unchecked { BindingRevision++; } }
        return found != null;
    }

    internal static bool IsContentMod(ModManager.Mod mod)
    {
        string id = mod?.id?.ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(id) && id != "moreslugcats" && id != "watcher" && id != "rwremix" && id != "devtools" &&
            id != "jollycoop" && id != "expedition" && id != "moreslugcatswatcher" && id != "rainwright";
    }

    private static List<string> Roots(ModManager.Mod mod)
    {
        var roots = new List<string>();
        if (mod.hasTargetedVersionFolder) roots.Add(mod.TargetedPath);
        if (mod.hasNewestFolder) roots.Add(mod.NewestPath);
        roots.Add(mod.path);
        return roots;
    }

    private static string RegionOf(string relative)
    {
        string[] parts = relative?.Replace('\\', '/').Split('/') ?? Array.Empty<string>();
        if (parts.Length < 3 || !parts[0].Equals("world", StringComparison.OrdinalIgnoreCase)) return null;
        string region = parts[1];
        if (region.EndsWith("-rooms", StringComparison.OrdinalIgnoreCase)) region = region.Substring(0, region.Length - 6);
        return region.Equals("gates", StringComparison.OrdinalIgnoreCase) ? null : region;
    }

    private static string Root(string relative = null, string region = null, bool regionAlreadyBound = false)
    {
        region ??= RegionOf(relative) ?? regionContext;
        if (!regionAlreadyBound && !BindRegion(region)) throw new InvalidOperationException("Cannot locate the active content Mod that owns region '" + region + "'. No author file was redirected.");
        // Region author data belongs to <mod>/world, independently of versioned plugin folders.
        // Keep optional asset resolution separate: this rule must not redirect background assets.
        var roots = IsWorldRelative(relative) ? new List<string> { regionMod.path } : Roots(regionMod);
        string root = null;
        foreach (string candidate in roots)
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate)) { root = candidate; break; }
        if (relative != null)
            foreach (string candidate in roots)
            {
                if (global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(candidate, relative, out string direct) && File.Exists(direct) ||
                    global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(candidate, "modify/" + relative, out string patch) && File.Exists(patch))
                { root = candidate; break; }
            }
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, "modify/world/.author-path-check"))
            throw new IOException("The region's Mod has no safe author directory.");
        return Path.GetFullPath(root);
    }

    internal static string Resolve(string relative)
    {
        relative = relative.Replace('\\', '/').ToLowerInvariant();
        if (!relative.StartsWith("world/", StringComparison.Ordinal) || !relative.EndsWith(".txt", StringComparison.Ordinal))
            return "";
        string region = RegionOf(relative) ?? regionContext;
        if (!BindRegion(region)) return "";
        // The owner was just resolved above on this game-thread call. Reuse that result within
        // this resolution; later saves/recovery still perform their own fresh authorization.
        string root = Root(relative, region, regionAlreadyBound: true);
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string direct))
            throw new IOException("Invalid relative author path.");
        // Existing full source files in an author's custom region retain their normal writer.
        if (File.Exists(direct))
        {
            if (!global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedExistingModSource(root, relative, direct))
                throw new IOException("The author file redirects outside its Mod.");
            return direct;
        }
        string patchRelative = "modify/" + relative;
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, patchRelative, out string patch) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, patchRelative))
            throw new IOException("The modification destination redirects outside its Mod.");
        if (documents.ContainsKey(patch)) return patch;
        string source = AssetManager.ResolveFilePath(relative);
        // A new file in the region's own Mod is a normal author file, not an ADD-only patch.
        if (!File.Exists(source) && !File.Exists(patch)) return direct;
        string text = File.Exists(source) ? File.ReadAllText(source) : "";
        string original = File.Exists(patch) ? File.ReadAllText(patch) : "";
        documents.Add(patch, new Document { ModId = regionMod.id, Region = region, Root = root, Relative = relative,
            Source = source, Patch = patch, Baseline = text, Current = text,
            OriginalPatch = original, ExpectedPatch = original, Existed = File.Exists(patch) });
        return patch;
    }

    internal static bool IsPatch(string path) => path != null && documents.ContainsKey(path);
    internal static bool CanRead(string path) => IsPatch(path) || File.Exists(path);
    internal static string ReadAllText(string path) => documents.TryGetValue(path, out Document doc) ? doc.Current : File.ReadAllText(path);
    internal static string[] ReadAllLines(string path)
    {
        using var reader = new StringReader(ReadAllText(path));
        var lines = new List<string>(); string line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);
        return lines.ToArray();
    }

    internal static string Encode(string path, string content)
    {
        if (!documents.TryGetValue(path, out Document doc)) return content;
        Authorize(doc);
        return ModificationFileComposer.Compose(doc.Baseline, content, doc.OriginalPatch);
    }

    internal static void Write(string path, string content, Action<string, string> atomicWrite)
    {
        if (!documents.TryGetValue(path, out Document doc))
        {
            if (!IsActiveAuthorFile(path, requireExisting: false)) throw new IOException("The author destination no longer belongs to an active Mod. Its content was preserved.");
            atomicWrite(path, content); return;
        }
        string patch = Encode(path, content);
        if (patch.Length > 0 || doc.Existed) atomicWrite(path, patch);
        doc.ExpectedPatch = patch;
        doc.Existed = File.Exists(path);
        doc.Current = content;
    }

    private static void Authorize(Document doc)
    {
        if (!PathComparer.Equals(Root(doc.Relative, doc.Region), doc.Root) || doc.ModId != regionMod?.id ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(doc.Root, "modify/" + doc.Relative))
            throw new IOException("The modification file's author Mod changed.");
        if (global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(doc.Root, doc.Relative, out string direct) && File.Exists(direct))
            throw new IOException("A full author file appeared after this patch was loaded. Reload the author sources before saving; both files were preserved.");
        if (File.Exists(doc.Patch) != doc.Existed ||
            doc.Existed && File.ReadAllText(doc.Patch) != doc.ExpectedPatch)
            throw new IOException("The modification file changed outside RainWright. Restart Rain World to reload the author sources; the external edit was preserved.");
    }

    internal static bool TryAuthorize(string path, out string modId)
    {
        modId = "";
        if (!documents.TryGetValue(path, out Document doc)) return false;
        Authorize(doc); modId = doc.ModId; return true;
    }

    internal static bool TryAuthorizeBackground(string path, out string modId)
    {
        modId = "";
        if (!Path.IsPathRooted(path) || !path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;
        string relative = "assets/regionkit/backgrounds/" + Path.GetFileName(path);
        string root = null;
        if (ModManager.ActiveMods != null)
            foreach (var mod in ModManager.ActiveMods)
            {
                if (!IsContentMod(mod)) continue;
                foreach (string candidate in Roots(mod))
                    if (global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(candidate, relative, out string expectedPath) &&
                        PathComparer.Equals(Path.GetFullPath(path), expectedPath)) { root = candidate; modId = mod.id; break; }
                if (root != null) break;
            }
        if (root == null) return false;
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string expected) ||
            !PathComparer.Equals(Path.GetFullPath(path), expected) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative)) return false;
        string current = WorldAuthoringPathResolver.ResolveExistingSource(relative);
        if (current.Length > 0 && !PathComparer.Equals(current, expected)) return false;
        return true;
    }

    internal static string ResolveRoomSettings(string readPath)
    {
        if (IsPatch(readPath)) return readPath;
        // The existing room author file remains the native writer's destination. In particular,
        // do not turn an ordinary custom-room save into a modification script.
        if (IsActiveAuthorFile(readPath, requireExisting: true)) return readPath;
        string relative = RelativeWorldPath(readPath);
        if (relative == null) throw new IOException("Room settings are outside the standard world directory.");
        string target = WorldAuthoringPathResolver.ResolveExistingSource(relative);
        if (string.IsNullOrWhiteSpace(target)) throw new IOException("No writable author file or region Mod was found for these room settings.");
        return target;
    }

    private static bool IsActiveAuthorFile(string path, bool requireExisting)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) ||
            global::RainWright.IO.AuthoringSourcePaths.IsMergedCachePath(path) || ModManager.ActiveMods == null) return false;
        string full = Path.GetFullPath(path);
        if (IsVersionedWorldSource(full)) return false;
        foreach (var mod in ModManager.ActiveMods)
        {
            if (!IsContentMod(mod)) continue;
            foreach (string root in Roots(mod))
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string relative = full.Substring(prefix.Length);
                return (!requireExisting || File.Exists(full)) &&
                    global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative);
            }
        }
        return false;
    }

    private static bool IsWorldRelative(string relative) =>
        relative?.Replace('\\', '/').StartsWith("world/", StringComparison.OrdinalIgnoreCase) == true;

    internal static bool IsVersionedWorldSource(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || ModManager.ActiveMods == null) return false;
        string full = Path.GetFullPath(path);
        foreach (var mod in ModManager.ActiveMods)
        {
            if (mod == null) continue;
            foreach (string root in new[] { mod.hasTargetedVersionFolder ? mod.TargetedPath : null, mod.hasNewestFolder ? mod.NewestPath : null })
            {
                if (string.IsNullOrWhiteSpace(root)) continue;
                string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
                if (!full.StartsWith(prefix, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
                string relative = full.Substring(prefix.Length).Replace('\\', '/');
                if (IsWorldRelative(relative) || relative.StartsWith("modify/world/", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        return false;
    }

    internal static string AuxiliaryPath(string source, string fileName)
    {
        if (!documents.TryGetValue(source, out Document doc)) return Path.Combine(Path.GetDirectoryName(source), fileName);
        string relative = Path.Combine(Path.GetDirectoryName(doc.Relative), fileName);
        Authorize(doc);
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(doc.Root, relative, out string target) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(doc.Root, relative))
            throw new IOException("Invalid auxiliary author path.");
        return target;
    }

    internal static string TopologyPath(bool forSave)
    {
        const string relative = "world/WorldTopology.json";
        string root = Root(relative);
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string target) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative))
            throw new IOException("Invalid topology author path.");
        // Resolving/reading an inherited document must not create author files. The registry
        // keeps this destination pinned and only writes it after an actual edit is committed.
        return forSave || File.Exists(target) ? target : WorldAuthoringPathResolver.ResolveExistingSource(relative);
    }

    private static string RelativeWorldPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return null;
        string full = Path.GetFullPath(path);
        var roots = new List<string> { Path.Combine(RWCustom.Custom.RootFolderDirectory(), "mergedmods"), RWCustom.Custom.RootFolderDirectory() };
        if (ModManager.ActiveMods != null)
            foreach (var mod in ModManager.ActiveMods)
            {
                if (mod == null) continue;
                if (mod.hasTargetedVersionFolder) roots.Insert(0, mod.TargetedPath);
                if (mod.hasNewestFolder) roots.Insert(0, mod.NewestPath);
                roots.Add(mod.path);
            }
        foreach (string root in roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            string prefix = Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = full.Substring(prefix.Length).Replace('\\', '/');
            if (relative.StartsWith("world/", StringComparison.OrdinalIgnoreCase)) return relative;
        }
        return null;
    }
}
