using System;
using System.Collections.Generic;
using System.Security;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Personalization;
using ImGuiNET;
using RWIMGUI.API;
using RainWright.Remix;

namespace DryCycle.DevUI.DevTool.RWImGui;

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rwimgui", "1.12.0")]
public sealed class BridgePlugin : BaseUnityPlugin
{
    private readonly DevToolRetainedViewLifecycle retainedViewLifecycle = new();
    private readonly FrontendHookLedger lifecycleHooks =
        new((name, error) => EditorStartupDiagnostics.Failure("BridgePlugin/HookCleanup/" + name, error));
    // Optional frontend modules can also install HookGen callbacks, caches and
    // long-lived event handlers. Their cleanup must remain owned if it fails.
    private readonly FrontendHookLedger optionalFeatures =
        new((name, error) => EditorStartupDiagnostics.Failure("BridgePlugin/OptionalCleanup/" + name, error));
    public const string PluginId = "rainwright";
    public const string PluginName = "RainWright";
    public const string PluginVersion = "0.1.14";

    private static ManualLogSource log;
    // Native AddAlwaysCallback may publish a callback before throwing. The
    // lifecycle entry must exist *before* calling RWImGUI, so a failed remove
    // retains owner and cannot cause an unsafe repeated Add on the next frame.
    private static readonly FrontendHookLedger alwaysCallbackLease =
        new((name, error) => EditorStartupDiagnostics.Failure(
            "BridgePlugin/CallbackCleanup/" + name, error));
    private static bool callbackRegistrationAllowed;
    private static float nextCallbackRegistrationAttemptAt;
    private static int callbackRegistrationFailureLogged;
    private bool bridgeEnabled;
    private readonly FrontendSessionVisibilityPolicy<RainWorldGame> sessionVisibility = new();
    private bool applicationFocused;
    private readonly CreatureCatalogRuntimeLease creatureCatalog = new();
    private bool frontendInputAttached;
    private bool retainedFrontendActive;
    private bool mapFrontendWorkFaulted;
    private bool creatureCatalogWorkFaulted;
    private bool timelineIconWorkFaulted;
    private bool legacyVisualGuardFaulted;
    private bool retainedLifecycleFaulted;

    private void OnEnable()
    {
        if (!BridgePluginOwnership.TryClaim(this))
        {
            EditorStartupDiagnostics.Marker("BridgePlugin.OnEnable", "SKIP-DUPLICATE-OWNER");
            return;
        }
        // A partial creature catalog shutdown can fail after the frontend
        // callback/hook registrations were already removed. It is still an
        // owned runtime and must block a second plugin instance from starting
        // another cache writer. Retry its cleanup on this owner's next Enable.
        if (!bridgeEnabled && EditorBackendLifecycle.PendingCleanup)
            SafeFrontendCleanup("pending backend cleanup", EditorBackendLifecycle.Disable);
        if (!bridgeEnabled && creatureCatalog.PendingCleanup)
            SafeFrontendCleanup("pending creature catalog retry",
                () => creatureCatalog.Stop(WorldCreatureCatalogPicker.Shutdown));
        if (!bridgeEnabled && DevToolFontAtlasIntegration.PendingCleanup)
            SafeFrontendCleanup("pending font atlas detour retry",
                DevToolFontAtlasIntegration.Disable);
        if (!bridgeEnabled && optionalFeatures.HasActiveRegistrations)
            SafeFrontendCleanup("pending optional frontend cleanup retry", () =>
            {
                if (!optionalFeatures.ReleaseAll())
                    throw new InvalidOperationException(
                        "Optional frontend cleanup remains incomplete; refusing duplicate startup.");
            });

        if (bridgeEnabled || lifecycleHooks.HasActiveRegistrations ||
            optionalFeatures.HasActiveRegistrations ||
            alwaysCallbackLease.HasActiveRegistrations ||
            DevToolFontAtlasIntegration.PendingCleanup || creatureCatalog.PendingCleanup || EditorBackendLifecycle.PendingCleanup)
        {
            EditorStartupDiagnostics.Marker("BridgePlugin.OnEnable", "SKIP-ALREADY-ACTIVE",
                "an existing frontend hook, optional module, callback, or catalog cleanup has not been released");
            return;
        }

        log = Logger;
        bridgeEnabled = false;
        EditorStartupDiagnostics.Marker("BridgePlugin.OnEnable", "ENTER");

        try
        {
            if (!EditorBackendLifecycle.EnableCore())
                throw new InvalidOperationException("RainWright editor backend could not start; see original startup exception.");
            applicationFocused = UnityEngine.Application.isFocused;
            sessionVisibility.Reset(applicationFocused);
            frontendInputAttached = false;
            retainedFrontendActive = false;
            mapFrontendWorkFaulted = false;
            creatureCatalogWorkFaulted = false;
            timelineIconWorkFaulted = false;
            legacyVisualGuardFaulted = false;
            retainedLifecycleFaulted = false;
            callbackRegistrationAllowed = false;
            nextCallbackRegistrationAttemptAt = 0f;
            Interlocked.Exchange(ref callbackRegistrationFailureLogged, 0);

            EditorStartupDiagnostics.Step(
                "BridgePlugin/EditorUiModeState.SetOverlayHidden",
                () => EditorUiModeState.SetOverlayHidden(false));
            EditorStartupDiagnostics.Step(
                "BridgePlugin/EditorInputRouter.SetFrontendAttached",
                () => EditorInputRouter.SetFrontendAttached(false));
            EditorStartupDiagnostics.Step(
                "BridgePlugin/DevToolFrontend.SetLogger",
                () => DevToolFrontend.SetLogger(Logger));

            EditorStartupDiagnostics.Step(
                "BridgePlugin/DevToolUserSettingsStore.Load",
                () =>
                {
                    DevToolUserSettingsStore.Load(Logger);
                    RainWrightWorkspaceRuntime.Load();
                    FloatingWindowSnap.ReloadPersistedLayout();
                    DevToolOverlay.ReloadPersistedPresentationState();
                });

            // RWImGUI owns one shared native font atlas. The HarmonyOS CJK face must be inserted
            // before RWImGUI's first DX11 backend frame uploads that atlas; waiting until the user
            // switches the consumer context to Chinese is already too late.
            EditorStartupDiagnostics.Step(
                "BridgePlugin/DevToolFontAtlasIntegration.Enable",
                () => DevToolFontAtlasIntegration.Enable(Logger));

            EditorStartupDiagnostics.Step(
                "BridgePlugin/DevToolFrontend.ResetNativeReadiness",
                DevToolFrontend.ResetNativeReadinessFromMainThread);
            EditorStartupDiagnostics.Step(
                "BridgePlugin/DevToolFrontend.SetApplicationFocused",
                () => DevToolFrontend.SetApplicationFocusedFromMainThread(applicationFocused));

            // Only lifecycle wiring belongs to the fatal shell transaction. Map helpers, inspectors,
            // caches and diagnostics are optional and are enabled independently below.
            EditorStartupDiagnostics.Step(
                "BridgePlugin/Hook RainWorld.Start",
                () => lifecycleHooks.Install("RainWorld.Start",
                    () => On.RainWorld.Start += RainWorld_Start,
                    () => On.RainWorld.Start -= RainWorld_Start));
            EditorStartupDiagnostics.Step(
                "BridgePlugin/Hook RainWorld.OnModsInit",
                () => lifecycleHooks.Install("RainWorld.OnModsInit",
                    () => On.RainWorld.OnModsInit += RainWorld_OnModsInit,
                    () => On.RainWorld.OnModsInit -= RainWorld_OnModsInit));
            bridgeEnabled = true;
            EditorStartupDiagnostics.Marker("BridgePlugin.OnEnable", "CORE-READY");
        }
        catch (Exception error)
        {
            EditorStartupDiagnostics.Failure("BridgePlugin.OnEnable", error);
            Logger?.LogError(
                "RainWright core shell failed during OnEnable; Rain World startup will continue. " +
                error);
            ShutdownBridgeState();
            return;
        }

        TryEnableOptionalFrontendFeature(
            "RwImGuiLocalizationReloadGuard",
            () => RwImGuiLocalizationReloadGuard.Enable(Logger),
            RwImGuiLocalizationReloadGuard.Disable);

        TryEnableOptionalFrontendFeature(
            "WorldCreatureSpawnInspector",
            () => WorldCreatureSpawnInspector.Enable(Logger),
            WorldCreatureSpawnInspector.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldLineageInspector",
            () => WorldLineageInspector.Enable(Logger),
            WorldLineageInspector.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldTimelineSlugcatIconCatalog",
            () => WorldTimelineSlugcatIconCatalog.Initialize(Logger),
            WorldTimelineSlugcatIconCatalog.Shutdown);
        TryEnableOptionalFrontendFeature(
            "WorldSubregionSelector",
            () => WorldSubregionSelector.Enable(Logger),
            WorldSubregionSelector.Disable);
        TryEnableOptionalFrontendFeature(
            "ScopedScrollChrome",
            () => ScopedScrollChrome.Enable(Logger),
            ScopedScrollChrome.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapPlayerLocator",
            () => WorldMapPlayerLocator.Enable(Logger),
            WorldMapPlayerLocator.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapUpdateThrottle",
            () => WorldMapUpdateThrottle.Enable(Logger),
            WorldMapUpdateThrottle.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapBackgroundBudget",
            () => WorldMapBackgroundBudget.Enable(Logger),
            WorldMapBackgroundBudget.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapRasterReadbackFallback",
            () => WorldMapRasterReadbackFallback.Enable(Logger),
            WorldMapRasterReadbackFallback.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapExactShortcuts",
            () => WorldMapExactShortcuts.Enable(Logger),
            WorldMapExactShortcuts.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapLegacyVisualGuard",
            () => WorldMapLegacyVisualGuard.Enable(Logger),
            WorldMapLegacyVisualGuard.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapRetainedV2Runtime",
            () => WorldMapRetainedV2Runtime.Enable(Logger),
            WorldMapRetainedV2Runtime.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapPresentationCorrectness",
            () => WorldMapPresentationCorrectness.Enable(Logger),
            WorldMapPresentationCorrectness.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapRenderOrder",
            () => WorldMapRenderOrder.Enable(Logger),
            WorldMapRenderOrder.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapThumbnailVisibility",
            () => WorldMapThumbnailVisibility.Enable(Logger),
            WorldMapThumbnailVisibility.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldMapPipeLayers",
            () => WorldMapPipeLayers.Enable(Logger),
            WorldMapPipeLayers.Disable);
        TryEnableOptionalFrontendFeature(
            "WorldInspectorReadability",
            () => WorldInspectorReadability.Enable(Logger),
            WorldInspectorReadability.Disable);
        TryEnableOptionalFrontendFeature(
            "UserFacingCopyCleanup",
            () => DevToolUserFacingCopyCleanup.Enable(Logger),
            DevToolUserFacingCopyCleanup.Disable);
        TryEnableOptionalFrontendFeature(
            "PlayerMapFrontendLifecycle",
            () => PlayerMapFrontendLifecycle.Enable(Logger),
            PlayerMapFrontendLifecycle.Disable);
        TryEnableOptionalFrontendFeature(
            "RetainedViewLifecycle",
            retainedViewLifecycle.Enable,
            retainedViewLifecycle.Disable);

        EditorStartupDiagnostics.Marker("BridgePlugin.OnEnable", "EXIT");
    }

