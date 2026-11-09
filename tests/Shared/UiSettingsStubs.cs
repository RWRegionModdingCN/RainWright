using System;
using System.Numerics;

namespace BepInEx
{
    internal static class Paths
    {
        internal static string ConfigPath = System.IO.Path.GetTempPath();
    }
}

namespace BepInEx.Logging
{
    internal sealed class ManualLogSource
    {
        internal void LogWarning(string value) { }
        internal void LogInfo(string value) { }
        internal void LogError(string value) { }
    }
}

namespace DryCycle.DevUI.DevTool.RWImGui
{
    internal enum DevToolUiLanguage { Chinese, English }
    internal enum DevToolScenePlacement { Left, Center }
    internal static class DevToolUiSettings
    {
        internal static string T(string chinese, string english) =>
            Language == DevToolUiLanguage.Chinese ? chinese : english;
        internal const float DefaultFontSize = 18f;
        internal const float DefaultChineseFontSize = 21f;
        internal const int DefaultFontWeight = 400;
        internal static DevToolUiLanguage Language = DevToolUiLanguage.English;
        internal static DevToolScenePlacement ScenePlacement = DevToolScenePlacement.Center;
        internal static float PersistedChineseFontSize = 21f;
        internal static float PersistedEnglishFontSize = 18f;
        internal static int PersistedEnglishFontWeight = 400;
        internal static Vector4 TextColor = new(0.94f, 0.94f, 0.94f, 1f);
        internal static Vector4 DisabledTextColor = new(0.76f, 0.80f, 0.86f, 1f);
        internal static void RestorePersistedPreferences(
            DevToolUiLanguage language, DevToolScenePlacement placement,
            float chineseFontSize, float englishFontSize, int englishFontWeight,
            Vector4 textColor, Vector4 disabledTextColor)
        {
            Language = language;
            ScenePlacement = placement;
            PersistedChineseFontSize = chineseFontSize;
            PersistedEnglishFontSize = englishFontSize;
            PersistedEnglishFontWeight = englishFontWeight;
            TextColor = textColor;
            DisabledTextColor = disabledTextColor;
        }
    }
}
