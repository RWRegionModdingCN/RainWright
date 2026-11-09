namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Typed adapter from the game's BepInEx plugin entry to Rainwright's
/// game-neutral exclusive lifecycle gate. The actual plugin remains
/// frontend-owned until its native resources can be detached safely.
/// </summary>
internal static class BridgePluginOwnership
{
    private static readonly FrontendPluginOwnerGate<BridgePlugin> gate = new();

    internal static bool TryClaim(BridgePlugin plugin) => gate.TryClaim(plugin);
    internal static bool IsOwner(BridgePlugin plugin) => gate.IsOwner(plugin);
    internal static bool TryRelease(BridgePlugin plugin, bool cleanupComplete) =>
        gate.TryRelease(plugin, cleanupComplete);
}
