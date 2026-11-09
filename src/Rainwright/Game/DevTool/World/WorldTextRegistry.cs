using System;
using System.IO;
using System.Threading.Tasks;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Owns the editable lossless world.txt document for the region currently being authored.
/// Unknown sections and mod directives remain untouched because WorldDocument only regenerates
/// room/spawner lines that were actually changed by the editor.
/// </summary>
internal static class WorldTextRegistry
{
    private static WorldDocument document;
    private static int revision;
    private static int authoringRevision;
    private static bool topologyDirty;
    private static readonly WorldSourceFailureRetry failedSource = new();
    private static Task<WorldRoomRegistration.Result> roomRegistration;

    internal static string LoadedRegion { get; private set; } = string.Empty;
    internal static string LoadedPath { get; private set; } = string.Empty;
    internal static string LoadError { get; private set; }
    internal static bool Dirty => document?.Dirty == true || WorldCreatureAuthoringHooks.AdditionalDirty;
    internal static int Revision => revision;
    internal static int AuthoringRevision => authoringRevision;
    internal static string AuthorText => document?.Serialize() ?? string.Empty;

    internal static bool EnsureLoaded(string region)
    {
        // This method sits on several immediate-mode and map-topology hot paths. In the stable case
        // the caller already passes the active region id, so avoid Trim/ToUpperInvariant entirely.
        if (document != null &&
            !string.IsNullOrWhiteSpace(region) &&
            string.Equals(LoadedRegion, region, StringComparison.OrdinalIgnoreCase))
        {
            CompleteRoomRegistration();
            return true;
        }

        bool sameFailedRegion = document == null && !string.IsNullOrWhiteSpace(region) &&
            string.Equals(LoadedRegion, region, StringComparison.OrdinalIgnoreCase);
        if (sameFailedRegion && !failedSource.ShouldRetry(region)) return false;

        string normalized = NormalizeRegion(region);
        if (normalized.Length == 0) return false;

        if (!sameFailedRegion && document == null && string.Equals(LoadedRegion, normalized, StringComparison.OrdinalIgnoreCase) &&
            !failedSource.ShouldRetry(normalized))
            return false;

        if (document != null &&
            string.Equals(LoadedRegion, normalized, StringComparison.OrdinalIgnoreCase))
            return true;

        if (Dirty)
        {
            LoadError = "Cannot switch world.txt documents while the current document has unsaved changes.";
            return false;
        }

        return Reload(normalized);
    }

    internal static bool Reload(string region)
    {
        // Protect actual unsaved author edits. A pending post-save lineage
        // synchronization is not unsaved disk content and must be allowed to
        // call Reload; otherwise it deadlocks against its own Dirty marker.
        if (document?.Dirty == true || WorldLineageRegistry.Dirty) return false;

        string normalized = NormalizeRegion(region);
        LoadedRegion = normalized;
        LoadedPath = string.Empty;
        LoadError = null;
        document = null;
        roomRegistration = null;
        topologyDirty = false;
        BumpRevision();

        if (normalized.Length == 0)
        {
            LoadError = "Region id is empty.";
            return false;
        }

        using var loadTiming = Core.EditorSlowOperation.Measure("Map.Open.WorldText");
        using (Core.EditorSlowOperation.Measure("Map.Open.SourceNotifications"))
            failedSource.Prepare(normalized);
        try
        {
            string relativePath =
                "World" + Path.DirectorySeparatorChar +
                normalized + Path.DirectorySeparatorChar +
                "world_" + normalized + ".txt";
            string path = WorldAuthoringPathResolver.ResolveExistingSource(relativePath);

            if (!WorldAuthoringPathResolver.CanReadSource(path))
            {
                string readPath = WorldAuthoringPathResolver.ResolveReadPath(relativePath);
                LoadError = WorldAuthoringPathResolver.IsMergedCachePath(readPath)
                    ? "world_" + normalized + ".txt resolves only to generated mergedmods data; no lossless writable mod source was found."
                    : "world_" + normalized + ".txt could not be resolved to a writable source file.";
                return false;
            }

            LoadedPath = path;
            string sourceText = WorldAuthoringPathResolver.ReadAllText(path);
            string authored = WorldFileVariantStore.Load(normalized, path, sourceText);
            document = WorldDocument.Parse(authored, path);
            WorldConnectionSyntax.SynchronizeRegion(normalized, document);
            int repaired = document.NormalizeLegacyConnectionTokens();
            if (repaired > 0 || WorldFileVariants.Project(authored).Standard != sourceText || WorldFileVariantStore.NeedsMigration(path))
            {
                MarkAuthoringChanged();
                try
                {
                    // Repair is explicitly requested author migration, independent of autosave settings.
                    // Keep a separate original backup so later ordinary .bak saves cannot erase it.
                    if (WorldAuthoringPathResolver.ReadAllText(path) != sourceText)
                        throw new IOException("The world author file changed during legacy connection repair; the external edit was preserved.");
                    string backup = null;
                    if (File.Exists(path))
                    {
                        backup = path + ".rainwright-legacy-" + Guid.NewGuid().ToString("N") + ".bak";
                        DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Copy(path, backup, overwrite: false);
                    }
                    WorldFileVariantStore.Save(normalized, path, document.Serialize());
                    document.MarkSaved(path);
                    global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Info(
                        "Repaired " + repaired + " legacy world connection tokens to ConnectionExtensions syntax in '" + path + "'. Backup: " + (backup ?? "original read source unchanged"));
                }
                catch (Exception repairError)
                {
                    // Retain the repaired in-memory document as dirty for a retry/recovery, never
                    // discard it through the outer load catch or claim that it reached disk.
                    LoadError = "Legacy connection repair could not be saved: " + repairError.Message;
                    global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(LoadError + "\n" + repairError);
                    return false;
                }
            }
            roomRegistration = WorldRoomRegistration.Begin(normalized, path, document);
            return true;
        }
        catch (Exception error)
        {
            LoadError = error.Message;
            document = null;
            if (error is IOException io && WorldSourceFailureRetry.IsSharingViolation(io) && !string.IsNullOrEmpty(LoadedPath))
                failedSource.RememberSharingFailure(LoadedPath);
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("WorldText load failed: " + error);
            return false;
        }
        finally
        {
            if (document != null) failedSource.Clear();
        }
    }

