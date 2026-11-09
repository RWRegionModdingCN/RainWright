using System;
using System.Collections.Generic;
using System.Globalization;

namespace DryCycle.DevUI.DevTool.Palette;

internal enum PaletteBlendMode { Normal, Multiply, Screen, Overlay, Add, Darken, Lighten }
internal sealed class PaletteChannel
{
    internal readonly string Name, Chinese;
    internal readonly int X, Y, Count;
    internal PaletteChannel(string name, string chinese, int x, int y, int count)
    { Name = name; Chinese = chinese; X = x; Y = y; Count = count; }
}
internal static class PaletteChannels
{
    internal const int PerCycle = 22;
    internal static readonly PaletteChannel[] All = Create();
    private static PaletteChannel[] Create()
    {
        string[] names = { "Sky", "Fog", "Black", "Item", "DeepWaterTop", "DeepWaterBottom", "WaterSurfaceClose", "WaterSurfaceFar", "WaterSurfaceHighlight", "FogIntensity", "ShortcutDot", "ShortcutDotBlink", "ShortcutDotTravel", "ShortcutSymbol", "Darkness", "Grime", "SunHighlight", "SunMiddle", "SunShadow", "ShadeHighlight", "ShadeMiddle", "ShadeShadow" };
        string[] chinese = { "天空", "雾色", "黑色", "物品", "深水上层", "深水下层", "近处水面", "远处水面", "水面高光", "雾强度", "管道点", "管道点闪烁", "管道点移动", "管道符号", "暗度", "污垢", "光照高光", "光照中间色", "光照暗部", "阴影高光", "阴影中间色", "阴影暗部" };
        var all = new PaletteChannel[PerCycle * 2];
        for (int c = 0; c < 2; c++) for (int i = 0; i < PerCycle; i++)
            all[c * PerCycle + i] = new PaletteChannel(names[i], chinese[i], i < 14 ? i : i == 14 ? 30 : 0, c * 8 + (i < 15 ? 0 : i - 14), i < 15 ? 1 : i == 15 ? 32 : 30);
        return all;
    }
    internal static PaletteChannel Get(int index) => (uint)index < All.Length ? All[index] : throw new ArgumentOutOfRangeException(nameof(index));
    internal static int At(int x, int y)
    { for (int i = 0; i < All.Length; i++) { var c = All[i]; if (c.Y == y && x >= c.X && x < c.X + c.Count) return i; } return -1; }
}
internal readonly struct PaletteGradientStop
{
    internal readonly int X, Color;
    internal PaletteGradientStop(int x, int color) { X = x; Color = color; }
}
internal static class PaletteEditing
{
    internal static int Blend(int target, int source, PaletteBlendMode mode, float amount)
    {
        Unit(amount); if ((uint)mode > (uint)PaletteBlendMode.Lighten) throw new ArgumentOutOfRangeException(nameof(mode));
        if (amount == 0) return target;
        uint output = 0;
        for (int shift = 0; shift <= 24; shift += 8)
        {
            float a = ((uint)target >> shift & 255) / 255f, b = ((uint)source >> shift & 255) / 255f;
            float blended = shift == 24 && mode != PaletteBlendMode.Normal ? a : mode switch
            {
                PaletteBlendMode.Multiply => a * b,
                PaletteBlendMode.Screen => 1 - (1 - a) * (1 - b),
                PaletteBlendMode.Overlay => a < .5f ? 2 * a * b : 1 - 2 * (1 - a) * (1 - b),
                PaletteBlendMode.Add => Math.Min(1, a + b),
                PaletteBlendMode.Darken => Math.Min(a, b),
                PaletteBlendMode.Lighten => Math.Max(a, b),
                _ => b
            };
            output |= (uint)Math.Max(0, Math.Min(255, (int)Math.Round((a + (blended - a) * amount) * 255))) << shift;
        }
        return unchecked((int)output);
    }
    internal static void MapChannel(PaletteDocument target, PaletteDocument source, int sourceIndex, int targetIndex, PaletteBlendMode mode, float start, float end, bool reverse)
    {
        Unit(start); Unit(end);
        var from = PaletteChannels.Get(sourceIndex); var to = PaletteChannels.Get(targetIndex);
        // A frozen source prevents overlapping channel mappings from feeding back into themselves.
        var colors = new int[from.Count]; for (int i = 0; i < colors.Length; i++) colors[i] = source.Get(from.X + i, from.Y);
        uint average = 0;
        for (int shift = 0; shift <= 24; shift += 8) { int sum = 0; foreach (int c in colors) sum += (int)((uint)c >> shift & 255); average |= (uint)((sum + colors.Length / 2) / colors.Length) << shift; }
        for (int i = 0; i < to.Count; i++)
        {
            float t = to.Count > 1 ? (float)i / (to.Count - 1) : 0; if (reverse) t = 1 - t;
            int color = colors.Length == to.Count ? colors[i] : unchecked((int)average);
            target.Set(to.X + i, to.Y, Blend(target.Get(to.X + i, to.Y), color, mode, start + (end - start) * t));
        }
    }
    internal static void Gradient(PaletteDocument target, int row, IReadOnlyList<PaletteGradientStop> stops)
    {
        target.Get(0, row);
        if (stops == null || stops.Count < 2 || stops.Count > 30) throw new ArgumentException("A gradient requires 2–30 stops.");
        for (int i = 0; i < stops.Count; i++)
            if ((uint)stops[i].X >= 30 || i > 0 && stops[i].X <= stops[i - 1].X) throw new ArgumentException("Gradient stops require unique increasing depths 0–29.");
        for (int i = 1; i < stops.Count; i++)
        {
            var a = stops[i - 1]; var b = stops[i];
            for (int x = a.X; x <= b.X; x++) target.Set(x, row, Blend(a.Color, b.Color, PaletteBlendMode.Normal, (float)(x - a.X) / (b.X - a.X)));
        }
    }
    internal static void Paint(PaletteDocument target, int x0, int y0, int x1, int y1, int radius, int color, PaletteBlendMode mode, float opacity, bool[] visited)
    {
        target.Get(x0, y0); target.Get(x1, y1); Unit(opacity);
        if (radius < 0 || radius > 6 || y0 / 8 != y1 / 8 || visited == null || visited.Length != PaletteDocument.Count) throw new ArgumentException("Invalid palette stroke.");
        int cycle = y0 / 8, dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, error = dx + dy;
        for (;;)
        {
            for (int y = Math.Max(cycle * 8, y0 - radius); y <= Math.Min(cycle * 8 + 7, y0 + radius); y++)
                for (int x = Math.Max(0, x0 - radius); x <= Math.Min(31, x0 + radius); x++)
                {
                    if ((x - x0) * (x - x0) + (y - y0) * (y - y0) > radius * radius) continue;
                    if (x0 < 30 && y0 % 8 >= 2 && (x >= 30 || y % 8 < 2)) continue;
                    int index = y * 32 + x; if (visited[index]) continue; visited[index] = true;
                    target.Set(x, y, Blend(target.Get(x, y), color, mode, opacity));
                }
            if (x0 == x1 && y0 == y1) break;
            int twice = error * 2; if (twice >= dy) { error += dy; x0 += sx; } if (twice <= dx) { error += dx; y0 += sy; }
        }
    }
    internal static PaletteGradientStop[] ParseStops(string text)
    {
        string[] parts = (text ?? "").Split(';'); if (parts.Length < 2 || parts.Length > 30) throw new ArgumentException("Invalid gradient stop count.");
        var result = new PaletteGradientStop[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            string[] pair = parts[i].Split(':');
            if (pair.Length != 2 || !int.TryParse(pair[0], NumberStyles.None, CultureInfo.InvariantCulture, out int x) || !uint.TryParse(pair[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint color)) throw new ArgumentException("Invalid gradient stop.");
            result[i] = new PaletteGradientStop(x, unchecked((int)color));
        }
        return result;
    }
    private static void Unit(float value) { if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value)); }
}
