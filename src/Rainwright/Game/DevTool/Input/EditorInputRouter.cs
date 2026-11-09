using System;
using DryCycle.DevUI.DevTool.Commands;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Sound;
using DryCycle.DevUI.DevTool.Triggers;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Input;

/// <summary>
/// Single input-arbitration point for the rebuilt DevTool. It coordinates optional ImGui
/// capture, optional legacy DevUI text owners, editor shortcuts, vanilla DevTool hotkeys,
/// remaining legacy world-space handles and gameplay input. ImGui itself is never referenced from
/// DryCycle.dll.
/// </summary>
public static class EditorInputRouter
{
    private static volatile bool frontendAttached;
    private static volatile bool wantsMouse;
    private static volatile bool wantsKeyboard;
    private static volatile bool wantsTextInput;
    private static volatile bool chromeWantsMouse;
    private static volatile int captureRevision = -1;
    private static bool enabled;
    private static readonly EditorBackendServiceLedger hookLeases = new();
    private static RainWorldGame vanillaHotkeysSuppressedGame;
    private static RainWorldGame capturedGame;
    private static int capturedUnityFrame = -1;

    public static bool FrontendAttached => frontendAttached;
    private static bool OwnsFrontendCapture => frontendAttached && !EditorUiModeState.UseVanilla &&
        !EditorUiModeState.OverlayHidden && captureRevision == EditorUiModeState.PresentationRevision;
    public static bool WantsMouse => OwnsFrontendCapture && wantsMouse;
    public static bool WantsKeyboard => OwnsFrontendCapture && wantsKeyboard;
    public static bool WantsTextInput => OwnsFrontendCapture && wantsTextInput;
    internal static bool ChromeWantsMouse => frontendAttached && chromeWantsMouse;
    public static void SetFrontendChromeCapture(bool mouse) => chromeWantsMouse = mouse;

    public static void SetFrontendAttached(bool attached)
    {
        frontendAttached = attached;
        if (!attached)
        {
            SetFrontendCapture(false, false, false);
            chromeWantsMouse = false;
        }
    }

    public static void SetFrontendCapture(bool mouse, bool keyboard, bool textInput)
    {
        wantsMouse = mouse;
        wantsKeyboard = keyboard;
        wantsTextInput = textInput;
        captureRevision = EditorUiModeState.PresentationRevision;
    }

    internal static LegacyMouseCaptureScope CaptureChromeMouse(DevInterface.DevUI owner) => new(owner);

    internal readonly struct LegacyMouseCaptureScope : IDisposable
    {
        private readonly DevInterface.DevUI owner;
        private readonly bool click, down;
        internal LegacyMouseCaptureScope(DevInterface.DevUI candidate)
        {
            owner = ChromeWantsMouse ? candidate : null;
            click = owner?.mouseClick ?? false;
            down = owner?.mouseDown ?? false;
            if (owner != null) { owner.mouseClick = false; owner.mouseDown = false; }
        }
        public void Dispose()
        {
            if (owner != null) { owner.mouseClick = click; owner.mouseDown = down; }
        }
    }

