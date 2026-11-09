using System.Collections.Generic;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

// Scene/raster adapter only. Rainwright plans layer order, room-connection
// stacking, render passes and which nodes belong to each immutable batch.
internal sealed class CartographyRenderLayer
{
    internal string LayerId, Name;
    internal CartographyRenderPass Pass;
    internal CartographySceneNode[] Nodes;
    internal bool Accepts(CartographyPrimitive shape) => Pass == CartographyRenderPass.Guides
        ? shape.GuideOnly : !shape.GuideOnly && shape.UnderRooms == (Pass == CartographyRenderPass.UnderRooms);
}

internal static class CartographyComposition
{
    internal static CartographyRenderLayer[] Build(CartographyDocument document,
        IEnumerable<CartographySceneNode> nodes)
    {
        var descriptors = nodes.Select(n =>
        {
            bool underRooms = false, content = false, guides = false;
            foreach (CartographyPrimitive shape in n.Primitives)
            {
                if (shape.GuideOnly) guides = true;
                else if (shape.UnderRooms) underRooms = true;
                else content = true;
            }
            return new CartographyRenderNode<CartographySceneNode>(
                n, n.LayerId, n.Room, n.FromId != null,
                underRooms, content, guides);
        });
        return CartographyRenderLayerPlanner.Build(document, descriptors)
            .Select(plan => new CartographyRenderLayer
            {
                LayerId = plan.LayerId, Name = plan.Name,
                Pass = plan.Pass, Nodes = plan.Nodes
            }).ToArray();
    }
}
