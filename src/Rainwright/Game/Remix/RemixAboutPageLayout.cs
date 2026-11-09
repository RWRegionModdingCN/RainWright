using System;
using System.Collections.Generic;
using System.Linq;
using Menu;
using Menu.Remix.MixedUI;
using UnityEngine;

namespace RainWright.Remix;

/// <summary>Call on the game UI thread, inside OptionInterface.Initialize.</summary>
public interface IRemixAboutPageLayout
{
    /// <summary>
    /// Returns a new tab without modifying owner.Tabs. The caller adds it to that
    /// array; normal Remix tab unloading owns the controls and scroll camera.
    /// </summary>
    OpTab CreateTab(OptionInterface owner, string tabName, IRemixAboutPageContent content);
}

/// <summary>Rain World's native fonts, muted frame, left-aligned sections and fixed credits.</summary>
public sealed class RemixAboutPageLayout : IRemixAboutPageLayout
{
    private const float BodyWidth = 482f;
    private sealed class Block
    {
        internal readonly OpLabelLong Label;
        internal readonly float GapAfter;
        internal Block(OpLabelLong label, float gapAfter) { Label = label; GapAfter = gapAfter; }
    }

    public OpTab CreateTab(OptionInterface owner, string tabName, IRemixAboutPageContent content)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (content == null) throw new ArgumentNullException(nameof(content));
        if (content.Sections == null) throw new ArgumentException("Sections are required.", nameof(content));

        // Snapshot custom providers before creating controls, including potentially
        // mutable collections. A failed build must not leave a scroll camera alive.
        content = new RemixAboutPageContent(content.Title, content.Subtitle, content.Sections,
            content.CreditsHeading, content.CreditsText, content.CoverAtlasElement);
        var owned = new List<UIelement>();
        OpTab tab = null;
        try
        {
            tab = new OpTab(owner, tabName ?? string.Empty);
            tab.AddItems(Own(owned, new OpRect(new Vector2(20, 20), new Vector2(560, 560), .22f)
            {
                colorEdge = MenuColorEffect.rgbMediumGrey,
                colorFill = MenuColorEffect.rgbBlack
            }));
            tab.AddItems(Own(owned, new OpLabel(new Vector2(44, 516), new Vector2(512, 44), content.Title,
                FLabelAlignment.Left, true) { color = MenuColorEffect.rgbWhite }));
            tab.AddItems(Own(owned, new OpLabel(new Vector2(44, 483), new Vector2(512, 28), content.Subtitle,
                FLabelAlignment.Left) { color = MenuColorEffect.rgbMediumGrey }));

            float bodyTop = 464;
            if (!string.IsNullOrEmpty(content.CoverAtlasElement))
            {
                // Atlas images are borrowed. OpImage(string) does not create or destroy
                // an FTexture, preventing caller texture ownership/name collisions.
                var cover = Own(owned, new OpImage(Vector2.zero, content.CoverAtlasElement));
                float factor = Mathf.Min(512f / cover.size.x, 140f / cover.size.y);
                cover.scale = new Vector2(factor, factor);
                cover.SetPos(new Vector2((600f - cover.size.x) / 2f, bodyTop - cover.size.y));
                bodyTop -= cover.size.y + 18f;
                tab.AddItems(cover);
            }

            var blocks = new List<Block>();
            foreach (var section in content.Sections)
            {
                if (section == null) throw new ArgumentException("Sections cannot contain null.", nameof(content));
                if (!string.IsNullOrEmpty(section.Heading)) blocks.Add(new Block(Label(owned, section.Heading, MenuColorEffect.rgbWhite), 6));
                blocks.Add(new Block(Label(owned, section.Text, MenuColorEffect.rgbMediumGrey), 22));
            }

            var creditBlocks = new List<Block>();
            if (!string.IsNullOrEmpty(content.CreditsHeading)) creditBlocks.Add(new Block(Label(owned, content.CreditsHeading, MenuColorEffect.rgbWhite), 6));
            if (!string.IsNullOrEmpty(content.CreditsText)) creditBlocks.Add(new Block(Label(owned, content.CreditsText, MenuColorEffect.rgbMediumGrey), 0));
            float creditsHeight = Height(creditBlocks);
            float bodyBottom = 44;
            if (creditsHeight > 120)
            {
                // Extensive credits remain readable in the main scroll area, rather
                // than overflowing a fixed footer or being silently truncated.
                blocks.AddRange(creditBlocks);
            }
            else if (creditsHeight > 0)
            {
                Place(creditBlocks, 44, 44 + creditsHeight);
                tab.AddItems(creditBlocks.Select(block => block.Label).ToArray());
                bodyBottom += creditsHeight + 22;
            }

            float viewHeight = bodyTop - bodyBottom;
            float contentHeight = Mathf.Max(viewHeight, Height(blocks) + 24);
            if (contentHeight > OpScrollBox.MAXCONTENTSIZE)
                throw new ArgumentException("The about page exceeds Remix's scroll content limit.", nameof(content));

            var body = Own(owned, new OpScrollBox(new Vector2(34, bodyBottom), new Vector2(532, viewHeight), contentHeight,
                hasBack: false) { colorEdge = MenuColorEffect.rgbMediumGrey });
            tab.AddItems(body);
            Place(blocks, 14, contentHeight - 12);
            body.AddItems(blocks.Select(block => block.Label).ToArray());
            body.ScrollToTop();
            return tab;
        }
        catch (Exception failure)
        {
            var cleanupFailures = new List<Exception>();
            var unattached = owned.Where(element => element.tab == null).ToArray();
            try { tab?._Unload(); }
            catch (Exception cleanup) { cleanupFailures.Add(cleanup); }
            foreach (var element in unattached)
            {
                try { element.Unload(); }
                catch (Exception cleanup) { cleanupFailures.Add(cleanup); }
            }
            if (cleanupFailures.Count > 0)
                throw new AggregateException("About page creation failed; UI cleanup also failed.",
                    new[] { failure }.Concat(cleanupFailures));
            throw;
        }
    }

    private static T Own<T>(List<UIelement> owned, T element) where T : UIelement
    {
        owned.Add(element);
        return element;
    }

    private static OpLabelLong Label(List<UIelement> owned, string text, Color color)
    {
        // Force native wrapping even when the selected game language is Japanese
        // and the caller supplies an English fallback. No custom font renderer.
        string wrapped = (text ?? string.Empty).WrapText(false, BodyWidth, forceWrapping: true);
        var label = Own(owned, new OpLabelLong(Vector2.zero, new Vector2(BodyWidth, 600), wrapped,
            autoWrap: false, FLabelAlignment.Left) { color = color });
        label.size = new Vector2(BodyWidth, Mathf.Max(label.LineHeight, Mathf.Ceil(label.GetDisplaySize().y) + 4));
        return label;
    }

    private static float Height(List<Block> blocks)
    {
        float height = 0;
        foreach (var block in blocks) height += block.Label.size.y + block.GapAfter;
        return height - (blocks.Count > 0 ? blocks[blocks.Count - 1].GapAfter : 0);
    }

    private static void Place(List<Block> blocks, float x, float top)
    {
        foreach (var block in blocks)
        {
            top -= block.Label.size.y;
            block.Label.SetPos(new Vector2(x, top));
            top -= block.GapAfter;
        }
    }
}
