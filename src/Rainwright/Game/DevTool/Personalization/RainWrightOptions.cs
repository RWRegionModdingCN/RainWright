using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using BepInEx;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.RWImGui;
using Menu.Remix.MixedUI;
using UnityEngine;
using RainWright.Remix;

namespace DryCycle.DevUI.DevTool.Personalization;

// Remix is a draft editor for the existing preference owners, never a second source of truth.
internal sealed class RainWrightOptions : OptionInterface
{
    private static RainWrightOptions instance;
    private readonly Dictionary<string, Configurable<string>> shortcuts = new();
    private readonly Configurable<string> language, placement, scene, page, presetId, name, author, description, importPath;
    private readonly Configurable<float> chineseSize, englishSize, alpha, sensitivity;
    private readonly Configurable<int> weight, gridSize, interval, snapshots;
    private readonly Configurable<bool> remember, reverse, snap, autoSave, recovery, allowZ, allowS, allowY;
    private readonly Configurable<Color> textColor, mutedColor;
    private readonly Configurable<float> remixOpacity, remixReflection;
    private readonly Configurable<int> remixGap;
    private readonly Configurable<bool> remixGold, remixHeadings;
    private OpComboBox presetBox;
    private OpLabel statusLabel;
    private readonly List<OpLabel> statusLines = new();
    private OpTextBox nameBox, authorBox, descriptionBox, importBox;
    private bool pulling, activated;

