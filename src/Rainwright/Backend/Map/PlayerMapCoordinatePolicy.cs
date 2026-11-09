using DryCycle.DevUI.DevTool.Map;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Game-neutral Player Map/Canon coordinate and output policy.
/// Unity Vector2 conversion remains at the game adapter boundary.
/// </summary>
public static class PlayerMapCoordinatePolicy
{
    public const float WorldLayoutPixelsPerTile = 2f;
    public const float CanonPixelsPerTile = 3f;
    public const float WorldToCanonScale = CanonPixelsPerTile / WorldLayoutPixelsPerTile;
    public const int OutputPadding = 10;
    public const int LayerCount = MapRoomLayer.Count;

    public static float WorldLayoutToCanon(float coordinate) => coordinate * WorldToCanonScale;

    public static float CanonToWorldLayout(float coordinate) => coordinate / WorldToCanonScale;

    // Preserve truncation toward zero for signed fractional Canon output offsets.
    internal static int CanonDeltaToOutputPixel(float offset) => (int)(offset / CanonPixelsPerTile);
}

/// <summary>Player-map author coordinates are Y-up; canvas coordinates are Y-down.</summary>
internal static class PlayerMapCanvasCoordinates
{
    internal static System.Numerics.Vector2 Project(float x, float y) => new(x, -y);

    internal static System.Numerics.Vector2 Unproject(System.Numerics.Vector2 canvas) =>
        new(canvas.X, -canvas.Y);
}
