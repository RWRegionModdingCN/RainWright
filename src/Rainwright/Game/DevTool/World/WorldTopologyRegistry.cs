using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Authoritative editable store for explicit endpoint-to-endpoint world connections.
///
/// Vanilla world.txt only records the destination room for each source exit. When two
/// rooms appear more than once in each other's connection arrays, AbstractRoom.ExitIndex
/// resolves the target with Array.IndexOf and therefore always chooses the first pipe.
/// WorldTopology.json stores the missing target-node identity and direction without
/// replacing or rewriting unrelated world.txt syntax.
/// </summary>
internal static class WorldTopologyRegistry
{
    internal const string FileName = "WorldTopology.json";

    private const int CurrentVersion = 1;
    private static int observedPathOwnerRevision;
    private static int observedRegionBindingRevision;

    private static WorldTopologyDocument document = new();
    private static readonly List<string> warnings = new();
    private static bool loaded;
    // A verified .bak is the only known-good copy after a broken primary.
    // It must not be overwritten with corrupt primary bytes on recovery save.
    private static bool recoveredFromBackup;
    private static int revision = 1;

    internal static bool Dirty { get; private set; }
    internal static string LoadedPath { get; private set; } = string.Empty;
    internal static string LoadError { get; private set; }
    internal static IReadOnlyList<string> Warnings => warnings;

    /// <summary>
    /// Semantic in-memory topology revision. Persistence alone does not change it; replacing the
    /// loaded document or changing an edge does. Map presentation can therefore distinguish an
    /// actual connection change from an unrelated map visual edit without polling every edge.
    /// </summary>
    internal static int Revision => revision;
    internal static string SerializeAuthoringState()
    {
        EnsureLoaded();
        if (!WorldTopologyPathOwnership.HasOwner) throw new IOException("World topology owner is unavailable; legacy author files were preserved.");
        if (LoadError != null && !recoveredFromBackup) throw new IOException(LoadError);
        if (warnings.Count > 0) throw new IOException("Topology contains unrecognized data and cannot be automatically migrated: " + string.Join("; ", warnings));
        if (Dirty && !WorldTopologyAuthorWriteGuard.TryAuthorize(LoadedPath, observedPathOwnerRevision, ResolvePath(true), WorldTopologyPathOwnership.Revision, out _, out string error)) throw new IOException(error);
        return Json.Serialize(BuildJsonRoot());
    }
    internal static string[] RegionNames() { EnsureLoaded(); return new List<string>(document.Regions.Keys).ToArray(); }
    internal static string OverlayLegacyRoutes(string region, string text)
    {
        var target = WorldDocument.Parse(text);
        foreach (var edge in GetRegionEdges(region))
        {
            if (edge.AllowsFromA) Apply(edge.A, edge.B);
            if (edge.AllowsFromB) Apply(edge.B, edge.A);
        }
        return target.Serialize();
        void Apply(WorldConnectionEndpoint from, WorldConnectionEndpoint to)
        {
            if (!target.TryGetConnection(from.Room, from.NodeIndex, out string token) || !WorldConnectionToken.TryParse(token, out string room, out int node) ||
                !room.Equals(to.Room, StringComparison.OrdinalIgnoreCase) || !target.TryGetRoom(to.Room, out var other) || to.NodeIndex < 0 || to.NodeIndex >= other.Connections.Count || node >= 0 && node != to.NodeIndex)
                throw new InvalidDataException("Legacy topology conflicts with the current world at " + from + "; original files were preserved.");
            target.TrySetConnection(from.Room, from.NodeIndex, WorldConnectionToken.Format(to.Room, to.NodeIndex));
        }
    }

    internal static void EnsureLoaded()
    {
        if (!loaded || (!Dirty && (observedPathOwnerRevision != WorldTopologyPathOwnership.Revision ||
            observedRegionBindingRevision != WorldModificationWorkspace.BindingRevision))) Reload();
    }

