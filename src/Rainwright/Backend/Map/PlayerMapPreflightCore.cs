using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Map;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// A detached, single-room preflight input. Unity Vector2 and game Page objects
/// are projected only when a changed authoring revision invalidates preflight.
/// </summary>
internal sealed class PlayerMapPreflightRoom
{
    internal int RoomIndex { get; init; }
    internal string Name { get; init; } = string.Empty;
    internal float X { get; init; }
    internal float Y { get; init; }
    internal int Layer { get; init; }
    internal bool Disabled { get; init; }
    internal RoomMapBakeSnapshot Bake { get; init; } = RoomMapBakeSnapshot.Empty;
}

/// <summary>
/// Pure preflight for Player Map render/export. Only detached geometry snapshots
/// and map connectivity cross this boundary; no Unity, RW classes or author writes.
/// </summary>
internal static class PlayerMapPreflightCore
{
    private const int MaxDimension = 16384;
    private const long MaxPixels = 160_000_000L;

    internal static PlayerMapPreflightSnapshot Build(
        string regionName,
        PlayerMapPreflightRoom[] roomSources,
        EditorMapConnectionSnapshot[] connectionSources)
    {
        List<string> errors = new();
        List<string> warnings = new();
        Dictionary<int, PlayerMapPreflightRoom> enabledByIndex = new();
        HashSet<string> endpointClaims = new(StringComparer.Ordinal);

        int enabledRooms = 0;
        int readyRooms = 0;
        int pendingRooms = 0;
        int missingRooms = 0;
        int failedRooms = 0;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        PlayerMapPreflightRoom[] rooms = roomSources ?? Array.Empty<PlayerMapPreflightRoom>();
        for (int i = 0; i < rooms.Length; i++)
        {
            PlayerMapPreflightRoom room = rooms[i];
            if (room == null || room.Disabled) continue;
            enabledRooms++;
            enabledByIndex[room.RoomIndex] = room;

            if (!Finite(room.X) || !Finite(room.Y))
            {
                errors.Add((room.Name ?? room.RoomIndex.ToString()) + ": Canon Position contains NaN/Infinity.");
                continue;
            }

            RoomMapBakeSnapshot bake = room.Bake ?? RoomMapBakeSnapshot.Empty;
            switch (bake.Status)
            {
                case RoomMapBakeStatus.Ready:
                    readyRooms++;
                    if (bake.Width <= 0 || bake.Height <= 0)
                    {
                        errors.Add(room.Name + ": ready room bake has an invalid size.");
                        break;
                    }
                    float halfW = bake.Width * PlayerMapCoordinatePolicy.CanonPixelsPerTile * 0.5f;
                    float halfH = bake.Height * PlayerMapCoordinatePolicy.CanonPixelsPerTile * 0.5f;
                    minX = Math.Min(minX, room.X - halfW);
                    minY = Math.Min(minY, room.Y - halfH);
                    maxX = Math.Max(maxX, room.X + halfW);
                    maxY = Math.Max(maxY, room.Y + halfH);
                    break;
                case RoomMapBakeStatus.Pending:
                    pendingRooms++;
                    break;
                case RoomMapBakeStatus.Missing:
                    missingRooms++;
                    errors.Add(room.Name + ": room source file is missing" +
                               (string.IsNullOrWhiteSpace(bake.Error) ? "." : " (" + bake.Error + ")"));
                    break;
                case RoomMapBakeStatus.Failed:
                    failedRooms++;
                    errors.Add(room.Name + ": room bake failed" +
                               (string.IsNullOrWhiteSpace(bake.Error) ? "." : " (" + bake.Error + ")"));
                    break;
            }
        }

        if (enabledRooms == 0)
            errors.Add("No enabled rooms are available for Player Map rendering.");

        int width = 0;
        int layerHeight = 0;
        int height = 0;
        bool outputSafe = false;
        if (readyRooms > 0 && minX != float.MaxValue)
        {
            width = SafeOutputDimension(maxX - minX);
            layerHeight = SafeOutputDimension(maxY - minY);
            long fullHeight = (long)layerHeight * PlayerMapCoordinatePolicy.LayerCount;
            height = fullHeight > int.MaxValue ? int.MaxValue : (int)fullHeight;
            outputSafe = width > 0 && layerHeight > 0 && height > 0 &&
                         width <= MaxDimension && height <= MaxDimension &&
                         (long)width * height <= MaxPixels;
            if (!outputSafe)
                errors.Add("Output dimensions are unsafe: " + width + "x" + height + ".");
        }

        int exactConnections = 0;
        int ambiguousConnections = 0;
        int invalidEndpoints = 0;
        int duplicateClaims = 0;
        EditorMapConnectionSnapshot[] connections = connectionSources ?? Array.Empty<EditorMapConnectionSnapshot>();
        for (int i = 0; i < connections.Length; i++)
        {
            EditorMapConnectionSnapshot connection = connections[i];
            if (connection == null ||
                !enabledByIndex.TryGetValue(connection.FromRoomIndex, out PlayerMapPreflightRoom from) ||
                !enabledByIndex.TryGetValue(connection.ToRoomIndex, out PlayerMapPreflightRoom to))
                continue;

            if (connection.Ambiguous || connection.FromNodeIndex < 0 || connection.ToNodeIndex < 0)
            {
                ambiguousConnections++;
                warnings.Add("Unresolved connection " + from.Name + ":" + connection.FromNodeIndex + " -> " +
                             to.Name + ":" + connection.ToNodeIndex + ".");
                continue;
            }

            exactConnections++;
            if (!ValidRoomExit(from, connection.FromNodeIndex))
            {
                invalidEndpoints++;
                errors.Add(from.Name + ":" + connection.FromNodeIndex + " is not a valid baked RoomExit endpoint.");
            }
            if (!ValidRoomExit(to, connection.ToNodeIndex))
            {
                invalidEndpoints++;
                errors.Add(to.Name + ":" + connection.ToNodeIndex + " is not a valid baked RoomExit endpoint.");
            }

            if (!endpointClaims.Add(connection.FromRoomIndex + ":" + connection.FromNodeIndex))
            {
                duplicateClaims++;
                warnings.Add("Multiple connections claim endpoint " + from.Name + ":" + connection.FromNodeIndex + ".");
            }
            if (!endpointClaims.Add(connection.ToRoomIndex + ":" + connection.ToNodeIndex))
            {
                duplicateClaims++;
                warnings.Add("Multiple connections claim endpoint " + to.Name + ":" + connection.ToNodeIndex + ".");
            }
        }

        // Room overlap is an authored layout choice, including within a native layer.
        // It neither invalidates export nor needs a pairwise scan or warning overlay.

        if (pendingRooms > 0)
            warnings.Add(pendingRooms + " room/terrain bake(s) are still pending; a Render request will wait for them.");

        bool canRender = errors.Count == 0 && missingRooms == 0 && failedRooms == 0 && pendingRooms == 0 &&
                         enabledRooms > 0 && readyRooms == enabledRooms && outputSafe;
        return new PlayerMapPreflightSnapshot
        {
            Available = true,
            RegionName = regionName ?? string.Empty,
            EnabledRooms = enabledRooms,
            ReadyRooms = readyRooms,
            PendingRooms = pendingRooms,
            MissingRooms = missingRooms,
            FailedRooms = failedRooms,
            ExactConnections = exactConnections,
            AmbiguousConnections = ambiguousConnections,
            InvalidEndpoints = invalidEndpoints,
            DuplicateEndpointClaims = duplicateClaims,
            OverlapPairs = 0,
            OverlapRoomIndices = Array.Empty<int>(),
            Width = width,
            LayerHeight = layerHeight,
            Height = height,
            OutputSafe = outputSafe,
            CanRender = canRender,
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray()
        };
    }

    private static bool ValidRoomExit(PlayerMapPreflightRoom room, int nodeIndex)
    {
        RoomMapBakeSnapshot bake = room?.Bake;
        return bake?.Status == RoomMapBakeStatus.Ready &&
               bake.TryGetNodeAnchor(nodeIndex, out RoomMapNodeAnchorSnapshot anchor) &&
               anchor.Kind == RoomMapPixelKind.RoomExit;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    /// <summary>
    /// A malicious or corrupted region can yield a huge but finite coordinate span.
    /// Never cast that span into int before checking the PNG dimension limit:
    /// unchecked float-to-int overflow could otherwise wrap the preflight output.
    /// Return a stable over-limit sentinel rather than allocating an unsafe image.
    /// Normal map coordinates retain the historical truncate-toward-zero rounding.
    /// </summary>
    private static int SafeOutputDimension(float span)
    {
        if (!Finite(span) || span < 0f)
            return 0;
        float pixels = span / PlayerMapCoordinatePolicy.CanonPixelsPerTile;
        if (!Finite(pixels))
            return 0;
        if (pixels > MaxDimension)
            return MaxDimension + 1;
        return (int)pixels + PlayerMapCoordinatePolicy.OutputPadding * 2;
    }

}
