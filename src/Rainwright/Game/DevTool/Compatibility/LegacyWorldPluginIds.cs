using BepInEx;

namespace DryCycle.DevUI.DevTool.World;

// Historical BepInEx identities for DryCycle users. These are host-only and
// intentionally excluded from an independently compiled RainWright editor.
[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class WorldCreatureAuthoringRuntimePlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.World.CreatureAuthoringRuntime";
    public const string PluginName = "DryCycle World Creature Authoring Runtime";
    public const string PluginVersion = "0.1.1";

    // Compatibility BepInPlugin GUID only. EditorBackendLifecycle owns
    // the lineage save/preview service as part of the REQUIRED editor core.
    // A separately unloaded helper must never disable an active authoring save.
}

[BepInPlugin(PluginId, PluginName, PluginVersion)]
[BepInDependency("rainwright")]
public sealed class WorldTopologyLiveTraversalFixPlugin : BaseUnityPlugin
{
    public const string PluginId = "DryCycle.DevTool.WorldTopology.LiveTraversalFix";
    public const string PluginName = "DryCycle World Topology Live Traversal Fix";
    public const string PluginVersion = "0.1.1";

    // Keep the BepInPlugin GUID for compatibility only. The gameplay
    // WorldTopologyRuntime owns this diagnostic service from its own Hook
    // ledger, even while the editor UI is disabled or helpers are undiscovered.
}