    internal static void Enable()
    {
        if (enabled) return;
        if (hookLeases.Count != 0)
            throw new InvalidOperationException(
                "DevTool input has pending cleanup; refusing duplicate HookGen installation.");

        try
        {
            // Vanilla RawUpdate IL patterns vary by Rain World build.
            // A failed optional add must also release its partial subscription.
            try
            {
                hookLeases.Start("RainWorldGame.RawUpdate IL",
                    () => IL.RainWorldGame.RawUpdate += RainWorldGame_RawUpdateIL,
                    () => IL.RainWorldGame.RawUpdate -= RainWorldGame_RawUpdateIL);
            }
            catch (Exception error)
            {
                hookLeases.Release("RainWorldGame.RawUpdate IL", null, ReportHookCleanup);
                EditorDiagnostics.Warning("DevTool optional RawUpdate IL gate unavailable: " + error);
                if (hookLeases.Count != 0)
                    throw new InvalidOperationException(
                        "Failed optional IL hook detach blocks input startup.", error);
            }

            hookLeases.Start("Player.checkInput",
                () => On.Player.checkInput += Player_checkInput,
                () => On.Player.checkInput -= Player_checkInput);
            hookLeases.Start("RainWorldGame.RawUpdate",
                () => On.RainWorldGame.RawUpdate += RainWorldGame_RawUpdate,
                () => On.RainWorldGame.RawUpdate -= RainWorldGame_RawUpdate);
            hookLeases.Start("RainWorldGame.Update",
                () => On.RainWorldGame.Update += RainWorldGame_Update,
                () => On.RainWorldGame.Update -= RainWorldGame_Update);
            hookLeases.Start("DevInterface.Handle.Update",
                () => On.DevInterface.Handle.Update += Handle_Update,
                () => On.DevInterface.Handle.Update -= Handle_Update);
            hookLeases.Start("DevInterface.MapPage.Update",
                () => On.DevInterface.MapPage.Update += MapPage_Update,
                () => On.DevInterface.MapPage.Update -= MapPage_Update);
            enabled = true;
        }
        catch
        {
            // Each external add is recorded before invocation, including an add
            // that throws after changing the invocation list.
            hookLeases.ReleaseAll(null, ReportHookCleanup);
            throw;
        }
    }

    internal static void Disable()
    {
        if (!enabled && hookLeases.Count == 0) return;
        if (!hookLeases.ReleaseAll(null, ReportHookCleanup))
            throw new InvalidOperationException(
                "DevTool input HookGen cleanup remains incomplete; retained for retry.");

        SetFrontendAttached(false);
        vanillaHotkeysSuppressedGame = null;
        capturedGame = null;
        capturedUnityFrame = -1;
        enabled = false;
    }

    private static void ReportHookCleanup(string name, Exception error) =>
        EditorDiagnostics.Warning("DevTool input hook cleanup failed for '" + name + "': " + error);

