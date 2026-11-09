using System;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal enum ObjectSceneSemanticZoomBand
{
    Near,
    Mid,
    Far
}

/// <summary>
/// Rainwright-owned semantic zoom hysteresis for the Objects scene. Keeping
/// projection separate from game snapshots prevents display jitter, while
/// explicit selection/search/hover always takes precedence over decluttering.
/// </summary>
internal static class ObjectSceneProjectionCore
{
    private const float MidZoomEnter = 1.55f;
    private const float MidZoomExit = 1.30f;
    private const float FarZoomEnter = 2.50f;
    private const float FarZoomExit = 2.20f;

    private static ObjectSceneSemanticZoomBand band;
    private static bool initialized;

    internal static ObjectSceneSemanticZoomBand Band => band;

    internal static void Update(bool viewportAvailable, float width, float height, Num.Vector2 display)
    {
        if (!viewportAvailable || display.X <= 1f || display.Y <= 1f)
            return;

        float unitsPerPixelX = width / Math.Max(1f, display.X);
        float unitsPerPixelY = height / Math.Max(1f, display.Y);
        float unitsPerPixel = Math.Max(unitsPerPixelX, unitsPerPixelY);

        if (!initialized)
        {
            band = unitsPerPixel >= FarZoomEnter
                ? ObjectSceneSemanticZoomBand.Far
                : unitsPerPixel >= MidZoomEnter
                    ? ObjectSceneSemanticZoomBand.Mid
                    : ObjectSceneSemanticZoomBand.Near;
            initialized = true;
            return;
        }

        band = band switch
        {
            ObjectSceneSemanticZoomBand.Near when unitsPerPixel >= FarZoomEnter => ObjectSceneSemanticZoomBand.Far,
            ObjectSceneSemanticZoomBand.Near when unitsPerPixel >= MidZoomEnter => ObjectSceneSemanticZoomBand.Mid,
            ObjectSceneSemanticZoomBand.Mid when unitsPerPixel >= FarZoomEnter => ObjectSceneSemanticZoomBand.Far,
            ObjectSceneSemanticZoomBand.Mid when unitsPerPixel <= MidZoomExit => ObjectSceneSemanticZoomBand.Near,
            ObjectSceneSemanticZoomBand.Far when unitsPerPixel <= MidZoomExit => ObjectSceneSemanticZoomBand.Near,
            ObjectSceneSemanticZoomBand.Far when unitsPerPixel <= FarZoomExit => ObjectSceneSemanticZoomBand.Mid,
            _ => band
        };
    }

    internal static bool ShouldPresent(int importance, ObjectSceneVisibility visibility, bool emphasized)
    {
        if (visibility == ObjectSceneVisibility.Hidden) return false;
        // Explicit intent overrides semantic zoom in all distance bands.
        if (visibility is ObjectSceneVisibility.Selected or ObjectSceneVisibility.Hovered || emphasized)
            return true;
        return band != ObjectSceneSemanticZoomBand.Far || importance >= 2;
    }

    internal static bool IsInteractive(ObjectSceneVisibility visibility, bool presented) =>
        presented && (visibility is ObjectSceneVisibility.Normal or
            ObjectSceneVisibility.Hovered or ObjectSceneVisibility.Selected);

    internal static void Reset()
    {
        band = ObjectSceneSemanticZoomBand.Near;
        initialized = false;
    }
}
