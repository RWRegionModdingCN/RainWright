using System;
using DryCycle.DevUI.DevTool.Input;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Opt-in player-input exceptions while holding Ctrl+Z/S/Y in the editor.
/// No file access or Remix interaction occurs in the render callback.
/// </summary>
internal static class ShortcutInputSettingsWindow
{
    private static bool open;
    private static bool restoreOnOpen = true;
    private static bool z, s, y;
    private static string status = string.Empty;
    private static bool saveFailed;
    private static long observedRevision;

    internal static void Toggle()
    {
        open = !open;
        if (!open) return;
        restoreOnOpen = true;
        status = string.Empty;
        saveFailed = false;
        observedRevision = EditorShortcutGameplayPolicy.OwnedWriteRevision;
    }

    internal static void Draw()
    {
        if (!open) return;
        if (restoreOnOpen)
        {
            z = EditorShortcutGameplayPolicy.AllowZ;
            s = EditorShortcutGameplayPolicy.AllowS;
            y = EditorShortcutGameplayPolicy.AllowY;
            restoreOnOpen = false;
        }

        long revision = EditorShortcutGameplayPolicy.OwnedWriteRevision;
        if (revision != observedRevision)
        {
            observedRevision = revision;
            saveFailed = !EditorShortcutGameplayPolicy.LastOwnedWriteSucceeded;
            status = saveFailed
                ? DevToolUiSettings.T("保存失败：", "Save failed: ") +
                    EditorShortcutGameplayPolicy.LastOwnedWriteError
                : DevToolUiSettings.T("设置已保存。", "Settings saved.");
        }

        ImGui.SetNextWindowSize(new Num.Vector2(480f, 280f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);
        if (!ImGui.Begin(
                DevToolUiSettings.T("快捷键玩家输入###RainwrightInput",
                    "Shortcut Player Input###RainwrightInput"),
                ref open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.TextWrapped(DevToolUiSettings.T(
            "这些开关只控制按住 Ctrl 使用编辑器快捷键时，是否仍允许 Z、S、Y 键影响玩家。不会关闭保存、撤销或重做。",
            "These switches allow Z/S/Y to affect the player while Ctrl editor shortcuts are held. They do not disable Save, Undo or Redo."));
        ImGui.Spacing();
        ImGui.Checkbox(DevToolUiSettings.T("Ctrl+Z 时允许 Z 控制玩家", "Allow player Z during Ctrl+Z"), ref z);
        ImGui.Checkbox(DevToolUiSettings.T("Ctrl+S 时允许 S 控制玩家", "Allow player S during Ctrl+S"), ref s);
        ImGui.Checkbox(DevToolUiSettings.T("Ctrl+Y 时允许 Y 控制玩家", "Allow player Y during Ctrl+Y"), ref y);
        ImGui.Spacing();

        if (ImGui.Button(DevToolUiSettings.T("应用并保存", "Apply & Save")))
        {
            if (EditorShortcutGameplayPolicy.TryQueueOwned(z, s, y, out string error))
            {
                status = DevToolUiSettings.T(
                    "正在保存……",
                    "Saving...");
                saveFailed = false;
            }
            else
            {
                status = DevToolUiSettings.T("保存失败：", "Save failed: ") + error;
                saveFailed = true;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button(DevToolUiSettings.T("取消修改", "Discard Changes")))
        {
            z = EditorShortcutGameplayPolicy.AllowZ;
            s = EditorShortcutGameplayPolicy.AllowS;
            y = EditorShortcutGameplayPolicy.AllowY;
            status = string.Empty;
            saveFailed = false;
        }

        if (!string.IsNullOrEmpty(status))
        {
            ImGui.Spacing();
            if (saveFailed) ImGui.TextWrapped(status);
            else ImGui.TextUnformatted(status);
        }
        ImGui.End();
    }
}
