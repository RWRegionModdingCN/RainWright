using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using BepInEx;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.World;

internal static class Program
{
    private static int assertions;
    private static string baseDirectory;

    private static int Main()
    {
        try
        {
            TestEditorDiagnostics();
            TestEditorStartupDiagnostics();
            Run("No author overwrite, duplicate prevention, crash recovery", TestCaptureAndCrash);
            Run("Recovery text matches native UTF8 writer bytes", TestRecoveryUtf8);
            Run("Read-only legacy crash recovery migration", TestLegacyRecoveryImport);
            Run("Legacy snapshots without a crash marker are never imported", TestCleanLegacyDoesNotImport);
            Run("Tampered legacy snapshot is never imported", TestCorruptLegacyDoesNotImport);
            Run("RoomSettings production serializer snapshots", TestRoomCapture);
            Run("Autosave scheduler and authoring gates", SchedulerTests.Run);
            Run("Native close and quit persistence boundaries", LifecycleTests.Run);
            Run("Failed exit save retains recovery on clean shutdown", TestFailedExitRecovery);
            Run("No stale recovery after clean shutdown", TestCleanShutdown);
            Run("Corrupt recovery data refused", TestCorruptData);
            Run("Tampered manifest refused", TestBadManifest);
            Run("Independent document retention", TestPrunePerDocument);
            Run("Configurable recovery retention", TestConfiguredRetention);
            Run("Explicit restore and author backup", TestRestore);
            Run("Restore preserves old backup and stale staging file", TestRestorePreservesPriorArtifacts);
            Run("Recovery refuses external changes after candidate listing", TestRestoreExternalConflict);
            Run("Recovery refuses newly created author source after listing", TestRestoreSourceAppearance);
            Run("Recovery refuses switched/disabled Mod author sources", TestGenericRestoreOwner);
            Run("Recovery refuses same-directory author identity replacement", TestSamePathDifferentModIdentity);
            Run("Recovery refuses newly overlaid source", TestOverlaidRecoverySource);
            Run("Recovery refuses symlink redirected Mod author files", TestSymlinkRecoverySource);
            Run("Recovery respects targeted/newest author source precedence", TestVersionedRecoverySource);
            Run("WorldTopology restore requires current game owner", TestTopologyRestoreOwner);
            Run("Unsafe cache paths rejected", TestMergedCacheProtection);
            Run("Oversized snapshot rejected", TestOversizedSnapshot);
            Run("Nonexistent author folder cannot be restored", TestMissingAuthorFolder);
            Run("New background recovery binds to its active author Mod without manual selection", TestNewBackgroundRecovery);
            Run("Private palette draft recovery preserves unrelated config files", TestPaletteDraftRecovery);
            Console.WriteLine("PASS: " + assertions +
                " assertions; production crash snapshot persistence, integrity, rollback and retention.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Run(string name, Action test)
    {
        RecoverySnapshotStore.End();
        baseDirectory = Path.Combine(Path.GetTempPath(), "DryCycle-AutoSave-Tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseDirectory);
        Paths.ConfigPath = baseDirectory;
        ModManager.ActiveMods = new()
        {
            new() { id = "authoring-test", path = Path.Combine(baseDirectory, "author") }
        };
        try
        {
            test();
            Console.WriteLine("  PASS " + name);
        }
        catch (IOException error) when (test == TestSymlinkRecoverySource && OperatingSystem.IsWindows() && (error.HResult & 0xffff) == 1314)
        {
            Console.WriteLine("  SKIP " + name + ": Windows symlink privilege unavailable; CI runs this on Linux.");
        }
        finally
        {
            RecoverySnapshotStore.End();
            try { Directory.Delete(baseDirectory, recursive: true); }
            catch { }
        }
    }

    private static string RecoveryRoot => Path.Combine(baseDirectory, "Rainwright", "AutoSave");
    private static void TestFailedExitRecovery()
    {
        string target = Author("failed-exit.txt");
        File.WriteAllText(target, "original-author");
        var session = new DryCycle.DevUI.DevTool.Core.EditorSession { RoomSettings = new RoomSettings { filePath = target, content = "unsaved-room" } };
        session.History.CommitEdit();
        DryCycle.DevUI.DevTool.Commands.EditorActions.SaveResult = false;
        Check(!AutoSaveCoordinator.FlushOnExit(session), "A failed exit writer must report failure.");
        DryCycle.DevUI.DevTool.Commands.EditorActions.SaveResult = true;
        var other = new DryCycle.DevUI.DevTool.Core.EditorSession { RoomSettings = session.RoomSettings };
        other.History.CommitEdit();
        Check(AutoSaveCoordinator.FlushOnExit(other) && AutoSaveCoordinator.LastSaveFailed,
            "Saving another session cannot erase the earlier failed document's recovery requirement.");
        AutoSaveCoordinator.Shutdown();
        Check(File.Exists(Path.Combine(RecoveryRoot, "active.lock")), "Orderly shutdown after a failed exit save must retain the recovery marker.");
        RecoverySnapshotStore.Begin();
        Check(RecoverySnapshotStore.Pending.Length == 1 && File.ReadAllText(target) == "original-author", "The next session must offer the failed exit snapshot without replacing author data.");
        DryCycle.DevUI.DevTool.Commands.EditorActions.SaveResult = true;
    }
    private static void TestNewBackgroundRecovery()
    {
        string target = Author(Path.Combine("assets", "regionkit", "backgrounds", "new-background.txt"));
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("ROOM/Background", target, "Type: AboveCloudsView\n");
        Check(!File.Exists(target), "Recovery capture must not create an authored background file.");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1, "A new background file must remain recoverable after a crash.");
        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.Exists(target) && File.ReadAllText(target) == "Type: AboveCloudsView\n" && RecoverySnapshotStore.RestoreRequiresReload,
            "Explicit recovery must create the new background in its active author Mod and require reload.");
    }
    private static string LegacyRecoveryRoot => Path.Combine(baseDirectory, "DryCycle", "AutoSave");

    private static string Author(string file)
    {
        string folder = Path.Combine(baseDirectory, "author");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, file);
    }

    private static string[] Snapshots() => Directory.GetFiles(RecoveryRoot, "snapshot-*.data");

    private static void CrashRestart()
    {
        // End only resets static state; recreating the marker simulates a process
        // that stopped without completing End().
        RecoverySnapshotStore.End();
        File.WriteAllText(Path.Combine(RecoveryRoot, "active.lock"), "interrupted");
        RecoverySnapshotStore.Begin();
    }

    private static void TestCaptureAndCrash()
    {
        string target = Author("A.txt");
        File.WriteAllText(target, "author-original");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("A", target, "unsaved");
        DateTime idleStamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        Directory.SetLastWriteTimeUtc(RecoveryRoot, idleStamp);
        RecoverySnapshotStore.CaptureText("A", target, "unsaved");
        Check(Directory.GetLastWriteTimeUtc(RecoveryRoot) == idleStamp,
            "An unchanged recovery capture must not create and delete another temporary file.");
        Check(File.ReadAllText(target) == "author-original", "Capture must not overwrite the author file.");
        Check(Snapshots().Length == 1, "An unchanged recovery payload must be deduplicated.");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1, "Unexpected exit must expose valid recovery candidates.");
        Check(RecoverySnapshotStore.Pending[0].Document == "A", "Candidate must be attached to its document.");
        RecoverySnapshotStore.Dismiss(RecoverySnapshotStore.Pending[0].Id);
        Check(RecoverySnapshotStore.Pending.Length == 0, "Dismiss must hide the candidate.");
    }

