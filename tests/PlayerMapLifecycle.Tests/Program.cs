using System;
using System.Linq;
using DryCycle.DevUI.DevTool.Map.PlayerMap;
using BepInEx.Logging;

internal static class Program
{
    private static int checks;

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Execute()
    {
        Check(PlayerMapConfigSerializerV2Plugin.PluginId ==
              "DryCycle.DevTool.PlayerMap.ConfigSerializerV2" &&
              PlayerMapConnectionMetadataSerializerPlugin.PluginId ==
              "DryCycle.DevTool.PlayerMap.ConnectionMetadata" &&
              PlayerMapMigrationStreamSerializerPlugin.PluginId ==
              "DryCycle.DevTool.PlayerMap.MigrationStreamSerializer" &&
              PlayerMapNormalizedConfigSerializerPlugin.PluginId ==
              "DryCycle.DevTool.PlayerMap.NormalizedConfig",
            "Retired Player Map plugin identifiers stay on the game compatibility boundary.");

        ManualLogSource log = new();
        Probe.Clear();
        PlayerMapBackendLifecycle.Disable();
        Probe.Clear();

        PlayerMapBackendLifecycle.Enable(log);
        int installedCount = Probe.Events.Count(x => x.StartsWith("+"));
        Check(installedCount == 13, "Exactly thirteen stateful Player Map backend services started.");
        PlayerMapBackendLifecycle.Enable(log);
        Check(Probe.Events.Count(x => x.StartsWith("+")) == 13,
            "OnEnable repeat cannot install any backend service twice.");

        Probe.Events.Clear();
        PlayerMapBackendLifecycle.Disable();
        var removals = Probe.Events.Where(x => x.StartsWith("-")).ToArray();
        Check(removals.Length == 13 && removals[0] == "-RenderPreparation" &&
              removals[12] == "-Workspace",
            "Backend Disable must unwind every service in reverse order.");
        Check(Probe.Events.IndexOf("reset:RenderPreparationController") < Probe.Events.IndexOf("-Workspace"),
            "Transient render/bake state is reset before the workspace owner detaches.");
        Check(!PlayerMapBackendLifecycle.PendingCleanup, "All hooks must be removed on clean Disable.");
        Probe.Events.Clear();
        PlayerMapBackendLifecycle.Disable();
        Check(Probe.Events.All(x => !x.StartsWith("-")),
            "Repeated Disable may reset state but must not double-remove hooks.");

        // Retired output validator GUID remains loadable but cannot add hooks
        // or toggle the mandatory Rainwright commit validation.
        Probe.Clear();
        var outputValidatorCompatibility = new PlayerMapRenderOutputValidatorPlugin();
        var compatFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var compatEnable = typeof(PlayerMapRenderOutputValidatorPlugin).GetMethod("OnEnable", compatFlags);
        var compatDisable = typeof(PlayerMapRenderOutputValidatorPlugin).GetMethod("OnDisable", compatFlags);
        Check(compatEnable == null && compatDisable == null &&
              PlayerMapRenderOutputValidatorPlugin.PluginId == "DryCycle.DevTool.PlayerMap.RenderOutputValidator",
            "The retired BepInEx output-validator GUID remains source- and install-compatible.");
        Check(Probe.Events.Count == 0,
            "Retired output-validator compatibility entry registered a duplicate service.");

        Probe.Clear();
        Probe.ThrowOnEnable = "GroupCommands";
        bool caughtOriginal = false;
        try { PlayerMapBackendLifecycle.Enable(log); }
        catch (InvalidOperationException e) { caughtOriginal = e.Message.Contains("Injected enable failure"); }
        Check(caughtOriginal, "Mid-bootstrap failure propagates original exception.");
        Check(Probe.Events.Contains("-GroupCommands") &&
              Probe.Events.Contains("-Workspace"),
            "Partially installed failing step and all earlier steps must be rolled back.");
        Check(!PlayerMapBackendLifecycle.PendingCleanup, "Successful rollback clears the partial Enable ledger.");
        Probe.ThrowOnEnable = null;
        Probe.Clear();
        PlayerMapBackendLifecycle.Enable(log);
        PlayerMapBackendLifecycle.Disable();
        Check(!PlayerMapBackendLifecycle.PendingCleanup, "Backend may start after a failed but cleaned Enable.");

        Probe.Clear();
        PlayerMapBackendLifecycle.Enable(log);
        Probe.ThrowOnDisable = "ConfigBuild";
        Probe.DisableFailures = 1;
        PlayerMapBackendLifecycle.Disable();
        Check(PlayerMapBackendLifecycle.PendingCleanup,
            "Failed cleanup retains the exact service for retry.");
        Check(Probe.Events.Contains("-Workspace"),
            "Failed cleanup must not prevent later services from detaching.");
        Check(log.Errors.Any(x => x.Contains("ConfigBuild") && x.Contains("Injected cleanup failure")),
            "Cleanup error diagnostics must retain service name and original exception.");
        Probe.ThrowOnDisable = null;
        PlayerMapBackendLifecycle.Disable();
        Check(!PlayerMapBackendLifecycle.PendingCleanup,
            "Later Disable retries and releases a failed cleanup.");

        Probe.Clear();
        PlayerMapBackendLifecycle.Enable(log);
        Probe.ThrowOnDisable = "TerrainBake";
        Probe.DisableFailures = 2;
        bool ownershipRetained = false;
        try { PlayerMapBackendLifecycle.DisableAndVerify(); }
        catch (InvalidOperationException error)
        {
            ownershipRetained = error.Message.Contains("cleanup is incomplete");
        }
        Check(ownershipRetained && PlayerMapBackendLifecycle.PendingCleanup &&
              PlayerMapBackendLifecycle.OwnsWorkspace,
            "Outer editor lifecycle must retain its lease after a child Hook failed to detach.");
        bool secondRetryBlocked = false;
        try { PlayerMapBackendLifecycle.DisableAndVerify(); }
        catch (InvalidOperationException) { secondRetryBlocked = true; }
        Check(secondRetryBlocked && PlayerMapBackendLifecycle.PendingCleanup,
            "Repeated failed child cleanup still reports failure to the parent.");
        Probe.ThrowOnDisable = null;
        PlayerMapBackendLifecycle.DisableAndVerify();
        Check(!PlayerMapBackendLifecycle.PendingCleanup &&
              !PlayerMapBackendLifecycle.OwnsWorkspace,
            "Verified cleanup eventually releases the owner so the editor may restart.");

        Probe.Clear();
        Probe.ThrowOnReset = "RenderScheduler";
        PlayerMapBackendLifecycle.Disable();
        Check(Probe.Events.Contains("reset:RoomMapBakeCache"),
            "Failure in one reset must not skip later caches.");
        Check(log.Errors.Any(x => x.Contains("Reset/Scheduler") && x.Contains("Injected reset failure")),
            "Reset error is logged with the original exception.");

        // Historical GUIDs are inert aliases; the single RainWright backend owns startup.
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        Check(typeof(PlayerMapRuntimePlugin).GetMethod("OnEnable", flags) == null &&
              typeof(PlayerMapRuntimePlugin).GetMethod("OnDisable", flags) == null,
            "Retired aliases cannot acquire or release central backend hooks.");

        // Production compatibility lease exercises both fallback and takeover,
        // including the case when the backend has already torn down before the
        // separate BepInEx component receives its own OnDisable.
        Probe.Clear();
        var early = new PlayerMapAuxiliaryCompatibilityLease(
            "ConfigBuild", PlayerMapConfigBuildPipeline.Disable);
        early.Enable(() => PlayerMapConfigBuildPipeline.Enable(log));
        early.Enable(() => PlayerMapConfigBuildPipeline.Enable(log));
        Check(Probe.Events.Count(x => x == "+ConfigBuild") == 1,
            "An auxiliary entrypoint cannot install its fallback twice.");
        PlayerMapBackendLifecycle.Enable(log);
        Probe.Events.Clear();
        early.Disable();
        Check(!Probe.Events.Contains("-ConfigBuild"),
            "Auxiliary Disable cannot destroy a currently centrally owned service.");
        PlayerMapBackendLifecycle.Disable();
        Probe.Events.Clear();
        early.Disable();
        Check(!Probe.Events.Contains("-ConfigBuild"),
            "After ownership transfer, repeated helper Disable cannot remove the service twice.");

        Probe.Clear();
        var delayed = new PlayerMapAuxiliaryCompatibilityLease(
            "RenderRevision", PlayerMapRenderRevisionGuard.Disable);
        delayed.Enable(() => PlayerMapRenderRevisionGuard.Enable(log));
        PlayerMapBackendLifecycle.Enable(log);
        PlayerMapBackendLifecycle.Disable();
        Probe.Events.Clear();
        delayed.Disable();
        Check(!Probe.Events.Contains("-RenderRevision"),
            "A helper must not undo a service that the central backend already cleaned up.");

        Probe.Clear();
        var owned = new PlayerMapAuxiliaryCompatibilityLease(
            "TerrainBake", PlayerMapTerrainBakeBridge.Disable);
        PlayerMapBackendLifecycle.Enable(log);
        Probe.Events.Clear();
        owned.Enable(() => PlayerMapTerrainBakeBridge.Enable(log));
        owned.Disable();
        Check(Probe.Events.All(x => x != "+TerrainBake" && x != "-TerrainBake"),
            "Auxiliary plugin activated while central backend owns a service must be a no-op.");
        PlayerMapBackendLifecycle.Disable();

        Probe.Clear();
        var standalone = new PlayerMapAuxiliaryCompatibilityLease(
            "TerrainBake", PlayerMapTerrainBakeBridge.Disable);
        standalone.Enable(() => PlayerMapTerrainBakeBridge.Enable(log));
        standalone.Disable();
        standalone.Disable();
        Check(Probe.Events.Count(x => x == "+TerrainBake") == 1 &&
              Probe.Events.Count(x => x == "-TerrainBake") == 1,
            "Standalone fallback preserves one symmetric install/remove.");

        Probe.Clear();
        var partial = new PlayerMapAuxiliaryCompatibilityLease(
            "GroupCommands", PlayerMapGroupCommandRuntime.Disable);
        Probe.ThrowOnEnable = "GroupCommands";
        InvalidOperationException original = null;
        try { partial.Enable(() => PlayerMapGroupCommandRuntime.Enable(log)); }
        catch (InvalidOperationException error) { original = error; }
        Check(original?.Message.Contains("Injected enable failure") == true,
            "Compatibility fallback must rethrow its original failed startup.");
        partial.Disable();
        Check(Probe.Events.Contains("-GroupCommands"),
            "A failing fallback is still recorded so the caller's rollback can detach it.");
        Probe.ThrowOnEnable = null;

        Probe.Clear();
        var retry = new PlayerMapAuxiliaryCompatibilityLease(
            "GroupCommands", PlayerMapGroupCommandRuntime.Disable);
        retry.Enable(() => PlayerMapGroupCommandRuntime.Enable(log));
        Probe.ThrowOnDisable = "GroupCommands";
        Probe.DisableFailures = 1;
        bool failedRemoval = false;
        try { retry.Disable(); }
        catch (InvalidOperationException) { failedRemoval = true; }
        Check(failedRemoval, "A failing compatibility removal surfaces its original exception.");
        Probe.ThrowOnDisable = null;
        retry.Disable();
        Check(Probe.Events.Count(x => x == "-GroupCommands") == 2,
            "An auxiliary cleanup failure retains its lease for the next retry.");

        Console.WriteLine("PASS: " + checks + " assertions; production Player Map lifecycle/rollback tests.");
    }

    public static int Main()
    {
        try { Execute(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
