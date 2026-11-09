using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Map.Cartography;

namespace DryCycle.DevUI.DevTool.Map.Cartography
{
    // Minimal scene source doubles. They expose the same adapter surface,
    // but game-dependent scene code is NOT compiled into this net10 test.
    internal sealed class CartographySource
    {
        internal int Added;
        internal void AddMissingRooms(CartographyDocument document)
        {
            Added++;
            document.Items.Add(new CartographyItem
            {
                Id = "room:SU_A03", Kind = CartographyItemKind.Room,
                Room = "SU_A03", LayerId = "rooms", X = 120
            });
        }
    }

    internal sealed class CartographyRoomSource { }

    internal sealed class CartographyPrimitive
    {
        internal bool GuideOnly;
        internal bool UnderRooms;
    }

    internal sealed class CartographySceneNode
    {
        internal string LayerId = string.Empty;
        internal string FromId;
        internal bool Room;
        internal CartographyPrimitive[] Primitives = Array.Empty<CartographyPrimitive>();
        internal CartographyPoint[] Points = Array.Empty<CartographyPoint>();
    }

    internal static class CartographySceneBuilder
    {
        internal static int BoundsCalls;
        internal static int PortCalls;
        internal static CartographyRect Bounds(CartographyItem item, CartographySource source)
        {
            BoundsCalls++;
            return new CartographyRect(item.X - 10, item.Y - 10, 20, 20);
        }

        internal static bool Port(CartographyItem item, int port,
            CartographySource source, out float x, out float y)
        {
            PortCalls++;
            x = item.X + 5; y = item.Y + 6;
            return true;
        }
    }
}

