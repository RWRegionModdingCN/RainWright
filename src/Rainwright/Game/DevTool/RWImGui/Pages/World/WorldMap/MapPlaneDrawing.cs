using ImGuiNET;
using Num = System.Numerics;
namespace DryCycle.DevUI.DevTool.RWImGui;

internal static class MapPlaneDrawing
{
    internal static void Quad(ImDrawListPtr draw, Num.Vector2 min, Num.Vector2 max, int layer,
        MapPlaneFrame frame, Num.Vector2 origin, Num.Vector2 pan, float zoom, uint color, bool filled, float thickness = 1f)
    {
        MapDepthProjection.Corners(min,max,layer,true,frame,out var a,out var b,out var c,out var d);
        a=origin+pan+a*zoom; b=origin+pan+b*zoom; c=origin+pan+c*zoom; d=origin+pan+d*zoom;
        if(filled) draw.AddQuadFilled(a,b,c,d,color); else draw.AddQuad(a,b,c,d,color,thickness);
    }

}
