using System;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.World;
using BepInEx.Logging;

internal static class Program
{
    private static int assertions;

    private static void Check(bool condition, string reason)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(reason);
    }

    private static void Run()
    {
        var log = new ManualLogSource();
        WorldCreatureAuthoringHooks.Disable();
        WorldLineageRegistry.Dirty = false;
        Check(!WorldCreatureAuthoringHooks.AdditionalDirty &&
              WorldCreatureAuthoringHooks.SaveAdditional("AA") &&
              WorldLineageRegistry.Saves == 0,
            "Clean lineage authoring is a no-op even before the editor starts.");

        WorldLineageRegistry.Dirty = true;
        Check(WorldCreatureAuthoringHooks.AdditionalDirty,
            "Unactivated editor must not hide dirty lineage modifications.");
        Check(!WorldCreatureAuthoringHooks.SaveAdditional("AA") &&
              EditorDiagnostics.Errors.Count == 1 &&
              EditorDiagnostics.Errors[0].Contains("Refusing to report a successful save") &&
              WorldLineageRegistry.Saves == 0 && WorldLineageRegistry.Dirty,
            "Dirty lineage save is rejected and logged when the required authoring service is offline.");

        WorldCreatureAuthoringHooks.Enable(log);
        WorldCreatureAuthoringHooks.Enable(log);
        Check(log.Info.Count == 1, "Repeated editor enable cannot register authoring twice.");

        WorldLineageRegistry.SaveSucceeds = false;
        Check(!WorldCreatureAuthoringHooks.SaveAdditional("AA") &&
              WorldLineageRegistry.Dirty &&
              log.Warnings.Count == 1 &&
              log.Warnings[0].Contains("injected writer failure"),
            "Actual lineage writer failure is surfaced without clearing unsaved content.");

        WorldLineageRegistry.SaveSucceeds = true;
        WorldTextRegistry.ReloadSucceeds = false;
        Check(!WorldCreatureAuthoringHooks.SaveAdditional("AA") &&
              WorldTextRegistry.Reloads == 1 &&
              WorldTextRegistry.LastRegion == "AA" &&
              WorldCreatureAuthoringHooks.AdditionalDirty &&
              log.Warnings.Exists(x => x.Contains("could not be reloaded")),
            "Successful lineage persistence must retain dirty/synchronization state after world.txt reload failure.");
        int savesBeforeRetry = WorldLineageRegistry.Saves;
        WorldTextRegistry.ReloadSucceeds = true;
        Check(WorldCreatureAuthoringHooks.SaveAdditional("AA") &&
              WorldTextRegistry.Reloads == 2 &&
              WorldLineageRegistry.Saves == savesBeforeRetry &&
              !WorldCreatureAuthoringHooks.AdditionalDirty,
            "Retry a failed reparse without rewriting the already persisted lineage author file.");

        WorldLineageRegistry.Dirty = true;
        WorldTextRegistry.ReloadSucceeds = true;
        Check(WorldCreatureAuthoringHooks.SaveAdditional("BB") &&
              WorldTextRegistry.LastRegion == "BB" &&
              !WorldLineageRegistry.Dirty &&
              !WorldCreatureAuthoringHooks.AdditionalDirty,
            "Successful lineage save and reparse clear unsaved status.");

        WorldLineageRegistry.Dirty = true;
        int reloads = WorldTextRegistry.Reloads;
        Check(WorldCreatureAuthoringHooks.SaveAdditional("") &&
              WorldTextRegistry.Reloads == reloads,
            "No active region skips only the world document reload.");

        DevToolRuntime.ActiveSession = new Session
        {
            World = new World
            {
                abstractRooms = new[]
                {
                    new AbstractRoom { name = "AA_01" },
                    new AbstractRoom { name = "AA_02" }
                }
            }
        };
        WorldTextRegistry.SpawnByRoom["AA_02"] =
            new[] { new WorldCreatureSpawnRecord { Id = 12 } };
        Check(WorldCreatureAuthoringHooks.FindSpawnRoom("AA", 12) == "AA_02",
            "Active authoring can find the exact creature-spawn room.");
        WorldCreatureAuthoringHooks.OnCreatureAdded("AA", "AA_01");
        WorldCreatureAuthoringHooks.OnCreatureEdited("AA", "AA_02");
        Check(WorldCreatureLiveReload.Previewed.Count == 2,
            "Active authoring dispatches bounded room-local preview on mutations.");

        WorldCreatureAuthoringHooks.Disable();
        WorldCreatureAuthoringHooks.OnCreatureAdded("AA", "AA_01");
        Check(WorldCreatureLiveReload.ResetCount >= 2 &&
              WorldCreatureLiveReload.Previewed.Count == 2 &&
              WorldCreatureAuthoringHooks.FindSpawnRoom("AA", 12) == "",
            "Disabling editor stops creating new previews and releases the live session lookup.");

        WorldLineageRegistry.Dirty = true;
        Check(WorldCreatureAuthoringHooks.AdditionalDirty &&
              !WorldCreatureAuthoringHooks.SaveAdditional("AA"),
            "A service teardown cannot hide or falsely save later dirty lineage data.");

        WorldCreatureAuthoringHooks.Enable(log);
        WorldTextRegistry.ReloadSucceeds = false;
        Check(!WorldCreatureAuthoringHooks.SaveAdditional("CC") &&
              !WorldLineageRegistry.Dirty &&
              WorldCreatureAuthoringHooks.AdditionalDirty,
            "An unsuccessful post-save reparse retains pending synchronization.");
        int writesAfterFailure = WorldLineageRegistry.Saves;
        WorldCreatureAuthoringHooks.Disable();
        Check(WorldCreatureAuthoringHooks.AdditionalDirty &&
              !WorldCreatureAuthoringHooks.SaveAdditional("CC") &&
              WorldLineageRegistry.Saves == writesAfterFailure,
            "Disabling the editor cannot erase pending reparse or trigger extra writes.");
        WorldCreatureAuthoringHooks.Enable(log);
        WorldTextRegistry.ReloadSucceeds = true;
        Check(WorldCreatureAuthoringHooks.SaveAdditional("CC") &&
              WorldLineageRegistry.Saves == writesAfterFailure &&
              !WorldCreatureAuthoringHooks.AdditionalDirty,
            "Reopening editor retries the pending reparse without rewriting source data.");
        WorldCreatureAuthoringHooks.Disable();

        Console.WriteLine("PASS: " + assertions +
            " assertions; production WorldCreatureAuthoringHooks write-safety, reload and live-preview.");
    }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