    private void TryEnableOptionalFrontendFeature(
        string name,
        Action enable,
        Action rollback)
    {
        try
        {
            // Record cleanup before enabling. If a partially installed module
            // throws and cleanup also fails, the entry stays held by this Bridge.
            optionalFeatures.Install(name, enable, rollback);
        }
        catch (Exception error)
        {
            EditorStartupDiagnostics.Failure(
                "BridgePlugin/Optional/" + name,
                error);
            Logger?.LogError(
                "DryCycle DevTool optional frontend feature '" + name +
                "' failed; the New UI shell remains enabled. " + error);
        }
    }

    private void LateUpdate()
    {
        if (!bridgeEnabled) return;

        EditorSession labelSession =
            DevToolSessionHub.Current;
        RainWorldGame labelGame =
            labelSession?.Owner?.game;
        bool topBarOwnsDevToolsStatus =
            DevToolFrontend.NativeBackendReady &&
            DevToolSessionHub.IsCurrentSessionLive;

        VanillaDevToolsLabelVisibility.Apply(
            labelGame,
            topBarOwnsDevToolsStatus);

        if (!legacyVisualGuardFaulted)
        {
            try
            {
                WorldMapLegacyVisualGuard.LateUpdate();
            }
            catch (Exception error)
            {
                legacyVisualGuardFaulted = true;
                Logger?.LogError(
                    "WorldMapLegacyVisualGuard failed and has been disabled for this frontend lifetime. " +
                    error);
            }
        }

        bool shouldRunRetainedFrontend =
            DevToolFrontend.NativeBackendReady &&
            !EditorUiModeState.UseVanilla &&
            DevToolSessionHub.IsCurrentSessionLive;

        if (shouldRunRetainedFrontend && !retainedLifecycleFaulted)
        {
            try
            {
                retainedViewLifecycle.LateUpdate();
                retainedFrontendActive = true;
            }
            catch (Exception error)
            {
                retainedLifecycleFaulted = true;
                retainedFrontendActive = false;
                Logger?.LogError(
                    "DevTool retained view lifecycle failed; core New UI rendering remains enabled. " +
                    error);
            }
        }
        else if (retainedFrontendActive)
        {
            try
            {
                DevToolPageViewRegistry.DeactivateActive();
            }
            catch (Exception error)
            {
                Logger?.LogWarning(
                    "DevTool retained page deactivation failed: " + error);
            }
            retainedFrontendActive = false;
        }
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!bridgeEnabled) return;
        applicationFocused = hasFocus;

        // Unity may stop calling Update while the window is unfocused. Remember the transition in
        // the focus callback itself so the very first frame after Alt+Tab cannot mistake a transient
        // DevUI/game-state mismatch for an actual DevTools shutdown and release the ImGui context.
        sessionVisibility.OnApplicationFocus(hasFocus);

