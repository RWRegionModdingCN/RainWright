using System;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Exclusive lifetime of the on-demand creature catalog's expensive
/// main-thread worker and persistent cache. This is deliberately one
/// specific owner, not another general plugin hook registry.
/// </summary>
internal sealed class CreatureCatalogRuntimeLease
{
    private bool initialized;
    private bool cleanupPending;

    internal bool Started => initialized;
    internal bool PendingCleanup => cleanupPending;

    internal void EnsureStarted(
        Action initialize,
        Action shutdown,
        Action<Exception> reportCleanupFailure)
    {
        if (initialized) return;
        if (initialize == null) throw new ArgumentNullException(nameof(initialize));
        if (shutdown == null) throw new ArgumentNullException(nameof(shutdown));

        if (cleanupPending)
        {
            // A previous partial initialization still owns resources. Never
            // create a second catalog until its cleanup actually succeeds.
            Stop(shutdown);
        }

        // Track the cleanup obligation before invoking initialization:
        // HookGen and icon cache setup can succeed partially then throw.
        cleanupPending = true;
        try
        {
            initialize();
            initialized = true;
        }
        catch
        {
            try { Stop(shutdown); }
            catch (Exception cleanupError)
            {
                try { reportCleanupFailure?.Invoke(cleanupError); }
                catch { /* Preserve the original initializer exception. */ }
            }
            throw; // Exact original exception instance and stack.
        }
    }

    internal void Stop(Action shutdown)
    {
        if (!cleanupPending) return;
        if (shutdown == null) throw new ArgumentNullException(nameof(shutdown));
        // If this throws, leave both flags untouched for a later cleanup.
        shutdown();
        initialized = false;
        cleanupPending = false;
    }
}
