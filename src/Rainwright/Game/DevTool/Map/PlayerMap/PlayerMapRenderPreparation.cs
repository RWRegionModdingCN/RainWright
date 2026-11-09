using System;
using BepInEx.Logging;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.World;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Render request gate at the scheduler boundary. A render request no longer fails merely because
/// some room/authored-terrain bakes are still progressing through their bounded cache pipeline.
/// Every caller of PlayerMapRenderScheduler.Begin (UI, shortcut, future API) therefore receives the
/// same preparation semantics without depending on command-hook ordering.
///
/// Authoring state is fingerprinted at request time. Bake readiness is allowed to advance; room
/// placement/layer/Def_Mat/topology edits cancel the request instead of silently changing the output
/// the author asked to render.
/// </summary>
internal static class PlayerMapRenderPreparationController
{
    private sealed class Request
    {
        internal EditorSession Session;
        internal MapPage Page;
        internal string Region = string.Empty;
        internal bool Export;
        internal ulong AuthoringFingerprint;
        internal int TopologyRevision;
        internal int WorldTextRevision;
    }

    private static ManualLogSource log;
    private static bool enabled;
    private static Request active;

    internal static bool IsRunning => active != null;
    internal static PlayerMapRenderPreparationSnapshot Progress => PlayerMapRenderPreparationState.Current;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map render preparation enabled through direct scheduler/runtime calls; no self-detours attached.");
    }

    internal static void Disable()
    {
        Reset();
        enabled = false;
        log = null;
    }

    internal static void Reset()
    {
        active = null;
        PlayerMapRenderPreparationState.Clear();
    }

    internal static void RequestCancel()
    {
        if (active != null)
            Cancel("Render preparation cancelled. Existing output files were preserved.");
    }

    internal static bool TryBeginGate(
        EditorSession session,
        MapPage page,
        PlayerMapPresentationSnapshot snapshot,
        bool export,
        out bool handled)
    {
        handled = false;
        if (!enabled)
            return false;

        if (active != null || PlayerMapRenderScheduler.IsRunning)
        {
            handled = true;
            return false;
        }

        if (session == null || page?.world == null || snapshot?.Available != true)
            return false;

        bool waiting = NeedsPreparation(snapshot, out int ready, out int pending, out bool hardFailure);
        if (hardFailure || !waiting)
            return false;

        active = new Request
        {
            Session = session,
            Page = page,
            Region = snapshot.RegionName ?? string.Empty,
            Export = export,
            AuthoringFingerprint = AuthoringFingerprint(snapshot),
            TopologyRevision = WorldTopologyRegistry.Revision,
            WorldTextRevision = WorldTextRegistry.Revision
        };
        Publish(ready, ready + pending,
            pending + " room/terrain bake(s) are still preparing.");
        handled = true;
        return true;
    }

    internal static void AfterSynchronize(EditorSession session)
    {
        Request request = active;
        if (!enabled || request == null || !ReferenceEquals(request.Session, session))
            return;

        if (session?.Owner?.activePage is not MapPage page || !ReferenceEquals(page, request.Page) || page.world == null ||
            !string.Equals(page.world.name ?? string.Empty, request.Region, StringComparison.OrdinalIgnoreCase))
        {
            Cancel("Render preparation cancelled because the active map/region changed.");
            return;
        }
        if (WorldTopologyRegistry.Revision != request.TopologyRevision || WorldTextRegistry.Revision != request.WorldTextRevision)
        {
            Cancel("Render preparation cancelled because world connection topology changed.");
            return;
        }

        PlayerMapPresentationSnapshot snapshot = PlayerMapWorkspaceRuntime.GetPresentation(session);
        if (AuthoringFingerprint(snapshot) != request.AuthoringFingerprint)
        {
            Cancel("Render preparation cancelled because Player Map authoring data changed.");
            return;
        }

        bool waiting = NeedsPreparation(snapshot, out int ready, out int pending, out bool hardFailure);
        if (hardFailure)
        {
            Cancel("Render preparation stopped because a room bake is missing or failed.");
            return;
        }
        if (waiting)
        {
            Publish(ready, ready + pending,
                pending + " room/terrain bake(s) remaining.");
            return;
        }

        bool export = request.Export;
        active = null;
        PlayerMapRenderPreparationState.Clear();
        // Re-enter the single scheduler boundary. The direct preparation gate sees that all bakes
        // are ready and immediately delegates to the authoritative incremental renderer.
        if (!PlayerMapRenderScheduler.Begin(session, page, snapshot, export))
            log?.LogWarning("Player Map render preparation completed, but the render scheduler did not accept the job.");
    }

    private static bool NeedsPreparation(
        PlayerMapPresentationSnapshot snapshot,
        out int ready,
        out int pending,
        out bool hardFailure)
    {
        PlayerMapBakeReadiness readiness = default;
        if (snapshot?.Available == true)
        {
            PlayerMapRoomSnapshot[] rooms = snapshot.Rooms ?? Array.Empty<PlayerMapRoomSnapshot>();
            for (int i = 0; i < rooms.Length; i++)
            {
                PlayerMapRoomSnapshot room = rooms[i];
                if (room == null || room.Disabled) continue;
                readiness.Observe(room.Bake?.Status ?? RoomMapBakeStatus.Missing);
            }
        }
        ready = readiness.ReadyRooms;
        pending = readiness.PendingRooms;
        hardFailure = readiness.HardFailure;
        return readiness.NeedsPreparation;
    }

    private static ulong AuthoringFingerprint(PlayerMapPresentationSnapshot snapshot)
    {
        // Authoring identity is Rainwright-owned and allocation-free. The host
        // only reads projected Unity coordinates; no game object is retained.
        PlayerMapAuthoringFingerprint fingerprint = PlayerMapAuthoringFingerprint.Begin();
        PlayerMapRoomSnapshot[] rooms = snapshot?.Rooms ?? Array.Empty<PlayerMapRoomSnapshot>();
        for (int i = 0; i < rooms.Length; i++)
        {
            PlayerMapRoomSnapshot room = rooms[i];
            if (room == null) continue;
            fingerprint.ObserveRoom(room.RoomIndex, room.EffectivePosition.x,
                room.EffectivePosition.y, room.Layer, room.Disabled, room.Mode);
        }

        PlayerMapDefMaterialSnapshot[] defs = snapshot?.DefaultMaterials ?? Array.Empty<PlayerMapDefMaterialSnapshot>();
        for (int i = 0; i < defs.Length; i++)
        {
            PlayerMapDefMaterialSnapshot item = defs[i];
            fingerprint.ObserveMaterial(item.Id, item.A.x, item.A.y, item.B.x, item.B.y, item.Air);
        }
        return fingerprint.Value;
    }

    private static void Publish(int ready, int total, string detail)
    {
        PlayerMapRenderPreparationState.Publish(new PlayerMapRenderPreparationSnapshot
        {
            Running = true,
            ExportRequested = active?.Export == true,
            CanCancel = true,
            ReadyRooms = ready,
            TotalRooms = Math.Max(1, total),
            Progress = total <= 0 ? 0f : Mathf.Clamp01((float)ready / total),
            Detail = detail ?? string.Empty
        });
    }

    private static void Cancel(string detail)
    {
        bool export = active?.Export == true;
        active = null;
        PlayerMapRenderPreparationState.Publish(new PlayerMapRenderPreparationSnapshot
        {
            Running = false,
            ExportRequested = export,
            CanCancel = false,
            Progress = 0f,
            Detail = detail ?? string.Empty
        });
    }

}
