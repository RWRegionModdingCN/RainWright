using System;
using System.Collections.Generic;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

/// <summary>
/// Semantic retained scene invalidation, independent of the game renderer.
/// Room data and frozen visual nodes are opaque reference types; the exact
/// author data/style fingerprint determines whether a snapshot is reusable.
/// </summary>
internal sealed class CartographySceneRetentionCache<TRoom, TNode>
    where TRoom : class
    where TNode : class
{
    private sealed class Entry
    {
        internal CartographyItem Item;
        internal CartographyLayer Layer;
        internal TRoom Room;
        internal bool Names, Crop, SolidTerrain;
        internal uint Terrain, Water;
        internal TNode Node;
        internal string Error;
        internal string Style;
    }

    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    internal int Count => entries.Count;

    internal bool TryGet(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, TRoom room, out TNode node, out string error)
        => TryGetWithStyle(document, item, layer, room, StyleKey(document), out node, out error);

    internal bool TryGetWithStyle(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, TRoom room, string style, out TNode node, out string error)
    {
        node = null; error = null;
        if (!entries.TryGetValue(item.Id, out Entry entry) ||
            !CartographyEditEngine.SameItem(entry.Item, item) ||
            !CartographyEditEngine.SameLayer(entry.Layer, layer) ||
            !ReferenceEquals(entry.Room, room) ||
            entry.Style != style ||
            (item.Kind == CartographyItemKind.Room &&
             (entry.Names != document.ShowRoomNames ||
              entry.Crop != document.CropSolid ||
              entry.SolidTerrain != document.SolidTerrain ||
              entry.Terrain != document.Terrain ||
              entry.Water != document.Water)))
            return false;
        node = entry.Node; error = entry.Error; return true;
    }

    internal void Store(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, TRoom room, TNode node, string error) =>
        StoreWithStyle(document, item, layer, room, node, error, StyleKey(document));

    internal void StoreWithStyle(CartographyDocument document, CartographyItem item,
        CartographyLayer layer, TRoom room, TNode node, string error, string style) =>
        entries[item.Id] = new Entry
        {
            Item = item.Clone(), Layer = layer.Clone(), Room = room,
            Names = document.ShowRoomNames, Crop = document.CropSolid,
            SolidTerrain = document.SolidTerrain,
            Terrain = document.Terrain, Water = document.Water,
            Node = node, Error = error,
            Style = style
        };

    internal static string StyleKey(CartographyDocument document) =>
        document.FontFamily + CartographyRecord.Key(document.Options) +
        string.Join(";", document.Palettes.Select(CartographyRecord.Key));

    internal void Prune(CartographyDocument document)
    {
        HashSet<string> ids = new(document.Items.Select(item => item.Id), StringComparer.Ordinal);
        foreach (string id in entries.Keys.Where(id => !ids.Contains(id)).ToArray())
            entries.Remove(id);
    }
}