    internal static void UpdateShortcuts(EditorSession session)
    {
        if (session == null) return;

        // Vanilla presentation hides every rebuilt window, so a keyboard path is required to
        // return without leaving DevTools. This shortcut deliberately works in either mode.
        if (!WantsTextInput && RainWrightShortcutInput.Pressed("presentation"))
        {
            bool nextVanilla = !EditorUiModeState.UseVanilla;
            EditorUiModeState.SetVanilla(nextVanilla);
            EditorUiModeState.SetOverlayHidden(false);
            EditorShortcutFeedback.PublishCustom(
                nextVanilla ? "切换到原版 DevUI" : "切换到新 UI",
                nextVanilla ? "Vanilla DevUI" : "New UI",
                RainWrightPersonalSettings.Chord("presentation"),
                true,
                EditorShortcutFeedbackVisual.Toggle);
            SetFrontendCapture(false, false, false);
            return;
        }

        // Vanilla owns every editor action, text field and mod-added page in this mode.
        // Only the explicit presentation toggle above belongs to RainWright.
        if (EditorUiModeState.UseVanilla)
            return;

        if (!WantsTextInput && RainWrightShortcutInput.Pressed("top-status"))
        {
            EditorUiModeState.ToggleTopStatus();
            EditorShortcutFeedback.PublishCustom(
                EditorUiModeState.TopStatusHidden ? "隐藏顶部状态栏" : "显示顶部状态栏",
                EditorUiModeState.TopStatusHidden ? "Top status bar hidden" : "Top status bar shown",
                RainWrightPersonalSettings.Chord("top-status"), true, EditorShortcutFeedbackVisual.Toggle);
            SetFrontendCapture(false, false, false);
            return;
        }

        // Escape belongs to the game/Warp Menu. The editor only changes its own visibility and
        // immediately drops capture; it never consumes or synthesizes the Escape key itself.
        if (!EditorUiModeState.UseVanilla && global::UnityEngine.Input.GetKeyDown(KeyCode.Escape))
        {
            if (session.PlacementActive)
            {
                session.CancelPlacement();
                EditorShortcutFeedback.PublishCustom(
                    "已取消放置",
                    "Placement canceled",
                    "Esc",
                    true,
                    EditorShortcutFeedbackVisual.Cancel);
            }
            else
            {
                bool hiding = !EditorUiModeState.OverlayHidden;
                EditorUiModeState.ToggleOverlayHidden();
                EditorShortcutFeedback.PublishCustom(
                    hiding ? "隐藏 DevTool UI" : "显示 DevTool UI",
                    hiding ? "DevTool UI hidden" : "DevTool UI shown",
                    "Esc",
                    true,
                    EditorShortcutFeedbackVisual.Toggle);
            }

            SetFrontendCapture(false, false, false);
            capturedGame = null;
            capturedUnityFrame = -1;
            return;
        }

        bool textEditing = HasLocalTextFocus(session.Owner?.game) ||
                           WantsTextInput ||
                           session.LegacyTransactions.HasPendingTransaction;

        // Save / Undo / Redo are application-level document commands. They are deliberately
        // resolved before page visibility, Vanilla/New UI presentation and text-focus guards so
        // every DevTool view has exactly the same keyboard contract. Individual pages must never
        // re-implement these chords.
        if (RainWrightShortcutInput.Pressed("save"))
        {
            bool succeeded = EditorActions.Save(session);
            EditorShortcutFeedback.Publish(
                EditorShortcutFeedbackKind.Save,
                succeeded);
            return;
        }

        if (RainWrightShortcutInput.Pressed("undo") || RainWrightShortcutInput.Pressed("redo-shift"))
        {
            EditorShortcutFeedbackKind kind =
                RainWrightShortcutInput.Pressed("redo-shift")
                    ? EditorShortcutFeedbackKind.Redo
                    : EditorShortcutFeedbackKind.Undo;
            bool succeeded =
                RainWrightShortcutInput.Pressed("redo-shift")
                    ? EditorActions.Redo(session)
                    : EditorActions.Undo(session);
            EditorShortcutFeedback.Publish(
                kind,
                succeeded);
            return;
        }

        if (RainWrightShortcutInput.Pressed("redo-y"))
        {
            bool succeeded = EditorActions.Redo(session);
            EditorShortcutFeedback.Publish(
                EditorShortcutFeedbackKind.Redo,
                succeeded);
            return;
        }

        if (textEditing)
            return;

        // Vanilla mode restores the original editor interaction model. Do not let hidden
        // New-UI commands such as duplicate/delete/focus/browser shortcuts fire behind it.
        if (EditorUiModeState.UseVanilla || EditorUiModeState.OverlayHidden)
            return;

        if (RainWrightShortcutInput.Pressed("delete"))
        {
            HandleGlobalDeleteShortcut(session);
            return;
        }

        if (RainWrightShortcutInput.Pressed("objects-duplicate") && session.ToolMode == EditorToolMode.Objects)
        {
            bool succeeded = EditorActions.DuplicateSelection(session);
            EditorShortcutFeedback.PublishCustom(
                succeeded ? "已复制所选物件" : "没有可复制的物件",
                succeeded ? "Selection duplicated" : "Nothing to duplicate",
                RainWrightPersonalSettings.Chord("objects-duplicate"),
                succeeded,
                succeeded ? EditorShortcutFeedbackVisual.Duplicate : EditorShortcutFeedbackVisual.Warning);
        }
        else if (RainWrightShortcutInput.PlainDeletePressed() &&
                 session.ToolMode == EditorToolMode.Objects)
        {
            bool succeeded = EditorActions.DeleteSelection(session);
            EditorShortcutFeedback.PublishCustom(
                succeeded ? "已删除所选物件" : "没有可删除的物件",
                succeeded ? "Selection deleted" : "Nothing to delete",
                "Delete",
                succeeded,
                succeeded ? EditorShortcutFeedbackVisual.Delete : EditorShortcutFeedbackVisual.Warning);
        }
        else if (RainWrightShortcutInput.PlainDeletePressed() &&
                 session.ToolMode == EditorToolMode.Triggers)
        {
            TriggerEditorState triggerState = TriggerEditorStateHub.Get(session);
            int selectedIndex = triggerState?.SelectedIndex ?? -1;
            bool succeeded =
                session.RoomSettings?.triggers != null &&
                selectedIndex >= 0 &&
                selectedIndex < session.RoomSettings.triggers.Count;

            if (succeeded)
            {
                TriggerEditorCommandQueue.Enqueue(
                    new TriggerEditorCommand(
                        TriggerEditorCommandKind.Delete,
                        selectedIndex));
            }

            EditorShortcutFeedback.PublishCustom(
                succeeded ? "已删除触发器" : "没有可删除的触发器",
                succeeded ? "Trigger deleted" : "No trigger selected",
                "Delete",
                succeeded,
                succeeded ? EditorShortcutFeedbackVisual.Delete : EditorShortcutFeedbackVisual.Warning);
        }
        else if (RainWrightShortcutInput.Pressed("browser"))
        {
            bool opening = !session.BrowserOpen;
            session.ToggleBrowser();
            EditorShortcutFeedback.PublishCustom(
                opening ? "显示浏览器" : "隐藏浏览器",
                opening ? "Browser shown" : "Browser hidden",
                RainWrightPersonalSettings.Chord("browser"),
                true,
                EditorShortcutFeedbackVisual.Toggle);
        }
        else if (RainWrightShortcutInput.Pressed("inspector"))
        {
            bool opening = !session.InspectorOpen;
            session.ToggleInspector();
            EditorShortcutFeedback.PublishCustom(
                opening ? "显示检查器" : "隐藏检查器",
                opening ? "Inspector shown" : "Inspector hidden",
                RainWrightPersonalSettings.Chord("inspector"),
                true,
                EditorShortcutFeedbackVisual.Toggle);
        }
        else if (RainWrightShortcutInput.Pressed("focus"))
        {
            bool entering = !session.FocusMode;
            session.ToggleFocusMode();
            EditorShortcutFeedback.PublishCustom(
                entering ? "进入专注模式" : "退出专注模式",
                entering ? "Entered Focus mode" : "Exited Focus mode",
                RainWrightPersonalSettings.Chord("focus"),
                true,
                EditorShortcutFeedbackVisual.Toggle);
        }
    }

