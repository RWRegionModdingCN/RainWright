using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace DryCycle.DevUI.DevTool.Core;

public sealed class RainWrightPersonalPreferences
{
    public bool ReverseZoom;
    public float DragSensitivity = 1f;
    public bool GridSnap;
    public int GridSize = 20;
    public string DefaultPage = "Room";
    public bool RememberLayout = true;
    public bool RecoveryPrompt = true;
    public int SnapshotCount = 5;
    public float WindowAlpha = .52f;
    public float RemixGlassOpacity = .44f;
    public float RemixReflection = 1f;
    public int RemixSectionGap = 20;
    public bool RemixGildedTitle = true;
    public bool RemixSectionHeadings = true;
    public readonly Dictionary<string, string> Shortcuts = Defaults();
    public static Dictionary<string, string> Defaults() => new(StringComparer.Ordinal)
    {
        ["save"] = "Ctrl+S", ["undo"] = "Ctrl+Z", ["redo-shift"] = "Ctrl+Shift+Z",
        ["redo-y"] = "Ctrl+Y", ["delete"] = "X", ["focus"] = "Tab",
        ["browser"] = "Ctrl+B", ["inspector"] = "Ctrl+I", ["presentation"] = "Ctrl+Shift+U",
        ["objects-duplicate"] = "Ctrl+D", ["window-group"] = "Ctrl+G", ["top-status"] = ""
    };
    public RainWrightPersonalPreferences Copy() => FromXml(ToXml());
    public XElement ToXml()
    {
        return new XElement("Preferences", new XAttribute("reverseZoom", ReverseZoom),
            new XAttribute("dragSensitivity", DragSensitivity), new XAttribute("gridSnap", GridSnap),
            new XAttribute("gridSize", GridSize), new XAttribute("defaultPage", DefaultPage),
            new XAttribute("rememberLayout", RememberLayout), new XAttribute("recoveryPrompt", RecoveryPrompt),
            new XAttribute("snapshotCount", SnapshotCount), new XAttribute("windowAlpha", WindowAlpha),
            new XAttribute("remixGlassOpacity", RemixGlassOpacity), new XAttribute("remixReflection", RemixReflection),
            new XAttribute("remixSectionGap", RemixSectionGap), new XAttribute("remixGildedTitle", RemixGildedTitle),
            new XAttribute("remixSectionHeadings", RemixSectionHeadings),
            Shortcuts.OrderBy(x => x.Key).Select(x => new XElement("Shortcut",
                new XAttribute("id", x.Key), new XAttribute("chord", x.Value))));
    }
    public static RainWrightPersonalPreferences FromXml(XElement root)
    {
        if (root == null || root.Name != "Preferences") throw new InvalidDataException("Missing personal preferences.");
        var value = new RainWrightPersonalPreferences
        {
            ReverseZoom = Bool(root, "reverseZoom"), DragSensitivity = Float(root, "dragSensitivity", .25f, 3f),
            GridSnap = Bool(root, "gridSnap"), GridSize = Integer(root, "gridSize", 1, 200),
            DefaultPage = (string)root.Attribute("defaultPage"), RememberLayout = Bool(root, "rememberLayout"),
            RecoveryPrompt = Bool(root, "recoveryPrompt"), SnapshotCount = Integer(root, "snapshotCount", 1, 30),
            WindowAlpha = Float(root, "windowAlpha", .15f, 1f),
            RemixGlassOpacity = root.Attribute("remixGlassOpacity") == null ? .44f : Float(root, "remixGlassOpacity", .15f, .85f),
            RemixReflection = root.Attribute("remixReflection") == null ? 1f : Float(root, "remixReflection", 0f, 1f),
            RemixSectionGap = root.Attribute("remixSectionGap") == null ? 20 : Integer(root, "remixSectionGap", 8, 40),
            RemixGildedTitle = root.Attribute("remixGildedTitle") == null || Bool(root, "remixGildedTitle"),
            RemixSectionHeadings = root.Attribute("remixSectionHeadings") == null || Bool(root, "remixSectionHeadings")
        };
        if (!new[] { "Room", "Objects", "Sound", "Triggers", "Map", "Dialog", "Relationships" }.Contains(value.DefaultPage))
            throw new InvalidDataException("Invalid default editor page.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chords = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement key in root.Elements("Shortcut"))
        {
            string id = (string)key.Attribute("id"), chord = NormalizeChord((string)key.Attribute("chord"));
            if (id == null || !value.Shortcuts.ContainsKey(id) || !seen.Add(id)) throw new InvalidDataException("Unknown or duplicate shortcut: " + id);
            if (id != "delete" && chord == "Delete") throw new InvalidDataException("Delete is reserved for the alternate delete action.");
            if (chord.Length > 0 && !chords.Add(chord)) throw new InvalidDataException("Shortcut conflict: " + chord);
            value.Shortcuts[id] = chord;
        }
        // Missing bindings are defaults; they still participate in conflict checking.
        var bound = value.Shortcuts.Values.Where(chord => chord.Length > 0).ToArray();
        if (bound.Distinct(StringComparer.Ordinal).Count() != bound.Length)
            throw new InvalidDataException("Shortcut conflicts with another action.");
        return value;
    }
    public static string NormalizeChord(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var parts = (text ?? "").Split('+').Select(x => x.Trim()).ToArray();
        if (parts.Length < 1 || parts.Length > 4) throw new InvalidDataException("Invalid shortcut: " + text);
        bool ctrl = false, shift = false, alt = false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i].Equals("Ctrl", StringComparison.OrdinalIgnoreCase) && !ctrl) ctrl = true;
            else if (parts[i].Equals("Shift", StringComparison.OrdinalIgnoreCase) && !shift) shift = true;
            else if (parts[i].Equals("Alt", StringComparison.OrdinalIgnoreCase) && !alt) alt = true;
            else throw new InvalidDataException("Invalid shortcut modifier: " + text);
        }
        string key = parts[parts.Length - 1].ToUpperInvariant();
        if (key.Length == 1 && key[0] >= 'A' && key[0] <= 'Z') { }
        else if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.Substring(1), out int f) && f >= 1 && f <= 12) { key = "F" + f; }
        else if (key == "TAB") key = "Tab";
        else if (key == "DELETE") key = "Delete";
        else if (key == "SPACE") key = "Space";
        else throw new InvalidDataException("Use A-Z, F1-F12, Tab, Delete or Space: " + text);
        if (key == "O" || key == "H") throw new InvalidDataException("O and H are reserved by Rain World DevTools.");
        return (ctrl ? "Ctrl+" : "") + (shift ? "Shift+" : "") + (alt ? "Alt+" : "") + key;
    }
    internal static bool Bool(XElement root, string key) => bool.TryParse((string)root.Attribute(key), out bool v)
        ? v : throw new InvalidDataException("Invalid boolean: " + key);
    internal static int Integer(XElement root, string key, int min, int max) =>
        int.TryParse((string)root.Attribute(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v >= min && v <= max
        ? v : throw new InvalidDataException("Invalid range: " + key);
    internal static float Float(XElement root, string key, float min, float max) =>
        float.TryParse((string)root.Attribute(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) && !float.IsNaN(v) && v >= min && v <= max
        ? v : throw new InvalidDataException("Invalid range: " + key);
}

public static class RainWrightPersonalSettings
{
    private static RainWrightPersonalPreferences current = new();
    // Callers receive copies; the published frame snapshot is never mutated in place.
    public static RainWrightPersonalPreferences Capture() => Volatile.Read(ref current).Copy();
    public static bool ReverseZoom => Volatile.Read(ref current).ReverseZoom;
    public static float DragSensitivity => Volatile.Read(ref current).DragSensitivity;
    public static bool RememberLayout => Volatile.Read(ref current).RememberLayout;
    public static bool RecoveryPrompt => Volatile.Read(ref current).RecoveryPrompt;
    public static int SnapshotCount => Volatile.Read(ref current).SnapshotCount;
    public static float WindowAlpha => Volatile.Read(ref current).WindowAlpha;
    public static float RemixGlassOpacity => Volatile.Read(ref current).RemixGlassOpacity;
    public static float RemixReflection => Volatile.Read(ref current).RemixReflection;
    public static int RemixSectionGap => Volatile.Read(ref current).RemixSectionGap;
    public static bool RemixGildedTitle => Volatile.Read(ref current).RemixGildedTitle;
    public static bool RemixSectionHeadings => Volatile.Read(ref current).RemixSectionHeadings;
    public static string DefaultPage => Volatile.Read(ref current).DefaultPage;
    public static string Chord(string id) => Volatile.Read(ref current).Shortcuts[id];
    public static float Snap(float value)
    {
        var prefs = Volatile.Read(ref current);
        return prefs.GridSnap ? (float)Math.Round(value / prefs.GridSize) * prefs.GridSize : value;
    }
    public static void Publish(RainWrightPersonalPreferences prefs) => Volatile.Write(ref current, prefs.Copy());
    public static XElement ReadXml(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 512 * 1024 });
        return XDocument.Load(reader).Root ?? throw new InvalidDataException("Empty settings document.");
    }
    public static void WriteXml(string path, XElement data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string staged = path + ".pending-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(staged, new XDocument(data).ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(staged, path, path + ".bak");
            else File.Move(staged, path);
        }
        catch (Exception error) { throw new IOException("Settings write failed; recovery file: " + staged, error); }
    }
}

