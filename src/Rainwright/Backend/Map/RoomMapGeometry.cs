using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

internal readonly struct RoomMapPixel
{
    internal RoomMapPixel(RoomMapPixelKind kind, bool water)
    {
        Kind = kind;
        Water = water;
    }

    internal RoomMapPixelKind Kind { get; }
    internal bool Water { get; }
}

internal sealed class RoomMapBake
{
    internal int RoomIndex;
    internal string RoomName = string.Empty;
    internal int Width;
    internal int Height;
    internal RoomMapPixel[] Pixels = Array.Empty<RoomMapPixel>();
    internal RoomMapPreviewRun[] Runs = Array.Empty<RoomMapPreviewRun>();
    internal RoomMapNodeAnchorSnapshot[] NodeAnchors = Array.Empty<RoomMapNodeAnchorSnapshot>();
    internal readonly Dictionary<int, RoomMapNodeAnchorSnapshot> NodeAnchorByIndex = new();
    internal string SourcePath = string.Empty;
    internal long SourceLength;
    internal DateTime SourceWriteTimeUtc;

    internal bool TryGetNodeAnchor(int nodeIndex, out RoomMapNodeAnchorSnapshot anchor) =>
        NodeAnchorByIndex.TryGetValue(nodeIndex, out anchor);
}

internal sealed class RoomMapSource
{
    internal int Width;
    internal int Height;
    internal SourceTile[] Tiles = Array.Empty<SourceTile>();

    internal ref SourceTile Tile(int x, int y) => ref Tiles[y * Width + x];
    internal bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;
}

internal struct SourceTile
{
    internal byte Terrain;
    internal bool VerticalBeam;
    internal bool HorizontalBeam;
    internal bool WallBehind;
    internal byte Shortcut;
    internal bool Water;
}

