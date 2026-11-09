using System;
using System.Collections.Generic;

namespace BepInEx.Logging
{
    internal sealed class ManualLogSource
    {
        internal readonly List<string> Info = new();
        internal readonly List<string> Warnings = new();
        internal void LogInfo(string message) => Info.Add(message);
        internal void LogWarning(string message) => Warnings.Add(message);
    }
}

namespace DryCycle.DevUI.DevTool.Core
{
    internal static class EditorDiagnostics
    {
        internal static readonly List<string> Errors = new();
        internal static void Error(string message) => Errors.Add(message);
    }

    internal static class DevToolRuntime
    {
        internal static Session ActiveSession;
    }

    internal sealed class Session
    {
        internal global::World World;
    }
}

namespace DryCycle.DevUI.DevTool.World
{
    internal static class WorldLineageRegistry
    {
        internal static bool Dirty;
        internal static bool SaveSucceeds = true;
        internal static int Saves;
        internal static bool Save(out string error)
        {
            Saves++;
            error = SaveSucceeds ? null : "injected writer failure";
            if (!SaveSucceeds) return false;
            Dirty = false;
            return true;
        }
    }

    internal static class WorldTextRegistry
    {
        internal static bool ReloadSucceeds = true;
        internal static int Reloads;
        internal static string LastRegion;
        internal static bool Reload(string region)
        {
            Reloads++;
            LastRegion = region;
            return ReloadSucceeds;
        }

        internal static Dictionary<string, WorldCreatureSpawnRecord[]> SpawnByRoom = new();
        internal static WorldCreatureSpawnRecord[] GetCreatureSpawns(string region, string roomName) =>
            SpawnByRoom.TryGetValue(roomName, out var value) ? value : Array.Empty<WorldCreatureSpawnRecord>();
    }

    internal sealed class WorldCreatureSpawnRecord
    {
        internal int Id;
    }

    internal static class WorldCreatureLiveReload
    {
        internal static readonly List<string> Previewed = new();
        internal static int ResetCount;
        internal static void ReloadRoom(string region, string roomName) =>
            Previewed.Add(region + "/" + roomName);
        internal static void Reset() => ResetCount++;
    }
}

internal sealed class AbstractRoom
{
    internal string name;
}

internal sealed class World
{
    internal AbstractRoom[] abstractRooms;
}
