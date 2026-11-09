using System;
using System.Collections.Generic;
using System.Linq;
using DryCycle.DevUI.DevTool.RWImGui;

namespace DryCycle.DevUI.DevTool.Core
{
    // Only the fields accessed by the original frontend adapter.
    internal sealed class EditorObjectSnapshot
    {
        internal long StableId;
        internal bool Selected;
        internal int Importance, Index;
        internal float X, Y;
        internal string Type, DisplayName, Category, Source;
    }
    internal enum EditorUiCommandKind
    {
        SelectObject,
        ToggleObjectSelection
    }
    internal readonly struct EditorUiCommand
    {
        internal EditorUiCommandKind Kind { get; }
        internal int Index { get; }
        internal long StableId { get; }
        internal EditorUiCommand(EditorUiCommandKind kind, int index = -1, long stableId = 0)
        {
            Kind = kind; Index = index; StableId = stableId;
        }
    }
    internal static class EditorUiCommandQueue
    {
        internal static readonly List<EditorUiCommand> Commands = new();
        internal static void Enqueue(EditorUiCommand c) => Commands.Add(c);
    }
    internal sealed class EditorViewportSnapshot
    {
        internal bool Available = true;
        internal float Width, Height;
    }
}

namespace DryCycle.DevUI.DevTool.RWImGui
{
    // The original frontend still owns its display-row interface.
    internal interface IDevToolExplorerListItem
    {
        string StableId { get; }
        string PrimaryText { get; }
        string SecondaryText { get; }
        string StatusText { get; }
        string Tooltip { get; }
    }
}

internal static class Program
{
    private static int assertions;
    private static void Check(bool ok, string description)
    {
        assertions++;
        if (!ok) throw new InvalidOperationException(description);
    }

