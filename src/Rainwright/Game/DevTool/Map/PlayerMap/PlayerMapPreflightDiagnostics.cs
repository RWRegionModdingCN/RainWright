using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.World;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Pure preflight shared by the live inspector and Render Map semantics. It consumes only rebuilt
/// immutable Player Map / exact topology snapshots; it never asks vanilla MapPage, MiniMap or
/// RoomRepresentation to update. Results are cached by presentation + topology revisions so stable
/// UI frames do not repeatedly scan every room/connection.
/// </summary>
internal static class PlayerMapPreflightDiagnostics
{
    private static readonly object gate = new();
    private static Task<PlayerMapPreflightSnapshot> work;
    private static string workRegion;
    private static long workRevision;
    private static int workTopology, workWorldText, workGeneration, generation;
    private static PlayerMapPreflightSnapshot pending = PlayerMapPreflightSnapshot.Empty;
    private const int MaxDimension = 16384;
    private const long MaxPixels = 160_000_000L;

    private static string cachedRegion = string.Empty;
    private static long cachedPresentationRevision = long.MinValue;
    private static int cachedTopologyRevision = int.MinValue;
    private static int cachedWorldTextRevision = int.MinValue;
    private static PlayerMapPreflightSnapshot cached = PlayerMapPreflightSnapshot.Empty;

    internal static PlayerMapPreflightSnapshot Evaluate(
        PlayerMapPresentationSnapshot player,
        EditorMapPresentationSnapshot world)
    {
        if (player?.Available != true || world?.Available != true ||
            !string.Equals(player.RegionName ?? string.Empty, world.RegionName ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))
            return PlayerMapPreflightSnapshot.Empty;

        string region = player.RegionName ?? string.Empty;
        int topologyRevision = WorldTopologyRegistry.Revision;
        int worldTextRevision = WorldTextRegistry.Revision;
        lock (gate)
            return EvaluateCore(player, world, region, topologyRevision, worldTextRevision);
    }

    private static PlayerMapPreflightSnapshot EvaluateCore(PlayerMapPresentationSnapshot player,
        EditorMapPresentationSnapshot world, string region, int topologyRevision, int worldTextRevision)
    {
        if (cachedPresentationRevision == player.Revision &&
            cachedTopologyRevision == topologyRevision &&
            cachedWorldTextRevision == worldTextRevision &&
            string.Equals(cachedRegion, region, StringComparison.OrdinalIgnoreCase))
            return cached;

        if (work != null && work.IsCompleted)
        {
            Exception failure = work.IsFaulted ? work.Exception : null;
            if (failure != null) Core.EditorDiagnostics.Error("Player Map background preflight failed.", failure);
            bool current = workGeneration == generation && workRevision == player.Revision &&
                           workTopology == topologyRevision && workWorldText == worldTextRevision &&
                           string.Equals(workRegion, region, StringComparison.OrdinalIgnoreCase);
            PlayerMapPreflightSnapshot result = failure == null ? work.Result : new PlayerMapPreflightSnapshot
            {
                Available = true, RegionName = region, Errors = new[] { failure.ToString() }
            };
            work = null;
            if (current)
            {
                cachedRegion = region;
                cachedPresentationRevision = player.Revision;
                cachedTopologyRevision = topologyRevision;
                cachedWorldTextRevision = worldTextRevision;
                cached = result;
                return cached;
            }
        }
        // One detached solve, including obsolete work after reset. No synchronous joins or
        // per-frame task proliferation while room readiness/author revisions are advancing.
        if (work == null)
        {
            workRegion = region; workRevision = player.Revision;
            workTopology = topologyRevision; workWorldText = worldTextRevision; workGeneration = generation;
            work = Task.Run(() => Build(player, world));
        }
        if (!pending.Computing || !string.Equals(pending.RegionName, region, StringComparison.OrdinalIgnoreCase))
            pending = new PlayerMapPreflightSnapshot { Available = true, Computing = true, RegionName = region };
        return pending;
    }

    internal static void Reset()
    {
        lock (gate)
        {
            generation++;
            cachedRegion = string.Empty;
            cachedPresentationRevision = long.MinValue;
            cachedTopologyRevision = int.MinValue;
            cachedWorldTextRevision = int.MinValue;
            cached = PlayerMapPreflightSnapshot.Empty;
            pending = PlayerMapPreflightSnapshot.Empty;
        }
    }

    private static PlayerMapPreflightSnapshot Build(
        PlayerMapPresentationSnapshot player,
        EditorMapPresentationSnapshot world)
    {
        // Only project on a preflight cache miss. The pure core never retains
        // mutable game object graphs or drives vanilla map rendering.
        PlayerMapRoomSnapshot[] source = player.Rooms ?? Array.Empty<PlayerMapRoomSnapshot>();
        PlayerMapPreflightRoom[] rooms = new PlayerMapPreflightRoom[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            PlayerMapRoomSnapshot room = source[i];
            if (room == null) continue;
            rooms[i] = new PlayerMapPreflightRoom
            {
                RoomIndex = room.RoomIndex,
                Name = room.Name,
                X = room.EffectivePosition.x,
                Y = room.EffectivePosition.y,
                Layer = room.Layer,
                Disabled = room.Disabled,
                Bake = room.Bake ?? RoomMapBakeSnapshot.Empty
            };
        }
        return PlayerMapPreflightCore.Build(player.RegionName, rooms, world.Connections);
    }

}
