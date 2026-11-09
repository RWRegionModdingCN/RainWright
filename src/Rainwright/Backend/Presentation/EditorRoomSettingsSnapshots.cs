using System;

namespace DryCycle.DevUI.DevTool.Room;

public sealed class EditorRoomEffectSnapshot
{
    public int Index { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public bool Inherited { get; init; }
    public bool OverWrite { get; init; }
    public bool Save { get; init; }
    public string[] SliderNames { get; init; } = Array.Empty<string>();
    public float[] Values { get; init; } = Array.Empty<float>();
}

public sealed class EditorRoomSettingsSnapshot
{
    public static readonly EditorRoomSettingsSnapshot Empty = new();

    public bool Available { get; init; }
    public string DangerType { get; init; } = string.Empty;
    public string[] DangerTypes { get; init; } = Array.Empty<string>();

    public float RainIntensity { get; init; }
    public float RumbleIntensity { get; init; }
    public float CeilingDrips { get; init; }
    public float WaveSpeed { get; init; }
    public float WaveLength { get; init; }
    public float WaveAmplitude { get; init; }
    public float SecondWaveLength { get; init; }
    public float SecondWaveAmplitude { get; init; }
    public float Clouds { get; init; }
    public float Grime { get; init; }
    public float RandomItemDensity { get; init; }
    public float RandomItemSpearChance { get; init; }
    public float WaterReflectionAlpha { get; init; }

    public int Palette { get; init; }
    public int EffectColorA { get; init; }
    public int EffectColorB { get; init; }
    public bool HasFadePalette { get; init; }
    public int FadePalette { get; init; } = -1;
    public float[] FadePaletteFades { get; init; } = Array.Empty<float>();

    public bool RoomSpecificScript { get; init; }
    public bool WetTerrain { get; init; }

    public bool TerrainAvailable { get; init; }
    public float TerrainLight { get; init; }
    public float TerrainStainAmount { get; init; }
    public float TerrainStainBrightness { get; init; }
    public float TerrainStainHeight { get; init; }
    public float TerrainWaves { get; init; }
    public float TerrainEdgeRadius { get; init; }
    public float TerrainGooHeight { get; init; }
    public float TerrainGrain { get; init; }
    public float TerrainDepth { get; init; }
    public float TerrainSkyFade { get; init; }
    public string TerrainPalette { get; init; } = string.Empty;
    public string[] TerrainPalettes { get; init; } = Array.Empty<string>();
    public bool HasTerrainFadePalette { get; init; }
    public string TerrainFadePalette { get; init; } = string.Empty;
    public float[] TerrainFadePaletteFades { get; init; } = Array.Empty<float>();

    public int CameraCount { get; init; }

    public bool TemplateControlsAvailable { get; init; }
    public string RegionName { get; init; } = string.Empty;
    public string CurrentTemplate { get; init; } = "NONE";
    public string[] TemplateNames { get; init; } = Array.Empty<string>();

    public string[] LocalSettingKeys { get; init; } = Array.Empty<string>();
    public string[] TemplateOverrideKeys { get; init; } = Array.Empty<string>();

    public EditorRoomEffectSnapshot[] Effects { get; init; } = Array.Empty<EditorRoomEffectSnapshot>();
    public string[] AvailableEffects { get; init; } = Array.Empty<string>();
    public string[] AvailableEffectCategories { get; init; } = Array.Empty<string>();

    public bool IsLocal(string key) => Contains(LocalSettingKeys, key);

    public bool InheritedFromTemplate(string key) =>
        !IsLocal(key) && Contains(TemplateOverrideKeys, key);

    private static bool Contains(string[] values, string key)
    {
        if (values == null || string.IsNullOrEmpty(key)) return false;
        for (int i = 0; i < values.Length; i++)
            if (string.Equals(values[i], key, StringComparison.Ordinal)) return true;
        return false;
    }
}


 
/// <summary>Shared Room Settings view owned by Rainwright, not by its Unity capture adapter.</summary>
public static class EditorRoomPresentationState
{
    private static EditorRoomSettingsSnapshot current = EditorRoomSettingsSnapshot.Empty;
    public static EditorRoomSettingsSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorRoomSettingsSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorRoomSettingsSnapshot.Empty);
    internal static void Clear() => Publish(EditorRoomSettingsSnapshot.Empty);
}
