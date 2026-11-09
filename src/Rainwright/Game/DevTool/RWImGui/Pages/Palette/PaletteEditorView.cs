using System;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Palette;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal sealed class PaletteDevToolPage : DevToolFrontendPageBase
{
    public override string Id => "palette";
    public override EditorToolMode Mode => EditorToolMode.Palette;
    public override int NavigationOrder => 800;
    public override string NavigationLabel => DevToolUiSettings.T("色板", "Palette");
    public override string NavigationTooltip => NavigationLabel;
    public override bool UsesDedicatedWorkspace => true;
    public override void DrawBrowser(EditorPresentationSnapshot snapshot) { }
    public override void DrawInspector(EditorPresentationSnapshot snapshot) { }
    public override void DrawWorkspace(EditorPresentationSnapshot snapshot, Num.Vector2 display) => PaletteEditorView.Draw(display);
    public override void DrawBackground(EditorPresentationSnapshot snapshot, Num.Vector2 display) { if (snapshot.FocusMode) DrawWorkspace(snapshot, display); }
    protected override void OnDeactivate() => PaletteEditorView.Close();
    protected override void OnReset() => PaletteEditorView.Close();
}

internal static partial class PaletteEditorView
{
    private static long observed = -1;
    private static int selectedX, selectedY, cycle;
    private static string filename = "palette.png";
    private static Num.Vector4 color, gradientEnd = Num.Vector4.One;
    private static bool colorPending, pickerOwned;
    private static PaletteSnapshot pendingSource;
    private static string T(string zh, string en) => DevToolUiSettings.T(zh, en);

    internal static void Close()
    {
        FlushColor();
        if (pickerOwned) InGameFolderPicker.Close();
        if (pickerOwned) NativeFolderPicker.Abandon();
        pickerOwned = false; observed = -1;
    }

    private static void Send(PaletteSnapshot snapshot, string kind, string value = null, int x = 0, int y = 0, int a = 0, int b = 0) =>
        PaletteEditorRuntime.Enqueue(new PaletteCommand { Kind = kind, Revision = snapshot.Revision, Value = value, X = x, Y = y, Color = a, Other = b });

    private static void FlushColor()
    {
        FlushStroke();
        if (!colorPending || pendingSource == null) return;
        Send(pendingSource, "pixel", x: selectedX, y: selectedY, a: Pack(color));
        RememberColor(Pack(color));
        colorPending = false;
    }

