using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Room-scoped sound multiselection and frozen command selection snapshots.
/// Detached from gameplay scene snapshots and from runtime scene entities.
/// One frontend owns one instance for its session.
/// </summary>
internal sealed class SoundSceneSelectionCore
{
    private readonly HashSet<int> SceneSelectionSet = new();
    private readonly List<int> InvalidSelectionScratch = new();
    private string roomKey = string.Empty;
    private int selectionAnchor = -1;
    private int lastLegacySelectedIndex = -1;
    private int expectedLegacySelection = int.MinValue;
    private int observedSoundCount = -1;
    private bool sceneSelectionInitialized;
    private bool selectedIndicesDirty = true;
    private int[] selectedIndicesCache = Array.Empty<int>();

    internal int SelectionCount => SceneSelectionSet.Count;

    internal void SynchronizeScene(string nextRoom, int count, int legacyIndex)
    {
        if (!string.Equals(roomKey, nextRoom, StringComparison.Ordinal))
        {
            roomKey = nextRoom;
            ClearSelectionSet();
            selectionAnchor = -1;
            lastLegacySelectedIndex = -1;
            expectedLegacySelection = int.MinValue;
            observedSoundCount = -1;
            sceneSelectionInitialized = false;
        }

        if (count != observedSoundCount)
        {
            observedSoundCount = count;
            PruneSelection(count);
        }

        bool legacyValid = legacyIndex >= 0 && legacyIndex < count;
        if (!sceneSelectionInitialized)
        {
            ClearSelectionSet();
            if (legacyValid) AddSelection(legacyIndex);
            selectionAnchor = legacyValid ? legacyIndex : -1;
            sceneSelectionInitialized = true;
            lastLegacySelectedIndex = legacyIndex;
            expectedLegacySelection = int.MinValue;
            return;
        }

        if (legacyIndex != lastLegacySelectedIndex)
        {
            if (legacyIndex == expectedLegacySelection)
            {
                expectedLegacySelection = int.MinValue;
            }
            else
            {
                ClearSelectionSet();
                if (legacyValid) AddSelection(legacyIndex);
                selectionAnchor = legacyValid ? legacyIndex : -1;
            }
            lastLegacySelectedIndex = legacyIndex;
        }
        else if (expectedLegacySelection == legacyIndex)
        {
            expectedLegacySelection = int.MinValue;
        }

        if (selectionAnchor >= count)
            selectionAnchor = FindMinimumSelection();
    }

    internal bool IsSceneSelected(int index) => SceneSelectionSet.Contains(index);

    internal void HandleSceneClick(int index, bool ctrl, bool shift)
    {
        if (shift && selectionAnchor >= 0)
        {
            if (!ctrl) ClearSelectionSet();
            int min = Math.Min(selectionAnchor, index);
            int max = Math.Max(selectionAnchor, index);
            for (int i = min; i <= max; i++) AddSelection(i);
        }
        else if (ctrl)
        {
            if (!SceneSelectionSet.Add(index))
                SceneSelectionSet.Remove(index);
            MarkSelectionDirty();
            selectionAnchor = index;
        }
        else
        {
            ClearSelectionSet();
            AddSelection(index);
            selectionAnchor = index;
        }

        sceneSelectionInitialized = true;
        expectedLegacySelection = index;
    }

    internal void SelectAll(int count)
    {
        ClearSelectionSet();
        for (int i = 0; i < count; i++) SceneSelectionSet.Add(i);
        if (count > 0) MarkSelectionDirty();
        selectionAnchor = count > 0 ? 0 : -1;
        sceneSelectionInitialized = true;
        expectedLegacySelection = int.MinValue;
    }

    internal int[] SelectedIndices()
    {
        if (!selectedIndicesDirty) return selectedIndicesCache;
        if (SceneSelectionSet.Count == 0)
        {
            selectedIndicesCache = Array.Empty<int>();
            selectedIndicesDirty = false;
            return selectedIndicesCache;
        }

        // Publish a fresh immutable snapshot only when selection actually changes. Commands queued
        // from the UI may retain this array after the frame; reusing and refilling the same backing
        // array would let a later selection mutation silently rewrite an already queued command.
        int[] next = new int[SceneSelectionSet.Count];
        int cursor = 0;
        foreach (int index in SceneSelectionSet)
            next[cursor++] = index;
        Array.Sort(next);
        selectedIndicesCache = next;
        selectedIndicesDirty = false;
        return selectedIndicesCache;
    }

    internal void ClearSelection()
    {
        ClearSelectionSet();
        selectionAnchor = -1;
        sceneSelectionInitialized = true;
        expectedLegacySelection = int.MinValue;
    }

    internal void ResetSelectionFromLegacy()
    {
        ClearSelectionSet();
        selectionAnchor = -1;
        sceneSelectionInitialized = false;
        expectedLegacySelection = int.MinValue;
    }

    private void PruneSelection(int count)
    {
        if (SceneSelectionSet.Count == 0)
        {
            if (selectionAnchor >= count) selectionAnchor = -1;
            return;
        }

        InvalidSelectionScratch.Clear();
        foreach (int index in SceneSelectionSet)
            if (index < 0 || index >= count)
                InvalidSelectionScratch.Add(index);

        for (int i = 0; i < InvalidSelectionScratch.Count; i++)
            SceneSelectionSet.Remove(InvalidSelectionScratch[i]);
        if (InvalidSelectionScratch.Count > 0) MarkSelectionDirty();
        InvalidSelectionScratch.Clear();

        if (selectionAnchor >= count)
            selectionAnchor = FindMinimumSelection();
    }

    private int FindMinimumSelection()
    {
        if (SceneSelectionSet.Count == 0) return -1;
        int minimum = int.MaxValue;
        foreach (int index in SceneSelectionSet)
            if (index < minimum) minimum = index;
        return minimum == int.MaxValue ? -1 : minimum;
    }

    private void AddSelection(int index)
    {
        if (SceneSelectionSet.Add(index)) MarkSelectionDirty();
    }

    private void ClearSelectionSet()
    {
        if (SceneSelectionSet.Count == 0) return;
        SceneSelectionSet.Clear();
        MarkSelectionDirty();
    }

    private void MarkSelectionDirty()
    {
        selectedIndicesDirty = true;
    }
}