    private static void TestRecoveryUtf8()
    {
        string target = Author("unicode.txt"), reference = Author("encoding-reference.txt");
        File.WriteAllText(target, "author-original");
        string content = "雨匠\r\nWorld\0 Emoji: \ud83d\ude00 broken: \ud800 tail\r\n";
        File.WriteAllText(reference, content, new System.Text.UTF8Encoding(false));
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("Unicode", target, content);
        Check(File.ReadAllBytes(Snapshots().Single()).SequenceEqual(File.ReadAllBytes(reference)),
            "Prepared recovery bytes must match the original framework writer, including surrogate fallback and newlines.");
        Check(File.ReadAllText(target) == "author-original", "UTF8 recovery preparation must preserve the author file.");
    }

    private static void WriteLegacyRecoveryFixture(string target, string content,
        bool crashed, bool corruptPayload = false)
    {
        Directory.CreateDirectory(LegacyRecoveryRoot);
        string id = Guid.NewGuid().ToString("N");
        string dataPath = Path.Combine(LegacyRecoveryRoot, "snapshot-" + id + ".data");
        string manifest = Path.Combine(LegacyRecoveryRoot, "snapshot-" + id + ".xml");
        byte[] original = Encoding.UTF8.GetBytes(content);
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original))
            .ToLowerInvariant();
        File.WriteAllBytes(dataPath, original);
        if (corruptPayload)
            File.WriteAllText(dataPath, "tampered payload");

        new XDocument(new XElement("Recovery",
            new XAttribute("version", "1"),
            new XAttribute("document", "legacy"),
            new XAttribute("target", Path.GetFullPath(target)),
            new XAttribute("utc", DateTime.UtcNow.ToString("O")),
            new XAttribute("sha256", hash))).Save(manifest);

        if (crashed)
            File.WriteAllText(Path.Combine(LegacyRecoveryRoot, "active.lock"), "legacy crashed");
    }

    private static void TestLegacyRecoveryImport()
    {
        string target = Author("legacy-world.txt");
        File.WriteAllText(target, "original-author");
        WriteLegacyRecoveryFixture(target, "recover-legacy", crashed: true);
        string originalLegacyManifest =
            File.ReadAllText(Directory.GetFiles(LegacyRecoveryRoot, "*.xml")[0]);
        string originalLegacyData =
            File.ReadAllText(Directory.GetFiles(LegacyRecoveryRoot, "*.data")[0]);

        RecoverySnapshotStore.Begin();
        Check(RecoverySnapshotStore.Pending.Length == 1 &&
              RecoverySnapshotStore.Pending[0].Document == "legacy",
            "First Rainwright session must offer valid crashed DryCycle recovery.");
        Check(Snapshots().Length == 1 &&
              File.Exists(Path.Combine(RecoveryRoot, "legacy-recovery-imported.marker")),
            "Legacy recovery is copied once to Rainwright's independent directory.");
        Check(File.ReadAllText(target) == "original-author",
            "Import must not overwrite any author source before explicit restore.");
        Check(File.ReadAllText(Directory.GetFiles(LegacyRecoveryRoot, "*.xml")[0]) ==
              originalLegacyManifest &&
              File.ReadAllText(Directory.GetFiles(LegacyRecoveryRoot, "*.data")[0]) ==
              originalLegacyData &&
              File.Exists(Path.Combine(LegacyRecoveryRoot, "active.lock")),
            "Old metadata, immutable snapshot and legacy active marker are read-only.");

        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "recover-legacy" &&
              File.ReadAllText(target + ".Rainwright-before-recovery.bak") == "original-author",
            "Legacy recovery uses explicit action and creates a new Rainwright author backup.");
        RecoverySnapshotStore.End();
        Check(File.Exists(Path.Combine(LegacyRecoveryRoot, "active.lock")),
            "Closing Rainwright must never clear another plugin's crash marker.");
        RecoverySnapshotStore.Begin();
        Check(RecoverySnapshotStore.Pending.Length == 0,
            "Clean restart must not reoffer the previously imported legacy snapshot.");
    }

    private static void TestCleanLegacyDoesNotImport()
    {
        string target = Author("clean-legacy.txt");
        WriteLegacyRecoveryFixture(target, "old-snapshot", crashed: false);
        RecoverySnapshotStore.Begin();
        Check(RecoverySnapshotStore.Pending.Length == 0 && Snapshots().Length == 0,
            "A clean legacy session must not create a fake crash recovery prompt.");
        Check(Directory.GetFiles(LegacyRecoveryRoot, "*.data").Length == 1,
            "Read-only legacy snapshots are never pruned by Rainwright.");
    }

    private static void TestCorruptLegacyDoesNotImport()
    {
        string target = Author("tampered-legacy.txt");
        WriteLegacyRecoveryFixture(target, "original", crashed: true, corruptPayload: true);
        RecoverySnapshotStore.Begin();
        Check(RecoverySnapshotStore.Pending.Length == 0 && Snapshots().Length == 0,
            "Checksum mismatch in legacy snapshot must prevent any import.");
        Check(File.Exists(Path.Combine(LegacyRecoveryRoot, "active.lock")),
            "Invalid legacy data must not be removed by import.");
    }

    private static void TestRoomCapture()
    {
        string path = Author("room_settings.txt");
        File.WriteAllText(path, "original-room-author");
        var settings = new RoomSettings
        {
            filePath = path,
            content = "unsaved-room-settings"
        };
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureRoom("ROOM-01", settings);
        Check(Snapshots().Length == 1, "RoomSettings serializer must produce a recovery data file.");
        Check(File.ReadAllText(path) == "original-room-author", "Room capture must not modify author file.");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1, "Room snapshot should survive an unexpected exit.");
        Check(RecoverySnapshotStore.Pending[0].Document == "ROOM-01", "Room identity must persist.");
    }

    private static void TestCleanShutdown()
    {
        string target = Author("B.txt");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("B", target, "unsaved");
        RecoverySnapshotStore.End();
        RecoverySnapshotStore.Begin();
        Check(RecoverySnapshotStore.Pending.Length == 0, "Clean close must not offer crash recovery.");
        Check(Snapshots().Length == 1, "Clean close should not erase useful snapshots prematurely.");
    }

    private static void TestCorruptData()
    {
        string target = Author("C.txt");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("C", target, "untampered");
        File.WriteAllText(Snapshots()[0], "corrupted");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 0, "Checksum mismatch must reject the snapshot.");
    }

    private static void TestBadManifest()
    {
        string target = Author("D.txt");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("D", target, "data");
        string meta = Directory.GetFiles(RecoveryRoot, "snapshot-*.xml")[0];
        XDocument doc = XDocument.Load(meta);
        doc.Root.SetAttributeValue("sha256", "invalid");
        doc.Save(meta);
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 0, "Invalid manifest must not be trusted.");
    }

    private static void TestPrunePerDocument()
    {
        string a = Author("A.txt");
        string b = Author("B.txt");
        RecoverySnapshotStore.Begin();
        for (int i = 0; i < 8; i++)
            RecoverySnapshotStore.CaptureText("A", a, "revision-" + i);
        RecoverySnapshotStore.CaptureText("B", b, "separate-document");
        Check(Snapshots().Length == 6, "Keep five per document, without touching another document.");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Count(c => c.Document == "A") == 5,
            "Only five newest A revisions should survive.");
        Check(RecoverySnapshotStore.Pending.Count(c => c.Document == "B") == 1,
            "Pruning A must retain B.");
    }

    private static void TestConfiguredRetention()
    {
        var preferences = new global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalPreferences { SnapshotCount = 2 };
        global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.Publish(preferences);
        try
        {
            string source = Author("Configured.txt");
            RecoverySnapshotStore.Begin();
            for (int i = 0; i < 6; i++) RecoverySnapshotStore.CaptureText("Configured", source, "revision-" + i);
            Check(Snapshots().Length == 2, "User retention value prunes actual recovery files.");
            preferences.SnapshotCount = 8;
            global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.Publish(preferences);
            for (int i = 6; i < 12; i++) RecoverySnapshotStore.CaptureText("Configured", source, "revision-" + i);
            Check(Snapshots().Length == 8, "Increasing retention keeps further recovery revisions.");
        }
        finally { global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.Publish(new()); }
    }

    private static void TestRestore()
    {
        string target = Author("restore.txt");
        File.WriteAllText(target, "original");
        File.WriteAllText(target + ".DryCycle-before-recovery.bak", "previous-DryCycle-backup");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("restore", target, "recover-this");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1, "A recoverable snapshot should be offered.");
        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "recover-this", "Explicit recovery must replace target.");
        Check(File.ReadAllText(target + ".Rainwright-before-recovery.bak") == "original",
            "Recovery must keep a byte-for-byte backup of the existing author file.");
        Check(File.ReadAllText(target + ".DryCycle-before-recovery.bak") ==
              "previous-DryCycle-backup",
            "Rainwright must not overwrite a previously created DryCycle recovery backup.");
        Check(RecoverySnapshotStore.RestoreRequiresReload,
            "Recovery must write-suspend the editor until reload.");
        Check(RecoverySnapshotStore.Pending.Length == 0, "Restored candidate must be cleared.");
        RecoverySnapshotStore.CaptureText("restore", target, "illegal-after-restore");
        Check(Snapshots().Length == 1, "No new snapshots while author writes are suspended.");
    }

    private static void TestPaletteDraftRecovery()
    {
        string target = Path.Combine(Paths.ConfigPath, "RainWright.Palette.xml");
        File.WriteAllText(target, "original-draft");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("Palette:workspace", target, "recovered-draft");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1, "Failed draft save must retain a recovery candidate.");
        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "recovered-draft" && RecoverySnapshotStore.RestoreRequiresReload,
            "The exact private palette draft can be restored without pretending it belongs to a Mod.");
    }

    private static void TestRestorePreservesPriorArtifacts()
    {
        string target = Author("restore-again.txt");
        File.WriteAllText(target, "live-author");
        string previousBackup = target + ".Rainwright-before-recovery.bak";
        string oldTemp = target + ".Rainwright-recovery.tmp";
        File.WriteAllText(previousBackup, "previous-recovery-backup");
        File.WriteAllText(oldTemp, "stale-but-owned-by-previous-operation");

        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("repeated-restore", target, "new-recovery");
        CrashRestart();
        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();

        Check(File.ReadAllText(target) == "new-recovery" &&
              File.ReadAllText(previousBackup) == "previous-recovery-backup" &&
              File.ReadAllText(oldTemp) == "stale-but-owned-by-previous-operation",
            "Restore cannot overwrite an earlier backup or a predictable stale staging file.");

        string[] backups = Directory.GetFiles(Path.GetDirectoryName(target),
            "*.Rainwright-before-recovery.bak");
        Check(backups.Length == 2 &&
              backups.Any(path => path != previousBackup &&
                  File.ReadAllText(path) == "live-author"),
            "A second recovery preserves the live author's pre-restore bytes in a distinct backup.");
        Check(Directory.GetFiles(Path.GetDirectoryName(target),
                "*.Rainwright-recovery.tmp").Length == 1,
            "Successful recovery removes only its own unique staging file.");
    }

    private static void TestRestoreExternalConflict()
    {
        string target = Author("changed-after-discovery.txt");
        File.WriteAllText(target, "original-author");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("conflict", target, "snapshot");
        CrashRestart();
        string candidate = RecoverySnapshotStore.Pending[0].Id;
        File.WriteAllText(target, "updated-by-external-tool");

        RecoverySnapshotStore.RequestRestore(candidate);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "updated-by-external-tool" &&
              RecoverySnapshotStore.Pending.Length == 1 &&
              !RecoverySnapshotStore.RestoreRequiresReload &&
              !File.Exists(target + ".Rainwright-before-recovery.bak"),
            "External author changes after listing cannot be overwritten or backed up as if unchanged.");
        Check(RecoverySnapshotStore.Status.Contains("changed after", StringComparison.OrdinalIgnoreCase),
            "Rejected external conflicts provide a useful visible reason.");
    }

    private static void TestRestoreSourceAppearance()
    {
        string target = Author("appeared-after-discovery.txt");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("appeared", target, "snapshot");
        CrashRestart();
        string candidate = RecoverySnapshotStore.Pending[0].Id;
        File.WriteAllText(target, "new-author-data");

        RecoverySnapshotStore.RequestRestore(candidate);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "new-author-data" &&
              RecoverySnapshotStore.Pending.Length == 1 &&
              !RecoverySnapshotStore.RestoreRequiresReload,
            "A newly created author file cannot be overwritten by an older missing-file snapshot.");
    }

    private static void TestGenericRestoreOwner()
    {
        string target = Author("author-file.txt");
        File.WriteAllText(target, "original");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("generic-author", target, "edited");
        CrashRestart();
        string id = RecoverySnapshotStore.Pending[0].Id;
        ModManager.ActiveMods.Clear();

        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "original" &&
              RecoverySnapshotStore.Pending.Length == 1 &&
              !RecoverySnapshotStore.RestoreRequiresReload,
            "A disabled author Mod cannot receive a stale crash snapshot.");

        string unrelatedMod = Path.Combine(baseDirectory, "foreign-mod");
        Directory.CreateDirectory(unrelatedMod);
        ModManager.ActiveMods.Add(new() { id = "foreign", path = unrelatedMod });
        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "original" &&
              RecoverySnapshotStore.Pending.Length == 1,
            "An active unrelated Mod does not authorize a former author's path.");

        ModManager.ActiveMods.Add(new() { id = "authoring-test", path = Path.GetDirectoryName(target) });
        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "edited" &&
              File.ReadAllText(target + ".Rainwright-before-recovery.bak") == "original" &&
              RecoverySnapshotStore.RestoreRequiresReload,
            "The current highest-priority author source permits explicit recovery.");
    }

    private static void TestSamePathDifferentModIdentity()
    {
        string target = Author("same-path-other-mod.txt");
        File.WriteAllText(target, "original-A");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("same-directory-owner", target, "snapshot-A");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1,
            "The original Mod creates a recoverable candidate.");
        string id = RecoverySnapshotStore.Pending[0].Id;

        // Simulate a Mod identity replacement without moving the author root.
        ModManager.ActiveMods[0].id = "different-mod";
        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "original-A" &&
              RecoverySnapshotStore.Pending.Length == 1 &&
              !RecoverySnapshotStore.RestoreRequiresReload &&
              RecoverySnapshotStore.Status.Contains("Mod identity changed",
                  StringComparison.OrdinalIgnoreCase),
            "A different active Mod at the same root cannot inherit an old snapshot.");

        ModManager.ActiveMods[0].id = "authoring-test";
        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "snapshot-A" &&
              RecoverySnapshotStore.RestoreRequiresReload,
            "Returning to the original active Mod identity permits deliberate restore.");
    }

    private static void TestOverlaidRecoverySource()
    {
        string target = Author("overlaid.txt");
        File.WriteAllText(target, "original-lower");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("overlay-test", target, "snapshot-lower");
        CrashRestart();
        string id = RecoverySnapshotStore.Pending[0].Id;

        string upper = Path.Combine(baseDirectory, "upper-mod");
        Directory.CreateDirectory(upper);
        string upperFile = Path.Combine(upper, "overlaid.txt");
        File.WriteAllText(upperFile, "higher-priority-author");
        ModManager.ActiveMods.Add(new() { id = "overlay", path = upper });

        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "original-lower" &&
              File.ReadAllText(upperFile) == "higher-priority-author" &&
              RecoverySnapshotStore.Pending.Length == 1,
            "A new high-priority Mod overlay refuses recovery to the hidden old file.");

        ModManager.ActiveMods.RemoveAt(ModManager.ActiveMods.Count - 1);
        RecoverySnapshotStore.RequestRestore(id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "snapshot-lower" &&
              File.ReadAllText(upperFile) == "higher-priority-author",
            "After removing the overlay, the original Mod may explicitly recover.");
    }

    private static void TestSymlinkRecoverySource()
    {
        string authorRoot = Path.Combine(baseDirectory, "author");
        Directory.CreateDirectory(authorRoot);
        string foreignRoot = Path.Combine(baseDirectory, "foreign-source");
        Directory.CreateDirectory(foreignRoot);
        string foreignTarget = Path.Combine(foreignRoot, "other-author.txt");
        File.WriteAllText(foreignTarget, "foreign-original");
        string linkRoot = Path.Combine(authorRoot, "redirect");
        Directory.CreateSymbolicLink(linkRoot, foreignRoot);
        string redirectedTarget = Path.Combine(linkRoot, "other-author.txt");

        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("linked-author", redirectedTarget, "redirected-snapshot");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1,
            "Linked author source is retained as a snapshot but must be reauthorized before restore.");
        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(foreignTarget) == "foreign-original" &&
              RecoverySnapshotStore.Pending.Length == 1 &&
              !RecoverySnapshotStore.RestoreRequiresReload,
            "A symlinked directory inside an active Mod cannot redirect recovery into another Mod.");

        // A file symlink must also be refused, even without a linked directory.
        string fileLink = Path.Combine(authorRoot, "linked-file.txt");
        File.CreateSymbolicLink(fileLink, foreignTarget);
        RecoverySnapshotStore.CaptureText("file-link", fileLink, "file-link-snapshot");
        CrashRestart();
        string candidate = RecoverySnapshotStore.Pending.First(c => c.Document == "file-link").Id;
        RecoverySnapshotStore.RequestRestore(candidate);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(foreignTarget) == "foreign-original" &&
              !RecoverySnapshotStore.RestoreRequiresReload,
            "A file symlink cannot become a verified author recovery destination.");
    }

    private static void TestVersionedRecoverySource()
    {
        string target = Author("versioned.txt");
        File.WriteAllText(target, "base-original");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("versioned", target, "base-snapshot");
        CrashRestart();
        string candidate = RecoverySnapshotStore.Pending[0].Id;

        string newestRoot = Path.Combine(baseDirectory, "newest-variant");
        string targetedRoot = Path.Combine(baseDirectory, "targeted-variant");
        Directory.CreateDirectory(newestRoot);
        Directory.CreateDirectory(targetedRoot);
        string newest = Path.Combine(newestRoot, "versioned.txt");
        string targeted = Path.Combine(targetedRoot, "versioned.txt");
        File.WriteAllText(newest, "newest-author");
        File.WriteAllText(targeted, "targeted-author");
        ModManager.ActiveMods[0].hasNewestFolder = true;
        ModManager.ActiveMods[0].NewestPath = newestRoot;
        ModManager.ActiveMods[0].hasTargetedVersionFolder = true;
        ModManager.ActiveMods[0].TargetedPath = targetedRoot;

        RecoverySnapshotStore.RequestRestore(candidate);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "base-original" &&
              File.ReadAllText(targeted) == "targeted-author" &&
              RecoverySnapshotStore.Pending.Length == 1,
            "A targeted version folder masks a former base author file.");

        File.Delete(targeted);
        RecoverySnapshotStore.RequestRestore(candidate);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "base-original" &&
              File.ReadAllText(newest) == "newest-author" &&
              RecoverySnapshotStore.Pending.Length == 1,
            "The newest version folder still masks the former base author file.");

        File.Delete(newest);
        RecoverySnapshotStore.RequestRestore(candidate);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "base-snapshot" &&
              RecoverySnapshotStore.RestoreRequiresReload,
            "When both variant sources are absent, the active base author file may be restored.");
    }

    private static void TestTopologyRestoreOwner()
    {
        string target = Author("WorldTopology.json");
        string other = Path.Combine(baseDirectory, "other-mod", "world", "WorldTopology.json");
        File.WriteAllText(target, "original-topology");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("region/WorldTopology", target, "edited-topology");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1,
            "A saved gameplay topology snapshot remains recoverable for inspection.");
        string snapshotId = RecoverySnapshotStore.Pending[0].Id;

        RecoverySnapshotStore.RequestRestore(snapshotId);
        RecoverySnapshotStore.ProcessRequests();
        Check(File.ReadAllText(target) == "original-topology" &&
              !RecoverySnapshotStore.RestoreRequiresReload &&
              RecoverySnapshotStore.Pending.Length == 1,
            "Rainwright-only operation cannot restore gameplay topology without a lease.");

        using (WorldTopologyPathOwnership.Register(_ => other))
        {
            RecoverySnapshotStore.RequestRestore(snapshotId);
            RecoverySnapshotStore.ProcessRequests();
            Check(File.ReadAllText(target) == "original-topology" &&
                  RecoverySnapshotStore.Pending.Length == 1 &&
                  !RecoverySnapshotStore.RestoreRequiresReload,
                "A lease for another Mod location cannot restore an old source.");
        }

        using (WorldTopologyPathOwnership.Register(_ => target))
        {
            RecoverySnapshotStore.RequestRestore(snapshotId);
            RecoverySnapshotStore.ProcessRequests();
            Check(File.ReadAllText(target) == "edited-topology" &&
                  File.ReadAllText(target + ".Rainwright-before-recovery.bak") == "original-topology" &&
                  RecoverySnapshotStore.Pending.Length == 0 &&
                  RecoverySnapshotStore.RestoreRequiresReload,
                "A matching current gameplay owner allows explicit recovery and keeps a backup.");
        }
    }

    private static void TestMergedCacheProtection()
    {
        string folder = Path.Combine(baseDirectory, "mergedmods", "world");
        Directory.CreateDirectory(folder);
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("merged", Path.Combine(folder, "world.txt"), "unsafe");
        Check(Snapshots().Length == 0, "Generated mergedmods path cannot be snapshotted for author recovery.");
    }

    private static void TestOversizedSnapshot()
    {
        string target = Author("huge.txt");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("huge", target, new string('x', 8 * 1024 * 1024 + 1));
        Check(Snapshots().Length == 0, "Oversized snapshot must not be published.");
        Check(RecoverySnapshotStore.Status.Contains("failed", StringComparison.OrdinalIgnoreCase),
            "Oversized snapshot rejection must be observable.");
    }

    private static void TestMissingAuthorFolder()
    {
        string target = Path.Combine(baseDirectory, "missing-directory", "recover.txt");
        RecoverySnapshotStore.Begin();
        RecoverySnapshotStore.CaptureText("missing", target, "author-data");
        CrashRestart();
        Check(RecoverySnapshotStore.Pending.Length == 1, "Candidate should be visible even if the folder moved.");
        RecoverySnapshotStore.RequestRestore(RecoverySnapshotStore.Pending[0].Id);
        RecoverySnapshotStore.ProcessRequests();
        Check(!File.Exists(target), "Recovery must not fabricate missing author folders.");
        Check(!RecoverySnapshotStore.RestoreRequiresReload, "Failed restore must not claim success.");
        Check(RecoverySnapshotStore.Status.Contains("Recovery failed", StringComparison.Ordinal),
            "Failed restore must provide diagnostics.");
    }

    private static void TestEditorDiagnostics()
    {
        DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
            "Recovery writer failed", new InvalidOperationException("diagnostic sentinel"));
        BepInEx.Logging.ManualLogSource source = BepInEx.Logging.Logger.LastCreated;
        Check(source != null && source.SourceName == "Rainwright.Editor",
            "Editor diagnostics should own a named BepInEx source independent of DryCycle.Plugin.");
        Check(source.LastError?.Contains("InvalidOperationException") == true &&
              source.LastError.Contains("diagnostic sentinel"),
            "Independent editor diagnostics must preserve the full exception.");
    }

    private static void TestEditorStartupDiagnostics()
    {
        var log = BepInEx.Logging.Logger.LastCreated;
        log.Entries.Clear();
        DryCycle.DevUI.DevTool.Core.EditorStartupDiagnostics.Step(
            "Test/Init", () => Check(true, "Editor bootstrap action reached."));
        Check(log.Entries.Count == 2 &&
              log.Entries[0].Contains("[BEGIN][Test/Init]") &&
              log.Entries[1].Contains("[OK][Test/Init]"),
            "Editor startup must log BEGIN and OK for a completed action.");

        InvalidOperationException original = new("startup sentinel");
        bool identityPreserved = false;
        try
        {
            DryCycle.DevUI.DevTool.Core.EditorStartupDiagnostics.Step(
                "Test/Hook", () => throw original);
        }
        catch (InvalidOperationException error)
        {
            identityPreserved = ReferenceEquals(error, original);
        }
        Check(identityPreserved, "Editor startup must rethrow the original exception.");
        Check(log.LastError?.Contains("[FAIL][Test/Hook]") == true &&
              log.LastError.Contains("InvalidOperationException") &&
              log.LastError.Contains("startup sentinel"),
            "Editor startup failure must include complete exception diagnostics.");
        DryCycle.DevUI.DevTool.Core.EditorStartupDiagnostics.Marker("Test/Phase", "DISABLED");
        Check(log.LastInfo?.Contains("[DISABLED][Test/Phase]") == true,
            "Editor-owned phase markers retain owner and state.");
    }

    private static void Check(bool ok, string message)
    {
        assertions++;
        if (!ok) throw new InvalidOperationException(message);
    }
}
