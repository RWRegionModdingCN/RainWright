using System;
using System.Collections.Generic;
using System.Globalization;
using DryCycle.DevUI.DevTool.Background;
using DryCycle.DevUI.DevTool.Core;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal sealed class BackgroundDevToolPage : DevToolFrontendPageBase
{
    public override string Id => "background";
    public override EditorToolMode Mode => EditorToolMode.Background;
    public override int NavigationOrder => 550;
    public override string NavigationLabel => DevToolUiSettings.T("背景", "Background");
    public override string NavigationTooltip => DevToolUiSettings.T("原版背景与 RegionKit 背景数据", "Vanilla and RegionKit background data");
    public override bool UsesDedicatedWorkspace => true;
    public override void DrawBrowser(EditorPresentationSnapshot snapshot) { }
    public override void DrawInspector(EditorPresentationSnapshot snapshot) { }
    public override void DrawWorkspace(EditorPresentationSnapshot snapshot, Num.Vector2 display) => BackgroundEditorView.Draw(display);
    protected override void OnDeactivate() => BackgroundEditorView.Reset();
    protected override void OnReset() => BackgroundEditorView.Reset();
}

internal static class BackgroundEditorView
{
    private static long context;
    private static long revision;
    private static int selected;
    private static string file = "", output = "", search = "";
    private static readonly Dictionary<int, string> edits = new();
    private static BackgroundElementSnapshot[] observedElements;
    private static string observedSearch;
    private static readonly List<BackgroundEditorLayout.Row> visibleElements = new();
    private static readonly HashSet<int> collapsedGroups = new();
    private static readonly Dictionary<BackgroundFieldSection, List<BackgroundFieldSnapshot>> fieldGroups = new();
    private static BackgroundFieldSnapshot[] observedFields;
    private static bool treeDirty;
    private static int assetGroup, assetField;
    private static string assetSearch = "", observedAssetSearch;
    private static int observedAssetGroup = -1;
    private static string[] observedIllustrations, observedSprites, visibleAssets = Array.Empty<string>();
    internal static void Reset()
    {
        context = 0; revision = 0; selected = 0; edits.Clear(); observedElements = null; visibleElements.Clear();
        collapsedGroups.Clear(); fieldGroups.Clear(); observedFields = null; search = ""; file = ""; treeDirty = true;
        assetGroup = 0; assetField = 0; assetSearch = ""; observedAssetSearch = null; observedAssetGroup = -1;
        observedIllustrations = null; observedSprites = null; visibleAssets = Array.Empty<string>();
    }
    internal static void Draw(Num.Vector2 display)
    {
        var snapshot = BackgroundEditorRuntime.Current;
        if (!snapshot.Available) return;
        if (context != snapshot.Context) { Reset(); context = snapshot.Context; output = snapshot.Name + "-rainwright"; }
        if (revision != snapshot.Revision) { edits.Clear(); revision = snapshot.Revision; }
        float width = Math.Min(480f, Math.Max(300f, display.X * .32f));
        float top = 140f;
        if (DevToolTopStatusWindow.TryGetCurrentRect(out var position, out var size)) top = position.Y + size.Y + 58f;
        ImGui.SetNextWindowPos(new Num.Vector2(Math.Max(8f, display.X - width - 16f), top), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Num.Vector2(width, Math.Max(300f, display.Y - top - 24f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Num.Vector2(300f, 240f), new Num.Vector2(Math.Max(300f, display.X - 16f), Math.Max(300f, display.Y - 16f)));
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);
        if (!ImGui.Begin(DevToolUiSettings.T("背景###RainWrightBackground", "Background###RainWrightBackground"), ImGuiWindowFlags.NoCollapse)) { ImGui.End(); return; }
        try
        {
            FloatingWindowSnap.TrackCurrentWindow("Background");
            if (snapshot.Failed)
            {
                ImGui.TextWrapped(snapshot.Error);
                DevToolWidgets.MutedText(DevToolUiSettings.T("可重试，或通过顶部切换按钮使用原版 DevTool。", "Retry, or use the global switch to return to the original DevTool."), true);
                if (DevToolWidgets.ActionButton(DevToolUiSettings.T("重试初始化", "Retry initialization"), "BgRetry", DevToolButtonTone.Normal)) Send(snapshot, "retry");
                return;
            }
            DevToolWidgets.SectionHeader(snapshot.Name + (snapshot.Dirty ? "  *" : ""));
            if (ImGui.BeginCombo(DevToolUiSettings.T("类型", "Type"), snapshot.Scene))
            {
                foreach (string type in BackgroundSceneAdapter.SceneNames)
                {
                    if (!snapshot.HasRegionKit && type is "RotWormScene" or "VoidSeaScene") continue;
                    if (ImGui.Selectable(type, snapshot.Scene == type)) Send(snapshot, "type", value: type);
                }
                ImGui.EndCombo();
            }
            if (!snapshot.HasRegionKit) DevToolWidgets.MutedText(DevToolUiSettings.T("可编辑原版背景类型。自定义背景文件需要启用 RegionKit。", "Vanilla scene types are available. Custom background files require RegionKit."), true);
            if (snapshot.Parent.Length > 0) ImGui.TextDisabled(DevToolUiSettings.T("父背景：", "Parent: ") + snapshot.Parent);
            if (ImGui.BeginTabBar("##BackgroundTabs"))
            {
                if (ImGui.BeginTabItem(DevToolUiSettings.T("场景", "Scene")))
                {
                    DrawFields(snapshot, snapshot.Settings, false);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(DevToolUiSettings.T("元素", "Elements")))
                {
                    DrawElements(snapshot);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem(DevToolUiSettings.T("文件", "Files")))
                {
                    DrawFiles(snapshot);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
            if (snapshot.Error.Length > 0) ImGui.TextWrapped(snapshot.Error);
            else if (snapshot.Status.Length > 0) DevToolWidgets.MutedText(snapshot.Status, true);
        }
        finally { ImGui.End(); }
    }
    private static void DrawElements(BackgroundEditorSnapshot snapshot)
    {
        DevToolWidgets.FullWidthInputText(DevToolUiSettings.T("搜索元素", "Search elements"), "BgElementSearch", ref search, 128);
        if (snapshot.HasRegionKit && ImGui.BeginCombo(DevToolUiSettings.T("添加元素", "Add element"), "+"))
        {
            foreach (string name in snapshot.AddTypes) if (ImGui.Selectable(name)) Send(snapshot, "add", value: name);
            ImGui.EndCombo();
        }
        // A clipped list keeps large backgrounds cheap without materializing old DevUI controls.
        if (ImGui.BeginChild("##BgElementList", new Num.Vector2(0, Math.Max(130f, Math.Min(270f, ImGui.GetContentRegionAvail().Y * .36f))), ImGuiChildFlags.Borders))
        {
            if (treeDirty || !ReferenceEquals(observedElements, snapshot.Elements) || observedSearch != search)
            {
                observedElements = snapshot.Elements; observedSearch = search; treeDirty = false;
                BackgroundEditorLayout.BuildRows(snapshot.Elements, search, collapsedGroups, visibleElements);
                bool exists = false;
                foreach (var element in snapshot.Elements) if (element.Id == selected) { exists = true; break; }
                if (!exists) selected = 0;
            }
            using DevToolListClipper clipper = new(visibleElements.Count);
            while (clipper.Step(out int first, out int last))
                for (int i = first; i < last; i++)
                {
                    var row = visibleElements[i];
                    var element = row.Element;
                    ImGui.PushID(element.Id);
                    float x = ImGui.GetCursorPosX();
                    ImGui.SetCursorPosX(x + row.Depth * 14f);
                    if (element.IsGroup)
                    {
                        if (ImGui.SmallButton(collapsedGroups.Contains(element.Id) ? ">" : "v"))
                        {
                            if (!collapsedGroups.Add(element.Id)) collapsedGroups.Remove(element.Id);
                            treeDirty = true;
                        }
                        ImGui.SameLine();
                    }
                    if (ImGui.Selectable(element.Name, selected == element.Id)) selected = element.Id;
                    ImGui.PopID();
                }
        }
        ImGui.EndChild();
        if (selected == 0) DevToolWidgets.MutedText(DevToolUiSettings.T("选择上方元素查看属性；分组的位移和深度会影响组内元素。", "Select an element to inspect it. Group position and depth affect its children."), true);
        foreach (var element in snapshot.Elements)
        {
            if (element.Id != selected) continue;
            DevToolWidgets.SectionHeader(element.Name);
            DevToolWidgets.MutedText(element.IsGroup
                ? DevToolUiSettings.T("分组：修改会共同影响所有子元素。", "Group: changes apply to all children.")
                : DevToolUiSettings.T("仅编辑当前所选元素。", "Editing the selected element."), true);
            if (DevToolWidgets.ActionButton(DevToolUiSettings.T("删除元素", "Delete element"), "BgDelete", DevToolButtonTone.Subtle)) Send(snapshot, "delete", element.Id);
            string parentName = DevToolUiSettings.T("无分组", "No group");
            foreach (var group in snapshot.Elements) if (group.Id == element.ParentId) { parentName = group.Name; break; }
            if (ImGui.BeginCombo(DevToolUiSettings.T("所属分组", "Group"), parentName))
            {
                if (ImGui.Selectable(DevToolUiSettings.T("无分组", "No group"), element.ParentId == 0)) Send(snapshot, "group", element.Id, "0");
                foreach (var group in snapshot.Elements)
                    if (group.IsGroup && group.Id != element.Id && ImGui.Selectable(group.Name + "##" + group.Id, element.ParentId == group.Id))
                        Send(snapshot, "group", element.Id, group.Id.ToString(CultureInfo.InvariantCulture));
                ImGui.EndCombo();
            }
            if (element.ParentId != 0) DevToolWidgets.MutedText(DevToolUiSettings.T("坐标与深度相对于所属分组。", "Position and depth are relative to the selected group."), true);
            ImGui.Separator();
            DrawFields(snapshot, element.Fields, true);
            break;
        }
    }
    private static void DrawFields(BackgroundEditorSnapshot snapshot, BackgroundFieldSnapshot[] fields, bool element)
    {
        // Only rebuild the projection when the detached snapshot changes.
        if (!ReferenceEquals(observedFields, fields))
        {
            observedFields = fields;
            foreach (var list in fieldGroups.Values) list.Clear();
            foreach (var field in fields)
            {
                var section = BackgroundEditorLayout.Section(field.Key, element);
                if (!fieldGroups.TryGetValue(section, out var list)) fieldGroups[section] = list = new List<BackgroundFieldSnapshot>();
                list.Add(field);
            }
        }
        for (int index = 0; index <= (int)BackgroundFieldSection.Other; index++)
        {
            var section = (BackgroundFieldSection)index;
            if (!fieldGroups.TryGetValue(section, out var list) || list.Count == 0) continue;
            string title = section switch
            {
                BackgroundFieldSection.Placement => DevToolUiSettings.T("位置与深度", "Position & depth"),
                BackgroundFieldSection.Day => DevToolUiSettings.T("白天 · 天空与色彩", "Day · sky & colors"),
                BackgroundFieldSection.Dusk => DevToolUiSettings.T("黄昏 · 天空与色彩", "Dusk · sky & colors"),
                BackgroundFieldSection.Night => DevToolUiSettings.T("夜晚 · 天空与色彩", "Night · sky & colors"),
                BackgroundFieldSection.Clouds => DevToolUiSettings.T("云层 · 高度、数量与分布", "Clouds · altitude, count & distribution"),
                BackgroundFieldSection.Fog => DevToolUiSettings.T("雾 · 高度与深度", "Fog · altitude & depth"),
                BackgroundFieldSection.Ground => DevToolUiSettings.T("地面与碎石层", "Ground & rubble"),
                BackgroundFieldSection.Wind => DevToolUiSettings.T("风", "Wind"),
                BackgroundFieldSection.Geometry => DevToolUiSettings.T("形状与缩放", "Shape & scale"),
                BackgroundFieldSection.Appearance => DevToolUiSettings.T("图像与外观", "Image & appearance"),
                BackgroundFieldSection.Layer => DevToolUiSettings.T("绘制层级", "Draw layers"),
                _ => DevToolUiSettings.T("其他参数", "Other properties")
            };
            ImGui.PushID(index + (element ? 100 : 0));
            if (ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen))
            {
                if (section == BackgroundFieldSection.Clouds)
                    DevToolWidgets.MutedText(DevToolUiSettings.T("高度控制显示范围；数量与深度共同决定云层的远近分布。", "Altitude sets the visible range; count and depth determine cloud distribution."), true);
                else if (section is BackgroundFieldSection.Day or BackgroundFieldSection.Dusk or BackgroundFieldSection.Night)
                    DevToolWidgets.MutedText(DevToolUiSettings.T("此时段的天空图像、大气色和叠乘色。", "Sky image, atmosphere and multiply colors for this time of day."), true);
                foreach (var field in list) DrawField(snapshot, field);
                ImGui.Spacing();
            }
            ImGui.PopID();
        }
    }
    private static void DrawField(BackgroundEditorSnapshot snapshot, BackgroundFieldSnapshot field)
    {
        ImGui.PushID(field.Id);
        try
        {
            string label = field.Key switch
            {
                "daySky" or "duskSky" or "nightSky" => DevToolUiSettings.T("天空图像", "Sky image"),
                "atmosphereColor" or "duskAtmosphereColor" or "nightAtmosphereColor" => DevToolUiSettings.T("大气颜色", "Atmosphere color"),
                "multiplyColor" or "duskMultiplyColor" or "nightMultiplyColor" => DevToolUiSettings.T("叠乘颜色", "Multiply color"),
                "startAltitude" => DevToolUiSettings.T("起始高度", "Start altitude"),
                "endAltitude" => DevToolUiSettings.T("结束高度", "End altitude"),
                "cloudsStartDepth" => DevToolUiSettings.T("近云起始深度", "Near clouds start depth"),
                "cloudsEndDepth" => DevToolUiSettings.T("近云结束 / 远云起始深度", "Near end / distant start depth"),
                "distantCloudsEndDepth" => DevToolUiSettings.T("远云结束深度", "Distant clouds end depth"),
                "cloudsCount" => DevToolUiSettings.T("近云数量", "Near clouds count"),
                "distantCloudsCount" => DevToolUiSettings.T("远云数量", "Distant clouds count"),
                "curveCloudDepth" => DevToolUiSettings.T("云层深度分布曲线", "Cloud depth curve"),
                "startFogAltitude" => DevToolUiSettings.T("雾起始高度", "Fog start altitude"),
                "endFogAltitude" => DevToolUiSettings.T("雾结束高度", "Fog end altitude"),
                "floorLevel" => DevToolUiSettings.T("地面高度基准", "Floor level"),
                "rubbleCount" => DevToolUiSettings.T("碎石层数量", "Rubble layer count"),
                "rubbleStartDepth" => DevToolUiSettings.T("碎石起始深度", "Rubble start depth"),
                "rubbleEndDepth" => DevToolUiSettings.T("碎石结束深度", "Rubble end depth"),
                "windDir" => DevToolUiSettings.T("风向", "Wind direction"),
                "roomOffset" => DevToolUiSettings.T("房间偏移", "Room offset"),
                "_backgroundOffset" => DevToolUiSettings.T("局部背景偏移", "Local background offset"),
                "pos" => DevToolUiSettings.T("位置 X, Y", "Position X, Y"),
                "depth" => DevToolUiSettings.T("深度", "Depth"),
                "assetName" => DevToolUiSettings.T("图像资源", "Image asset"),
                "spriteScale" => DevToolUiSettings.T("缩放 X, Y", "Scale X, Y"),
                "spriteAlpha" => DevToolUiSettings.T("不透明度", "Opacity"),
                "spriteColor" => DevToolUiSettings.T("颜色", "Color"),
                "spriteShader" => DevToolUiSettings.T("着色器", "Shader"),
                "anchorPos" => DevToolUiSettings.T("锚点 X, Y", "Anchor X, Y"),
                "container" => DevToolUiSettings.T("绘制容器", "Draw container"),
                "lockX" => DevToolUiSettings.T("锁定横向移动", "Lock horizontal movement"),
                "lockY" => DevToolUiSettings.T("锁定纵向移动", "Lock vertical movement"),
                _ => field.Name
            };
            if (field.Inherited)
                ImGui.TextDisabled(DevToolUiSettings.T("继承 / 默认值", "Inherited / default"));
            else if (field.CanInherit && ImGui.SmallButton(DevToolUiSettings.T("恢复继承 / 默认", "Use inherited / default")))
                Send(snapshot, "field", field.Id, "Null");
            if (DrawCompoundField(snapshot, field, label)) return;
            if (BackgroundAssetCatalog.IsAsset(field.Key)) { DrawAssetPicker(snapshot, field, label); return; }
            if (BackgroundAssetCatalog.IsShader(field.Key))
            {
                if (ImGui.BeginCombo(label, field.Value))
                {
                    int chosen = DevToolChoiceList.DrawAll(snapshot.Shaders, Array.IndexOf(snapshot.Shaders, field.Value));
                    if (chosen >= 0) Send(snapshot, "field", field.Id, snapshot.Shaders[chosen]);
                    ImGui.EndCombo();
                }
                return;
            }
            if (field.Kind == "Int32")
            {
                int.TryParse(field.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer);
                var edit = DevToolNumericWidgets.InputInt("Background", field.Id.ToString(CultureInfo.InvariantCulture), label, integer,
                    instance: unchecked((int)(snapshot.Context * 397 ^ snapshot.Revision)));
                if (edit.Committed) Send(snapshot, "field", field.Id, edit.Value.ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (field.Kind == "Boolean")
            { bool.TryParse(field.Value, out bool flag); if (ImGui.Checkbox(label, ref flag)) Send(snapshot, "field", field.Id, flag.ToString()); return; }
            if (field.Choices.Length > 0)
            {
                if (ImGui.BeginCombo(label, field.Value))
                {
                    int chosen = DevToolChoiceList.DrawAll(field.Choices, Array.IndexOf(field.Choices, field.Value));
                    if (chosen >= 0) Send(snapshot, "field", field.Id, field.Choices[chosen]);
                    ImGui.EndCombo();
                }
                return;
            }
            if (field.Kind == "Single")
            {
                float.TryParse(field.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number);
                var edit = DevToolNumericWidgets.InputFloat("Background", field.Id.ToString(CultureInfo.InvariantCulture), label, number, instance: unchecked((int)(snapshot.Context * 397 ^ snapshot.Revision)));
                if (edit.Committed) Send(snapshot, "field", field.Id, edit.Value.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (!edits.TryGetValue(field.Id, out string value)) value = field.Value;
            ImGui.TextUnformatted(label);
            ImGui.SetNextItemWidth(-1);
            bool changed = ImGui.InputText("##value", ref value, 2048);
            bool commit = ImGui.IsItemDeactivatedAfterEdit() || ImGui.IsItemActive() && ImGui.IsKeyPressed(ImGuiKey.Enter);
            if (changed || ImGui.IsItemActive()) edits[field.Id] = value;
            else edits[field.Id] = field.Value;
            if (commit) { Send(snapshot, "field", field.Id, value); edits.Remove(field.Id); }
        }
        finally { ImGui.PopID(); }
    }
    private static bool DrawCompoundField(BackgroundEditorSnapshot snapshot, BackgroundFieldSnapshot field, string label)
    {
        string raw = edits.TryGetValue(field.Id, out string pending) ? pending : field.Value;
        if (raw == "Null") raw = field.Kind == "Color" ? "FFFFFFFF" : field.Kind == "Vector2" ? "0, 0" : raw;
        if (field.Kind == "Vector2")
        {
            string[] parts = raw.Split(',');
            if (parts.Length != 2 || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) return false;
            Num.Vector2 value = new(x, y);
            if (ImGui.InputFloat2(label, ref value))
                edits[field.Id] = value.X.ToString("R", CultureInfo.InvariantCulture) + ", " + value.Y.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (field.Kind == "Color")
        {
            string hex = raw.TrimStart('#');
            if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgba)) return false;
            if (hex.Length == 6) rgba = (rgba << 8) | 255;
            Num.Vector4 value = new((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
            if (ImGui.ColorEdit4(label, ref value, ImGuiColorEditFlags.AlphaBar))
            {
                int Byte(float channel) => (int)Math.Round(Math.Max(0f, Math.Min(1f, channel)) * 255f);
                edits[field.Id] = "#" + Byte(value.X).ToString("X2") + Byte(value.Y).ToString("X2") + Byte(value.Z).ToString("X2") + Byte(value.W).ToString("X2");
            }
        }
        else return false;
        if (ImGui.IsItemDeactivatedAfterEdit() && edits.TryGetValue(field.Id, out string valueText))
            Send(snapshot, "field", field.Id, valueText);
        return true;
    }
    private static void DrawAssetPicker(BackgroundEditorSnapshot snapshot, BackgroundFieldSnapshot field, string label)
    {
        ImGui.TextUnformatted(label);
        ImGui.SetNextItemWidth(-1f);
        if (!ImGui.BeginCombo("##asset", field.Value)) return;
        try
        {
            if (assetField != field.Id) { assetField = field.Id; assetSearch = ""; }
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.BeginCombo(DevToolUiSettings.T("分类", "Category"), BackgroundAssetCatalog.Groups[assetGroup]))
            {
                for (int i = 0; i < BackgroundAssetCatalog.Groups.Length; i++)
                    if (ImGui.Selectable(BackgroundAssetCatalog.Groups[i], assetGroup == i)) assetGroup = i;
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputText(DevToolUiSettings.T("搜索", "Search"), ref assetSearch, 128);
            if (!ReferenceEquals(observedIllustrations, snapshot.Illustrations) || !ReferenceEquals(observedSprites, snapshot.Sprites) ||
                observedAssetSearch != assetSearch || observedAssetGroup != assetGroup)
            {
                observedIllustrations = snapshot.Illustrations; observedSprites = snapshot.Sprites;
                observedAssetSearch = assetSearch; observedAssetGroup = assetGroup;
                visibleAssets = BackgroundAssetCatalog.Select(snapshot.Illustrations, snapshot.Sprites, assetGroup, assetSearch);
            }
            ImGui.BeginChild("##assets", new Num.Vector2(0f, 240f));
            try
            {
                using DevToolListClipper clipper = new(visibleAssets.Length);
                while (clipper.Step(out int first, out int last))
                    for (int i = first; i < last; i++)
                    {
                        string asset = visibleAssets[i];
                        if (ImGui.Selectable(asset, asset == field.Value))
                        { Send(snapshot, "field", field.Id, asset); ImGui.CloseCurrentPopup(); }
                    }
            }
            finally { ImGui.EndChild(); }
        }
        finally { ImGui.EndCombo(); }
    }
    private static void DrawFiles(BackgroundEditorSnapshot snapshot)
    {
        if (ImGui.BeginCombo(DevToolUiSettings.T("背景模板", "Background preset"), file))
        {
            int chosen = DevToolChoiceList.DrawAll(snapshot.Files, Array.IndexOf(snapshot.Files, file));
            if (chosen >= 0) file = snapshot.Files[chosen];
            ImGui.EndCombo();
        }
        if (DevToolWidgets.ActionButton(DevToolUiSettings.T("载入", "Load"), "BgLoad", DevToolButtonTone.Normal) && file.Length > 0) Send(snapshot, "load", value: file);
        ImGui.SameLine();
        if (DevToolWidgets.ActionButton(DevToolUiSettings.T("刷新列表", "Refresh"), "BgRefresh", DevToolButtonTone.Subtle)) Send(snapshot, "refresh");
        if (snapshot.HasRegionKit)
        {
            ImGui.Separator();
            if (ImGui.BeginCombo(DevToolUiSettings.T("父背景", "Parent background"), snapshot.Parent))
            {
                if (ImGui.Selectable(DevToolUiSettings.T("无继承（保留当前外观）", "None (keep current appearance)"), snapshot.Parent.Length == 0)) Send(snapshot, "parent");
                foreach (string name in snapshot.Files)
                    if (name != snapshot.Name && ImGui.Selectable(name, name == snapshot.Parent)) Send(snapshot, "parent", value: name);
                ImGui.EndCombo();
            }
        }
        ImGui.Separator();
        DevToolWidgets.FullWidthInputText(DevToolUiSettings.T("保存名称", "Output name"), "BgOutput", ref output, 120);
        DevToolWidgets.MutedText(DevToolUiSettings.T("自动保存到当前区域所属 Mod；载入模板不会直接覆盖模板。", "Save into the current region’s Mod; loading a preset does not overwrite it."), true);
        if (DevToolWidgets.ActionButton(DevToolUiSettings.T("保存背景与房间设置", "Save background and room settings"), "BgSave", DevToolButtonTone.Primary)) Send(snapshot, "save", value: output);
    }
    private static void Send(BackgroundEditorSnapshot snapshot, string kind, int id = 0, string value = "") => BackgroundEditorRuntime.Enqueue(new BackgroundEditorCommand(snapshot, kind, id, value));
}
