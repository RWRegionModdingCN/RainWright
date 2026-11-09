using System;
using System.Collections.Generic;
using System.Linq;
using Menu.Remix.MixedUI;
using UnityEngine;
using DryCycle.DevUI.DevTool.Core;

namespace RainWright.Remix;

/// <summary>Translucent glass canvas with a fixed gilded heading and full-width scrolling cover.</summary>
public sealed class RemixModDetailsLayout : IRemixModDetailsLayout
{
    public const float CoverX = 12, CoverWidth = 516, CoverHeight = 290.25f;
    public const float BodyWidth = 570, BodyTextWidth = 480;
    public RemixModDetailsView CreateView(OpTab target, IRemixAboutPageContent content, RemixModDetailsMetadata metadata)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));
        if (content == null) throw new ArgumentNullException(nameof(content));
        var data = new RemixAboutPageContent(content.Title, content.Subtitle, content.Sections,
            content.CreditsHeading, content.CreditsText, content.CoverAtlasElement);
        var owned = new List<UIelement>();
        OpScrollBox body = null;
        var view = new RemixModDetailsView(owned, null, Array.Empty<float>());
        try
        {
            T Add<T>(T element) where T : UIelement
            { owned.Add(element); target.AddItems(element); return element; }
            string coverElement = string.IsNullOrEmpty(data.CoverAtlasElement) ? metadata.CoverAtlasElement : data.CoverAtlasElement;
            Add(new RemixDetailsChrome());
            string meta = string.Join("    /    ", new[] { metadata.Version.Length > 0 ? "v" + metadata.Version : "", metadata.Authors }
                .Where(text => text.Length > 0));
            // Use the existing title palette and the same black outline,
            // upper-left highlight and lower-right extrusion in native Futile.
            OpLabel Gilded(string text, Vector2 position, Vector2 size, bool primary)
            {
                var probe = new OpLabel(position, size, text, FLabelAlignment.Right, primary);
                owned.Add(probe);
                float scale = primary ? Mathf.Min(1.45f, size.x / Mathf.Max(1, probe.GetDisplaySize().x)) : 1;
                if (!RainWrightPersonalSettings.RemixGildedTitle)
                {
                    probe.color = RemixDetailsChrome.Text; probe.label.scale = scale;
                    target.AddItems(probe); return probe;
                }
                OpLabel Layer(float x, float y, System.Numerics.Vector4 tint)
                {
                    var label = Add(new OpLabel(new Vector2(position.x + x, position.y + y), size, text, FLabelAlignment.Right, primary)
                        { color = new Color(tint.X, tint.Y, tint.Z), alpha = tint.W });
                    label.label.scale = scale;
                    return label;
                }
                float stroke = primary ? 1.05f : .75f;
                var outline = new System.Numerics.Vector4(0, 0, 0, .9f);
                foreach (var offset in new[] { new Vector2(-stroke, 0), new Vector2(stroke, 0), new Vector2(0, -stroke),
                    new Vector2(0, stroke), new Vector2(-stroke, -stroke), new Vector2(stroke, -stroke),
                    new Vector2(-stroke, stroke), new Vector2(stroke, stroke) }) Layer(offset.x, offset.y, outline);
                if (primary) Layer(-.9f, .8f, RemixTitleGold.Highlight);
                Layer(primary ? 1.15f : .8f, primary ? -1.35f : -1, primary ? RemixTitleGold.PrimaryRelief : RemixTitleGold.SecondaryRelief);
                probe.color = primary ? new Color(RemixTitleGold.Primary.X, RemixTitleGold.Primary.Y, RemixTitleGold.Primary.Z)
                    : new Color(RemixTitleGold.Secondary.X, RemixTitleGold.Secondary.Y, RemixTitleGold.Secondary.Z);
                probe.label.scale = scale;
                target.AddItems(probe);
                return probe;
            }
            Gilded(data.Title, new Vector2(34, 545), new Vector2(500, 44), true);
            Gilded(meta, new Vector2(34, 517), new Vector2(500, 22), false);
            Add(new OpLabel(new Vector2(34, 489), new Vector2(500, 24), data.Subtitle,
                FLabelAlignment.Right) { color = RemixDetailsChrome.Muted });

            OpLabelLong Measure(string text, Color color, float width = BodyTextWidth)
            {
                var label = new OpLabelLong(Vector2.zero, new Vector2(width, 600),
                    RemixTextLayout.Wrap(text, width), false, FLabelAlignment.Left) { color = color };
                owned.Add(label);
                label.size = new Vector2(width, Mathf.Max(label.LineHeight, Mathf.Ceil(label.GetDisplaySize().y) + 4));
                return label;
            }
            var paragraphs = new List<(OpLabelLong Label, float Gap)>();
            var anchors = new List<float>();
            // The full-width cover leads the scrolling document. Its size never
            // shrinks to make room for text. The heading is outside the document.
            float heroHeight = CoverHeight + 24;
            float total = heroHeight;
            void Block(OpLabelLong label, float gap)
            { paragraphs.Add((label, gap)); total += label.size.y + gap; }
            foreach (var section in data.Sections)
            {
                anchors.Add(total);
                if (RainWrightPersonalSettings.RemixSectionHeadings && section.Heading.Length > 0)
                    Block(Measure(section.Heading, RemixDetailsChrome.Accent), 7);
                Block(Measure(section.Text, RemixDetailsChrome.Text), RainWrightPersonalSettings.RemixSectionGap);
            }

            string credits = string.Join("  /  ", new[] { data.CreditsHeading.TrimEnd(':', '：'), data.CreditsText }
                .Where(text => !string.IsNullOrEmpty(text)));
            var creditLabel = string.IsNullOrWhiteSpace(credits) ? null : Measure(credits, RemixDetailsChrome.Accent);
            var requirementLabel = string.IsNullOrWhiteSpace(metadata.Requirements) ? null : Measure(metadata.Requirements,
                metadata.RequirementsWarning ? new Color(.94f, .53f, .43f) : RemixDetailsChrome.Muted);
            float footerHeight = (creditLabel?.size.y ?? 0) + (requirementLabel?.size.y ?? 0) + 12;
            if (footerHeight > 110)
            {
                if (creditLabel != null) Block(creditLabel, 12);
                if (requirementLabel != null) Block(requirementLabel, 20);
                footerHeight = 20;
            }
            else
            {
                if (requirementLabel != null) { requirementLabel.SetPos(new Vector2(34, 22)); target.AddItems(requirementLabel); }
                if (creditLabel != null) { creditLabel.SetPos(new Vector2(34, 28 + (requirementLabel?.size.y ?? 0))); target.AddItems(creditLabel); }
            }
            float bottom = 28 + footerHeight;
            float height = 480 - bottom;
            float contentHeight = Mathf.Max(height, total + 20);
            if (contentHeight > OpScrollBox.MAXCONTENTSIZE)
                throw new ArgumentException("The details text exceeds Remix's scroll canvas limit.", nameof(content));
            body = Add(new OpScrollBox(new Vector2(18, bottom), new Vector2(BodyWidth, height), contentHeight,
                hasBack: false) { colorEdge = RemixDetailsChrome.Accent });
            float y = contentHeight - 10;
            T InBody<T>(T element) where T : UIelement
            { owned.Add(element); body.AddItems(element); return element; }
            float coverY = y - CoverHeight;
            InBody(new RemixDetailsChrome(new Vector2(CoverX, coverY), new Vector2(CoverWidth, CoverHeight),
                !string.IsNullOrEmpty(coverElement)));
            if (!string.IsNullOrEmpty(coverElement))
            {
                var cover = new OpImage(Vector2.zero, coverElement);
                owned.Add(cover);
                float scale = Mathf.Min((CoverWidth - 8) / cover.size.x, (CoverHeight - 8) / cover.size.y);
                cover.scale = new Vector2(scale, scale);
                cover.SetPos(new Vector2(CoverX + (CoverWidth - cover.size.x) / 2, coverY + (CoverHeight - cover.size.y) / 2));
                body.AddItems(cover);
            }
            else
                InBody(new OpLabel(new Vector2(CoverX + 12, coverY + (CoverHeight - 24) / 2), new Vector2(CoverWidth - 24, 24),
                    OptionInterface.Translate("rainwright-about-cover"), FLabelAlignment.Center) { color = RemixDetailsChrome.Muted });
            y = coverY - 24;
            foreach (var paragraph in paragraphs)
            {
                y -= paragraph.Label.size.y;
                paragraph.Label.SetPos(new Vector2(12, y));
                body.AddItems(paragraph.Label);
                y -= paragraph.Gap;
            }
            view = new RemixModDetailsView(owned, body, anchors.ToArray());
            body.ScrollToTop();
            return view;
        }
        catch (Exception failure)
        {
            try { view.Dispose(); }
            catch (Exception cleanup) { throw new AggregateException("Details layout failed and cleanup is incomplete.", failure, cleanup); }
            throw;
        }
    }
}