    internal RainWrightOptions()
    {
        language = Bind("Language", "English"); placement = Bind("Toolbar", "TopDocked"); scene = Bind("Scene", "Center");
        page = Bind("DefaultPage", "Room"); chineseSize = BindRange("ChineseFontSize", 21f, 12f, 72f); englishSize = BindRange("EnglishFontSize", 18f, 12f, 72f);
        weight = BindRange("EnglishFontWeight", 400, 100, 900); alpha = BindRange("WindowAlpha", .52f, .15f, 1f);
        textColor = Bind("TextColor", new Color(.94f, .94f, .94f, 1)); mutedColor = Bind("MutedColor", new Color(.76f, .80f, .86f, 1));
        remember = Bind("RememberLayout", true); reverse = Bind("ReverseZoom", false); sensitivity = BindRange("PanSensitivity", 1f, .25f, 3f);
        snap = Bind("GridSnap", false); gridSize = BindRange("GridSize", 20, 1, 200); autoSave = Bind("AutoSave", true);
        interval = BindRange("AutoSaveSeconds", 150, 30, 1800); snapshots = BindRange("SnapshotCount", 5, 1, 30); recovery = Bind("RecoveryPrompt", true);
        allowZ = Bind("AllowZ", false); allowS = Bind("AllowS", false); allowY = Bind("AllowY", false);
        presetId = Bind("SelectedPreset", "Room"); name = Bind("PresetName", "My workspace"); author = Bind("PresetAuthor", "");
        description = Bind("PresetDescription", ""); importPath = Bind("ImportPath", "");
        remixOpacity = BindRange("RemixGlassOpacity", .44f, .15f, .85f);
        remixReflection = BindRange("RemixReflection", 1f, 0f, 1f);
        remixGap = BindRange("RemixSectionGap", 20, 8, 40);
        remixGold = Bind("RemixGildedTitle", true); remixHeadings = Bind("RemixSectionHeadings", true);
        // Shortcut IDs are also used by presets and the editor registry. Keep those
        // IDs intact; only Remix's config keys require underscore separators.
        foreach (var item in RainWrightPersonalPreferences.Defaults()) shortcuts[item.Key] = Bind("Key_" + item.Key.Replace('-', '_'), item.Value);
        OnActivate += () => { activated = true; Pull(); RefreshPresets(); };
        OnDeactivate += () => { activated = false; RemixSettingsNavigation.Restore(this); };
        OnUnload += () => RemixSettingsNavigation.Restore(this);
        OnConfigChanged += () =>
        {
            if (pulling || !activated) return;
            RainWrightWorkspaceRuntime.Run(() => RainWrightWorkspaceRuntime.Apply(Draft()));
            Pull();
        };
    }
    private Configurable<T> Bind<T>(string key, T value) => config.Bind(key, value);
    // OpUpdown computes Abs(min) during its constructor. Its unbounded int
    // default is int.MinValue, which overflows before a range can be set on
    // the control itself. Supply the domain range on Configurable first.
    private Configurable<T> BindRange<T>(string key, T value, T min, T max) where T : IComparable =>
        config.Bind(key, value, new ConfigAcceptableRange<T>(min, max));
    internal static void Register()
    {
        if (MachineConnector.GetRegisteredOI("rainwright") is RainWrightOptions existing) { instance = existing; return; }
        instance ??= new RainWrightOptions();
        if (!MachineConnector.SetRegisteredOI("rainwright", instance)) throw new InvalidOperationException("RainWright Remix registration failed.");
        EditorDiagnostics.Info("RainWright Remix options registered successfully.");
    }
    internal static void RefreshStatus()
    {
        if (instance?.statusLabel != null) instance.statusLabel.text = RainWrightWorkspaceRuntime.Status;
        string brief = RainWrightWorkspaceRuntime.Status;
        if (brief.Length > 90) brief = brief.Substring(0, 87) + "…";
        if (instance != null) foreach (var label in instance.statusLines) label.text = brief;
    }
    public override void Initialize()
    {
        base.Initialize();
        statusLines.Clear();
        Tabs = new[] { new OpTab(this, TabName("ui")), new OpTab(this, TabName("edit")), new OpTab(this, TabName("keys")),
            new OpTab(this, TabName("save")), new OpTab(this, TabName("presets")), new OpTab(this, TabName("style")) };
        foreach (var tab in Tabs)
        {
            tab.colorButton = RemixDetailsChrome.Accent;
            tab.AddItems(new RemixDetailsChrome(), new RemixSettingsNavigation(this));
        }
        Title(0, "RainWright — 界面个性化 / Interface");
        Combo(0, 500, "语言 / Language", language, new[] { "Chinese", "English" });
        Number(0, 450, "中文字号 / CJK size (12–72)", chineseSize);
        Number(0, 400, "英文字号 / Latin size (12–72)", englishSize);
        Integer(0, 350, "英文字重 / Weight (100–900)", weight);
        Number(0, 300, "窗口透明度 / Opacity (0.15–1)", alpha);
        Label(0, 235, "正文颜色 / Text"); Label(0, 235, "次要文字 / Muted", 320);
        Tabs[0].AddItems(new OpColorPicker(textColor, new Vector2(30, 75)), new OpColorPicker(mutedColor, new Vector2(320, 75)));

        Title(1, "工作区与地图操作 / Workspace and map controls");
        Combo(1, 500, "工具栏位置 / Toolbar", placement, Enum.GetNames(typeof(DevToolActivityPlacement)));
        Combo(1, 450, "场景位置 / Scene", scene, new[] { "Left", "Center" });
        Check(1, 405, "记住窗口布局 / Remember layout", remember);
        Combo(1, 355, "首次打开页面 / Startup page", page, new[] { "Room", "Objects", "Sound", "Triggers", "Map", "Dialog", "Relationships" });
        Check(1, 310, "反转地图滚轮 / Reverse map zoom", reverse);
        Number(1, 260, "地图平移灵敏度 / Pan (0.25–3)", sensitivity);
        Check(1, 215, "世界/玩家地图房间网格吸附 / Snap room positions", snap);
        Integer(1, 165, "地图网格间距 / Grid (1–200)", gridSize);
        Button(1, 80, "重置窗口布局 / Reset layout", () => { RainWrightWorkspaceRuntime.ResetLayout(); Pull(); });
        Label(1, 40, "首次页面在新编辑会话生效。预设切换保留未保存文档。", 30);

        Title(2, "自定义快捷键 / Custom shortcuts");
        string[] labels = { "保存 / Save", "撤销 / Undo", "重做 / Redo", "重做备选 / Redo alternate", "删除 / Delete",
            "聚焦 / Focus", "浏览器 / Browser", "检查器 / Inspector", "新旧界面 / UI mode", "复制物件 / Duplicate", "窗口成组 / Group", "顶部状态栏 / Top status" };
        int row = 0;
        foreach (var item in shortcuts)
        {
            float y = 503 - row * 36;
            Label(2, y, labels[row++]); Tabs[2].AddItems(new OpTextBox(item.Value, new Vector2(325, y), 240));
        }
        Label(2, 65, "格式：Ctrl+Shift+K、F1、Tab；留空不绑定。拒绝重复按键。", 30);
        Label(2, 30, "O / H 保留给游戏；Esc 保持游戏原有行为。", 30);

        Title(3, "自动保存与恢复 / Auto-save and recovery");
        Check(3, 500, "启用自动保存 / Auto-save", autoSave);
        Integer(3, 450, "保存间隔（秒）/ Interval (30–1800)", interval);
        Integer(3, 400, "每份文档保留快照 / Snapshots (1–30)", snapshots);
        Label(3, 355, "顶部栏点击“恢复”展开 / Open Recovery from the top bar", 30);
        Check(3, 305, "Ctrl+Z 同时允许玩家 Z 输入 / Allow gameplay Z", allowZ);
        Check(3, 260, "Ctrl+S 同时允许玩家 S 输入 / Allow gameplay S", allowS);
        Check(3, 215, "Ctrl+Y 同时允许玩家 Y 输入 / Allow gameplay Y", allowY);
        Button(3, 130, "打开配置目录 / Open config", () => RainWrightWorkspaceRuntime.OpenDirectory(Paths.ConfigPath));
        Button(3, 75, "打开恢复目录 / Open recovery", () => RainWrightWorkspaceRuntime.OpenDirectory(System.IO.Path.Combine(Paths.ConfigPath, "Rainwright", "AutoSave")));

        Title(4, "工作预设与分享 / Workspace presets and sharing");
        presetBox = new OpComboBox(presetId, new Vector2(30, 470), 535, PresetItems()); Tabs[4].AddItems(presetBox);
        Button(4, 420, "应用选中预设 / Apply selected", () => { RainWrightWorkspaceRuntime.Apply(RainWrightWorkspaceRuntime.ReadPreset(presetBox.value)); Pull(); });
        nameBox = Text(4, 365, "名称 / Name", name); authorBox = Text(4, 325, "作者 / Author", author); descriptionBox = Text(4, 285, "说明 / Description", description);
        Button(4, 235, "保存当前设置 / Save current", () => { RainWrightWorkspaceRuntime.SavePreset(NamedCapture(), false); RefreshPresets(); });
        Button(4, 190, "导出当前设置供分享 / Export current", () => RainWrightWorkspaceRuntime.SavePreset(NamedCapture(), true));
        var exportSelected = new OpSimpleButton(new Vector2(315, 190), new Vector2(250, 30), "导出选中 / Export selected");
        exportSelected.OnClick += _ => RainWrightWorkspaceRuntime.Run(() => RainWrightWorkspaceRuntime.SavePreset(RainWrightWorkspaceRuntime.ReadPreset(presetBox.value), true)); Tabs[4].AddItems(exportSelected);
        importBox = Text(4, 140, "导入文件完整路径 / File path", importPath);
        Button(4, 95, "导入到列表 / Import to list", () => { RainWrightWorkspaceRuntime.Import(importBox.value.Trim().Trim('"')); RefreshPresets(); });
        var folder = new OpSimpleButton(new Vector2(315, 95), new Vector2(250, 30), "分享文件夹 / Share folder");
        folder.OnClick += _ => RainWrightWorkspaceRuntime.Run(() => RainWrightWorkspaceRuntime.OpenDirectory(RainWrightWorkspaceRuntime.ExportDirectory)); Tabs[4].AddItems(folder);
        statusLabel = new OpLabel(new Vector2(30, 5), new Vector2(535, 75), "", FLabelAlignment.Left) { autoWrap = true };
        Tabs[4].AddItems(statusLabel);
        Title(5, "Remix 外观 / Appearance");
        Number(5, 490, "玻璃透明度 / Glass opacity", remixOpacity);
        Number(5, 430, "边缘与反光强度 / Reflections", remixReflection);
        Check(5, 365, "鎏金标题 / Gilded title", remixGold);
        Integer(5, 310, "简介段落间距 / Paragraph spacing", remixGap);
        Label(5, 165, "玻璃强度即时生效；简介排版重新打开后生效。", 30);
        Label(5, 120, "外观设置可随工作预设一起分享。", 30);
        foreach (int tab in new[] { 0, 1, 2, 3, 5 })
        {
            var line = new OpLabel(new Vector2(30, 0), new Vector2(535, 25), "", FLabelAlignment.Left);
            statusLines.Add(line); Tabs[tab].AddItems(line);
        }
        Tabs = new[] { RainWrightAboutPage.Create(this) }.Concat(Tabs).ToArray();
        Tabs[0].AddItems(new RemixSettingsNavigation(this));
        Tabs[0].colorButton = RemixDetailsChrome.Accent;
        Pull();
    }
    private RainWrightWorkspacePreset NamedCapture() => RainWrightWorkspaceRuntime.Capture(nameBox.value, authorBox.value, descriptionBox.value);
    private List<ListItem> PresetItems() => RainWrightWorkspaceRuntime.ListPresets().Select((x, i) => new ListItem(x.Key, x.Value, i)).ToList();
    private void RefreshPresets()
    {
        if (presetBox == null) return;
        var existing = presetBox.GetItemList();
        var additions = PresetItems().Where(x => !existing.Any(e => e.name == x.name)).ToArray();
        if (additions.Length > 0) presetBox.AddItems(false, additions);
    }
    private void Pull()
    {
        pulling = true;
        try
        {
            var current = RainWrightWorkspaceRuntime.Capture(); var p = current.Preferences; var ui = current.Ui.Element("Presentation");
            language.Value = (string)ui.Attribute("language"); placement.Value = (string)ui.Attribute("toolPlacement"); scene.Value = (string)ui.Attribute("scenePlacement");
            chineseSize.Value = DevToolUiSettings.PersistedChineseFontSize; englishSize.Value = DevToolUiSettings.PersistedEnglishFontSize;
            weight.Value = DevToolUiSettings.PersistedEnglishFontWeight; alpha.Value = p.WindowAlpha;
            remixOpacity.Value = p.RemixGlassOpacity; remixReflection.Value = p.RemixReflection; remixGap.Value = p.RemixSectionGap;
            remixGold.Value = p.RemixGildedTitle; remixHeadings.Value = p.RemixSectionHeadings;
            var t = DevToolUiSettings.TextColor; var m = DevToolUiSettings.DisabledTextColor;
            textColor.Value = new Color(t.X, t.Y, t.Z, t.W); mutedColor.Value = new Color(m.X, m.Y, m.Z, m.W);
            page.Value = p.DefaultPage; remember.Value = p.RememberLayout; reverse.Value = p.ReverseZoom; sensitivity.Value = p.DragSensitivity;
            snap.Value = p.GridSnap; gridSize.Value = p.GridSize; autoSave.Value = current.AutoSave; interval.Value = current.AutoSaveSeconds;
            snapshots.Value = p.SnapshotCount; recovery.Value = p.RecoveryPrompt; allowZ.Value = current.AllowZ; allowS.Value = current.AllowS; allowY.Value = current.AllowY;
            foreach (var item in shortcuts) item.Value.Value = p.Shortcuts[item.Key];
            if (Tabs != null) ShowConfigs();
        }
        finally { pulling = false; }
    }
    private RainWrightWorkspacePreset Draft()
    {
        var result = RainWrightWorkspaceRuntime.Capture("Remix settings"); var p = result.Preferences;
        p.DefaultPage = page.Value; p.WindowAlpha = alpha.Value; p.RememberLayout = remember.Value; p.ReverseZoom = reverse.Value;
        p.DragSensitivity = sensitivity.Value; p.GridSnap = snap.Value; p.GridSize = gridSize.Value; p.SnapshotCount = snapshots.Value; p.RecoveryPrompt = recovery.Value;
        p.RemixGlassOpacity = remixOpacity.Value; p.RemixReflection = remixReflection.Value; p.RemixSectionGap = remixGap.Value;
        p.RemixGildedTitle = remixGold.Value; p.RemixSectionHeadings = remixHeadings.Value;
        foreach (var item in shortcuts) p.Shortcuts[item.Key] = item.Value.Value;
        result.AutoSave = autoSave.Value; result.AutoSaveSeconds = interval.Value; result.AllowZ = allowZ.Value; result.AllowS = allowS.Value; result.AllowY = allowY.Value;
        var ui = result.Ui.Element("Presentation");
        ui.SetAttributeValue("language", language.Value); ui.SetAttributeValue("toolPlacement", placement.Value); ui.SetAttributeValue("scenePlacement", scene.Value);
        ui.SetAttributeValue("chineseFontSize", chineseSize.Value); ui.SetAttributeValue("englishFontSize", englishSize.Value); ui.SetAttributeValue("englishFontWeight", weight.Value);
        SetColor(ui, "text", textColor.Value); SetColor(ui, "muted", mutedColor.Value);
        return result;
    }
    private static void SetColor(XElement element, string key, Color color)
    { element.SetAttributeValue(key + "R", color.r); element.SetAttributeValue(key + "G", color.g); element.SetAttributeValue(key + "B", color.b); element.SetAttributeValue(key + "A", color.a); }
    private void Title(int tab, string title) => Tabs[tab].AddItems(new OpLabel(30, 550, title, true));
    private static string TabName(string id) => OptionInterface.Translate("rainwright-tab-" + id);
    private void Label(int tab, float y, string text, float x = 30, float width = 535) =>
        Tabs[tab].AddItems(new OpLabel(new Vector2(x, y - 5), new Vector2(width, 40),
            RemixTextLayout.Wrap(text, width - 4), FLabelAlignment.Left));
    private void Check(int tab, float y, string text, Configurable<bool> value)
    { Tabs[tab].AddItems(new OpCheckBox(value, new Vector2(30, y))); Label(tab, y, text, 65, 500); }
    private void Number(int tab, float y, string text, Configurable<float> value)
    { Label(tab, y, text, width: 385); Tabs[tab].AddItems(new OpUpdown(value, new Vector2(435, y), 130, 2)); }
    private void Integer(int tab, float y, string text, Configurable<int> value)
    { Label(tab, y, text, width: 385); Tabs[tab].AddItems(new OpUpdown(value, new Vector2(435, y), 130)); }
    private OpTextBox Text(int tab, float y, string text, Configurable<string> value)
    { Label(tab, y, text, width: 200); var box = new OpTextBox(value, new Vector2(250, y), 315); Tabs[tab].AddItems(box); return box; }
    private void Combo(int tab, float y, string text, Configurable<string> value, string[] choices)
    { Label(tab, y, text, width: 300); Tabs[tab].AddItems(new OpComboBox(value, new Vector2(350, y), 215, choices)); }
    private void Button(int tab, float y, string text, Action action)
    {
        var button = new OpSimpleButton(new Vector2(30, y), new Vector2(270, 30), text);
        button.OnClick += _ => RainWrightWorkspaceRuntime.Run(action); Tabs[tab].AddItems(button);
    }
}
