using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DryCycle.DevUI.DevTool.Palette;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal static partial class PaletteEditorView
{
    private static bool paintMode, hsv, reverseBlend, bothCycles;
    private static int brushRadius, paintBlend, channelBlend, sourceChannel = 16, targetChannel = 16, sourceNumber;
    private static float opacity = 1, blendStart = 1, blendEnd = 1;
    private static PaletteDocument strokeDocument;
    private static PaletteSnapshot strokeSource;
    private static bool[] strokeVisited;
    private static int lastStrokeX, lastStrokeY;
    private static readonly List<int> recentColors = new(8);
    private static readonly List<PaletteGradientStop> gradientStops = new();

    private static string ChannelName(int index)
    {
        var channel = PaletteChannels.Get(index);
        return (index >= PaletteChannels.PerCycle ? T("雨前 · ", "Rain · ") : T("正常 · ", "Normal · ")) + T(channel.Chinese, channel.Name);
    }
    private static void RememberColor(int value)
    {
        recentColors.Remove(value); recentColors.Insert(0, value);
        if (recentColors.Count > 8) recentColors.RemoveAt(8);
    }
    private static void FlushStroke()
    {
        if (strokeDocument == null) return;
        Send(strokeSource, "stroke", strokeDocument.ToHex());
        RememberColor(Pack(color)); strokeDocument = null; strokeSource = null; strokeVisited = null;
    }
    private static void HandleGridInput(PaletteSnapshot snapshot, Num.Vector2 origin, float label, float cell, float rowHeight)
    {
        if (strokeDocument != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) FlushStroke();
        if (!ImGui.IsItemHovered()) return;
        var relative = ImGui.GetIO().MousePos - origin;
        int x = (int)Math.Floor((relative.X - label) / cell), y = (int)Math.Floor(relative.Y / rowHeight) - 1 + cycle * 8;
        if ((uint)x >= 32 || y < cycle * 8 || y >= cycle * 8 + 8) return;
        if (paintMode)
        {
            ImGui.SetItemKeyOwner(ImGuiKey.MouseWheelY);
            float wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0) brushRadius = Math.Max(0, Math.Min(6, brushRadius + Math.Sign(wheel)));
        }
        bool sample = ImGui.IsMouseClicked(ImGuiMouseButton.Right);
        if (sample || !paintMode && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            FlushColor(); selectedX = x; selectedY = y; color = Unpack(snapshot.Pixels[y * 32 + x]);
            if (sample) RememberColor(Pack(color));
        }
        else if (paintMode && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            FlushColor();
            strokeDocument = new PaletteDocument(snapshot.Pixels); strokeSource = snapshot; strokeVisited = new bool[PaletteDocument.Count];
            selectedX = x; selectedY = y;
            lastStrokeX = x; lastStrokeY = y;
            PaletteEditing.Paint(strokeDocument, x, y, x, y, brushRadius, Pack(color), (PaletteBlendMode)paintBlend, opacity, strokeVisited);
        }
        if (strokeDocument != null && ImGui.IsMouseDown(ImGuiMouseButton.Left) && (x != lastStrokeX || y != lastStrokeY))
        {
            PaletteEditing.Paint(strokeDocument, lastStrokeX, lastStrokeY, x, y, brushRadius, Pack(color), (PaletteBlendMode)paintBlend, opacity, strokeVisited);
            lastStrokeX = x; lastStrokeY = y;
            selectedX = x; selectedY = y;
        }
    }
    private static void DrawTools(PaletteSnapshot snapshot)
    {
        int selectedChannel = PaletteChannels.At(selectedX, selectedY);
        ImGui.TextUnformatted((selectedChannel >= 0 ? ChannelName(selectedChannel) : T("辅助像素", "Auxiliary pixel")) + "  (" + selectedX + ", " + selectedY + ")");
        if (!ImGui.BeginTabBar("##PaletteTools")) return;
        if (ImGui.BeginTabItem(T("绘制", "Paint")))
        {
            if (ImGui.RadioButton(T("选择", "Select"), !paintMode)) { FlushColor(); paintMode = false; }
            ImGui.SameLine(); if (ImGui.RadioButton(T("笔刷", "Brush"), paintMode)) { FlushColor(); paintMode = true; }
            ImGui.SameLine(); ImGui.Checkbox("HSV", ref hsv);
            ImGui.SetNextItemWidth(Math.Min(520, ImGui.GetContentRegionAvail().X));
            if (ImGui.ColorEdit4(T("颜色 RGBA", "Color RGBA"), ref color, ImGuiColorEditFlags.AlphaBar | (hsv ? ImGuiColorEditFlags.DisplayHSV : ImGuiColorEditFlags.DisplayHex)))
            { if (!paintMode) { colorPending = true; pendingSource = snapshot; } }
            if (ImGui.IsItemDeactivatedAfterEdit()) { if (paintMode) RememberColor(Pack(color)); else FlushColor(); }
            if (paintMode)
            {
                ImGui.SetNextItemWidth(220); ImGui.SliderInt(T("笔刷半径", "Brush radius"), ref brushRadius, 0, 6);
                ImGui.SetNextItemWidth(220); ImGui.SliderFloat(T("强度", "Opacity"), ref opacity, 0, 1);
                BlendCombo(T("笔刷混合", "Brush blend"), ref paintBlend);
            }
            for (int i = 0; i < recentColors.Count; i++)
            {
                if (i > 0) ImGui.SameLine();
                int recent = recentColors[i];
                if (ImGui.ColorButton("##PaletteRecent" + i, Unpack(recent), ImGuiColorEditFlags.AlphaPreview, new Num.Vector2(24, 24)))
                { FlushColor(); color = Unpack(recent); }
            }
            if (ImGui.Button(T("应用颜色", "Apply color"))) { colorPending = false; FlushStroke(); Send(snapshot, "pixel", x: selectedX, y: selectedY, a: Pack(color)); RememberColor(Pack(color)); }
            ImGui.SameLine(); if (ImGui.Button(T("填充整行", "Fill row"))) { colorPending = false; FlushStroke(); Send(snapshot, "row", y: selectedY, a: Pack(color)); RememberColor(Pack(color)); }
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem(T("通道混合", "Channel blend"))) { DrawChannelTools(snapshot); ImGui.EndTabItem(); }
        if (ImGui.BeginTabItem(T("渐变", "Gradient"))) { DrawGradientTools(snapshot); ImGui.EndTabItem(); }
        ImGui.EndTabBar();
    }
    private static void BlendCombo(string label, ref int value)
    {
        string[] names = { T("正常", "Normal"), T("正片叠底", "Multiply"), T("滤色", "Screen"), T("叠加", "Overlay"), T("相加", "Add"), T("变暗", "Darken"), T("变亮", "Lighten") };
        ImGui.SetNextItemWidth(220); ImGui.Combo(label, ref value, names, names.Length);
    }
    private static void ChannelCombo(string label, ref int value)
    {
        ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
        if (!ImGui.BeginCombo(label, ChannelName(value))) return;
        for (int i = 0; i < PaletteChannels.All.Length; i++) if (ImGui.Selectable(ChannelName(i), i == value)) value = i;
        ImGui.EndCombo();
    }
    private static void DrawChannelTools(PaletteSnapshot snapshot)
    {
        if (ImGui.Button(T("当前色板设为源", "Use current as source"))) { FlushColor(); Send(snapshot, "source-current"); }
        ImGui.SameLine(); if (ImGui.Button(T("从所选 PNG 读取源", "Load source from selected PNG"))) { FlushColor(); Send(snapshot, "source-file", filename); }
        ImGui.SetNextItemWidth(120); ImGui.InputInt(T("游戏色板编号", "Game palette number"), ref sourceNumber); sourceNumber = Math.Max(0, Math.Min(999999, sourceNumber));
        ImGui.SameLine(); if (ImGui.Button(T("读取为源", "Load as source"))) { FlushColor(); Send(snapshot, "source-palette", x: sourceNumber); }
        if (snapshot.SourcePixels.Length != PaletteDocument.Count) return;
        ImGui.TextUnformatted(snapshot.SourceLabel);
        ChannelCombo(T("源通道", "Source channel"), ref sourceChannel);
        DrawChannelStrip(snapshot.SourcePixels, sourceChannel);
        ChannelCombo(T("目标通道", "Target channel"), ref targetChannel);
        BlendCombo(T("混合方式", "Blend mode"), ref channelBlend);
        ImGui.SetNextItemWidth(220); ImGui.SliderFloat(T("近端强度", "Near strength"), ref blendStart, 0, 1);
        ImGui.SetNextItemWidth(220); ImGui.SliderFloat(T("远端强度", "Far strength"), ref blendEnd, 0, 1);
        ImGui.Checkbox(T("反向渐变", "Reverse gradient"), ref reverseBlend);
        ImGui.SameLine(); ImGui.Checkbox(T("同时应用两个周期", "Apply to both cycles"), ref bothCycles);
        if (ImGui.Button(T("应用通道混合", "Apply channel blend")))
        {
            FlushColor();
            int other = targetChannel < PaletteChannels.PerCycle ? targetChannel + PaletteChannels.PerCycle : targetChannel - PaletteChannels.PerCycle;
            PaletteEditorRuntime.Enqueue(new PaletteCommand { Kind = "blend", Revision = snapshot.Revision, X = sourceChannel, Targets = bothCycles ? new[] { targetChannel, other } : new[] { targetChannel }, Color = channelBlend, Start = blendStart, End = blendEnd, Reverse = reverseBlend });
        }
    }
    private static void DrawChannelStrip(int[] pixels, int channelIndex)
    {
        var channel = PaletteChannels.Get(channelIndex); var start = ImGui.GetCursorScreenPos();
        float width = Math.Min(480, ImGui.GetContentRegionAvail().X), height = 20;
        ImGui.Dummy(new Num.Vector2(width, height)); var draw = ImGui.GetWindowDrawList();
        for (int i = 0; i < channel.Count; i++) draw.AddRectFilled(start + new Num.Vector2(width * i / channel.Count, 0), start + new Num.Vector2(width * (i + 1) / channel.Count, height), ToImGui(pixels[channel.Y * 32 + channel.X + i]));
    }
    private static void DrawGradientTools(PaletteSnapshot snapshot)
    {
        ImGui.SetNextItemWidth(Math.Min(520, ImGui.GetContentRegionAvail().X)); ImGui.ColorEdit4(T("起点颜色", "Start color"), ref color, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.DisplayHex);
        ImGui.SetNextItemWidth(Math.Min(520, ImGui.GetContentRegionAvail().X)); ImGui.ColorEdit4(T("终点颜色", "End color"), ref gradientEnd, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.DisplayHex);
        bool depthRow = selectedY % 8 >= 2;
        if (!depthRow) ImGui.BeginDisabled();
        if (ImGui.Button(T("生成此行深度渐变", "Generate depth gradient"))) { colorPending = false; FlushStroke(); Send(snapshot, "gradient", y: selectedY, a: Pack(color), b: Pack(gradientEnd)); }
        if (!depthRow) ImGui.EndDisabled();
        bool canAddStop = depthRow && selectedX < 30;
        if (!canAddStop) ImGui.BeginDisabled();
        if (ImGui.Button(T("将选中像素加入节点", "Add selected pixel as stop")) && selectedX < 30)
        {
            gradientStops.RemoveAll(stop => stop.X == selectedX);
            gradientStops.Add(new PaletteGradientStop(selectedX, snapshot.Pixels[selectedY * 32 + selectedX]));
            gradientStops.Sort((a, b) => a.X.CompareTo(b.X));
        }
        if (!canAddStop) ImGui.EndDisabled();
        ImGui.SameLine(); if (ImGui.Button(T("重置节点", "Reset stops"))) gradientStops.Clear();
        for (int i = 0; i < gradientStops.Count; i++)
        {
            var stop = gradientStops[i]; var value = Unpack(stop.Color);
            ImGui.PushID(i); ImGui.SetNextItemWidth(Math.Min(420, ImGui.GetContentRegionAvail().X));
            if (ImGui.ColorEdit4(T("深度 ", "Depth ") + stop.X, ref value, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.DisplayHex)) gradientStops[i] = new PaletteGradientStop(stop.X, Pack(value));
            ImGui.PopID();
        }
        bool canGenerate = gradientStops.Count >= 2 && depthRow;
        if (!canGenerate) ImGui.BeginDisabled();
        if (ImGui.Button(T("生成多点渐变", "Generate multi-stop gradient")))
        {
            FlushColor();
            string encoded = string.Join(";", gradientStops.Select(stop => stop.X.ToString(CultureInfo.InvariantCulture) + ":" + unchecked((uint)stop.Color).ToString("X8", CultureInfo.InvariantCulture)));
            Send(snapshot, "stops", encoded, y: selectedY);
        }
        if (!canGenerate) ImGui.EndDisabled();
    }
}
