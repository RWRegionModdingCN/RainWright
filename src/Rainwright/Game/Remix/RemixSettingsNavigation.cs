using System;
using System.Collections.Generic;
using Menu.Remix;
using Menu.Remix.MixedUI;
using UnityEngine;

namespace RainWright.Remix;

// Native controls retain tab switching, sounds, mouse hit tests and focus logic.
// This presenter runs after ConfigMenuTab's GrafUpdate and adjusts only geometry.
internal sealed class RemixSettingsNavigation : UIelement
{
    private readonly OptionInterface owner;
    private static OptionInterface activeOwner;
    private static ConfigTabController controller;
    private static float canvasAlpha;
    private static readonly Dictionary<ConfigTabController.TabSelectButton, Saved> originals = new();
    private sealed class Saved
    {
        internal readonly Vector2 Position, Size;
        internal readonly string Text;
        internal readonly FLabelAlignment Alignment;
        internal readonly float Rotation, FillAlpha;
        internal readonly DyeableRect.HiddenSide Edge, HoverEdge;
        internal readonly UIfocusable[] Focus;
        internal readonly string Caption;
        internal Saved(ConfigTabController.TabSelectButton button, string caption, float width)
        {
            Position = button.pos; Size = button.size; Text = button._label.text;
            Alignment = button._label.alignment; Rotation = button._label.rotation;
            FillAlpha = button._rect.fillAlpha; Edge = button._rect.hiddenSide; HoverEdge = button._rectH.hiddenSide;
            Focus = (UIfocusable[])button.NextFocusable.Clone();
            Caption = LabelTest.TrimText(caption, width - 10, addDots: true);
        }
        internal void Restore(ConfigTabController.TabSelectButton button)
        {
            button.SetPos(Position); button.size = Size;
            button._label.text = Text; button._label.rotation = Rotation; button._label.alignment = Alignment;
            button._rect.hiddenSide = Edge; button._rectH.hiddenSide = HoverEdge;
            button._rect.fillAlpha = FillAlpha; button.NextFocusable = Focus;
        }
    }
    internal RemixSettingsNavigation(OptionInterface owner) : base(Vector2.zero, Vector2.zero) => this.owner = owner;
    internal static void Restore(OptionInterface owner)
    {
        if (activeOwner != owner) return;
        foreach (var entry in originals) entry.Value.Restore(entry.Key);
        controller._rectCanvas.fillAlpha = canvasAlpha;
        originals.Clear(); controller = null; activeOwner = null;
    }
    public override void GrafUpdate(float timeStacker)
    {
        base.GrafUpdate(timeStacker);
        if (ConfigContainer.ActiveInterface != owner) return;
        var current = ConfigContainer.menuTab?.tabCtrler;
        if (current == null) return;
        if (controller != current || activeOwner != owner)
        {
            if (activeOwner != null) Restore(activeOwner);
            controller = current; activeOwner = owner;
            canvasAlpha = current._rectCanvas.fillAlpha;
        }
        int count = owner.Tabs.Length;
        float width = (600 - (count - 1) * 5) / count;
        for (int i = 0; i < count && i < current._tabButtons.Length; i++)
        {
            var button = current._tabButtons[i];
            if (button == null) continue;
            if (!originals.ContainsKey(button)) originals.Add(button, new Saved(button, owner.Tabs[i].name, width));
            var position = new Vector2(current.pos.x + 40 + i * (width + 5), current.pos.y + 612);
            if (button.pos.x != position.x || button.pos.y != position.y) button.SetPos(position);
            if (button.size.x != width || button.size.y != 28) button.size = new Vector2(width, 28);
            button._rect.hiddenSide = DyeableRect.HiddenSide.None;
            button._rectH.hiddenSide = DyeableRect.HiddenSide.None;
            button._rect.fillAlpha = .22f;
            button._rect.pos = Vector2.zero; button._rect.addSize = Vector2.zero;
            button._rectH.pos = Vector2.zero; button._rectH.addSize = Vector2.zero;
            button._label.text = originals[button].Caption;
            button._label.rotation = 0; button._label.alignment = FLabelAlignment.Center;
            button._label.x = width / 2; button._label.y = 14;
            button.SetNextFocusable(UIfocusable.NextDirection.Left, current._tabButtons[Math.Max(0, i - 1)]);
            button.SetNextFocusable(UIfocusable.NextDirection.Right, current._tabButtons[Math.Min(count - 1, i + 1)]);
            button.SetNextFocusable(UIfocusable.NextDirection.Up, button);
            button.SetNextFocusable(UIfocusable.NextDirection.Down, null);
            button._rect.GrafUpdate(timeStacker); button._rectH.GrafUpdate(timeStacker);
        }
        current._rectCanvas.fillAlpha = .06f;
        current._rectCanvas.GrafUpdate(timeStacker);
    }
}
