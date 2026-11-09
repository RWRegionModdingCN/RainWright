using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Map.Cartography;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

// A gesture owns one isolated document clone. Pointer motion only updates its moved roots;
// the immutable author document and the command/history boundary remain authoritative.
internal sealed class CartographyDragPreview
{
    private CartographyDocument source;
    internal CartographyDocument Document { get; private set; }
    internal readonly Dictionary<string, CartographyItem> Items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CartographyItem> sourceItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> membership = new(StringComparer.Ordinal);
    private readonly HashSet<string> selection = new(StringComparer.Ordinal), path = new(StringComparer.Ordinal);
    private readonly List<string> walked = new();
    private readonly List<(CartographyItem Original, CartographyItem Copy)> roots = new();
    private Num.Vector2 offset;

    internal bool Update(CartographyDocument document, HashSet<string> selected, Num.Vector2 delta)
    {
        bool reset = !ReferenceEquals(source, document) || !selection.SetEquals(selected);
        if (reset)
        {
            Clear(); source = document; Document = document.Clone(); selection.UnionWith(selected);
            foreach (var item in document.Items) if (!sourceItems.ContainsKey(item.Id)) sourceItems.Add(item.Id, item);
            foreach (var item in Document.Items) if (!Items.ContainsKey(item.Id)) Items.Add(item.Id, item);
            foreach (var item in document.Items)
                if (selection.Contains(item.Id) && !selection.Contains(item.Appearance.ParentId)) roots.Add((item, Items[item.Id]));
        }
        if (!reset && offset == delta) return false;
        offset = delta;
        foreach (var pair in roots)
        {
            if (pair.Copy.Kind == CartographyItemKind.Connection)
            {
                for (int i = 0; i < pair.Original.Points.Count; i++)
                { pair.Copy.Points[i].X = pair.Original.Points[i].X + delta.X; pair.Copy.Points[i].Y = pair.Original.Points[i].Y + delta.Y; }
            }
            else { pair.Copy.X = pair.Original.X + delta.X; pair.Copy.Y = pair.Original.Y + delta.Y; }
        }
        return true;
    }

    internal bool Moves(string id)
    {
        if (id == null) return false;
        if (membership.TryGetValue(id, out bool moved)) return moved;
        path.Clear(); walked.Clear(); string cursor = id;
        while (!string.IsNullOrEmpty(cursor) && path.Add(cursor))
        {
            if (membership.TryGetValue(cursor, out moved)) break;
            walked.Add(cursor);
            if (selection.Contains(cursor)) { moved = true; break; }
            if (!sourceItems.TryGetValue(cursor, out var item)) break;
            cursor = item.Appearance.ParentId;
        }
        foreach (string key in walked) membership[key] = moved;
        return moved;
    }

    internal void Clear()
    {
        source = null; Document = null; Items.Clear(); sourceItems.Clear(); membership.Clear();
        selection.Clear(); path.Clear(); walked.Clear(); roots.Clear(); offset = default;
    }
}
