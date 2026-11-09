using System;
using System.Collections.Generic;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

// A single scene-build scope, never retained against mutable author drafts. Parent traversal is
// iterative and memoized so valid deep hierarchies cannot exhaust the stack or be rescanned per item.
internal sealed class CartographySceneHierarchy
{
    private readonly CartographyDocument document;
    private readonly Dictionary<string, CartographyItem> items;
    private readonly Dictionary<string, bool> visibility = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (float X, float Y)> positions = new(StringComparer.Ordinal);
    private readonly HashSet<string> path = new(StringComparer.Ordinal);
    private readonly List<CartographyItem> walked = new();
    private readonly HashSet<string> hidden;

    internal CartographySceneHierarchy(CartographyDocument document)
    {
        this.document = document;
        items = document.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        hidden = new(document.Options.HiddenTypes.Split(',').Select(value => value.Trim()).Where(value => value.Length > 0), StringComparer.Ordinal);
    }

    internal CartographyItem Resolve(CartographyItem item)
    {
        if (item.Appearance.ParentId.Length == 0) return item;
        path.Clear(); walked.Clear();
        CartographyItem cursor = item;
        (float X, float Y) position = default;
        while (cursor != null)
        {
            if (positions.TryGetValue(cursor.Id, out position)) break;
            if (!path.Add(cursor.Id)) throw new InvalidOperationException("Object parent cycle.");
            walked.Add(cursor);
            if (cursor.Appearance.ParentId.Length == 0) break;
            items.TryGetValue(cursor.Appearance.ParentId, out cursor);
        }
        for (int i = walked.Count - 1; i >= 0; i--)
        {
            CartographyItem current = walked[i];
            position = (current.X + position.X, current.Y + position.Y);
            positions[current.Id] = position;
        }
        CartographyItem resolved = item.Clone();
        resolved.X = position.X; resolved.Y = position.Y;
        return resolved;
    }

    internal bool IsVisible(CartographyItem item)
    {
        path.Clear(); walked.Clear();
        bool visible = true;
        for (CartographyItem cursor = item; cursor != null;)
        {
            if (visibility.TryGetValue(cursor.Id, out visible)) break;
            if (!path.Add(cursor.Id)) throw new InvalidOperationException("Object parent cycle.");
            walked.Add(cursor);
            if (!LocallyVisible(cursor)) { visible = false; break; }
            if (cursor.Appearance.ParentId.Length == 0) { visible = true; break; }
            if (!items.TryGetValue(cursor.Appearance.ParentId, out cursor)) { visible = false; break; }
        }
        foreach (CartographyItem current in walked) visibility[current.Id] = visible;
        return visible;
    }

    private bool LocallyVisible(CartographyItem item)
    {
        if (!item.Visible || document.Layer(item.LayerId) is not { Visible: true, Opacity: > 0 }) return false;
        if (item.Kind == CartographyItemKind.Connection && !document.ShowConnections) return false;
        if (item.Kind == CartographyItemKind.Room && document.Palettes.Find(p => p.Name == item.Appearance.Subregion)?.Visible == false) return false;
        string category = item.Appearance.Category;
        if (category == "Special" && !document.Options.SpecialRooms || category == "Object" && !document.Options.Objects ||
            category == "Pickup" && (!document.Options.Objects || !document.Options.Pickups)) return false;
        return !hidden.Contains(item.Text) && !hidden.Contains(item.Appearance.Icon);
    }
}
