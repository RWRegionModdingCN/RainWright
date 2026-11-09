using System;
using DryCycle.DevUI.DevTool.AutoSave;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Editor-owned AutoSave settings. This window is shown only on demand, does
/// no disk I/O during drawing, and never writes DryCycle's legacy Remix config.
/// </summary>
internal static class AutoSaveSettingsWindow
{
    private static bool open;
    private static bool restoreDraftOnOpen = true;
    private static bool enabled;
    private static int intervalSeconds;
    private static string status = string.Empty;
    private static bool lastSaveFailed;
    private static long observedWriteRevision;

    internal static void Toggle()
    {
        open = !open;
        if (open)
        {
            restoreDraftOnOpen = true;
            status = string.Empty;
            lastSaveFailed = false;
            observedWriteRevision = EditorAutoSavePreferences.OwnedWriteRevision;
        }
    }

    internal static void Draw()
    {
        if (!open) return;
        if (restoreDraftOnOpen)
        {
            enabled = EditorAutoSavePreferences.Enabled;
            intervalSeconds = EditorAutoSavePreferences.IntervalSeconds;
            restoreDraftOnOpen = false;
        }

        long revisionNow = EditorAutoSavePreferences.OwnedWriteRevision;
        if (revisionNow != observedWriteRevision)
        {
            observedWriteRevision = revisionNow;
            lastSaveFailed = !EditorAutoSavePreferences.LastOwnedWriteSucceeded;
            status = lastSaveFailed
                ? DevToolUiSettings.T("保存失败：", "Save failed: ") +
                    EditorAutoSavePreferences.LastOwnedWriteError
                : DevToolUiSettings.T(
                    "设置已保存。",
                    "Settings saved.");
        }

        ImGui.SetNextWindowSize(new Num.Vector2(430f, 250f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);
        if (!ImGui.Begin(
                DevToolUiSettings.T("自动保存设置###RainwrightAutoSave", "AutoSave Settings###RainwrightAutoSave"),
                ref open, ImGuiWindowFlags.NoCollapse))
        {
            ImGui.End();
            return;
        }

        ImGui.TextWrapped(DevToolUiSettings.T(
            "提交编辑后立即写盘，拖动结束时保存一次。关闭 UI 或正常退出时补存；失败会保留修改并重试。崩溃恢复仍需确认。",
            "Committed edits save immediately; drags save on release. Closing the UI or quitting flushes pending edits. Failed writes remain pending and retry. Recovery still requires confirmation."));
        ImGui.Spacing();
        ImGui.Checkbox(DevToolUiSettings.T("启用自动保存", "Enable AutoSave"), ref enabled);

        ImGui.Spacing();
        if (ImGui.Button(DevToolUiSettings.T("应用并保存", "Apply & Save")))
        {
            if (EditorAutoSavePreferences.TryQueueOwned(enabled, intervalSeconds, out string error))
            {
                status = DevToolUiSettings.T(
                    "正在保存……",
                    "Saving...");
                lastSaveFailed = false;
            }
            else
            {
                status = DevToolUiSettings.T("保存失败：", "Save failed: ") + error;
                lastSaveFailed = true;
            }
        }
        ImGui.SameLine();
        if (ImGui.Button(DevToolUiSettings.T("取消修改", "Discard Changes")))
        {
            enabled = EditorAutoSavePreferences.Enabled;
            intervalSeconds = EditorAutoSavePreferences.IntervalSeconds;
            status = string.Empty;
            lastSaveFailed = false;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            ImGui.Spacing();
            if (lastSaveFailed)
                ImGui.TextWrapped(status);
            else
                ImGui.TextUnformatted(status);
        }
        ImGui.End();
    }
}
