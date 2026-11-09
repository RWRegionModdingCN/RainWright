using System;
using System.Collections.Generic;
using System.Linq;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>One game-neutral row descriptor used only while rebuilding.</summary>
internal readonly struct ObjectSceneListEntry<TRow>
{
    internal readonly TRow Row;
    internal readonly string Category, Label, Source;
    internal readonly long StableId;
    internal readonly int Index;
    internal readonly bool Selected;

    internal ObjectSceneListEntry(TRow row, string category, string label,
        string source, long stableId, int index, bool selected)
    {
        Row = row; Category = category; Label = label; Source = source;
        StableId = stableId; Index = index; Selected = selected;
    }
}

internal sealed class ObjectSceneListGroup<TRow>
{
    internal string Category;
    internal TRow[] Rows;
}

internal sealed class ObjectSceneListPlan<TRow>
{
    internal ObjectSceneListGroup<TRow>[] Categories;
    internal TRow[] Rows;
}

/// <summary>
/// Rainwright-owned Objects browser grouping and ordering, shared by the
/// central scene and the browser. The host supplies detached rows only.
/// </summary>
internal static class ObjectSceneListPlanner
{
    internal static string Canonical(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    internal static ObjectSceneListPlan<TRow> Build<TRow>(
        IEnumerable<ObjectSceneListEntry<TRow>> entries)
    {
        var groups = new Dictionary<string, List<ObjectSceneListEntry<TRow>>>(
            StringComparer.OrdinalIgnoreCase);
        if (entries != null)
            foreach (var row in entries)
            {
                string category = Canonical(row.Category, "Unsorted");
                if (!groups.TryGetValue(category, out var list))
                {
                    list = new List<ObjectSceneListEntry<TRow>>();
                    groups.Add(category, list);
                }
                list.Add(row);
            }

        var orderedGroups = groups.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        var categories = new ObjectSceneListGroup<TRow>[orderedGroups.Length];
        var all = new List<TRow>();
        for (int i = 0; i < orderedGroups.Length; i++)
        {
            var rows = orderedGroups[i].Value;
            rows.Sort(Compare);
            var sorted = rows.Select(row => row.Row).ToArray();
            categories[i] = new ObjectSceneListGroup<TRow>
            {
                Category = orderedGroups[i].Key, Rows = sorted
            };
            all.AddRange(sorted);
        }
        return new ObjectSceneListPlan<TRow>
        {
            Categories = categories,
            Rows = all.ToArray()
        };
    }

    // EnqueueRangeSelection must be driven by the *visible projected* rows
    // instead of RoomSettings indices. The selected state is captured at click
    // time, not at the previous render, so Ctrl/Shift semantics remain stable.
    internal static bool TryRange<TRow>(
        IReadOnlyList<ObjectSceneListEntry<TRow>> rows,
        long anchorStableId, long targetStableId, bool additive,
        out ObjectSceneListEntry<TRow>[] selected)
    {
        selected = Array.Empty<ObjectSceneListEntry<TRow>>();
        if (rows == null || rows.Count == 0 ||
            anchorStableId == 0 || targetStableId == 0)
            return false;

        int anchor = -1, target = -1;
        for (int i = 0; i < rows.Count && (anchor < 0 || target < 0); i++)
        {
            if (rows[i].StableId == anchorStableId) anchor = i;
            if (rows[i].StableId == targetStableId) target = i;
        }
        if (anchor < 0 || target < 0) return false;

        var matches = new List<ObjectSceneListEntry<TRow>>();
        int min = Math.Min(anchor, target), max = Math.Max(anchor, target);
        for (int i = min; i <= max; i++)
        {
            var item = rows[i];
            if (item.StableId == 0 || (additive && item.Selected)) continue;
            matches.Add(item);
        }
        selected = matches.ToArray();
        return true;
    }

    private static int Compare<TRow>(
        ObjectSceneListEntry<TRow> a, ObjectSceneListEntry<TRow> b)
    {
        int label = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
        if (label != 0) return label;
        int source = string.Compare(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);
        if (source != 0) return source;
        int stable = a.StableId.CompareTo(b.StableId);
        return stable != 0 ? stable : a.Index.CompareTo(b.Index);
    }
}