        DevToolFrontend.SetApplicationFocusedFromMainThread(hasFocus);
    }

    private void Update()
    {
        if (!bridgeEnabled) return;
        RainWrightWorkspaceRuntime.Pump();
        RainWrightOptions.RefreshStatus();

        // Only vanilla RainWorld.OnModsInit owns the normal readiness transition.
        // On hot enable/reload that event may already have fired; observing an active editor
        // session proves RainWorld has initialized without referencing DryCycle's lifecycle.
        // The readiness flag makes this a one-shot fallback, not a per-frame logger/poller.
        if (!callbackRegistrationAllowed && DevToolSessionHub.IsCurrentSessionLive)
        {
            EditorBackendLifecycle.EnableExtras(EditorDiagnostics.LogSource);
            AllowCallbackRegistration("existing live DevTool session");
        }
        // Callback registration is retryable. A single timing/API failure during OnModsInit must not
        // leave NativeBackendReady=false for the rest of the process.
        TryRegisterCallback();

        bool nativeFrontendReady = DevToolFrontend.NativeBackendReady;
        if (frontendInputAttached != nativeFrontendReady)
        {
            frontendInputAttached = nativeFrontendReady;
            EditorInputRouter.SetFrontendAttached(nativeFrontendReady);
        }

        bool sessionLiveNow = DevToolSessionHub.IsCurrentSessionLive;

        // Gameplay identity is adapted to the Rainwright focus/session policy.
        // A temporary missing editor hub is not proof that the native game ended.
        EditorSession session = DevToolSessionHub.Current;
        RainWorldGame game = session?.Owner?.game;
        FrontendSessionVisibilityDecision visibility = sessionVisibility.Observe(
            sessionLiveNow, game, game?.GamePaused == true, IsGameDefinitelyClosed);
        bool definitelyClosed = visibility.DefinitelyClosed;
        bool sessionVisible = visibility.Visible;

        if (visibility.Returned)
        {
            mapFrontendWorkFaulted = false;
            creatureCatalogWorkFaulted = false;
            timelineIconWorkFaulted = false;
        }
        if (EditorUiModeState.OverlayHidden && visibility.ShouldUnhideOverlay)
            EditorUiModeState.SetOverlayHidden(false);

        bool feedbackHold = EditorShortcutFeedback.PresentationHoldActive;
        bool frontendVisible = visibility.FrontendVisible(
            EditorUiModeState.OverlayHidden, feedbackHold);

        // Core shell/context activation happens before Map caches, image pumps or catalog work.
        // Optional page code can no longer prevent this call from being reached.
        DevToolFrontend.SetVisibleFromMainThread(
            frontendVisible,
            releaseContextWhenHidden: definitelyClosed);

        if (!sessionVisible)
        {
            mapFrontendWorkFaulted = false;
            creatureCatalogWorkFaulted = false;
            timelineIconWorkFaulted = false;
            return;
        }

        bool rebuiltFrontendWorkActive =
            nativeFrontendReady &&
            !EditorUiModeState.UseVanilla &&
            sessionLiveNow;
        if (!rebuiltFrontendWorkActive)
            return;

        PumpOptionalFrontendWork();
    }

    private void PumpOptionalFrontendWork()
    {
        EditorSession mapSession = DevToolRuntime.ActiveSession;
        if (mapSession?.ToolMode == EditorToolMode.Map && !mapFrontendWorkFaulted)
        {
            try
            {
                MapRoomGeometryPresentationHub.Prime(mapSession);
                if (WorldMapBackgroundBudget.AllowSourceRecovery(mapSession))
                    MapRoomGeometryPresentationHub.RecoverMissingSources(mapSession);

                int selectedRoomIndex =
                    MapEditorStateHub.Get(mapSession)?.SelectedRoomIndex ?? -1;
                WorldMapExactShortcuts.UpdateMainThread(mapSession, selectedRoomIndex);
                WorldMapShortcutPresentation.Prime(mapSession, selectedRoomIndex);
                WorldMapRetainedV2Runtime.UpdateMainThread();
                // The optional helper BepInPlugin is now only a GUID alias.
                // Capture live players on this Unity thread, never on Present.
                WorldMapPlayerLocator.UpdateFromMainThread();
                CartographyCanvasImages.UpdateMainThread();
            }
            catch (Exception error)
            {
                mapFrontendWorkFaulted = true;
                Logger?.LogError(
                    "DevTool optional Map frontend pump failed and is disabled until DevTools is reopened. " +
                    error);
            }
        }

        // Time-line icons use a bounded main-thread job queue. Never
        // generate Futile/atlas assets from the native ImGui render callback.
        if (mapSession?.ToolMode == EditorToolMode.Map && !timelineIconWorkFaulted)
        {
            try { WorldTimelineSlugcatIconCatalog.PumpMainThread(); }
            catch (Exception error)
            {
                timelineIconWorkFaulted = true;
                Logger?.LogError("World timeline icon pump failed for this frontend lifetime: " + error);
            }
        }

        if (!creatureCatalogWorkFaulted)
        {
            try
            {
                EnsureCreatureCatalogRuntime();
                if (creatureCatalog.Started)
                    WorldCreatureCatalogPicker.PumpMainThread();
            }
            catch (Exception error)
            {
                creatureCatalogWorkFaulted = true;
                Logger?.LogError(
                    "DevTool optional creature catalog pump failed and is disabled until DevTools is reopened. " +
                    error);
            }
        }
    }

    private void EnsureCreatureCatalogRuntime()
    {
        try
        {
            // A failed initializer can leave HookGen, icon or cache state
            // partially allocated. The dedicated lease records that intent
            // BEFORE initialization and retries any failed cleanup before
            // ever allowing another initialization.
            creatureCatalog.EnsureStarted(
                () => WorldCreatureCatalogPicker.Initialize(Logger),
                WorldCreatureCatalogPicker.Shutdown,
                error => EditorStartupDiagnostics.Failure(
                    "BridgePlugin/CreatureCatalog/PartialCleanup", error));
        }
        catch (Exception error)
        {
            EditorStartupDiagnostics.Failure("BridgePlugin/CreatureCatalog/Initialize", error);
            throw;
        }
    }

    private static bool IsGameDefinitelyClosed(RainWorldGame game)
    {
        if (game == null) return true;
        if (!game.processActive || !game.devToolsActive) return true;

        // currentMainLoop changing is an actual process transition. By contrast game.devUI == null,
        // a temporary owner mismatch, or owner.room == null can occur around Alt+Tab and therefore
        // must not be used as reasons to destroy the frontend context.
        if (game.manager?.currentMainLoop != null && !ReferenceEquals(game.manager.currentMainLoop, game))
            return true;

        return false;
    }

    private void OnDisable()
    {
        if (BridgePluginOwnership.IsOwner(this))
            ShutdownBridgeState();
    }

    private void ShutdownBridgeState()
    {
        bridgeEnabled = false;

        SafeFrontendCleanup("DevTool user settings", DevToolUserSettingsStore.FlushNow);
        SafeFrontendCleanup("vanilla DevTools label", VanillaDevToolsLabelVisibility.Restore);
        SafeFrontendCleanup("frontend lifecycle hooks", () =>
        {
            if (!lifecycleHooks.ReleaseAll())
                throw new InvalidOperationException(
                    "One or more frontend hooks could not be detached; registrations are retained for retry.");
        });
        SafeFrontendCleanup("frontend visibility", () => DevToolFrontend.SetVisibleFromMainThread(false));
        SafeFrontendCleanup(
            "frontend focus state",
            () => DevToolFrontend.SetApplicationFocusedFromMainThread(true));
        SafeFrontendCleanup("overlay state", () => EditorUiModeState.SetOverlayHidden(false));
        applicationFocused = true;
        sessionVisibility.Reset(applicationFocused);
        mapFrontendWorkFaulted = false;
        creatureCatalogWorkFaulted = false;
        timelineIconWorkFaulted = false;
        legacyVisualGuardFaulted = false;
        retainedLifecycleFaulted = false;
        callbackRegistrationAllowed = false;
        nextCallbackRegistrationAttemptAt = 0f;
        Interlocked.Exchange(ref callbackRegistrationFailureLogged, 0);
        SafeFrontendCleanup("frontend input attachment", () => EditorInputRouter.SetFrontendAttached(false));
        SafeFrontendCleanup("RWImGui callback", TryUnregisterCallback);
        SafeFrontendCleanup("font atlas integration", DevToolFontAtlasIntegration.Disable);

        // The catalog is on-demand and has its own pending-cleanup lease.
        // Flush any buffered Map author assets before releasing its helpers.
        if (creatureCatalog.PendingCleanup)
            SafeFrontendCleanup("creature catalog", () =>
                creatureCatalog.Stop(WorldCreatureCatalogPicker.Shutdown));
        frontendInputAttached = false;
        retainedFrontendActive = false;
        SafeFrontendCleanup(
            "world map persistent cache flush",
            MapRoomGeometryPresentationHub.FlushPersistentCache);
        SafeFrontendCleanup("optional frontend lifecycle", () =>
        {
            if (!optionalFeatures.ReleaseAll())
                throw new InvalidOperationException(
                    "One or more optional frontend modules could not be shut down; owner retained.");
        });
        SafeFrontendCleanup("world map source recovery", MapRoomGeometryPresentationHub.ResetSourceRecovery);

        SafeFrontendCleanup("editor backend", EditorBackendLifecycle.Disable);

        if (!EditorBackendLifecycle.PendingCleanup && !lifecycleHooks.HasActiveRegistrations && !optionalFeatures.HasActiveRegistrations &&
            !alwaysCallbackLease.HasActiveRegistrations &&
            !DevToolFontAtlasIntegration.PendingCleanup && !creatureCatalog.PendingCleanup)
            BridgePluginOwnership.TryRelease(this, cleanupComplete: true);
        else
            EditorStartupDiagnostics.Marker("BridgePlugin.OnDisable", "OWNER-RETAINED",
                "hook/optional module/callback/font/catalog cleanup incomplete; duplicate frontend instances remain blocked");
    }

    private void SafeFrontendCleanup(string name, Action action)
    {
        try
        {
            action?.Invoke();
        }
        catch (Exception cleanupError)
        {
            EditorStartupDiagnostics.Failure("BridgePlugin.Cleanup/" + name, cleanupError);
            Logger?.LogWarning("DryCycle DevTool frontend cleanup failed for " + name + ": " + cleanupError);
        }
    }

    private static void RainWorld_Start(On.RainWorld.orig_Start orig, RainWorld self)
    {
        EditorStartupDiagnostics.Marker("BridgePlugin/RainWorld.Start", "ENTER");

        // RWImGUI installs native bindings here. DryCycle does not create/switch consumer contexts
        // during game startup, but DevToolFontAtlasIntegration is already armed from BridgePlugin
        // OnEnable so the fixed HarmonyOS face can join RWImGUI's shared atlas before first upload.
        EditorStartupDiagnostics.Step(
            "BridgePlugin/RainWorld.Start/orig",
            () => orig(self));

        // Do not touch ImGUIAPI context/font state from RainWorld.Start.
        // RWImGUI can return from its Start hook even when native D3D11 initialization failed.
        // Calling HasContext/SwitchContext in that state can cross an uninitialized native binding
        // and terminate the whole process before a managed exception can be logged.
        //
        // Font registration itself happens inside the backend pre-upload callback; this hook does
        // not call ImGUIAPI.HasContext/SwitchContext and therefore keeps startup ownership safe.
        EditorStartupDiagnostics.Marker("BridgePlugin/RainWorld.Start", "EXIT");
    }

    private void RainWorld_OnModsInit(
        On.RainWorld.orig_OnModsInit orig,
        RainWorld self)
    {
        orig(self);
        World.WorldTextRegistry.InvalidateFailedSourceContext();
        RainWrightWorkspaceRuntime.Run(RainWrightOptions.Register);
        // HookGen resolves native method pointers and can run ModButton's static
        // constructor. PreModsInit must initialize Remix's text table first;
        // installing this during OnEnable permanently poisons that type.
        if (!RemixModDetailsBridge.HasActiveRegistrations)
            TryEnableOptionalFrontendFeature("RemixModDetails",
                () => RemixModDetailsBridge.Enable(Logger), RemixModDetailsBridge.Disable);
        EditorBackendLifecycle.EnableExtras(EditorDiagnostics.LogSource);
        AllowCallbackRegistration("RainWorld.OnModsInit");
    }

    private static void AllowCallbackRegistration(string source)
    {
        callbackRegistrationAllowed = true;
        nextCallbackRegistrationAttemptAt = 0f;
        log?.LogInfo(
            "RainWright callback registration enabled by " + source + ".");
    }

    private static void TryRegisterCallback()
    {
        if (alwaysCallbackLease.HasActiveRegistrations || !callbackRegistrationAllowed)
            return;

        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < nextCallbackRegistrationAttemptAt)
            return;

        try
        {
            alwaysCallbackLease.Install(
                "RWImGUI.AlwaysCallback",
                AttachNativeAlwaysCallback,
                DetachNativeAlwaysCallback);
            nextCallbackRegistrationAttemptAt = 0f;
            Interlocked.Exchange(ref callbackRegistrationFailureLogged, 0);
            log?.LogInfo(
                "RainWright frontend callback registered. " +
                "Waiting for the first healthy Present heartbeat.");
        }
        catch (Exception error)
        {
            nextCallbackRegistrationAttemptAt = now + 1.0f;

            if (Interlocked.Exchange(ref callbackRegistrationFailureLogged, 1) == 0)
            {
                EditorStartupDiagnostics.Failure(
                    "BridgePlugin/TryRegisterCallback",
                    error);
                log?.LogError(
                    (alwaysCallbackLease.HasActiveRegistrations
                        ? "RainWright callback registration or rollback failed; " +
                          "pending callback cleanup blocks re-registration. "
                        : "RainWright callback registration failed; " +
                          "retrying once per second. ") + error);
            }
        }
    }

    private static unsafe void AttachNativeAlwaysCallback()
        => ImGUIAPI.AddAlwaysCallback(&DevToolFrontend.FrameCallback);

    private static unsafe void DetachNativeAlwaysCallback()
        => ImGUIAPI.RemoveAlwaysCallback(&DevToolFrontend.FrameCallback);

    private static void TryUnregisterCallback()
    {
        if (!alwaysCallbackLease.ReleaseAll())
            throw new InvalidOperationException(
                "DryCycle DevTool RWImGUI callback removal failed; " +
                "the native callback lease remains owned for a later retry.");
    }
}