    private static void HandleGlobalDeleteShortcut(EditorSession session)
    {
        if (session == null)
            return;

        // X is a DevTool-wide delete command. Reserve the gameplay key immediately, then dispatch
        // according to the active workspace. Map subviews keep their presentation-only selection
        // locally, so they consume the same X edge in the active frontend.
        MarkKeyboardCaptured(session.Owner?.game);

        switch (session.ToolMode)
        {
            case EditorToolMode.Objects:
            {
                bool succeeded =
                    EditorActions.DeleteSelection(session);
                EditorShortcutFeedback.PublishCustom(
                    succeeded ? "已删除所选物件" : "没有可删除的物件",
                    succeeded ? "Selection deleted" : "Nothing to delete",
                    RainWrightPersonalSettings.Chord("delete"),
                    succeeded,
                    succeeded ? EditorShortcutFeedbackVisual.Delete : EditorShortcutFeedbackVisual.Warning);
                return;
            }

            case EditorToolMode.Sound:
            {
                SoundEditorState state =
                    SoundEditorStateHub.Get(session);
                int index =
                    state?.SelectedIndex ?? -1;
                bool succeeded =
                    session.RoomSettings?.ambientSounds != null &&
                    index >= 0 &&
                    index < session.RoomSettings.ambientSounds.Count &&
                    session.RoomSettings.ambientSounds[index]?.inherited != true;

                if (succeeded)
                {
                    SoundEditorCommandQueue.Enqueue(
                        new SoundEditorCommand(
                            SoundEditorCommandKind.Delete,
                            index));
                }

                EditorShortcutFeedback.PublishCustom(
                    succeeded ? "已删除声音" : "没有可删除的声音",
                    succeeded ? "Sound deleted" : "No deletable sound selected",
                    RainWrightPersonalSettings.Chord("delete"),
                    succeeded,
                    succeeded ? EditorShortcutFeedbackVisual.Delete : EditorShortcutFeedbackVisual.Warning);
                return;
            }

            case EditorToolMode.Triggers:
            {
                TriggerEditorState state =
                    TriggerEditorStateHub.Get(session);
                int index =
                    state?.SelectedIndex ?? -1;
                bool succeeded =
                    session.RoomSettings?.triggers != null &&
                    index >= 0 &&
                    index < session.RoomSettings.triggers.Count;

                if (succeeded)
                {
                    TriggerEditorCommandQueue.Enqueue(
                        new TriggerEditorCommand(
                            TriggerEditorCommandKind.Delete,
                            index));
                }

                EditorShortcutFeedback.PublishCustom(
                    succeeded ? "已删除触发器" : "没有可删除的触发器",
                    succeeded ? "Trigger deleted" : "No trigger selected",
                    RainWrightPersonalSettings.Chord("delete"),
                    succeeded,
                    succeeded ? EditorShortcutFeedbackVisual.Delete : EditorShortcutFeedbackVisual.Warning);
                return;
            }

            case EditorToolMode.Map:
                // Map subviews own presentation-only selections (World Map links, Cartography
                // elements), so their active frontend consumes the same X edge directly.
                return;

            default:
                EditorShortcutFeedback.PublishCustom(
                    "当前视图没有可删除内容",
                    "Nothing deletable in this view",
                    RainWrightPersonalSettings.Chord("delete"),
                    false,
                    EditorShortcutFeedbackVisual.Warning);
                return;
        }
    }

