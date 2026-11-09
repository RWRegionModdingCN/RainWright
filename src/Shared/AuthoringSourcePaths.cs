using System;
using System.IO;

#if RAINWRIGHT_RUNTIME
namespace RainWright.IO;
#else
namespace DryCycle.Shared;
#endif

/// <summary>
/// Editor-independent guard for generated Rain World mergedmods paths.
/// File ownership and writable authoring destinations are resolved separately.
/// </summary>
internal static class AuthoringSourcePaths
{
    /// <summary>
    /// Resolve an author file strictly inside the active Mod's own source directory.
    /// Neither absolute paths nor parent traversal may redirect writes into the game,
    /// another Mod, or the mergedmods cache.
    /// </summary>
    internal static bool TryCombineUnderModRoot(string modRoot, string relativePath, out string candidate)
    {
        candidate = string.Empty;
        if (string.IsNullOrWhiteSpace(modRoot) || string.IsNullOrWhiteSpace(relativePath))
            return false;

        try
        {
            string normalized = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) || normalized.IndexOf(':') >= 0)
                return false;

            string root = Path.GetFullPath(modRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length == 0)
                return false;

            string resolved = Path.GetFullPath(Path.Combine(root, normalized));
            if (!resolved.StartsWith(
                    root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            candidate = resolved;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Crash recovery requires a physical source that cannot redirect writes
    /// through a symlink or junction below its active Mod root. Plain lexical
    /// containment is insufficient for write authorization.
    /// </summary>
    internal static bool IsUnredirectedExistingModSource(
        string modRoot, string relativePath, string expectedTarget)
    {
        if (!TryCombineUnderModRoot(modRoot, relativePath, out string candidate) ||
            string.IsNullOrWhiteSpace(expectedTarget))
            return false;

        try
        {
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetFullPath(expectedTarget), candidate, comparison) ||
                !File.Exists(candidate))
                return false;

            string root = Path.GetFullPath(modRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return false;

            string relative = candidate.Substring(root.Length + 1);
            string[] segments = relative.Split(Path.DirectorySeparatorChar);
            string cursor = root;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                    return false;
                cursor = Path.Combine(cursor, segments[i]);
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            return true;
        }
        catch (Exception)
        {
            // If a directory vanished or link attributes are inaccessible,
            // no author source is safe to restore into.
            return false;
        }
    }

    /// <summary>
    /// Checks a potential new gameplay author target. Missing trailing files
    /// and directories are permitted for first-time Save, but every existing
    /// component must be a real directory/file, not a link or junction.
    /// </summary>
    internal static bool IsUnredirectedModAuthorPath(string modRoot, string relativePath)
    {
        if (!TryCombineUnderModRoot(modRoot, relativePath, out string candidate))
            return false;
        try
        {
            string root = Path.GetFullPath(modRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                return false;

            string[] segments = candidate.Substring(root.Length + 1)
                .Split(Path.DirectorySeparatorChar);
            string cursor = root;
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Length == 0 || segments[i] == "." || segments[i] == "..")
                    return false;
                cursor = Path.Combine(cursor, segments[i]);
                // This file may not exist yet, but every already-existing
                // parent must be inspected before gameplay authoring writes.
                if (!Directory.Exists(cursor) && !File.Exists(cursor))
                    continue;
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static bool IsMergedCachePath(string path) =>
        IsMergedCachePath(path, global::RWCustom.Custom.RootFolderDirectory());

    internal static bool IsMergedCachePath(string path, string rootFolder)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(rootFolder))
            return false;

        try
        {
            string root = Path.GetFullPath(rootFolder)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string merged = Path.GetFullPath(Path.Combine(root, "mergedmods"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(path);
            return candidate.StartsWith(merged, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
