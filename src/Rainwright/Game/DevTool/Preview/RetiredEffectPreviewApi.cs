namespace DryCycle.DevUI.DevTool.Preview;

// Preserve the published binary entry points for older optional frontends. They carry no state,
// install no hooks, and cannot change RoomEffects, game objects, cameras, shaders or author files.
public static class EffectPreviewIntentHub
{
    public static void Hover(string typeName) { }
    public static void ClearHover() { }
    public static void SuppressForCommit(string typeName) { }
    public static string ActiveType => string.Empty;
    public static bool IsActive => false;
    public static void PumpMainThread() { }
}
