namespace DryCycle.DevUI.DevTool.Map.Cartography;

// Game-only adapter: mod/room geometry is decoded by the host, while the
// identity/fingerprint bounded LRU is implemented in RainWright.dll.
internal sealed class CartographySourceCache
{
    private readonly CartographySourceFingerprintCache<CartographySource> cache;

    internal CartographySourceCache(int capacity = 4) =>
        cache = new CartographySourceFingerprintCache<CartographySource>(capacity);

    internal int Count => cache.Count;

    // Keep the editor-facing signature and the game's authoritative mod
    // asset resolver. A static delegate prevents per-call capture allocations.
    internal CartographySource Load(string identity, CartographyRegionFiles files,
        string campaign, bool refresh, out bool hit) =>
        cache.Load(identity, files, refresh,
            static (snapshot, currentCampaign) =>
                CartographyRegionLoader.Load(snapshot.Region, currentCampaign, snapshot.Read),
            campaign, out hit);
}