internal static class RoomMapTextDecoder
{
    internal static RoomMapSource Parse(string[] lines)
    {
        if (lines == null || lines.Length < 12) throw new InvalidDataException("Room source has fewer than 12 lines.");
            string[] header = lines[1].Split('|');
            if (header.Length < 2)
            {
                throw new InvalidDataException("Room size/water header is malformed.");
            }

            string[] size = header[0].Split('*');
            if (size.Length < 2 ||
                !int.TryParse(size[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width) ||
                !int.TryParse(size[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height) ||
                width <= 0 || height <= 0)
            {
                throw new InvalidDataException("Room dimensions are invalid.");
            }

            int waterLevel = -1;
            int.TryParse(header[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out waterLevel);
            if ((long)width * height > 4000000) throw new InvalidDataException("Room exceeds four million tiles.");
            SourceTile[] tiles = new SourceTile[checked(width * height)];
            string encoded = lines[11];
            int cursor = 0;
            for (int ordinal = 0; ordinal < width * height; ordinal++)
            {
                int x = ordinal / height;
                int y = height - 1 - ordinal % height;
                SourceTile tile = new()
                {
                    Terrain = 0,
                    Water = waterLevel >= 0 && y <= waterLevel
                };

                // RW files terminate tiles with '|'. Preserve the existing decoder's
                // treatment of an unterminated final token as missing, including short files.
                if (cursor >= 0)
                {
                    int end = encoded.IndexOf('|', cursor);
                    if (end < 0) cursor = -1;
                    else
                    {
                        DecodeTile(encoded, cursor, end, ref tile);
                        cursor = end + 1;
                    }
                }
                tiles[y * width + x] = tile;
            }

            return new RoomMapSource { Width = width, Height = height, Tiles = tiles };
    }

    private static void DecodeTile(string encoded, int start, int end, ref SourceTile tile)
    {
        int comma = encoded.IndexOf(',', start, end - start);
        int terrainEnd = comma < 0 ? end : comma;
        if (terrainEnd - start == 1 && encoded[start] >= '0' && encoded[start] <= '9')
            tile.Terrain = (byte)(encoded[start] - '0');
        else if (terrainEnd > start && byte.TryParse(encoded.Substring(start, terrainEnd - start),
                     NumberStyles.Integer, CultureInfo.InvariantCulture, out byte terrain))
            tile.Terrain = terrain;

        while (comma >= 0)
        {
            start = comma + 1;
            comma = encoded.IndexOf(',', start, end - start);
            int tokenEnd = comma < 0 ? end : comma;
            // Feature tokens are exact strings in RW; whitespace, padded numbers and
            // unknown extensions remain ignored, as with the original switch on Split().
            if (tokenEnd - start == 1)
            {
                switch (encoded[start])
                {
                    case '1': tile.VerticalBeam = true; break;
                    case '2': tile.HorizontalBeam = true; break;
                    case '3': if (tile.Shortcut < 1) tile.Shortcut = 1; break;
                    case '4': tile.Shortcut = 2; break;
                    case '5': tile.Shortcut = 3; break;
                    case '9': tile.Shortcut = 4; break;
                    case '6': tile.WallBehind = true; break;
                }
            }
            else if (tokenEnd - start == 2 && encoded[start] == '1' && encoded[start + 1] == '2')
                tile.Shortcut = 5;
        }
    }

}

internal static class RoomMapSemanticCompiler
{
    internal static List<(float X, float Y)[]> TraceInternal(RoomMapSource source)
    {
        List<(float X, float Y)[]> paths = new(); HashSet<long> seen = new();
        for (int sy = 0; sy < source.Height; sy++) for (int sx = 0; sx < source.Width; sx++)
        {
            if (source.Tile(sx,sy).Terrain != ShortcutEntrance || seen.Contains(TileKey(sx,sy))) continue;
            List<(float X, float Y)> points = new() { (sx + .5f, sy + .5f) };
            int x = sx, y = sy, lx = sx, ly = sy;
            HashSet<long> trace = new() { TileKey(x,y) };
            for (int step = 0; step < ShortcutGuard; step++)
            {
                int oldX = x, oldY = y;
                if (!TryNextShortcutPosition(source, ref x, ref y, lx, ly) || !trace.Add(TileKey(x,y))) break;
                lx = oldX; ly = oldY; points.Add((x + .5f,y + .5f));
                if (source.Tile(x,y).Terrain == ShortcutEntrance)
                { paths.Add(points.ToArray()); seen.Add(TileKey(x,y)); break; }
                if (source.Tile(x,y).Shortcut > 1) break;
            }
        }
        return paths;
    }
    private const byte Solid = 1;
    private const byte Slope = 2;
    private const byte Floor = 3;
    private const byte ShortcutEntrance = 4;
    private const int ShortcutGuard = 1000;
    // Immutable direction order shared by CPU decoders; no arrays per pipe step.
    private static readonly int[] DirectionX = { -1, 0, 1, 0 };
    private static readonly int[] DirectionY = { 0, -1, 0, 1 };

    private readonly struct ShortcutResolution
    {
        internal ShortcutResolution(RoomMapPixelKind kind, int nodeIndex, int terminalX, int terminalY)
        {
            Kind = kind;
            NodeIndex = nodeIndex;
            TerminalX = terminalX;
            TerminalY = terminalY;
        }

        internal RoomMapPixelKind Kind { get; }
        internal int NodeIndex { get; }
        internal int TerminalX { get; }
        internal int TerminalY { get; }
    }

    internal static RoomMapBake Compile(int roomIndex, string roomName, RoomMapSource source, string sourcePath)
    {
        Dictionary<long, int> nodeByTerminal = BuildNodeIndex(source);
        Dictionary<int, RoomMapNodeAnchorSnapshot> anchors = new();
        RoomMapPixel[] pixels = new RoomMapPixel[source.Width * source.Height];

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                ref SourceTile tile = ref source.Tile(x, y);
                RoomMapPixelKind kind;
                if (tile.Terrain == ShortcutEntrance)
                {
                    ShortcutResolution shortcut = ResolveShortcut(source, x, y, nodeByTerminal);
                    kind = shortcut.Kind;
                    if (shortcut.NodeIndex >= 0 &&
                        (kind == RoomMapPixelKind.RoomExit ||
                         kind == RoomMapPixelKind.CreatureHole ||
                         kind == RoomMapPixelKind.RegionTransport) &&
                        !anchors.ContainsKey(shortcut.NodeIndex))
                    {
                        anchors.Add(shortcut.NodeIndex, new RoomMapNodeAnchorSnapshot(
                            shortcut.NodeIndex,
                            x + 0.5f,
                            y + 0.5f,
                            shortcut.TerminalX,
                            shortcut.TerminalY,
                            kind));
                    }
                }
                else if (tile.Terrain == Solid)
                {
                    kind = RoomMapPixelKind.Solid;
                }
                else if (tile.Terrain == Floor || tile.Terrain == Slope || tile.VerticalBeam || tile.HorizontalBeam)
                {
                    kind = RoomMapPixelKind.Structure;
                }
                else if (tile.WallBehind)
                {
                    kind = RoomMapPixelKind.BackWall;
                }
                else
                {
                    kind = RoomMapPixelKind.Air;
                }
                pixels[y * source.Width + x] = new RoomMapPixel(kind, tile.Water);
            }
        }

        List<RoomMapNodeAnchorSnapshot> orderedAnchors = new(anchors.Values);
        orderedAnchors.Sort((a, b) => a.NodeIndex.CompareTo(b.NodeIndex));

        FileInfo info = new(sourcePath);
        RoomMapBake bake = new()
        {
            RoomIndex = roomIndex,
            RoomName = roomName ?? string.Empty,
            Width = source.Width,
            Height = source.Height,
            Pixels = pixels,
            NodeAnchors = orderedAnchors.ToArray(),
            SourcePath = sourcePath,
            SourceLength = info.Exists ? info.Length : 0L,
            SourceWriteTimeUtc = info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue
        };
        for (int i = 0; i < bake.NodeAnchors.Length; i++)
            bake.NodeAnchorByIndex[bake.NodeAnchors[i].NodeIndex] = bake.NodeAnchors[i];
        bake.Runs = BuildRuns(bake);
        return bake;
    }

    /// <summary>
    /// Reproduces ShortcutMapper.nodesIndex ordering without constructing a Room. This exact ordering
    /// is what lets two or more RoomExit pipes between the same room pair remain distinct by node id.
    /// </summary>
    private static Dictionary<long, int> BuildNodeIndex(RoomMapSource source)
    {
        Dictionary<long, int> result = new();
        int nodeIndex = 0;
        int[] terminalTypes = { 2, 3, 5 };
        for (int t = 0; t < terminalTypes.Length; t++)
        {
            int type = terminalTypes[t];
            for (int y = source.Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < source.Width; x++)
                {
                    if (source.Tile(x, y).Shortcut != type) continue;
                    result[TileKey(x, y)] = nodeIndex++;
                }
            }
        }
        return result;
    }

