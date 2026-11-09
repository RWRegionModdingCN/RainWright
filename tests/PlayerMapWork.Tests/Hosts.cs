using System;
using System.Collections.Generic;
using System.Threading;
using DryCycle.DevUI.DevTool.Map.PlayerMap;
namespace UnityEngine { internal static class Time { internal static int frameCount; } internal struct Vector2 { internal float x, y; internal Vector2(float x, float y) { this.x=x; this.y=y; } } }
namespace BepInEx { }
namespace BepInEx.Logging { internal class ManualLogSource { internal void LogInfo(string value) { } internal void LogError(string value) => WorldLoader.Errors.Add(new Exception(value)); } }
internal static class WorldLoader
{
    internal static readonly Dictionary<string, string> Paths = new();
    internal static readonly List<Exception> Errors = new();
    internal static int MainThread = Environment.CurrentManagedThreadId;
    internal static string FindRoomFile(string name, bool includeRootDirectory, string extension, bool showWarning)
    {
        if (Environment.CurrentManagedThreadId != MainThread) throw new Exception("Game path resolution ran on a worker.");
        return Paths.TryGetValue(name, out string path) ? path : string.Empty;
    }
}
internal static class RoomPreprocessor
{
    internal static readonly ManualResetEventSlim Release = new(true);
    internal static CountdownEvent Started;
    internal static void VersionFix(ref string[] lines)
    {
        if (Environment.CurrentManagedThreadId == WorldLoader.MainThread) throw new Exception("Room decoding blocked the game thread.");
        var started = Started;
        if (started != null && started.CurrentCount > 0) started.Signal();
        if (!Release.Wait(5000)) throw new TimeoutException("Fixture worker was not released.");
    }
}
namespace DryCycle.DevUI.DevTool.Core
{
    internal static class EditorDiagnostics
    {
        internal static void Error(string text, Exception error) => WorldLoader.Errors.Add(error);
        internal static void Warning(string text, Exception error) => WorldLoader.Errors.Add(error);
    }
}
namespace DryCycle.DevUI.DevTool.World
{
    internal static class WorldTopologyRegistry { internal static int Revision => 1; }
    internal static class WorldTextRegistry { internal static int Revision => 1; }
}
namespace DryCycle.DevUI.DevTool.Map
{
    internal enum EditorMapGeometryKind { Air, Solid, CurvedSlope, Structure, LocalTerrain, QuicksandBody, QuicksandMaterial }
    internal struct EditorMapRectSnapshot { internal float X, Y, Width, Height; internal EditorMapGeometryKind Kind; }
    internal sealed class WorldMapRoomSource { internal RoomMapBake Bake; }
    internal static class MapRoomGeometryPresentationHub
    {
        internal static int Revision = 1;
        internal static EditorMapRectSnapshot[] Runs = Array.Empty<EditorMapRectSnapshot>();
        internal static WorldMapRoomSource Shared;
        internal static bool TryGetDecodedRoomSource(int index, out WorldMapRoomSource source) { source = index==6 ? Shared : null; return source!=null; }
        internal static bool TryGetPlayerMapTerrainFillRuns(int index, out EditorMapRectSnapshot[] runs, out int revision) { runs=Runs; revision=Revision; return true; }
    }
    internal sealed class EditorMapPresentationSnapshot { internal bool Available; internal string RegionName; internal EditorMapConnectionSnapshot[] Connections=Array.Empty<EditorMapConnectionSnapshot>(); }
    internal sealed class EditorMapConnectionSnapshot { internal int FromRoomIndex, ToRoomIndex, FromNodeIndex, ToNodeIndex; internal bool Ambiguous; }
}
namespace DryCycle.DevUI.DevTool.Map.PlayerMap
{
    internal sealed class PlayerMapPresentationSnapshot { internal bool Available; internal string RegionName; internal long Revision; internal PlayerMapRoomSnapshot[] Rooms=Array.Empty<PlayerMapRoomSnapshot>(); }
    internal sealed class PlayerMapRoomSnapshot { internal int RoomIndex, Layer; internal string Name; internal bool Disabled; internal UnityEngine.Vector2 EffectivePosition; internal RoomMapBakeSnapshot Bake; }
}