    private static void RainWorldGame_RawUpdateIL(ILContext il)
    {
        // Gate only vanilla's DevTools hotkey block. Changing game.devToolsActive while RawUpdate
        // calls the simulation Update makes the editor lifetime monitor retire the live session.
        // The branch exits at the O-key toggle, whose edge latch is handled by the wrapper below.
        ILCursor cursor = new(il);
        if (!cursor.TryGotoNext(MoveType.After,
                instruction => instruction.MatchLdfld<RainWorldGame>(nameof(RainWorldGame.devToolsActive)),
                instruction => instruction.MatchBrfalse(out ILLabel target) && target.Target.MatchLdstr("o")))
            throw new InvalidOperationException("DevTool input routing could not locate RainWorldGame.RawUpdate's DevTools hotkey block.");

        cursor.Index--;
        cursor.Emit(OpCodes.Ldarg_0);
        cursor.EmitDelegate<Func<bool, RainWorldGame, bool>>(AllowVanillaDevToolsHotkeys);
    }

    private static bool AllowVanillaDevToolsHotkeys(bool active, RainWorldGame game) =>
        active && (EditorUiModeState.UseVanilla || !ReferenceEquals(vanillaHotkeysSuppressedGame, game));

    private static bool SuppressesRemappedKeys(RainWorldGame game) => game?.devToolsActive == true && game.devUI != null &&
        (RainWrightShortcutInput.HasHeldDocumentChord() ||
         frontendAttached && !EditorUiModeState.UseVanilla && !EditorUiModeState.OverlayHidden && RainWrightShortcutInput.HasHeldViewChord());

    internal static bool CapturesNativeDevUiKeys(RainWorldGame game) => !EditorUiModeState.UseVanilla &&
        (HasTextKeyboardOwner(game) || ShouldSuppressVanillaFastForward(game) || SuppressesRemappedKeys(game));

