namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Exact authoring identity for Player Map render preparation. This is the
/// legacy 64-bit rolling fingerprint contract: never include caches, bake
/// readiness or render progress, only author-controlled map data.
/// </summary>
internal struct PlayerMapAuthoringFingerprint
{
    private ulong value;

    private PlayerMapAuthoringFingerprint(ulong initial) => value = initial;

    internal static PlayerMapAuthoringFingerprint Begin() =>
        new(1469598103934665603UL);

    internal ulong Value => value;

    internal void ObserveRoom(int roomIndex, float x, float y,
        int layer, bool disabled, PlayerMapPlacementMode mode)
    {
        Mix(roomIndex);
        Mix(x.GetHashCode());
        Mix(y.GetHashCode());
        Mix(layer);
        Mix(disabled ? 1 : 0);
        Mix((int)mode);
    }

    internal void ObserveMaterial(int id, float ax, float ay, float bx, float by, bool air)
    {
        Mix(id);
        Mix(ax.GetHashCode());
        Mix(ay.GetHashCode());
        Mix(bx.GetHashCode());
        Mix(by.GetHashCode());
        Mix(air ? 1 : 0);
    }

    private void Mix(int number)
    {
        unchecked
        {
            value ^= (uint)number;
            value *= 1099511628211UL;
        }
    }
}
