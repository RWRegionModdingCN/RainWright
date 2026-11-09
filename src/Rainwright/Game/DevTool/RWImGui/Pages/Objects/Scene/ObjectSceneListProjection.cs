using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// One retained, category-first projection of the current room's object scene.
///
/// Center Scene and Browser Scene are two hosts for the same semantic list. Keeping projection,
/// ordering and range-selection here prevents the hosts from drifting into different search/group
/// behavior and makes Shift-selection follow the rows the user can actually see.
/// </summary>
internal sealed class ObjectSceneProjectedRow : IDevToolExplorerListItem
{
    internal EditorObjectSnapshot Item;
    internal string Category;
    internal string Source;
    internal string Label;
    internal string TooltipText;

    public string StableId => "ObjectScene:" + (Item?.StableId ?? 0L);
    public string PrimaryText => Label ?? string.Empty;
    public string SecondaryText => string.Empty;
    public string StatusText => string.Empty;
    public string Tooltip => TooltipText ?? string.Empty;
}

internal sealed class ObjectSceneProjectedCategory
{
    internal string Category;
    internal readonly List<ObjectSceneProjectedRow> Rows = new();
}

internal sealed class ObjectSceneListProjectionSnapshot
{
    internal static readonly ObjectSceneListProjectionSnapshot Empty = new()
    {
        Categories = Array.Empty<ObjectSceneProjectedCategory>(),
        Rows = Array.Empty<ObjectSceneProjectedRow>()
    };

    internal ObjectSceneProjectedCategory[] Categories = Array.Empty<ObjectSceneProjectedCategory>();
    internal ObjectSceneProjectedRow[] Rows = Array.Empty<ObjectSceneProjectedRow>();
    internal int MatchCount;
}

internal static class ObjectSceneListProjection
{
    private static EditorObjectSnapshot[] cachedObjects;
    private static string cachedQuery = string.Empty;
    private static ObjectSceneListProjectionSnapshot cached = ObjectSceneListProjectionSnapshot.Empty;

    internal static ObjectSceneListProjectionSnapshot Get(
        EditorObjectSnapshot[] objects,
        string query)
    {
        objects ??= Array.Empty<EditorObjectSnapshot>();
        query = query?.Trim() ?? string.Empty;

        if (ReferenceEquals(cachedObjects, objects) &&
            string.Equals(cachedQuery, query, StringComparison.Ordinal))
            return cached;

        Rebuild(objects, query);
        return cached;
    }

    internal static void Reset()
    {
        cachedObjects = null;
        cachedQuery = string.Empty;
        cached = ObjectSceneListProjectionSnapshot.Empty;
    }

    /// <summary>
    /// Enqueues one visible-row range. The range is based on the projection order, not underlying
    /// RoomSettings indices, so filtering/grouping can never cause hidden or unrelated objects to
    /// enter a Shift selection.
    /// </summary>
    internal static bool EnqueueRangeSelection(
        ObjectSceneListProjectionSnapshot projection,
        long anchorStableId,
        long targetStableId,
        bool additive)
    {
        ObjectSceneProjectedRow[] rows = projection?.Rows;
        if (rows == null || rows.Length == 0) return false;

        var descriptors = new ObjectSceneListEntry<ObjectSceneProjectedRow>[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            EditorObjectSnapshot item = rows[i]?.Item;
            descriptors[i] = new ObjectSceneListEntry<ObjectSceneProjectedRow>(
                rows[i], rows[i]?.Category, rows[i]?.Label, rows[i]?.Source,
                item?.StableId ?? 0L, item?.Index ?? -1, item?.Selected ?? false);
        }
        if (!ObjectSceneListPlanner.TryRange(descriptors, anchorStableId,
            targetStableId, additive, out var selected))
            return false;

        if (!additive)
            EditorUiCommandQueue.Enqueue(new EditorUiCommand(
                EditorUiCommandKind.SelectObject, index: -1));
        foreach (var entry in selected)
        {
            EditorUiCommandQueue.Enqueue(new EditorUiCommand(
                EditorUiCommandKind.ToggleObjectSelection,
                index: entry.Index, stableId: entry.StableId));
        }
        return true;
    }

    private static void Rebuild(EditorObjectSnapshot[] objects, string query)
    {
        var rows = new List<ObjectSceneListEntry<ObjectSceneProjectedRow>>();
        for (int i = 0; i < objects.Length; i++)
        {
            EditorObjectSnapshot item = objects[i];
            if (item == null || !ObjectSceneVisibilityState.MatchesQuery(item, query))
                continue;

            string category = ObjectSceneListPlanner.Canonical(item.Category, "Unsorted");
            string source = ObjectSceneListPlanner.Canonical(item.Source, "Unknown Source");
            string displayName = ObjectSceneListPlanner.Canonical(
                item.DisplayName, item.Type ?? "Unknown");
            string label = displayName + "  |  (" + item.X.ToString("0") +
                ", " + item.Y.ToString("0") + ")";
            var row = new ObjectSceneProjectedRow
            {
                Item = item, Category = category, Source = source,
                Label = label,
                TooltipText = source + " | " + (item.Type ?? string.Empty) + " | " + category
            };
            rows.Add(new ObjectSceneListEntry<ObjectSceneProjectedRow>(
                row, category, label, source, item.StableId, item.Index, item.Selected));
        }

        ObjectSceneListPlan<ObjectSceneProjectedRow> plan = ObjectSceneListPlanner.Build(rows);
        var groups = new ObjectSceneProjectedCategory[plan.Categories.Length];
        for (int i = 0; i < groups.Length; i++)
        {
            var group = plan.Categories[i];
            groups[i] = new ObjectSceneProjectedCategory { Category = group.Category };
            groups[i].Rows.AddRange(group.Rows);
        }

        cachedObjects = objects;
        cachedQuery = query;
        cached = new ObjectSceneListProjectionSnapshot
        {
            Categories = groups,
            Rows = plan.Rows,
            MatchCount = plan.Rows.Length
        };
    }

}
