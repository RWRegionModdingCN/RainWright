using System;
using System.Collections.Generic;
using System.IO;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Authoring write failed and restoring the previous config also failed.
/// Includes the original failure and recovery failure for the editor error UI.
/// </summary>
internal sealed class PlayerMapConfigRollbackIncompleteException : IOException
{
    internal PlayerMapConfigRollbackIncompleteException(string message, AggregateException cause)
        : base(message, cause) { }
}

/// <summary>
/// Single-process authoring transaction for Player Map config output.
/// Path validation/World source selection happens in the game adapter.
/// Owned staging suffixes do not collide with DryCycle's historic *.drycycle.*
/// files; an interrupted or rejected write preserves the previous map config.
/// </summary>
internal static class PlayerMapConfigFileTransaction
{
    private static readonly object Sync = new();

    internal static void WriteAllLines(string target, IReadOnlyList<string> lines)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new ArgumentException("Player Map authoring target is empty.", nameof(target));
        if (lines == null) throw new ArgumentNullException(nameof(lines));

        lock (Sync)
        {
            string directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Never reuse an orphaned staging file from an interrupted save.
            // Each writer owns exactly the temporary path it generated.
            string temp = target + ".rainwright." + Guid.NewGuid().ToString("N") + ".tmp";
            bool preserveStaging = false;
            try
            {
                File.WriteAllLines(temp, ToArray(lines));
                CommitStaged(temp, target);
            }
            catch (PlayerMapConfigRollbackIncompleteException)
            {
                // The previous config could not be restored; the staged
                // authoring text may be the only complete recoverable copy.
                preserveStaging = true;
                throw;
            }
            finally
            {
                if (!preserveStaging) TryDelete(temp);
            }
        }
    }

    internal static void CommitStaged(string temp, string target)
    {
        if (!File.Exists(temp))
            throw new IOException("Player Map staging file was not created.");

        string backup = target + ".rainwright.bak";
        // Do not delete *any* backup from a previous save, even when the
        // current destination also exists. That backup may be the only good
        // state after an interrupted or manually recovered authoring session.
        if (File.Exists(backup) || Directory.Exists(backup))
            throw new IOException(
                "An earlier Player Map backup exists. Restore or archive it before saving: " + backup);

        bool backed = false;
        bool installed = false;
        try
        {
            if (File.Exists(target))
            {
                File.Move(target, backup);
                backed = true;
            }
            File.Move(temp, target);
            installed = true;
            TryDelete(backup);
        }
        catch (Exception original)
        {
            try
            {
                if (installed) File.Delete(target);
                if (backed) File.Move(backup, target);
            }
            catch (Exception rollbackError)
            {
                throw new PlayerMapConfigRollbackIncompleteException(
                    "Player Map config write failed and rollback was incomplete. " +
                    "Inspect the target, Rainwright backup and staged file before saving again. " +
                    "Staged path: " + temp + "; backup path: " + backup,
                    new AggregateException(original, rollbackError));
            }
            throw;
        }
        finally { DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Hide(backup); }
    }

    private static string[] ToArray(IReadOnlyList<string> lines)
    {
        string[] result = new string[lines.Count];
        for (int i = 0; i < lines.Count; i++)
            result[i] = lines[i] ?? string.Empty;
        return result;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The authoring write reports errors through the caller.
        }
    }
}
