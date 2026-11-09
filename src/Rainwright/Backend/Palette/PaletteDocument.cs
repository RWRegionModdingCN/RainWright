using System;
using System.Globalization;
using System.IO;
using System.Text;
using DryCycle.DevUI.DevTool.Map.Cartography;

namespace DryCycle.DevUI.DevTool.Palette;

// Coordinates follow the PNG/editor convention: (0, 0) is the top left.
internal sealed class PaletteDocument
{
    internal const int Width = 32, Height = 16, Count = Width * Height;
    private readonly int[] pixels;

    internal PaletteDocument(int[] source)
    {
        if (source == null || source.Length != Count) throw new ArgumentException("A palette must contain 32 × 16 pixels.");
        pixels = (int[])source.Clone();
    }

    internal int Get(int x, int y) => pixels[Index(x, y)];
    internal void Set(int x, int y, int color) => pixels[Index(x, y)] = color;
    internal int[] CopyPixels() => (int[])pixels.Clone();
    internal PaletteDocument Clone() => new(pixels);

    private static int Index(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException("pixel");
        return y * Width + x;
    }

    internal void FillRow(int y, int color)
    { Index(0, y); for (int x = 0; x < Width; x++) Set(x, y, color); }

    internal void Gradient(int y, int start, int end)
    {
        Index(0, y);
        // Only the 30 depth columns; the two auxiliary columns are preserved.
        for (int x = 0; x < 30; x++)
        {
            uint color = 0;
            for (int shift = 0; shift <= 24; shift += 8)
            {
                int a = (int)((uint)start >> shift & 255), b = (int)((uint)end >> shift & 255);
                color |= (uint)((a * (29 - x) + b * x + 14) / 29) << shift;
            }
            Set(x, y, unchecked((int)color));
        }
    }

    internal void CopyCycle(bool fromRain)
    {
        int source = fromRain ? 8 : 0, destination = fromRain ? 0 : 8;
        Array.Copy(pixels, source * Width, pixels, destination * Width, 8 * Width);
    }

    internal string ToHex()
    {
        var text = new StringBuilder(Count * 8);
        foreach (int color in pixels) text.Append(unchecked((uint)color).ToString("X8", CultureInfo.InvariantCulture));
        return text.ToString();
    }

    internal static PaletteDocument FromHex(string text)
    {
        if (text == null || text.Length != Count * 8) throw new InvalidDataException("Invalid palette draft pixels.");
        var pixels = new int[Count];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = unchecked((int)uint.Parse(text.Substring(i * 8, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        return new PaletteDocument(pixels);
    }

    internal byte[] EncodePng()
    {
        using var output = new MemoryStream();
        CartographyPngStreamEncoder.Write(output, Width, Height, (top, rows, write) =>
        {
            var row = new int[Width];
            for (int y = top; y < top + rows; y++) { Array.Copy(pixels, y * Width, row, 0, Width); write(row); }
        });
        return output.ToArray();
    }

    internal static void ValidatePngHeader(byte[] bytes)
    {
        byte[] signature = { 137, 80, 78, 71, 13, 10, 26, 10 };
        if (bytes == null || bytes.Length < 33) throw new InvalidDataException("Invalid palette PNG.");
        for (int i = 0; i < 8; i++) if (bytes[i] != signature[i]) throw new InvalidDataException("Not a PNG file.");
        if (bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 13 ||
            bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R' ||
            bytes[16] != 0 || bytes[17] != 0 || bytes[18] != 0 || bytes[19] != Width ||
            bytes[20] != 0 || bytes[21] != 0 || bytes[22] != 0 || bytes[23] != Height)
            throw new InvalidDataException("Rain World palettes must be 32 × 16 pixels.");
    }
}