[SuppressUnmanagedCodeSecurity]
internal static class DevToolFrontend
{
    // Do not construct a consumer IMGUIContext merely because BepInEx loads the bridge assembly.
    // Context creation is deferred until the DevTool is actually visible and RWImGui reports that
    // no other context owns input. This keeps the entire BepInEx/RainWorld startup path free of
    // consumer context construction.
    private static volatile DevToolInputContext inputContext;
    private static ManualLogSource log;
    private static volatile bool visible;
    private static volatile bool applicationFocused = true;
    private static int contextBusyLogged;
    private static int drawFailureLogged;
    private static int firstPresentLogged;
    private static int contextAttachedLogged;
    private static int firstRenderLogged;
    private static int contextRebuildRequested;
    private static int textureFrameFailureLogged;
    private static volatile bool frontendSafeMode;
    private static string frontendSafeModeReason = string.Empty;
    private static int cjkFontLogged;
    private static int cjkFontMissingLogged;
    private static int fontPushFailureLogged;
    private static int contextActivationFailureLogged;
    private static int rwimguiPresentObserved;
    private static int backendUnavailableLogged;
    private static volatile bool contextAttached;
    private static float nextContextAttemptAt;
    private static ImFontPtr activeFont;
    private static string resolvedFontName = string.Empty;
    private static int resolvedFontWeight = DevToolUiSettings.DefaultFontWeight;
    private static int resolvedFontWeightVariantCount = 1;
    private static DevToolUiLanguage projectedFontLanguage = (DevToolUiLanguage)(-1);
    private static string projectedFontFamily = string.Empty;
    private static int projectedFontWeight = int.MinValue;