    internal static void InvalidateFailedSourceContext() => failedSource.Clear();

    private static void CompleteRoomRegistration()
    {
        if (roomRegistration?.IsCompleted != true || Dirty || WorldLineageRegistry.Dirty) return;
        var completed = roomRegistration;
        roomRegistration = null;
        try
        {
            var found = completed.GetAwaiter().GetResult();
            foreach (string warning in found.Warnings) Core.EditorDiagnostics.Warning(warning);
            int added = 0;
            foreach (var room in found.Rooms)
            {
                if (!room.SourceUnchanged())
                {
                    Core.EditorDiagnostics.Warning("Room source changed after discovery; registration skipped: " + room.Path);
                    continue;
                }
                if (document.TryAddRoom(room.Name, room.Exits)) added++;
            }
            if (added == 0) return;
            BumpRevision();
            // Do not fabricate live AbstractRooms or recycle indexes in an already loaded World.
            // Save verifies the original author bytes and retains dirty data on any conflict.
            if (Save()) Core.EditorDiagnostics.Info("Registered " + added + " room(s) in '" + LoadedPath + "'. Reload the region to load the added rooms.");
        }
        catch (Exception error)
        {
            LoadError = "Room registration failed: " + error.Message;
            Core.EditorDiagnostics.Error(LoadError + "\n" + error);
        }
    }

    internal static bool HasRoom(string region, string roomName)
    {
        return EnsureLoaded(region) &&
               document != null &&
               document.TryGetRoom(roomName, out _);
    }

    internal static bool TryGetConnection(
        string region,
        string roomName,
        int exitIndex,
        out string destination)
    {
        destination = string.Empty;
        if (!EnsureLoaded(region) || document == null) return false;
        return document.TryGetConnection(roomName, exitIndex, out destination);
    }

    internal static bool TryGetConnectionEndpoint(
        string region,
        string roomName,
        int exitIndex,
        out string destinationRoom,
        out int destinationNode)
    {
        destinationRoom = string.Empty;
        destinationNode = -1;
        if (!TryGetConnection(region, roomName, exitIndex, out string token)) return false;
        if (!WorldConnectionSyntax.TryParseDestination(token, out destinationRoom, out destinationNode))
            return false;

        // Vanilla world.txt stores only the destination room for ordinary links. When the opposite
        // room contains one and only one reciprocal route back to this source Exit, that reciprocal
        // slot is the exact target node. Resolve it here so the editor never turns a perfectly
        // deterministic old-format link into "Room:?" merely because it lacks the extended <n>
        // prefix. Repeated room-to-room links intentionally remain unresolved for manual mapping.
        if (destinationNode < 0 &&
            !string.Equals(destinationRoom, "DISCONNECTED", StringComparison.OrdinalIgnoreCase))
        {
            int reciprocal = FindUniqueReciprocalNode(roomName, exitIndex, destinationRoom);
            if (reciprocal >= 0) destinationNode = reciprocal;
        }

        return true;
    }

