using System;
using System.Threading;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Process-wide, non-reentrant ownership gate for a native frontend plugin.
/// Preserve the owner after failed hook/callback cleanup: another plugin
/// instance must never register the same native resources concurrently.
/// Holds only a reference to its owner; no engine or gameplay dependency.
/// </summary>
internal sealed class FrontendPluginOwnerGate<TOwner> where TOwner : class
{
    private TOwner owner;

    internal bool TryClaim(TOwner candidate)
    {
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        TOwner observed = Interlocked.CompareExchange(ref owner, candidate, null);
        return observed == null || ReferenceEquals(observed, candidate);
    }

    internal bool IsOwner(TOwner candidate) =>
        candidate != null &&
        ReferenceEquals(Interlocked.CompareExchange(ref owner, null, null), candidate);

    /// <summary>
    /// Release only after *all* required hook/optional callback lifetimes have
    /// been cleaned up. A false cleanup result leaves the original owner
    /// installed so a later Enable/Disable can retry cleanup safely.
    /// </summary>
    internal bool TryRelease(TOwner candidate, bool cleanupComplete)
    {
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (!cleanupComplete) return false;
        return ReferenceEquals(
            Interlocked.CompareExchange(ref owner, null, candidate),
            candidate);
    }
}