    internal static void Reload()
    {
        if (Dirty) return;

        loaded = true;
        observedPathOwnerRevision = WorldTopologyPathOwnership.Revision;
        recoveredFromBackup = false;
        warnings.Clear();
        LoadError = null;
        LoadedPath = ResolvePath(forSave: true) ?? string.Empty;
        observedRegionBindingRevision = WorldModificationWorkspace.BindingRevision;

        string readPath = File.Exists(LoadedPath) ? LoadedPath : ResolvePath(forSave: false);
        if (string.IsNullOrEmpty(readPath) || !File.Exists(readPath))
        {
            document = new WorldTopologyDocument();
            Dirty = false;
            BumpRevision();
            return;
        }

        if (TryLoadFile(readPath, out WorldTopologyDocument parsed, out string error))
        {
            document = parsed;
            Dirty = false;
            BumpRevision();
            return;
        }

        string backup = readPath + ".bak";
        string backupError = null;
        if (File.Exists(backup) && TryLoadFile(backup, out parsed, out backupError))
        {
            document = parsed;
            Dirty = true;
            recoveredFromBackup = true;
            LoadError = $"Primary {FileName} is invalid ({error}); recovered from {Path.GetFileName(backup)}.";
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("WorldTopology: " + LoadError);
            BumpRevision();
            return;
        }

        document = new WorldTopologyDocument();
        Dirty = false;
        LoadError = File.Exists(backup)
            ? $"{FileName} and backup are invalid. Primary: {error}; backup: {backupError}"
            : $"{FileName} is invalid and has no backup: {error}";
        global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error("WorldTopology: " + LoadError);
        BumpRevision();
    }

    internal static WorldConnectionEdge[] GetRegionEdges(string region)
    {
        EnsureLoaded();
        if (!document.TryGetRegion(region, out List<WorldConnectionEdge> edges) || edges.Count == 0)
            return Array.Empty<WorldConnectionEdge>();

        WorldConnectionEdge[] result = new WorldConnectionEdge[edges.Count];
        for (int i = 0; i < edges.Count; i++) result[i] = edges[i].Clone();
        return result;
    }

    internal static bool TryAddEdge(
        string region,
        WorldConnectionEndpoint a,
        WorldConnectionEndpoint b,
        WorldConnectionDirection direction,
        out string edgeId,
        out string error)
    {
        EnsureLoaded();
        edgeId = string.Empty;
        error = null;

        if (!a.IsValid || !b.IsValid)
        {
            error = "Both connection endpoints must have a room and a non-negative node index.";
            return false;
        }
        if (a.Equals(b))
        {
            error = "A connection cannot connect an endpoint to itself.";
            return false;
        }

        List<WorldConnectionEdge> edges = document.GetOrCreateRegion(region);
        for (int i = 0; i < edges.Count; i++)
        {
            WorldConnectionEdge edge = edges[i];
            if (SameUndirectedEdge(edge.A, edge.B, a, b))
            {
                error = "That endpoint pair is already connected.";
                return false;
            }
            if (edge.A.Equals(a) || edge.B.Equals(a))
            {
                error = a + " is already used by connection " + edge.Id + ".";
                return false;
            }
            if (edge.A.Equals(b) || edge.B.Equals(b))
            {
                error = b + " is already used by connection " + edge.Id + ".";
                return false;
            }
        }

        edgeId = Guid.NewGuid().ToString("N");
        edges.Add(new WorldConnectionEdge
        {
            Id = edgeId,
            A = a,
            B = b,
            Direction = direction
        });
        Dirty = true;
        BumpRevision();
        return true;
    }

