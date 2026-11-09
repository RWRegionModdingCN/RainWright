using System;
using System.Collections.Generic;
namespace DryCycle
{
    internal static class Plugin
    {
        internal const string RainWorldModId = "NR.B5";
        internal static LoggerStub Logger = new();
    }
    internal sealed class LoggerStub
    {
        internal void LogWarning(string message) { }
    }
}
namespace DryCycle.DevUI.DevTool.Core
{
    internal static class EditorDiagnostics
    {
        internal static DryCycle.LoggerStub LogSource = new();
        internal static void Info(string message) { }
        internal static void Warning(string message, Exception error = null) { }
        internal static readonly List<string> Errors = new();
        internal static void Error(string message, Exception error = null) =>
            Errors.Add(message + (error == null ? "" : " " + error));
    }
}
namespace RWCustom
{
    internal static class Custom
    {
        internal static string RootFolderDirectory() => System.IO.Path.GetTempPath();
    }
}
internal static class ModManager
{
    internal sealed class Mod
    {
        internal string id;
        internal string TargetedPath, NewestPath, path, basePath;
        internal bool hasTargetedVersionFolder, hasNewestFolder;
    }
    internal static List<Mod> ActiveMods = new();
}
internal static class Json
{
    internal static object Deserialize(string raw)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var dict = new Dictionary<string, object>();
        foreach (var pair in doc.RootElement.EnumerateObject())
            dict[pair.Name] = pair.Value.ValueKind == System.Text.Json.JsonValueKind.String
                ? pair.Value.GetString() : null;
        return dict;
    }
}

internal static class AssetManager { internal static string ResolveFilePath(string path) => path; }
namespace DryCycle.DevUI.DevTool.World
{
    internal static class WorldFileVariantStore { internal static string TopologyReadPath() => null; }
}
internal sealed class RoomSettings
{
    internal string filePath;
    internal void Save(string path, bool template) => System.IO.File.WriteAllText(path, "");
}
