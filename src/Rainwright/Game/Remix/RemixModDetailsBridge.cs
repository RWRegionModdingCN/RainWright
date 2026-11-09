using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Personalization;
using DryCycle.DevUI.DevTool.RWImGui;
using Menu.Remix;
using Menu.Remix.MixedUI;

namespace RainWright.Remix;

// The sole external HookGen boundary. Layouts and providers use normal interfaces.
internal static class RemixModDetailsBridge
{
    private sealed class RainWrightProvider : IRemixModDetailsProvider
    {
        public IRemixAboutPageContent CreateContent(ModManager.Mod mod) => RainWrightAboutPage.Content(mod);
    }
    private sealed class Mounted
    {
        internal readonly InternalOI_Stats Stats;
        internal readonly string ModId;
        internal readonly RemixModDetailsView View;
        internal readonly (UIelement Element, bool Hidden)[] Native;
        internal readonly OnEventHandler PreUnload;
        internal Mounted(InternalOI_Stats stats, string modId, RemixModDetailsView view)
        {
            Stats = stats; ModId = modId; View = view;
            Native = new UIelement[] { stats.lblName, stats.lblID, stats.lblVersion, stats.lblTargetVersion,
                stats.lblAuthors, stats.btnAuthorExtra, stats.lblDescription, stats.lblRequirements,
                stats.imgThumbnail, stats.boxDescription }.Where(element => element != null)
                .Select(element => (element, element.Hidden)).ToArray();
            PreUnload = () => RemixModDetailsBridge.Release(stats);
        }
        internal void Mount()
        {
            foreach (var item in Native) item.Element.Hide();
            Stats.Tabs[1].OnPreUnload += PreUnload;
            Stats.btnWorkshopUpload?.MoveToFront();
        }
        internal void Release()
        {
            View.Dispose();
            Stats.Tabs[1].OnPreUnload -= PreUnload;
            foreach (var item in Native)
            { if (item.Hidden) item.Element.Hide(); else item.Element.Show(); }
        }
    }
    private static ManualLogSource log;
    private static readonly FrontendHookLedger hooks = new((name, failure) => log?.LogError(name + ": " + failure));
    private static readonly Dictionary<InternalOI_Stats, Mounted> mounted = new();
    private static IDisposable rainWrightRegistration;
    internal static bool HasActiveRegistrations => hooks.HasActiveRegistrations;

    internal static void Enable(ManualLogSource logger)
    {
        if (hooks.HasActiveRegistrations) return;
        if (OptionalText.engText == null)
            throw new InvalidOperationException("Remix text is not initialized; details hooks must be installed after RainWorld.PreModsInit.");
        log = logger;
        hooks.Install("Remix Mod details preview",
            () => On.Menu.Remix.InternalOI_Stats._PreviewMod += Preview,
            () => On.Menu.Remix.InternalOI_Stats._PreviewMod -= Preview);
        rainWrightRegistration = RemixModDetails.Register("rainwright", new RainWrightProvider());
        log?.LogInfo("RainWright Remix details registered after native Remix initialization.");
    }
    internal static void Disable()
    {
        foreach (var stats in mounted.Keys.ToArray()) Release(stats);
        rainWrightRegistration?.Dispose();
        rainWrightRegistration = null;
        if (!hooks.ReleaseAll()) throw new InvalidOperationException("Mod details preview hook cleanup is incomplete.");
    }
    internal static void ReleaseMod(string modId)
    {
        foreach (var item in mounted.Values.Where(item => item.ModId == modId).ToArray()) Release(item.Stats);
    }
    private static void Release(InternalOI_Stats stats)
    {
        if (!mounted.TryGetValue(stats, out var state)) return;
        state.Release();
        mounted.Remove(stats);
    }
    private static void Preview(On.Menu.Remix.InternalOI_Stats.orig__PreviewMod orig, InternalOI_Stats self, MenuModList.ModButton button)
    {
        // Always release our own view before the original method reuses its cached
        // controls. No wholesale replacement of the native tab or requirements logic.
        Release(self);
        orig(self, button);
        var mod = button.itf.mod;
        if (!RemixModDetails.TryGet(mod.id, out var entry)) return;
        RemixModDetailsView view = null;
        try
        {
            string requirements = self.lblRequirements.Hidden ? string.Empty : self.lblRequirements.text;
            if (!self.lblTargetVersion.Hidden)
                requirements += "<LINE>" + self.lblTargetVersion.text;
            var metadata = new RemixModDetailsMetadata(mod.id, mod.hideVersion ? string.Empty : mod.version,
                mod.authors, requirements, mod.DLCMissing || ModManager.FailedRequirementIds.Contains(mod.id),
                self.imgThumbnail.Hidden ? null : self.imgThumbnail.sprite?.element?.name);
            view = entry.Layout.CreateView(self.Tabs[1], entry.Provider.CreateContent(mod), metadata);
            var state = new Mounted(self, mod.id, view);
            mounted.Add(self, state);
            state.Mount();
            self.Tabs[1]._Update();
            self.Tabs[1]._GrafUpdate(0);
        }
        catch (Exception failure)
        {
            log?.LogError("Custom Mod details failed for '" + mod.id + "': " + failure);
            try
            {
                if (mounted.ContainsKey(self)) Release(self);
                else view?.Dispose();
                log?.LogWarning("Restored native Mod details for '" + mod.id + "'.");
            }
            catch (Exception cleanup)
            {
                log?.LogError("Custom Mod details recovery is incomplete: " + cleanup);
                throw new AggregateException("Custom Mod details failed and recovery is incomplete.", failure, cleanup);
            }
        }
    }
}