    private static int FindUniqueReciprocalNode(
        string sourceRoom,
        int sourceNode,
        string destinationRoom)
    {
        if (document == null ||
            string.IsNullOrWhiteSpace(sourceRoom) ||
            string.IsNullOrWhiteSpace(destinationRoom) ||
            !document.TryGetRoom(sourceRoom, out WorldRoomRecord source) ||
            !document.TryGetRoom(destinationRoom, out WorldRoomRecord target))
            return -1;

        // A plain reciprocal token cannot identify which source Exit it points to if the source
        // room itself has multiple links to the same destination room. In that case the mapping is
        // genuinely ambiguous and must stay editable as such in the UI.
        int sourceMultiplicity = 0;
        for (int i = 0; i < source.Connections.Count; i++)
        {
            if (!WorldConnectionSyntax.TryParseDestination(
                    source.Connections[i],
                    out string sourceDestination,
                    out _))
                continue;
            if (string.Equals(sourceDestination, destinationRoom, StringComparison.OrdinalIgnoreCase))
                sourceMultiplicity++;
        }

        int match = -1;
        for (int i = 0; i < target.Connections.Count; i++)
        {
            if (!WorldConnectionSyntax.TryParseDestination(
                    target.Connections[i],
                    out string backRoom,
                    out int backTargetNode))
                continue;
            if (!string.Equals(backRoom, sourceRoom, StringComparison.OrdinalIgnoreCase))
                continue;

            // Extended syntax can disambiguate repeated links explicitly. Plain vanilla syntax is
            // safe only when this source room has a single route to the destination room.
            if (backTargetNode >= 0)
            {
                if (backTargetNode != sourceNode) continue;
            }
            else if (sourceMultiplicity != 1)
            {
                continue;
            }

            if (match >= 0) return -1;
            match = i;
        }

        return match;
    }

    internal static bool TrySetConnection(
        string region,
        string roomName,
        int exitIndex,
        string destinationRoom,
        out string error)
    {
        error = null;
        if (!EnsureLoaded(region) || document == null)
        {
            error = LoadError ?? "world.txt is unavailable.";
            return false;
        }

        if (!document.TryGetRoom(roomName, out _))
        {
            error = "Room '" + roomName + "' does not exist in the editable ROOMS section.";
            return false;
        }

        string storedDestination = WorldConnectionSyntax.FormatForSource(
            region,
            roomName,
            exitIndex,
            destinationRoom);
        bool changed = document.TrySetConnection(roomName, exitIndex, storedDestination);

        WorldConnectionSyntax.SynchronizeRoute(region, roomName, exitIndex, storedDestination);
        WorldTopologyLiveTraversalFix.OnConnectionChanged(region, roomName, exitIndex, storedDestination);
        if (changed)
        {
            topologyDirty = true;
            BumpRevision();
            WorldTopologyRuntime.NotifyTopologyChanged();
        }
        return true;
    }

    internal static WorldCreatureSpawnRecord[] GetCreatureSpawns(string region, string roomName)
    {
        if (!EnsureLoaded(region) || document == null)
            return Array.Empty<WorldCreatureSpawnRecord>();
        return document.GetCreatureSpawns(roomName);
    }