    private static void RainWorldGame_RawUpdate(
        On.RainWorldGame.orig_RawUpdate orig,
        RainWorldGame self,
        float dt)
    {
        if (EditorUiModeState.UseVanilla) { orig(self, dt); return; }
        bool textKeyboardOwner = HasTextKeyboardOwner(self);
        bool suppressVanillaFastForward = ShouldSuppressVanillaFastForward(self);
        bool suppressRemappedKeys = SuppressesRemappedKeys(self);

        // Vanilla RainWorldGame.RawUpdate interprets any held S as the DevTools 400 FPS shortcut.
        // Ctrl/Command+S belongs to the rebuilt UI's save command, so bypass only that vanilla
        // DevTools shortcut path while keeping plain S fast-forward unchanged.
        if (!textKeyboardOwner && !suppressVanillaFastForward && !suppressRemappedKeys)
        {
            orig(self, dt);
            return;
        }

        if (textKeyboardOwner)
            MarkKeyboardCaptured(self);

        DevInterface.DevUI focusedDevUi = self.devUI;

        self.mDown = global::UnityEngine.Input.GetKey(KeyCode.M);
        self.hDown = global::UnityEngine.Input.GetKey(KeyCode.H);
        self.pDown = global::UnityEngine.Input.GetKey(KeyCode.P);
        self.kDown = global::UnityEngine.Input.GetKey(KeyCode.K);
        self.oDown = global::UnityEngine.Input.GetKey(KeyCode.O);

        if (suppressVanillaFastForward || suppressRemappedKeys && global::UnityEngine.Input.GetKey(KeyCode.S))
            self.framesPerSecond = 40;

        // Keep the wrapper and IL gate on the same capture decision even if the render thread
        // publishes new focus flags during the simulation update. This scope owns input only.
        RainWorldGame previousSuppressedGame = vanillaHotkeysSuppressedGame;
        vanillaHotkeysSuppressedGame = self;
        try
        {
            orig(self, dt);
        }
        finally
        {
            vanillaHotkeysSuppressedGame = previousSuppressedGame;
        }

        if (self.devToolsActive && focusedDevUi != null && ReferenceEquals(self.devUI, focusedDevUi))
            focusedDevUi.Update();
    }

    private static void RainWorldGame_Update(On.RainWorldGame.orig_Update orig, RainWorldGame self)
    {
        if (EditorUiModeState.UseVanilla) { orig(self); return; }
        bool captured = IsKeyboardCapturedThisFrame(self) || HasKeyboardOwner(self);
        if (captured)
        {
            self.lastRestartButton = global::UnityEngine.Input.GetKey(KeyCode.R);
            self.lastPauseButton = true;
        }

        try
        {
            orig(self);
        }
        finally
        {
            if (ReferenceEquals(capturedGame, self) && capturedUnityFrame == Time.frameCount)
            {
                capturedGame = null;
                capturedUnityFrame = -1;
            }
        }
    }

    /// <summary>
    /// Prevents a click consumed by the overlay from also beginning a drag on a remaining legacy
    /// DevInterface handle underneath it. Existing drags are always allowed to finish. Built-in
    /// Sound/Trigger handles no longer receive special treatment because native gizmos own those
    /// workspaces and their vanilla Panel/Handle trees are retired.
    /// </summary>
    private static void Handle_Update(On.DevInterface.Handle.orig_Update orig, DevInterface.Handle self)
    {
        if (self?.owner == null || self.dragged)
        {
            orig(self);
            return;
        }

        EditorSession session = DevToolSessionHub.Current;
        bool ownsThisUi = session != null && ReferenceEquals(session.Owner, self.owner);
        bool newUiVisible = !EditorUiModeState.UseVanilla && !EditorUiModeState.OverlayHidden;
        bool newUiPlacementOwnsMouse = newUiVisible && session?.PlacementActive == true;
        bool frontendOwnsMouse = newUiVisible && frontendAttached && WantsMouse;
        bool blockNewDrag = ownsThisUi && self.owner.game?.devToolsActive == true &&
                            (newUiPlacementOwnsMouse || frontendOwnsMouse);
        if (!blockNewDrag)
        {
            orig(self);
            return;
        }

        bool mouseClick = self.owner.mouseClick;
        self.owner.mouseClick = false;
        try
        {
            orig(self);
        }
        finally
        {
            self.owner.mouseClick = mouseClick;
        }
    }

    /// <summary>
    /// The rebuilt Map workspace must keep the vanilla MapPage alive because its world data,
    /// save path and mod hooks remain authoritative. However, its hidden controls must not
    /// react behind the ImGui canvas. Suppress only the DevUI mouse state for the duration of
    /// the vanilla MapPage update; no RoomPanel position or map data is modified.
    /// </summary>
    private static void MapPage_Update(On.DevInterface.MapPage.orig_Update orig, DevInterface.MapPage self)
    {
        using var chromeMouse = CaptureChromeMouse(self?.owner);
        if (!ShouldBlockLegacyMapMouse(self))
        {
            orig(self);
            return;
        }

        DevInterface.DevUI owner = self.owner;
        bool mouseClick = owner.mouseClick;
        bool mouseDown = owner.mouseDown;
        owner.mouseClick = false;
        owner.mouseDown = false;
        try
        {
            orig(self);
        }
        finally
        {
            owner.mouseClick = mouseClick;
            owner.mouseDown = mouseDown;
        }
    }

