using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DryCycle.DevUI.DevTool.Persistence;

namespace DryCycle.DevUI.DevTool.World;

internal sealed class WorldVariantFileWrite
{
    internal string Path, Text, BackupPath;
    internal byte[] Expected;
}

internal static class WorldVariantFileTransaction
{
    internal static byte[] Bytes(string text) => new UTF8Encoding(false, true).GetBytes(text);
    internal static byte[] ReadExisting(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
    private static void Check(WorldVariantFileWrite write)
    {
        byte[] current = ReadExisting(write.Path);
        if (current == null ? write.Expected != null : write.Expected == null || !current.SequenceEqual(write.Expected))
            throw new IOException("Author file changed outside RainWright: " + write.Path);
    }
    internal static void Verify(string path, byte[] expected) => Check(new WorldVariantFileWrite { Path = path, Expected = expected });

    internal static void Commit(IReadOnlyList<WorldVariantFileWrite> writes, Action<string> authorize)
    {
        var stages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var committed = new List<WorldVariantFileWrite>();
        try
        {
            foreach (var write in writes)
            {
                authorize(write.Path); Check(write);
                if (write.BackupPath != null)
                {
                    authorize(write.BackupPath);
                    if (File.Exists(write.BackupPath)) throw new IOException("Migration backup already exists: " + write.BackupPath);
                }
                if (write.Text == null) continue;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(write.Path));
                string staged = write.Path + ".rainwright-" + Guid.NewGuid().ToString("N") + ".tmp";
                stages.Add(write.Path, staged);
                using var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] bytes = Bytes(write.Text); output.Write(bytes, 0, bytes.Length); output.Flush(true);
            }
            foreach (var write in writes)
            {
                authorize(write.Path); Check(write);
                if (write.Text == null)
                {
                    if (write.Expected == null) continue;
                    string backup = write.BackupPath ?? write.Path + ".bak";
                    if (write.BackupPath != null) authorize(backup);
                    EditorBackupFiles.Copy(write.Path, backup, write.BackupPath == null);
                    File.Delete(write.Path);
                }
                else if (write.Expected == null) File.Move(stages[write.Path], write.Path);
                else EditorBackupFiles.Replace(stages[write.Path], write.Path, write.Path + ".bak");
                committed.Add(write);
            }
        }
        catch (Exception original)
        {
            var failures = new List<Exception> { original };
            for (int i = committed.Count - 1; i >= 0; i--)
            {
                var write = committed[i];
                try
                {
                    byte[] current = ReadExisting(write.Path), expectedNow = write.Text == null ? null : Bytes(write.Text);
                    if (current == null ? expectedNow != null : expectedNow == null || !current.SequenceEqual(expectedNow))
                        throw new IOException("Rollback refused an external edit: " + write.Path);
                    if (write.Expected == null) { if (current != null) File.Delete(write.Path); }
                    else
                    {
                        string restore = write.Path + ".rainwright-restore-" + Guid.NewGuid().ToString("N") + ".tmp";
                        stages[restore] = restore;
                        using (var output = new FileStream(restore, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { output.Write(write.Expected, 0, write.Expected.Length); output.Flush(true); }
                        if (current == null) File.Move(restore, write.Path);
                        else File.Replace(restore, write.Path, null);
                    }
                }
                catch (Exception recovery) { failures.Add(recovery); }
            }
            Cleanup(stages.Values, failures);
            throw new AggregateException("World variant save failed; rollback results are attached.", failures);
        }
        var cleanupFailures = new List<Exception>();
        Cleanup(stages.Values, cleanupFailures);
        if (cleanupFailures.Count > 0) throw new AggregateException("World variants saved, but temporary file cleanup failed.", cleanupFailures);
    }

    private static void Cleanup(IEnumerable<string> paths, List<Exception> failures)
    {
        foreach (string path in paths)
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception error) { failures.Add(error); }
    }
}
