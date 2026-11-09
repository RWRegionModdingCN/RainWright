using DevInterface;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;



internal static class PlayerMapConfigSerializerV2
{
    internal static bool Save(MapPage page, PlayerMapSessionState state, out string error) =>
        PlayerMapConfigBuildPipeline.Save(page, state, out error);
}