    internal static bool NativeBackendReady => Volatile.Read(ref rwimguiPresentObserved) != 0;
    internal static string ResolvedFontName => resolvedFontName;
    internal static int ResolvedFontWeight => resolvedFontWeight;
    internal static int ResolvedFontWeightVariantCount => resolvedFontWeightVariantCount;

    internal static void ResetNativeReadinessFromMainThread()
    {
        contextAttached = false;
        nextContextAttemptAt = 0f;
        Interlocked.Exchange(ref rwimguiPresentObserved, 0);
        Interlocked.Exchange(ref backendUnavailableLogged, 0);
        Interlocked.Exchange(ref contextActivationFailureLogged, 0);
        Interlocked.Exchange(ref contextBusyLogged, 0);
        Interlocked.Exchange(ref drawFailureLogged, 0);
        Interlocked.Exchange(ref firstPresentLogged, 0);
        Interlocked.Exchange(ref contextAttachedLogged, 0);
        Interlocked.Exchange(ref firstRenderLogged, 0);
        Interlocked.Exchange(ref contextRebuildRequested, 0);
        Interlocked.Exchange(ref textureFrameFailureLogged, 0);
        frontendSafeMode = false;
        frontendSafeModeReason = string.Empty;
        ResetFontProjectionForNewContext();
        EditorInputRouter.SetFrontendCapture(false, false, false);
    }

    internal static void SetLogger(ManualLogSource value) => log = value;

    internal static void LogRelationshipTrace(string message) =>
        log?.LogInfo("[DevTool.Relationships][Frontend] " + (message ?? string.Empty));

    internal static void RequestContextRebuildForLanguageChange()
    {
        // Language buttons are clicked on RWImGui's render thread. Only publish intent here; the
        // Unity main thread performs SwitchContext(null) on the next Update.
        Interlocked.Exchange(ref contextRebuildRequested, 1);
    }

    internal static void SetApplicationFocusedFromMainThread(bool value)
    {
        applicationFocused = value;
        if (!value)
            EditorInputRouter.SetFrontendCapture(false, false, false);
    }

    internal static void SetVisibleFromMainThread(
        bool value,
        bool releaseContextWhenHidden = true)
    {
        bool wasVisible = visible;
        visible = value;

        if (Interlocked.Exchange(ref contextRebuildRequested, 0) != 0)
        {
            if (contextAttached)
                ReleaseContext();

            inputContext = null;
            ResetFontProjectionForNewContext();
        }

        if (!value)
        {
            // H only closes Rain World's DevUI surface; it does not end the DevTools/game lifetime.
            // Releasing RWImGUI's native consumer context on that transient edge can race the Present
            // thread that is still finishing the current frame and hard-crash the process without a
            // managed exception. Keep the context attached but idle for transient hides (H / Esc),
            // and release it only when the game/session is definitely ending or during explicit
            // teardown/rebuild.
            if (releaseContextWhenHidden && (contextAttached || wasVisible))
                ReleaseContext();

            EditorInputRouter.SetFrontendCapture(false, false, false);
            return;
        }

        if (contextAttached)
            return;

        // Do not call any native-backed RWImGUI context API unless its Present callback has run.
        // The user's crash log shows RWImGUI failing D3D11CreateDeviceAndSwapChain with
        // DXGI_ERROR_UNSUPPORTED while Unity is on "Microsoft Basic Render Driver". In that state
        // HasContext/SwitchContext can terminate the process rather than throwing managed errors.
        if (Volatile.Read(ref rwimguiPresentObserved) == 0)
        {
            EditorInputRouter.SetFrontendCapture(false, false, false);

            if (Interlocked.Exchange(ref backendUnavailableLogged, 1) == 0)
            {
                log?.LogWarning(
                    "DryCycle DevTool New UI is waiting for a healthy RWImGUI Present. " +
                    "No native context calls will be attempted. Unity graphics device='" +
                    UnityEngine.SystemInfo.graphicsDeviceName +
                    "'.");
            }

            return;
        }

        float now = UnityEngine.Time.realtimeSinceStartup;
        if (now < nextContextAttemptAt)
            return;

        EnsureContext(now);
    }

    public static void FrameCallback(ref nint idxgiSwapChain, ref uint syncInterval, ref uint flags)
    {
        // This is the only native-health signal we trust. AddAlwaysCallback reaching Present means
        // RWImGUI completed enough of its D3D11 path for consumer context APIs to be used safely.
        Interlocked.Exchange(ref rwimguiPresentObserved, 1);
        Interlocked.Exchange(ref backendUnavailableLogged, 0);

        if (Interlocked.Exchange(ref firstPresentLogged, 1) == 0)
        {
            log?.LogInfo(
                "RainWright first healthy Present observed. " +
                "Consumer context activation is now allowed.");
        }
    }

