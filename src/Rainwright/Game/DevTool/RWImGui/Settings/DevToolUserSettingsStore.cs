using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using BepInEx;
using BepInEx.Logging;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal enum DevToolActivityPlacement
{
    TopDocked = 0,
    LeftSidebar = 1
}

internal readonly struct DevToolPersistedWindowLayout
{
    internal DevToolPersistedWindowLayout(Num.Vector2 position, Num.Vector2 size)
    {
        Position = position;
        Size = size;
    }

    internal Num.Vector2 Position { get; }
    internal Num.Vector2 Size { get; }

    internal bool IsValid =>
        IsFinite(Position.X) &&
        IsFinite(Position.Y) &&
        IsFinite(Size.X) &&
        IsFinite(Size.Y) &&
        Size.X > 1f &&
        Size.Y > 1f;

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}

internal readonly struct DevToolPersistedWindowGroup
{
    internal DevToolPersistedWindowGroup(int id, string[] members)
    {
        Id = id;
        Members = members ?? Array.Empty<string>();
    }

    internal int Id { get; }
    internal string[] Members { get; }
}

/// <summary>
/// Process-independent preferences for the rebuilt DevTool.
///
/// ImGui consumer contexts are recreated when the game restarts and can also be rebuilt when the
/// active language/font context changes. Relying on an in-memory ImGui.ini therefore loses authored
/// panel geometry. This store keeps stable window IDs, splitter/group layout and presentation
/// preferences under BepInEx/config and is intentionally separate from room/editor document data.
/// </summary>
internal static class DevToolUserSettingsStore
{
    private const int CurrentVersion = 4;
    private const string FileName = "Rainwright.UI.xml";
    private const string LegacyFileName = "DryCycle.DevTool.UI.xml";
    private static readonly object Sync = new();
    private static readonly Dictionary<string, DevToolPersistedWindowLayout> Windows =
        new(StringComparer.Ordinal);
    private static readonly List<DevToolPersistedWindowGroup> Groups = new();
    private static readonly HashSet<string> ObjectFavourites = new(StringComparer.Ordinal);
    private static long objectFavouritesRevision;

    private static ManualLogSource log;
    private static bool loaded;
    private static bool dirty;
    private static bool sourceUnreadable;
    private static XElement preservedDocument;
    private static DateTime saveAfterUtc;

    private static float browserInspectorSplit = 0.23f;
    private static float soundBrowserSplit = 0.26f;
    private static float soundSceneSplit = 0.30f;
    private static DevToolActivityPlacement activityPlacement = DevToolActivityPlacement.TopDocked;

    internal static string SettingsPath =>
        Path.Combine(Paths.ConfigPath, FileName);

    internal static long ObjectFavouritesRevision { get { lock (Sync) return objectFavouritesRevision; } }

    internal static string[] GetObjectFavourites()
    { lock (Sync) return ObjectFavourites.OrderBy(type => type, StringComparer.Ordinal).ToArray(); }

    internal static bool SetObjectFavourite(string type, bool favourite)
    {
        if (string.IsNullOrEmpty(type)) return false;
        lock (Sync)
        {
            if (!loaded || sourceUnreadable)
            {
                log?.LogWarning("Object favourite could not be changed: UI preferences are not loaded or are unreadable.");
                return false;
            }
            if (favourite ? !ObjectFavourites.Add(type) : !ObjectFavourites.Remove(type)) return false;
            objectFavouritesRevision++;
            MarkDirtyLocked();
            FlushLocked();
            return true;
        }
    }

    // Historical DryCycle UI preferences are an import source only.
    // Never overwrite or rename this file, even when migration fails halfway.
    internal static string LegacySettingsPath =>
        Path.Combine(Paths.ConfigPath, LegacyFileName);

    internal static DevToolActivityPlacement ActivityPlacement
    {
        get
        {
            lock (Sync)
                return activityPlacement;
        }
    }

