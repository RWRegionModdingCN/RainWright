using System;

namespace DryCycle.DevUI.DevTool.Triggers;

public sealed class EditorTriggeredEventSnapshot
{
    public bool HasEvent { get; init; }
    public string Type { get; init; } = string.Empty;
    public string SongName { get; init; } = string.Empty;
    public float Priority { get; init; }
    public float MaxThreatLevel { get; init; }
    public float DroneTolerance { get; init; }
    public float Volume { get; init; }
    public float FadeInSeconds { get; init; }
    public bool Loop { get; init; }
    public bool OneSongPerCycle { get; init; }
    public bool StopAtDeath { get; init; }
    public bool StopAtGate { get; init; }
    public int RoomsRange { get; init; } = -1;
    public int CyclesRest { get; init; } = -1;
    public string StopMode { get; init; } = string.Empty;
    public float FadeOutSeconds { get; init; }
    public bool AfterEncounter { get; init; }
    public bool OnlyWhenShowingDirection { get; init; }
    public int FromCycle { get; init; }
}

public sealed class EditorTriggerSnapshot
{
    public int Index { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool Selected { get; init; }
    public bool IsSpot { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Radius { get; init; }
    public float RadiusHandleX { get; init; }
    public float RadiusHandleY { get; init; }
    public int ActiveFromCycle { get; init; }
    public int ActiveToCycle { get; init; }
    public float DelaySeconds { get; init; }
    public float FireChance { get; init; }
    public bool MultiUse { get; init; }
    public int Entrance { get; init; }
    public int Karma { get; init; }
    public string CreatureType { get; init; } = string.Empty;
    public string[] AllowedSlugcats { get; init; } = Array.Empty<string>();
    public EditorTriggeredEventSnapshot Event { get; init; } = new();
}

public sealed class EditorTriggerPresentationSnapshot
{
    public static readonly EditorTriggerPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string[] TriggerTypes { get; init; } = Array.Empty<string>();
    public string[] EventTypes { get; init; } = Array.Empty<string>();
    public string[] SongNames { get; init; } = Array.Empty<string>();
    public string[] SlugcatNames { get; init; } = Array.Empty<string>();
    public EditorTriggerSnapshot[] Triggers { get; init; } = Array.Empty<EditorTriggerSnapshot>();
    public int SelectedIndex { get; init; } = -1;
    public int EntranceCount { get; init; }
}



/// <summary>Shared editor view state owned by Rainwright rather than the game adapter.</summary>
public static class EditorTriggerPresentationState
{
    private static EditorTriggerPresentationSnapshot current = EditorTriggerPresentationSnapshot.Empty;
    public static EditorTriggerPresentationSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorTriggerPresentationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorTriggerPresentationSnapshot.Empty);
    internal static void Clear() => Publish(EditorTriggerPresentationSnapshot.Empty);
}
