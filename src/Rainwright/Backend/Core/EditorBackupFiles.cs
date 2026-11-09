using System.IO;

namespace DryCycle.DevUI.DevTool.Persistence;

/// <summary>Backups are hidden; author files remain unchanged and hidden backups can be overwritten.</summary>
internal static class EditorBackupFiles
{
    internal static void Copy(string source, string backup, bool overwrite)
    {
        // CopyFile cannot overwrite an existing hidden destination on Windows.
        Unhide(backup);
        try { File.Copy(source, backup, overwrite); }
        finally { Hide(backup); }
    }

    internal static void Replace(string staged, string target, string backup)
    {
        Unhide(backup);
        try { File.Replace(staged, target, backup); }
        finally { Hide(backup); }
    }

    internal static void Hide(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.Hidden) == 0)
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
    }

    internal static void Unhide(string path)
    {
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.Hidden) != 0)
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.Hidden);
    }
}
