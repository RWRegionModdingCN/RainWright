using BepInEx;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

// Historical GUIDs only; RainWright owns every editor service.
[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapConfigBuildPipelinePlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.ConfigBuildPipeline";
    public const string PluginName = "DryCycle Player Map Config Build Pipeline";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapDerivedLayoutBridgePlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.DerivedLayoutBridge";
    public const string PluginName = "DryCycle Player Map Derived Layout Bridge";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapGroupCommandPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.GroupCommands";
    public const string PluginName = "DryCycle Player Map Group Commands";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapIncrementalRenderPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.IncrementalRender";
    public const string PluginName = "DryCycle Player Map Incremental Render";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapRenderOutputValidatorPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.RenderOutputValidator";
    public const string PluginName = "DryCycle Player Map Render Output Validator";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapRenderRevisionGuardPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.RenderRevisionGuard";
    public const string PluginName = "DryCycle Player Map Render Revision Guard";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapRuntimePlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.Runtime";
    public const string PluginName = "DryCycle DevTool Player Map Runtime";
    public const string PluginVersion = "0.1.1";
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class PlayerMapTerrainBakeBridgePlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.TerrainBakeBridge";
    public const string PluginName = "DryCycle Player Map Terrain Bake Bridge";
    public const string PluginVersion = "0.1.1";
}

public static class PlayerMapConfigSerializerV2Plugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.ConfigSerializerV2";
    public const string PluginName = "DryCycle Player Map Config Serializer V2 (retired)";
    public const string PluginVersion = "0.1.1";
}

public static class PlayerMapConnectionMetadataSerializerPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.ConnectionMetadata";
    public const string PluginName = "DryCycle Player Map Connection Metadata (retired)";
    public const string PluginVersion = "0.1.1";
}

public static class PlayerMapMigrationStreamSerializerPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.MigrationStreamSerializer";
    public const string PluginName = "DryCycle Player Map Migration Stream Serializer (retired)";
    public const string PluginVersion = "0.1.1";
}

public static class PlayerMapNormalizedConfigSerializerPlugin
{
    public const string PluginId = "DryCycle.DevTool.PlayerMap.NormalizedConfig";
    public const string PluginName = "DryCycle Player Map Normalized Config (retired)";
    public const string PluginVersion = "0.1.1";
}

