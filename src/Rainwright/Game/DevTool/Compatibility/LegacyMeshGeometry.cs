using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Compatibility;

// Mirror only the boundary of an original triangle mesh, not its shader/business effect.
internal static class LegacyMeshGeometry
{
    internal static EditorObjectLineSegmentSnapshot[] Capture(float[] xy, int[] triangles,
        float x, float y, float scaleX, float scaleY, float rotation, float red, float green, float blue, float alpha)
    {
        if (xy == null || triangles == null || xy.Length % 2 != 0 || triangles.Length % 3 != 0 ||
            xy.Length > 8192 || triangles.Length > 12288) throw new InvalidOperationException("Invalid or oversized original triangle mesh.");
        foreach (float value in xy) if (!Finite(value)) throw new InvalidOperationException("Original mesh contains non-finite vertices.");
        foreach (float value in new[] { x, y, scaleX, scaleY, rotation, red, green, blue, alpha })
            if (!Finite(value)) throw new InvalidOperationException("Original mesh transform/color contains non-finite components.");
        var edges = new Dictionary<(int A, int B), int>();
        void Edge(int a, int b)
        {
            if (a < 0 || b < 0 || a >= xy.Length / 2 || b >= xy.Length / 2) throw new InvalidOperationException("Original mesh triangle references a missing vertex.");
            if (a == b) return;
            var key = a < b ? (a, b) : (b, a);
            edges.TryGetValue(key, out int count); edges[key] = count + 1;
        }
        for (int i = 0; i < triangles.Length; i += 3)
        {
            Edge(triangles[i], triangles[i + 1]); Edge(triangles[i + 1], triangles[i + 2]); Edge(triangles[i + 2], triangles[i]);
        }
        double radians = rotation * Math.PI / 180, cos = Math.Cos(radians), sin = Math.Sin(radians);
        (float X, float Y) Point(int index)
        {
            double a = (double)xy[index * 2] * scaleX, b = (double)xy[index * 2 + 1] * scaleY;
            float px = (float)(x + a * cos + b * sin), py = (float)(y - a * sin + b * cos);
            if (!Finite(px) || !Finite(py)) throw new InvalidOperationException("Original mesh projection exceeds finite coordinates.");
            return (px, py);
        }
        var result = new List<EditorObjectLineSegmentSnapshot>();
        foreach (var edge in edges)
        {
            if (edge.Value != 1) continue;
            var a = Point(edge.Key.A); var b = Point(edge.Key.B);
            result.Add(new() { X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y, HasColor = true,
                Red = Clamp(red), Green = Clamp(green), Blue = Clamp(blue), Alpha = Clamp(alpha) });
        }
        return result.ToArray();
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static float Clamp(float value) => Math.Max(0, Math.Min(1, value));
}
