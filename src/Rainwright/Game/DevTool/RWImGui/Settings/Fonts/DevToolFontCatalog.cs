using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Logging;
using ImGuiNET;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Registers one local CJK face in RWImGUI's native atlas before its first backend upload.
/// Input-context switches neither destroy this atlas nor invalidate its font pointers.
/// </summary>
internal static unsafe class DevToolFontCatalog
{
    internal const string DefaultChineseFamily = "HarmonyOS Sans SC";
    internal const string ChineseFontFileName = "HarmonyOS_Sans_SC_Medium.ttf";
    internal const string UbuntuMonoFamily = "Ubuntu Mono";

    private const int FixedChineseFontWeight = 500;

    private sealed class RegisteredFace
    {
        internal ImFontPtr Font;
        internal string FileName;
        internal string Family;
        internal int Weight;
        internal bool ChineseCapable;
    }

    private static readonly List<RegisteredFace> RegisteredFaces = new();
    private static readonly HashSet<string> RegisteredPaths = new(StringComparer.OrdinalIgnoreCase);
    private static bool registrationAttempted;
    private static bool registrationSucceeded;
    private static string registrationMessage = "Embedded font registration has not been attempted.";
    private static IntPtr atlasIdentity;

    internal static void RegisterBeforeBackendFrame(ManualLogSource log)
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        if (atlas.NativePtr == null) return;
        if ((IntPtr)atlas.NativePtr != atlasIdentity)
        {
            ResetAtlasState();
            atlasIdentity = (IntPtr)atlas.NativePtr;
        }
        if (!registrationAttempted) TryRegisterLocalFonts(log);
    }

    // Font files and the ImGui atlas are stable after startup. The settings window is rendered every
    // frame, so never repeat filesystem enumeration or glyph probing there.
    private static string[] cachedChineseFamilies;
    private static int cachedSelectableLocalChineseFaces = -1;

    internal static bool RegistrationAttempted => registrationAttempted;
    internal static bool RegistrationSucceeded => registrationSucceeded;
    internal static int RegisteredLocalFaceCount => RegisteredFaces.Count;
    internal static string RegistrationMessage => registrationMessage;

    /// <summary>
    /// Called only when the native atlas changes, never when an IMGUIContext callback is replaced.
    /// </summary>
    private static void ResetAtlasState()
    {
        RegisteredFaces.Clear();
        RegisteredPaths.Clear();
        registrationAttempted = false;
        registrationSucceeded = false;
        registrationMessage = "Embedded font registration has not been attempted.";
        cachedChineseFamilies = null;
        cachedSelectableLocalChineseFaces = -1;
    }

    internal static string ChineseFontPath => "RainWright.Fonts." + ChineseFontFileName;
    internal static string FontDirectory => "RainWright.dll (embedded)";

    /// <summary>
    /// Registers once on the Present thread, before DX11 NewFrame uploads the unlocked native atlas.
    /// </summary>
    internal static bool TryRegisterLocalFonts(ManualLogSource log)
    {
        if (registrationAttempted) return registrationSucceeded;
        registrationAttempted = true;
        InvalidatePresentationCaches();

        try
        {
            ImGuiIOPtr io = ImGui.GetIO();
            if (io.Fonts.NativePtr == null)
            {
                registrationMessage = "RWImGui font atlas is unavailable.";
                log?.LogWarning("RainWright skipped embedded font registration: RWImGui font atlas is unavailable.");
                return false;
            }

            if (io.Fonts.Locked || io.Fonts.TexID != 0UL)
            {
                registrationMessage = "RWImGUI's atlas is already locked or uploaded; font initialization missed the pre-upload boundary.";
                log?.LogWarning(
                    "RainWright font registration missed RWImGUI's pre-upload boundary. The atlas was left intact.");
                return false;
            }


            // The common subset omits UI characters such as 浏 (浏览器), as well as names entered
            // by region authors. Bake the complete CJK range once; context switches reuse it.
            IntPtr chineseGlyphRanges = io.Fonts.GetGlyphRangesChineseFull();

            ImFontPtr font;
            try
            {
                font = EmbeddedFontAssets.Add(io.Fonts, ChineseFontFileName,
                    DevToolUiSettings.ReferenceFontSize, chineseGlyphRanges);
            }
            catch (Exception error)
            {
                registrationMessage = "Required Chinese font could not be added: " + error.Message;
                log?.LogWarning(
                    "RainWright could not register required Chinese font '" +
                    ChineseFontFileName +
                    "': " +
                    error);
                return false;
            }

            if (font.NativePtr == null)
            {
                registrationMessage = "Required Chinese font returned a null ImFont.";
                log?.LogWarning(
                    "RainWright required Chinese font returned a null ImFont: " +
                    ChineseFontFileName);
                return false;
            }

            RegisteredFaces.Clear();
            RegisteredPaths.Clear();
            RegisteredPaths.Add(ChineseFontPath);
            RegisteredFaces.Add(new RegisteredFace
            {
                Font = font,
                FileName = ChineseFontFileName,
                Family = DefaultChineseFamily,
                Weight = FixedChineseFontWeight,
                ChineseCapable = true
            });

            // The English face is an independent embedded input. A failed Latin
            // registration must not invalidate the successfully registered CJK face.
            try
            {
                ImFontPtr latin = EmbeddedFontAssets.Add(io.Fonts, "UbuntuMono-Regular.ttf", DevToolUiSettings.ReferenceFontSize);
                RegisteredFaces.Add(new RegisteredFace
                {
                    Font = latin, FileName = "UbuntuMono-Regular.ttf", Family = UbuntuMonoFamily,
                    Weight = 400, ChineseCapable = false
                });
            }
            catch (Exception error)
            {
                log?.LogWarning("RainWright embedded Ubuntu Mono registration failed; using the RWImGUI Latin face. " + error);
            }

            cachedSelectableLocalChineseFaces = 1;
            cachedChineseFamilies = new[] { DefaultChineseFamily };
            registrationSucceeded = true;
            registrationMessage = "Registered fixed Chinese font: " + ChineseFontFileName + ".";

            log?.LogInfo(
                "RainWright registered fixed Chinese font '" +
                ChineseFontFileName +
                "' before RWImGUI's first atlas upload.");
            return true;
        }
        catch (Exception error)
        {
            registrationMessage = "Embedded font registration failed: " + error.Message;
            log?.LogWarning("RainWright embedded font registration failed safely: " + error);
            return false;
        }
    }

    /// <summary>
    /// Enumerates Chinese-capable families once per atlas lifetime. Glyph probing is deliberately
    /// kept out of the frame loop because FindGlyphNoFallback crosses the managed/native boundary.
    /// </summary>
    internal static string[] GetAvailableChineseFamilies()
    {
        if (cachedChineseFamilies != null) return cachedChineseFamilies;
        cachedChineseFamilies =
            registrationSucceeded
                ? new[] { DefaultChineseFamily }
                : Array.Empty<string>();
        return cachedChineseFamilies;
    }

    internal static int CountLocalFontFiles() => 2;

    internal static int CountSelectableLocalChineseFaces()
    {
        if (cachedSelectableLocalChineseFaces >= 0) return cachedSelectableLocalChineseFaces;

        int count = 0;
        for (int i = 0; i < RegisteredFaces.Count; i++)
            if (RegisteredFaces[i].ChineseCapable) count++;
        cachedSelectableLocalChineseFaces = count;
        return count;
    }

    internal static bool TryResolveRegisteredFace(
        string family,
        int preferredWeight,
        bool requireChinese,
        out ImFontPtr font,
        out string name,
        out int actualWeight,
        out int variantCount)
    {
        if (TryResolveFamily(
                family,
                preferredWeight,
                requireChinese,
                out font,
                out name,
                out actualWeight,
                out variantCount))
            return true;

        if (requireChinese &&
            !string.Equals(
                NormalizeFamily(family),
                NormalizeFamily(DefaultChineseFamily),
                StringComparison.OrdinalIgnoreCase) &&
            TryResolveFamily(
                DefaultChineseFamily,
                preferredWeight,
                true,
                out font,
                out name,
                out actualWeight,
                out variantCount))
            return true;

        if (requireChinese)
        {
            for (int i = 0; i < RegisteredFaces.Count; i++)
            {
                RegisteredFace face = RegisteredFaces[i];
                if (!face.ChineseCapable) continue;
                if (TryResolveFamily(
                        face.Family,
                        preferredWeight,
                        true,
                        out font,
                        out name,
                        out actualWeight,
                        out variantCount))
                    return true;
            }
        }

        font = default;
        name = string.Empty;
        actualWeight = preferredWeight;
        variantCount = 0;
        return false;
    }

    private static bool TryResolveFamily(
        string family,
        int preferredWeight,
        bool requireChinese,
        out ImFontPtr font,
        out string name,
        out int actualWeight,
        out int variantCount)
    {
        font = default;
        name = string.Empty;
        actualWeight = preferredWeight;
        variantCount = 0;

        int bestDistance = int.MaxValue;
        for (int i = 0; i < RegisteredFaces.Count; i++)
        {
            RegisteredFace face = RegisteredFaces[i];
            if (!string.Equals(
                    NormalizeFamily(face.Family),
                    NormalizeFamily(family),
                    StringComparison.OrdinalIgnoreCase))
                continue;
            if (requireChinese && !face.ChineseCapable)
                continue;

            variantCount++;
            int distance = Math.Abs(face.Weight - preferredWeight);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            font = face.Font;
            name = face.FileName;
            actualWeight = face.Weight;
        }

        return font.NativePtr != null;
    }

    internal static bool TryGetRegisteredFace(ImFontPtr font, out string name, out int weight)
    {
        for (int i = 0; i < RegisteredFaces.Count; i++)
        {
            RegisteredFace face = RegisteredFaces[i];
            if (face.Font.NativePtr != font.NativePtr) continue;
            name = face.FileName;
            weight = face.Weight;
            return true;
        }

        name = string.Empty;
        weight = DevToolUiSettings.DefaultFontWeight;
        return false;
    }

    internal static bool IsChineseUiSelectable(ImFontPtr font, string candidateName)
    {
        if (font.NativePtr == null) return false;
        for (int i = 0; i < RegisteredFaces.Count; i++)
        {
            RegisteredFace face = RegisteredFaces[i];
            if (face.Font.NativePtr == font.NativePtr)
                return face.ChineseCapable;
        }
        return false;
    }

    internal static bool IsFamilyMatch(string candidateName, string family)
    {
        if (string.IsNullOrWhiteSpace(family)) return true;
        return string.Equals(
            NormalizeFamily(FamilyFromName(candidateName)),
            NormalizeFamily(family),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static string FriendlyFaceName(string candidateName)
    {
        if (string.IsNullOrWhiteSpace(candidateName)) return string.Empty;
        string value = candidateName.Trim();
        int comma = value.IndexOf(',');
        if (comma > 0) value = value.Substring(0, comma);
        value = Path.GetFileName(value);
        value = Path.GetFileNameWithoutExtension(value);
        return value.Replace('_', ' ').Trim();
    }

    internal static string FamilyFromName(string candidateName)
    {
        string value = FriendlyFaceName(candidateName);
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        value = value.Replace('-', ' ');
        string[] parts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int count = parts.Length;
        while (count > 1 && IsWeightToken(parts[count - 1])) count--;

        string family = string.Join(" ", parts, 0, count).Trim();
        if (string.IsNullOrEmpty(family)) family = value;
        return string.Equals(NormalizeFamily(family), "UbuntuMono", StringComparison.OrdinalIgnoreCase)
            ? UbuntuMonoFamily
            : family;
    }

    internal static int InferWeight(string name)
    {
        string value = (name ?? string.Empty).ToLowerInvariant().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
        if (value.Contains("black") || value.Contains("heavy")) return 900;
        if (value.Contains("extrabold") || value.Contains("ultrabold")) return 800;
        if (value.Contains("semibold") || value.Contains("demibold")) return 600;
        if (value.Contains("bold")) return 700;
        if (value.Contains("medium")) return 500;
        if (value.Contains("extralight") || value.Contains("ultralight")) return 200;
        if (value.Contains("light")) return 300;
        if (value.Contains("thin")) return 100;
        return 400;
    }

    private static int CompareFontFilesForRegistration(string left, string right)
    {
        int leftPriority = FontRegistrationPriority(left);
        int rightPriority = FontRegistrationPriority(right);
        int compare = leftPriority.CompareTo(rightPriority);
        return compare != 0
            ? compare
            : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static int FontRegistrationPriority(string file)
    {
        string extension = Path.GetExtension(file);
        if (!IsFontExtension(extension)) return int.MaxValue;

        string fileName = Path.GetFileName(file);
        string faceName = Path.GetFileNameWithoutExtension(file);
        string family = FamilyFromName(faceName);
        if (!IsChineseFamilyName(family, fileName)) return int.MaxValue;

        string normalizedFamily = NormalizeFamily(family);
        string selectedFamily = NormalizeFamily(DevToolUiSettings.ChineseFontFamily);
        string defaultFamily = NormalizeFamily(DefaultChineseFamily);

        int familyRank =
            string.Equals(normalizedFamily, selectedFamily, StringComparison.OrdinalIgnoreCase)
                ? 0
                : string.Equals(normalizedFamily, defaultFamily, StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : 2;

        int weightDistance = Math.Abs(InferWeight(faceName) - DevToolUiSettings.DefaultChineseFontWeight);
        return familyRank * 10000 + weightDistance;
    }

    private static void InvalidatePresentationCaches()
    {
        cachedChineseFamilies = null;
        cachedSelectableLocalChineseFaces = -1;
    }

    private static bool IsFontExtension(string extension)
    {
        return string.Equals(extension, ".ttf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".otf", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".ttc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChineseFamilyName(string family, string fileName)
    {
        string value =
            NormalizeFamily((family ?? string.Empty) + " " + (fileName ?? string.Empty))
                .ToLowerInvariant();

        // Keep the detector conservative. A false positive is much worse than a false negative:
        // selecting a Latin-only face for the Chinese UI turns every label into '?'.
        string[] markers =
        {
            "harmonyossanssc",
            "notosanssc",
            "notoserifsc",
            "notosanscjk",
            "notoserifcjk",
            "sourcehansans",
            "sourcehanserif",
            "pingfangsc",
            "microsoftyahei",
            "simhei",
            "simsun",
            "kaiti",
            "fangsong",
            "wenquanyi",
            "lxgwwenkai",
            "sarasa",
            "smileysans",
            "alibabapuhuiti",
            "misans",
            "opposans",
            "droidsansfallback"
        };

        for (int i = 0; i < markers.Length; i++)
            if (value.Contains(markers[i]))
                return true;

        return false;
    }

    private static void AddUnique(List<string> values, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        for (int i = 0; i < values.Count; i++)
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return;
        values.Add(value);
    }

    private static string NormalizeFamily(string value) =>
        (value ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).Trim();

    private static bool IsWeightToken(string value)
    {
        string token = (value ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        return token == "thin" || token == "extralight" || token == "ultralight" ||
               token == "light" || token == "regular" || token == "normal" || token == "book" ||
               token == "medium" || token == "semibold" || token == "demibold" ||
               token == "bold" || token == "extrabold" || token == "ultrabold" ||
               token == "black" || token == "heavy";
    }
}