    private static void EnsureContext(float now)
    {
        try
        {
            // This path runs only when the rebuilt DevTool is actually visible, i.e. well after
            // RainWorld.Start. Do not gate it on the Always callback: some RWImGUI builds can keep
            // consumer contexts usable even when that callback is unavailable, and the old gate
            // permanently prevented New UI from opening.
            if (ImGUIAPI.HasContext)
            {
                EditorInputRouter.SetFrontendCapture(false, false, false);

                nextContextAttemptAt = now + 0.25f;

                if (Interlocked.Exchange(ref contextBusyLogged, 1) == 0)
                {
                    log?.LogWarning(
                        "DevTool UI is waiting because another RWImGui context owns input.");
                }

                return;
            }

            DevToolInputContext context = inputContext;
            bool createdContext = false;
            if (context == null)
            {
                context = new DevToolInputContext();
                inputContext = context;
                createdContext = true;
            }

            ImGUIAPI.SwitchContext(context);
            contextAttached = true;
            if (createdContext)
                FloatingWindowSnap.BeginContextSession();
            nextContextAttemptAt = 0f;

            // The shared atlas is immutable by this point. HarmonyOS is registered by
            // DevToolFontAtlasIntegration before RWImGUI's first backend upload, regardless of
            // which language is active at startup. Context switches only select an existing face.
            Interlocked.Exchange(ref contextBusyLogged, 0);
            Interlocked.Exchange(ref contextActivationFailureLogged, 0);

            if (Interlocked.Exchange(ref contextAttachedLogged, 1) == 0)
            {
                log?.LogInfo(
                    "RainWright consumer context activated. language=" +
                    DevToolUiSettings.Language +
                    ", localFontRegistration=" +
                    DevToolFontCatalog.RegistrationAttempted +
                    ".");
            }
        }
        catch (Exception error)
        {
            // SwitchContext can fail after RWImGui has already changed part of its ownership state.
            // Best-effort release prevents the next retry from being stuck forever behind HasContext.
            try
            {
                ImGUIAPI.SwitchContext(null);
            }
            catch (Exception cleanupError)
            {
                log?.LogWarning(
                    "DevTool RWImGui context activation cleanup also failed: " +
                    cleanupError);
            }

            contextAttached = false;
            nextContextAttemptAt = now + 1.0f;
            inputContext = null;
            ResetFontProjectionForNewContext();
            EditorInputRouter.SetFrontendCapture(false, false, false);

            if (Interlocked.Exchange(ref contextActivationFailureLogged, 1) == 0)
            {
                log?.LogError(
                    "DevTool RWImGui context activation failed; retrying at low frequency. " +
                    error);
            }
        }
    }

    private static void ReleaseContext()
    {
        if (!contextAttached)
            return;

        try
        {
            ImGUIAPI.SwitchContext(null);
        }
        catch (Exception error)
        {
            log?.LogWarning("DevTool RWImGui context release failed: " + error);
        }
        finally
        {
            contextAttached = false;
            nextContextAttemptAt = 0f;
            EditorInputRouter.SetFrontendCapture(false, false, false);
        }
    }

    internal static void NotifyContextDestroyedFromRwImGui()
    {
        contextAttached = false;
        nextContextAttemptAt = 0f;
        inputContext = null;
        ResetFontProjectionForNewContext();
        EditorInputRouter.SetFrontendCapture(false, false, false);
    }

    private static void ResetFontProjectionForNewContext()
    {
        activeFont = default;
        resolvedFontName = string.Empty;
        resolvedFontWeight = DevToolUiSettings.DefaultFontWeight;
        resolvedFontWeightVariantCount = 1;
        projectedFontLanguage = (DevToolUiLanguage)(-1);
        projectedFontFamily = string.Empty;
        projectedFontWeight = int.MinValue;
        Interlocked.Exchange(ref cjkFontLogged, 0);
        Interlocked.Exchange(ref cjkFontMissingLogged, 0);
        Interlocked.Exchange(ref fontPushFailureLogged, 0);
        Interlocked.Exchange(ref contextAttachedLogged, 0);
        Interlocked.Exchange(ref firstRenderLogged, 0);
        Interlocked.Exchange(ref drawFailureLogged, 0);
        // Input contexts share the native atlas; the catalog tracks the atlas's own lifetime.
        DevToolGlyphs.ResetCache();
    }

