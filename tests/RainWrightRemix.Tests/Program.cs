using DryCycle.DevUI.DevTool.Personalization;
using Menu.Remix.MixedUI;
using RainWright.Remix;
using Menu.Remix;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void CheckBody(OpTab tab)
{
    var body = tab.items.OfType<OpScrollBox>().Single();
    Check(body.size.y > 0 && body.pos.y + body.size.y <= 532, "Scroll viewport fits below the navigation.");
    var labels = body.items.OfType<OpLabelLong>().OrderByDescending(label => label.pos.y).ToArray();
    Check(labels.All(label => label.pos.y >= 0 && label.pos.y + label.size.y <= body.contentSize), "All content is reachable inside the scroll canvas.");
    Check(labels.Zip(labels.Skip(1)).All(pair => pair.First.pos.y >= pair.Second.pos.y + pair.Second.size.y), "Sections do not overlap.");
    Check(labels.All(label => label.alignment == FLabelAlignment.Left), "Body remains left aligned.");
    Check(tab.items.OfType<OpSimpleButton>().All(button => button.scrollBox == null), "Chapter navigation remains fixed while the cover and document scroll.");
}
Dictionary<string, string> Read(string code)
{
    string path = Path.Combine("mod", "text", "text_" + code, "strings.txt");
    return File.ReadAllText(path)[1..].Replace("\r\n", "\n").Split('\n').Where(line => line.Contains('|'))
        .Select(line => line.Split('|')).ToDictionary(pair => pair[0], pair => pair[1]);
}
var english = Read("eng");
foreach (string code in new[] { "eng", "chi", "tra" })
{
    OptionInterface.Translations = new(english);
    foreach (var pair in Read(code)) OptionInterface.Translations[pair.Key] = pair.Value;
    var owner = new OptionInterface { Tabs = Array.Empty<OpTab>() };
    var tab = RainWrightAboutPage.Create(owner);
    CheckBody(tab);
    Check(owner.Tabs.Length == 0, "Factory does not replace the caller's tabs.");
    Check(tab.name == OptionInterface.Translate("rainwright-about-tab"), "Tab uses selected game language.");
    Check(tab.items.OfType<OpLabelLong>().Count(label => label.scrollBox == null) == 1, "Only required Mods remain in the fixed footer; no empty credit row is reserved.");
    string rendered = string.Join("", tab.items.OfType<OpLabel>().Select(label => label.text.Replace("\n", "")));
    Check(rendered.Contains("Cornifer") && rendered.Replace(" ", "").Contains("WorldEditor"), "Introduction contains the supplied tool names across wrapped lines.");
    Check(!rendered.Contains("Moyang", StringComparison.OrdinalIgnoreCase) && !rendered.Contains("墨阳") && !rendered.Contains("墨陽"),
        "The removed cover credit is absent in every supported language.");
    Check(tab.items.OfType<OpLabelLong>().Count(label => label.scrollBox != null) == 3,
        "Introduction contains only the three supplied paragraphs, without section headings.");
    tab._Unload();
    Check(OpScrollBox.ActiveCameras == 0, "Normal tab unloading releases the camera.");
}
IRemixAboutPageLayout layout = new RemixAboutPageLayout();
var section = new RemixAboutSection("Features", new string('x', 3000));
var content = new RemixAboutPageContent("Other Mod", "Other toolkit", new[] { section }, "Credits", "Cover: Other Author", "other-atlas");
LabelTest.Japanese = true;
var otherOwner = new OptionInterface();
var otherTab = layout.CreateTab(otherOwner, "About", content);
CheckBody(otherTab);
var cover = otherTab.items.OfType<OpImage>().Single();
Check(cover.size.x <= 512 && cover.size.y <= 140 && Math.Abs(cover.size.x / cover.size.y - 4) < .01, "Borrowed cover fits without distortion.");
Check(otherTab.items.OfType<OpScrollBox>().Single().pos.y + otherTab.items.OfType<OpScrollBox>().Single().size.y < cover.pos.y, "Cover and body do not overlap.");
Check(otherTab.items.OfType<OpScrollBox>().Single().items.OfType<OpLabelLong>().All(label => label.text.Split('\n').All(line => line.Length <= 60)), "English fallback wraps with Japanese selected.");
otherTab._Unload();
var longCredits = layout.CreateTab(otherOwner, "About", new RemixAboutPageContent("Other Mod", "", new[] { section }, "Credits", new string('c', 1200)));
CheckBody(longCredits);
Check(longCredits.items.OfType<OpLabelLong>().All(label => label.scrollBox != null), "Long credits move into the scroll canvas without truncation.");
longCredits._Unload();
OpScrollBox.FailAdd = true;
try { layout.CreateTab(otherOwner, "About", content); throw new Exception("Failure was hidden."); }
catch (InvalidOperationException failure) { Check(failure.Message.Contains("Injected"), "Original attachment failure is preserved."); }
finally { OpScrollBox.FailAdd = false; }
Check(OpScrollBox.ActiveCameras == 0 && OpTab.Created.Last().unloaded, "Failed page construction releases the tab and camera.");
try { layout.CreateTab(otherOwner, "About", new RemixAboutPageContent("Huge", "", new[] { new RemixAboutSection("", new string('x', 40000)) })); throw new Exception("Overflow was hidden."); }
catch (ArgumentException) { Check(OpTab.Created.Last().unloaded, "Native scroll limit failure cleans up its tab."); }
OptionInterface.Translations = new(english) { ["rainwright-description"] = "Custom replacement introduction" };
var replacement = RainWrightAboutPage.Create(new OptionInterface());
Check(replacement.items.OfType<OpLabelLong>().Any(label => label.text == "Custom replacement introduction"), "A pack author's replacement description is preserved.");
replacement._Unload();
// Exercise the exact entry point from the screenshot, including switching away,
// native warnings, opt-in registration, and unloading a live details tab.
OptionInterface.Translations = new(english);
foreach (var pair in Read("chi")) OptionInterface.Translations[pair.Key] = pair.Value;
var logger = new BepInEx.Logging.ManualLogSource();
try { RemixModDetailsBridge.Enable(logger); throw new Exception("Early startup was accepted."); }
catch (InvalidOperationException failure)
{ Check(failure.Message.Contains("PreModsInit") && !RemixModDetailsBridge.HasActiveRegistrations, "Before Remix text initialization, reject setup without touching ModButton or installing a hook."); }
OptionalText.engText = new[] { "Ready" };
RemixModDetailsBridge.Enable(logger);
var stats = new InternalOI_Stats();
var rainButton = new MenuModList.ModButton(ModManager.InstalledMods[0]);
On.Menu.Remix.InternalOI_Stats.Invoke(stats, rainButton);
Check(stats.NativeCalls == 1 && stats.lblName.Hidden, "The actual Mod details entry point mounts the new presentation.");
Check(!stats.Tabs[1].items.OfType<OpSimpleButton>().Any(), "Details no longer add chapter buttons.");
var detailsBody = stats.Tabs[1].items.OfType<OpScrollBox>().Single(box => !box.Hidden);
Check(detailsBody.size.y > 200, "Details replace the stock 100-pixel description viewport.");
Check(stats.Tabs[1].items.OfType<OpLabel>().Any(label => label.text == "雨匠" && label.label.scale > 1), "The details hero has a larger localized title.");
Check(!stats.btnWorkshopUpload.Hidden, "The native upload action remains available.");
Check(stats.Tabs[1].items.OfType<OpLabel>().Any(label => label.text == "封面图" && label.pos.x == RemixModDetailsLayout.CoverX + 12),
    "A fixed cover slot is visible even when no image is supplied.");