    private static void Main()
    {
        Check(typeof(ObjectSceneVisibilityCore).Assembly.GetName().Name == "RainWright" &&
              typeof(ObjectSceneVisibilityCore).Assembly != typeof(ObjectSceneVisibilityState).Assembly,
            "Object visibility state must be compiled once into Rainwright.dll.");
        ObjectSceneVisibilityState.Reset();
        var source = new DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot
        {
            StableId = 1001, Type = "Spear", DisplayName = "Red Spear",
            Category = "Weapons", Source = "Rain World"
        };
        var other = new DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot
        {
            StableId = 1002, Type = "Plant", DisplayName = "Moss",
            Category = "Nature", Source = "Extra Objects"
        };
        Check(ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Normal &&
              ObjectSceneVisibilityState.Resolve(null) == ObjectSceneVisibility.Hidden,
            "Default classification or null-object visibility changed.");
        Check(ObjectSceneVisibilityState.MatchesQuery(source, "red") &&
              ObjectSceneVisibilityState.MatchesQuery(source, "RSP") &&
              ObjectSceneVisibilityState.MatchesQuery(source, "rain world") &&
              !ObjectSceneVisibilityState.MatchesQuery(other, "spear"),
            "Object Scene substring/fuzzy/source matching changed.");
        long revision = ObjectSceneVisibilityState.Revision;
        ObjectSceneVisibilityState.SetHoveredStableId(1001);
        Check(ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Hovered &&
              ObjectSceneVisibilityState.Revision == revision,
            "Hover must not invalidate retained object labels.");
        ObjectSceneVisibilityState.CycleCategoryMode("weapons");
        Check(ObjectSceneVisibilityState.GetCategoryMode("WEAPONS") == ObjectCategoryVisibility.Ghost &&
              ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Ghost,
            "Category ghost filtering must be case-insensitive and outrank hover.");
        ObjectSceneVisibilityState.CycleCategoryMode("Weapons");
        Check(ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Hidden,
            "Category must cycle from Ghost to Hidden.");
        source.Selected = true;
        Check(ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Selected,
            "Selected objects must stay visible even when a category is hidden.");
        source.Selected = false;
        ObjectSceneVisibilityState.CycleCategoryMode("WEAPONS");
        Check(ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Hovered,
            "Category must cycle back to Normal without losing transient hover.");
        ObjectSceneVisibilityState.SetHoveredStableId(0);
        ObjectSceneVisibilityState.SetSearchQuery("spe");
        Check(ObjectSceneVisibilityState.Resolve(source) == ObjectSceneVisibility.Normal &&
              ObjectSceneVisibilityState.Resolve(other) == ObjectSceneVisibility.Ghost &&
              ObjectSceneVisibilityState.IsSearchMatch(source),
            "Search should ghost unmatched objects without hiding matching objects.");
        ObjectSceneVisibilityState.SetSearchQuery("");
        ObjectSceneVisibilityState.SetFocusedCategory("Weapons");
        Check(ObjectSceneVisibilityState.Resolve(other) == ObjectSceneVisibility.Ghost &&
              ObjectSceneVisibilityState.IsFocusedCategory(source) &&
              ObjectSceneVisibilityState.IsExplicitlyEmphasized(source),
            "Focused category must emphasize its own objects and ghost the others.");
        ObjectSceneVisibilityState.SetFocusedCategory("weapons");
        Check(ObjectSceneVisibilityState.FocusedCategory.Length == 0,
            "Clicking the focused category again must clear the focus.");
        ObjectSceneVisibilityState.Reset();
        Check(ObjectSceneVisibilityState.SearchQuery.Length == 0 &&
              ObjectSceneVisibilityState.HoveredStableId == 0 &&
              ObjectSceneVisibilityState.Resolve(other) == ObjectSceneVisibility.Normal,
            "Reset must clear cross-room Object Scene state.");
        // Exercise the production ObjectSceneProjectionPolicy with real DLL
        // zoom state, rather than source-linked copies or UI simulations.
        Check(typeof(ObjectSceneProjectionCore).Assembly.GetName().Name == "RainWright" &&
              typeof(ObjectSceneProjectionCore).Assembly !=
              typeof(ObjectSceneProjectionPolicy).Assembly,
            "Semantic zoom runtime must have a single Rainwright owner.");
        ObjectSceneProjectionPolicy.Reset();
        var view = new DryCycle.DevUI.DevTool.Core.EditorViewportSnapshot
        {
            Width = 100, Height = 100
        };
        var display = new System.Numerics.Vector2(100, 100);
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Near,
            "Default semantic zoom must show all objects.");
        view.Width = 170;
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Mid,
            "Object zoom should enter Mid at the established 1.55 threshold.");
        view.Width = 270;
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Far,
            "Object zoom should enter Far at the established 2.50 threshold.");
        other.Importance = 0;
        source.Importance = 3;
        Check(!ObjectSceneProjectionPolicy.ShouldPresent(other) &&
              ObjectSceneProjectionPolicy.ShouldPresent(source),
            "Far zoom must cull only low-priority background objects.");
        other.Selected = true;
        Check(ObjectSceneProjectionPolicy.ShouldPresent(other) &&
              ObjectSceneProjectionPolicy.IsInteractive(other),
            "Selected object must remain interactive in Far zoom.");
        other.Selected = false;
        ObjectSceneVisibilityState.SetSearchQuery("moss");
        Check(ObjectSceneProjectionPolicy.ShouldPresent(other),
            "Search result must override Far zoom decluttering.");
        ObjectSceneVisibilityState.SetSearchQuery("");
        ObjectSceneVisibilityState.SetHoveredStableId(other.StableId);
        Check(ObjectSceneProjectionPolicy.ShouldPresent(other),
            "Hovered object must remain visible in Far zoom.");
        ObjectSceneVisibilityState.SetHoveredStableId(0);
        view.Width = 230;
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Far,
            "Far band should retain hysteresis above its 2.20 exit threshold.");
        view.Width = 215;
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Mid,
            "Far band should exit to Mid below 2.20.");
        view.Width = 120;
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Near,
            "Mid band should exit to Near below 1.30.");
        view.Available = false;
        view.Width = 9999;
        ObjectSceneProjectionPolicy.Update(view, display);
        Check(ObjectSceneProjectionPolicy.Band == ObjectSceneSemanticZoomBand.Near,
            "Unavailable viewport must not perturb semantic zoom.");
        ObjectSceneProjectionPolicy.Reset();
        ObjectSceneVisibilityState.Reset();
        // Real Objects browser production projection linked into this test.
        // The grouping/range algorithms still execute in actual Rainwright.dll.
        Check(typeof(ObjectSceneListPlanner).Assembly.GetName().Name == "RainWright" &&
              typeof(ObjectSceneListPlanner).Assembly != typeof(ObjectSceneListProjection).Assembly,
            "Object browser grouping must be owned only by Rainwright.dll.");
        ObjectSceneListProjection.Reset();
        ObjectSceneVisibilityState.Reset();
        var inputs = new[]
        {
            new DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot
            {
                Index = 3, StableId = 2003, Type = "Spear",
                DisplayName = "Zeta", Category = "Weapons", Source = "Vanilla", X = 40
            },
            new DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot
            {
                Index = 1, StableId = 2001, Type = "Plant",
                DisplayName = "Alpha", Category = "Nature", Source = "Mod", Y = 10
            },
            new DryCycle.DevUI.DevTool.Core.EditorObjectSnapshot
            {
                Index = 2, StableId = 2002, Type = "Spear",
                DisplayName = "Beta", Category = "Weapons", Source = "Vanilla"
            }
        };
        var browser = ObjectSceneListProjection.Get(inputs, "");
        Check(browser.MatchCount == 3 && browser.Categories.Length == 2 &&
              browser.Categories[0].Category == "Nature" &&
              browser.Categories[1].Category == "Weapons" &&
              browser.Rows[0].Item.StableId == 2001 &&
              browser.Rows[1].Item.StableId == 2002 &&
              browser.Rows[2].Item.StableId == 2003,
            "Grouped Objects browser rows must sort category then label deterministically.");
        Check(ReferenceEquals(browser, ObjectSceneListProjection.Get(inputs, "")),
            "Unchanged Objects snapshot identity should reuse cached projections.");
        var filtered = ObjectSceneListProjection.Get(inputs, "spear");
        Check(filtered.MatchCount == 2 && filtered.Categories.Length == 1 &&
              filtered.Rows.All(row => row.Item.Type == "Spear"),
            "Search should exclude hidden/unmatched source objects from visible row projection.");
        var queue = DryCycle.DevUI.DevTool.Core.EditorUiCommandQueue.Commands;
        queue.Clear();
        Check(ObjectSceneListProjection.EnqueueRangeSelection(filtered, 2002, 2003, false) &&
              queue.Count == 3 &&
              queue[0].Kind == DryCycle.DevUI.DevTool.Core.EditorUiCommandKind.SelectObject &&
              queue[1].StableId == 2002 && queue[2].StableId == 2003 &&
              queue.All(cmd => cmd.StableId != 2001),
            "Shift selection must only queue the two currently visible object rows.");
        queue.Clear();
        inputs[2].Selected = true;
        Check(ObjectSceneListProjection.EnqueueRangeSelection(filtered, 2002, 2003, true) &&
              queue.Count == 1 && queue[0].StableId == 2003,
            "Ctrl+Shift should preserve already-selected rows without clearing selection.");
        queue.Clear();
        Check(!ObjectSceneListProjection.EnqueueRangeSelection(filtered, 2001, 2003, false) &&
              queue.Count == 0,
            "Range selection may not include anchors hidden by the active filter.");
        ObjectSceneListProjection.Reset();

        Console.WriteLine("PASS: " + assertions +
            " Object Scene checks against the actual Rainwright.dll and production UI adapter.");
    }
}
