using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Background;

/// <summary>Lossless author document for the community BackgroundBuilder TXT format.</summary>
internal sealed class BackgroundDocument
{
    internal sealed class Line
    {
        internal string Original, Prefix, Key, Value;
        internal int Depth;
        internal bool Changed;
        internal Line Clone() => (Line)MemberwiseClone();
        internal string Text => !Changed ? Original : Prefix + Key + ": " + Value;
    }
    internal readonly List<Line> Lines = new();
    private string newline = "\n";
    internal static BackgroundDocument Parse(string text)
    {
        var result = new BackgroundDocument { newline = text.Contains("\r\n") ? "\r\n" : "\n" };
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = new Line { Original = raw, Prefix = "", Key = "", Value = "" };
            string body = raw; int prefix = 0;
            while (true)
            {
                if (body.StartsWith("- ", StringComparison.Ordinal)) { line.Depth++; prefix += 2; body = body.Substring(2); }
                else if (body.StartsWith("(", StringComparison.Ordinal) && body.IndexOf(')') is int end && end > 0)
                { prefix += end + 1; body = body.Substring(end + 1); }
                else break;
            }
            int colon = body.IndexOf(':');
            if (colon > 0 && !body.TrimStart().StartsWith("//", StringComparison.Ordinal) && !body.TrimStart().StartsWith("\\", StringComparison.Ordinal))
            { line.Key = body.Substring(0, colon).Trim(); line.Value = body.Substring(colon + 1).Trim(); line.Prefix = raw.Substring(0, prefix); }
            result.Lines.Add(line);
        }
        return result;
    }
    internal string Serialize() => string.Join(newline, Lines.Select(l => l.Text));
    internal BackgroundDocument Clone() => Parse(Serialize());
    internal static bool Applies(string prefix, string campaign)
    {
        int start = prefix.IndexOf('('), end = prefix.IndexOf(')');
        if (start < 0 || end <= start) return true;
        string rule = prefix.Substring(start + 1, end - start - 1);
        bool exclude = rule.StartsWith("X-", StringComparison.Ordinal);
        bool matches = (exclude ? rule.Substring(2) : rule).Split(',').Any(n => n.Trim().Equals(campaign, StringComparison.OrdinalIgnoreCase));
        return exclude ? !matches : matches;
    }
    internal string Get(string key, string fallback = "") => Lines.LastOrDefault(l => l.Depth == 0 && l.Prefix.Length == 0 && l.Key == key)?.Value ?? fallback;
    internal void Set(string key, string value)
    {
        ValidateValue(value);
        var line = Lines.LastOrDefault(l => l.Depth == 0 && l.Prefix.Length == 0 && l.Key == key);
        if (line == null) { line = new Line { Prefix = "", Key = key, Original = "", Value = "" }; Lines.Add(line); }
        line.Value = value; line.Changed = true;
    }
    internal void SetLine(int index, string value)
    { ValidateValue(value); Lines[index].Value = value; Lines[index].Changed = true; }
    internal static void ValidateValue(string value)
    { if (value == null || value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0) throw new InvalidDataException("Background values must occupy one line."); }
    internal static string FileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > 120 || name is "." or ".." ||
            name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|', '\0', '\r', '\n' }) >= 0 || name.EndsWith(".", StringComparison.Ordinal))
            throw new InvalidDataException("Use a background name without directories or special characters.");
        string stem = Path.GetFileNameWithoutExtension(name).ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem) || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))
            throw new InvalidDataException("Reserved background filename.");
        return name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
    }
    internal static float Number(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 100000000)
            throw new InvalidDataException("Invalid background number: " + text);
        return value;
    }
}

internal sealed class BackgroundElementSchema
{
    internal readonly string Name, Record, Scenes, Defaults;
    internal readonly string[] Fields;
    internal BackgroundElementSchema(string name, string record, string scenes, string fields, string defaults)
    { Name = name; Record = record; Scenes = scenes; Fields = fields.Split(','); Defaults = defaults; }
    internal bool Allowed(string scene) => Scenes.Length == 0 || Scenes.Split('|').Contains(scene);
    internal static readonly BackgroundElementSchema[] All =
    {
        new("GROUP", "BG_ElementGroup", "", "name,pos.x,pos.y,depth", "Group, 0, 0, 0"),
        new("SimpleElement", "BG_SimpleElement", "", "assetName,pos.x,pos.y,depth", "Futile_White, 0, 0, 100"),
        new("SimpleIllustration", "BG_Illustration", "", "assetName,pos.x,pos.y,depth", "AtC_Sky, 0, 0, 100"),
        new("DistantBuilding", "ACV_DistantBuilding", "AboveCloudsView", "assetName,pos.x,pos.y,depth,atmoDepthAdd", "AtC_Structure1, 0, 0, 100, 0"),
        new("DistantLightning", "ACV_DistantLightning", "AboveCloudsView", "assetName,pos.x,pos.y,depth,minusDepthForLayering", "AtC_Light1, 0, 0, 100, 0"),
        new("FlyingCloud", "ACV_FlyingCloud", "AboveCloudsView", "pos.x,pos.y,depth,flattened,alpha,shaderInputColor", "0, 0, 100, 1, 1, 1"),
        new("HorizonFog", "ACV_HorizonFog", "AboveCloudsView", "assetName,pos.x,pos.y,depth", "pnk_fog, 0, 0, 100"),
        new("DistantBuilding", "RTV_DistantBuilding", "RoofTopView", "assetName,pos.x,pos.y,depth,atmoDepthAdd", "city1, 0, 0, 100, 0"),
        new("Building", "RTV_Building", "RoofTopView", "assetName,pos.x,pos.y,depth,scale", "city1, 0, 0, 100, 1"),
        new("Floor", "RTV_Floor", "RoofTopView", "assetName,pos.x,pos.y,fromDepth,toDepth", "floor, 0, 0, 1, 12"),
        new("DistantGhost", "RTV_DistantGhost", "RoofTopView", "pos.x,pos.y,depth", "0, 0, 100"),
        new("DustWave", "RTV_DustWave", "RoofTopView", "assetName,pos.x,pos.y,depth", "RF_CityA, 0, 0, 100"),
        new("Smoke", "RTV_Smoke", "RoofTopView|AncientUrbanView", "pos.x,pos.y,depth,flattened,alpha,shaderInputColor,shaderType", "0, 0, 100, 1, 1, 1, False"),
        new("Building", "AUV_Building", "AncientUrbanView", "assetName,pos.x,pos.y,depth,scale,rotation,thickness", "au_buildingp_1, 0, 0, 100, 1, 0, 1"),
        new("SmokeGradient", "AUV_SmokeGradient", "AncientUrbanView", "pos.x,pos.y,depth", "0, 0, 100"),
        new("RotWorm", "RWS_RotWorm", "", "pos.x,pos.y,depth", "0, 0, 100"),
        new("PebbsGrid", "RWS_PebbsGrid", "RotWormScene", "pos.x,pos.y,depth,scale,perpendicular", "0, 0, 100, 1, False")
    };
    internal static BackgroundElementSchema Find(string key, string scene) => All.FirstOrDefault(s => s.Name.Equals(key, StringComparison.OrdinalIgnoreCase) && s.Allowed(scene));
}