var fixedTitle = stats.Tabs[1].items.OfType<OpLabel>().Last(label => label.text == "雨匠");
Check(fixedTitle.scrollBox == null && fixedTitle.alignment == FLabelAlignment.Right && fixedTitle.pos.y >= 540,
    "The gilded title is fixed at the upper right, outside the scrolling cover.");
Check(fixedTitle.color.r == RemixTitleGold.Primary.X && fixedTitle.color.g == RemixTitleGold.Primary.Y,
    "Native details share the editor's existing primary gold palette.");
Check(stats.Tabs[1].items.OfType<OpLabel>().Any(label => label.text.StartsWith("v0.1.9") && label.scrollBox == null
    && label.alignment == FLabelAlignment.Right && label.pos.y > detailsBody.pos.y + detailsBody.size.y),
    "Version and author remain fixed above the document.");
var surface = stats.Tabs[1].items.OfType<RemixDetailsChrome>().Single(chrome => chrome.scrollBox == null);
Check(surface.myContainer.sprites.Count == 2 && surface.myContainer.sprites.All(sprite => sprite.name.StartsWith("RainWright.Remix.Glass.")),
    "The page is one smooth optical surface, with no grain overlay or decorative stripes.");
Check(Futile.atlasManager.textures.Count == 2 && UnityEngine.Texture2D.Active == 2,
    "All glass pages borrow the shared body and reflection textures.");
