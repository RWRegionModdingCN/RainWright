using System;
using DryCycle.DevUI.DevTool.Core;
using ImGuiNET;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Shared top status surface for every rebuilt DevTool page.
///
/// Global UI mode, room/status title and language live on this single persistent row.
/// Page-owned controls are rendered by the tool strip surface below navigation, keeping the
/// hierarchy stable as Global status -> Tool navigation -> Page controls.
/// </summary>
internal static class DevToolTopStatusWindow
{
    private const float PreferredWidth = 780f;
    private const float ModeButtonWidth = 72f;
    private const float LanguageButtonWidth = 58f;
    private const float SegmentGap = 2f;

    private static int lastDrawFrame = -1;
    private static int rectFrame = -1;
    private static Num.Vector2 currentPosition;
    private static Num.Vector2 currentSize;

    private static bool chromeMouseGesture;

    internal static bool CaptureMouse()
    {
        if (!TryGetCurrentRect(out var position,out var size)) { chromeMouseGesture=false; return false; }
        var mouse=ImGui.GetIO().MousePos;
        bool inside=mouse.X>=position.X && mouse.Y>=position.Y && mouse.X<=position.X+size.X && mouse.Y<=position.Y+size.Y;
        bool down=ImGui.IsMouseDown(ImGuiMouseButton.Left);
        if (!down) chromeMouseGesture=false;
        else if (inside && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) chromeMouseGesture=true;
        // Do not steal a native drag which merely passes over the global switch bar.
        return chromeMouseGesture || inside && !down;
    }

    internal static bool TryGetCurrentRect(out Num.Vector2 position, out Num.Vector2 size)
    {
        if ((!EditorUiModeState.TopStatusHidden || EditorUiModeState.UseVanilla) &&
            rectFrame == ImGui.GetFrameCount() &&
            currentSize.X > 1f &&
            currentSize.Y > 1f)
        {
            position = currentPosition;
            size = currentSize;
            return true;
        }

        position = default;
        size = default;
        return false;
    }

    internal static void Draw(
        EditorPresentationSnapshot snapshot,
        IDevToolPageView page,
        Num.Vector2 display)
    {
        DrawCore(snapshot, display);
    }

    internal static void DrawGlobalOnly(
        EditorPresentationSnapshot snapshot,
        Num.Vector2 display)
    {
        DrawCore(snapshot, display);
    }

    private static void DrawCore(
        EditorPresentationSnapshot snapshot,
        Num.Vector2 display)
    {
        if (EditorUiModeState.TopStatusHidden && !EditorUiModeState.UseVanilla)
            return;
        if (display.X <= 1f || display.Y <= 1f)
            return;

        // A Vanilla -> New UI switch can make both shell paths execute in the same render frame.
        // Draw this shared window only once so the transition cannot submit the same ImGui window
        // twice with different contents.
        int frame = ImGui.GetFrameCount();
        if (lastDrawFrame == frame)
            return;
        lastDrawFrame = frame;

        float width = Math.Max(
            320f,
            Math.Min(
                PreferredWidth,
                Math.Max(320f, display.X - 16f)));

        ImGui.SetNextWindowPos(
            new Num.Vector2(
                Math.Max(8f, (display.X - width) * 0.5f),
                EditorUiModeState.UseVanilla ? Math.Max(8f, display.Y - 116f) : 6f),
            EditorUiModeState.UseVanilla ? ImGuiCond.FirstUseEver : ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(
            new Num.Vector2(width, 0f),
            new Num.Vector2(width, Math.Max(120f, display.Y - 12f)));
        ImGui.SetNextWindowBgAlpha(DevToolUiSettings.WindowAlpha);

        ImGui.PushStyleVar(
            ImGuiStyleVar.WindowPadding,
            new Num.Vector2(13f, 9f));
        ImGui.PushStyleVar(
            ImGuiStyleVar.ItemSpacing,
            new Num.Vector2(9f, 7f));

        ImGuiWindowFlags flags =
            ImGuiWindowFlags.NoDecoration |
            ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.NoFocusOnAppearing;
        if (EditorUiModeState.UseVanilla)
            flags = (flags & ~ImGuiWindowFlags.NoDecoration) | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;

        if (!ImGui.Begin(
                EditorUiModeState.UseVanilla ? "RainWright###DevToolVanillaSwitch" : "##DevToolTopStatus",
                flags))
        {
            ImGui.End();
            ImGui.PopStyleVar(2);
            return;
        }

        try
        {
            DrawGlobalRow(snapshot);
            currentPosition = ImGui.GetWindowPos();
            currentSize = ImGui.GetWindowSize();
            rectFrame = frame;
        }
        finally
        {
            ImGui.End();
            ImGui.PopStyleVar(2);
        }
    }

    private static void DrawGlobalRow(EditorPresentationSnapshot snapshot)
    {
        string room =
            snapshot?.Available == true &&
            !string.IsNullOrWhiteSpace(snapshot.RoomName)
                ? snapshot.RoomName
                : "-";

        string title;
        if (EditorUiModeState.UseVanilla)
        {
            title = room + " : Vanilla DevUI Active";
        }
        else if (snapshot?.Available != true)
        {
            title = DevToolUiSettings.T("DevTool 正在启动...", "DevTool starting...");
        }
        else
        {
            title = room + " : RainWright Active";
        }

        float modeWidth = ModeButtonWidth * 2f + SegmentGap;
        float languageWidth = LanguageButtonWidth * 2f + SegmentGap;
        float sideWidth = Math.Max(modeWidth, languageWidth) + 8f;
        float contentStartX = ImGui.GetCursorPosX();
        float available = ImGui.GetContentRegionAvail().X;

        // Use three real layout columns instead of drawing the centered title and then moving the
        // cursor backwards over the same row. Rewinding the cursor produced an overlapping-item
        // layout on the native ImGui backend and could terminate Rain World as soon as H opened
        // DevUI. Equal side columns keep the title centered against the whole top window.
        if (available >= sideWidth * 2f + 260f)
        {
            ImGui.Columns(3, "##DevToolTopGlobalColumns", false);
            try
            {
                ImGui.SetColumnWidth(0, sideWidth);
                ImGui.SetColumnWidth(1, Math.Max(260f, available - sideWidth * 2f));
                ImGui.SetColumnWidth(2, sideWidth);
    
                DrawModeSegment();
    
                ImGui.NextColumn();
                DevToolWidgets.CenteredPrimaryTitle(
                    title,
                    1.56f);
    
                ImGui.NextColumn();
                float rightOffset =
                    Math.Max(
                        0f,
                        ImGui.GetColumnWidth() - languageWidth - ImGui.GetStyle().ItemSpacing.X);
                if (rightOffset > 0f)
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + rightOffset);
                DrawLanguageSegment();
    
            }
            finally { ImGui.Columns(1); }
            return;
        }

        // Narrow displays stack the centered title over one compact controls row. This avoids
        // overlap while preserving access to both global switches.
        DevToolWidgets.CenteredPrimaryTitle(
            title,
            1.46f);
        ImGui.Spacing();

        DrawModeSegment();
        ImGui.SameLine();

        float languageX =
            contentStartX + available - languageWidth;
        if (languageX > ImGui.GetCursorPosX())
            ImGui.SetCursorPosX(languageX);
        DrawLanguageSegment();
    }

