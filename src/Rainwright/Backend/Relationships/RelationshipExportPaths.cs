using System;
using System.IO;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Owns only Rainwright's generated relationship source export.
/// An existing DryCycle export is a read-only import source, never an output.
/// The first Rainwright export overwrites the imported *copy* through the
/// existing atomic-replace path, preserving the previous text in .bak.
/// </summary>
internal static class RelationshipExportPaths
{
    private const string ExportFile = "relationship-overrides.txt";

    internal static string Current(string configRoot) =>
        Path.Combine(configRoot ?? throw new ArgumentNullException(nameof(configRoot)),
            "Rainwright", "Relationships", ExportFile);

    internal static string Legacy(string configRoot) =>
        Path.Combine(configRoot ?? throw new ArgumentNullException(nameof(configRoot)),
            "DryCycle", "Relationships", ExportFile);

    internal static void ImportLegacyIfNeeded(string configRoot)
    {
        string current = Current(configRoot);
        if (File.Exists(current)) return;

        string legacy = Legacy(configRoot);
        if (!File.Exists(legacy)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(current));
        // Create a unique temporary file in the destination filesystem; only
        // move it into place if the Rainwright-owned target is still absent.
        // On any failed copy, neither author-owned legacy text nor an existing
        // Rainwright export is modified.
        string staged = current + ".legacy-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(legacy, staged, overwrite: false);
            if (!File.Exists(current))
                File.Move(staged, current);
        }
        finally
        {
            if (File.Exists(staged)) File.Delete(staged);
        }
    }
}