    private static bool ShouldBlockLegacyMapMouse(DevInterface.MapPage page)
    {
        if (!frontendAttached || EditorUiModeState.UseVanilla || page?.owner == null)
            return false;

        EditorSession session = DevToolSessionHub.Current;
        return session != null &&
               session.ToolMode == EditorToolMode.Map &&
               !session.LegacyUiVisible &&
               ReferenceEquals(session.Owner, page.owner) &&
               page.owner.game?.devToolsActive == true;
    }

    private static void Player_checkInput(On.Player.orig_checkInput orig, Player self)
    {
        orig(self);
        if (EditorUiModeState.UseVanilla) return;
        if (self.input == null || self.input.Length == 0) return;

        RainWorldGame game = self?.room?.game;
        if (HasKeyboardOwner(game) || IsKeyboardCapturedThisFrame(game))
        {
            Player.InputPackage focusedInput = self.input[0];
            NeutralizeGameplayInput(ref focusedInput);
            self.input[0] = focusedInput;
            self.mapInput = focusedInput;
            return;
        }

        if (!ShouldFilterEditorShortcuts(self)) return;

        bool reserveZ = !EditorShortcutGameplayPolicy.AllowZ && global::UnityEngine.Input.GetKey(KeyCode.Z);
        bool reserveS = !EditorShortcutGameplayPolicy.AllowS && global::UnityEngine.Input.GetKey(KeyCode.S);
        bool reserveY = !EditorShortcutGameplayPolicy.AllowY && global::UnityEngine.Input.GetKey(KeyCode.Y);
        if (!reserveZ && !reserveS && !reserveY && !RainWrightShortcutInput.HasHeldDocumentChord()) return;

        int playerNumber = self.playerState?.playerNumber ?? 0;
        if (ModManager.ChallengeModule &&
            self.abstractCreature?.world?.game?.IsArenaSession == true &&
            self.abstractCreature.world.game.GetArenaGameSession.chMeta != null)
        {
            playerNumber = 0;
        }

        Options.ControlSetup[] allControls = self.room.game.rainWorld.options.controls;
        if (allControls == null || playerNumber < 0 || playerNumber >= allControls.Length) return;

        Options.ControlSetup controls = allControls[playerNumber];
        if (controls == null) return;

        Player.InputPackage input = self.input[0];

        if (IsReserved(controls.KeyCodeFromAction(0, 0), reserveZ, reserveS, reserveY)) input.jmp = false;
        if (IsReserved(controls.KeyCodeFromAction(4, 0), reserveZ, reserveS, reserveY)) input.thrw = false;
        if (IsReserved(controls.KeyCodeFromAction(3, 0), reserveZ, reserveS, reserveY)) input.pckp = false;
        if (IsReserved(controls.KeyCodeFromAction(11, 0), reserveZ, reserveS, reserveY)) input.mp = false;
        if (IsReserved(controls.KeyCodeFromAction(34, 0), reserveZ, reserveS, reserveY)) input.spec = false;

        bool blockLeft = IsReserved(controls.KeyCodeFromAction(1, 0, axisPositive: false), reserveZ, reserveS, reserveY);
        bool blockRight = IsReserved(controls.KeyCodeFromAction(1, 0, axisPositive: true), reserveZ, reserveS, reserveY);
        bool blockDown = IsReserved(controls.KeyCodeFromAction(2, 0, axisPositive: false), reserveZ, reserveS, reserveY);
        bool blockUp = IsReserved(controls.KeyCodeFromAction(2, 0, axisPositive: true), reserveZ, reserveS, reserveY);

        if ((input.x < 0 && blockLeft) || (input.x > 0 && blockRight))
        {
            input.x = 0;
            input.analogueDir.x = 0f;
        }

        if ((input.y < 0 && blockDown) || (input.y > 0 && blockUp))
        {
            input.y = 0;
            input.analogueDir.y = 0f;
        }

        RecalculateDownDiagonal(ref input);
        self.input[0] = input;
        self.mapInput = input;
    }

