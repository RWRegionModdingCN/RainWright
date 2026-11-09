using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Immutable font inputs shared by successive RWImGUI atlases. RWImGUI retains
/// its native atlas across consumer disable/re-enable, so these two pinned inputs
/// have process lifetime, not UI lifetime. Rebuilding an atlas reuses the same
/// buffers; it never allocates another copy or transfers GC memory to ImGui's allocator.
/// </summary>
internal static unsafe class EmbeddedFontAssets
{
    private sealed class FontData
    {
        private readonly GCHandle pin;
        internal readonly int Length;
        internal IntPtr Address => pin.AddrOfPinnedObject();

        internal FontData(byte[] bytes)
        {
            Length = bytes.Length;
            pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        }
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<string, FontData> Fonts = new(StringComparer.Ordinal);

    internal static ImFontPtr Add(ImFontAtlasPtr atlas, string name, float size, IntPtr ranges = default)
    {
        FontData data;
        lock (Gate)
        {
            if (!Fonts.TryGetValue(name, out data))
            {
                using Stream source = typeof(EmbeddedFontAssets).Assembly.GetManifestResourceStream("RainWright.Fonts." + name)
                    ?? throw new FileNotFoundException("RainWright embedded font is missing: " + name);
                byte[] bytes = EmbeddedTrueTypeFont.Read(source, name);
                data = new FontData(bytes);
                Fonts.Add(name, data);
            }
        }

        ImFontConfigPtr config = new(ImGuiNative.ImFontConfig_ImFontConfig());
        if (config.NativePtr == null) throw new InvalidOperationException("RWImGUI could not allocate a font configuration.");
        try
        {
            config.FontDataOwnedByAtlas = false;
            ImFontPtr font = atlas.AddFontFromMemoryTTF(data.Address, data.Length, size, config, ranges);
            if (font.NativePtr == null) throw new InvalidOperationException("RWImGUI rejected embedded font: " + name);
            return font;
        }
        finally { config.Destroy(); }
    }
}