var glass = Futile.atlasManager.textures.Single(entry => entry.Key.Contains(".Body.")).Value;
var light = Futile.atlasManager.textures.Single(entry => entry.Key.Contains(".Light.")).Value;
UnityEngine.Color32 Pixel(int x, int y) => glass.pixels[y * glass.width + x];
Check(Pixel(0, 0).a == 0 && Pixel(3, 3).a == 0, "Rounded glass corners remain transparent.");
Check(Pixel(256, 256).a * surface.myContainer.sprites[0].alpha is > 90 and < 128, "The center remains translucent enough for the native menu background to show through.");
Check(light.pixels[508 * light.width + 256].a > light.pixels[3 * light.width + 256].a
    && light.pixels[508 * light.width + 256].a > light.pixels[256 * light.width + 256].a,
    "Directional edge light creates a brighter top rim instead of a uniform white outline.");
Check(Enumerable.Range(160, 190).All(x => Enumerable.Range(160, 190).All(y =>
    Math.Abs(Pixel(x, y).r - Pixel(x + 1, y).r) <= 1 && Math.Abs(Pixel(x, y).a - Pixel(x + 1, y).a) <= 1)),
    "The interior reflection is smooth at neighboring pixels, without random grain or hard reflection bands.");
Check(glass.wrapMode == UnityEngine.TextureWrapMode.Clamp && glass.filterMode == UnityEngine.FilterMode.Bilinear,
    "The material uses smooth sampling and does not repeat at its border.");
Check(RemixGlassMaterial.GenerationCount == 1, "Glass pixel geometry is generated once across all page constructions.");
Check(detailsBody.items.OfType<OpLabelLong>().All(label => label.text.Split('\n').All(line => LabelTest.GetWidth(line) <= RemixModDetailsLayout.BodyTextWidth)
    && label.pos.x + label.size.x <= detailsBody.size.x - 50), "Mixed-language lines stay within measured text bounds, leaving an independent slider lane.");
Check(RemixModDetailsLayout.CoverX + RemixModDetailsLayout.CoverWidth <= detailsBody.size.x - 35,
    "The full-size cover remains clear of the native slider without shrinking.");
detailsBody.ScrollToTop();
foreach (var label in detailsBody.items.OfType<OpLabelLong>())
    Check(label.pos.y >= 0 && label.pos.y + label.size.y <= detailsBody.contentSize, "Details text remains within the scroll canvas.");
