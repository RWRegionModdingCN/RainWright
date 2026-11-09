using System;
using System.IO;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

/// <summary>
/// Stable Cartography author-project identity and read-only legacy migration.
/// Keeping this filesystem-only helper independent of game plugin state lets
/// Rainwright use the same project names in its future standalone assembly.
/// </summary>
internal static class CartographyProjectPaths
{
    private static string FileName(string identity, string region) =>
        new string((region ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()) +
        "-" + CartographyStorage.HashText(identity ?? string.Empty).Substring(0, 16) +
        ".xml";

    internal static string Owned(string configRoot, string identity, string region) =>
        Path.Combine(configRoot, "Rainwright", "Cartography", FileName(identity, region));

    internal static string Legacy(string configRoot, string identity, string region) =>
        Path.Combine(configRoot, "DryCycle", "Cartography", FileName(identity, region));

    /// <summary>
    /// A user may explicitly open a historical DryCycle project, but the
    /// ordinary Save target must still remain within Rainwright's own root.
    /// Compare canonical path boundaries, not substring matches.
    /// </summary>
    internal static bool IsLegacyDefaultPath(string configRoot, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        string folder = Path.GetFullPath(
            Path.Combine(configRoot, "DryCycle", "Cartography"))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string absolute = Path.GetFullPath(path);
        StringComparison comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return absolute.StartsWith(folder, comparison);
    }

    /// <summary>
    /// Returns the current editor file first, then the previous game-owned file.
    /// This only identifies a read source, never an authoring write destination.
    /// </summary>
    internal static string ResolveReadable(string owned, string legacy)
    {
        if (File.Exists(owned)) return owned;
        return File.Exists(legacy) ? legacy : null;
    }
}
