using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using BepInEx;
using DryCycle.DevUI.DevTool.AutoSave;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.RWImGui;

namespace DryCycle.DevUI.DevTool.Personalization;

internal static class RainWrightWorkspaceRuntime
{
    internal static string SettingsPath => Path.Combine(Paths.ConfigPath, "RainWright.Personalization.xml");
    internal static string PresetDirectory => Path.Combine(Paths.ConfigPath, "RainWright", "Presets");
    internal static string ExportDirectory => Path.Combine(Paths.ConfigPath, "RainWright", "SharedPresets");
    private static readonly ConcurrentQueue<Action> pending = new();
    private static int frontendReload;
    private static string status = "";
    internal static string Status => Volatile.Read(ref status);
    private static bool loaded;
    private static KeyValuePair<string, string>[] presets = Array.Empty<KeyValuePair<string, string>>();
    internal static KeyValuePair<string, string>[] Presets => Volatile.Read(ref presets);
    internal static void RefreshPresetList() => Volatile.Write(ref presets, ListPresets().ToArray());
    internal static void Load()
    {
        if (loaded) return;
        loaded = true;
        RefreshPresetList();
        if (!File.Exists(SettingsPath)) return;
        try
        {
            XElement root = RainWrightPersonalSettings.ReadXml(SettingsPath);
            if (root.Name != "RainWrightPersonalization" || (string)root.Attribute("version") != "1") throw new InvalidDataException("Unsupported personal settings version.");
            RainWrightPersonalSettings.Publish(RainWrightPersonalPreferences.FromXml(root.Element("Preferences")));
            UpdateShortcutCatalog();
        }
        catch (Exception error) { Report(error); }
    }
    internal static RainWrightWorkspacePreset Capture(string name = "My workspace", string author = "", string description = "") =>
        new() { Name = name, Author = author, Description = description, Preferences = RainWrightPersonalSettings.Capture(),
            Ui = DevToolUserSettingsStore.CaptureWorkspace(), AutoSave = EditorAutoSavePreferences.Enabled,
            AutoSaveSeconds = EditorAutoSavePreferences.IntervalSeconds,
            AllowZ = EditorShortcutGameplayPolicy.AllowZ, AllowS = EditorShortcutGameplayPolicy.AllowS, AllowY = EditorShortcutGameplayPolicy.AllowY };
    internal static RainWrightWorkspacePreset BuiltIn(string id)
    {
        var preset = new RainWrightWorkspacePreset { Name = id == "World" ? "世界连线 / World" : id == "Sound" ? "声音调试 / Sound" : "房间制作 / Rooms",
            Description = "RainWright built-in workspace preset." };
        preset.Preferences.DefaultPage = id == "World" ? "Map" : id == "Sound" ? "Sound" : "Objects";
        preset.Preferences.GridSnap = id == "World";
        preset.AutoSaveSeconds = id == "World" ? 90 : 120;
        preset.Ui.Add(new XElement("Presentation", new XAttribute("language", DevToolUiSettings.Language),
            new XAttribute("toolPlacement", id == "Room" ? "LeftSidebar" : "TopDocked"),
            new XAttribute("scenePlacement", id == "Room" ? "Left" : "Center"),
            new XAttribute("chineseFontSize", DevToolUiSettings.DefaultChineseFontSize),
            new XAttribute("englishFontSize", DevToolUiSettings.DefaultFontSize), new XAttribute("englishFontWeight", 400),
            new XAttribute("textR", .94f), new XAttribute("textG", .94f), new XAttribute("textB", .94f), new XAttribute("textA", 1f),
            new XAttribute("mutedR", .76f), new XAttribute("mutedG", .80f), new XAttribute("mutedB", .86f), new XAttribute("mutedA", 1f)));
        preset.Ui.Add(new XElement("Windows"), new XElement("Groups"));
        return preset;
    }
    internal static void Apply(RainWrightWorkspacePreset proposed)
    {
        var preset = RainWrightWorkspacePreset.FromXml(proposed.ToXml()); // Validate the whole proposal before IO.
        if (EditorAutoSavePreferences.OwnedWritePending || EditorShortcutGameplayPolicy.OwnedWritePending)
            throw new InvalidOperationException("A preference write is pending; retry applying the preset after it completes.");
        string[] paths = { SettingsPath, DevToolUserSettingsStore.SettingsPath,
            RainwrightAutoSaveSettingsStore.PathForRoot(Paths.ConfigPath), RainwrightShortcutInputSettingsStore.PathForRoot(Paths.ConfigPath) };
        var originals = paths.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllBytes(path) : null);
        var previous = Capture();
        // Remix material changes are consumed by its existing sprites each frame.
        // Rebuilding the in-game editor is necessary only for a changed workspace.
        bool reloadFrontend = !XNode.DeepEquals(previous.Ui, preset.Ui);
        var written = new List<string>();
        try
        {
            // Every preference file uses its normal owner. No editor document, selection or history is reset.
            written.Add(paths[2]);
            if (!EditorAutoSavePreferences.TrySaveOwned(preset.AutoSave, preset.AutoSaveSeconds, out string error)) throw new IOException(error);
            written.Add(paths[3]);
            if (!RainwrightShortcutInputSettingsStore.TrySave(preset.AllowZ, preset.AllowS, preset.AllowY, out error)) throw new IOException(error);
            written.Add(paths[1]);
            DevToolUserSettingsStore.ApplyWorkspace(preset.Ui);
            written.Add(paths[0]);
            RainWrightPersonalSettings.WriteXml(SettingsPath, new XElement("RainWrightPersonalization", new XAttribute("version", 1), preset.Preferences.ToXml()));
            RainWrightPersonalSettings.Publish(preset.Preferences);
            UpdateShortcutCatalog();
            if (reloadFrontend) Interlocked.Exchange(ref frontendReload, 1);
            Volatile.Write(ref status, "已应用 / Applied: " + preset.Name);
        }
        catch (Exception original)
        {
            foreach (string path in written.AsEnumerable().Reverse())
                try
                {
                    if (originals[path] == null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        string staged = path + ".rollback-" + Guid.NewGuid().ToString("N");
                        File.WriteAllBytes(staged, originals[path]);
                        if (File.Exists(path)) File.Replace(staged, path, null); else File.Move(staged, path);
                    }
                    EditorDiagnostics.Info("Workspace preference rollback restored: " + path);
                }
                catch (Exception recovery) { EditorDiagnostics.Error("Workspace rollback remains incomplete: " + path, recovery); }
            EditorAutoSavePreferences.LoadOwned(Paths.ConfigPath);
            EditorShortcutGameplayPolicy.LoadOwned(Paths.ConfigPath);
            DevToolUserSettingsStore.Load(EditorDiagnostics.LogSource);
            DevToolUserSettingsStore.RestoreWorkspaceMemory(previous.Ui);
            RainWrightPersonalSettings.Publish(previous.Preferences);
            UpdateShortcutCatalog();
            if (reloadFrontend) Interlocked.Exchange(ref frontendReload, 1);
            throw new IOException("Workspace settings were not applied; preference rollback attempted.", original);
        }
    }
    internal static void ResetLayout()
    {
        var preset = Capture("Reset layout");
        preset.Ui.Element("Windows")?.Remove(); preset.Ui.Element("Groups")?.Remove();
        preset.Ui.Add(new XElement("Windows"), new XElement("Groups"));
        Apply(preset);
    }
    internal static string SavePreset(RainWrightWorkspacePreset preset, bool share)
    {
        preset = RainWrightWorkspacePreset.FromXml(preset.ToXml());
        string directory = share ? ExportDirectory : PresetDirectory;
        string readable = new string(preset.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        string path = Path.Combine(directory, readable.TrimEnd('.', ' ') + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".rainwright-preset.xml");
        RainWrightPersonalSettings.WriteXml(path, preset.ToXml());
        if (!share) RefreshPresetList();
        Volatile.Write(ref status, (share ? "已导出 / Exported: " : "已保存 / Saved: ") + path);
        return path;
    }
    internal static string Import(string path)
    {
        var preset = RainWrightWorkspacePreset.FromXml(RainWrightPersonalSettings.ReadXml(Path.GetFullPath(path)));
        return SavePreset(preset, false); // Import adds a local copy; it never applies it implicitly.
    }
    internal static RainWrightWorkspacePreset ReadPreset(string id)
    {
        if (new[] { "Room", "World", "Sound" }.Contains(id)) return BuiltIn(id);
        if (id != Path.GetFileName(id) || !id.EndsWith(".rainwright-preset.xml", StringComparison.Ordinal)) throw new InvalidDataException("Invalid preset selection.");
        return RainWrightWorkspacePreset.FromXml(RainWrightPersonalSettings.ReadXml(Path.Combine(PresetDirectory, id)));
    }
    internal static List<KeyValuePair<string, string>> ListPresets()
    {
        var list = new List<KeyValuePair<string, string>>
        { new("Room", "房间制作 / Rooms"), new("World", "世界连线 / World"), new("Sound", "声音调试 / Sound") };
        if (!Directory.Exists(PresetDirectory)) return list;
        foreach (string path in Directory.GetFiles(PresetDirectory, "*.rainwright-preset.xml").OrderBy(x => x))
            try { var preset = RainWrightWorkspacePreset.FromXml(RainWrightPersonalSettings.ReadXml(path)); list.Add(new(Path.GetFileName(path), preset.Name)); }
            catch (Exception error) { EditorDiagnostics.Warning("Invalid local preset: " + path, error); }
        return list;
    }
    internal static void OpenDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
    }
    internal static void Queue(Action action) => pending.Enqueue(action);
    internal static void Pump()
    {
        if (pending.TryDequeue(out Action action)) Run(action);
    }
    internal static void Run(Action action)
    {
        try { action(); }
        catch (Exception error) { Report(error); }
    }
    private static void Report(Exception error)
    {
        Volatile.Write(ref status, "失败 / Failed: " + error.Message);
        EditorDiagnostics.Error("RainWright personalization operation failed.", error);
    }
    internal static void PumpFrontend()
    {
        if (Interlocked.Exchange(ref frontendReload, 0) == 0) return;
        FloatingWindowSnap.ReloadPersistedLayout();
        DevToolOverlay.ReloadPersistedPresentationState();
        DevToolFrontend.RequestContextRebuildForLanguageChange();
    }
    private static void UpdateShortcutCatalog()
    {
        foreach (var descriptor in DevToolShortcutRegistry.GetCommon())
            if (RainWrightPersonalPreferences.Defaults().ContainsKey(descriptor.Id))
                DevToolShortcutRegistry.RegisterCommon(new DevToolShortcutDescriptor(descriptor.Id,
                    RainWrightPersonalSettings.Chord(descriptor.Id), descriptor.ChineseDescription, descriptor.EnglishDescription, descriptor.Order));
        foreach (var descriptor in DevToolShortcutRegistry.GetMode(EditorToolMode.Objects))
            if (descriptor.Id == "objects-duplicate") DevToolShortcutRegistry.RegisterMode(EditorToolMode.Objects,
                new DevToolShortcutDescriptor(descriptor.Id, RainWrightPersonalSettings.Chord(descriptor.Id), descriptor.ChineseDescription, descriptor.EnglishDescription, descriptor.Order));
    }
}
