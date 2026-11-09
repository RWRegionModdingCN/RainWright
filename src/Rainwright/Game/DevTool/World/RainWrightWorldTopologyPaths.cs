using System;
using System.Collections.Generic;
using System.IO;
namespace DryCycle.DevUI.DevTool.World;

internal static class RainWrightWorldTopologyPaths
{
    private const string FileName = "WorldTopology.json";
    private const int MaxAssemblyParentSearchDepth = 8;
    internal static string Resolve(bool forSave)
    {
        if (WorldModificationWorkspace.HasRegionOwner)
            return forSave ? WorldModificationWorkspace.VariantPath(WorldModificationWorkspace.RegionContext) : WorldFileVariantStore.TopologyReadPath();
        // Preserve existing active Mod author sources after moving the runtime.
        // Only a new document defaults to the active RainWright Mod.
        string existing = WorldAuthoringPathResolver.ResolveExistingSource("world/" + FileName);
        if (!string.IsNullOrEmpty(existing)) return existing;
        string firstWritable = null;
        // A non-null ActiveMods list is authoritative even when it is empty.
        // An empty list means "no active gameplay Mod", not "startup".
        // The assembly fallback is available only before this list exists.
        var activeMods = ModManager.ActiveMods;
        try
        {
            if (activeMods != null)
            {
                for (int i = activeMods.Count - 1; i >= 0; i--)
                {
                    ModManager.Mod mod = activeMods[i];
                    if (mod == null ||
                        !string.Equals(mod.id, "rainwright",
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    string[] roots =
                    {
                        mod.hasTargetedVersionFolder ? mod.TargetedPath : null,
                        mod.hasNewestFolder ? mod.NewestPath : null,
                        mod.path,
                        mod.basePath
                    };
                    for (int rootIndex = 0; rootIndex < roots.Length; rootIndex++)
                    {
                        if (!TryOwnedTopologyPath(roots[rootIndex], out string candidate))
                            continue;
                        if (File.Exists(candidate)) return candidate;
                        if (forSave && firstWritable == null)
                            firstWritable = candidate;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning(
                "WorldTopology active Mod path lookup failed: " + ex);
        }

        if (forSave && firstWritable != null)
            return firstWritable;
        // Never let an empty or newly initialized list fall through to a
        // stale on-disk RainWright DLL, including a list installed during lookup.
        if (activeMods != null || ModManager.ActiveMods != null)
            return null;

        // Compatibility with early startup, before ActiveMods is populated: the
        // assembly directory is trusted only if its actual modinfo.json has the
        // expected gameplay Mod ID. Bare 'nearest modinfo.json' is not ownership.
        return ResolvePathFromContainingMod(forSave);
    }

    private static bool TryOwnedTopologyPath(string modRoot, out string candidate)
    {
        string relative = Path.Combine("world", FileName);
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(
                modRoot, relative, out candidate) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(
                modRoot, relative))
            return false;
        if (!global::RainWright.IO.AuthoringSourcePaths.IsMergedCachePath(candidate))
            return true;

        candidate = string.Empty;
        return false;
    }

    private static string ResolvePathFromContainingMod(bool forSave)
    {
        try
        {
            string assemblyPath = typeof(RainWrightWorldTopologyPaths).Assembly.Location;
            string directoryPath = string.IsNullOrWhiteSpace(assemblyPath)
                ? null
                : Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
            if (string.IsNullOrWhiteSpace(directoryPath)) return null;

            DirectoryInfo directory = new(directoryPath);
            for (int depth = 0;
                 directory != null && depth < MaxAssemblyParentSearchDepth;
                 depth++, directory = directory.Parent)
            {
                string manifest = Path.Combine(directory.FullName, "modinfo.json");
                if (!File.Exists(manifest)) continue;

                object raw = Json.Deserialize(File.ReadAllText(manifest));
                if (raw is not Dictionary<string, object> info ||
                    !info.TryGetValue("id", out object modId) ||
                    !string.Equals(modId as string, "rainwright",
                        StringComparison.OrdinalIgnoreCase))
                {
                    // An unrelated containing Mod cannot be the owner. Do not
                    // continue walking upward and accidentally claim a parent.
                    return null;
                }

                if (!TryOwnedTopologyPath(directory.FullName, out string candidate))
                    return null;
                return forSave || File.Exists(candidate) ? candidate : null;
            }
        }
        catch (Exception ex)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning(
                "WorldTopology verified assembly Mod path lookup failed: " + ex);
        }
        // No active RainWright author Mod or verified manifest: refuse to save.
        // AssetManager's read/mergedmods destination is not a writable fallback.
        return null;
    }
}
