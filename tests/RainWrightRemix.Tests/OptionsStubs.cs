using System.Xml.Linq;
using Menu.Remix.MixedUI;
using UnityEngine;
using DryCycle.DevUI.DevTool.Core;

// Controls exercise the real Options.Initialize layout. Native ConfigHolder
// and ranges are verified separately against the real game assembly.
public abstract class ConfigAcceptableBase { public abstract object Clamp(object value); }
public class ConfigAcceptableRange<T>(T min, T max) : ConfigAcceptableBase where T : IComparable
{
    public override object Clamp(object value) => min.CompareTo(value) > 0 ? min : max.CompareTo(value) < 0 ? max : value;
}
public class Configurable<T>(string key, T initial, ConfigAcceptableBase acceptable)
{
    public string key = key;
    public ConfigAcceptableBase acceptable = acceptable;
    private T current = initial;
    public T Value { get => current; set => current = acceptable == null ? value : (T)acceptable.Clamp(value); }
}
public class ConfigHolder
{
    public Dictionary<string, object> bindings = new();
    public Configurable<T> Bind<T>(string key, T initial, ConfigAcceptableBase acceptable = null)
    {
        if (key.Any(character => !char.IsLetterOrDigit(character) && character != '_')) throw new ArgumentException("Invalid native config key.");
        var result = new Configurable<T>(key, initial, acceptable); bindings.Add(key, result); return result;
    }
}
public static class MachineConnector
{
    public static OptionInterface Registered;
    public static OptionInterface GetRegisteredOI(string id) => Registered;
    public static bool SetRegisteredOI(string id, OptionInterface options) { Registered = options; return true; }
}
namespace BepInEx { public static class Paths { public static string ConfigPath => "FixtureConfig"; } }
namespace Menu.Remix.MixedUI
{
    public partial class OptionInterface
    {
        public ConfigHolder config = new();
        public event Action OnActivate, OnConfigChanged, OnDeactivate, OnUnload;
        public void Activate() => OnActivate?.Invoke();
        public void ChangeConfig() => OnConfigChanged?.Invoke();
        public void Deactivate() => OnDeactivate?.Invoke();
        public void UnloadOwner() => OnUnload?.Invoke();
        public virtual void Initialize() { }
        public void ShowConfigs() { }
    }
    public class OpUpdown : UIelement
    {
        public OpUpdown(Configurable<int> config, Vector2 pos, float width) : base(pos, new(width, 30))
        {
            // The actual constructor computes Abs(min) before any post-creation
            // SetRange could run. The fixture preserves this failure behavior.
            int min = config.acceptable == null ? int.MinValue : (int)config.acceptable.Clamp(int.MinValue);
            int max = config.acceptable == null ? int.MaxValue : (int)config.acceptable.Clamp(int.MaxValue);
            _ = Math.Abs(min); _ = Math.Abs(max);
        }
        public OpUpdown(Configurable<float> config, Vector2 pos, float width, byte decimals) : base(pos, new(width, 30)) { }
    }
    public class OpColorPicker(Configurable<Color> config, Vector2 pos) : UIelement(pos, new(200, 150)) { public Configurable<Color> config = config; }
    public class OpCheckBox(Configurable<bool> config, Vector2 pos) : UIelement(pos, new(24, 24)) { public Configurable<bool> config = config; }
    public class OpTextBox : UIelement
    {
        private Configurable<string> config;
        public string value { get => config.Value; set => config.Value = value; }
        public OpTextBox(Configurable<string> config, Vector2 pos, float width) : base(pos, new(width, 30)) { this.config = config; }
    }
    public class ListItem(string name, string display, int index)
    { public string name = name, display = display; public int index = index; }
    public class OpComboBox : UIelement
    {
        private Configurable<string> config;
        private List<ListItem> items;
        public string value => config.Value;
        public OpComboBox(Configurable<string> config, Vector2 pos, float width, string[] names)
            : this(config, pos, width, names.Select((name, index) => new ListItem(name, name, index)).ToList()) { }
        public OpComboBox(Configurable<string> config, Vector2 pos, float width, List<ListItem> items) : base(pos, new(width, 30))
        { this.config = config; this.items = items; }
        public ListItem[] GetItemList() => items.ToArray();
        public void AddItems(bool alphabetical, ListItem[] extra) => items.AddRange(extra);
    }
}
namespace DryCycle.DevUI.DevTool.RWImGui
{
    public enum DevToolActivityPlacement { TopDocked, LeftSidebar }
    public static class DevToolUiSettings
    {
        public static float PersistedChineseFontSize = 21, PersistedEnglishFontSize = 18;
        public static int PersistedEnglishFontWeight = 400;
        public static System.Numerics.Vector4 TextColor = new(.94f), DisabledTextColor = new(.76f);
    }
}
namespace DryCycle.DevUI.DevTool.Core
{
    public static class EditorDiagnostics { public static void Info(string text) { } }
}
namespace DryCycle.DevUI.DevTool.Personalization
{
    public static class RainWrightWorkspaceRuntime
    {
        public static string Status = "", ExportDirectory = "FixtureExports";
        public static RainWrightWorkspacePreset Capture(string name = "Fixture", string author = "", string description = "") => new()
        {
            Name = name, Author = author, Description = description,
            Ui = new XElement("Workspace", new XElement("Presentation", new XAttribute("language", "English"),
                new XAttribute("toolPlacement", "TopDocked"), new XAttribute("scenePlacement", "Center")))
        };
        public static List<KeyValuePair<string, string>> ListPresets() => new() { new("Room", "Rooms"), new("World", "World"), new("Sound", "Sound") };
        public static void Run(Action action) => action();
        public static void Apply(RainWrightWorkspacePreset preset) { }
        public static void ResetLayout() { }
        public static void OpenDirectory(string path) { }
        public static RainWrightWorkspacePreset ReadPreset(string id) => Capture();
        public static string SavePreset(RainWrightWorkspacePreset preset, bool share) => "Fixture";
        public static string Import(string path) => "Fixture";
    }
}
