using System;

namespace DryCycle.DevUI.DevTool.Relationships;

public enum EditorRelationshipDirection
{
    PrimaryToOther,
    OtherToPrimary
}

public sealed class EditorRelationshipValueSnapshot
{
    public string Type { get; init; } = string.Empty;
    public float Intensity { get; init; }
    public bool DirectOverride { get; init; }
}

public sealed class EditorRelationshipRowSnapshot
{
    public string CreatureType { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public bool Selected { get; init; }
    public EditorRelationshipValueSnapshot PrimaryToOther { get; init; } = new();
    public EditorRelationshipValueSnapshot OtherToPrimary { get; init; } = new();
}

public sealed class EditorRelationshipPresentationSnapshot
{
    public static readonly EditorRelationshipPresentationSnapshot Empty = new();

    public bool Available { get; init; }
    public string PrimaryCreature { get; init; } = string.Empty;
    public string SelectedOtherCreature { get; init; } = string.Empty;
    public EditorRelationshipDirection SelectedDirection { get; init; }
    public string[] CreatureTypes { get; init; } = Array.Empty<string>();
    public string[] RelationshipTypes { get; init; } = Array.Empty<string>();
    public EditorRelationshipRowSnapshot[] Rows { get; init; } = Array.Empty<EditorRelationshipRowSnapshot>();
    public bool LoadingRows { get; init; }
    public int LoadedRowCount { get; init; }
    public int TotalRowCount { get; init; }
}



/// <summary>Published authoring view identity and lifecycle owned by Rainwright.</summary>
public static class EditorRelationshipPresentationState
{
    private static EditorRelationshipPresentationSnapshot current = EditorRelationshipPresentationSnapshot.Empty;
    public static EditorRelationshipPresentationSnapshot Current => System.Threading.Volatile.Read(ref current);
    internal static void Publish(EditorRelationshipPresentationSnapshot value) =>
        System.Threading.Volatile.Write(ref current, value ?? EditorRelationshipPresentationSnapshot.Empty);
    internal static void Clear() => Publish(EditorRelationshipPresentationSnapshot.Empty);
}
