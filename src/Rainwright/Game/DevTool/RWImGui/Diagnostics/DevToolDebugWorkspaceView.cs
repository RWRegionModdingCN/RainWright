using System;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.Core;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Author-facing playtest tools. Implementation diagnostics stay in logs.
/// </summary>
internal static class DevToolDebugWorkspaceView
{
    private enum DebugSection
    {
        Overview,
        OptionalExtension
    }

    private static DebugSection section = DebugSection.Overview;
    private static readonly OptionalFrontendDebugSection<EditorPresentationSnapshot> optionalExtension = new();
    private static bool optionalExtensionFaulted;

    internal static IDisposable RegisterOptionalDiagnostics(
        string chinese, string english, Action<EditorPresentationSnapshot> draw, Action stop)
    {
        IDisposable lease = optionalExtension.Register(chinese, english, draw, stop);
        optionalExtensionFaulted = false;
        return lease;
    }

    private static void StopOptionalDiagnostics()
    {
        if (!optionalExtension.Available) return;
        try { optionalExtension.Stop(); }
        catch (Exception error)
        {
            if (!optionalExtensionFaulted)
                EditorDiagnostics.Warning("Optional diagnostics page could not stop cleanly.", error);
            optionalExtensionFaulted = true;
        }
    }

    internal static void Draw(EditorPresentationSnapshot snapshot, Num.Vector2 display)
    {
        if (section == DebugSection.OptionalExtension && !optionalExtension.Available)
            section = DebugSection.Overview;
        DevUiDiagnosticsPolicy.Enabled = false;

        // Author playtesting does not need internal frontend profiling or migration audits.
        if (DevToolPerformanceMonitor.Enabled)
        {
            DevToolPerformanceMonitor.SetEnabled(false);
            DevToolFrontendPerformanceMonitor.SetEnabled(false);
        }

        if (section != DebugSection.OptionalExtension)
            StopOptionalDiagnostics();

        float x = Math.Min(188f, Math.Max(8f, display.X * 0.12f));
        float y = 92f;
        float width = Math.Max(620f, display.X - x - 12f);
        float height = Math.Max(420f, display.Y - y - 12f);

        ImGui.SetNextWindowPos(new Num.Vector2(x, y), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Num.Vector2(width, height), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(
            new Num.Vector2(Math.Min(620f, Math.Max(420f, display.X - 24f)), 360f),
            new Num.Vector2(Math.Max(620f, display.X - 16f), Math.Max(360f, display.Y - 16f)));
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);

        if (!ImGui.Begin(
                DevToolUiSettings.T("调试中心###DevToolDebugWorkspace", "Debug Center###DevToolDebugWorkspace"),
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoBringToFrontOnFocus))
        {
            ImGui.End();
            return;
        }

        FloatingWindowSnap.TrackCurrentWindow("DebugWorkspace");

        Num.Vector2 available = ImGui.GetContentRegionAvail();
        float navWidth = Math.Min(220f, Math.Max(170f, available.X * 0.18f));

        if (ImGui.BeginChild(
                "##DevToolDebugNavigation",
                new Num.Vector2(navWidth, available.Y),
                ImGuiChildFlags.Borders))
        {
            DevToolWidgets.PaneTitle(DevToolUiSettings.T("分类", "CATEGORIES"));
            DrawSectionButton(DebugSection.Overview, DevToolUiSettings.T("总览", "Overview"));
            if (optionalExtension.Available)
                DrawSectionButton(DebugSection.OptionalExtension,
                    DevToolUiSettings.T(optionalExtension.ChineseTitle, optionalExtension.EnglishTitle));

        }
        ImGui.EndChild();

        ImGui.SameLine();

        if (ImGui.BeginChild(
                "##DevToolDebugContents",
                new Num.Vector2(0f, available.Y),
                ImGuiChildFlags.Borders))
        {
            DrawCurrentSection(snapshot);
            ScopedScrollChrome.Draw("DevToolDebugContents", pruneAfter: true);
        }
        ImGui.EndChild();

        ImGui.End();
    }

    internal static void Deactivate()
    {
        DevUiDiagnosticsPolicy.Enabled = false;
        if (DevToolPerformanceMonitor.Enabled)
        {
            DevToolPerformanceMonitor.SetEnabled(false);
            DevToolFrontendPerformanceMonitor.SetEnabled(false);
        }
        StopOptionalDiagnostics();
    }

    internal static void ResetRetainedState()
    {
        section = DebugSection.Overview;
        Deactivate();
    }

    private static void DrawSectionButton(DebugSection target, string label)
    {
        if (DevToolWidgets.ActionButton(
                label,
                "DebugSection:" + target,
                section == target ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle,
                fullWidth: true))
        {
            section = target;
        }
    }

    private static void DrawCurrentSection(EditorPresentationSnapshot snapshot)
    {
        switch (section)
        {
            case DebugSection.OptionalExtension:
                if (optionalExtension.Available)
                {
                    DrawHeading(DevToolUiSettings.T(
                        optionalExtension.ChineseTitle, optionalExtension.EnglishTitle));
                    if (!optionalExtensionFaulted)
                    {
                        try { optionalExtension.TryDraw(snapshot); }
                        catch (Exception error)
                        {
                            optionalExtensionFaulted = true;
                            EditorDiagnostics.Error(
                                "Optional diagnostics renderer threw; disabled until the extension reloads.", error);
                        }
                    }
                    if (optionalExtensionFaulted)
                        DevToolWidgets.MutedText(
                            DevToolUiSettings.T(
                                "可选调试页面出现错误，已停止绘制。请查看诊断日志。",
                                "Optional diagnostics failed and have been disabled. Check the diagnostics log."),
                            true);
                }
                else
                    DrawOverview(snapshot);
                break;

            default:
                DrawOverview(snapshot);
                break;
        }
    }

    private static void DrawOverview(EditorPresentationSnapshot snapshot)
    {
        DrawHeading(DevToolUiSettings.T("当前房间", "CURRENT ROOM"));
        DrawValue(DevToolUiSettings.T("房间", "Room"), snapshot?.RoomName);
    }
    private static void DrawHeading(string text)
    {
        ImGui.TextUnformatted(text);
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void DrawValue(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(Math.Max(170f, ImGui.GetWindowWidth() * 0.32f));
        ImGui.TextUnformatted(string.IsNullOrEmpty(value) ? "-" : value);
    }
}