internal static class Program
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var rainwright = typeof(CartographyDocument).Assembly;
        Check(rainwright.GetName().Name == "RainWright", "Production document was not built into Rainwright.");
        Check(typeof(CartographyCommand).Assembly == rainwright,
            "Host adapter must consume Rainwright command identity.");
        var source = new CartographySource();
        var original = new CartographyDocument { Identity = "world|SU", Region = "SU" };
        original.Layers.Add(new CartographyLayer { Id = "links", Name = "Connections" });
        original.Layers.Add(new CartographyLayer { Id = "rooms", Name = "Rooms" });
        original.Layers.Add(new CartographyLayer { Id = "notes", Name = "Notes" });
        original.Items.Add(new CartographyItem
        {
            Id = "room:SU_A01", Kind = CartographyItemKind.Room, Room = "SU_A01",
            LayerId = "rooms", X = 10, Y = 15
        });
        original.Items.Add(new CartographyItem
        {
            Id = "room:SU_A02", Kind = CartographyItemKind.Room, Room = "SU_A02",
            LayerId = "rooms", X = 90, Y = 60
        });
        original.Items.Add(new CartographyItem
        {
            Id = "link:A-B", Kind = CartographyItemKind.Connection, LayerId = "links",
            Appearance = new CartographyAppearance
            {
                From = "SU_A01", To = "SU_A02", FromPort = 0, ToPort = 1,
                Route = CartographyRouteMode.Manual
            },
            Points = new List<CartographyPoint> { new() { X = 5, Y = 5 } }
        });
        original.Items.Add(new CartographyItem
        {
            Id = "note", Kind = CartographyItemKind.Text,
            LayerId = "notes", Text = "Old", X = 25, Y = 35
        });
        original.Validate();

        var moved = CartographyEditing.Apply(original, source,
            new CartographyCommand
            {
                Kind = CartographyCommandKind.Move,
                Ids = new[] { "note" }, X = 12, Y = -7
            });
        Check(moved.Items[3].X == 37 && moved.Items[3].Y == 28 &&
              original.Items[3].X == 25 && original.Items[3].Y == 35,
            "Ordinary edit lost the delta or mutated its previous revision.");
        Check(CartographySceneBuilder.BoundsCalls == 0 &&
              CartographySceneBuilder.PortCalls == 0 && source.Added == 0,
            "Ordinary commands must not touch scene callbacks.");
        Check(!CartographyEditing.SameItem(moved.Items[3], original.Items[3]) &&
              CartographyEditing.SameLayer(moved.Layers[0], original.Layers[0]),
            "Adapter item/layer identity forwarding broke.");

        var aligned = CartographyEditing.Apply(moved, source,
            new CartographyCommand
            {
                Kind = CartographyCommandKind.Align,
                Ids = new[] { "room:SU_A01", "room:SU_A02" },
                Integer = (int)CartographyAlignment.Left
            });
        Check(CartographySceneBuilder.BoundsCalls == 2 &&
              aligned.Items[0].X == aligned.Items[1].X &&
              moved.Items[1].X == 90,
            "Host bounds callback must drive geometric alignment without changing the previous revision.");

        var portsAligned = CartographyEditing.Apply(aligned, source,
            new CartographyCommand
            {
                Kind = CartographyCommandKind.AlignPorts, Ids = new[] { "link:A-B" },
                Integer = 0
            });
        Check(CartographySceneBuilder.PortCalls == 2 &&
              portsAligned.Items[1].Y == portsAligned.Items[0].Y &&
              portsAligned.Items[2].Points.Count == 0 &&
              portsAligned.Items[2].Appearance.Route == CartographyRouteMode.Straight,
            "Port geometry callback failed to snap rooms and reset the authored link.");

        var recovered = CartographyEditing.Apply(portsAligned, source,
            new CartographyCommand { Kind = CartographyCommandKind.AddMissingRooms });
        Check(source.Added == 1 && recovered.Items.Count == 5 &&
              portsAligned.Items.Count == 4,
            "Host room source callback must only modify the new author revision.");
        var style = recovered.Clone(); style.SolidTerrain = true;
        var styled = CartographyEditing.Apply(recovered, source,
            new CartographyCommand { Kind = CartographyCommandKind.Style, Style = style });
        Check(styled.SolidTerrain && !recovered.SolidTerrain,
            "SolidTerrain style changes must survive the host-to-core boundary.");

        // The public-to-frontend bridge receives concrete scene nodes, but
        // delegates all handle/segment math to the Rainwright-owned core.
        var points = new[]
        {
            new CartographyPoint { X = 0, Y = 0 },
            new CartographyPoint { X = 8, Y = 0 },
            new CartographyPoint { X = 12, Y = 4 }
        };
        int nearestIndex = CartographyRouteEditing.NearestSegment(points, 4, 1, 3, out var closest);
        Check(nearestIndex == 0 && closest.X == 4 && closest.Y == 0,
            "Route adapter failed segment projection against the real Rainwright core.");
        Check(CartographyRouteEditing.NearestHandle(points, 8, 0, 2) == 0 &&
              CartographyRouteEditing.NearestHandle(points, 0, 0, 2) == -1,
            "The route adapter exposed immutable endpoint ports as handles.");
        var straightLink = original.Items[2].Clone();
        straightLink.Appearance.Route = CartographyRouteMode.Straight;
        var manualLink = CartographyRouteEditing.Manual(straightLink,
            new CartographySceneNode { Points = points });
        Check(manualLink.Appearance.Route == CartographyRouteMode.Manual &&
              manualLink.Points.Count == 1 && manualLink.Points[0].X == 8 &&
              straightLink.Points[0].X == 5 && original.Items[2].Points.Count == 1,
            "Manual route adapter must capture internal handles without changing the previous revision.");
        var keptManual = CartographyRouteEditing.Manual(original.Items[2],
            new CartographySceneNode { Points = points });
        Check(keptManual.Points.Count == 1 && keptManual.Points[0].X == 5,
            "Existing manual routes must preserve explicit authored handles.");
        var dragged = CartographyRouteEditing.Drag(3, 12, new CartographyPoint { X = 2, Y = 2 }, true);
        Check(dragged.X == 2 && dragged.Y == 12,
            "Route drag adapter did not honor constrained drag gesture.");
        // Use the real production host composition adapter and the actual
        // Rainwright planner, not a synthetic copy of the core method.
        var renderDocument = new CartographyDocument { Identity = "render|SU", Region = "SU" };
        renderDocument.Layers.Add(new CartographyLayer { Id = "links", Name = "Connections" });
        renderDocument.Layers.Add(new CartographyLayer { Id = "rooms", Name = "Rooms" });
        renderDocument.Layers.Add(new CartographyLayer { Id = "hidden", Name = "Hidden", Visible = false });
        var roomBody = new CartographySceneNode
        {
            LayerId = "rooms", Room = true,
            Primitives = new[] { new CartographyPrimitive() }
        };
        var border = new CartographySceneNode
        {
            LayerId = "rooms",
            Primitives = new[] { new CartographyPrimitive { UnderRooms = true } }
        };
        var guide = new CartographySceneNode
        {
            LayerId = "rooms",
            Primitives = new[] { new CartographyPrimitive { GuideOnly = true } }
        };
        var connection = new CartographySceneNode
        {
            LayerId = "links", FromId = "SU_A01",
            Primitives = Array.Empty<CartographyPrimitive>()
        };
        var hiddenNode = new CartographySceneNode
        {
            LayerId = "hidden", Primitives = new[] { new CartographyPrimitive() }
        };
        var renderLayers = CartographyComposition.Build(renderDocument,
            new[] { connection, roomBody, border, guide, hiddenNode });
        Check(renderLayers.Length == 6 &&
              renderLayers[0].Pass == CartographyRenderPass.UnderRooms &&
              renderLayers[0].LayerId == "rooms" &&
              ReferenceEquals(renderLayers[0].Nodes[0], border) &&
              renderLayers[1].LayerId == "links" &&
              renderLayers[2].Pass == CartographyRenderPass.Content &&
              renderLayers[2].LayerId == "rooms" &&
              ReferenceEquals(renderLayers[2].Nodes[0], roomBody) &&
              renderLayers[3].LayerId == "links" &&
              renderLayers[4].Pass == CartographyRenderPass.Guides &&
              ReferenceEquals(renderLayers[4].Nodes[0], guide) &&
              renderLayers[5].LayerId == "links" &&
              renderDocument.Layers[0].Id == "links",
            "Production composition bridge changed room/link stacking, pass order, source nodes or author layer order.");
        Check(renderLayers[0].Accepts(border.Primitives[0]) &&
              !renderLayers[0].Accepts(roomBody.Primitives[0]),
            "Host composition primitive filter no longer matches Rainwright render passes.");

        // This is the real host-side retained-cache adapter. Verify the
        // generic Rainwright owner through its original TryGet/Store/Prune API.
        var signedZeroA = original.Items[0].Clone(); var signedZeroB = signedZeroA.Clone();
        signedZeroA.Appearance.Alignment = 0f; signedZeroB.Appearance.Alignment = float.NegativeZero;
        Check(CartographyEditing.SameItem(signedZeroA, signedZeroB) ==
              (CartographyRecord.Key(signedZeroA.Appearance) == CartographyRecord.Key(signedZeroB.Appearance)),
            "Portable record comparison changed the serializer's signed-zero semantics.");
        var sceneCache = new CartographySceneCache();
        var roomSource = new CartographyRoomSource();
        var roomItem = original.Items[0];
        var roomLayer = original.Layer("rooms");
        var cachedSceneNode = roomBody;
        sceneCache.Store(original, roomItem, roomLayer, roomSource,
            cachedSceneNode, "worker-error");
        Check(sceneCache.TryGet(original, roomItem, roomLayer, roomSource,
                  out var retainedNode, out var retainedError) &&
              ReferenceEquals(retainedNode, cachedSceneNode) && retainedError == "worker-error",
            "Rainwright-backed host scene cache lost its node or diagnostics.");
        var roomVisibilityChanged = original.Clone();
        roomVisibilityChanged.ShowRoomNames = !original.ShowRoomNames;
        Check(!sceneCache.TryGet(roomVisibilityChanged, roomItem, roomLayer,
                  roomSource, out _, out _),
            "Room display changes did not invalidate the retained scene bridge.");
        Check(!sceneCache.TryGet(original, roomItem, roomLayer,
                  new CartographyRoomSource(), out _, out _),
            "A newly decoded room source must invalidate a stale scene node.");
        original.Items.RemoveAt(0);
        sceneCache.Prune(original);
        Check(!sceneCache.TryGet(original, roomItem, roomLayer, roomSource,
                  out _, out _),
            "Removed author nodes must be pruned from the scene cache.");

        Console.WriteLine("PASS: Cartography edit, route, composition and scene cache adapters delegate to Rainwright.dll.");
    }
}