public sealed class RainWrightWorkspacePreset
{
    public string Name = "My workspace";
    public string Author = "";
    public string Description = "";
    public RainWrightPersonalPreferences Preferences = new();
    public XElement Ui = new("RainwrightUi", new XAttribute("version", 4));
    public bool AutoSave = true;
    public int AutoSaveSeconds = 150;
    public bool AllowZ, AllowS, AllowY;
    public XElement ToXml() => new("RainWrightPreset", new XAttribute("version", 1),
        new XAttribute("name", Name), new XAttribute("author", Author), new XAttribute("description", Description),
        Preferences.ToXml(), new XElement(Ui), new XElement("AutoSave", new XAttribute("enabled", AutoSave),
            new XAttribute("seconds", AutoSaveSeconds)), new XElement("Input", new XAttribute("allowZ", AllowZ),
            new XAttribute("allowS", AllowS), new XAttribute("allowY", AllowY)));
    public static RainWrightWorkspacePreset FromXml(XElement root)
    {
        if (root?.Name != "RainWrightPreset" || (string)root.Attribute("version") != "1") throw new InvalidDataException("Unsupported RainWright preset format/version.");
        string name = (string)root.Attribute("name"), author = (string)root.Attribute("author") ?? "", description = (string)root.Attribute("description") ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || author.Length > 120 || description.Length > 1000)
            throw new InvalidDataException("Invalid preset name, author or description.");
        if (root.Elements("Preferences").Count() != 1 || root.Elements("RainwrightUi").Count() != 1 ||
            root.Elements("AutoSave").Count() != 1 || root.Elements("Input").Count() != 1 || root.Elements().Count() != 4)
            throw new InvalidDataException("Preset must contain only UI and personal preferences.");
        XElement ui = root.Element("RainwrightUi"), save = root.Element("AutoSave"), input = root.Element("Input");
        if ((string)ui.Attribute("version") != "4" || ui.Descendants().Count() > 1024 ||
            ui.Descendants().Any(x => !new[] { "Presentation", "Windows", "Window", "Groups", "Group", "Member" }.Contains(x.Name.LocalName)))
            throw new InvalidDataException("Invalid UI layout schema.");
        ValidateUi(ui);
        foreach (XElement window in ui.Descendants("Window"))
        {
            if (((string)window.Attribute("id") ?? "").Length > 120) throw new InvalidDataException("Invalid window ID.");
            foreach (string key in new[] { "x", "y", "w", "h" }) RainWrightPersonalPreferences.Float(window, key, key == "x" || key == "y" ? -65536 : 2, 65536);
        }
        return new RainWrightWorkspacePreset { Name = name.Trim(), Author = author, Description = description,
            Preferences = RainWrightPersonalPreferences.FromXml(root.Element("Preferences")), Ui = new XElement(ui),
            AutoSave = RainWrightPersonalPreferences.Bool(save, "enabled"), AutoSaveSeconds = RainWrightPersonalPreferences.Integer(save, "seconds", 30, 1800),
            AllowZ = RainWrightPersonalPreferences.Bool(input, "allowZ"), AllowS = RainWrightPersonalPreferences.Bool(input, "allowS"), AllowY = RainWrightPersonalPreferences.Bool(input, "allowY") };
    }
    private static void ValidateUi(XElement ui)
    {
        foreach (var element in ui.DescendantsAndSelf())
        {
            string[] allowed = element.Name.LocalName switch
            {
                "RainwrightUi" => new[] { "version" }, "Window" => new[] { "id", "x", "y", "w", "h" },
                "Group" => new[] { "id" }, "Member" => new[] { "id" },
                "Windows" or "Groups" => Array.Empty<string>(), "Presentation" => null,
                _ => throw new InvalidDataException("Unknown UI element.")
            };
            if (element.Name.Namespace != XNamespace.None || element.Attributes().Any(a => a.Name.Namespace != XNamespace.None || allowed != null && !allowed.Contains(a.Name.LocalName)))
                throw new InvalidDataException("Unexpected data in UI layout.");
            if ((element.Name == "Window" || element.Name == "Member") && element.HasElements) throw new InvalidDataException("Unexpected nested layout data.");
            if (element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) throw new InvalidDataException("Unexpected text in UI layout.");
        }
        if (ui.Elements().Any(e => e.Name != "Presentation" && e.Name != "Windows" && e.Name != "Groups") ||
            ui.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate or unknown UI section.");
        var presentation = ui.Element("Presentation");
        if (presentation != null)
        {
            var ranges = new Dictionary<string, float[]> {
                ["chineseFontSize"] = new[] { 12f, 72f }, ["englishFontSize"] = new[] { 12f, 72f },
                ["englishFontWeight"] = new[] { 100f, 900f }, ["browserInspectorSplit"] = new[] { .08f, .82f },
                ["soundBrowserSplit"] = new[] { .12f, .60f }, ["soundSceneSplit"] = new[] { .12f, .60f } };
            foreach (string prefix in new[] { "text", "muted" }) foreach (string channel in new[] { "R", "G", "B", "A" }) ranges[prefix + channel] = new[] { 0f, 1f };
            foreach (var attr in presentation.Attributes())
            {
                string key = attr.Name.LocalName;
                if (ranges.TryGetValue(key, out float[] range)) RainWrightPersonalPreferences.Float(presentation, key, range[0], range[1]);
                else if (key == "language" && new[] { "Chinese", "English" }.Contains(attr.Value)) { }
                else if (key == "scenePlacement" && new[] { "Left", "Center" }.Contains(attr.Value)) { }
                else if (key == "toolPlacement" && new[] { "TopDocked", "LeftSidebar" }.Contains(attr.Value)) { }
                else throw new InvalidDataException("Invalid UI preference: " + key);
            }
            if (presentation.HasElements) throw new InvalidDataException("Unexpected presentation data.");
        }
        var windows = ui.Element("Windows");
        if (windows != null && (windows.Elements().Any(e => e.Name != "Window") || windows.Elements().GroupBy(e => (string)e.Attribute("id"), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)))
            throw new InvalidDataException("Invalid or duplicate window layout.");
        var groups = ui.Element("Groups");
        if (groups != null)
        {
            var ids = new HashSet<int>(); var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in groups.Elements())
            {
                if (group.Name != "Group" || !ids.Add(RainWrightPersonalPreferences.Integer(group, "id", 1, int.MaxValue)) || group.Elements().Count() < 2 || group.Elements().Any(e => e.Name != "Member"))
                    throw new InvalidDataException("Invalid window group.");
                foreach (var member in group.Elements())
                    if (!members.Add(ValidId(member))) throw new InvalidDataException("Window occurs in multiple groups.");
            }
        }
        foreach (var window in ui.Descendants("Window")) ValidId(window);
    }
    private static string ValidId(XElement element)
    {
        string id = (string)element.Attribute("id");
        if (string.IsNullOrWhiteSpace(id) || id.Length > 120 || id.Contains("/") || id.Contains("\\") || id.Contains(":")) throw new InvalidDataException("Invalid window ID.");
        return id;
    }
}
