using System;
using System.IO;

namespace DryCycle.DevUI.DevTool.Background
{
    internal static class BackgroundEditorRuntime
    {
        internal static bool Dirty;
        internal static bool HasPendingChanges(Core.EditorSession session) => Dirty;
        internal static bool CaptureRecovery(Core.EditorSession session, string document) => false;
    }
}

namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath { get; set; } = Path.GetTempPath();
    }
}

namespace DryCycle
{
    public static class Plugin
    {
        internal static readonly LogCollector Logger = new();
    }

    internal sealed class LogCollector
    {
        internal void LogWarning(string message) { }
        internal void LogInfo(string message) { }
        internal void LogError(string message) { }
    }
}

internal static class ModManager
{
    internal sealed class Mod
    {
        internal string id;
        internal string path, TargetedPath, NewestPath;
        internal bool hasTargetedVersionFolder, hasNewestFolder;
    }
    internal static System.Collections.Generic.List<Mod> ActiveMods = new();
}

internal static class AssetManager
{
    internal static string ResolveFilePath(string path) => path;
}

namespace RWCustom
{
    internal static class Custom
    {
        internal static string RootFolderDirectory() => BepInEx.Paths.ConfigPath;
    }
}

internal sealed class RoomSettings
{
    internal string filePath;
    internal string content;
    internal void Save(string path, bool saveAsTemplate)
        => File.WriteAllText(path, content);
}

namespace BepInEx.Logging
{
    // Test-only transport: compile the real EditorDiagnostics against a minimal logger.
    // Production continues to use BepInEx's registered ManualLogSource.
    public sealed class ManualLogSource
    {
        public readonly string SourceName;
        public string LastWarning;
        public string LastError;
        public string LastInfo;
        public readonly System.Collections.Generic.List<string> Entries = new();

        public ManualLogSource(string sourceName) { SourceName = sourceName; }
        public void LogWarning(object message) { LastWarning = message?.ToString(); Entries.Add("WARN " + LastWarning); }
        public void LogError(object message) { LastError = message?.ToString(); Entries.Add("ERROR " + LastError); }
        public void LogInfo(object message) { LastInfo = message?.ToString(); Entries.Add("INFO " + LastInfo); }
    }

    public static class Logger
    {
        public static ManualLogSource LastCreated;
        public static ManualLogSource CreateLogSource(string sourceName) =>
            LastCreated = new ManualLogSource(sourceName);
    }
}