    internal static void RenderFromContext(ref nint idxgiSwapChain, ref uint syncInterval, ref uint flags)
    {
        try
        {
            WorldMapTextureFrame.Begin();
            Interlocked.Exchange(ref textureFrameFailureLogged, 0);
        }
        catch (Exception error)
        {
            // Texture leases are a World Map implementation detail. A bad/stale COM texture handle
            // must not escape IMGUIContext.Render and take the entire editor shell with it.
            if (Interlocked.Exchange(ref textureFrameFailureLogged, 1) == 0)
            {
                log?.LogError(
                    "DevTool World Map texture-frame cleanup failed; continuing to render the UI shell. " +
                    error);
            }
        }

        EditorPresentationSnapshot snapshot = EditorPresentationState.Current;

        bool traceFirstRender =
            Interlocked.Exchange(ref firstRenderLogged, 1) == 0;
        if (traceFirstRender)
        {
            log?.LogInfo(
                "RainWright context Render reached Present. visible=" +
                visible +
                ", snapshotAvailable=" +
                snapshot.Available +
                ", vanilla=" +
                EditorUiModeState.UseVanilla +
                ".");
        }

        // While the OS owns focus, keep the consumer context alive but submit no ImGui windows.
        // This prevents temporary fullscreen/display-size changes and stale mouse input from moving,
        // snapping, resizing or recreating any DevTool window during Alt+Tab.
        if (!applicationFocused)
        {
            EditorInputRouter.SetFrontendCapture(false, false, false);
            return;
        }

        // A shortcut may hide the normal editor UI (Esc) while its top-center acknowledgement is
        // still animating. Keep a feedback-only frame alive for that brief hold without reopening
        // any editor windows or requiring a presentation snapshot.
        bool feedbackOnly =
            EditorUiModeState.OverlayHidden &&
            EditorShortcutFeedback.PresentationHoldActive;
        if (!visible)
        {
            EditorInputRouter.SetFrontendCapture(false, false, false);
            EditorInputRouter.SetFrontendChromeCapture(false);
            return;
        }

        int framePresentationRevision = EditorUiModeState.PresentationRevision;
        using DevToolFrontendPerformanceMonitor.Scope frontendFrameScope =
            DevToolFrontendPerformanceMonitor.Measure(DevToolFrontendPerformanceMetric.FrontendFrameTotal);

        try
        {
            ImGuiIOPtr io = ImGui.GetIO();
            DevToolUiFrameContext frameContext = new(io);
            RainWrightWorkspaceRuntime.PumpFrontend();

            if (frontendSafeMode)
            {
                EditorInputRouter.SetFrontendChromeCapture(false);
                DrawFrontendSafeMode(frameContext.DisplaySize);
                EditorInputRouter.SetFrontendCapture(
                    io.WantCaptureMouse,
                    io.WantCaptureKeyboard,
                    io.WantTextInput);
                return;
            }

            // Rain World's developer cursor remains authoritative. Do not toggle Unity's cursor
            // visibility and do not draw a second ImGui software cursor; both approaches fight
            // the vanilla DevUI and cause visible flicker. Tooltip placement is handled separately.
            io.MouseDrawCursor = false;

            if (!feedbackOnly)
                FloatingWindowSnap.BeginFrame(frameContext);

            bool pushedActiveFont = TryPushActiveFont();
            float oldGlobalScale = io.FontGlobalScale;
            float baseFontSize = ResolveActiveBaseFontSize();
            float fontScale = ResolveUiFontScale(baseFontSize);
            float layoutScale = ResolveLayoutScale();

            // Font size and widget geometry are driven from the same absolute presentation scale.
            // This prevents large text from being laid out against stale small paddings/scrollbars.
            io.FontGlobalScale = fontScale;

            int pushedLayoutVars = PushScaledLayout(layoutScale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, DevToolUiSettings.WindowOutlineWidth);
            ImGui.PushStyleColor(ImGuiCol.Text, DevToolUiSettings.TextColor);
            ImGui.PushStyleColor(ImGuiCol.TextDisabled, DevToolUiSettings.DisabledTextColor);
            ImGui.PushStyleColor(ImGuiCol.Border, new System.Numerics.Vector4(0f, 0f, 0f, 1f));
            try
            {
                if (!feedbackOnly)
                {
                    // The two-way mode switch is always visible while DevUI itself is alive. Vanilla
                    // presentation hides rebuilt editor panels, not the control used to return.
                    if (traceFirstRender)
                        log?.LogInfo("DevTool first-frame stage: before UiModeSwitch.");
                    using (DevToolFrontendPerformanceMonitor.Measure(DevToolFrontendPerformanceMetric.UiModeSwitch))
                        UiModeSwitch.Draw();
                    if (traceFirstRender)
                        log?.LogInfo("DevTool first-frame stage: after UiModeSwitch.");

                    // Switching from Vanilla to New UI can happen inside UiModeSwitch.Draw(). If the
                    // recreated session has not published its first snapshot yet, wait one frame rather
                    // than feeding an unavailable snapshot into rebuilt editor windows.
                    if (framePresentationRevision == EditorUiModeState.PresentationRevision && !EditorUiModeState.UseVanilla && snapshot.Available)
                    {
                        // Submit the main editor surfaces before the persistent top chrome.
                        if (traceFirstRender)
                            log?.LogInfo("DevTool first-frame stage: before Overlay.");
                        using (DevToolFrontendPerformanceMonitor.Measure(DevToolFrontendPerformanceMetric.Overlay))
                            DevToolOverlay.Draw(snapshot, frameContext);
                        if (traceFirstRender)
                            log?.LogInfo("DevTool first-frame stage: after Overlay.");

                        bool sceneSurfaceSupported =
                            !snapshot.FocusMode &&
                            !DevToolOverlay.SuppressesSharedPageSurfaces &&
                            DevToolPageViewRegistry.SupportsSceneSurface(snapshot.ToolMode);
                        if (sceneSurfaceSupported && DevToolUiSettings.SceneInCenter)
                        {
                            using (DevToolFrontendPerformanceMonitor.Measure(DevToolFrontendPerformanceMetric.SceneWorkspace))
                                SceneWorkspaceWindow.Draw(snapshot, frameContext.DisplaySize);
                        }
                        using (DevToolFrontendPerformanceMonitor.Measure(DevToolFrontendPerformanceMetric.ActionToast))
                            ActionToastOverlay.Draw(snapshot, frameContext);

                        // Persistent top chrome is submitted after every regular editor window.
                        // This gives it the highest normal DevTool layer without forcing keyboard
                        // focus every frame. Shortcut feedback below remains the only transient
                        // surface intentionally allowed above it.
                        IDevToolPageView topPage =
                            DevToolOverlay.IsDebugWorkspace
                                ? null
                                : DevToolPageViewRegistry.Get(snapshot.ToolMode);
                        DevToolTopStatusWindow.Draw(
                            snapshot,
                            topPage,
                            frameContext.DisplaySize);

                        // The tool palette is submitted after the top status surface so TopDocked
                        // placement can use its exact auto-resized rectangle from this same frame.
                        // It never overlaps the top bar, so drawing it one layer later does not
                        // compromise the top chrome's readability.
                        DevToolOverlay.DrawActivityBarAfterTop(
                            snapshot,
                            frameContext.DisplaySize);

                    }
                }

                // Every keyboard shortcut uses one page-independent feedback surface. It is drawn
                // last so global commands and view-local commands have the same top-center animation.
                using (DevToolFrontendPerformanceMonitor.Measure(DevToolFrontendPerformanceMetric.ActionToast))
                    GlobalShortcutFeedbackOverlay.Draw(frameContext.DisplaySize);

                if (!feedbackOnly)
                    FloatingWindowSnap.EndFrame();
            }
            finally
            {
                ImGui.PopStyleColor(3);
                ImGui.PopStyleVar(pushedLayoutVars + 1);
                io.FontGlobalScale = oldGlobalScale;
                if (pushedActiveFont) ImGui.PopFont();
            }

            // A marquee can begin over empty room pixels, where ImGui itself would normally report
            // WantCaptureMouse=false. Reserve the mouse explicitly so selection never clicks or
            // drags a vanilla world-space DevInterface handle underneath the layout gesture.
            EditorInputRouter.SetFrontendChromeCapture(!feedbackOnly && DevToolTopStatusWindow.CaptureMouse());
            if (feedbackOnly || EditorUiModeState.UseVanilla || framePresentationRevision != EditorUiModeState.PresentationRevision)
            {
                EditorInputRouter.SetFrontendCapture(false, false, false);
            }
            else
            {
                EditorInputRouter.SetFrontendCapture(
                    io.WantCaptureMouse ||
                    ShortcutWindow.OwnsMouse ||
                    FloatingWindowSnap.OwnsMouse ||
                    NativeObjectGizmoView.OwnsMouse ||
                    NativeSpatialGizmoView.OwnsMouse ||
                    ObjectMarqueeSelectionView.OwnsMouse,
                    io.WantCaptureKeyboard,
                    io.WantTextInput);
            }
        }
        catch (Exception error)
        {
            EditorInputRouter.SetFrontendCapture(false, false, false);
            EditorInputRouter.SetFrontendChromeCapture(false);

            frontendSafeModeReason =
                error.GetType().Name +
                ": " +
                (error.Message ?? string.Empty);
            frontendSafeMode = true;

            // A page may have thrown after ImGui.Begin/Push*, leaving the current native frame
            // stack inconsistent. Ask the Unity main thread to rebuild the consumer context before
            // drawing the dependency-free recovery panel.
            Interlocked.Exchange(ref contextRebuildRequested, 1);

            if (Interlocked.Exchange(ref drawFailureLogged, 1) == 0)
            {
                log?.LogError(
                    "DevTool RWImGui draw failed; entering recovery safe mode and rebuilding the consumer context. " +
                    error);
            }
        }
    }

