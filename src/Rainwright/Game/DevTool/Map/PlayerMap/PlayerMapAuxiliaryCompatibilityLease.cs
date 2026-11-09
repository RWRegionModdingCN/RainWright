using System;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// A per-BepInEx-instance compatibility lease. A helper may install its old
/// fallback when the central backend is absent, but must NEVER undo a service
/// subsequently claimed by PlayerMapBackendLifecycle (even after it shuts down).
/// No global plugin registry, reflection, or frame-loop checks are required.
/// </summary>
internal sealed class PlayerMapAuxiliaryCompatibilityLease
{
    private readonly string service;
    private readonly Action disable;
    private bool fallbackAttempted;
    private int generationAtEnable;

    internal PlayerMapAuxiliaryCompatibilityLease(string service, Action disable)
    {
        this.service = service ?? throw new ArgumentNullException(nameof(service));
        this.disable = disable ?? throw new ArgumentNullException(nameof(disable));
    }

    internal void Enable(Action enable)
    {
        if (enable == null) throw new ArgumentNullException(nameof(enable));
        if (PlayerMapBackendLifecycle.OwnsService(service))
        {
            // Central backend owns cleanup of any earlier compatibility install.
            fallbackAttempted = false;
            return;
        }
        if (fallbackAttempted) return;
        generationAtEnable = PlayerMapBackendLifecycle.ServiceGeneration(service);
        // Record BEFORE calling external Enable. The startup guard can now roll
        // back partial installs, even when the original callback throws.
        fallbackAttempted = true;
        enable();
    }

    internal void Disable()
    {
        if (!fallbackAttempted) return;
        if (PlayerMapBackendLifecycle.OwnsService(service) ||
            PlayerMapBackendLifecycle.ServiceGeneration(service) != generationAtEnable)
        {
            // A central Enable has taken over since this helper ran. It either
            // owns the service now or has already performed its own teardown.
            fallbackAttempted = false;
            return;
        }
        // Preserve the lease if removal throws so a later Disable can retry.
        disable();
        fallbackAttempted = false;
    }
}
