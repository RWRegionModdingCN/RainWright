// Geometry/lifecycle fixture only. Actual font rendering requires the game;
// production is also compiled separately against the real Remix assemblies.
using System.Text;

public enum FLabelAlignment { Left, Center, Right }
public static class ModManager
{
    public sealed class Mod
    {
        public string id;
        public string version = "0.1.9", authors = "NR";
        public bool hideVersion, DLCMissing;
        public string[] requirementsNames = new[] { "RWImGUI 1.12.0" };
        public string LocalizedName => Menu.Remix.MixedUI.OptionInterface.Translate(id + "-name");
        public string LocalizedDescription => Menu.Remix.MixedUI.OptionInterface.Translate(id + "-description");
    }
    public static List<Mod> InstalledMods = new() { new Mod { id = "rainwright" } };
    public static HashSet<string> FailedRequirementIds = new();
}
namespace UnityEngine
{
    public struct Vector2(float x, float y)
    {
        public float x = x, y = y;
        public static Vector2 zero => new(0, 0);
    }
    public struct Color(float r, float g, float b, float a = 1)
    { public float r = r, g = g, b = b, a = a; }
    public struct Color32(byte r, byte g, byte b, byte a)
    { public byte r = r, g = g, b = b, a = a; }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public class Texture2D
    {
        public static int Active;
        public int width, height;
        public string name;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public Color32[] pixels;
        public Texture2D(int width, int height, TextureFormat format, bool mipmap) { this.width = width; this.height = height; Active++; }
        public void SetPixels32(Color32[] pixels) => this.pixels = pixels;
        public void Apply(bool mipmap, bool readable) { }
    }
    public class Object { public static void Destroy(Texture2D texture) => Texture2D.Active--; }
    public static class Mathf
    {
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Ceil(float a) => (float)Math.Ceiling(a);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
    }
}
namespace Menu
{
    public static class MenuColorEffect
    {
        public static UnityEngine.Color rgbWhite, rgbMediumGrey, rgbBlack;
    }
}
namespace Menu.Remix.MixedUI
{
    using UnityEngine;
    public partial class OptionInterface
    {
        public OpTab[] Tabs;
        public static Dictionary<string, string> Translations = new();
        public static string Translate(string key) => Translations.TryGetValue(key, out var value) ? value : key;
    }
    public class UIelement
    {
        public Vector2 pos, size;
        public OpTab tab;
        public OpScrollBox scrollBox;
        public bool unloaded;
        public bool Hidden;
        public FContainer myContainer = new();
        public UIelement() { }
        public UIelement(Vector2 pos, Vector2 size) { this.pos = pos; this.size = size; }
        public void Hide() => Hidden = true;
        public void Show() => Hidden = false;
        public void MoveToFront() { }
        public void SetPos(Vector2 pos) => this.pos = pos;
        public virtual void Unload() => unloaded = true;
        public virtual void GrafUpdate(float timeStacker) { }
    }
    public class OpTab(OptionInterface owner, string name)
    {
        public OptionInterface owner = owner;
        public static List<OpTab> Created = new();
        public string name = name;
        public UnityEngine.Color colorButton;
        public List<UIelement> items = new();
        public bool unloaded;
        public event OnEventHandler OnPreUnload;
        public void AddItems(params UIelement[] elements)
        {
            if (!Created.Contains(this)) Created.Add(this);
            foreach (var element in elements) { element.tab = this; if (!items.Contains(element)) items.Add(element); }
        }
        public void RemoveItems(params UIelement[] elements)
        { foreach (var item in elements) { items.Remove(item); item.scrollBox?.items.Remove(item); item.tab = null; item.scrollBox = null; } }
        public void _Unload() { OnPreUnload?.Invoke(); foreach (var item in items.ToArray()) item.Unload(); unloaded = true; }
        public void _Update() { }
        public void _GrafUpdate(float time) { }
    }
    public class OpRect : UIelement
    {
        public Color colorEdge, colorFill;
        public OpRect(Vector2 pos, Vector2 size, float alpha) { this.pos = pos; this.size = size; }
    }
    public class OpLabel : UIelement
    {
        public string text;
        public FLabelAlignment alignment;
        public Color color;
        public float alpha = 1;
        public FLabel label = new();
        public OpLabel(Vector2 pos, Vector2 size, string text, FLabelAlignment alignment, bool bigText = false)
        { this.pos = pos; this.size = size; this.text = text; this.alignment = alignment; }
        public OpLabel(float x, float y, string text, bool bigText = false) : this(new(x, y), new(535, 25), text, FLabelAlignment.Left, bigText) { }
        public bool autoWrap;
        public virtual Vector2 GetDisplaySize() => new(LabelTest.GetWidth(text), text.Split('\n').Length * 20);
    }
    public class OpLabelLong : OpLabel
    {
        public float LineHeight => 20;
        public OpLabelLong(Vector2 pos, Vector2 size, string text, bool autoWrap, FLabelAlignment alignment)
            : base(pos, size, text, alignment) { }
        public override Vector2 GetDisplaySize() => new(LabelTest.GetWidth(text), text.Split('\n').Length * LineHeight);
    }
    public class OpImage : UIelement
    {
        public FSprite sprite;
        private Vector2 currentScale = new(1, 1);
        public Vector2 scale
        {
            get => currentScale;
            set { currentScale = value; size = new Vector2(1024 * value.x, 256 * value.y); }
        }
        public OpImage(Vector2 pos, string atlas) { this.pos = pos; size = new Vector2(1024, 256); sprite = new FSprite(atlas); }
    }
    public class OpScrollBox : UIelement
    {
        public const float MAXCONTENTSIZE = 10000;
        public static int ActiveCameras;
        public static bool FailAdd;
        public Color colorEdge;
        public float contentSize;
        public float targetScrollOffset;
        public float MaxScroll => -Math.Max(0, contentSize - size.y);
        public List<UIelement> items = new();
        public OpScrollBox(Vector2 pos, Vector2 size, float contentSize, bool horizontal = false, bool hasBack = true, bool hasSlideBar = true)
        { this.pos = pos; this.size = size; this.contentSize = contentSize; ActiveCameras++; }
        public void AddItems(params UIelement[] elements)
        {
            if (FailAdd) throw new InvalidOperationException("Injected scroll attachment failure");
            foreach (var element in elements) { element.scrollBox = this; items.Add(element); tab.AddItems(element); }
        }
        public void ScrollToTop() { targetScrollOffset = MaxScroll; }
        public override void Unload() { if (!unloaded) ActiveCameras--; base.Unload(); }
    }
    public delegate void OnEventHandler();
    public class OpSimpleButton : UIelement
    {
        public string text;
        public Color colorEdge, colorFill;
        public event Action<UIelement> OnClick;
        public OpSimpleButton(Vector2 pos, Vector2 size, string text) : base(pos, size) { this.text = text; }
        public void Click() => OnClick?.Invoke(this);
    }
    public static class LabelTest
    {
        public static bool Japanese;
        public static float GetWidth(string text, bool bigText = false) => text.Split('\n').Max(line => line.Sum(character => character > 127 ? 16 : 8));
        public static string TrimText(string text, float width, bool addDots = false, bool bigText = false)
        { while (text.Length > 0 && GetWidth(text) > width) text = text[..^1]; return text; }
        public static string WrapText(this string text, bool bigText, float width, bool forceWrapping = false)
        {
            text = text.Replace("<LINE>", "\n");
            if (Japanese && !forceWrapping) return text;
            var result = new StringBuilder();
            float lineWidth = 0;
            foreach (char character in text)
            {
                float advance = character > 127 ? 16 : 8;
                if (character == '\n') { result.Append(character); lineWidth = 0; continue; }
                if (lineWidth + advance > width) { result.Append('\n'); lineWidth = 0; }
                result.Append(character); lineWidth += advance;
            }
            return result.ToString();
        }
    }
}
public class FLabel
{
    public float scale = 1, rotation, x, y;
    public string text;
    public FLabelAlignment alignment;
}
public class FSprite
{
    public string name;
    public FAtlasElement element;
    public FSprite(string name)
    {
        this.name = name;
        element = new() { name = name };
        if (Futile.atlasManager.textures.TryGetValue(name, out var texture))
            element.sourceSize = new(texture.width, texture.height);
    }
    public float anchorX, anchorY, x, y, scaleX, scaleY, alpha;
    public UnityEngine.Color color;
}
public class FAtlasElement { public string name; public UnityEngine.Vector2 sourceSize = new(256, 256); }
public class FContainer
{
    public List<FSprite> sprites = new();
    public void AddChild(FSprite sprite) => sprites.Add(sprite);
}
public static class Futile { public static FAtlasManager atlasManager = new(); }
public class FAtlasManager
{
    public Dictionary<string, UnityEngine.Texture2D> textures = new();
    public bool DoesContainAtlas(string name) => textures.ContainsKey(name);
    public void LoadAtlasFromTexture(string name, UnityEngine.Texture2D texture, bool fromAsset) => textures.Add(name, texture);
    public void UnloadAtlas(string name) => textures.Remove(name);
}
