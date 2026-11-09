using System;

namespace DryCycle.DevUI.DevTool.Sound;

public sealed class EditorSoundSnapshot
{
    public int Index { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Sample { get; init; } = string.Empty;
    public bool Inherited { get; init; }
    public bool OverWrite { get; init; }
    public float Volume { get; init; }
    public float Pitch { get; init; }
    public float Doppler { get; init; }
    public float Taper { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Radius { get; init; }
    public float DirectionX { get; init; }
    public float DirectionY { get; init; }
    public bool Selected { get; init; }
    public bool ResourceAvailable { get; init; }
    public EditorSoundSourceKind ResourceSourceKind { get; init; }
    public string ResourceSourceName { get; init; } = string.Empty;
    public string ResourceSourceId { get; init; } = string.Empty;
}

public sealed class EditorSoundPresentationSnapshot
{
    public static readonly EditorSoundPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string RoomKey { get; init; } = string.Empty;
    public float BackgroundDroneVolume { get; init; }
    public float NoThreatDroneVolume { get; init; }
    public string[] Samples { get; init; } = Array.Empty<string>();
    public EditorSoundSampleSnapshot[] SampleEntries { get; init; } = Array.Empty<EditorSoundSampleSnapshot>();
    public EditorSoundSnapshot[] Sounds { get; init; } = Array.Empty<EditorSoundSnapshot>();
    public int SelectedIndex { get; init; } = -1;
}



/// <summary>
/// Process-wide Sound presentation state and detached selection projection.
/// Unity captures room sounds; Rainwright owns the immutable view handed to UI frontends.
/// </summary>
public static class EditorSoundPresentationState
{
    private static EditorSoundPresentationSnapshot current = EditorSoundPresentationSnapshot.Empty;
    public static EditorSoundPresentationSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorSoundPresentationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorSoundPresentationSnapshot.Empty);
    internal static void Clear() => Publish(EditorSoundPresentationSnapshot.Empty);

    internal static EditorSoundPresentationSnapshot WithSelection(
        EditorSoundPresentationSnapshot source, int index)
    {
        source ??= EditorSoundPresentationSnapshot.Empty;
        return new EditorSoundPresentationSnapshot
        {
            Available = source.Available,
            RoomKey = source.RoomKey,
            BackgroundDroneVolume = source.BackgroundDroneVolume,
            NoThreatDroneVolume = source.NoThreatDroneVolume,
            Samples = source.Samples,
            SampleEntries = source.SampleEntries,
            Sounds = ApplySelection(source.Sounds, index),
            SelectedIndex = index
        };
    }

    internal static EditorSoundSnapshot[] ApplySelection(EditorSoundSnapshot[] source, int index)
    {
        source ??= Array.Empty<EditorSoundSnapshot>();
        EditorSoundSnapshot[] result = null;
        for (int i = 0; i < source.Length; i++)
        {
            EditorSoundSnapshot sound = source[i];
            bool selected = i == index;
            if (sound == null || sound.Selected == selected) continue;
            result ??= (EditorSoundSnapshot[])source.Clone();
            result[i] = new EditorSoundSnapshot
            {
                Index = sound.Index,
                Type = sound.Type,
                Sample = sound.Sample,
                Inherited = sound.Inherited,
                OverWrite = sound.OverWrite,
                Volume = sound.Volume,
                Pitch = sound.Pitch,
                Doppler = sound.Doppler,
                Taper = sound.Taper,
                X = sound.X,
                Y = sound.Y,
                Radius = sound.Radius,
                DirectionX = sound.DirectionX,
                DirectionY = sound.DirectionY,
                Selected = selected,
                ResourceAvailable = sound.ResourceAvailable,
                ResourceSourceKind = sound.ResourceSourceKind,
                ResourceSourceName = sound.ResourceSourceName,
                ResourceSourceId = sound.ResourceSourceId
            };
        }
        return result ?? source;
    }
}
