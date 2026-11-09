using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Sound;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Detached Sound workspace group state: active local group, snapshot cache,
/// destination, ID validation and collision-free naming. The game frontend
/// supplies one already-captured SoundGroupSnapshot[]; no gameplay library
/// or plugin lifecycle type is referenced here.
/// </summary>
internal enum SoundLibraryDestination
{
    Scene,
    WorkingGroup,
    SceneAndWorkingGroup
}

internal sealed class SoundGroupWorkspaceCore
{
    private string activeGroupId = string.Empty;
    private SoundGroupSnapshot[] observedGroups;
    private SoundGroupSnapshot cachedActiveGroup;
    private bool groupsSynchronized;
    private SoundLibraryDestination libraryDestination = SoundLibraryDestination.Scene;

    internal string ActiveGroupId => activeGroupId;
    internal SoundLibraryDestination LibraryDestination
    {
        get => libraryDestination;
        set => libraryDestination = value;
    }

    internal void SynchronizeGroups(SoundGroupSnapshot[] groups)
    {
        groups ??= Array.Empty<SoundGroupSnapshot>();
        if (groupsSynchronized && ReferenceEquals(observedGroups, groups)) return;

        observedGroups = groups;
        groupsSynchronized = true;
        cachedActiveGroup = null;

        SoundGroupSnapshot firstLocal = null;
        for (int i = 0; i < groups.Length; i++)
        {
            SoundGroupSnapshot group = groups[i];
            if (!group.IsLocal) continue;
            firstLocal ??= group;
            if (!string.Equals(group.Id, activeGroupId, StringComparison.OrdinalIgnoreCase)) continue;
            cachedActiveGroup = group;
            return;
        }

        cachedActiveGroup = firstLocal;
        activeGroupId = firstLocal?.Id ?? string.Empty;
        if (string.IsNullOrEmpty(activeGroupId) && libraryDestination != SoundLibraryDestination.Scene)
            libraryDestination = SoundLibraryDestination.Scene;
    }

    internal bool TryGetActiveLocalGroup(SoundGroupSnapshot[] groups, out SoundGroupSnapshot group)
    {
        SynchronizeGroups(groups);
        group = cachedActiveGroup;
        return group != null;
    }

    internal void SetActiveGroup(string groupId)
    {
        string next = groupId?.Trim() ?? string.Empty;
        if (string.Equals(activeGroupId, next, StringComparison.OrdinalIgnoreCase)) return;
        activeGroupId = next;
        groupsSynchronized = false;
    }

    internal bool GroupIdExists(string groupId, SoundGroupSnapshot[] groups)
    {
        if (string.IsNullOrWhiteSpace(groupId)) return false;
        string value = groupId.Trim();
        groups ??= Array.Empty<SoundGroupSnapshot>();
        for (int i = 0; i < groups.Length; i++)
            if (string.Equals(groups[i].Id, value, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    internal string SuggestUniqueGroupId(string displayName, SoundGroupSnapshot[] groups)
    {
        string source = displayName ?? string.Empty;
        var chars = new List<char>(source.Length);
        bool upperNext = true;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            bool asciiLetter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
            bool digit = c >= '0' && c <= '9';
            if (!asciiLetter && !digit)
            {
                upperNext = true;
                continue;
            }

            if (chars.Count == 0 && digit)
                continue;

            if (asciiLetter)
            {
                char next = upperNext ? char.ToUpperInvariant(c) : c;
                chars.Add(next);
            }
            else
            {
                chars.Add(c);
            }
            upperNext = false;
        }

        string root = chars.Count > 0 ? new string(chars.ToArray()) : "SoundGroup";
        string candidate = root;
        int suffix = 2;
        while (GroupIdExists(candidate, groups))
            candidate = root + suffix++;
        return candidate;
    }

    internal bool IsValidGroupId(string groupId)
    {
        if (string.IsNullOrWhiteSpace(groupId)) return false;
        string value = groupId.Trim();
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool valid = (c >= 'A' && c <= 'Z') ||
                         (c >= 'a' && c <= 'z') ||
                         (c >= '0' && c <= '9') ||
                         c == '_' || c == '-' || c == '.';
            if (!valid) return false;
        }
        return true;
    }
}
