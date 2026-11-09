using System;

namespace DryCycle.DevUI.DevTool.Compatibility;

/// <summary>
/// A quarantined legacy proof host may be reused only for the identical placed object AND
/// its identical data instance. A new data object invalidates its cached third-party controls.
/// Reference identity is deliberate; value equality cannot prove the representation is current.
/// </summary>
internal static class ObjectProofHostLease
{
    internal static bool Matches(object cachedTarget, object cachedData, object selection, object selectionData)
        => selection != null && selectionData != null &&
           ReferenceEquals(cachedTarget, selection) &&
           ReferenceEquals(cachedData, selectionData);
}