    private static bool HasTextKeyboardOwner(RainWorldGame game)
    {
        if (game == null || game.devUI == null || !game.devToolsActive) return false;
        bool frontendVisible = frontendAttached && !EditorUiModeState.UseVanilla && !EditorUiModeState.OverlayHidden;
        return HasLocalTextFocus(game) || (frontendVisible && WantsTextInput);
    }

    private static bool ShouldSuppressVanillaFastForward(RainWorldGame game)
    {
        if (game == null || game.devUI == null || !game.devToolsActive) return false;
        if (!frontendAttached || EditorUiModeState.UseVanilla || EditorUiModeState.OverlayHidden) return false;

        bool ctrlOrCommand =
            global::UnityEngine.Input.GetKey(KeyCode.LeftControl) ||
            global::UnityEngine.Input.GetKey(KeyCode.RightControl) ||
            global::UnityEngine.Input.GetKey(KeyCode.LeftCommand) ||
            global::UnityEngine.Input.GetKey(KeyCode.RightCommand);

        return ctrlOrCommand && global::UnityEngine.Input.GetKey(KeyCode.S);
    }

    private static bool HasKeyboardOwner(RainWorldGame game)
    {
        if (game == null || game.devUI == null || !game.devToolsActive) return false;
        bool frontendVisible = frontendAttached && !EditorUiModeState.UseVanilla && !EditorUiModeState.OverlayHidden;
        return HasLocalTextFocus(game) || (frontendVisible && (WantsKeyboard || WantsTextInput));
    }

    private static bool HasLocalTextFocus(RainWorldGame game)
    {
        return game != null && game.devUI != null &&
               LegacyTextInputSources.HasTextFocus;
    }

    private static void MarkKeyboardCaptured(RainWorldGame game)
    {
        capturedGame = game;
        capturedUnityFrame = Time.frameCount;
    }

    private static bool IsKeyboardCapturedThisFrame(RainWorldGame game)
    {
        return game != null && ReferenceEquals(capturedGame, game) && capturedUnityFrame == Time.frameCount;
    }

    private static bool ShouldFilterEditorShortcuts(Player player)
    {
        RainWorldGame game = player?.room?.game;
        if (game == null || !game.devToolsActive || game.devUI == null) return false;
        return RainWrightShortcutInput.HasHeldDocumentChord() || global::UnityEngine.Input.GetKey(KeyCode.LeftControl) || global::UnityEngine.Input.GetKey(KeyCode.RightControl) ||
               global::UnityEngine.Input.GetKey(KeyCode.LeftCommand) || global::UnityEngine.Input.GetKey(KeyCode.RightCommand);
    }

    private static void NeutralizeGameplayInput(ref Player.InputPackage input)
    {
        input.x = 0;
        input.y = 0;
        input.jmp = false;
        input.thrw = false;
        input.pckp = false;
        input.mp = false;
        input.spec = false;
        input.crouchToggle = false;
        input.analogueDir = Vector2.zero;
        input.downDiagonal = 0;
    }

    private static bool IsReserved(KeyCode mappedKey, bool reserveZ, bool reserveS, bool reserveY)
    {
        return RainWrightShortcutInput.ReservesKey(mappedKey) || (reserveZ && mappedKey == KeyCode.Z) ||
               (reserveS && mappedKey == KeyCode.S) ||
               (reserveY && mappedKey == KeyCode.Y);
    }

    private static void RecalculateDownDiagonal(ref Player.InputPackage input)
    {
        bool down = input.y < 0 || input.analogueDir.y < -0.05f;
        if (!down)
        {
            input.downDiagonal = 0;
            return;
        }

        if (input.x < 0 || input.analogueDir.x < -0.05f)
            input.downDiagonal = -1;
        else if (input.x > 0 || input.analogueDir.x > 0.05f)
            input.downDiagonal = 1;
        else
            input.downDiagonal = 0;
    }
}