    private static void DrawModeSegment()
    {
        // Shared top-bar actions use a readable 1.14x control label scale.
        ImGui.SetWindowFontScale(1.14f);
        bool vanilla = EditorUiModeState.UseVanilla;

        if (DevToolWidgets.ActionButton(
                DevToolUiSettings.T("新 UI", "New UI"),
                "TopModeNewUi",
                vanilla ? DevToolButtonTone.Subtle : DevToolButtonTone.Primary,
                fullWidth: false,
                fixedWidth: ModeButtonWidth))
        {
            EditorUiModeState.SetVanilla(false);
        }

        ImGui.SameLine(0f, SegmentGap);
        if (DevToolWidgets.ActionButton(
                DevToolUiSettings.T("原版", "Vanilla"),
                "TopModeVanilla",
                vanilla ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle,
                fullWidth: false,
                fixedWidth: ModeButtonWidth))
        {
            EditorUiModeState.SetVanilla(true);
        }
        ImGui.SetWindowFontScale(1f);
    }

    private static void DrawLanguageSegment()
    {
        ImGui.SetWindowFontScale(1.14f);
        bool chinese =
            DevToolUiSettings.Language ==
            DevToolUiLanguage.Chinese;

        bool pushedChineseLabelFont =
            TryPushChineseLanguageLabelFont();

        bool chooseChinese =
            DevToolWidgets.ActionButton(
                "中文",
                "TopLanguageChinese",
                chinese ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle,
                fullWidth: false,
                fixedWidth: LanguageButtonWidth);

        if (pushedChineseLabelFont)
            ImGui.PopFont();

        if (chooseChinese)
            DevToolUiSettings.SetLanguage(DevToolUiLanguage.Chinese);

        ImGui.SameLine(0f, SegmentGap);
        if (DevToolWidgets.ActionButton(
                "EN",
                "TopLanguageEnglish",
                chinese ? DevToolButtonTone.Subtle : DevToolButtonTone.Primary,
                fullWidth: false,
                fixedWidth: LanguageButtonWidth))
        {
            DevToolUiSettings.SetLanguage(DevToolUiLanguage.English);
        }
        ImGui.SetWindowFontScale(1f);
    }

    private static unsafe bool TryPushChineseLanguageLabelFont()
    {
        if (DevToolUiSettings.IsChinese)
            return false;

        if (!DevToolFontCatalog.TryResolveRegisteredFace(
                DevToolFontCatalog.DefaultChineseFamily,
                DevToolUiSettings.DefaultChineseFontWeight,
                requireChinese: true,
                out ImFontPtr chineseFont,
                out _,
                out _,
                out _))
        {
            return false;
        }

        ImFontPtr currentFont =
            ImGui.GetFont();

        if (chineseFont.NativePtr == null ||
            currentFont.NativePtr == null ||
            chineseFont.FontSize <= 0.01f ||
            currentFont.FontSize <= 0.01f ||
            !DevToolFrontend.TryPushRegisteredFont(
                chineseFont,
                "top-bar Chinese language label"))
        {
            return false;
        }

        return true;
    }
}
