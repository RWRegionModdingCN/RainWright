namespace DryCycle.DevUI.DevTool.RWImGui;

// Shared, locally implemented layer palette for both map views. No vanilla renderer calls.
// Presentation colors only. Native map indices and stored coordinates remain unchanged.
internal static class WorldMapLayerStyle
{
    internal static readonly string[] Labels = { "L0", "L1", "L2" };
    internal static uint Color(int layer) => layer switch
    {
        0 => 0xFFF0B47C,
        2 => 0xFFC0A9E8,
        _ => 0xFF9ED2A0
    };
}