    internal static void Draw(Num.Vector2 display)
    {
        PaletteSnapshot snapshot = PaletteEditorRuntime.Current;
        if (pickerOwned && NativeFolderPicker.TryTake(out var folderResult))
        {
            pickerOwned = false;
            if (folderResult.Error != null) PaletteEditorRuntime.Enqueue(new PaletteCommand { Kind = "folder-error", Revision = snapshot.Revision, Failure = folderResult.Error });
            else if (!string.IsNullOrEmpty(folderResult.Path)) { FlushColor(); Send(snapshot, "directory", folderResult.Path); }
        }
        if (observed != snapshot.Revision && snapshot.Available || observed < 0)
        {
            observed = snapshot.Revision; filename = snapshot.Filename;
            colorPending = false;
            if (snapshot.Available && !paintMode) color = Unpack(snapshot.Pixels[selectedY * 32 + selectedX]);
        }
        DevToolWorkspaceLayout.GetCentralRect(display, out var position, out var size);
        ImGui.SetNextWindowPos(position, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(size, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);
        bool shown = ImGui.Begin(T("色板###RainWrightPalette", "Palette###RainWrightPalette"), ImGuiWindowFlags.NoCollapse);
        try
        {
            if (!shown) return;
            FloatingWindowSnap.TrackCurrentWindow("Palette");
            if (!string.IsNullOrEmpty(snapshot.Error)) ImGui.TextWrapped(snapshot.Error);
            if (!snapshot.Available)
            {
                if (ImGui.Button(T("重试", "Retry"))) Send(snapshot, "retry");
                return;
            }
            if (ImGui.Button(T("新建", "New"))) { FlushColor(); Send(snapshot, "new"); }
            ImGui.SameLine();
            if (ImGui.Button(T("读取当前房间色板", "Load room palette"))) { FlushColor(); Send(snapshot, "room"); }
            ImGui.SameLine();
            if (ImGui.Button(T("撤销", "Undo"))) { FlushColor(); Send(snapshot, "undo"); }
            ImGui.SameLine();
            if (ImGui.Button(T("重做", "Redo"))) { FlushColor(); Send(snapshot, "redo"); }
            ImGui.Separator();
            ImGui.TextUnformatted(T("导出文件夹", "Export folder"));
            bool folderBusy = NativeFolderPicker.Busy;
            if (folderBusy) ImGui.BeginDisabled();
            string folderLabel = string.IsNullOrEmpty(snapshot.Directory) ? T("点击选择文件夹…", "Click to choose a folder…") : snapshot.Directory;
            if (ImGui.Button(folderLabel + "###PaletteExportFolder", new Num.Vector2(Math.Max(120, ImGui.GetContentRegionAvail().X), 0)))
            {
                FlushColor();
                try
                {
                    if (NativeFolderPicker.Supported) NativeFolderPicker.Open(snapshot.Directory, T("选择色板导出文件夹", "Choose palette export folder"));
                    else InGameFolderPicker.Open(snapshot.Directory);
                    pickerOwned = true;
                }
                catch (Exception error) { PaletteEditorRuntime.Enqueue(new PaletteCommand { Kind = "folder-error", Revision = snapshot.Revision, Failure = error }); }
            }
            if (folderBusy) ImGui.EndDisabled();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(folderLabel);
            ImGui.SetNextItemWidth(Math.Max(140f, Math.Min(360f, ImGui.GetContentRegionAvail().X * .5f)));
            ImGui.InputText(T("文件名", "Filename"), ref filename, 200);
            bool nameChanged = ImGui.IsItemDeactivatedAfterEdit();
            if (nameChanged) { FlushColor(); Send(snapshot, "filename", filename); }
            ImGui.SetNextItemWidth(Math.Min(360f, ImGui.GetContentRegionAvail().X * .5f));
            if (ImGui.BeginCombo(T("文件夹内的 PNG", "PNGs in folder"), filename))
            {
                if (snapshot.Listing) ImGui.TextDisabled(T("读取中…", "Loading…"));
                using (DevToolListClipper clipper = new(snapshot.Files.Length))
                    while (clipper.Step(out int first, out int end)) for (int i = first; i < end; i++)
                    { string file = snapshot.Files[i]; if (ImGui.Selectable(file, file == filename)) { filename = file; Send(snapshot, "filename", filename); } }
                ImGui.EndCombo();
            }
            ImGui.SameLine(); if (ImGui.Button(T("刷新", "Refresh"))) Send(snapshot, "refresh");
            if (ImGui.Button(T("导入 PNG", "Import PNG")))
            { FlushColor(); Send(snapshot, "import", filename); }
            ImGui.SameLine();
            if (ImGui.Button(T("导出 PNG", "Export PNG")))
            { FlushColor(); Send(snapshot, "export", filename); }
            if (snapshot.DraftDirty)
            { ImGui.SameLine(); if (ImGui.Button(T("重试保存草稿", "Retry saving draft"))) Send(snapshot, "save"); }
            if (!string.IsNullOrEmpty(snapshot.ConfirmPath))
            {
                ImGui.TextWrapped(T("覆盖现有文件？ ", "Replace existing file? ") + snapshot.ConfirmPath);
                if (ImGui.Button(T("确认覆盖", "Replace"))) Send(snapshot, "overwrite");
                ImGui.SameLine(); if (ImGui.Button(T("取消", "Cancel"))) Send(snapshot, "cancel");
            }
            if (!string.IsNullOrEmpty(snapshot.Status)) ImGui.TextWrapped(snapshot.Status);
            ImGui.Separator();
            if (ImGui.RadioButton(T("正常周期", "Normal cycle"), cycle == 0)) { FlushColor(); cycle = 0; selectedY %= 8; color = Unpack(snapshot.Pixels[selectedY * 32 + selectedX]); }
            ImGui.SameLine();
            if (ImGui.RadioButton(T("雨前", "Before rain"), cycle == 1)) { FlushColor(); cycle = 1; selectedY = selectedY % 8 + 8; color = Unpack(snapshot.Pixels[selectedY * 32 + selectedX]); }
            ImGui.SameLine();
            if (ImGui.Button(cycle == 0 ? T("复制到雨前", "Copy to rain") : T("复制到正常周期", "Copy to normal"))) { FlushColor(); Send(snapshot, "cycle", x: cycle); }
            if (ImGui.BeginChild("##PalettePixels", new Num.Vector2(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
            {
                DrawGrid(snapshot);
                ImGui.Separator();
                DrawTools(snapshot);
            }
            ImGui.EndChild();
        }
        finally { ImGui.End(); }
        if (pickerOwned && !NativeFolderPicker.Supported && InGameFolderPicker.Draw(T("色板文件夹", "Palette folder"), out string directory))
        { pickerOwned = false; FlushColor(); Send(snapshot, "directory", directory); }
        if (pickerOwned && !NativeFolderPicker.Supported && !InGameFolderPicker.Visible) pickerOwned = false;
    }

    private static void DrawGrid(PaletteSnapshot snapshot)
    {
        float label = Math.Max(130f, ImGui.GetFontSize() * 7f);
        float cell = Math.Max(12f, Math.Min(28f, (ImGui.GetContentRegionAvail().X - label - 4f) / 32f));
        float rowHeight = Math.Max(cell, ImGui.GetTextLineHeightWithSpacing());
        Num.Vector2 origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##PaletteGrid", new Num.Vector2(label + cell * 32, rowHeight * 9));
        HandleGridInput(snapshot, origin, label, cell, rowHeight);
        var draw = ImGui.GetWindowDrawList();
        draw.AddText(origin + new Num.Vector2(label, 0), 0xffcccccc, T("近  ←  深度  →  远", "Near  ←  Depth  →  Far"));
        string[] labels = { T("参数", "Parameters"), T("污垢", "Grime"), T("光照 · 高光", "Sun · Highlight"), T("光照 · 中间色", "Sun · Middle"), T("光照 · 暗部", "Sun · Shadow"), T("阴影 · 高光", "Shade · Highlight"), T("阴影 · 中间色", "Shade · Middle"), T("阴影 · 暗部", "Shade · Shadow") };
        for (int row = 0; row < 8; row++)
        {
            int y = row + cycle * 8;
            draw.AddText(origin + new Num.Vector2(0, (row + 1) * rowHeight), 0xffdddddd, labels[row]);
            for (int x = 0; x < 32; x++)
            {
                var min = origin + new Num.Vector2(label + x * cell, (row + 1) * rowHeight);
                var max = min + new Num.Vector2(cell - 1, rowHeight - 1);
                int pixel = strokeDocument != null ? strokeDocument.Get(x, y) : colorPending && selectedX == x && selectedY == y ? Pack(color) : snapshot.Pixels[y * 32 + x];
                if ((uint)pixel >> 24 < 255) draw.AddRectFilled(min, max, ((x + y) & 1) == 0 ? 0xff999999 : 0xff444444);
                draw.AddRectFilled(min, max, ToImGui(pixel));
                if (selectedX == x && selectedY == y) draw.AddRect(min, max, 0xffffffff, 0, ImDrawFlags.None, 2);
            }
        }
        if (!ImGui.IsItemHovered()) return;
        var mouse = ImGui.GetIO().MousePos - origin;
        int hitX = (int)Math.Floor((mouse.X - label) / cell), hitY = (int)Math.Floor(mouse.Y / rowHeight) - 1 + cycle * 8;
        if (hitX < 0 || hitX >= 32 || hitY < cycle * 8 || hitY >= cycle * 8 + 8) return;
        int channel = PaletteChannels.At(hitX, hitY);
        ImGui.SetTooltip((channel >= 0 ? ChannelName(channel) + "  " : "") + "(" + hitX + ", " + hitY + ")  #" + unchecked((uint)snapshot.Pixels[hitY * 32 + hitX]).ToString("X8"));
    }

    private static Num.Vector4 Unpack(int value) => new(((uint)value >> 16 & 255) / 255f, ((uint)value >> 8 & 255) / 255f, (value & 255) / 255f, ((uint)value >> 24) / 255f);
    private static int Pack(Num.Vector4 value) => unchecked((int)((uint)Byte(value.W) << 24 | (uint)Byte(value.X) << 16 | (uint)Byte(value.Y) << 8 | (uint)Byte(value.Z)));
    private static byte Byte(float value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));
    private static uint ToImGui(int value) => (uint)value & 0xff00ff00u | ((uint)value & 0xff0000u) >> 16 | ((uint)value & 255) << 16;
}
