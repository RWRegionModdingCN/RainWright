using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.Map;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Keeps the vanilla MapPage alive as a data source while suppressing its Futile presentation after
/// refreshes. BridgePlugin owns enable/disable and LateUpdate scheduling.
/// </summary>
internal static class WorldMapLegacyVisualGuard
{
    private const int SafetyAuditIntervalFrames = 120;

    private static readonly HashSet<FSprite> SuppressedSprites = new();
    private static readonly HashSet<FLabel> SuppressedLabels = new();

    private static ManualLogSource log;
    // Both Refresh subscriptions must be tracked even if the second add
    // partially succeeds and throws before the aggregate enabled flag is set.
    private static readonly FrontendHookLedger refreshHooks = new((name, error) =>
        log?.LogWarning("World Map legacy visual Hook cleanup failed for " + name + ": " + error));
    private static MapPage suppressedPage;
    private static bool enabled;
    private static bool suppressionDirty;
    private static int nextSafetyAuditFrame;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        if (refreshHooks.HasActiveRegistrations)
            throw new InvalidOperationException(
                "World Map legacy visual Hook cleanup is pending; duplicate install refused.");

        log = logger;
        try
        {
            refreshHooks.Install("RoomPanel.Refresh",
                () => On.DevInterface.RoomPanel.Refresh += RoomPanel_Refresh,
                () => On.DevInterface.RoomPanel.Refresh -= RoomPanel_Refresh);
            refreshHooks.Install("MiniMap.Refresh",
                () => On.DevInterface.MiniMap.Refresh += MiniMap_Refresh,
                () => On.DevInterface.MiniMap.Refresh -= MiniMap_Refresh);
            refreshHooks.Install("Legacy presentation restore",
                () => WorldMapFrontendBridge.RegisterLegacyPresentationRestore(RestoreBeforeLegacyUpdate),
                () => WorldMapFrontendBridge.UnregisterLegacyPresentationRestore(RestoreBeforeLegacyUpdate));
            enabled = true;
            suppressionDirty = true;
            nextSafetyAuditFrame = 0;
            log?.LogInfo("World Map legacy visual guard enabled (dirty-driven).");
        }
        catch
        {
            enabled = false;
            // Ledger retains any failed removal, so the parent Bridge does not
            // release this module's ownership and cannot install another copy.
            refreshHooks.ReleaseAll();
            throw;
        }
    }

    internal static void Disable()
    {
        enabled = false;
        bool hooksReleased = refreshHooks.ReleaseAll();
        // Vanilla sprites must be restored even if a Refresh unsubscribe
        // fails; live callbacks are inert while enabled is false.
        Restore();
        suppressionDirty = false;
        nextSafetyAuditFrame = 0;
        if (!hooksReleased)
            throw new InvalidOperationException(
                "World Map legacy visual hooks could not all detach; retry required.");
        log = null;
    }

    private static void RoomPanel_Refresh(On.DevInterface.RoomPanel.orig_Refresh orig, RoomPanel self)
    {
        orig(self);
        MarkDirty(self?.mapPage);
    }

    private static void MiniMap_Refresh(On.DevInterface.MiniMap.orig_Refresh orig, MiniMap self)
    {
        orig(self);
        MarkDirty(self?.mapPage);
    }

    private static void MarkDirty(MapPage page)
    {
        if (!enabled || page == null) return;

        EditorSession session = DevToolRuntime.ActiveSession;
        if (session?.ToolMode != EditorToolMode.Map ||
            !ReferenceEquals(session.Owner?.activePage, page) ||
            EditorUiModeState.UseVanilla || session.LegacyUiVisible)
            return;

        suppressionDirty = true;
    }

    internal static void LateUpdate()
    {
        if (!enabled) return;

        // Once DevTools is gone there is no page that can legitimately be refreshed later in this
        // frame. Restore a still-owned page exactly once at the lifetime edge, then make gameplay
        // frames an O(1) no-op instead of re-evaluating every map/frontend ownership condition.
        if (!DevToolSessionHub.IsCurrentSessionLive)
        {
            if (suppressedPage != null || SuppressedSprites.Count != 0 || SuppressedLabels.Count != 0)
                Restore();
            suppressionDirty = true;
            nextSafetyAuditFrame = 0;
            return;
        }

        EditorSession session = DevToolRuntime.ActiveSession;
        bool rebuiltMapOwnsLegacySuppression =
            !EditorUiModeState.UseVanilla &&
            EditorInputRouter.FrontendAttached &&
            session != null &&
            !session.LegacyUiVisible &&
            session.ToolMode == EditorToolMode.Map &&
            session.Owner?.activePage is MapPage;

        if (!rebuiltMapOwnsLegacySuppression)
        {
            Restore();
            suppressionDirty = true;
            nextSafetyAuditFrame = 0;
            return;
        }

        // Escape-hidden overlay is still rebuilt-mode ownership: the V2 off-screen surface is not
        // presented while the vanilla MapPage must remain visually suppressed. If we
        // restored here, Restore() would run MapPage.Refresh in LateUpdate after the core suppression
        // pass and make the entire vanilla map leak into the supposedly hidden editor frame.
        MapPage page = session.Owner.activePage as MapPage;
        if (!ReferenceEquals(page, suppressedPage))
        {
            Restore();
            suppressedPage = page;
            suppressionDirty = true;
            nextSafetyAuditFrame = 0;
        }

        bool safetyAuditDue = Time.frameCount >= nextSafetyAuditFrame;
        if (!suppressionDirty && !safetyAuditDue)
            return;

        SuppressNode(page);
        SuppressCreatureVisuals(page);
        suppressionDirty = false;
        nextSafetyAuditFrame = Time.frameCount + SafetyAuditIntervalFrames;
    }

    private static void RestoreBeforeLegacyUpdate(EditorSession session)
    {
        if (session?.Owner?.activePage is MapPage &&
            (EditorUiModeState.UseVanilla || session.LegacyUiVisible))
            Restore();
    }

    private static void SuppressNode(DevUINode node)
    {
        if (node == null) return;

        List<FSprite> sprites = node.fSprites;
        if (sprites != null)
        {
            for (int i = 0; i < sprites.Count; i++) Suppress(sprites[i]);
        }

        List<FLabel> labels = node.fLabels;
        if (labels != null)
        {
            for (int i = 0; i < labels.Count; i++) Suppress(labels[i]);
        }

        List<DevUINode> children = node.subNodes;
        if (children == null) return;
        for (int i = 0; i < children.Count; i++) SuppressNode(children[i]);
    }

    private static void SuppressCreatureVisuals(MapPage page)
    {
        List<MapPage.CreatureVis> creatures = page?.creatureVisualizations;
        if (creatures == null) return;

        for (int i = 0; i < creatures.Count; i++)
        {
            MapPage.CreatureVis creature = creatures[i];
            if (creature == null) continue;
            Suppress(creature.sprite);
            Suppress(creature.sprite2);
            Suppress(creature.label);
            Suppress(creature.label2);
        }
    }

    private static void Suppress(FSprite sprite)
    {
        if (sprite == null) return;

        // Only remember objects that vanilla actually wanted visible. Objects which are logically
        // hidden by layer/mode stay hidden when we later restore the page.
        if (sprite.isVisible) SuppressedSprites.Add(sprite);
        if (SuppressedSprites.Contains(sprite)) sprite.isVisible = false;
    }

    private static void Suppress(FLabel label)
    {
        if (label == null) return;
        if (label.isVisible) SuppressedLabels.Add(label);
        if (SuppressedLabels.Contains(label)) label.isVisible = false;
    }

    private static void Restore()
    {
        if (suppressedPage == null && SuppressedSprites.Count == 0 && SuppressedLabels.Count == 0)
            return;

        foreach (FSprite sprite in SuppressedSprites)
        {
            try
            {
                if (sprite != null) sprite.isVisible = true;
            }
            catch
            {
                // A page can be destroyed while DevTools closes. Dead Futile nodes need no restore.
            }
        }

        foreach (FLabel label in SuppressedLabels)
        {
            try
            {
                if (label != null) label.isVisible = true;
            }
            catch
            {
            }
        }

        suppressedPage = null;
        SuppressedSprites.Clear();
        SuppressedLabels.Clear();

        // Restoring presentation must not refresh/rebuild a native page after its button handler
        // ran. Vanilla Update and the backend's deferred-refresh boundary own that lifecycle.
    }
}
