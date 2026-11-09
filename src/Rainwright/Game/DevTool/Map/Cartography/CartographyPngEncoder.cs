using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

/// <summary>
/// Game-only bitmap readback bridge: convert a temporary GDI+ bitmap band
/// into pooled scanline-sized pixel rows for Rainwright's pure PNG codec.
/// </summary>
internal static class CartographyPngEncoder
{
    internal const int BandPixels = CartographyPngStreamEncoder.BandPixels;

    internal static void Write(Stream output, int width, int height,
        Func<int, int, Bitmap> renderBand) =>
        CartographyPngStreamEncoder.Write(output, width, height, (top, rows, consumeRow) =>
        {
            using Bitmap bitmap = renderBand(top, rows);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, rows),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[width];
                for (int y = 0; y < rows; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, width);
                    consumeRow(row);
                }
            }
            finally { bitmap.UnlockBits(data); }
        });
}
