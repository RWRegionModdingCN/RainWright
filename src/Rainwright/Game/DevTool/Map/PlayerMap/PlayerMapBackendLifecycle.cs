using System;
using BepInEx.Logging;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Single backend owner for Player Map services. Each step is recorded before Enable,
/// because a HookGen subscription can succeed partially and then throw. Disable rolls
/// back every recorded step even when startup never reached the final enabled flag.
/// </summary>
internal static class PlayerMapBackendLifecycle
{
    // The physical Rainwright assembly owns every service lease and the
    // generation tokens used by separately loaded compatibility plugins.
    private static readonly PlayerMapServiceLifetimeLedger services = new();
    private static bool enabled;
    private static bool enabling;
    private static ManualLogSource log;

    internal static bool PendingCleanup => services.Count > 0 && !enabled;
    // Secondary BepInPlugin compatibility entries must never detach a workspace
    // claimed by the authoritative backend, including partial startup/cleanup.
    internal static bool OwnsWorkspace => enabled || enabling || services.Count > 0;

    internal static bool OwnsService(string name) => services.Contains(name);

    internal static int ServiceGeneration(string name) => services.Generation(name);

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        if (enabling) throw new InvalidOperationException("Player Map backend Enable is already running.");

        log = logger;
        if (services.Count > 0)
        {
            Disable();
            if (services.Count > 0)
                throw new InvalidOperationException(
                    "Player Map backend has unreleased service hooks; refusing duplicate registration.");
        }

        enabling = true;
        try
        {
            Start("Workspace", PlayerMapWorkspaceRuntime.Enable, PlayerMapWorkspaceRuntime.Disable);
            Start("MigrationStream", () => PlayerMapMigrationStreamRuntime.Enable(logger), PlayerMapMigrationStreamRuntime.Disable);
            Start("Placement", () => PlayerMapPlacementBootstrap.Enable(logger), PlayerMapPlacementBootstrap.Disable);
            Start("IncrementalRender", () => PlayerMapIncrementalRenderHooks.Enable(logger), PlayerMapIncrementalRenderHooks.Disable);
            Start("TerrainBake", () => PlayerMapTerrainBakeBridge.Enable(logger), PlayerMapTerrainBakeBridge.Disable);
            Start("DerivedLayout", () => PlayerMapDerivedLayoutBridge.Enable(logger), PlayerMapDerivedLayoutBridge.Disable);
            Start("ConfigBuild", () => PlayerMapConfigBuildPipeline.Enable(logger), PlayerMapConfigBuildPipeline.Disable);
            Start("DisabledConfigFilter", () => PlayerMapDisabledConfigFilter.Enable(logger), PlayerMapDisabledConfigFilter.Disable);
            Start("GroupCommands", () => PlayerMapGroupCommandRuntime.Enable(logger), PlayerMapGroupCommandRuntime.Disable);
            Start("MigrationCommands", () => PlayerMapMigrationCommandBridge.Enable(logger), PlayerMapMigrationCommandBridge.Disable);
            Start("LayerMutation", () => PlayerMapLayerMutationFilter.Enable(logger), PlayerMapLayerMutationFilter.Disable);
            Start("RenderRevision", () => PlayerMapRenderRevisionGuard.Enable(logger), PlayerMapRenderRevisionGuard.Disable);
            // Preparation is last so its scheduler can see all prerequisites.
            Start("RenderPreparation", () => PlayerMapRenderPreparationController.Enable(logger),
                PlayerMapRenderPreparationController.Disable);

            enabled = true;
            logger?.LogInfo("Player Map backend lifecycle enabled explicitly.");
        }
        catch (Exception error)
        {
            ReportFailure("Enable failed; rolling back all previously installed services", error);
            Disable();
            throw; // Preserve the original startup exception and stack.
        }
        finally
        {
            enabling = false;
        }
    }

    private static void Start(string name, Action enable, Action disable) =>
        services.Start(name, enable, disable);

    internal static void Disable()
    {
        enabled = false;
        bool resetPerformed = false;
        services.ReleaseAll(
            (name, stop) =>
            {
                if (name == "Workspace" && !resetPerformed)
                {
                    // Cache teardown precedes Workspace hook removal, even when
                    // a prior service failed to detach in this cleanup pass.
                    ResetTransientState();
                    resetPerformed = true;
                }
                stop();
            },
            (name, error) => ReportFailure(
                "Cleanup/" + name + " failed; retry remains pending", error));

        if (!resetPerformed)
            ResetTransientState();

        if (services.Count > 0)
            ReportFailure("Player Map cleanup incomplete; " + services.Count +
                " service(s) still tracked; next Disable will retry", null);
    }

    /// <summary>
    /// Use this at the containing editor lifecycle boundary. Disable itself
    /// remains best-effort for individual compatibility plugins, but the parent
    /// must never release its owner lease while child hooks are still tracked.
    /// </summary>
    internal static void DisableAndVerify()
    {
        Disable();
        if (PendingCleanup)
            throw new InvalidOperationException(
                "Player Map backend cleanup is incomplete; " + services.Count +
                " service hook(s) remain tracked for retry.");
    }

    internal static void ResetTransientState()
    {
        // One failing reset must not skip the rest; the editor must not carry
        // stale work into the next room/session.
        SafeReset("Preparation", PlayerMapRenderPreparationController.Reset);
        SafeReset("Scheduler", PlayerMapRenderScheduler.Reset);
        SafeReset("MigrationQueue", PlayerMapMigrationCommandQueue.Clear);
        SafeReset("MigrationStream", PlayerMapMigrationStreamRuntime.Reset);
        SafeReset("GroupQueue", PlayerMapGroupCommandQueue.Clear);
        SafeReset("TerrainRevision", PlayerMapTerrainSemanticRevision.Reset);
        SafeReset("Preflight", PlayerMapPreflightDiagnostics.Reset);
        SafeReset("ActivityGate", PlayerMapActivityGate.Reset);
        SafeReset("RoomBakeCache", RoomMapBakeCache.Clear);
    }

    private static void SafeReset(string name, Action reset)
    {
        try { reset(); }
        catch (Exception error) { ReportFailure("Reset/" + name, error); }
    }

    private static void ReportFailure(string name, Exception error)
    {
        try
        {
            log?.LogError("Player Map backend " + name +
                (error == null ? string.Empty : ": " + error));
        }
        catch (Exception) { /* A broken logger cannot interrupt rollback. */ }
    }
}
