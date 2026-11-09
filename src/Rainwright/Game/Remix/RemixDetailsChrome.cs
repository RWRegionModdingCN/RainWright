using System;
using Menu.Remix.MixedUI;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

namespace RainWright.Remix;

/// <summary>Translucent optical surface and cover frame, rendered by native Futile.</summary>
internal sealed class RemixDetailsChrome : UIelement
{
    internal static readonly Color Ink = new(.075f, .085f, .095f);
    internal static readonly Color Accent = new(.78f, .71f, .55f);
    internal static readonly Color Text = new(.90f, .90f, .86f);
    internal static readonly Color Muted = new(.65f, .68f, .69f);
    private RemixGlassMaterial.Lease material;
    private FSprite bodySurface, reflectionSurface;

    internal RemixDetailsChrome() : base(Vector2.zero, new Vector2(600, 600))
    {
        try
        {
            Glass();
        }
        catch (Exception failure)
        {
            try { Unload(); }
            catch (Exception cleanup) { throw new AggregateException("Glass surface creation and cleanup failed.", failure, cleanup); }
            throw;
        }
    }
    internal RemixDetailsChrome(Vector2 position, Vector2 size, bool hasCover) : base(position, size)
    {
        float x = 0, y = 0;
        float width = size.x, height = size.y;
        // A cover is artwork, not another opaque material stacked on the glass.
        // Keep its full reserved size, with a quiet continuous frame.
        if (!hasCover) Box(x, y, width, height, Ink, .035f);
        var edge = new Color(.72f, .77f, .80f);
        Box(x, y, width, .6f, edge, .075f); Box(x, y + height, width, .6f, edge, .12f);
        Box(x, y, .6f, height, edge, .09f); Box(x + width, y, .6f, height, edge, .075f);
    }
    private void Glass()
    {
        material = RemixGlassMaterial.Acquire();
        FSprite Surface(string atlas)
        {
            var sprite = new FSprite(atlas) { anchorX = 0, anchorY = 0 };
            sprite.scaleX = 600 / sprite.element.sourceSize.x;
            sprite.scaleY = 600 / sprite.element.sourceSize.y;
            myContainer.AddChild(sprite);
            return sprite;
        }
        bodySurface = Surface(material.BodyAtlas);
        reflectionSurface = Surface(material.LightAtlas);
        UpdateMaterial();
    }
    private void UpdateMaterial()
    {
        if (bodySurface == null) return;
        bodySurface.alpha = RainWrightPersonalSettings.RemixGlassOpacity;
        reflectionSurface.alpha = RainWrightPersonalSettings.RemixReflection;
    }
    public override void GrafUpdate(float timeStacker)
    {
        base.GrafUpdate(timeStacker);
        UpdateMaterial();
    }
    public override void Unload()
    {
        base.Unload();
        material?.Dispose(); material = null;
    }
    private void Box(float x, float y, float width, float height, Color color, float alpha)
    {
        var sprite = new FSprite("pixel")
        { anchorX = 0, anchorY = 0, x = x, y = y, scaleX = width, scaleY = height, color = color, alpha = alpha };
        myContainer.AddChild(sprite);
    }
}
