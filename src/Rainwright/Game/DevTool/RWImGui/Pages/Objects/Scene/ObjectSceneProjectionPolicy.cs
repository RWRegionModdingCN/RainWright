using DryCycle.DevUI.DevTool.Core;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

// Game snapshot adapter. Zoom hysteresis, level-of-detail suppression and
// explicit editing-intent priorities are wholly Rainwright-owned.
internal static class ObjectSceneProjectionPolicy
{
    internal static ObjectSceneSemanticZoomBand Band => ObjectSceneProjectionCore.Band;

    internal static void Update(EditorViewportSnapshot viewport, Num.Vector2 display) =>
        ObjectSceneProjectionCore.Update(viewport?.Available == true,
            viewport?.Width ?? 0f, viewport?.Height ?? 0f, display);

    internal static bool ShouldPresent(EditorObjectSnapshot item)
    {
        if (item == null) return false;
        return ShouldPresent(item, ObjectSceneVisibilityState.Resolve(item));
    }

    internal static bool ShouldPresent(EditorObjectSnapshot item, ObjectSceneVisibility visibility) =>
        item != null && ObjectSceneProjectionCore.ShouldPresent(
            item.Importance, visibility, ObjectSceneVisibilityState.IsExplicitlyEmphasized(item));

    internal static bool IsInteractive(EditorObjectSnapshot item)
    {
        if (item == null) return false;
        ObjectSceneVisibility visibility = ObjectSceneVisibilityState.Resolve(item);
        return ObjectSceneProjectionCore.IsInteractive(
            visibility, ShouldPresent(item, visibility));
    }

    internal static void Reset() => ObjectSceneProjectionCore.Reset();
}
