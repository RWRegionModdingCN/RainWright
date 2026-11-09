using System;
using System.IO;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal static class EmbeddedTrueTypeFont
{
    // Manifest resources advertise an exact size. Read straight into the final pinned
    // input instead of growing a MemoryStream and then copying its entire buffer.
    internal static byte[] Read(Stream source, string name)
    {
        long length = source.Length;
        if (length < 1024 || length > 64L * 1024 * 1024)
            throw new InvalidDataException("RainWright embedded font has an invalid length: " + name);
        byte[] bytes = new byte[(int)length];
        int offset = 0;
        while (offset < bytes.Length)
        {
            int read = source.Read(bytes, offset, bytes.Length - offset);
            if (read == 0) throw new InvalidDataException("RainWright embedded TrueType font is truncated: " + name);
            offset += read;
        }
        if (source.ReadByte() != -1 || bytes[0] != 0 || bytes[1] != 1 || bytes[2] != 0 || bytes[3] != 0)
            throw new InvalidDataException("RainWright embedded TrueType font is truncated or invalid: " + name);
        return bytes;
    }
}
