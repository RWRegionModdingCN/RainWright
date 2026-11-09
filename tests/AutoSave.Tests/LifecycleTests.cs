using System;
using System.IO;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.Commands;
using DryCycle.DevUI.DevTool.Core;

internal static class LifecycleTests
{
    private static int checks;
    private static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    internal static void Run()
    {
        AutoSaveCoordinator.Shutdown();
        EditorActions.SaveCount = 0; EditorActions.SaveResult = true;
        string target = Path.Combine(BepInEx.Paths.ConfigPath, "lifecycle-room.txt");
        File.WriteAllText(target, "author");
        var game = new RainWorldGame();
        var ui = new DevUI { game = game }; game.devUI = ui;
        var session = new EditorSession { Owner = ui, RoomSettings = new RoomSettings { filePath = target, content = "pending" } };
        session.History.CommitEdit();
        DevToolSessionHub.Current = session; DevToolSessionHub.IsCurrentSessionLive = true;
        int originals = 0;
        EditorSaveLifecycle.Enable();
        try
        {
            UnityEngine.Input.CloseHeld = true;
            int closeDrains = DevToolSubsystemCoordinator.Drains;
            On.RainWorldGame.CloseForTest(game, () =>
            {
                Check(EditorActions.SaveCount == 1 && DevToolSubsystemCoordinator.Drains == closeDrains + 1, "H-close must drain accepted edits and save before native clears the UI.");
                originals++; game.devUI = null; DevToolSessionHub.IsCurrentSessionLive = false;
            });
            UnityEngine.Input.CloseHeld = false;
            Check(originals == 1 && EditorActions.SaveCount == 1, "The real close hook must call native once and save after H destroys the UI reference.");
            On.RainWorldGame.CloseForTest(game, () => originals++);
            Check(originals == 2 && EditorActions.SaveCount == 1, "Dormant frames cannot repeatedly save.");
            game.devUI = ui; DevToolSessionHub.IsCurrentSessionLive = true; session.History.CommitEdit();
            DryCycle.DevUI.DevTool.Input.EditorInputRouter.WantsTextInput = true;
            UnityEngine.Input.CloseHeld = true;
            int textDrains = DevToolSubsystemCoordinator.Drains;
            On.RainWorldGame.CloseForTest(game, () => originals++);
            Check(EditorActions.SaveCount == 1 && DevToolSubsystemCoordinator.Drains == textDrains, "Typing a captured H must not drain commands or trigger a close-save.");
            DryCycle.DevUI.DevTool.Input.EditorInputRouter.WantsTextInput = false;
            UnityEngine.Input.CloseHeld = false;
            int drains = DevToolSubsystemCoordinator.Drains;
            On.RainWorldGame.ShutdownForTest(game, () =>
            {
                Check(EditorActions.SaveCount == 2, "Save must happen before native world teardown.");
                originals++; DevToolSessionHub.IsCurrentSessionLive = false;
            });
            Check(originals == 4 && DevToolSubsystemCoordinator.Drains == drains + 1, "Game shutdown drains accepted commands once and invokes native once.");
            session.History.CommitEdit();
            UnityEngine.Application.QuitForTest();
            Check(EditorActions.SaveCount == 3, "Application quit must flush pending data even when normal live UI updates have stopped.");
            EditorSaveLifecycle.Disable();
            var dormant = new EditorSession { Owner = ui, RoomSettings = session.RoomSettings };
            dormant.History.CommitEdit();
            DevToolSessionHub.Current = dormant;
            UnityEngine.Application.QuitForTest();
            On.RainWorldGame.CloseForTest(game, () => originals++);
            Check(EditorActions.SaveCount == 3 && originals == 5, "Disabled lifecycle hooks cannot retain background writers or suppress native updates.");
            DevToolSessionHub.Current = session;
            EditorSaveLifecycle.Enable(); EditorSaveLifecycle.Disable();
            Check(EditorActions.SaveCount == 3, "Re-enable/disable cannot duplicate exit subscriptions or clean writes.");
            Console.WriteLine($"  PASS {checks} native H/O-close, process teardown, application quit and hook lifetime assertions.");
        }
        finally
        {
            UnityEngine.Input.CloseHeld = false;
            DryCycle.DevUI.DevTool.Input.EditorInputRouter.WantsTextInput = false;
            DevToolSessionHub.Current = null;
            EditorSaveLifecycle.Disable(); AutoSaveCoordinator.Shutdown();
        }
    }
}
