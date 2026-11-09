using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map;

/// <summary>
/// Vanilla MapPage keeps canonical/player-map and developer-map coordinates independently.
/// This bridge is used by both legacy-panel import paths and the vanilla save projection.
/// </summary>
internal readonly struct NativeMapLegacyCoordinates
{
    internal NativeMapLegacyCoordinates(Vector2 canonical, Vector2 developer)
    {
        Canonical = canonical;
        Developer = developer;
    }

    internal Vector2 Canonical { get; }
    internal Vector2 Developer { get; }

    internal static NativeMapLegacyCoordinates Read(Vector2 panelPosition, Vector2 panelDevPosition)
        => new(panelPosition, panelDevPosition);

    // An editor-specific developer placement must not leak into vanilla's canonical save.
    internal static Vector2 ForVanillaSave(Vector2 inheritedDeveloperPosition)
        => inheritedDeveloperPosition;
}
