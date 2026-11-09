using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Generic page identity and lifecycle coordinator. The host chooses concrete
/// game/UI page types and supplies lifecycle callbacks; the registry itself
/// never touches game modes, renderer, snapshots or plugin engine types.
/// </summary>
internal sealed class FrontendPageRegistryCore<TMode, TPage> where TPage : class
{
    private sealed class PageEntry
    {
        internal readonly TPage Page;
        internal readonly string Id;
        internal readonly int Order;
        internal PageEntry(TPage page, string id, int order)
        {
            Page = page; Id = id; Order = order;
        }
    }

    private readonly Dictionary<TMode, PageEntry> byMode = new();
    private readonly Dictionary<string, PageEntry> byId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PageEntry> navigation = new();
    private readonly List<TPage> navigationProjection = new();
    private readonly Action<TPage> activate, deactivate, reset;
    private TPage activePage;

    internal FrontendPageRegistryCore(
        Action<TPage> activate, Action<TPage> deactivate, Action<TPage> reset)
    {
        this.activate = activate ?? throw new ArgumentNullException(nameof(activate));
        this.deactivate = deactivate ?? throw new ArgumentNullException(nameof(deactivate));
        this.reset = reset ?? throw new ArgumentNullException(nameof(reset));
    }

    internal int Count => byMode.Count;
    internal TPage ActivePage => activePage;
    internal IReadOnlyList<TPage> NavigationPages => navigationProjection;

    internal void Register(TMode mode, string id, string label, int order, TPage page)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Page id must not be empty.", nameof(page));
        if (string.IsNullOrWhiteSpace(label))
            throw new ArgumentException("Page navigation label must not be empty.", nameof(page));
        if (byMode.ContainsKey(mode))
            throw new InvalidOperationException("A DevTool page is already registered for mode " + mode + ".");
        if (byId.ContainsKey(id))
            throw new InvalidOperationException("A DevTool page is already registered with id '" + id + "'.");

        var entry = new PageEntry(page, id, order);
        byMode.Add(mode, entry);
        byId.Add(id, entry);
        navigation.Add(entry);
        navigation.Sort((left, right) =>
        {
            int cmp = left.Order.CompareTo(right.Order);
            return cmp != 0 ? cmp : StringComparer.OrdinalIgnoreCase.Compare(left.Id, right.Id);
        });
        navigationProjection.Clear();
        foreach (PageEntry row in navigation)
            navigationProjection.Add(row.Page);
    }

    internal bool TryGet(TMode mode, out TPage page)
    {
        if (byMode.TryGetValue(mode, out PageEntry entry))
        {
            page = entry.Page;
            return true;
        }
        page = null;
        return false;
    }

    internal bool TryGet(string id, out TPage page)
    {
        if (!string.IsNullOrWhiteSpace(id) && byId.TryGetValue(id.Trim(), out PageEntry entry))
        {
            page = entry.Page;
            return true;
        }
        page = null;
        return false;
    }

    internal void SynchronizeActive(TMode mode)
    {
        TryGet(mode, out TPage next);
        if (ReferenceEquals(activePage, next)) return;

        TPage previous = activePage;
        activePage = null;
        if (previous != null) deactivate(previous);

        if (next == null) return;
        activate(next);
        activePage = next;
    }

    internal void DeactivateActive()
    {
        TPage previous = activePage;
        activePage = null;
        if (previous != null) deactivate(previous);
    }

    internal void ResetAll()
    {
        DeactivateActive();
        foreach (PageEntry entry in byMode.Values)
            reset(entry.Page);
    }
}
