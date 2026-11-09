using System;
using System.Collections.Generic;
using System.IO;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// A failed two-file export that could not fully restore its previous targets.
/// The original and rollback exceptions are retained in InnerException.
/// </summary>
internal sealed class PlayerMapRenderRollbackIncompleteException : IOException
{
    internal PlayerMapRenderRollbackIncompleteException(string message, AggregateException cause)
        : base(message, cause) { }
}

/// <summary>
/// Rainwright-owned, validated PNG + map_image authoring transaction. Both files
/// are staged before any live output is touched, and an interrupted earlier
/// transaction's backups are preserved for explicit recovery.
/// </summary>
internal static class PlayerMapRenderFileTransaction
{
    private static readonly object Sync = new();

    /// <summary>
    /// Fresh staging path for one game-side render job. Never reuse a fixed
    /// filename: a prior crashed frame may leave the only recoverable image
    /// in its staging file. The scheduler deletes only paths it just obtained.
    /// </summary>
    internal static string StagingPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Player Map staging target is missing.", nameof(target));
        return target + ".rainwright." + Guid.NewGuid().ToString("N") + ".tmp";
    }

    internal static void CommitValidatedPair(
        string stagedPng, string outputPng, string stagedMetadata, string outputMetadata)
    {
        if (string.IsNullOrWhiteSpace(stagedPng) ||
            string.IsNullOrWhiteSpace(outputPng) ||
            string.IsNullOrWhiteSpace(stagedMetadata) ||
            string.IsNullOrWhiteSpace(outputMetadata))
            throw new ArgumentException("Player Map output paths must not be empty.");

        string[] paths =
        {
            Path.GetFullPath(stagedPng), Path.GetFullPath(outputPng),
            Path.GetFullPath(stagedMetadata), Path.GetFullPath(outputMetadata)
        };
        for (int i = 0; i < paths.Length; i++)
            for (int j = i + 1; j < paths.Length; j++)
                if (string.Equals(paths[i], paths[j], StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Player Map staged and destination paths must be distinct.");

        lock (Sync)
        {
            if (!File.Exists(stagedPng) || !File.Exists(stagedMetadata))
                throw new IOException("Render staging files are incomplete.");

            // Validate *before* the first rename; UI lifecycle flags must not
            // allow a malformed PNG or map_image to replace known-good output.
            PlayerMapStagedOutputValidator.ValidatePair(stagedPng, stagedMetadata);

            string pngBackup = outputPng + ".rainwright.bak";
            string metaBackup = outputMetadata + ".rainwright.bak";
            if (File.Exists(pngBackup) || File.Exists(metaBackup))
                throw new IOException(
                    "A previous Rainwright map export backup remains. Restore or archive " +
                    "both outputs and their backups before exporting again.");

            bool pngBacked = false, metaBacked = false;
            bool pngInstalled = false, metaInstalled = false;
            try
            {
                if (File.Exists(outputPng))
                {
                    File.Move(outputPng, pngBackup);
                    pngBacked = true;
                }
                if (File.Exists(outputMetadata))
                {
                    File.Move(outputMetadata, metaBackup);
                    metaBacked = true;
                }

                File.Move(stagedPng, outputPng);
                pngInstalled = true;
                File.Move(stagedMetadata, outputMetadata);
                metaInstalled = true;
            }
            catch (Exception failure)
            {
                // A rollback error must not be hidden: retaining a backup is
                // safer than claiming failure with a silently damaged pair.
                List<Exception> rollbackErrors = new();
                Restore(outputMetadata, metaBackup, metaInstalled, metaBacked, rollbackErrors);
                Restore(outputPng, pngBackup, pngInstalled, pngBacked, rollbackErrors);
                if (rollbackErrors.Count > 0)
                {
                    rollbackErrors.Insert(0, failure);
                    throw new PlayerMapRenderRollbackIncompleteException(
                        "Player Map export failed and rollback was incomplete; Rainwright backups were retained.",
                        new AggregateException(rollbackErrors));
                }
                throw;
            }

            finally
            {
                DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Hide(pngBackup);
                DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Hide(metaBackup);
            }

            // The pair is fully installed. Cleanup must never attempt rollback
            // after commit simply because a backup file is locked.
            TryDelete(pngBackup);
            TryDelete(metaBackup);
        }
    }

    private static void Restore(
        string target, string backup, bool installed, bool backed, List<Exception> errors)
    {
        try
        {
            if (installed) File.Delete(target);
            if (backed) File.Move(backup, target);
        }
        catch (Exception failure)
        {
            errors.Add(new IOException("Failed to restore Player Map output: " + target, failure));
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
