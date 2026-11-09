using System;
using System.Collections.Generic;
using System.IO;
internal static class ModManager
{
    internal static int FolderPresenceReads;
    internal sealed class Mod
    {
        internal string id, path, TargetedPath, NewestPath;
        private bool targeted, newest;
        internal bool hasTargetedVersionFolder { get { FolderPresenceReads++; return targeted; } set { targeted = value; } }
        internal bool hasNewestFolder { get { FolderPresenceReads++; return newest; } set { newest = value; } }
    }
    internal static List<Mod> ActiveMods = new();
}
internal static class AssetManager
{
    internal static string SourceRoot;
    internal static string ResolveFilePath(string relative) => Path.Combine(SourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
}
internal sealed class RoomSettings
{
    internal string filePath, Text;
    internal void Save(string path, bool template) => File.WriteAllText(path, Text);
}
namespace RWCustom { internal static class Custom { internal static string RootFolderDirectory() => AssetManager.SourceRoot; } }
namespace DryCycle.DevUI.DevTool.Core
{
    internal static class EditorDiagnostics
    {
        internal static void Info(string text) { }
        internal static void Warning(string text, Exception error = null) { }
        internal static void Error(string text, Exception error = null) { }
    }
}
