using System;
using System.IO;
using DryCycle.DayNight;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.Commands;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.History;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.World;

internal static class SchedulerTests
{
    private static int checks;
    private static void Check(bool ok, string reason) { checks++; if (!ok) throw new Exception(reason); }
    internal static void Run()
    {
        AutoSaveCoordinator.Shutdown();
        EditorActions.SaveCount = 0; EditorActions.SaveResult = true;
        DevToolSessionHub.IsCurrentSessionLive = true; DevToolSessionHub.Current = null;
        RegionDayNightOptions.DevToolAutoSaveEnabled = true;
        using var preferences = EditorAutoSavePreferences.Register(() => RegionDayNightOptions.DevToolAutoSaveEnabled, () => 150);
        string path = Path.Combine(BepInEx.Paths.ConfigPath, "room.txt");
        File.WriteAllText(path, "author");
        var session = new EditorSession { RoomSettings = new RoomSettings { filePath = path, content = "pending" } };
        try
        {
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 0, "Opening an unchanged document does not save.");
            session.History.CommitEdit();
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 1, "Committed edits save immediately, without a clock interval.");
            for (int i = 0; i < 250; i++) AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 1, "Stable frames do not rewrite the same document.");
            UnityEngine.Input.MousePressed = true; session.History.CommitEdit();
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 1, "Pointer gestures defer disk writes.");
            UnityEngine.Input.MousePressed = false; AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 2, "Pointer release saves once.");
            session.LegacyTransactions.HasPendingTransaction = true; session.History.CommitEdit();
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 2, "Unfinished legacy transactions are not written.");
            session.LegacyTransactions.CommitPending(session); AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 3, "Committed legacy edits save immediately.");
            EditorContinuousTransactionHub.Active = true; session.History.CommitEdit();
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 3, "Continuous native previews are not persisted mid-drag.");
            EditorContinuousTransactionHub.Active = false; AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 4, "Finished native gestures save once.");
            EditorInputRouter.WantsTextInput = true; session.History.CommitEdit();
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 5, "Committed fields save while retaining keyboard focus.");
            RegionDayNightOptions.DevToolAutoSaveEnabled = false; session.History.CommitEdit();
            AutoSaveCoordinator.Tick(session);
            Check(EditorActions.SaveCount == 5, "The automatic-save preference remains effective.");
            DevToolSessionHub.IsCurrentSessionLive = false;
            Check(AutoSaveCoordinator.FlushOnExit(session, "H closed UI") && EditorActions.SaveCount == 6, "H-close flush bypasses interval, focus and preference after UI dormancy.");
            WorldTextRegistry.Dirty = true; WorldTextRegistry.Revision++;
            Check(AutoSaveCoordinator.FlushOnExit(session) && EditorActions.SaveCount == 7 && !WorldTextRegistry.Dirty && !EditorActions.LastIncludedActiveDocument, "World-only edits flush after switching tools without rewriting unrelated RoomSettings or map geometry.");
            Check(AutoSaveCoordinator.FlushOnExit(session) && EditorActions.SaveCount == 7, "Duplicate exit hooks do not write again.");
            var reopened = new EditorSession { RoomSettings = session.RoomSettings, DocumentKey = session.DocumentKey };
            RegionDayNightOptions.DevToolAutoSaveEnabled = true; DevToolSessionHub.IsCurrentSessionLive = true;
            AutoSaveCoordinator.Tick(reopened);
            Check(EditorActions.SaveCount == 7, "Reopened sessions use independent history clocks.");
            EditorActions.SaveResult = false; reopened.History.CommitEdit();
            AutoSaveCoordinator.Tick(reopened);
            for (int i = 0; i < 100; i++) AutoSaveCoordinator.Tick(reopened);
            Check(EditorActions.SaveCount == 8, "Failed writes cannot form a per-frame IO loop.");
            WorldTextRegistry.Dirty = true; WorldTextRegistry.Revision++;
            AutoSaveCoordinator.Tick(reopened);
            Check(EditorActions.SaveCount == 9 && WorldTextRegistry.Dirty, "New world commits retry immediately and failures retain dirty state.");
            UnityEngine.Input.MousePressed = true; EditorContinuousTransactionHub.Active = true;
            Check(!AutoSaveCoordinator.FlushOnExit(reopened) && EditorActions.SaveCount == 10, "Quitting commits current gestures and forces a retry.");
            Check(File.ReadAllText(path) == "author", "Failed writes and recovery capture preserve author bytes.");
            EditorActions.SaveResult = true;
            Check(AutoSaveCoordinator.FlushOnExit(reopened) && EditorActions.SaveCount == 11, "Explicit retries can save retained edits.");
            AutoSaveCoordinator.Shutdown();
            Check(EditorActions.SaveCount == 11, "Clean shutdown is idempotent after flush.");
            UnityEngine.Input.MousePressed = false;
            var browsing = new EditorSession { RoomSettings = session.RoomSettings };
            Check(browsing.History.Revision > 0 && browsing.History.AuthoringRevision == 0,
                "The real journal starts with a presentation revision but no authored edits.");
            AutoSaveCoordinator.Tick(browsing);
            browsing.DocumentKey = new EditorDocumentKey(EditorDocumentKind.RegionMap, "browse");
            AutoSaveCoordinator.Tick(browsing);
            Check(EditorActions.SaveCount == 11, "Opening and switching untouched documents never writes author files.");
            browsing.History.CommitEdit();
            AutoSaveCoordinator.Tick(browsing);
            Check(EditorActions.SaveCount == 12, "The first real edit still saves on its first tick.");
            browsing.DocumentKey = new EditorDocumentKey(EditorDocumentKind.Room);
            AutoSaveCoordinator.Tick(browsing);
            browsing.DocumentKey = new EditorDocumentKey(EditorDocumentKind.RegionMap, "browse");
            AutoSaveCoordinator.Tick(browsing);
            Check(EditorActions.SaveCount == 12, "Returning to saved documents preserves their own saved author revisions.");
            Check(browsing.History.Undo(), "Undo uses the real document journal."); AutoSaveCoordinator.Tick(browsing);
            Check(EditorActions.SaveCount == 13, "Undo remains an immediate author write.");
            Check(browsing.History.Redo(), "Redo uses the real document journal."); AutoSaveCoordinator.Tick(browsing);
            Check(EditorActions.SaveCount == 14, "Redo remains an immediate author write.");
            var editedBeforeTick = new EditorSession { RoomSettings = session.RoomSettings };
            editedBeforeTick.History.CommitEdit();
            AutoSaveCoordinator.Tick(editedBeforeTick);
            Check(EditorActions.SaveCount == 15, "An edit accepted before the first scheduler tick is never mistaken for an initial baseline.");
            Console.WriteLine($"  PASS {checks} assertions; immediate commits, gestures, H/quit flush, failure throttling and session isolation.");
        }
        finally
        {
            EditorActions.SaveResult = true; WorldTextRegistry.Dirty = false;
            EditorInputRouter.WantsTextInput = false; UnityEngine.Input.MousePressed = false;
            EditorContinuousTransactionHub.Active = false;
            AutoSaveCoordinator.Shutdown();
        }
    }
}
