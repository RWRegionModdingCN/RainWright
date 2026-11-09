using System;
using System.IO;
using DryCycle.DevUI.DevTool.AutoSave;
using BepInEx;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "rainwright-relationships-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string current = RelationshipExportPaths.Current(root);
            string legacy = RelationshipExportPaths.Legacy(root);

            Check(current != legacy && current.Contains(Path.DirectorySeparatorChar + "Rainwright" + Path.DirectorySeparatorChar),
                "Relationship exports must be owned by Rainwright, not DryCycle.");
            RelationshipExportPaths.ImportLegacyIfNeeded(root);
            Check(!File.Exists(current), "Fresh Rainwright installs create no empty export.");

            Directory.CreateDirectory(Path.GetDirectoryName(legacy));
            File.WriteAllText(legacy, "// hand-authored legacy relationships");
            RelationshipExportPaths.ImportLegacyIfNeeded(root);
            Check(File.Exists(current) && File.ReadAllText(current) == File.ReadAllText(legacy),
                "First import copies the existing DryCycle relationship export without changing its text.");
            File.WriteAllText(current, "// new Rainwright export");
            RelationshipExportPaths.ImportLegacyIfNeeded(root);
            Check(File.ReadAllText(current) == "// new Rainwright export" &&
                  File.ReadAllText(legacy) == "// hand-authored legacy relationships",
                "A Rainwright-owned export always wins later; the DryCycle original remains untouched.");

            File.Delete(current);
            File.WriteAllText(legacy, "// updated legacy source");
            RelationshipExportPaths.ImportLegacyIfNeeded(root);
            Check(File.ReadAllText(current) == "// updated legacy source",
                "Deleting only the Rainwright copy permits explicit reimport without rewriting DryCycle.");

            // Real exporter uses File.Replace with .bak on an existing file.
            string tmp = current + ".tmp";
            File.WriteAllText(tmp, "// exported UI relationships");
            File.Replace(tmp, current, current + ".bak");
            Check(File.ReadAllText(current) == "// exported UI relationships" &&
                  File.ReadAllText(current + ".bak") == "// updated legacy source" &&
                  File.ReadAllText(legacy) == "// updated legacy source",
                "First generated export retains the imported source as .bak and never edits the legacy file.");

            // Exercise the real export endpoint, not just its path helper.
            // The existing generated source must be backed up before replacing it.
            Paths.ConfigPath = root;
            DevInterface.RelationshipPage.changedRelationships.Clear();
            var scav = new DevInterface.RelationshipKey("Scavenger");
            var lizard = new DevInterface.RelationshipKey("LizardGreen");
            var centipede = new DevInterface.RelationshipKey("Centipede");
            DevInterface.RelationshipPage.changedRelationships[scav] =
                new System.Collections.Generic.Dictionary<DevInterface.RelationshipKey, float>
                {
                    [lizard] = 0.7f,
                    [centipede] = -0.2f
                };
            Check(RelationshipSaveAdapter.TryGetText(out string preparedPath, out string preparedText) &&
                  preparedPath == current &&
                  preparedText.Contains("Centipede") &&
                  preparedText.IndexOf("Centipede", StringComparison.Ordinal) <
                      preparedText.IndexOf("LizardGreen", StringComparison.Ordinal),
                "Production relationship code is deterministically sorted into the Rainwright export target.");
            string previous = File.ReadAllText(current);
            Check(RelationshipSaveAdapter.Export() &&
                  File.ReadAllText(current).Contains("LizardGreen") &&
                  File.ReadAllText(current + ".bak") == previous &&
                  File.ReadAllText(legacy) == "// updated legacy source",
                "Production exporter replaces only its owned file and retains prior author data in backup.");
            Check(DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Messages.Count > 0,
                "Production exporter emits Rainwright-owned success diagnostics.");

            DevInterface.RelationshipPage.changedRelationships.Clear();
            Check(RelationshipSaveAdapter.Export() &&
                  File.ReadAllText(current).Contains("empty export") &&
                  File.ReadAllText(legacy) == "// updated legacy source",
                "An explicit empty export updates only Rainwright and never clears the legacy original.");

            // Exercise a real I/O failure: a regular file cannot contain the
            // Rainwright/Relationships directory. False success is forbidden.
            string blockingFile = Path.Combine(root, "blocked-config-root");
            File.WriteAllText(blockingFile, "not a directory");
            Paths.ConfigPath = blockingFile;
            int errorsBefore = DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Errors.Count;
            Check(!RelationshipSaveAdapter.Export() &&
                  DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Errors.Count == errorsBefore + 1,
                "A failing directory/export operation returns false and records its original exception.");
            Paths.ConfigPath = root;

            Console.WriteLine("PASS: " + checks + " assertions; real Rainwright relationship source migration and precedence.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }
}
