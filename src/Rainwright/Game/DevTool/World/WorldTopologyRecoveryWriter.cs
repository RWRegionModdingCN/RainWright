using System;
using System.IO;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Commit a topology recovered from an already-verified .bak without replacing
/// that only known-good copy with bytes from the broken primary file.
/// Failure leaves both source files in place; it must never fake success.
/// </summary>
internal static class WorldTopologyRecoveryWriter
{
    internal static void WritePreservingValidBackup(string target, string content)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Topology author file path is missing.", nameof(target));
        if (!File.Exists(target + ".bak"))
            throw new IOException("The verified WorldTopology backup disappeared before recovery save: " +
                target + ".bak");

        string parent = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("The topology author directory no longer exists: " + parent);

        string temp = target + ".recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, content ?? string.Empty);
            if (File.Exists(target))
            {
                // Atomic replacement keeps the verified .bak untouched.
                // If this filesystem cannot provide it, fail visibly rather
                // than using delete/move and risking the known-good backup.
                File.Replace(temp, target, null);
            }
            else
            {
                File.Move(temp, target);
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