    internal static float BrowserInspectorSplit
    {
        get
        {
            lock (Sync)
                return browserInspectorSplit;
        }
    }

    internal static float SoundBrowserSplit
    {
        get
        {
            lock (Sync)
                return soundBrowserSplit;
        }
    }

    internal static float SoundSceneSplit
    {
        get
        {
            lock (Sync)
                return soundSceneSplit;
        }
    }

    internal static void Load(ManualLogSource logger)
    {
        lock (Sync)
        {
            log = logger;
            loaded = false;
            dirty = false;
            sourceUnreadable = false;
            preservedDocument = null;
            Windows.Clear();
            Groups.Clear();
            ObjectFavourites.Clear(); objectFavouritesRevision++;
            browserInspectorSplit = 0.23f;
            soundBrowserSplit = 0.26f;
            soundSceneSplit = 0.30f;
            activityPlacement = DevToolActivityPlacement.TopDocked;

            try
            {
                string path = SettingsPath;
                // A preexisting Rainwright file is always authoritative, including when
                // it is corrupt; never silently fall back to stale DryCycle preferences.
                bool legacyImport = !File.Exists(path) && File.Exists(LegacySettingsPath);
                if (legacyImport)
                    path = LegacySettingsPath;

                if (File.Exists(path))
                {
                    using XmlReader reader = XmlReader.Create(path, new XmlReaderSettings
                    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2097152 });
                    XDocument document = XDocument.Load(reader, LoadOptions.None);
                    XElement root = document.Root;
                    string rootName = root?.Name.LocalName;
                    if (legacyImport
                        ? rootName != "DryCycleDevToolUi"
                        : rootName != "RainwrightUi" && rootName != "DryCycleDevToolUi")
                        throw new InvalidDataException("Unexpected DevTool UI settings document root in " + path);

                    if (root != null)
                    {
                        int storedVersion =
                            int.TryParse(
                                Attribute(root, "version"),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out int parsedVersion)
                                ? parsedVersion
                                : 1;

                        if (storedVersion > CurrentVersion)
                            throw new InvalidDataException("A newer RainWright UI configuration must not be overwritten.");
                        preservedDocument = new XElement(root);
                        LoadPresentation(root.Element("Presentation"));
                        LoadWindows(root.Element("Windows"));
                        LoadGroups(root.Element("Groups"));
                        LoadObjectFavourites(root.Element("ObjectFavourites"));

                        if (storedVersion < 2)
                        {
                            // Sound's original unified workspace default was intentionally very
                            // wide. Drop only that saved geometry once so existing installs receive
                            // the new compact default; subsequent developer resizing remains sticky.
                            Windows.Remove("SoundWorkspace");
                            dirty = true;
                            saveAfterUtc = DateTime.UtcNow;
                        }

                        if (storedVersion < 3)
                        {
                            // World Workspace moved from a right-offset editor window to the
                            // full-width canvas-first composition below the persistent top chrome.
                            // Clear only its previous geometry once; future developer resizing stays
                            // sticky through the same custom window-layout store.
                            Windows.Remove("WorldWorkspace");
                            dirty = true;
                            saveAfterUtc = DateTime.UtcNow;
                        }

                        if (storedVersion < 4)
                        {
                            // The font window no longer duplicates RGBA numeric fields beside each
                            // swatch. Drop its previous large geometry once so existing installs pick
                            // up the compact layout; later manual resizing remains persistent.
                            Windows.Remove("Font");
                            dirty = true;
                            saveAfterUtc = DateTime.UtcNow;
                        }

                        if (legacyImport)
                        {
                            // Import only into the NEW file on the next normal flush;
                            // the historical DryCycle source remains byte-for-byte intact.
                            dirty = true;
                            saveAfterUtc = DateTime.UtcNow;
                            log?.LogInfo("Rainwright UI preferences imported from the read-only DryCycle legacy path.");
                        }
                    }
                }
            }
            catch (Exception error)
            {
                sourceUnreadable = true;
                dirty = false;
                log?.LogWarning(
                    "Rainwright UI preferences could not be loaded from the selected source; using safe defaults. " +
                    error);
                Windows.Clear();
                Groups.Clear();
                ObjectFavourites.Clear(); objectFavouritesRevision++;
                browserInspectorSplit = 0.23f;
                soundBrowserSplit = 0.26f;
                soundSceneSplit = 0.30f;
                activityPlacement = DevToolActivityPlacement.TopDocked;
            }

            loaded = true;
        }
    }

    internal static bool TryGetWindow(
        string id,
        out DevToolPersistedWindowLayout layout)
    {
        lock (Sync)
        {
            if (!loaded || !global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.RememberLayout || string.IsNullOrEmpty(id))
            {
                layout = default;
                return false;
            }

            return Windows.TryGetValue(id, out layout) &&
                   layout.IsValid;
        }
    }

    internal static void RememberWindow(
        string id,
        Num.Vector2 position,
        Num.Vector2 size)
    {
        if (!global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.RememberLayout || string.IsNullOrEmpty(id))
            return;

        DevToolPersistedWindowLayout next =
            new(position, size);
        if (!next.IsValid)
            return;

        lock (Sync)
        {
            if (!loaded)
                return;

            if (Windows.TryGetValue(id, out DevToolPersistedWindowLayout current) &&
                NearlyEqual(current.Position, next.Position) &&
                NearlyEqual(current.Size, next.Size))
                return;

            Windows[id] = next;
            MarkDirtyLocked();
        }
    }

    internal static DevToolPersistedWindowGroup[] GetWindowGroups()
    {
        if (!global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.RememberLayout) return Array.Empty<DevToolPersistedWindowGroup>();
        lock (Sync)
        {
            DevToolPersistedWindowGroup[] result =
                new DevToolPersistedWindowGroup[Groups.Count];

            for (int i = 0; i < Groups.Count; i++)
            {
                DevToolPersistedWindowGroup group = Groups[i];
                result[i] = new DevToolPersistedWindowGroup(
                    group.Id,
                    (string[])(group.Members?.Clone() ?? Array.Empty<string>()));
            }

            return result;
        }
    }

    internal static void RememberWindowGroups(
        DevToolPersistedWindowGroup[] groups)
    {
        if (!global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.RememberLayout) return;
        groups ??= Array.Empty<DevToolPersistedWindowGroup>();

        lock (Sync)
        {
            if (!loaded)
                return;

            if (GroupsEqualLocked(groups))
                return;

            Groups.Clear();
            for (int i = 0; i < groups.Length; i++)
            {
                DevToolPersistedWindowGroup group = groups[i];
                if (group.Id <= 0 ||
                    group.Members == null ||
                    group.Members.Length < 2)
                    continue;

                string[] members =
                    NormalizeMembers(group.Members);
                if (members.Length < 2)
                    continue;

                Groups.Add(
                    new DevToolPersistedWindowGroup(
                        group.Id,
                        members));
            }

            Groups.Sort((a, b) => a.Id.CompareTo(b.Id));
            MarkDirtyLocked();
        }
    }

    internal static void RememberActivityPlacement(DevToolActivityPlacement value)
    {
        if (!Enum.IsDefined(typeof(DevToolActivityPlacement), value))
            value = DevToolActivityPlacement.TopDocked;

        lock (Sync)
        {
            if (!loaded || activityPlacement == value)
                return;

            activityPlacement = value;
            MarkDirtyLocked();
        }
    }

    internal static void RememberBrowserInspectorSplit(float value)
    {
        value = Math.Max(0.08f, Math.Min(0.82f, value));

        lock (Sync)
        {
            if (!loaded ||
                Math.Abs(browserInspectorSplit - value) <= 0.0005f)
                return;

            browserInspectorSplit = value;
            MarkDirtyLocked();
        }
    }

    internal static void RememberSoundWorkspaceSplits(
        float browser,
        float scene)
    {
        browser = Math.Max(0.12f, Math.Min(0.60f, browser));
        scene = Math.Max(0.12f, Math.Min(0.60f, scene));

        lock (Sync)
        {
            if (!loaded ||
                (Math.Abs(soundBrowserSplit - browser) <= 0.0005f &&
                 Math.Abs(soundSceneSplit - scene) <= 0.0005f))
                return;

            soundBrowserSplit = browser;
            soundSceneSplit = scene;
            MarkDirtyLocked();
        }
    }

    internal static void CapturePresentationPreferences()
    {
        lock (Sync)
        {
            if (!loaded)
                return;

            // Preferences are emitted directly from DevToolUiSettings during serialization. Marking
            // dirty is handled by that class's mutation points; this method exists so a future
            // setting can join the same store without adding per-frame disk writes.
        }
    }

    internal static void NotifyPresentationChanged()
    {
        lock (Sync)
        {
            if (loaded)
                MarkDirtyLocked();
        }
    }

    internal static void FlushIfDue(bool interactionActive)
    {
        lock (Sync)
        {
            if (!loaded ||
                !dirty ||
                interactionActive ||
                DateTime.UtcNow < saveAfterUtc)
                return;

            FlushLocked();
        }
    }

    internal static void FlushNow()
    {
        lock (Sync)
        {
            if (!loaded || !dirty)
                return;

            FlushLocked();
        }
    }

    internal static XElement CaptureWorkspace()
    {
        lock (Sync) return new XElement("RainwrightUi", new XAttribute("version", CurrentVersion),
            BuildPresentationElement(), BuildWindowsElement(), BuildGroupsElement());
    }

    internal static void ApplyWorkspace(XElement settings)
    {
        lock (Sync)
        {
            if (sourceUnreadable) throw new InvalidDataException("Repair and reload the unreadable UI settings before applying a workspace.");
            RestoreWorkspaceMemory(settings);
            dirty = true;
            if (!FlushLocked()) throw new IOException("Workspace UI preferences were not saved. See RainWright log and recovery staging file.");
        }
    }

    internal static void RestoreWorkspaceMemory(XElement settings)
    {
        lock (Sync)
        {
            LoadPresentation(settings.Element("Presentation"));
            Windows.Clear(); Groups.Clear();
            LoadWindows(settings.Element("Windows")); LoadGroups(settings.Element("Groups"));
            // Used by failed-transaction recovery: do not schedule the rejected draft for retry.
            dirty = false;
        }
    }

    private static void LoadPresentation(XElement element)
    {
        if (element == null)
            return;

        activityPlacement =
            ParseEnum(
                Attribute(element, "toolPlacement"),
                DevToolActivityPlacement.TopDocked);

        browserInspectorSplit =
            ReadFloat(
                element,
                "browserInspectorSplit",
                browserInspectorSplit,
                0.08f,
                0.82f);
        soundBrowserSplit =
            ReadFloat(
                element,
                "soundBrowserSplit",
                soundBrowserSplit,
                0.12f,
                0.60f);
        soundSceneSplit =
            ReadFloat(
                element,
                "soundSceneSplit",
                soundSceneSplit,
                0.12f,
                0.60f);

        DevToolUiLanguage language =
            ParseEnum(
                Attribute(element, "language"),
                DevToolUiLanguage.English);
        DevToolScenePlacement scenePlacement =
            ParseEnum(
                Attribute(element, "scenePlacement"),
                DevToolScenePlacement.Center);

        float chineseFontSize =
            ReadFloat(
                element,
                "chineseFontSize",
                DevToolUiSettings.DefaultChineseFontSize,
                12f,
                72f);
        float englishFontSize =
            ReadFloat(
                element,
                "englishFontSize",
                DevToolUiSettings.DefaultFontSize,
                12f,
                72f);
        int englishFontWeight =
            ReadInt(
                element,
                "englishFontWeight",
                DevToolUiSettings.DefaultFontWeight,
                100,
                900);

        Num.Vector4 textColor =
            ReadColor(
                element,
                "text",
                DevToolUiSettings.TextColor);
        Num.Vector4 disabledTextColor =
            ReadColor(
                element,
                "muted",
                DevToolUiSettings.DisabledTextColor);

        DevToolUiSettings.RestorePersistedPreferences(
            language,
            scenePlacement,
            chineseFontSize,
            englishFontSize,
            englishFontWeight,
            textColor,
            disabledTextColor);
    }

    private static void LoadWindows(XElement element)
    {
        if (element == null)
            return;

        foreach (XElement window in element.Elements("Window"))
        {
            string id =
                Attribute(window, "id");
            if (string.IsNullOrWhiteSpace(id))
                continue;

            DevToolPersistedWindowLayout layout =
                new(
                    new Num.Vector2(
                        ReadFloat(window, "x", 0f, -100000f, 100000f),
                        ReadFloat(window, "y", 0f, -100000f, 100000f)),
                    new Num.Vector2(
                        ReadFloat(window, "w", 0f, 0f, 100000f),
                        ReadFloat(window, "h", 0f, 0f, 100000f)));

            if (layout.IsValid)
                Windows[id.Trim()] = layout;
        }
    }

    private static void LoadGroups(XElement element)
    {
        if (element == null)
            return;

        foreach (XElement group in element.Elements("Group"))
        {
            int id =
                ReadInt(
                    group,
                    "id",
                    0,
                    1,
                    int.MaxValue);
            if (id <= 0)
                continue;

            List<string> members = new();
            foreach (XElement member in group.Elements("Member"))
            {
                string value =
                    Attribute(member, "id");
                if (!string.IsNullOrWhiteSpace(value) &&
                    !members.Contains(value.Trim()))
                    members.Add(value.Trim());
            }

            if (members.Count >= 2)
            {
                members.Sort(StringComparer.OrdinalIgnoreCase);
                Groups.Add(
                    new DevToolPersistedWindowGroup(
                        id,
                        members.ToArray()));
            }
        }

        Groups.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private static bool FlushLocked()
    {
        // Keep the unreadable original intact until a successful explicit reload.
        if (sourceUnreadable) return false;
        string temporary = SettingsPath + ".pending-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string directory =
                Paths.ConfigPath;
            Directory.CreateDirectory(directory);

            XElement root =
                new(
                    "RainwrightUi",
                    new XAttribute("version", CurrentVersion),
                    BuildPresentationElement(),
                    BuildWindowsElement(),
                    BuildGroupsElement(),
                    BuildObjectFavouritesElement());

            PreserveUnknownFields(preservedDocument, root);

            XDocument document =
                new(
                    new XDeclaration("1.0", "utf-8", null),
                    root);

            string path =
                SettingsPath;

            File.WriteAllText(
                temporary,
                document.ToString(SaveOptions.DisableFormatting),
                new UTF8Encoding(false));

            // Never delete the only good preferences file before promoting the replacement.
            if (File.Exists(path))
                DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);

            preservedDocument = new XElement(root);
            dirty = false;
            return true;
        }
        catch (Exception error)
        {
            saveAfterUtc =
                DateTime.UtcNow.AddSeconds(2);
            log?.LogWarning(
                "RainWright UI preferences could not be saved; will retry. Recovery staging path: " +
                temporary + ". " + error);
            return false;
        }
    }

    // Only replace schema-owned children. Preserve extension attributes and elements
    // recursively on records with the same stable ID; removed Window/Group records stay removed.
    private static void PreserveUnknownFields(XElement original, XElement replacement)
    {
        if (original == null) return;
        foreach (XAttribute attribute in original.Attributes())
            if (replacement.Attribute(attribute.Name) == null)
                replacement.Add(new XAttribute(attribute));
        string[] owned = replacement.Name.LocalName switch
        {
            "RainwrightUi" => new[] { "Presentation", "Windows", "Groups", "ObjectFavourites" },
            "ObjectFavourites" => new[] { "Object" },
            "Windows" => new[] { "Window" },
            "Groups" => new[] { "Group" },
            "Group" => new[] { "Member" },
            _ => Array.Empty<string>()
        };
        foreach (XElement element in replacement.Elements().ToArray())
        {
            string id = (string)element.Attribute("id");
            XElement previous = original.Elements(element.Name)
                .FirstOrDefault(x => (string)x.Attribute("id") == id);
            PreserveUnknownFields(previous, element);
        }
        foreach (XElement element in original.Elements())
            if (element.Name.Namespace != XNamespace.None || !owned.Contains(element.Name.LocalName))
                replacement.Add(new XElement(element));
    }

    private static void LoadObjectFavourites(XElement element)
    {
        if (element == null) return;
        foreach (var item in element.Elements("Object"))
        {
            string type = (string)item.Attribute("id");
            if (!string.IsNullOrEmpty(type)) ObjectFavourites.Add(type);
        }
    }

    private static XElement BuildObjectFavouritesElement() => new("ObjectFavourites",
        ObjectFavourites.OrderBy(type => type, StringComparer.Ordinal).Select(type => new XElement("Object", new XAttribute("id", type))));

    private static XElement BuildPresentationElement()
    {
        Num.Vector4 text =
            DevToolUiSettings.TextColor;
        Num.Vector4 muted =
            DevToolUiSettings.DisabledTextColor;

        return new XElement(
            "Presentation",
            new XAttribute("language", DevToolUiSettings.Language),
            new XAttribute("scenePlacement", DevToolUiSettings.ScenePlacement),
            new XAttribute("toolPlacement", activityPlacement),
            new XAttribute("browserInspectorSplit", F(browserInspectorSplit)),
            new XAttribute("soundBrowserSplit", F(soundBrowserSplit)),
            new XAttribute("soundSceneSplit", F(soundSceneSplit)),
            new XAttribute("chineseFontSize", F(DevToolUiSettings.PersistedChineseFontSize)),
            new XAttribute("englishFontSize", F(DevToolUiSettings.PersistedEnglishFontSize)),
            new XAttribute("englishFontWeight", DevToolUiSettings.PersistedEnglishFontWeight),
            new XAttribute("textR", F(text.X)),
            new XAttribute("textG", F(text.Y)),
            new XAttribute("textB", F(text.Z)),
            new XAttribute("textA", F(text.W)),
            new XAttribute("mutedR", F(muted.X)),
            new XAttribute("mutedG", F(muted.Y)),
            new XAttribute("mutedB", F(muted.Z)),
            new XAttribute("mutedA", F(muted.W)));
    }

    private static XElement BuildWindowsElement()
    {
        XElement result =
            new("Windows");

        List<string> ids =
            new(Windows.Keys);
        ids.Sort(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < ids.Count; i++)
        {
            string id =
                ids[i];
            DevToolPersistedWindowLayout layout =
                Windows[id];

            result.Add(
                new XElement(
                    "Window",
                    new XAttribute("id", id),
                    new XAttribute("x", F(layout.Position.X)),
                    new XAttribute("y", F(layout.Position.Y)),
                    new XAttribute("w", F(layout.Size.X)),
                    new XAttribute("h", F(layout.Size.Y))));
        }

        return result;
    }

    private static XElement BuildGroupsElement()
    {
        XElement result =
            new("Groups");

        for (int i = 0; i < Groups.Count; i++)
        {
            DevToolPersistedWindowGroup group =
                Groups[i];
            XElement element =
                new(
                    "Group",
                    new XAttribute("id", group.Id));

            string[] members =
                (string[])(group.Members?.Clone() ?? Array.Empty<string>());
            Array.Sort(
                members,
                StringComparer.OrdinalIgnoreCase);

            for (int member = 0; member < members.Length; member++)
            {
                if (!string.IsNullOrWhiteSpace(members[member]))
                {
                    element.Add(
                        new XElement(
                            "Member",
                            new XAttribute(
                                "id",
                                members[member])));
                }
            }

            if (element.HasElements)
                result.Add(element);
        }

        return result;
    }

    private static Num.Vector4 ReadColor(
        XElement element,
        string prefix,
        Num.Vector4 fallback) =>
        new(
            ReadFloat(element, prefix + "R", fallback.X, 0f, 1f),
            ReadFloat(element, prefix + "G", fallback.Y, 0f, 1f),
            ReadFloat(element, prefix + "B", fallback.Z, 0f, 1f),
            ReadFloat(element, prefix + "A", fallback.W, 0f, 1f));

    private static float ReadFloat(
        XElement element,
        string name,
        float fallback,
        float minimum,
        float maximum)
    {
        string text =
            Attribute(element, name);
        if (!float.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out float value) ||
            float.IsNaN(value) ||
            float.IsInfinity(value))
            return fallback;

        return Math.Max(
            minimum,
            Math.Min(
                maximum,
                value));
    }

    private static int ReadInt(
        XElement element,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        string text =
            Attribute(element, name);
        if (!int.TryParse(
                text,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value))
            return fallback;

        return Math.Max(
            minimum,
            Math.Min(
                maximum,
                value));
    }

    private static T ParseEnum<T>(
        string value,
        T fallback)
        where T : struct
    {
        return Enum.TryParse(value, true, out T parsed)
            ? parsed
            : fallback;
    }

    private static string Attribute(
        XElement element,
        string name) =>
        element?.Attribute(name)?.Value ??
        string.Empty;

    private static string F(float value) =>
        value.ToString(
            "R",
            CultureInfo.InvariantCulture);

    private static bool GroupsEqualLocked(
        DevToolPersistedWindowGroup[] candidate)
    {
        List<DevToolPersistedWindowGroup> normalized = new();
        for (int i = 0; i < candidate.Length; i++)
        {
            DevToolPersistedWindowGroup group = candidate[i];
            if (group.Id <= 0 ||
                group.Members == null)
                continue;

            string[] members =
                NormalizeMembers(group.Members);
            if (members.Length >= 2)
            {
                normalized.Add(
                    new DevToolPersistedWindowGroup(
                        group.Id,
                        members));
            }
        }

        normalized.Sort((a, b) => a.Id.CompareTo(b.Id));
        if (normalized.Count != Groups.Count)
            return false;

        for (int i = 0; i < normalized.Count; i++)
        {
            if (normalized[i].Id != Groups[i].Id)
                return false;

            string[] a =
                normalized[i].Members;
            string[] b =
                Groups[i].Members;
            if (a.Length != b.Length)
                return false;

            for (int member = 0; member < a.Length; member++)
            {
                if (!string.Equals(
                        a[member],
                        b[member],
                        StringComparison.Ordinal))
                    return false;
            }
        }

        return true;
    }

    private static string[] NormalizeMembers(
        string[] members)
    {
        HashSet<string> unique =
            new(StringComparer.Ordinal);

        for (int i = 0; i < members.Length; i++)
        {
            string value =
                members[i]?.Trim();
            if (!string.IsNullOrEmpty(value))
                unique.Add(value);
        }

        string[] result =
            new string[unique.Count];
        unique.CopyTo(result);
        Array.Sort(
            result,
            StringComparer.OrdinalIgnoreCase);
        return result;
    }

    private static bool NearlyEqual(
        Num.Vector2 a,
        Num.Vector2 b) =>
        Math.Abs(a.X - b.X) <= 0.25f &&
        Math.Abs(a.Y - b.Y) <= 0.25f;

    private static void MarkDirtyLocked()
    {
        dirty = true;
        saveAfterUtc =
            DateTime.UtcNow.AddMilliseconds(450);
    }
}
