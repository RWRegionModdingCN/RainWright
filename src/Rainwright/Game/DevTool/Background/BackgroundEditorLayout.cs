using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.Background;

internal enum BackgroundFieldSection { Placement, Day, Dusk, Night, Clouds, Fog, Ground, Wind, Geometry, Appearance, Layer, Other }

/// <summary>Presentation over detached data. Unknown extension fields remain editable in Other.</summary>
internal static class BackgroundEditorLayout
{
    internal static BackgroundFieldSection Section(string key, bool element)
    {
        string name = (key ?? "").TrimStart('_').ToLowerInvariant();
        if (element)
        {
            if (name is "pos" or "depth" or "fromdepth" or "todepth" or "anchorpos" or "lockx" or "locky") return BackgroundFieldSection.Placement;
            if (name is "scale" or "spritescale" or "rotation" or "thickness" or "flattened") return BackgroundFieldSection.Geometry;
            if (name is "container" or "minusdepthforlayering") return BackgroundFieldSection.Layer;
            if (name.Contains("asset") || name.Contains("sprite") || name.Contains("color") || name.Contains("alpha") || name.Contains("shader") || name == "atmodepthadd") return BackgroundFieldSection.Appearance;
            return BackgroundFieldSection.Other;
        }
        if (name.Contains("offset") || name is "origin" or "floorlevel" or "scenescale" or "depthscale") return BackgroundFieldSection.Placement;
        if (name.StartsWith("dusk")) return BackgroundFieldSection.Dusk;
        if (name.StartsWith("night")) return BackgroundFieldSection.Night;
        if (name.StartsWith("day") || name is "atmospherecolor" or "multiplycolor") return BackgroundFieldSection.Day;
        if (name.Contains("fog")) return BackgroundFieldSection.Fog;
        if (name.Contains("cloud") || name is "startaltitude" or "endaltitude" or "overrideystart" or "overrideyend") return BackgroundFieldSection.Clouds;
        if (name.Contains("rubble") || name.Contains("floor")) return BackgroundFieldSection.Ground;
        if (name.Contains("wind")) return BackgroundFieldSection.Wind;
        return BackgroundFieldSection.Other;
    }

    internal readonly struct Row
    {
        internal readonly BackgroundElementSnapshot Element;
        internal readonly int Depth;
        internal Row(BackgroundElementSnapshot element, int depth) { Element = element; Depth = depth; }
    }

    internal static void BuildRows(BackgroundElementSnapshot[] elements, string search, HashSet<int> collapsed, List<Row> rows)
    {
        rows.Clear();
        var byId = new Dictionary<int, BackgroundElementSnapshot>();
        var children = new Dictionary<int, List<BackgroundElementSnapshot>>();
        foreach (var element in elements) byId[element.Id] = element;
        var matches = new HashSet<int>();
        bool filtering = !string.IsNullOrWhiteSpace(search);
        if (filtering)
            foreach (var element in elements)
                if (element.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var cursor = element;
                    while (cursor != null && matches.Add(cursor.Id))
                        cursor = byId.TryGetValue(cursor.ParentId, out var parent) ? parent : null;
                }
        foreach (var element in elements)
        {
            int parent = element.ParentId != element.Id && byId.TryGetValue(element.ParentId, out var group) && group.IsGroup ? element.ParentId : 0;
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = new List<BackgroundElementSnapshot>();
            list.Add(element);
        }
        var visited = new HashSet<int>();
        void Visit(BackgroundElementSnapshot element, int depth)
        {
            if (!visited.Add(element.Id) || filtering && !matches.Contains(element.Id)) return;
            rows.Add(new Row(element, depth));
            if (depth >= 64 || !filtering && collapsed.Contains(element.Id)) return;
            if (children.TryGetValue(element.Id, out var list)) foreach (var child in list) Visit(child, depth + 1);
        }
        if (children.TryGetValue(0, out var roots)) foreach (var root in roots) Visit(root, 0);
    }
}
