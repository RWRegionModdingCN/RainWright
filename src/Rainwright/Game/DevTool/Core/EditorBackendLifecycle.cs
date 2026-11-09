using System;
using System.Collections.Generic;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.Map.PlayerMap;
using DryCycle.DevUI.DevTool.Room;
using DryCycle.DevUI.DevTool.World;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.Input;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// The one editor-owned runtime/backend lifecycle. DryCycle supplies optional
/// authoring providers via its legacy adapter, but never owns these core hooks.
/// A stop action is recorded before invoking the start action so a partial HookGen
/// attach is still tracked. Failed cleanup is retained for explicit retry.
/// </summary>
internal static class EditorBackendLifecycle
{
    // RainWright.dll owns the lifecycle ledger; this file wires game delegates.
    private static readonly EditorBackendServiceLedger services = new();

    internal static bool CoreEnabled => services.CoreEnabled;
    internal static bool PendingCleanup => services.PendingCleanup;
    internal static bool ExtrasReady => services.ExtrasReady;

    internal static bool EnableCore()
    {
        if (services.CoreEnabled) return true;

        if (services.Count > 0)
        {
            Disable();
            if (services.Count > 0)
            {
                EditorDiagnostics.Error(
                    "Editor backend refused a new Enable because previous service cleanup is incomplete.");
                return false;
            }
        }

        // Source-of-truth preference precedence: Rainwright file > old Remix
        // legacy provider > standalone default. A bad owned file is fail-closed.
        EditorAutoSavePreferences.LoadOwned(BepInEx.Paths.ConfigPath);
        EditorShortcutGameplayPolicy.LoadOwned(BepInEx.Paths.ConfigPath);

        StartOptional("LegacyDevUiQuiescenceController",
            LegacyDevUiQuiescenceController.Enable,
            LegacyDevUiQuiescenceController.Disable);
        StartOptional("NativeSoundTriggerDevUiScheduler",
            NativeSoundTriggerDevUiScheduler.Enable,
            NativeSoundTriggerDevUiScheduler.Disable);
        StartOptional("NativeMapLayoutCompatibility",
            Map.NativeMapLayoutCompatibility.Enable,
            Map.NativeMapLayoutCompatibility.Disable);

        try
        {
            StartRequired("WorldTopologyRuntime",
                WorldTopologyRuntime.Enable,
                WorldTopologyRuntime.DisableAndVerify);
            StartRequired("DevToolRuntime",
                DevToolRuntime.Enable,
                DevToolRuntime.Disable);
            // WorldTextRegistry's additional lineage save MUST be available
            // with the core editor. Depending on a separately discovered
            // BepInPlugin could return fake success with dirty author data.
            StartRequired("WorldCreatureAuthoringHooks",
                () => WorldCreatureAuthoringHooks.Enable(EditorDiagnostics.LogSource),
                WorldCreatureAuthoringHooks.Disable);
            StartRequired("EditorSaveLifecycle", EditorSaveLifecycle.Enable, EditorSaveLifecycle.Disable);
            services.CoreEnabled = true;
            EditorStartupDiagnostics.Marker("EditorBackendLifecycle/Core", "ENABLED");
            return true;
        }
        catch (Exception error)
        {
            EditorStartupDiagnostics.Failure("EditorBackendLifecycle/Core", error);
            EditorDiagnostics.Warning(
                "Editor backend failed; reverse cleanup attempted while DryCycle gameplay remains available.");
            Disable();
            return false;
        }
    }

    internal static void EnableExtras(ManualLogSource logger)
    {
        if (!services.CoreEnabled) return;

        if (!services.ExtrasReady)
        {
            // A failed initial map install may have left a partially attached
            // service. Do not install another copy until that stop action is
            // successfully removed; keep its original cleanup diagnostics.
            if (services.Contains("PlayerMapBackendLifecycle"))
            {
                EditorDiagnostics.Warning(
                    "Player Map extras retry refused because an earlier service hook is still tracked.");
            }
            else
            {
                services.ExtrasReady = StartOptional("PlayerMapBackendLifecycle",
                    () => PlayerMapBackendLifecycle.Enable(logger),
                    PlayerMapBackendLifecycle.DisableAndVerify);
            }
        }

        // Static catalogs are independent of Player Map. They must still warm
        // after a map failure, but a later map-only retry must not repeat them.
        if (!services.ExtrasWarmupAttempted)
        {
            services.ExtrasWarmupAttempted = true;
            try
            {
                EditorStartupDiagnostics.Step("EditorBackendLifecycle/RoomSettingsPresentation.WarmStaticCatalogs",
                    RoomSettingsPresentation.WarmStaticCatalogs);
            }
            catch (Exception error)
            {
                EditorStartupDiagnostics.Failure(
                    "EditorBackendLifecycle/RoomSettingsPresentation.WarmStaticCatalogs", error);
            }
        }
    }

    private static void StartRequired(string name, Action start, Action stop)
    {
        services.Start(name,
            () => EditorStartupDiagnostics.Step("EditorBackendLifecycle/" + name, start),
            stop);
    }

    private static bool StartOptional(string name, Action start, Action stop)
    {
        // A retained lease must not be detached by a second attempted installer.
        if (services.Contains(name))
        {
            EditorDiagnostics.Warning(
                "Editor backend optional service still owns an incomplete cleanup: " + name);
            return false;
        }
        try
        {
            services.Start(name,
                () => EditorStartupDiagnostics.Step("EditorBackendLifecycle/Optional/" + name, start),
                stop);
            return true;
        }
        catch (Exception error)
        {
            EditorStartupDiagnostics.Failure("EditorBackendLifecycle/Optional/" + name, error);
            // Failure may happen after a partially installed detour. Retain the
            // ledger entry if cleanup fails; never pretend hooks were released.
            services.Release(name, CleanupService, ReportCleanupFailure);
            return false;
        }
    }

    private static void CleanupService(string name, Action stop) =>
        EditorStartupDiagnostics.Step("EditorBackendLifecycle/Cleanup/" + name, stop);

    private static void ReportCleanupFailure(string name, Exception error) =>
        EditorStartupDiagnostics.Failure("EditorBackendLifecycle/Cleanup/" + name, error);

    internal static void Disable()
    {
        // Stop every started/partially started editor component in reverse order.
        // A single failed release cannot skip later services.
        bool completelyReleased = services.ReleaseAll(CleanupService, ReportCleanupFailure);
        services.ResetModeState();

        // A surviving editor Hook could still publish revisions this frame.
        // Do not reset its document ownership until every service is detached.
        if (completelyReleased)
        {
            try
            {
                EditorStartupDiagnostics.Step(
                    "EditorBackendLifecycle/Cleanup/EditorRevisionHub", EditorRevisionHub.Reset);
            }
            catch (Exception error)
            {
                EditorStartupDiagnostics.Failure("EditorBackendLifecycle/Cleanup/EditorRevisionHub", error);
            }
        }
        if (services.Count > 0)
            EditorDiagnostics.Warning(
                "Editor backend cleanup incomplete: " + services.Count +
                " registration(s) retained for retry; duplicate startup remains blocked.");
    }
}
