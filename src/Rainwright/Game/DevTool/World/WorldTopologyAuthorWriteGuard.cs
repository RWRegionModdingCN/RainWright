using System;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Validates a gameplay author target at commit time. The owner can disappear
/// or switch after loading while unsaved world edges remain in memory.
/// Nothing here resolves or creates directories; that belongs to the NR.B5 owner.
/// </summary>
internal static class WorldTopologyAuthorWriteGuard
{
    internal static bool TryAuthorize(string loadedPath, int loadedOwnerRevision,
        string currentPath, int currentOwnerRevision, out string approved, out string error)
    {
        approved = null;
        error = null;
        if (string.IsNullOrWhiteSpace(currentPath))
        {
            error = "WorldTopology save refused: the DryCycle gameplay Mod has no verified author target.";
            return false;
        }
        if (loadedOwnerRevision != currentOwnerRevision)
        {
            error = "WorldTopology save refused: the gameplay file owner changed after loading. " +
                "Unsaved edits are preserved; resolve the change before saving.";
            return false;
        }
        if (!string.IsNullOrEmpty(loadedPath) &&
            !string.Equals(loadedPath, currentPath, StringComparison.Ordinal))
        {
            error = "WorldTopology save refused: the original author file is no longer the current " +
                "gameplay-owned target. Unsaved edits are preserved.";
            return false;
        }
        approved = currentPath;
        return true;
    }
}