    internal static bool TryAddCreatureSpawn(
        string region,
        string roomName,
        int denNode,
        string creature,
        int amount,
        string spawnData,
        string timelineFilter,
        bool excludeTimeline,
        out int spawnId,
        out string error)
    {
        spawnId = -1;
        error = null;
        if (!EnsureLoaded(region) || document == null)
        {
            error = LoadError ?? "world.txt is unavailable.";
            return false;
        }
        if (!document.TryGetRoom(roomName, out _))
        {
            error = "Room '" + roomName + "' does not exist in the editable ROOMS section.";
            return false;
        }
        if (denNode < 0)
        {
            error = "A valid creature den node is required.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(creature))
        {
            error = "Creature id is empty.";
            return false;
        }

        if (!document.TryAddCreatureSpawn(
                roomName,
                denNode,
                creature,
                amount,
                spawnData,
                timelineFilter,
                excludeTimeline,
                out spawnId))
        {
            error = "Could not add the creature spawner to world.txt.";
            return false;
        }

        MarkAuthoringChanged();
        WorldCreatureAuthoringHooks.OnCreatureAdded(region, roomName);
        return true;
    }

    internal static bool TryUpdateCreatureSpawn(
        string region,
        int spawnId,
        int denNode,
        string creature,
        int amount,
        string spawnData,
        string timelineFilter,
        bool excludeTimeline,
        out string error)
    {
        error = null;
        if (!EnsureLoaded(region) || document == null)
        {
            error = LoadError ?? "world.txt is unavailable.";
            return false;
        }
        string roomName = WorldCreatureAuthoringHooks.FindSpawnRoom(region, spawnId);
        if (!document.TryUpdateCreatureSpawn(
                spawnId,
                denNode,
                creature,
                amount,
                spawnData,
                timelineFilter,
                excludeTimeline))
        {
            error = "Could not update the creature spawner.";
            return false;
        }

        MarkAuthoringChanged();
        WorldCreatureAuthoringHooks.OnCreatureEdited(region, roomName);
        return true;
    }

    internal static bool TryDeleteCreatureSpawn(string region, int spawnId, out string error)
    {
        error = null;
        if (!EnsureLoaded(region) || document == null)
        {
            error = LoadError ?? "world.txt is unavailable.";
            return false;
        }
        string roomName = WorldCreatureAuthoringHooks.FindSpawnRoom(region, spawnId);
        if (!document.TryDeleteCreatureSpawn(spawnId))
        {
            error = "Could not find the creature spawner to delete.";
            return false;
        }

        MarkAuthoringChanged();
        WorldCreatureAuthoringHooks.OnCreatureEdited(region, roomName);
        return true;
    }

    // A recovery snapshot serializes the live lossless author document without writing world.txt.
    internal static bool TryGetRecoveryText(out string path, out string text)
    {
        path = LoadedPath;
        text = string.Empty;
        if (document == null || !document.Dirty && !WorldLineageRegistry.Dirty || string.IsNullOrWhiteSpace(path)) return false;
        text = document.Dirty ? document.Serialize() : WorldAuthoringPathResolver.ReadAllText(path);
        if (!WorldLineageRegistry.TryComposeRecoveryText(path, text, out text, out string error))
        { global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error("Creature recovery composition failed: " + error); return false; }
        return true;
    }

    internal static bool Save()
    {
        if (document == null || string.IsNullOrWhiteSpace(LoadedPath)) return false;

        string region = LoadedRegion;
        // Also catch legacy tokens supplied after loading; exact route semantics stay unchanged.
        if (document.NormalizeLegacyConnectionTokens() > 0) MarkAuthoringChanged();
        if (document.Dirty)
        {
            try
            {
                WorldFileVariantStore.Save(region, LoadedPath, document.Serialize());
                document.MarkSaved(LoadedPath);

                if (topologyDirty)
                {
                    WorldConnectionSyntax.SynchronizeRegion(region, document);
                    WorldTopologyRuntime.NotifyTopologyChanged();
                    topologyDirty = false;
                }
                LoadError = null;
            }
            catch (Exception error)
            {
                LoadError = error.Message;
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error("WorldText save failed: " + error);
                return false;
            }
        }

        if (!WorldCreatureAuthoringHooks.SaveAdditional(region))
        {
            LoadError = "Lineage save failed.";
            return false;
        }

        LoadError = null;
        return true;
    }

    internal static void Clear()
    {
        if (Dirty) return;
        if (document != null || LoadedRegion.Length > 0 || LoadedPath.Length > 0)
            BumpRevision();
        document = null;
        roomRegistration = null;
        LoadedRegion = string.Empty;
        failedSource.Clear();
        LoadedPath = string.Empty;
        LoadError = null;
    }

    private static void BumpRevision()
    {
        MarkAuthoringChanged();
        unchecked
        {
            revision++;
            if (revision == int.MinValue) revision = 1;
        }
    }

    internal static void MarkAuthoringChanged()
    {
        unchecked { authoringRevision++; if (authoringRevision == int.MinValue) authoringRevision = 1; }
    }

    private static string NormalizeRegion(string region) =>
        string.IsNullOrWhiteSpace(region) ? string.Empty : region.Trim().ToUpperInvariant();
}
