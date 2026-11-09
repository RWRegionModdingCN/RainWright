using System;
using System.Linq;
using Menu.Remix.MixedUI;
using RainWright.Remix;

namespace DryCycle.DevUI.DevTool.Personalization;

internal static class RainWrightAboutPage
{
    internal static OpTab Create(OptionInterface owner)
    {
        // The native Mod description remains the sole copy of the introduction.
        // Its explicit paragraph breaks separate the sections and credit footer.
        var mod = ModManager.InstalledMods.FirstOrDefault(item => item.id == "rainwright");
        if (mod == null) throw new InvalidOperationException("RainWright Mod metadata is unavailable.");
        var tab = new OpTab(owner, Text("tab"));
        try
        {
            string requirements = string.Join(", ", mod.requirementsNames ?? Array.Empty<string>());
            var view = new RemixModDetailsLayout().CreateView(tab, Content(mod),
                new RemixModDetailsMetadata(mod.id, mod.hideVersion ? string.Empty : mod.version,
                    mod.authors, requirements, mod.DLCMissing || ModManager.FailedRequirementIds.Contains(mod.id)));
            tab.OnPreUnload += () => view.Dispose();
            return tab;
        }
        catch (Exception failure)
        {
            try { tab._Unload(); }
            catch (Exception cleanup) { throw new AggregateException("About tab failed and cleanup is incomplete.", failure, cleanup); }
            throw;
        }
    }

    internal static IRemixAboutPageContent Content(ModManager.Mod mod)
    {
        string[] paragraphs = mod.LocalizedDescription.Split(new[] { "<LINE><LINE>" }, StringSplitOptions.None);
        RemixAboutSection[] sections;
        string creditHeading = string.Empty, creditText = string.Empty;
        if (paragraphs.Length == 3 || paragraphs.Length == 4)
        {
            sections = Enumerable.Range(0, 3).Select(i => new RemixAboutSection(string.Empty,
                paragraphs[i].Replace("<LINE>", " "))).ToArray();
            if (paragraphs.Length == 4)
            {
                int separator = paragraphs[3].IndexOf("<LINE>", StringComparison.Ordinal);
                creditHeading = separator >= 0 ? paragraphs[3].Substring(0, separator) : paragraphs[3];
                creditText = separator >= 0 ? paragraphs[3].Substring(separator + 6) : string.Empty;
            }
        }
        else
        {
            // If a pack author replaces the description, keep their entire text.
            sections = new[] { new RemixAboutSection(string.Empty, mod.LocalizedDescription) };
        }
        return new RemixAboutPageContent(mod.LocalizedName, Text("subtitle"), sections,
            creditHeading, creditText);
    }

    private static string Text(string suffix) => OptionInterface.Translate("rainwright-about-" + suffix);
}
