namespace DryCycle.DevUI.DevTool.Map.Cartography;

/// <summary>
/// Thin scene-facing bridge. Preserve the old UI and canvas signatures while
/// the route geometry and gesture semantics live in RainWright.dll.
/// </summary>
internal static class CartographyRouteEditing
{
    internal static int NearestSegment(CartographyPoint[] points, float x, float y,
        float radius, out CartographyPoint closest) =>
        CartographyRouteGeometry.NearestSegment(points, x, y, radius, out closest);

    internal static int NearestHandle(CartographyPoint[] points, float x, float y,
        float radius) =>
        CartographyRouteGeometry.NearestHandle(points, x, y, radius);

    internal static CartographyItem Manual(CartographyItem item, CartographySceneNode node) =>
        CartographyRouteGeometry.Manual(item, node.Points);

    internal static CartographyPoint Drag(float x, float y, CartographyPoint anchor,
        bool constrainAxis) =>
        CartographyRouteGeometry.Drag(x, y, anchor, constrainAxis);
}
