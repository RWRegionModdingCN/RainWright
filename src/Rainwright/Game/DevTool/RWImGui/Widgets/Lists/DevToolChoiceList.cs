using System;
using System.Collections.Generic;
using ImGuiNET;

namespace DryCycle.DevUI.DevTool.RWImGui;

// The owning field retains this projection. Values and original indices remain model-owned;
// filtering only changes which single-line options the popup needs to submit.
internal sealed class DevToolChoiceList
{
    private string[] source;
    private string query;
    private readonly List<int> matches = new();

    internal int Draw(string[] options, int selected, string filter, string[] labels = null)
    {
        options ??= Array.Empty<string>();
        filter = string.IsNullOrWhiteSpace(filter) ? string.Empty : filter;
        if (!ReferenceEquals(source, options) || !string.Equals(query, filter, StringComparison.Ordinal))
        {
            source = options; query = filter; matches.Clear();
            if (filter.Length > 0)
                for (int i = 0; i < options.Length; i++)
                    if ((options[i] ?? string.Empty).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) matches.Add(i);
        }
        return DrawRows(options, labels, selected, filter.Length > 0 ? matches : null);
    }

    internal static int DrawAll(string[] options, int selected = -1) => DrawRows(options ?? Array.Empty<string>(), null, selected, null);

    private static int DrawRows(string[] options, string[] labels, int selected, List<int> indices)
    {
        int count = indices?.Count ?? options.Length;
        int focusRow = indices == null ? selected : indices.BinarySearch(selected);
        if (focusRow >= count) focusRow = -1;
        int chosen = -1;
        if (count < 32)
        {
            for (int i = 0; i < count; i++)
            {
                int index = indices == null ? i : indices[i];
                if (DrawOption(options, labels, index, selected)) chosen = index;
            }
        }
        else
        {
            using DevToolListClipper clipper = new(count);
            clipper.Include(focusRow);
            while (clipper.Step(out int first, out int last))
                for (int i = first; i < last; i++)
                {
                    int index = indices == null ? i : indices[i];
                    if (DrawOption(options, labels, index, selected)) chosen = index;
                }
        }
        return chosen;
    }

    private static bool DrawOption(string[] options, string[] labels, int index, int selected)
    {
        ImGui.PushID(index);
        bool chosen = ImGui.Selectable((labels ?? options)[index] ?? string.Empty, index == selected);
        if (index == selected) ImGui.SetItemDefaultFocus();
        ImGui.PopID();
        return chosen;
    }
}
