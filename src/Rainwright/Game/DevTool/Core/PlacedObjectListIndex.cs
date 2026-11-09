using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DryCycle.DevUI.DevTool.Core;

internal sealed class PlacedObjectReferenceComparer : IEqualityComparer<PlacedObject>
{
    internal static readonly PlacedObjectReferenceComparer Instance = new();
    public bool Equals(PlacedObject a, PlacedObject b) => ReferenceEquals(a, b);
    public int GetHashCode(PlacedObject value) => RuntimeHelpers.GetHashCode(value);
}

// Short-lived index for one synchronous bulk operation. A normal List mutation,
// including same-count replacement/reorder, retires it; no index survives a frame.
internal sealed class PlacedObjectListIndex
{
    private readonly List<PlacedObject> source;
    private List<PlacedObject>.Enumerator probe;
    private Dictionary<PlacedObject, int> firstIndices;
    private readonly bool plain;

    internal PlacedObjectListIndex(List<PlacedObject> source)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        firstIndices = new(PlacedObjectReferenceComparer.Instance);
        plain = true;
        int index = 0;
        foreach (var target in source)
        {
            if (target != null && target.GetType() != typeof(PlacedObject)) plain = false;
            if (target != null && !firstIndices.ContainsKey(target)) firstIndices.Add(target, index);
            index++;
        }
        probe = source.GetEnumerator();
    }
    private bool Current()
    {
        if (firstIndices == null) return false;
        try { probe.MoveNext(); return true; }
        catch (InvalidOperationException) { firstIndices = null; return false; }
    }
    internal int IndexOf(PlacedObject target)
    {
        // A subclass can supply its own Equals protocol. Preserve List.IndexOf for it.
        if (!plain || target == null || target.GetType() != typeof(PlacedObject) || !Current()) return source.IndexOf(target);
        return firstIndices.TryGetValue(target, out int index) ? index : -1;
    }
    internal bool Contains(PlacedObject target) => target != null && IndexOf(target) >= 0;
    internal bool ContainsReference(PlacedObject target)
    {
        if (target == null) return false;
        if (Current()) return firstIndices.ContainsKey(target);
        for (int i = 0; i < source.Count; i++) if (ReferenceEquals(source[i], target)) return true;
        return false;
    }
}
