using DryCycle.DevUI.DevTool.Personalization;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal static class RainWrightPresetsWindow
{
    private static bool open;
    private static string selected = "Room", name = "My workspace", author = "", description = "", importPath = "";
    internal static void Toggle()
    {
        open = !open;
        if (open) RainWrightWorkspaceRuntime.Queue(RainWrightWorkspaceRuntime.RefreshPresetList);
    }
    internal static void Draw()
    {
        if (!open) return;
        ImGui.SetNextWindowSize(new Num.Vector2(620, 510), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);
        if (!ImGui.Begin(DevToolUiSettings.T("工作预设与分享###RainWrightPresets", "Workspace presets and sharing###RainWrightPresets"), ref open)) { ImGui.End(); return; }
        var presets = RainWrightWorkspaceRuntime.Presets;
        string preview = selected;
        foreach (var preset in presets) if (preset.Key == selected) preview = preset.Value;
        if (ImGui.BeginCombo(DevToolUiSettings.T("选择预设", "Preset"), preview))
        {
            foreach (var preset in presets)
                if (ImGui.Selectable(preset.Value + "##" + preset.Key, preset.Key == selected)) selected = preset.Key;
            ImGui.EndCombo();
        }
        if (ImGui.Button(DevToolUiSettings.T("应用选中预设", "Apply selected")))
        { string id = selected; RainWrightWorkspaceRuntime.Queue(() => RainWrightWorkspaceRuntime.Apply(RainWrightWorkspaceRuntime.ReadPreset(id))); }
        ImGui.SameLine();
        if (ImGui.Button(DevToolUiSettings.T("导出选中预设", "Export selected")))
        { string id = selected; RainWrightWorkspaceRuntime.Queue(() => RainWrightWorkspaceRuntime.SavePreset(RainWrightWorkspaceRuntime.ReadPreset(id), true)); }
        ImGui.TextWrapped(DevToolUiSettings.T("只切换个人设置，保留当前文档、选择和撤销历史。首次页面在新会话生效。", "Changes personal preferences while preserving documents, selections and history. Startup page applies to new sessions."));
        ImGui.Separator();
        ImGui.InputText(DevToolUiSettings.T("名称", "Name"), ref name, 240);
        ImGui.InputText(DevToolUiSettings.T("作者", "Author"), ref author, 360);
        ImGui.InputText(DevToolUiSettings.T("说明", "Description"), ref description, 3000);
        if (ImGui.Button(DevToolUiSettings.T("保存当前设置为预设", "Save current as preset"))) Save(false);
        ImGui.SameLine();
        if (ImGui.Button(DevToolUiSettings.T("导出供分享", "Export for sharing"))) Save(true);
        ImGui.InputText(DevToolUiSettings.T("导入完整文件路径", "Import file path"), ref importPath, 2048);
        if (ImGui.Button(DevToolUiSettings.T("导入到列表", "Import to list")))
        { string path = importPath.Trim().Trim('"'); RainWrightWorkspaceRuntime.Queue(() => RainWrightWorkspaceRuntime.Import(path)); }
        ImGui.SameLine();
        if (ImGui.Button(DevToolUiSettings.T("打开分享文件夹", "Open share folder")))
            RainWrightWorkspaceRuntime.Queue(() => RainWrightWorkspaceRuntime.OpenDirectory(RainWrightWorkspaceRuntime.ExportDirectory));
        ImGui.TextWrapped(RainWrightWorkspaceRuntime.Status);
        ImGui.End();
    }
    private static void Save(bool share)
    {
        string title = name, creator = author, detail = description;
        RainWrightWorkspaceRuntime.Queue(() => RainWrightWorkspaceRuntime.SavePreset(RainWrightWorkspaceRuntime.Capture(title, creator, detail), share));
    }
}
