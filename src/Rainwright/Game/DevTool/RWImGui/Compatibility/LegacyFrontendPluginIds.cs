using BepInEx;

namespace DryCycle.DevUI.DevTool.RWImGui;

// Preserve third-party dependency GUIDs without installing a second UI owner.
[BepInPlugin("DryCycle.DevTool.RWImGui", "RainWright legacy frontend alias", BridgePlugin.PluginVersion)]
[BepInDependency(BridgePlugin.PluginId)]
public sealed class LegacyFrontendPlugin : BaseUnityPlugin { }

// Legacy DryCycle DevTool frontend plugin GUIDs. These compatibility entries
// are excluded from the future standalone Rainwright frontend build; the
// normal frontend BridgePlugin is the only active lifecycle owner.
[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency(BridgePlugin.PluginId, BepInDependency.DependencyFlags.HardDependency)]
public sealed class WorldCreatureCatalogRuntimePlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.RWImGui.WorldMap.CreatureCatalogRuntime";
    public const string PluginName = "DryCycle DevTool Creature Catalog Runtime";
    public const string PluginVersion = BridgePlugin.PluginVersion;

    // Compatibility BepInPlugin GUID only: BridgePlugin starts and pumps
    // this catalog on demand, instead of installing an always-running Unity
    // Update loop or a second cache writer from a BepInEx helper instance.
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency(BridgePlugin.PluginId, BepInDependency.DependencyFlags.HardDependency)]
public sealed class WorldCreatureSpawnInspectorPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.RWImGui.WorldMap.CreatureSpawns";
    public const string PluginName = "DryCycle DevTool World Creature Spawns";
    public const string PluginVersion = BridgePlugin.PluginVersion;

    // Compatibility plugin GUID only. BridgePlugin owns the World
    // inspector, slugcat atlas preparation and main-thread pumping. Separate
    // helper shutdown must never clear an actively displayed editor.
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency(WorldCreatureSpawnInspectorPlugin.PluginId, BepInDependency.DependencyFlags.HardDependency)]
public sealed class WorldLineageInspectorPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.RWImGui.WorldMap.Lineages";
    public const string PluginName = "DryCycle DevTool World Lineages";
    public const string PluginVersion = BridgePlugin.PluginVersion;

    // Compatibility plugin GUID only. The frontend owns the lineage
    // editor lifecycle, so a separate BepInPlugin cannot clear editing state.
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency(BridgePlugin.PluginId, BepInDependency.DependencyFlags.HardDependency)]
public sealed class WorldMapPlayerLocatorPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.RWImGui.WorldMapPlayerLocator";
    public const string PluginName = "DryCycle DevTool World Map Player Locator";
    public const string PluginVersion = BridgePlugin.PluginVersion;

    // Historical BepInEx identity only. The single BridgePlugin now owns
    // the service and its Unity main-thread snapshot pump. A separate
    // compatibility entry must not compete for Update or Disable.

}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency(BridgePlugin.PluginId, BepInDependency.DependencyFlags.HardDependency)]
public sealed class WorldSubregionSelectorPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.RWImGui.WorldSubregionSelector";
    public const string PluginName = "DryCycle DevTool World Subregion Selector";
    public const string PluginVersion = BridgePlugin.PluginVersion;

    // Preserve this plugin GUID for external BepInEx dependencies.
    // The always-present frontend now owns the subregion selector lifecycle,
    // even when the loader does not discover sibling helper plugin classes.
}
