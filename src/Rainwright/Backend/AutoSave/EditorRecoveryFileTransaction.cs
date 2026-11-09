using System;
using System.IO;
using System.Security.Cryptography;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Rainwright-owned immutable recovery payload validation and author-file
/// replacement. The caller MUST authorize the active Mod/author source first;
/// this portable layer enforces snapshot integrity, external-edit conflict
/// detection, same-volume atomic replacement and non-destructive backups.
/// </summary>
internal static class EditorRecoveryFileTransaction
{
    internal static string HashFile(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    internal static void Restore(
        string payloadPath,
        string expectedPayloadHash,
        string targetPath,
        bool targetExistedWhenOffered,
        string targetHashWhenOffered)
    {
        if (string.IsNullOrWhiteSpace(payloadPath) ||
            string.IsNullOrWhiteSpace(targetPath) ||
            string.IsNullOrWhiteSpace(expectedPayloadHash))
            throw new ArgumentException("Recovery needs a payload, author target and hash.");

        string target = Path.GetFullPath(targetPath);
        string dir = Path.GetDirectoryName(target);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            throw new DirectoryNotFoundException("The authoring folder no longer exists: " + dir);

        if (!File.Exists(payloadPath) ||
            !string.Equals(HashFile(payloadPath), expectedPayloadHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Snapshot integrity check failed.");

        VerifyAuthorUnchanged(target, targetExistedWhenOffered, targetHashWhenOffered);

        // Create a unique, exclusive staging file next to the author file.
        // This permits atomic File.Replace on the same volume and never
        // overwrites a predictable old staging artifact.
        string staged = target + "." + Guid.NewGuid().ToString("N") + ".Rainwright-recovery.tmp";
        try
        {
            File.Copy(payloadPath, staged, overwrite: false);
            if (!string.Equals(HashFile(staged), expectedPayloadHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recovery staging copy failed snapshot verification.");

            // An external editor can change the source during our copy; check
            // again immediately before the atomic write.
            VerifyAuthorUnchanged(target, targetExistedWhenOffered, targetHashWhenOffered);

            if (targetExistedWhenOffered)
            {
                string backup = target + ".Rainwright-before-recovery.bak";
                if (File.Exists(backup))
                    backup = target + "." + Guid.NewGuid().ToString("N") +
                        ".Rainwright-before-recovery.bak";
                DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(staged, target, backup);
            }
            else
            {
                File.Move(staged, target);
            }
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (Exception) { /* Retain the original operation exception. */ }
        }
    }

    private static void VerifyAuthorUnchanged(
        string target, bool existedWhenOffered, string hashWhenOffered)
    {
        bool exists = File.Exists(target);
        if (exists != existedWhenOffered ||
            (exists && !string.Equals(HashFile(target), hashWhenOffered,
                StringComparison.OrdinalIgnoreCase)))
            throw new IOException(
                "Recovery refused: the author file changed after this recovery candidate was listed. " +
                "Its external edits are preserved; restart the editor to re-evaluate recovery.");
    }
}