    internal static bool RemoveEdge(string region, string edgeId)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(edgeId) ||
            !document.TryGetRegion(region, out List<WorldConnectionEdge> edges))
            return false;

        for (int i = 0; i < edges.Count; i++)
        {
            if (!string.Equals(edges[i].Id, edgeId, StringComparison.OrdinalIgnoreCase)) continue;
            edges.RemoveAt(i);
            Dirty = true;
            BumpRevision();
            return true;
        }
        return false;
    }

    internal static bool SetDirection(
        string region,
        string edgeId,
        WorldConnectionDirection direction)
    {
        EnsureLoaded();
        if (string.IsNullOrWhiteSpace(edgeId) ||
            !document.TryGetRegion(region, out List<WorldConnectionEdge> edges))
            return false;

        for (int i = 0; i < edges.Count; i++)
        {
            if (!string.Equals(edges[i].Id, edgeId, StringComparison.OrdinalIgnoreCase)) continue;
            if (edges[i].Direction == direction) return false;
            edges[i].Direction = direction;
            Dirty = true;
            BumpRevision();
            return true;
        }
        return false;
    }

    internal static bool TryResolveOutgoing(
        string region,
        string sourceRoom,
        int sourceNode,
        out WorldConnectionEndpoint destination,
        out string edgeId)
    {
        EnsureLoaded();
        destination = default;
        edgeId = string.Empty;
        if (!document.TryGetRegion(region, out List<WorldConnectionEdge> edges)) return false;

        WorldConnectionEndpoint source = new(sourceRoom, sourceNode);
        for (int i = 0; i < edges.Count; i++)
        {
            WorldConnectionEdge edge = edges[i];
            if (edge.A.Equals(source) && edge.AllowsFromA)
            {
                destination = edge.B;
                edgeId = edge.Id;
                return true;
            }
            if (edge.B.Equals(source) && edge.AllowsFromB)
            {
                destination = edge.A;
                edgeId = edge.Id;
                return true;
            }
        }
        return false;
    }

    internal static List<WorldTopologyIssue> ValidateRegion(
        string region,
        Func<string, bool> roomExists = null,
        Func<string, int, bool> nodeExists = null)
    {
        EnsureLoaded();
        List<WorldTopologyIssue> issues = new();
        if (!document.TryGetRegion(region, out List<WorldConnectionEdge> edges)) return issues;

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<WorldConnectionEndpoint, string> endpointOwners = new();

        for (int i = 0; i < edges.Count; i++)
        {
            WorldConnectionEdge edge = edges[i];
            if (string.IsNullOrWhiteSpace(edge.Id) || !ids.Add(edge.Id))
                AddIssue(issues, WorldTopologyIssueKind.DuplicateId, edge.Id, "Connection id is empty or duplicated.");

            if (!edge.A.IsValid || !edge.B.IsValid || edge.A.Equals(edge.B))
                AddIssue(issues, WorldTopologyIssueKind.InvalidEndpoint, edge.Id, "Connection contains an invalid endpoint.");

            RegisterEndpoint(issues, endpointOwners, edge.Id, edge.A);
            RegisterEndpoint(issues, endpointOwners, edge.Id, edge.B);

            for (int j = 0; j < i; j++)
            {
                if (SameUndirectedEdge(edges[j].A, edges[j].B, edge.A, edge.B))
                {
                    AddIssue(issues, WorldTopologyIssueKind.DuplicateEdge, edge.Id, "The same endpoint pair is connected more than once.");
                    break;
                }
            }

            ValidateEndpointAgainstWorld(issues, edge.Id, edge.A, roomExists, nodeExists);
            ValidateEndpointAgainstWorld(issues, edge.Id, edge.B, roomExists, nodeExists);
        }

        return issues;
    }

    internal static bool TryGetRecoveryText(out string path, out string text)
    {
        path = string.Empty;
        text = string.Empty;
        EnsureLoaded();
        if (!Dirty) return false;

        // Recovery snapshots carry an author destination. A cached LoadedPath
        // cannot authorize restoring into a Mod that has since been disabled,
        // unloaded or replaced; use the same owner validation as Save().
        if (!WorldTopologyAuthorWriteGuard.TryAuthorize(
                LoadedPath, observedPathOwnerRevision,
                ResolvePath(forSave: true), WorldTopologyPathOwnership.Revision,
                out string authorizedPath, out _))
            return false;

        path = authorizedPath;
        text = Json.Serialize(BuildJsonRoot());
        if (WorldModificationWorkspace.HasRegionOwner)
        {
            var projection = WorldFileVariants.Project(WorldTextRegistry.AuthorText);
            if (!projection.RequiresExtension) return false;
            text = WorldFileVariants.Encode(projection.Extended, text);
        }
        return true;
    }

    internal static bool Save()
    {
        EnsureLoaded();

        // Do not interpret a corrupt source as an empty world topology and
        // subsequently overwrite it on an ordinary editor Save. The author
        // must repair the file or restore a verified backup first.
        if (LoadError != null && !recoveredFromBackup)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "WorldTopology save refused: loaded source is invalid and no verified backup was recovered. " +
                LoadError);
            return false;
        }

        // A clean editor Save must not create/overwrite gameplay-owned
        // WorldTopology.json while the developer is authoring another Mod.
        if (!Dirty) return true;

        // Never trust a cached LoadedPath alone. Mods can be removed,
        // switched or replaced while the in-memory topology is still Dirty.
        // Only the current gameplay owner may authorize an actual write.
        if (!WorldTopologyAuthorWriteGuard.TryAuthorize(
                LoadedPath, observedPathOwnerRevision,
                ResolvePath(forSave: true), WorldTopologyPathOwnership.Revision,
                out string path, out string pathError))
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(pathError);
            return false;
        }

        try
        {
            string payload = Json.Serialize(BuildJsonRoot());
            if (WorldModificationWorkspace.HasRegionOwner)
                WorldFileVariantStore.SaveTopology(payload);
            else if (recoveredFromBackup)
                WorldTopologyRecoveryWriter.WritePreservingValidBackup(path, payload);
            else
                WorldAuthoringPathResolver.AtomicWriteAllText(path, payload);

            LoadedPath = path;
            Dirty = false;
            recoveredFromBackup = false;
            LoadError = null;
            warnings.Clear();
            return true;
        }
        catch (Exception ex)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "WorldTopology save failed; unsaved changes and verified recovery backup were retained.", ex);
            return false;
        }
    }

    private static bool TryLoadFile(string path, out WorldTopologyDocument parsed, out string error)
    {
        parsed = new WorldTopologyDocument();
        error = null;
        try
        {
            string contents = File.ReadAllText(path);
            string json = WorldFileVariants.IsOwned(contents) ? WorldFileVariants.Topology(contents) ?? "{\"version\":1,\"regions\":{}}" : contents;
            object value = Json.Deserialize(json);
            if (value is not Dictionary<string, object> root)
            {
                error = "root JSON value is not an object";
                return false;
            }

            if (root.TryGetValue("version", out object versionObject) &&
                TryInt(versionObject, out int version) && version != CurrentVersion)
            {
                warnings.Add($"{FileName} version {version} differs from supported version {CurrentVersion}; known fields were loaded.");
            }

            if (!root.TryGetValue("regions", out object regionsObject) || regionsObject == null) return true;
            if (regionsObject is not Dictionary<string, object> regions)
            {
                error = "'regions' is not an object";
                return false;
            }

            foreach (KeyValuePair<string, object> regionPair in regions)
            {
                string region = WorldTopologyDocument.NormalizeRegion(regionPair.Key);
                if (region.Length == 0 || regionPair.Value is not Dictionary<string, object> regionObject)
                {
                    warnings.Add("Skipped malformed region entry '" + regionPair.Key + "'.");
                    continue;
                }
                if (!regionObject.TryGetValue("connections", out object connectionsObject) || connectionsObject == null)
                    continue;
                if (connectionsObject is not List<object> connections)
                {
                    warnings.Add(region + ": 'connections' is not an array and was ignored.");
                    continue;
                }

                List<WorldConnectionEdge> target = parsed.GetOrCreateRegion(region);
                for (int i = 0; i < connections.Count; i++)
                {
                    if (connections[i] is not Dictionary<string, object> connectionObject)
                    {
                        warnings.Add(region + ": skipped connection #" + i + " (entry is not an object)." );
                        continue;
                    }
                    if (!TryParseEdge(connectionObject, out WorldConnectionEdge edge, out string edgeError))
                    {
                        warnings.Add(region + ": skipped connection #" + i + " (" + edgeError + ").");
                        continue;
                    }
                    target.Add(edge);
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool TryParseEdge(
        Dictionary<string, object> value,
        out WorldConnectionEdge edge,
        out string error)
    {
        edge = null;
        error = null;
        if (!value.TryGetValue("a", out object aObject) ||
            !value.TryGetValue("b", out object bObject) ||
            !TryParseEndpoint(aObject, out WorldConnectionEndpoint a) ||
            !TryParseEndpoint(bObject, out WorldConnectionEndpoint b))
        {
            error = "invalid endpoint";
            return false;
        }

        string id = value.TryGetValue("id", out object idObject)
            ? Convert.ToString(idObject, CultureInfo.InvariantCulture)
            : string.Empty;
        if (string.IsNullOrWhiteSpace(id)) id = Guid.NewGuid().ToString("N");

        WorldConnectionDirection direction = WorldConnectionDirection.Bidirectional;
        if (value.TryGetValue("direction", out object directionObject) && directionObject != null)
            direction = ParseDirection(Convert.ToString(directionObject, CultureInfo.InvariantCulture));

        edge = new WorldConnectionEdge
        {
            Id = id.Trim(),
            A = a,
            B = b,
            Direction = direction
        };
        return true;
    }

    private static bool TryParseEndpoint(object value, out WorldConnectionEndpoint endpoint)
    {
        endpoint = default;
        if (value is not Dictionary<string, object> obj ||
            !obj.TryGetValue("room", out object roomObject) ||
            !obj.TryGetValue("node", out object nodeObject) ||
            !TryInt(nodeObject, out int node))
            return false;

        endpoint = new WorldConnectionEndpoint(
            Convert.ToString(roomObject, CultureInfo.InvariantCulture),
            node);
        return endpoint.IsValid;
    }

    private static Dictionary<string, object> BuildJsonRoot()
    {
        Dictionary<string, object> root = new()
        {
            ["version"] = CurrentVersion
        };
        Dictionary<string, object> regionsObject = new(StringComparer.OrdinalIgnoreCase);
        List<string> regionNames = new(document.Regions.Keys);
        regionNames.Sort(StringComparer.OrdinalIgnoreCase);

        for (int regionIndex = 0; regionIndex < regionNames.Count; regionIndex++)
        {
            string region = regionNames[regionIndex];
            List<WorldConnectionEdge> source = document.Regions[region];
            List<object> connections = new();
            for (int i = 0; i < source.Count; i++)
            {
                WorldConnectionEdge edge = source[i];
                connections.Add(new Dictionary<string, object>
                {
                    ["id"] = edge.Id,
                    ["direction"] = DirectionName(edge.Direction),
                    ["a"] = EndpointJson(edge.A),
                    ["b"] = EndpointJson(edge.B)
                });
            }

            regionsObject[region] = new Dictionary<string, object>
            {
                ["connections"] = connections
            };
        }

        root["regions"] = regionsObject;
        return root;
    }

    private static Dictionary<string, object> EndpointJson(WorldConnectionEndpoint endpoint) => new()
    {
        ["room"] = endpoint.Room,
        ["node"] = endpoint.NodeIndex
    };

    private static string DirectionName(WorldConnectionDirection direction) => direction switch
    {
        WorldConnectionDirection.AToB => "AToB",
        WorldConnectionDirection.BToA => "BToA",
        _ => "Bidirectional"
    };

    private static WorldConnectionDirection ParseDirection(string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.Equals("AToB", StringComparison.OrdinalIgnoreCase) || normalized == "→")
            return WorldConnectionDirection.AToB;
        if (normalized.Equals("BToA", StringComparison.OrdinalIgnoreCase) || normalized == "←")
            return WorldConnectionDirection.BToA;
        return WorldConnectionDirection.Bidirectional;
    }

    private static void RegisterEndpoint(
        List<WorldTopologyIssue> issues,
        Dictionary<WorldConnectionEndpoint, string> owners,
        string edgeId,
        WorldConnectionEndpoint endpoint)
    {
        if (!endpoint.IsValid) return;
        if (owners.TryGetValue(endpoint, out string owner) &&
            !string.Equals(owner, edgeId, StringComparison.OrdinalIgnoreCase))
        {
            AddIssue(
                issues,
                WorldTopologyIssueKind.EndpointConflict,
                edgeId,
                endpoint + " is already owned by connection " + owner + ".");
        }
        else
        {
            owners[endpoint] = edgeId;
        }
    }

    private static void ValidateEndpointAgainstWorld(
        List<WorldTopologyIssue> issues,
        string edgeId,
        WorldConnectionEndpoint endpoint,
        Func<string, bool> roomExists,
        Func<string, int, bool> nodeExists)
    {
        if (!endpoint.IsValid) return;
        if (roomExists != null && !roomExists(endpoint.Room))
        {
            AddIssue(issues, WorldTopologyIssueKind.MissingRoom, edgeId, "Room does not exist: " + endpoint.Room);
            return;
        }
        if (nodeExists != null && !nodeExists(endpoint.Room, endpoint.NodeIndex))
            AddIssue(issues, WorldTopologyIssueKind.MissingNode, edgeId, "Exit does not exist: " + endpoint);
    }

    private static void AddIssue(
        List<WorldTopologyIssue> issues,
        WorldTopologyIssueKind kind,
        string edgeId,
        string message)
    {
        issues.Add(new WorldTopologyIssue
        {
            Kind = kind,
            EdgeId = edgeId ?? string.Empty,
            Message = message ?? string.Empty
        });
    }

    private static bool SameUndirectedEdge(
        WorldConnectionEndpoint a1,
        WorldConnectionEndpoint b1,
        WorldConnectionEndpoint a2,
        WorldConnectionEndpoint b2) =>
        (a1.Equals(a2) && b1.Equals(b2)) ||
        (a1.Equals(b2) && b1.Equals(a2));

    private static void BumpRevision()
    {
        unchecked
        {
            revision++;
            if (revision <= 0) revision = 1;
        }
    }

    private static bool TryInt(object value, out int number)
    {
        number = 0;
        try
        {
            double parsed = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsNaN(parsed) || double.IsInfinity(parsed)) return false;
            number = (int)parsed;
            return Math.Abs(parsed - number) < 0.0001d;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolvePath(bool forSave) =>
        WorldTopologyPathOwnership.Resolve(forSave);
}
