namespace DryCycle.DevUI.DevTool.World;
internal static class WorldTopologyRegistry
{
    internal static string SerializeAuthoringState() => "{\"version\":1,\"regions\":{}}";
    internal static string OverlayLegacyRoutes(string region, string text) => text;
    internal static string[] RegionNames() => System.Array.Empty<string>();
}
