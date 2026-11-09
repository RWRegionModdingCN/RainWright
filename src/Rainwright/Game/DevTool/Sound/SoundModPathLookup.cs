using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace DryCycle.DevUI.DevTool.Sound;

internal static class SoundModPathLookup
{
    private sealed class Roots
    {
        internal string ModPath, WorkingDirectory, Targeted, Newest, Base;
        internal void Update(ModManager.Mod mod, string directory)
        {
            if (directory != null && ModPath == mod.path && WorkingDirectory == directory) return;
            // These two game properties are fixed children of mod.path, not directory scans.
            string targeted = Normalize(mod.TargetedPath, true);
            string newest = Normalize(mod.NewestPath, true);
            string root = Normalize(mod.path, true);
            Targeted = targeted; Newest = newest; Base = root;
            ModPath = mod.path; WorkingDirectory = directory;
        }
        internal bool Contains(string file) => Under(file, Targeted) || Under(file, Newest) || Under(file, Base);
    }
    private static readonly ConditionalWeakTable<ModManager.Mod, Roots> CachedRoots = new();

    internal static bool TryFind(string path, out ModManager.Mod owner)
    {
        owner = null;
        string file = Normalize(path, false);
        if (file == null) return false;
        string directory;
        try { directory = Directory.GetCurrentDirectory(); }
        catch (IOException) { directory = null; }
        catch (System.Security.SecurityException) { directory = null; }
        // Keep live load order, identity and path checks, including replacements at the same count.
        for (int i = ModManager.ActiveMods.Count - 1; i >= 0; i--)
        {
            var mod = ModManager.ActiveMods[i];
            if (mod == null) continue;
            var roots = CachedRoots.GetValue(mod, _ => new Roots());
            roots.Update(mod, directory);
            if (!roots.Contains(file)) continue;
            owner = mod;
            return true;
        }
        return false;
    }
    private static bool Under(string file, string root) =>
        root != null && file.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    private static string Normalize(string path, bool directory)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return directory ? full + Path.DirectorySeparatorChar : full;
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (IOException) { return null; }
        catch (System.Security.SecurityException) { return null; }
    }
}
