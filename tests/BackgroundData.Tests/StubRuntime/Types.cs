public sealed class RoomSettings { }
public class BackgroundScene { public class BackgroundSceneElement { } }
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new(0, 0);
    }
    public struct Color { public float r, g, b; }
    public static class ColorUtility
    {
        public static string ToHtmlStringRGB(Color color) => "123456";
        public static bool TryParseHtmlString(string text, out Color color) { color = default; return text.Length == 7; }
    }
}
