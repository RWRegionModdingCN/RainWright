using System;
using BepInEx.Logging;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Exclusive lifetime of rebuilt Player Map frontend submodules. Record each
/// removal before invoking its initializer: a partial failure must not orphan
/// earlier modules or allow a second Bridge to double-install their hooks.
/// </summary>
internal static class PlayerMapFrontendLifecycle
{
    private static readonly FrontendHookLedger modules = new((name, error) =>
        log?.LogWarning("Player Map frontend cleanup failed for " + name + ": " + error));
    private static ManualLogSource log;
    private static bool enabled;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        if (modules.HasActiveRegistrations)
            throw new InvalidOperationException(
                "Player Map frontend cannot restart while prior module cleanup remains pending.");

        log = logger;
        try
        {
            modules.Install("PlayerMapWorkspaceIntegration",
                () => PlayerMapWorkspaceIntegration.Enable(logger),
                PlayerMapWorkspaceIntegration.Disable);
            modules.Install("PlayerMapRenderProgressView",
                () => PlayerMapRenderProgressView.Enable(logger),
                PlayerMapRenderProgressView.Disable);
            modules.Install("PlayerMapCanvasAuthoring",
                () => PlayerMapCanvasAuthoring.Enable(logger),
                PlayerMapCanvasAuthoring.Disable);
            modules.Install("PlayerMapMultiSelection",
                () => PlayerMapMultiSelection.Enable(logger),
                PlayerMapMultiSelection.Disable);
            modules.Install("PlayerMapSelectionAccess",
                () => PlayerMapSelectionAccess.Enable(logger),
                PlayerMapSelectionAccess.Disable);
            modules.Install("PlayerMapGroupLayerControls",
                () => PlayerMapGroupLayerControls.Enable(logger),
                PlayerMapGroupLayerControls.Disable);
            modules.Install("PlayerMapGroupPlacementControls",
                () => PlayerMapGroupPlacementControls.Enable(logger),
                PlayerMapGroupPlacementControls.Disable);
            modules.Install("PlayerMapGroupLayoutControls",
                () => PlayerMapGroupLayoutControls.Enable(logger),
                PlayerMapGroupLayoutControls.Disable);
            modules.Install("PlayerMapLayoutAssist",
                () => PlayerMapLayoutAssist.Enable(logger),
                PlayerMapLayoutAssist.Disable);
            modules.Install("PlayerMapPreflightPanel",
                () => PlayerMapPreflightPanel.Enable(logger),
                PlayerMapPreflightPanel.Disable);
            modules.Install("PlayerMapConditionalVisibilityInfo",
                () => PlayerMapConditionalVisibilityInfo.Enable(logger),
                PlayerMapConditionalVisibilityInfo.Disable);
            modules.Install("PlayerMapMigrationStreamView",
                () => PlayerMapMigrationStreamView.Enable(logger),
                PlayerMapMigrationStreamView.Disable);
            modules.Install("PlayerMapMultiPipeConnections",
                () => PlayerMapMultiPipeConnections.Enable(logger),
                PlayerMapMultiPipeConnections.Disable);
            enabled = true;
            logger?.LogInfo("Player Map frontend lifecycle enabled explicitly.");
        }
        catch
        {
            // Preserve the original startup failure. ReleaseAll retries all
            // partially initialized modules, and keeps any failed cleanups.
            if (!modules.ReleaseAll())
                logger?.LogWarning(
                    "Player Map partial initialization left pending cleanup; restart remains blocked.");
            try { PlayerMapWorkspaceView.ResetRetainedState(); }
            catch (Exception cleanupError)
            {
                logger?.LogWarning("Player Map retained state cleanup failed: " + cleanupError);
            }
            throw;
        }
    }

    internal static void Disable()
    {
        // Even if the last enable never reached its success flag, an installed
        // submodule can still own resources. Never short-circuit on enabled.
        enabled = false;
        if (!modules.ReleaseAll())
            throw new InvalidOperationException(
                "Player Map frontend cleanup is incomplete; retry before re-enabling.");
        PlayerMapWorkspaceView.ResetRetainedState();
        log = null;
    }
}
