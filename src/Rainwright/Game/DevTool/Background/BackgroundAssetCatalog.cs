using System;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Background;

internal static class BackgroundAssetCatalog
{
    internal static readonly string[] Groups = { "ATC", "RFV", "PNK", "AUV", "OTR", "Illustrations", "Sprites", "All" };
    private static readonly string[] Prefixes = { "atc_", "rf_", "pnk_", "au_", "otr_" };

    internal static bool IsAsset(string key) => key is "assetName" or "spriteName" or "daySky" or "duskSky" or "nightSky";
    internal static bool IsShader(string key) => key == "spriteShader";

    internal static string[] Select(string[] illustrations, string[] sprites, int group, string search)
    {
        var source = group == 5 ? illustrations : group == 6 ? sprites : illustrations.Concat(sprites);
        string prefix = group >= 0 && group < Prefixes.Length ? Prefixes[group] : "";
        return source.Where(name => !string.IsNullOrEmpty(name) &&
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrEmpty(search) || name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
