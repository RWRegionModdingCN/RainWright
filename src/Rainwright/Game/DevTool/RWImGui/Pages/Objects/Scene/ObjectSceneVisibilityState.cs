using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.RWImGui;

// Frontend-facing source snapshot adapter. The complete category/search/hover
// state machine is uniquely owned by RainWright.dll.
internal static class ObjectSceneVisibilityState
{
    internal static long Revision => ObjectSceneVisibilityCore.Revision;
    internal static string FocusedCategory => ObjectSceneVisibilityCore.FocusedCategory;
    internal static string SearchQuery => ObjectSceneVisibilityCore.SearchQuery;
    internal static long HoveredStableId => ObjectSceneVisibilityCore.HoveredStableId;

    internal static ObjectCategoryVisibility GetCategoryMode(string category) =>
        ObjectSceneVisibilityCore.GetCategoryMode(category);
    internal static void CycleCategoryMode(string category) =>
        ObjectSceneVisibilityCore.CycleCategoryMode(category);
    internal static void SetFocusedCategory(string category) =>
        ObjectSceneVisibilityCore.SetFocusedCategory(category);
    internal static void ClearFocus() => ObjectSceneVisibilityCore.ClearFocus();
    internal static void SetSearchQuery(string query) =>
        ObjectSceneVisibilityCore.SetSearchQuery(query);
    internal static void SetHoveredStableId(long stableId) =>
        ObjectSceneVisibilityCore.SetHoveredStableId(stableId);
    internal static ObjectSceneVisibility Resolve(EditorObjectSnapshot item) =>
        item == null ? ObjectSceneVisibility.Hidden :
        ObjectSceneVisibilityCore.Resolve(Describe(item));
    internal static bool MatchesQuery(EditorObjectSnapshot item, string query) =>
        item != null && ObjectSceneVisibilityCore.MatchesQuery(Describe(item), query);
    internal static bool IsSearchMatch(EditorObjectSnapshot item) =>
        item != null && ObjectSceneVisibilityCore.IsSearchMatch(Describe(item));
    internal static bool IsFocusedCategory(EditorObjectSnapshot item) =>
        item != null && ObjectSceneVisibilityCore.IsFocusedCategory(Describe(item));
    internal static bool IsExplicitlyEmphasized(EditorObjectSnapshot item) =>
        item != null && ObjectSceneVisibilityCore.IsExplicitlyEmphasized(Describe(item));
    internal static void Reset() => ObjectSceneVisibilityCore.Reset();

    private static ObjectSceneDescriptor Describe(EditorObjectSnapshot item) =>
        new(item.StableId, item.Selected, item.Type,
            item.DisplayName, item.Category, item.Source);
}
