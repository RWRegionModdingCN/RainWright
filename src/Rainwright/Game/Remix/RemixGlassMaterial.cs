using System;
using UnityEngine;

namespace RainWright.Remix;

// UI-thread lease: all open RainWright pages borrow the same two GPU textures.
// Immutable pixel data is generated once; GPU objects leave with the last page.
internal static class RemixGlassMaterial
{
    private const int Resolution = 512;
    private static readonly Lazy<(Color32[] Body, Color32[] Light)> pixels = new(Generate);
    private static Texture2D body, light;
    private static string bodyAtlas, lightAtlas;
    private static int users;
    internal static int GenerationCount { get; private set; }
    internal static int Users => users;

    internal sealed class Lease : IDisposable
    {
        internal string BodyAtlas => bodyAtlas;
        internal string LightAtlas => lightAtlas;
        private bool disposed;
        public void Dispose()
        {
            if (disposed) return;
            if (users == 1) ReleaseTextures();
            users--; disposed = true;
        }
    }
    internal static Lease Acquire()
    {
        if (users == 0)
        {
            try
            {
                var data = pixels.Value;
                string id = Guid.NewGuid().ToString("N");
                bodyAtlas = "RainWright.Remix.Glass.Body." + id;
                lightAtlas = "RainWright.Remix.Glass.Light." + id;
                body = Create(data.Body, bodyAtlas);
                light = Create(data.Light, lightAtlas);
            }
            catch (Exception failure)
            {
                try { ReleaseTextures(); }
                catch (Exception cleanup) { throw new AggregateException("Shared glass creation and cleanup failed.", failure, cleanup); }
                throw;
            }
        }
        users++;
        return new Lease();
    }
    private static Texture2D Create(Color32[] data, string atlas)
    {
        var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            { name = atlas, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        try
        {
            texture.SetPixels32(data); texture.Apply(false, true);
            Futile.atlasManager.LoadAtlasFromTexture(atlas, texture, false);
            return texture;
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }
    private static void ReleaseTextures()
    {
        Release(ref body, ref bodyAtlas);
        Release(ref light, ref lightAtlas);
    }
    private static void Release(ref Texture2D texture, ref string atlas)
    {
        if (atlas != null && Futile.atlasManager.DoesContainAtlas(atlas)) Futile.atlasManager.UnloadAtlas(atlas);
        atlas = null;
        if (texture != null) UnityEngine.Object.Destroy(texture);
        texture = null;
    }
    private static (Color32[] Body, Color32[] Light) Generate()
    {
        GenerationCount++;
        var fill = new Color32[Resolution * Resolution];
        var highlights = new Color32[fill.Length];
        for (int py = 0; py < Resolution; py++)
        {
            double y = (py + .5) * 600 / Resolution, v = y / 600;
            const double radius = 14;
            double qy = Math.Abs(y - 300) - (297 - radius), oy = Math.Max(qy, 0);
            double opacity = .9 + .1 * (1 - v), reflectionOffset = .65 * (1 - v) - .25;
            double glowFactor = .006 + .018 * v * v, directionBase = .18 + .65 * v;
            int row = py * Resolution;
            for (int px = 0; px < Resolution; px++)
            {
                double x = (px + .5) * 600 / Resolution;
                double qx = Math.Abs(x - 300) - (297 - radius), ox = Math.Max(qx, 0);
                double distance = Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - radius;
                double coverage = Clamp(.5 - distance);
                if (coverage == 0) continue;
                double depth = Math.Max(0, -distance), u = x / 600;
                double reflectionDistance = (u + reflectionOffset) / .75;
                double rimDistance = depth / 1.25, bevelDistance = (depth - 3.3) / 2.8;
                double glow = Math.Exp(-reflectionDistance * reflectionDistance) * glowFactor;
                double rim = Math.Exp(-rimDistance * rimDistance);
                double bevel = Math.Exp(-bevelDistance * bevelDistance);
                double direction = directionBase + .17 * (1 - u);
                int index = row + px;
                fill[index] = new Color32(18, 21, 24, Byte(opacity * coverage));
                highlights[index] = new Color32(232, 241, 248,
                    Byte((glow + rim * (.12 + .48 * direction) + bevel * .035 * direction) * coverage));
            }
        }
        return (fill, highlights);
    }
    private static double Clamp(double value) => Math.Max(0, Math.Min(1, value));
    private static byte Byte(double value) => (byte)Math.Round(Clamp(value) * 255);
}