    private static ShortcutResolution ResolveShortcut(
        RoomMapSource source,
        int startX,
        int startY,
        Dictionary<long, int> nodeByTerminal)
    {
        int x = startX;
        int y = startY;
        int lastX = startX;
        int lastY = startY;
        int maxSteps = Math.Min(ShortcutGuard, Math.Max(16, source.Width * source.Height * 4));

        for (int step = 0; step < maxSteps; step++)
        {
            if (step > 0)
            {
                ref SourceTile current = ref source.Tile(x, y);
                if (current.Terrain == ShortcutEntrance)
                    return new ShortcutResolution(RoomMapPixelKind.NormalShortcut, -1, x, y);

                RoomMapPixelKind terminalKind = current.Shortcut switch
                {
                    2 => RoomMapPixelKind.RoomExit,
                    3 => RoomMapPixelKind.CreatureHole,
                    4 => RoomMapPixelKind.NpcTransport,
                    5 => RoomMapPixelKind.RegionTransport,
                    _ => RoomMapPixelKind.Air
                };
                if (terminalKind != RoomMapPixelKind.Air)
                {
                    int nodeIndex = nodeByTerminal.TryGetValue(TileKey(x, y), out int index) ? index : -1;
                    return new ShortcutResolution(terminalKind, nodeIndex, x, y);
                }
            }

            int previousX = x;
            int previousY = y;
            if (!TryNextShortcutPosition(source, ref x, ref y, lastX, lastY))
                break;
            lastX = previousX;
            lastY = previousY;
            if (x == startX && y == startY && step > 1) break;
        }
        return new ShortcutResolution(RoomMapPixelKind.UnknownShortcut, -1, x, y);
    }

    private static bool TryNextShortcutPosition(RoomMapSource source, ref int x, ref int y, int lastX, int lastY)
    {
        int dx = x - lastX;
        int dy = y - lastY;
        int nx = x + dx;
        int ny = y + dy;
        if ((dx != 0 || dy != 0) && HasShortcut(source, nx, ny))
        {
            x = nx;
            y = ny;
            return true;
        }

        // Match RWCustom.Custom.fourDirections used by ShortcutHandler exactly:
        // left, down, right, up. Branch/malformed tunnels therefore resolve identically to vanilla.
        int originalX = x;
        int originalY = y;
        for (int i = 0; i < 4; i++)
        {
            if (DirectionX[i] == -dx && DirectionY[i] == -dy) continue;
            nx = x + DirectionX[i];
            ny = y + DirectionY[i];
            if (!HasShortcut(source, nx, ny)) continue;
            x = nx;
            y = ny;
            break;
        }

        if (x == lastX && y == lastY)
        {
            x -= dx;
            y -= dy;
        }
        if (x == originalX && y == originalY && dx == 0 && dy == 0)
            return false;
        return source.Inside(x, y);
    }

    private static bool HasShortcut(RoomMapSource source, int x, int y) =>
        source.Inside(x, y) && source.Tile(x, y).Shortcut != 0;

    private static long TileKey(int x, int y) => ((long)(uint)x << 32) | (uint)y;

    private static RoomMapPreviewRun[] BuildRuns(RoomMapBake bake)
    {
        List<RoomMapPreviewRun> runs = new();
        for (int y = 0; y < bake.Height; y++)
        {
            int x = 0;
            while (x < bake.Width)
            {
                RoomMapPixel pixel = bake.Pixels[y * bake.Width + x];
                int end = x + 1;
                while (end < bake.Width)
                {
                    RoomMapPixel next = bake.Pixels[y * bake.Width + end];
                    if (next.Kind != pixel.Kind || next.Water != pixel.Water) break;
                    end++;
                }
                runs.Add(new RoomMapPreviewRun(x, y, end - x, pixel.Kind, pixel.Water));
                x = end;
            }
        }
        return runs.ToArray();
    }
}

