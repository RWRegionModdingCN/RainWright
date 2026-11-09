using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Compatibility;

// Detached sprite geometry. VectorCircle encodes stroke width in sprite alpha;
// ordinary atlas sprites encode opacity there. No object/mod names are involved.
internal static class LegacySpriteGeometry
{
    internal static bool Supports(string element, string shader) =>
        string.Equals(element, "pixel", StringComparison.OrdinalIgnoreCase) ||
        IsVectorCircle(shader) || IsAtlasCircle(element);

    internal static bool IsVectorCircle(string shader) =>
        (shader ?? string.Empty).IndexOf("VectorCircle", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsAtlasCircle(string element)
    {
        if (string.IsNullOrEmpty(element) || !element.StartsWith("Circle", StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(element.Substring(6), out int diameter) && diameter > 0;
    }

    internal static EditorObjectLineSegmentSnapshot[] Capture(string element, string shader,
        float sourceWidth, float sourceHeight, float x, float y, float scaleX, float scaleY,
        float rotation, float anchorX, float anchorY, float red, float green, float blue, float alpha)
    {
        if (!Supports(element, shader) || !Finite(sourceWidth) || !Finite(sourceHeight) ||
            !Finite(x) || !Finite(y) || !Finite(scaleX) || !Finite(scaleY) || !Finite(rotation) ||
            !Finite(anchorX) || !Finite(anchorY) || !Finite(red) || !Finite(green) || !Finite(blue) || !Finite(alpha))
            return Array.Empty<EditorObjectLineSegmentSnapshot>();
        bool vector = IsVectorCircle(shader);
        bool pixel = !vector && string.Equals(element, "pixel", StringComparison.OrdinalIgnoreCase);
        float width = (vector ? 16f : sourceWidth) * scaleX;
        float height = (vector ? 16f : sourceHeight) * scaleY;
        if (!Finite(width) || !Finite(height)) return Array.Empty<EditorObjectLineSegmentSnapshot>();
        double radians = rotation * Math.PI / 180;
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        float opacity = vector ? 1f : Clamp(alpha, 0, 1);
        float stroke = vector ? (float)Math.Max(1d, (double)Math.Min(Math.Abs(width), Math.Abs(height)) * .5 * Math.Abs(alpha)) : 1.2f;
        if (!Finite(stroke)) return Array.Empty<EditorObjectLineSegmentSnapshot>();
        var output = new List<EditorObjectLineSegmentSnapshot>();
        bool invalid = false;
        void Add(double ax, double ay, double bx, double by)
        {
            float x0 = (float)(x + ax * cos + ay * sin), y0 = (float)(y - ax * sin + ay * cos);
            float x1 = (float)(x + bx * cos + by * sin), y1 = (float)(y - bx * sin + by * cos);
            if (!Finite(x0) || !Finite(y0) || !Finite(x1) || !Finite(y1)) { invalid = true; return; }
            output.Add(new() { X0 = x0, Y0 = y0, X1 = x1, Y1 = y1,
                HasColor = true, Red = Clamp(red, 0, 1), Green = Clamp(green, 0, 1), Blue = Clamp(blue, 0, 1),
                Alpha = opacity, Thickness = stroke });
        }
        if (pixel)
        {
            if (Math.Abs(width) < 1.5f && Math.Abs(height) < 1.5f) return output.ToArray();
            if (Math.Abs(width) <= 2.5f || Math.Abs(height) <= 2.5f)
            {
                if (Math.Abs(height) >= Math.Abs(width)) Add(0, -(double)anchorY * height, 0, (1d - anchorY) * height);
                else Add(-(double)anchorX * width, 0, (1d - anchorX) * width, 0);
            }
            else
            {
                double left = -(double)anchorX * width, bottom = -(double)anchorY * height;
                Add(left, bottom, left + width, bottom); Add(left + width, bottom, left + width, bottom + height);
                Add(left + width, bottom + height, left, bottom + height); Add(left, bottom + height, left, bottom);
            }
        }
        else
        {
            double rx = width * .5, ry = height * .5;
            if (Math.Abs(rx) < .5 || Math.Abs(ry) < .5) return output.ToArray();
            double cx = (.5 - anchorX) * width, cy = (.5 - anchorY) * height;
            int segments = (int)Clamp((float)(Math.Max(Math.Abs(rx), Math.Abs(ry)) / 6), 32, 128);
            for (int i = 0; i < segments; i++)
            {
                double a = i * Math.PI * 2 / segments, b = (i + 1) * Math.PI * 2 / segments;
                Add(cx + Math.Cos(a) * rx, cy + Math.Sin(a) * ry, cx + Math.Cos(b) * rx, cy + Math.Sin(b) * ry);
            }
        }
        return invalid ? Array.Empty<EditorObjectLineSegmentSnapshot>() : output.ToArray();
    }

    private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
