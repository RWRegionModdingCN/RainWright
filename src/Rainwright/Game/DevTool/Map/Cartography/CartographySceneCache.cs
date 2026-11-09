namespace DryCycle.DevUI.DevTool.Map.Cartography;

// Host-owned typed adapter. Immutable snapshots and cache invalidation
// live in Rainwright, with no dependency on game room or raster classes.
internal sealed class CartographySceneCache
{
    private readonly CartographySceneRetentionCache<CartographyRoomSource, CartographySceneNode> backend = new();
    internal static string StyleKey(CartographyDocument document) =>
        CartographySceneRetentionCache<CartographyRoomSource, CartographySceneNode>.StyleKey(document);

    internal bool TryGetWithStyle(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, CartographyRoomSource room, string style,
        out CartographySceneNode node, out string error) =>
        backend.TryGetWithStyle(document, item, layer, room, style, out node, out error);

    internal void StoreWithStyle(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, CartographyRoomSource room, CartographySceneNode node, string error, string style) =>
        backend.StoreWithStyle(document, item, layer, room, node, error, style);

    internal bool TryGet(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, CartographyRoomSource room,
        out CartographySceneNode node, out string error) =>
        backend.TryGet(document, item, layer, room, out node, out error);

    internal void Store(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, CartographyRoomSource room,
        CartographySceneNode node, string error) =>
        backend.Store(document, item, layer, room, node, error);

    internal void Prune(CartographyDocument document) => backend.Prune(document);
}
