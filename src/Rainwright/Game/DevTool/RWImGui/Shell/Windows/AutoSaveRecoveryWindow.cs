using System;
using DryCycle.DevUI.DevTool.AutoSave;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Recovery is always explicit; rendering only queues an action. Actual filesystem changes
/// are executed by AutoSaveCoordinator on the Rain World main thread.
/// </summary>
internal static class AutoSaveRecoveryWindow
{
    private static bool open;
    internal static bool IsOpen => open;
    private static string confirming = string.Empty;

    internal static void Toggle()
    {
        open = !open;
        if (!open) confirming = string.Empty;
    }

    internal static void DrawInline(Num.Vector2 display)
    {
        if (!open) return;
        RecoverySnapshotStore.Candidate[] candidates = RecoverySnapshotStore.Pending;
        string status = RecoverySnapshotStore.Status;
        ImGui.Spacing();
        ImGui.Separator();
        float preferredHeight = candidates.Length == 0 ? 100f : 360f;
        float height = Math.Min(preferredHeight, Math.Max(72f, display.Y - ImGui.GetCursorScreenPos().Y - 16f));
        if (!ImGui.BeginChild("##DevToolRecoveryContents", new Num.Vector2(0f, height), ImGuiChildFlags.None))
        { ImGui.EndChild(); return; }
        try
        {
            DevToolWidgets.SectionHeader(DevToolUiSettings.T("恢复", "Recovery"));
            if (candidates.Length == 0 && string.IsNullOrEmpty(status))
            {
                DevToolWidgets.MutedText(DevToolUiSettings.T("没有待恢复的记录。", "No recovery records available."));
                return;
            }

            if (candidates.Length > 0) ImGui.TextWrapped(DevToolUiSettings.T(
                "发现之前意外中断的编辑记录。恢复会备份原文件，并要求重启游戏以重新加载作者数据。请先确认对应文件。",
                "A previous editing session may have ended unexpectedly. Recovery backs up the original file. Restart the game after restoring."));
            if (!string.IsNullOrEmpty(status))
            {
                ImGui.Separator();
                ImGui.TextWrapped(status);
            }
            for (int i = 0; i < candidates.Length; i++)
            {
                RecoverySnapshotStore.Candidate item = candidates[i];
                ImGui.PushID(item.Id);
                ImGui.Separator();
                ImGui.TextUnformatted(item.Document);
                ImGui.TextWrapped(item.Target);
                ImGui.TextUnformatted(item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                if (ImGui.Button(DevToolUiSettings.T("选择恢复", "Select Recovery")))
                    confirming = item.Id;
                ImGui.SameLine();
                if (ImGui.Button(DevToolUiSettings.T("忽略", "Ignore")))
                {
                    RecoverySnapshotStore.Dismiss(item.Id);
                    if (confirming == item.Id) confirming = string.Empty;
                }
                if (confirming == item.Id)
                {
                    ImGui.TextWrapped(DevToolUiSettings.T(
                        "将恢复内容写入所列作者文件，原文件备份为 .Rainwright-before-recovery.bak。确定继续？",
                        "Restore this snapshot to the author file? Its current contents will be backed up as .Rainwright-before-recovery.bak."));
                    if (ImGui.Button(DevToolUiSettings.T("确认恢复", "Confirm Recovery")))
                    {
                        RecoverySnapshotStore.RequestRestore(item.Id);
                        confirming = string.Empty;
                    }
                    ImGui.SameLine();
                    if (ImGui.Button(DevToolUiSettings.T("取消", "Cancel")))
                        confirming = string.Empty;
                }
                ImGui.PopID();
            }
        }
        finally { ImGui.EndChild(); }
    }
}