if (args.Length > 0)
{
    // Export the actual presentation commands for a preview, not a separate mock layout.
    var elements = stats.Tabs[1].items.Where(element => !element.Hidden).ToArray();
    var snapshot = new
    {
        shapes = elements.OfType<RemixDetailsChrome>().SelectMany(chrome => chrome.myContainer.sprites.Where(sprite => sprite.name == "pixel").Select(sprite => new
            { x = sprite.x + chrome.pos.x + (chrome.scrollBox?.pos.x ?? 0),
                y = sprite.y + chrome.pos.y + (chrome.scrollBox?.pos.y ?? 0) + (chrome.scrollBox?.targetScrollOffset ?? 0),
                width = sprite.scaleX, height = sprite.scaleY, sprite.color.r, sprite.color.g, sprite.color.b, sprite.alpha })),
        labels = elements.OfType<OpLabel>().Select(label => new
        {
            x = label.pos.x + (label.scrollBox?.pos.x ?? 0),
            y = label.pos.y + (label.scrollBox?.pos.y ?? 0) + (label.scrollBox?.targetScrollOffset ?? 0),
            width = label.size.x, height = label.size.y, label.text, scale = label.label.scale,
            large = label.label.scale > 1, label.color.r, label.color.g, label.color.b, alpha = label.alpha, alignment = label.alignment.ToString(),
            clip = label.scrollBox == null ? null : new { x = label.scrollBox.pos.x, y = label.scrollBox.pos.y,
                width = label.scrollBox.size.x, height = label.scrollBox.size.y }
        }),
        cards = elements.OfType<OpSimpleButton>().Select(card => new { x = card.pos.x, y = card.pos.y,
            width = card.size.x, height = card.size.y, card.text }),
        viewport = new { x = detailsBody.pos.x, y = detailsBody.pos.y, width = detailsBody.size.x, height = detailsBody.size.y },
        glassLayers = surface.myContainer.sprites.Select(sprite => new {
            rgba = Convert.ToBase64String(Futile.atlasManager.textures[sprite.name].pixels.SelectMany(pixel => new[] { pixel.r, pixel.g, pixel.b, pixel.a }).ToArray()),
            width = glass.width, height = glass.height, sprite.alpha })
    };
    File.WriteAllText(args[0], System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}
On.Menu.Remix.InternalOI_Stats.Invoke(stats, rainButton);
Check(stats.NativeCalls == 2 && OpScrollBox.ActiveCameras == 2, "Repeated previews replace the view without leaking native or custom cameras.");
Check(Futile.atlasManager.textures.Count == 2 && UnityEngine.Texture2D.Active == 2, "Repeated previews do not leak glass textures.");
stats.HasThumbnail = true;
On.Menu.Remix.InternalOI_Stats.Invoke(stats, rainButton);
var borrowedCover = stats.Tabs[1].items.OfType<OpImage>().Single(image => !image.Hidden);
Check(borrowedCover.sprite.element.name == "native-cover", "The native Mod thumbnail fills the reserved cover slot.");
var coverFrame = stats.Tabs[1].items.OfType<RemixDetailsChrome>().Single(chrome => chrome.scrollBox != null);
Check(coverFrame.size.x > 500 && coverFrame.size.y > 280 && coverFrame.scrollBox == borrowedCover.scrollBox,
    "The original-style large cover spans the scrolling document instead of sharing width with text.");
Check(borrowedCover.pos.x >= RemixModDetailsLayout.CoverX && borrowedCover.pos.y >= coverFrame.pos.y
    && borrowedCover.pos.x + borrowedCover.size.x <= RemixModDetailsLayout.CoverX + RemixModDetailsLayout.CoverWidth
    && borrowedCover.pos.y + borrowedCover.size.y <= coverFrame.pos.y + RemixModDetailsLayout.CoverHeight,
    "Non-16:9 covers fit entirely in the reserved frame without cropping.");
stats.HasThumbnail = false;
var nativeCount = 10;
var other = new ModManager.Mod { id = "unregistered" };
On.Menu.Remix.InternalOI_Stats.Invoke(stats, new MenuModList.ModButton(other));
Check(OpScrollBox.ActiveCameras == 0 && stats.Tabs[1].items.Count == nativeCount && !stats.lblName.Hidden,
    "Switching to an unregistered Mod restores the native panel and cached controls.");
Check(Futile.atlasManager.textures.Count == 0 && UnityEngine.Texture2D.Active == 0, "Switching Mods releases all frosted surface resources.");
using (var registration = RemixModDetails.Register("other", new FixtureProvider()))
{
    On.Menu.Remix.InternalOI_Stats.Invoke(stats, new MenuModList.ModButton(new ModManager.Mod { id = "other" }));
    Check(stats.lblName.Hidden && OpScrollBox.ActiveCameras == 1, "Other Mods can opt in through the public interface.");
}
Check(!stats.lblName.Hidden && OpScrollBox.ActiveCameras == 0, "Disposing a provider registration restores a live native view.");
using (var registration = RemixModDetails.Register("broken", new FixtureProvider(true)))
{
    On.Menu.Remix.InternalOI_Stats.Invoke(stats, new MenuModList.ModButton(new ModManager.Mod { id = "broken" }));
    Check(!stats.lblName.Hidden && OpScrollBox.ActiveCameras == 0 && logger.Errors.Last().Contains("fixture provider failure"),
        "Provider failure logs the original exception and preserves native details.");
}
ModManager.FailedRequirementIds.Add("rainwright");
stats.lblRequirements.text = "Missing required Mod";
On.Menu.Remix.InternalOI_Stats.Invoke(stats, rainButton);
Check(stats.Tabs[1].items.OfType<OpLabelLong>().Any(label => label.text == "Missing required Mod" && label.color.r > label.color.g),
    "Native requirement failures remain visible with warning color.");
stats.Tabs[1]._Unload();
Check(OpScrollBox.ActiveCameras == 0, "Closing the real details tab releases the custom view before native tab unloading.");
RemixModDetailsBridge.Disable();
Check(stats.Tabs[1].items.Count == nativeCount + 1 && !stats.Tabs[1].items.OfType<RemixDetailsChrome>().Any()
    && !stats.Tabs[1].items.OfType<OpSimpleButton>().Any(), "Boundary cleanup retains the game's cached elements and stock description box only.");
var options = new RainWrightOptions();
options.Initialize();
Check(options.Tabs.Length == 7, "The actual options initializer creates About, the existing five settings tabs, and Appearance.");
Check(options.Tabs.SelectMany(tab => tab.items).OfType<OpUpdown>().Count() == 11,
    "All eleven numeric control call sites initialize with valid ranges.");
Check(options.config.bindings.Count == 44 && options.config.bindings.TryGetValue("Key_top_status", out var headerBinding)
    && headerBinding is Configurable<string> headerKey && headerKey.Value == "",
    "The settings UI retains its preferences and includes the unbound top-status shortcut.");
Check(options.Tabs.SelectMany(tab => tab.items.OfType<OpUpdown>().Select(control => (Tab: tab, Control: control))).All(item =>
    item.Tab.items.OfType<OpLabel>().Where(label => label.pos.y == item.Control.pos.y - 5).All(label =>
        label.pos.x + label.size.x <= item.Control.pos.x - 20 && LabelTest.GetWidth(label.text) <= label.size.x)),
    "Numeric setting labels wrap within their column and stay clear of every input field.");
Check(options.Tabs.All(tab => tab.items.OfType<RemixDetailsChrome>().Any(chrome => chrome.scrollBox == null)), "Every settings tab uses the same glass material as About.");
Check(RemixGlassMaterial.Users == 7 && Futile.atlasManager.textures.Count == 2 && RemixGlassMaterial.GenerationCount == 1,
    "Seven settings tabs share two GPU textures and the single cached pixel generation.");
var appearance = DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.Capture();
appearance.RemixGlassOpacity = .7f; appearance.RemixReflection = .2f;
appearance.RemixGildedTitle = false; appearance.RemixSectionHeadings = false; appearance.RemixSectionGap = 35;
DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.Publish(appearance);
var settingsSurface = options.Tabs[1].items.OfType<RemixDetailsChrome>().Single();
settingsSurface.GrafUpdate(0);
Check(settingsSurface.myContainer.sprites[0].alpha == .7f && settingsSurface.myContainer.sprites[1].alpha == .2f
    && Futile.atlasManager.textures.Count == 2, "Opacity and reflections update independently without generating or uploading new textures.");
var customized = RainWrightAboutPage.Create(new OptionInterface());
Check(customized.items.OfType<OpLabel>().Count(label => label.text == "雨匠") == 1,
    "Disabling the gold effect uses one plain title layer.");
Check(customized.items.OfType<OpLabelLong>().Count(label => label.scrollBox != null) == 3,
    "Disabling section headings preserves the three complete introduction paragraphs.");
var customParagraphs = customized.items.OfType<OpLabelLong>().Where(label => label.scrollBox != null).OrderByDescending(label => label.pos.y).ToArray();
Check(customParagraphs.Zip(customParagraphs.Skip(1)).All(pair => pair.First.pos.y - (pair.Second.pos.y + pair.Second.size.y) == 35),
    "Custom paragraph spacing is applied to rendered geometry.");
customized._Unload();
DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.Publish(new());
Check(RemixGlassMaterial.Users == 7 && UnityEngine.Texture2D.Active == 2, "Unloading a borrowed page preserves materials still in use by settings tabs.");
var presetsTab = options.Tabs[5];
Check(presetsTab.items.OfType<OpComboBox>().Single().value == "Room", "The preset selector initializes to the built-in Room preset.");
Check(presetsTab.items.OfType<OpTextBox>().Count() == 4 && presetsTab.items.OfType<OpSimpleButton>().Count() == 6,
    "Preset initialization reaches metadata, import and sharing controls.");
ConfigContainer.ActiveInterface = options;
var controller = new ConfigTabController { _tabButtons = Enumerable.Range(0, 7).Select(index => new ConfigTabController.TabSelectButton
    { pos = new(520, 600 - index * 80), size = new(30, 74) }).ToArray() };
ConfigContainer.menuTab = new() { tabCtrler = controller };
var firstPosition = controller._tabButtons[0].pos;
controller._tabButtons[0].SetNextFocusable(UIfocusable.NextDirection.Right, controller._tabButtons[0]);
options.Tabs[0].items.OfType<RemixSettingsNavigation>().Single().GrafUpdate(0);
Check(controller._tabButtons.All(button => button._label.rotation == 0 && button.pos.y == 732 && button.pos.y + button.size.y <= 768),
    "Native tabs become a horizontal top row within the game's screen height.");
Check(controller._tabButtons.Zip(controller._tabButtons.Skip(1)).All(pair => pair.First.pos.x + pair.First.size.x < pair.Second.pos.x),
    "Horizontal tab hit rectangles do not overlap.");
Check(controller._tabButtons.All(button => LabelTest.GetWidth(button._label.text) <= button.size.x - 10), "Localized tab captions fit horizontally.");
Check(controller._tabButtons[0].NextFocusable[(int)UIfocusable.NextDirection.Right] == controller._tabButtons[1], "Controller focus follows the horizontal tab order.");
options.Deactivate();
Check(controller._tabButtons[0].pos.x == firstPosition.x && controller._tabButtons[0].pos.y == firstPosition.y
    && controller._tabButtons[0]._label.rotation == -90 && controller._rectCanvas.fillAlpha == .5f
    && controller._tabButtons[0].NextFocusable[(int)UIfocusable.NextDirection.Right] == controller._tabButtons[0],
    "Leaving RainWright restores the native layout, appearance and focus bindings for other Mods.");
ConfigContainer.ActiveInterface = new OptionInterface();
options.Tabs[0].items.OfType<RemixSettingsNavigation>().Single().GrafUpdate(0);
Check(controller._tabButtons[0].pos.x == firstPosition.x, "Inactive RainWright presenters do not modify another Mod's navigation.");
foreach (var tab in options.Tabs) tab._Unload();
Check(OpScrollBox.ActiveCameras == 0 && Futile.atlasManager.textures.Count == 0 && UnityEngine.Texture2D.Active == 0,
    "Closing the complete settings UI releases its document camera and frosted textures.");
Console.WriteLine($"PASS: {checks} Remix presentation geometry, language and lifecycle checks (font metrics are a headless fixture).");

sealed class FixtureProvider(bool fail = false) : IRemixModDetailsProvider
{
    public IRemixAboutPageContent CreateContent(ModManager.Mod mod) => fail ? throw new InvalidOperationException("fixture provider failure") :
        new RemixAboutPageContent("Other Mod", "Other toolkit", new[] { new RemixAboutSection("Features", "Other content") });
}