    private static void DrawFrontendSafeMode(System.Numerics.Vector2 display)
    {
        float width = Math.Min(560f, Math.Max(300f, display.X - 16f));
        ImGui.SetNextWindowPos(new System.Numerics.Vector2(8f, 8f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new System.Numerics.Vector2(width, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.96f);

        if (!ImGui.Begin(
                "DryCycle DevTool Recovery###DevToolRecoverySafeMode",
                ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.AlwaysAutoResize |
                ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.End();
            return;
        }

        ImGui.TextUnformatted("New UI renderer recovered from a draw failure.");
        ImGui.TextWrapped(
            "Page-specific UI has been isolated. You can retry the rebuilt UI or return to Vanilla DevUI.");
        if (!string.IsNullOrWhiteSpace(frontendSafeModeReason))
        {
            ImGui.Separator();
            ImGui.TextWrapped(frontendSafeModeReason);
        }

        ImGui.Separator();
        if (ImGui.Button("Retry New UI"))
        {
            frontendSafeMode = false;
            frontendSafeModeReason = string.Empty;
            Interlocked.Exchange(ref drawFailureLogged, 0);
            Interlocked.Exchange(ref contextRebuildRequested, 1);
        }

        ImGui.SameLine();
        if (ImGui.Button("Use Vanilla DevUI"))
        {
            EditorUiModeState.SetVanilla(true);
            frontendSafeMode = false;
            frontendSafeModeReason = string.Empty;
            Interlocked.Exchange(ref contextRebuildRequested, 1);
        }

        ImGui.End();
    }

    private static unsafe float ResolveActiveBaseFontSize()
    {
        ImFontPtr active = ImGui.GetFont();
        return active.NativePtr != null && active.FontSize > 0.01f ? active.FontSize : 13f;
    }

    private static float ResolveUiFontScale(float baseFontSize)
    {
        if (baseFontSize <= 0.01f) return 1f;
        float scale = DevToolUiSettings.FontSize / baseFontSize;
        return Math.Max(0.65f, Math.Min(4.0f, scale));
    }

    private static float ResolveLayoutScale()
    {
        float scale = DevToolUiSettings.FontSize / DevToolUiSettings.ReferenceFontSize;
        return Math.Max(0.70f, Math.Min(3.50f, scale));
    }

    private static int PushScaledLayout(float scale)
    {
        ImGuiStylePtr style = ImGui.GetStyle();
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, style.WindowPadding * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, style.FramePadding * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, style.ItemSpacing * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, style.ItemInnerSpacing * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.IndentSpacing, style.IndentSpacing * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, style.ScrollbarSize * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, style.GrabMinSize * scale);
        return 7;
    }

    internal static unsafe bool TryPushRegisteredFont(ImFontPtr font, string usage)
    {
        if (font.NativePtr == null)
            return false;

        try
        {
            ImGui.PushFont(font);
            return true;
        }
        catch (Exception error)
        {
            if (Interlocked.Exchange(ref fontPushFailureLogged, 1) == 0)
            {
                log?.LogWarning(
                    "DryCycle DevTool could not PushFont for " +
                    (string.IsNullOrWhiteSpace(usage) ? "a local font" : usage) +
                    "; falling back to the RWImGui context default font: " +
                    error.Message);
            }

            return false;
        }
    }

    private static unsafe bool TryPushActiveFont()
    {
        DevToolUiLanguage language = DevToolUiSettings.Language;
        int preferredWeight = DevToolUiSettings.FontWeight;

        // English is the startup-safe path: use the font that RWImGui created for this exact
        // consumer context. Avoid carrying a local ImFontPtr when no CJK coverage is required.
        if (language == DevToolUiLanguage.English)
        {
            if (projectedFontLanguage != language ||
                projectedFontWeight != preferredWeight ||
                projectedFontFamily.Length != 0)
            {
                projectedFontLanguage = language;
                projectedFontFamily = string.Empty;
                projectedFontWeight = preferredWeight;
                activeFont = default;
                resolvedFontName = "Default";
                resolvedFontWeight = DevToolUiSettings.DefaultFontWeight;
                resolvedFontWeightVariantCount = 1;
            }

            return false;
        }

        string family = DevToolUiSettings.ChineseFontFamily;

        if (projectedFontLanguage != language ||
            projectedFontWeight != preferredWeight ||
            !string.Equals(
                projectedFontFamily,
                family,
                StringComparison.OrdinalIgnoreCase))
        {
            projectedFontLanguage = language;
            projectedFontFamily = family ?? string.Empty;
            projectedFontWeight = preferredWeight;
            activeFont = default;

            bool requireChinese =
                language == DevToolUiLanguage.Chinese;
            if (DevToolFontCatalog.TryResolveRegisteredFace(
                    family,
                    preferredWeight,
                    requireChinese,
                    out ImFontPtr resolved,
                    out string name,
                    out int actualWeight,
                    out int variantCount))
            {
                activeFont = resolved;
                resolvedFontName = name;
                resolvedFontWeight = actualWeight;
                resolvedFontWeightVariantCount =
                    Math.Max(1, variantCount);

                if (requireChinese)
                {
                    string resolvedFamily =
                        DevToolFontCatalog.FamilyFromName(name);
                    if (!string.IsNullOrWhiteSpace(resolvedFamily) &&
                        !string.Equals(
                            resolvedFamily,
                            DevToolUiSettings.ChineseFontFamily,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        DevToolUiSettings.ChineseFontFamily =
                            resolvedFamily;
                    }
                }

                if (requireChinese &&
                    Interlocked.Exchange(ref cjkFontLogged, 1) == 0)
                {
                    log?.LogInfo(
                        "DryCycle DevTool selected local CJK font: " +
                        name +
                        " | weight " +
                        actualWeight +
                        ".");
                }
            }
            else
            {
                resolvedFontName = "Default";
                resolvedFontWeight =
                    language == DevToolUiLanguage.Chinese
                        ? DevToolUiSettings.DefaultChineseFontWeight
                        : DevToolUiSettings.DefaultFontWeight;
                resolvedFontWeightVariantCount = 1;

                if (requireChinese &&
                    Interlocked.Exchange(
                        ref cjkFontMissingLogged,
                        1) == 0)
                {
                    log?.LogWarning(
                        "DryCycle DevTool could not resolve a local Simplified Chinese font from " +
                        DevToolFontCatalog.FontDirectory +
                        ". The UI remains active with the context default font.");
                }
            }
        }

        if (activeFont.NativePtr == null)
            return false;

        if (TryPushRegisteredFont(activeFont, "active UI font"))
            return true;

        activeFont = default;
        resolvedFontName = "Default";
        resolvedFontWeight = DevToolUiSettings.DefaultFontWeight;
        resolvedFontWeightVariantCount = 1;
        DevToolGlyphs.ResetCache();
        return false;
    }

}

internal sealed class DevToolInputContext : IMGUIContext
{
    public override void Render(ref nint idxgiSwapChain, ref uint syncInterval, ref uint flags)
    {
        DevToolFrontend.RenderFromContext(ref idxgiSwapChain, ref syncInterval, ref flags);
    }

    public override bool BlockWMEvent() => false;

    public override void OnDestroyed()
    {
        DevToolFrontend.NotifyContextDestroyedFromRwImGui();
    }
}

