using System;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Shared editor tool and document identity. These types must be emitted by
/// Rainwright.dll because both the gameplay adapter and native frontend use
/// them as cross-plugin dictionary keys and public session metadata.
/// </summary>
public enum EditorToolMode
{
    Room,
    Objects,
    Sound,
    Triggers,
    Map,
    Dialog,
    Relationships,
    Background,
    Palette
}

public enum EditorDocumentKind
{
    Room,
    RegionMap,
    Relationships,
    Palette
}

public readonly struct EditorDocumentKey : IEquatable<EditorDocumentKey>
{
    public EditorDocumentKey(EditorDocumentKind kind, string identity)
    {
        Kind = kind;
        Identity = identity ?? string.Empty;
    }

    public EditorDocumentKind Kind { get; }
    public string Identity { get; }

    public bool Equals(EditorDocumentKey other) =>
        Kind == other.Kind && string.Equals(Identity, other.Identity, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is EditorDocumentKey other && Equals(other);
    public override int GetHashCode() => ((int)Kind * 397) ^ StringComparer.Ordinal.GetHashCode(Identity);
    public override string ToString() => Kind + ":" + Identity;
}

