using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;
using ImGuiNET;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Center Scene workspace owned by the Objects page.
///
/// The shared SceneWorkspaceWindow owns only window chrome and dispatch. Object search, grouping,
/// selection semantics and retained projection state stay here with the rest of the Objects views.
/// </summary>
internal static class ObjectSceneWorkspaceView
{

    private static string search = string.Empty;
    private static long selectionAnchorStableId;

    private static string observedSearch;
    private static string normalizedSearch = string.Empty;

    private static int statusObjectCount = -1;
    private static int statusSelectionCount = -1;
    private static bool statusChinese;
    private static bool statusValid;
    private static string statusText = string.Empty;

    internal static void ResetRetainedState()
    {
        search = string.Empty;
        observedSearch = null;
        normalizedSearch = string.Empty;
        selectionAnchorStableId = 0L;
        statusObjectCount = -1;
        statusSelectionCount = -1;
        statusChinese = false;
        statusValid = false;
        statusText = string.Empty;
    }

    internal static void Draw(EditorPresentationSnapshot snapshot)
    {
        EditorObjectSnapshot[] objects = snapshot.SceneObjects ?? Array.Empty<EditorObjectSnapshot>();
        int selectedCount = snapshot.Inspector?.SelectionCount ?? 0;

        DevToolWidgets.MutedText(GetStatusText(objects.Length, selectedCount));

        if (selectedCount > 0)
        {
            ImGui.SameLine();
            if (DevToolWidgets.ActionButton(
                    DevToolUiSettings.T("复制", "Duplicate"),
                    "CenterSceneSelectionDuplicate",
                    DevToolButtonTone.Normal))
            {
                EditorUiCommandQueue.Enqueue(new EditorUiCommand(EditorUiCommandKind.DuplicateSelection));
            }

            ImGui.SameLine();
            if (DevToolWidgets.ActionButton(
                    DevToolUiSettings.T("删除", "Delete"),
                    "CenterSceneSelectionDelete",
                    DevToolButtonTone.Danger))
            {
                EditorUiCommandQueue.Enqueue(new EditorUiCommand(EditorUiCommandKind.DeleteSelection));
            }
        }

        bool collapseAll = ObjectSceneFilterControls.DrawCollapseAllAction("Center");

        ImGui.Spacing();
        DevToolWidgets.MutedText(DevToolUiSettings.T("搜索场景物件", "Search scene objects"));
        ImGui.SetNextItemWidth(-1f);
        ImGui.InputText("##CenterSceneObjectSearch", ref search, 128);
        ObjectSceneVisibilityState.SetSearchQuery(search);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        ObjectSceneListProjectionSnapshot projection =
            ObjectSceneListProjection.Get(objects, SearchQuery());

        ImGuiIOPtr io = ImGui.GetIO();
        ObjectSceneProjectedCategory[] categories = projection.Categories;
        for (int categoryIndex = 0; categoryIndex < categories.Length; categoryIndex++)
        {
            ObjectSceneProjectedCategory category = categories[categoryIndex];
            if (categoryIndex > 0) ImGui.Spacing();

            if (!ObjectSceneFilterControls.DrawCategoryHeader(
                    category.Category,
                    "CenterScene",
                    collapseAll))
                continue;

            List<ObjectSceneProjectedRow> rows = category.Rows;
            using DevToolListClipper clipper = new(rows.Count);
            while (clipper.Step(out int firstVisible, out int lastVisibleExclusive))
            {
                for (int rowIndex = firstVisible; rowIndex < lastVisibleExclusive; rowIndex++)
                {
                    ObjectSceneProjectedRow row = rows[rowIndex];
                    EditorObjectSnapshot item = row.Item;
                    if (!DevToolExplorerRowRenderer.DrawSelectable(row, item.Selected))
                        continue;

                    if (io.KeyShift && selectionAnchorStableId != 0L)
                    {
                        if (!ObjectSceneListProjection.EnqueueRangeSelection(
                                projection,
                                selectionAnchorStableId,
                                item.StableId,
                                io.KeyCtrl))
                        {
                            EditorUiCommandQueue.Enqueue(new EditorUiCommand(
                                EditorUiCommandKind.SelectObject,
                                item.Index,
                                stableId: item.StableId));
                            selectionAnchorStableId = item.StableId;
                        }
                    }
                    else if (io.KeyCtrl)
                    {
                        EditorUiCommandQueue.Enqueue(new EditorUiCommand(
                            EditorUiCommandKind.ToggleObjectSelection,
                            item.Index,
                            stableId: item.StableId));
                        selectionAnchorStableId = item.StableId;
                    }
                    else
                    {
                        EditorUiCommandQueue.Enqueue(new EditorUiCommand(
                            EditorUiCommandKind.SelectObject,
                            item.Index,
                            stableId: item.StableId));
                        selectionAnchorStableId = item.StableId;
                    }
                }
            }
        }

        if (projection.MatchCount == 0)
            DevToolWidgets.MutedText(DevToolUiSettings.T("没有匹配的场景物件。", "No matching scene objects."));
    }



    private static string GetStatusText(int objectCount, int selectedCount)
    {
        bool chinese = DevToolUiSettings.IsChinese;
        if (statusValid && statusObjectCount == objectCount && statusSelectionCount == selectedCount && statusChinese == chinese)
            return statusText;

        statusText = chinese
            ? $"已放置 {objectCount} 个物件 | 已选 {selectedCount}"
            : $"{objectCount} placed | {selectedCount} selected";
        statusObjectCount = objectCount;
        statusSelectionCount = selectedCount;
        statusChinese = chinese;
        statusValid = true;
        return statusText;
    }

    private static string SearchQuery()
    {
        if (string.Equals(observedSearch, search, StringComparison.Ordinal))
            return normalizedSearch;
        observedSearch = search;
        normalizedSearch = search?.Trim() ?? string.Empty;
        return normalizedSearch;
    }

}
