using System;
using System.Collections.Generic;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

// The rendering order is a pure authoring policy: geometry and raster
// objects are never referenced by Rainwright's layer-plan algorithm.
internal enum CartographyRenderPass { UnderRooms, Content, Guides }

internal readonly struct CartographyRenderNode<TNode>
{
    internal readonly TNode Node;
    internal readonly string LayerId;
    internal readonly bool Room, Route, UnderRooms, Content, Guides;

    internal CartographyRenderNode(TNode node, string layerId, bool room, bool route,
        bool underRooms, bool content, bool guides)
    {
        Node = node; LayerId = layerId;
        Room = room; Route = route; UnderRooms = underRooms;
        Content = content; Guides = guides;
    }

    internal bool AppearsIn(CartographyRenderPass pass) => Route ||
        (pass == CartographyRenderPass.UnderRooms ? UnderRooms :
         pass == CartographyRenderPass.Content ? Content : Guides);
}

internal sealed class CartographyRenderLayerPlan<TNode>
{
    internal string LayerId, Name;
    internal CartographyRenderPass Pass;
    internal TNode[] Nodes;
}

internal static class CartographyRenderLayerPlanner
{
    internal static CartographyRenderLayerPlan<TNode>[] Build<TNode>(
        CartographyDocument document, IEnumerable<CartographyRenderNode<TNode>> nodes)
    {
        if (document == null) throw new ArgumentNullException(nameof(document));
        if (nodes == null) throw new ArgumentNullException(nameof(nodes));
        var byLayer = nodes.ToLookup(n => n.LayerId);
        var layers = document.Layers
            .Where(l => l.Visible && l.Opacity > 0 && byLayer.Contains(l.Id))
            .ToList();

        // Legacy projects put links before the room layers. Presentation
        // moves those links after the last room, never mutating the XML order.
        var links = layers.Find(l => l.Id == "links");
        var lastRoom = layers.LastOrDefault(l => byLayer[l.Id].Any(n => n.Room));
        if (links != null && lastRoom != null && layers.IndexOf(links) < layers.IndexOf(lastRoom))
        {
            layers.Remove(links);
            layers.Insert(layers.IndexOf(lastRoom) + 1, links);
        }

        var result = new List<CartographyRenderLayerPlan<TNode>>();
        foreach (var pass in new[]
        {
            CartographyRenderPass.UnderRooms,
            CartographyRenderPass.Content,
            CartographyRenderPass.Guides
        })
            foreach (CartographyLayer layer in layers)
            {
                TNode[] batch = byLayer[layer.Id]
                    .Where(n => n.AppearsIn(pass))
                    .Select(n => n.Node)
                    .ToArray();
                if (batch.Length == 0) continue;
                result.Add(new CartographyRenderLayerPlan<TNode>
                {
                    LayerId = layer.Id,
                    Pass = pass,
                    Name = layer.Name + (pass == CartographyRenderPass.UnderRooms ? " / Borders" : ""),
                    Nodes = batch
                });
            }
        return result.ToArray();
    }
}
