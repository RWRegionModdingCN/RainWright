using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Background;

/// <summary>One cached appearance record per edited native element; no reflection in draw callbacks.</summary>
internal static class BackgroundAppearanceRuntime
{
    private sealed class Appearance
    {
        internal Vector2? Scale, Anchor;
        internal Color? Color;
        internal float? Alpha;
        internal string Shader, Container;
        internal readonly ConditionalWeakTable<RoomCamera.SpriteLeaser, object> Initialized = new();
    }
    private static ConditionalWeakTable<BackgroundScene.BackgroundSceneElement, Appearance> records = new();
    private static bool enabled;
    internal static void Bind(BackgroundScene.BackgroundSceneElement element, object data)
    {
        if (element == null) return;
        var value = new Appearance
        {
            Scale = BackgroundDataAccess.Get(data, "spriteScale") as Vector2?, Anchor = BackgroundDataAccess.Get(data, "anchorPos") as Vector2?,
            Color = BackgroundDataAccess.Get(data, "spriteColor") as Color?, Alpha = BackgroundDataAccess.Get(data, "spriteAlpha") as float?,
            Shader = BackgroundDataAccess.Get(data, "spriteShader") as string, Container = BackgroundDataAccess.Get(data, "container")?.ToString()
        };
        records.Remove(element); records.Add(element, value);
        if (!enabled)
        {
            On.BackgroundScene.BackgroundSceneElement.DrawSprites += DrawSprites;
            enabled = true;
        }
    }
    private static void DrawSprites(On.BackgroundScene.BackgroundSceneElement.orig_DrawSprites orig, BackgroundScene.BackgroundSceneElement self,
        RoomCamera.SpriteLeaser leaser, RoomCamera camera, float timeStacker, Vector2 camPos)
    { orig(self, leaser, camera, timeStacker, camPos); Apply(leaser, camera, self); }
    internal static void Apply(RoomCamera.SpriteLeaser leaser, RoomCamera camera, BackgroundScene.BackgroundSceneElement element)
    {
        // Native DevTool edits the same records. Never replay a cached New-UI appearance over it.
        if (Core.EditorUiModeState.UseVanilla) return;
        if (leaser?.sprites == null || !records.TryGetValue(element, out var value) || element.slatedForDeletetion) return;
        bool first = !value.Initialized.TryGetValue(leaser, out _);
        if (first) value.Initialized.Add(leaser, new object());
        foreach (FSprite sprite in leaser.sprites)
        {
            if (sprite == null) continue;
            if (value.Scale is Vector2 scale) { sprite.scaleX = scale.x; sprite.scaleY = scale.y; }
            if (value.Anchor is Vector2 anchor) { sprite.anchorX = anchor.x; sprite.anchorY = anchor.y; }
            if (value.Alpha is float alpha) sprite.alpha = alpha;
            if (value.Color is Color color) sprite.color = color;
            if (first && value.Shader != null && camera.game.rainWorld.Shaders.TryGetValue(value.Shader, out FShader shader)) sprite.shader = shader;
            if (first && value.Container != null) { sprite.RemoveFromContainer(); camera.ReturnFContainer(value.Container).AddChild(sprite); }
        }
    }
    internal static void Disable()
    {
        if (enabled) On.BackgroundScene.BackgroundSceneElement.DrawSprites -= DrawSprites;
        enabled = false; records = new();
    }
}
