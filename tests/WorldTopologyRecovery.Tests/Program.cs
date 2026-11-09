using System;
using System.IO;
using DryCycle.DevUI.DevTool.World;

internal static class Program
{
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "rainwright-topology-recovery-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "WorldTopology.json");
        try
        {
            Directory.CreateDirectory(root);
            string corrupt = "{ damaged author primary";
            string healthy = "{ \"regions\": {\"AA\": [] } }";
            string edited = "{ \"regions\": {\"AA\": [], \"BB\": [] } }";
            File.WriteAllText(target, corrupt);
            File.WriteAllText(target + ".bak", healthy);

            WorldTopologyRecoveryWriter.WritePreservingValidBackup(target, edited);
            Check(File.ReadAllText(target) == edited,
                "Recovered author edits commit to the original topology location.");
            Check(File.ReadAllText(target + ".bak") == healthy,
                "The last verified backup stays byte-for-byte valid after recovery save.");
            Check(Directory.GetFiles(root, "*.tmp").Length == 0,
                "Successful recovered save cleans temporary files.");

            File.Delete(target);
            WorldTopologyRecoveryWriter.WritePreservingValidBackup(target, healthy);
            Check(File.ReadAllText(target) == healthy &&
                  File.ReadAllText(target + ".bak") == healthy,
                "Recovery succeeds when the damaged primary was removed, without touching the backup.");

            File.Delete(target + ".bak");
            bool refused = false;
            try { WorldTopologyRecoveryWriter.WritePreservingValidBackup(target, edited); }
            catch (IOException) { refused = true; }
            Check(refused && File.ReadAllText(target) == healthy,
                "Disappearing backup refuses replacement and preserves the primary author file.");

            refused = false;
            try { WorldTopologyRecoveryWriter.WritePreservingValidBackup("", edited); }
            catch (ArgumentException) { refused = true; }
            Check(refused, "An empty or unresolved author path is rejected.");

            File.WriteAllText(target + ".bak", healthy);
            string absentParent = Path.Combine(root, "missing", "WorldTopology.json");
            refused = false;
            try { WorldTopologyRecoveryWriter.WritePreservingValidBackup(absentParent, edited); }
            catch (IOException) { refused = true; }
            Check(refused && !File.Exists(absentParent),
                "Recovery must not create an unverified destination path.");

            Console.WriteLine("PASS: " + assertions +
                " assertions; production topology recovery never overwrites the sole good backup.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch { }
        }
    }
}
