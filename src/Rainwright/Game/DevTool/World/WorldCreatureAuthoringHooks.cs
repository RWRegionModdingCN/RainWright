using System;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Core;
using RWWorld = global::World;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Direct extension service for lineage dirty/save semantics and room-local creature preview.
/// WorldTextRegistry calls this service explicitly at its authoritative mutation/save boundaries;
/// no DryCycle-owned registry method is RuntimeDetoured.
/// </summary>
internal static class WorldCreatureAuthoringHooks
{
    private static ManualLogSource log;
    private static bool enabled;
    // A successful LINEAGE write may still fail to reparse world.txt. Keep
    // the synchronization requirement independently of the lineage writer's
    // Dirty flag, otherwise the next Save would incorrectly report success.
    private static string pendingReloadRegion = string.Empty;

    // Dirty authored lineage content must remain visible even if a startup
    // failure prevented this optional BepInPlugin-era service from running.
    // SaveAdditional will then fail explicitly rather than claim success.
    internal static bool AdditionalDirty =>
        WorldLineageRegistry.Dirty || pendingReloadRegion.Length > 0;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Creature authoring integration enabled through direct WorldTextRegistry calls; no self-detours attached.");
    }

    internal static void Disable()
    {
        WorldCreatureLiveReload.Reset();
        enabled = false;
        log = null;
    }

    internal static bool SaveAdditional(string region)
    {
        if (!WorldLineageRegistry.Dirty && pendingReloadRegion.Length == 0)
            return true;
        if (!enabled)
        {
            EditorDiagnostics.Error(
                "World lineage has unsaved edits or a pending world.txt synchronization, " +
                "but its authoring integration is not enabled. Refusing to report a successful save.");
            return false;
        }

        if (WorldLineageRegistry.Dirty)
        {
            if (!WorldLineageRegistry.Save(out string error))
            {
                log?.LogWarning("Lineage save failed: " + error);
                return false;
            }

            // Persisted LINEAGE changed; a fresh WorldDocument is required
            // before any subsequent ordinary creature save. Do not lose this
            // state if parsing fails after the author file is already written.
            if (!string.IsNullOrWhiteSpace(region))
                pendingReloadRegion = region;
        }

        if (pendingReloadRegion.Length > 0)
        {
            string reloadRegion = pendingReloadRegion;
            if (!WorldTextRegistry.Reload(reloadRegion))
            {
                log?.LogWarning("Lineage save succeeded, but world.txt could not be reloaded for region '" +
                    reloadRegion + "'. Retaining pending synchronization for a later retry.");
                return false;
            }

            pendingReloadRegion = string.Empty;
        }

        return true;
    }

    internal static void OnCreatureAdded(string region, string roomName)
    {
        if (enabled && !string.IsNullOrWhiteSpace(roomName))
            WorldCreatureLiveReload.ReloadRoom(region, roomName);
    }

    internal static string FindSpawnRoom(string region, int spawnId)
    {
        if (!enabled) return string.Empty;

        RWWorld world = DevToolRuntime.ActiveSession?.World;
        if (world?.abstractRooms == null) return string.Empty;
        for (int i = 0; i < world.abstractRooms.Length; i++)
        {
            AbstractRoom room = world.abstractRooms[i];
            if (room == null) continue;
            WorldCreatureSpawnRecord[] spawns = WorldTextRegistry.GetCreatureSpawns(region, room.name);
            for (int j = 0; j < spawns.Length; j++)
                if (spawns[j].Id == spawnId) return room.name;
        }
        return string.Empty;
    }

    internal static void OnCreatureEdited(string region, string roomName)
    {
        if (enabled && !string.IsNullOrWhiteSpace(roomName))
            WorldCreatureLiveReload.ReloadRoom(region, roomName);
    }
}
