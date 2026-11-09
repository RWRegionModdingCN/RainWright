using System;
using System.Collections.Generic;
using System.Linq;
using DryCycle.DevUI.DevTool.Map;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

// Detached source geometry is supplied by the existing Player Map bake pipeline. It is never saved.
internal readonly struct CartographyTileRun
{
    internal CartographyTileRun(int x, int y, int length, int kind, bool water)
    { X = x; Y = y; Length = length; Kind = kind; Water = water; }
    internal int X { get; }
    internal int Y { get; }
    internal int Length { get; }
    internal int Kind { get; }
    internal bool Water { get; }
}

internal sealed class CartographyRoomSource
{
    internal string Name = string.Empty;
    internal int Layer;
    internal float X, Y;
    internal int Width, Height;
    internal bool Ready;
    internal bool Disabled = false;
    internal string Error = string.Empty;
    internal CartographyTileRun[] Runs = Array.Empty<CartographyTileRun>();
    internal Dictionary<int, CartographyRect> Ports = new();
    internal string Subregion = "", Tags = "", Settings = "";
    internal byte[] Terrain = Array.Empty<byte>();

    // Authored continuous terrain is detached semantic geometry shared with World Map. Cartography
    // paints these fills with the exact same Solid/Structure palette rules as ordinary room tiles;
    // only the silhouette is curved.
    internal EditorMapRectSnapshot[] CurvedTerrainFills = Array.Empty<EditorMapRectSnapshot>();
    internal EditorMapPolylineSnapshot[] CurvedTerrainCurves = Array.Empty<EditorMapPolylineSnapshot>();

    internal List<CartographyPoint[]> Shortcuts = new();
}

internal sealed class CartographyConnectionSource
{
    internal string From = string.Empty, To = string.Empty;
    internal int FromPort = -1, ToPort = -1;
    internal bool Ambiguous = false;
}

internal sealed class CartographySource
{
    internal readonly Dictionary<string, CartographyRoomSource> Rooms = new(StringComparer.OrdinalIgnoreCase);
    internal readonly List<CartographyConnectionSource> Connections = new();
    internal readonly List<CartographyItem> Decorations = new();
    internal readonly List<CartographyPalette> Palettes = new();

    internal CartographyDocument CreateDocument(string identity, string region)
    {
        CartographyDocument document = new() { Identity = identity, Region = region, Title = region };
        document.Layers.Add(new CartographyLayer { Id = "links", Name = "Connections / 连线" });
        for (int i = 0; i < 3; i++) document.Layers.Add(new CartographyLayer { Id = "rooms" + i, Name = "Rooms / 房间 L" + i });
        document.Layers.Add(new CartographyLayer { Id = "notes", Name = "Annotations / 标注" });
        document.Palettes.AddRange(Palettes.Select(p => p.Clone()));
        AddMissingRooms(document);
        return document;
    }

    internal void AddMissingRooms(CartographyDocument document)
    {
        HashSet<string> known = new(document.Items.Where(item => item.Kind == CartographyItemKind.Room).Select(item => item.Room), StringComparer.OrdinalIgnoreCase);
        foreach (CartographyRoomSource room in Rooms.Values.OrderBy(room => room.Name, StringComparer.Ordinal))
        {
            if (known.Contains(room.Name)) continue;
            string layer = "rooms" + room.Layer;
            if (document.Layer(layer) == null) layer = document.Layers.First(candidate => candidate.Id != "links").Id;
            document.Items.Add(new CartographyItem { Id = "room:" + room.Name, Kind = CartographyItemKind.Room,
                LayerId = layer, Room = room.Name, X = room.X, Y = room.Y, Visible = !room.Disabled,
                Appearance = new CartographyAppearance { Subregion = room.Subregion } });
        }
        foreach (CartographyConnectionSource link in Connections)
        {
            string id = ConnectionId(link);
            if (document.Items.Any(i => i.Id == id)) continue;
            document.Items.Add(new CartographyItem { Id = id, Kind = CartographyItemKind.Connection, LayerId = "links", Color = document.Connections,
                Appearance = new CartographyAppearance { From = link.From, To = link.To, FromPort = link.FromPort, ToPort = link.ToPort, Dashed = true, Shade = true, Outline = 1 } });
        }
        foreach (CartographyItem decoration in Decorations)
            if (!document.Items.Any(i => i.Id == decoration.Id) && (decoration.Appearance.ParentId.Length == 0 || document.Items.Any(i => i.Id == decoration.Appearance.ParentId))) document.Items.Add(decoration.Clone());
    }
    internal static string ConnectionId(CartographyConnectionSource c) => "link:" + c.From + ":" + c.FromPort + ":" + c.To + ":" + c.ToPort;
}
