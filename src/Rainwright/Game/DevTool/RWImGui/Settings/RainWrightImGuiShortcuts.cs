using System;
using DryCycle.DevUI.DevTool.Core;
using ImGuiNET;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal static class RainWrightImGuiShortcuts
{
    internal static bool PlainDeletePressed()
    {
        var io = ImGui.GetIO();
        return !io.KeyCtrl && !io.KeyShift && !io.KeyAlt && !io.KeySuper && ImGui.IsKeyPressed(ImGuiKey.Delete, false);
    }
    internal static bool Pressed(string id)
    {
        string chord = RainWrightPersonalSettings.Chord(id);
        var io = ImGui.GetIO();
        if (io.KeyCtrl != chord.Contains("Ctrl+") || io.KeyShift != chord.Contains("Shift+") || io.KeyAlt != chord.Contains("Alt+")) return false;
        string key = chord.Substring(chord.LastIndexOf('+') + 1);
        return Enum.TryParse(key, true, out ImGuiKey code) && ImGui.IsKeyPressed(code, false);
    }
}
