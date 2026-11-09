using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.Core;

public sealed class EditorSelection
{
    private readonly List<PlacedObject> placedObjects = new();
    private long revision = 1L;

    public IReadOnlyList<PlacedObject> PlacedObjects => placedObjects;
    public PlacedObject PrimaryPlacedObject => placedObjects.Count == 0 ? null : placedObjects[placedObjects.Count - 1];
    public int Count => placedObjects.Count;
    public long Revision => revision;

    public bool Contains(PlacedObject value) => value != null && placedObjects.Contains(value);

    public void SelectOnly(PlacedObject value)
    {
        if (value == null)
        {
            Clear();
            return;
        }

        if (placedObjects.Count == 1 && ReferenceEquals(placedObjects[0], value))
            return;

        placedObjects.Clear();
        placedObjects.Add(value);
        Touch();
    }

    public void Toggle(PlacedObject value)
    {
        if (value == null) return;
        if (!placedObjects.Remove(value)) placedObjects.Add(value);
        Touch();
    }

    public void SelectRange(IList<PlacedObject> live, int anchor, int target, bool additive)
    {
        if (live == null || live.Count == 0) return;
        anchor = Math.Max(0, Math.Min(anchor, live.Count - 1));
        target = Math.Max(0, Math.Min(target, live.Count - 1));

        List<PlacedObject> previous = additive ? null : new(placedObjects);
        // Preserve arbitrary IList getters and custom PlacedObject equality protocols.
        bool plain = live is List<PlacedObject> && PlainObjects(placedObjects, 0, placedObjects.Count - 1) &&
            PlainObjects(live, Math.Min(anchor, target), Math.Max(anchor, target));
        var members = plain ? (additive ? new HashSet<PlacedObject>(placedObjects) : new HashSet<PlacedObject>()) : null;
        bool added = false;
        if (!additive) placedObjects.Clear();

        int min = Math.Min(anchor, target);
        int max = Math.Max(anchor, target);
        for (int i = min; i <= max; i++)
        {
            PlacedObject item = live[i];
            if (item != null && (members?.Add(item) ?? !placedObjects.Contains(item))) { placedObjects.Add(item); added = true; }
        }

        if (additive ? added : !SameSelection(previous, placedObjects))
            Touch();
    }

    public void Clear()
    {
        if (placedObjects.Count == 0) return;
        placedObjects.Clear();
        Touch();
    }

    internal void RemoveMissing(List<PlacedObject> live)
    {
        if (live == null)
        {
            Clear();
            return;
        }

        // Small selections stay allocation-free; bulk validation scans the live list once.
        var members = placedObjects.Count >= 16 && PlainObjects(placedObjects, 0, placedObjects.Count - 1) &&
            PlainObjects(live, 0, live.Count - 1) ? new HashSet<PlacedObject>(live) : null;
        if (placedObjects.RemoveAll(item => item == null || !(members?.Contains(item) ?? live.Contains(item))) > 0)
            Touch();
    }

    private void Touch()
    {
        revision = revision >= long.MaxValue ? 1L : revision + 1L;
    }

    private static bool PlainObjects(IList<PlacedObject> values, int start, int end)
    {
        for (int i = start; i <= end; i++)
            if (values[i] != null && values[i].GetType() != typeof(PlacedObject)) return false;
        return true;
    }

    private static bool SameSelection(List<PlacedObject> a, List<PlacedObject> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }
}

