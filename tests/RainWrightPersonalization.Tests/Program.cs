using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using BepInEx;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.Personalization;
using DryCycle.DevUI.DevTool.RWImGui;
using UnityEngine;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        assertions++;
        try { action(); } catch (InvalidDataException) { return; } catch (XmlException) { return; }
        throw new Exception(message);
    }
    private static void Load()
    {
        DevToolUserSettingsStore.Load(null); EditorAutoSavePreferences.LoadOwned(Paths.ConfigPath);
        EditorShortcutGameplayPolicy.LoadOwned(Paths.ConfigPath);
    }
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "RainWright-presets-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); Paths.ConfigPath = root;
        try
        {
            Load(); RainWrightWorkspaceRuntime.Load();
            Check(DevToolUserSettingsStore.GetObjectFavourites().Length == 0, "New personal configuration has no object favourites.");
            long favouriteRevision = DevToolUserSettingsStore.ObjectFavouritesRevision;
            Check(DevToolUserSettingsStore.SetObjectFavourite("RegionKit.Object", true), "An object type can be added to favourites.");
            Check(DevToolUserSettingsStore.ObjectFavouritesRevision > favouriteRevision && File.ReadAllText(DevToolUserSettingsStore.SettingsPath).Contains("RegionKit.Object"), "Favourite changes publish a revision and reach disk immediately.");
            favouriteRevision = DevToolUserSettingsStore.ObjectFavouritesRevision;
            Check(!DevToolUserSettingsStore.SetObjectFavourite("RegionKit.Object", true) && DevToolUserSettingsStore.ObjectFavouritesRevision == favouriteRevision, "Repeated Ctrl-right-click outside Favourite cannot remove or duplicate an existing entry.");
            DevToolUserSettingsStore.SetObjectFavourite("DisabledMod.测试Object", true);
            Load();
            Check(DevToolUserSettingsStore.GetObjectFavourites().SequenceEqual(new[] { "DisabledMod.测试Object", "RegionKit.Object" }), "Restart preserves Unicode type IDs and favourites from disabled mods.");
            var emptyWorkspace = DevToolUserSettingsStore.CaptureWorkspace();
            DevToolUserSettingsStore.ApplyWorkspace(emptyWorkspace);
            Check(DevToolUserSettingsStore.GetObjectFavourites().Length == 2, "Applying window workspace presets must not clear personal object favourites.");
            Check(DevToolUserSettingsStore.SetObjectFavourite("RegionKit.Object", false), "Removing from Favourite succeeds.");
            Load();
            Check(!DevToolUserSettingsStore.GetObjectFavourites().Contains("RegionKit.Object") && !File.ReadAllText(DevToolUserSettingsStore.SettingsPath).Contains("RegionKit.Object"), "Removed favourites cannot reappear from preserved XML on restart.");
            Check(RainWrightWorkspaceRuntime.Presets.Length == 3, "Three built-in presets");
            Check(RainWrightWorkspaceRuntime.BuiltIn("World").Preferences.DefaultPage == "Map", "World preset opens map for new sessions");
            var prefs = new RainWrightPersonalPreferences { GridSnap = true, GridSize = 20, SnapshotCount = 11, ReverseZoom = true };
            prefs.Shortcuts["save"] = "shift+ctrl+k";
            RainWrightPersonalSettings.Publish(prefs);
            Check(RainWrightPersonalSettings.Chord("save") == "Ctrl+Shift+K", "Chord canonicalized");
            prefs.Shortcuts["save"] = "Ctrl+J";
            Check(RainWrightPersonalSettings.Chord("save") == "Ctrl+Shift+K", "Published snapshot is immutable");
            Check(RainWrightPersonalSettings.Snap(31) == 40 && RainWrightPersonalSettings.Snap(-31) == -40, "Grid snaps positive and negative map positions");
            Check(RainWrightPersonalSettings.SnapshotCount == 11, "Recovery retention published");
            Input.Held.UnionWith(new[] { KeyCode.LeftControl, KeyCode.LeftShift, KeyCode.K }); Input.Down.Add(KeyCode.K);
            Check(RainWrightShortcutInput.Pressed("save") && !RainWrightShortcutInput.Pressed("undo"), "Remapped shortcut executes with exact modifiers");
            Check(RainWrightShortcutInput.ReservesKey(KeyCode.K) && !RainWrightShortcutInput.ReservesKey(KeyCode.Z), "Remapped player action key is reserved");
            Input.Held.Add(KeyCode.LeftAlt); Check(!RainWrightShortcutInput.Pressed("save"), "Extra modifier does not execute shortcut");
            Input.Held.Clear(); Input.Down.Clear();
            Check(RainWrightPersonalSettings.Chord("top-status") == "" && !RainWrightShortcutInput.Pressed("top-status"), "Top status shortcut starts unbound and never parses or fires an empty key.");
            Check(RainWrightPersonalPreferences.NormalizeChord("  ") == "", "Blank shortcut means unbound.");
            var unbound = RainWrightPersonalSettings.Capture();
            unbound.Shortcuts["undo"] = "";
            Check(RainWrightPersonalPreferences.FromXml(unbound.ToXml()).Shortcuts["undo"] == "", "Multiple unbound shortcuts survive persistence without false conflicts.");
            var oldPreferences = RainWrightPersonalSettings.Capture().ToXml();
            oldPreferences.Elements("Shortcut").Single(x => (string)x.Attribute("id") == "top-status").Remove();
            Check(RainWrightPersonalPreferences.FromXml(oldPreferences).Shortcuts["top-status"] == "", "Older presets gain an unbound top-status action.");
            var topBinding = RainWrightPersonalSettings.Capture(); topBinding.Shortcuts["top-status"] = "F8";
            RainWrightPersonalSettings.Publish(topBinding); Input.Held.Add(KeyCode.F8); Input.Down.Add(KeyCode.F8);
            Check(RainWrightShortcutInput.Pressed("top-status") && RainWrightShortcutInput.HasHeldViewChord(), "A user-bound top-status shortcut executes and claims its view key.");
            topBinding.Shortcuts["top-status"] = ""; RainWrightPersonalSettings.Publish(topBinding);
            Check(!RainWrightShortcutInput.Pressed("top-status") && !RainWrightShortcutInput.HasHeldViewChord(), "Clearing a previously cached binding disables it immediately.");
            Input.Held.Clear(); Input.Down.Clear();
            foreach (string chord in new[] { "Ctrl+Ctrl+K", "Ctrl+O", "H", "Escape", "Ctrl+F13", "Ctrl+mouse" })
                Reject(() => RainWrightPersonalPreferences.NormalizeChord(chord), "Invalid chord must fail: " + chord);
            Check(RainWrightPersonalPreferences.NormalizeChord("f01") == "F1", "Function key aliases canonicalize");
            var conflict = RainWrightPersonalSettings.Capture(); conflict.Shortcuts["undo"] = conflict.Shortcuts["save"];
            Reject(() => RainWrightPersonalSettings.Publish(conflict), "Conflicts cannot publish");
            Check(RainWrightPersonalSettings.Chord("undo") == "Ctrl+Z", "Rejected binding preserves live state");
            var invalid = RainWrightPersonalSettings.Capture(); invalid.DragSensitivity = float.NaN;
            Reject(() => RainWrightPersonalSettings.Publish(invalid), "NaN preferences fail");
            DevToolUserSettingsStore.RememberWindow("Browser", new Vector2(55, 90), new Vector2(400, 300));
            var original = RainWrightWorkspaceRuntime.Capture("测试布局", "开发者", "中文与 English");
            original.Preferences.RemixGlassOpacity = .7f; original.Preferences.RemixReflection = .25f;
            original.Preferences.RemixSectionGap = 32; original.Preferences.RemixGildedTitle = false;
            original.Preferences.RemixSectionHeadings = false;
            string saved = RainWrightWorkspaceRuntime.SavePreset(original, false);
            Check(File.Exists(saved) && RainWrightWorkspaceRuntime.Presets.Length == 4, "Save current refreshes local list");
            var roundtrip = RainWrightWorkspacePreset.FromXml(RainWrightPersonalSettings.ReadXml(saved));
            Check(roundtrip.Name == "测试布局" && roundtrip.Author == "开发者" && roundtrip.Description.Contains("English"), "Metadata survives Unicode roundtrip");
            Check(roundtrip.Ui.Element("Windows").Element("Window").Attribute("x").Value == "55", "Window layout included");
            Check(roundtrip.Preferences.RemixGlassOpacity == .7f && roundtrip.Preferences.RemixReflection == .25f
                && roundtrip.Preferences.RemixSectionGap == 32 && !roundtrip.Preferences.RemixGildedTitle
                && !roundtrip.Preferences.RemixSectionHeadings, "All five Remix appearance options survive preset sharing");
            var legacy = original.Preferences.ToXml();
            foreach (string attribute in new[] { "remixGlassOpacity", "remixReflection", "remixSectionGap", "remixGildedTitle", "remixSectionHeadings" })
                legacy.Attribute(attribute).Remove();
            var legacyPreferences = RainWrightPersonalPreferences.FromXml(legacy);
            Check(legacyPreferences.RemixGlassOpacity == .44f && legacyPreferences.RemixReflection == 1f
                && legacyPreferences.RemixSectionGap == 20 && legacyPreferences.RemixGildedTitle && legacyPreferences.RemixSectionHeadings,
                "Old presets without new appearance fields load with compatible defaults");
            foreach (string attribute in new[] { "remixGlassOpacity", "remixReflection", "remixSectionGap", "remixGildedTitle" })
            {
                var invalidAppearance = original.Preferences.ToXml(); invalidAppearance.SetAttributeValue(attribute, "invalid");
                Reject(() => RainWrightPersonalPreferences.FromXml(invalidAppearance), "Malformed appearance data is rejected: " + attribute);
            }
            string shared = RainWrightWorkspaceRuntime.SavePreset(original, true);
            string sharedBytes = File.ReadAllText(shared);
            string imported = RainWrightWorkspaceRuntime.Import(shared);
            Check(imported != shared && File.ReadAllText(shared) == sharedBytes, "Import preserves shared source and creates local copy");
            Check(RainWrightPersonalSettings.WindowAlpha == original.Preferences.WindowAlpha, "Import does not apply");
            Check(RainWrightPersonalSettings.RemixGlassOpacity == .44f && RainWrightPersonalSettings.RemixGildedTitle,
                "Importing appearance settings never changes the active theme implicitly");
            Reject(() => RainWrightWorkspaceRuntime.ReadPreset("../escape.rainwright-preset.xml"), "Selection cannot traverse directories");
            var malicious = original.ToXml(); malicious.Add(new XElement("WorldTopology", "author data"));
            Reject(() => RainWrightWorkspacePreset.FromXml(malicious), "Author documents cannot enter preset");
            malicious = original.ToXml(); malicious.Element("RainwrightUi").Element("Presentation").SetAttributeValue("englishFontSize", "NaN");
            Reject(() => RainWrightWorkspacePreset.FromXml(malicious), "Invalid imported presentation rejected");
            malicious = original.ToXml(); malicious.Element("RainwrightUi").Element("Windows").Element("Window").SetAttributeValue("source", "C:/secret");
            Reject(() => RainWrightWorkspacePreset.FromXml(malicious), "Unknown author path metadata rejected");
            string hostile = Path.Combine(root, "dtd.xml"); File.WriteAllText(hostile, "<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///invalid'>]><x>&y;</x>");
            Reject(() => RainWrightPersonalSettings.ReadXml(hostile), "DTD is disabled");
            string authorDocument = Path.Combine(root, "room-author.txt"); File.WriteAllText(authorDocument, "unchanged authored bytes");
            var changed = RainWrightWorkspaceRuntime.Capture("Applied workspace"); changed.AutoSaveSeconds = 60; changed.Preferences.WindowAlpha = .8f;
            changed.Preferences.RemixGlassOpacity = .65f; changed.Preferences.RemixReflection = .3f;
            changed.Preferences.RemixSectionGap = 28; changed.Preferences.RemixGildedTitle = false; changed.Preferences.RemixSectionHeadings = false;
            changed.Ui.Element("Presentation").SetAttributeValue("language", "Chinese"); changed.Preferences.Shortcuts["save"] = "Alt+K";
            RainWrightWorkspaceRuntime.Apply(changed);
            Check(RainWrightPersonalSettings.WindowAlpha == .8f && DevToolUiSettings.Language == DevToolUiLanguage.Chinese && EditorAutoSavePreferences.IntervalSeconds == 60, "Apply updates actual preference owners");
            var appliedAppearance = RainWrightPersonalPreferences.FromXml(RainWrightPersonalSettings.ReadXml(RainWrightWorkspaceRuntime.SettingsPath).Element("Preferences"));
            Check(appliedAppearance.RemixGlassOpacity == .65f && appliedAppearance.RemixReflection == .3f
                && appliedAppearance.RemixSectionGap == 28 && !appliedAppearance.RemixGildedTitle && !appliedAppearance.RemixSectionHeadings,
                "Applying appearance updates the shared persisted preference owner");
            RainWrightWorkspaceRuntime.PumpFrontend();
            int previousRebuilds = DevToolFrontend.Rebuilds;
            var appearanceOnly = RainWrightWorkspaceRuntime.Capture("Remix reflection");
            appearanceOnly.Preferences.RemixReflection = .4f;
            RainWrightWorkspaceRuntime.Apply(appearanceOnly);
            RainWrightWorkspaceRuntime.PumpFrontend();
            Check(DevToolFrontend.Rebuilds == previousRebuilds && RainWrightPersonalSettings.RemixReflection == .4f,
                "Changing only Remix materials does not rebuild the in-game editor");
            Check(DevToolShortcutRegistry.GetCommon().Single(x => x.Id == "save").Input == "Alt+K", "Shortcut help follows active bindings");
            EditorShortcutFeedback.Publish(EditorShortcutFeedbackKind.Save, true);
            Check(EditorShortcutFeedback.Current.Keys == "Alt+K", "Shortcut feedback uses active binding");
            Check(File.ReadAllText(authorDocument) == "unchanged authored bytes", "Applying preferences leaves author document untouched");
            string[] paths = { RainWrightWorkspaceRuntime.SettingsPath, DevToolUserSettingsStore.SettingsPath,
                RainwrightAutoSaveSettingsStore.PathForRoot(root), RainwrightShortcutInputSettingsStore.PathForRoot(root) };
            var before = paths.ToDictionary(x => x, File.ReadAllText);
            var rejected = RainWrightWorkspaceRuntime.Capture("Failed apply"); rejected.AutoSaveSeconds = 240; rejected.Preferences.WindowAlpha = .3f;
            rejected.Ui.Element("Presentation").SetAttributeValue("language", "English");
            // Fail the LAST write after three actual owners have persisted their changes.
            if (File.Exists(paths[0] + ".bak")) File.Delete(paths[0] + ".bak"); // This test's private temporary backup.
            Directory.CreateDirectory(paths[0] + ".bak");
            {
                bool failed = false;
                try { RainWrightWorkspaceRuntime.Apply(rejected); } catch (IOException error) { failed = error.InnerException != null; }
                Check(failed, "Apply must report original write failure");
            }
            Check(paths.All(x => File.ReadAllText(x) == before[x]), "Failed apply restores exact bytes across all preference owners");
            Check(RainWrightPersonalSettings.WindowAlpha == .8f && DevToolUiSettings.Language == DevToolUiLanguage.Chinese && EditorAutoSavePreferences.IntervalSeconds == 60, "Rollback restores active in-memory preferences");
            Check(RainWrightPersonalSettings.RemixGlassOpacity == .65f && RainWrightPersonalSettings.RemixReflection == .4f
                && !RainWrightPersonalSettings.RemixGildedTitle, "Rollback restores the active appearance as well as editor preferences");
            Check(Directory.GetFiles(root, "*.pending-*.tmp").Length > 0, "Failed staged writes remain available for recovery");
            var noLayout = RainWrightPersonalSettings.Capture(); noLayout.RememberLayout = false; RainWrightPersonalSettings.Publish(noLayout);
            Check(!DevToolUserSettingsStore.TryGetWindow("Browser", out _), "Remember layout toggle bypasses restored geometry");
            DevToolUserSettingsStore.RememberWindow("Browser", new Vector2(999, 999), new Vector2(400, 300));
            noLayout.RememberLayout = true; RainWrightPersonalSettings.Publish(noLayout);
            Check(DevToolUserSettingsStore.TryGetWindow("Browser", out var geometry) && geometry.Position.X == 55, "Disabling layout recording preserves previous preference");
            Console.WriteLine($"PASS: {assertions} personalization, preset sharing, input and rollback assertions."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally
        {
            RainWrightPersonalSettings.Publish(new RainWrightPersonalPreferences());
            // Exact test-owned absolute directory; never an author or installed mod path.
            Directory.Delete(root, true);
        }
    }
}
