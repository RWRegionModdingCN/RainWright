using System;
using System.Collections.Generic;
using System.Threading;
using RWCustom;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Map;

/// <summary>
/// Data-only authored terrain description. Optional mods provide their splines without
/// exposing game-specific Data subclasses to the World Map or Cartography compiler.
/// </summary>
public sealed class EditorMapTerrainBand
{
    public EditorMapTerrainBand(BezierSpline spline, float depth,
        EditorMapGeometryKind bodyKind, EditorMapGeometryKind materialKind,
        IReadOnlyList<Vector2> materialIntervals = null)
    {
        SurfaceSpline = spline ?? throw new ArgumentNullException(nameof(spline));
        Depth = depth;
        BodyKind = bodyKind;
        MaterialKind = materialKind;
        MaterialIntervals = materialIntervals ?? Array.Empty<Vector2>();
    }

    public BezierSpline SurfaceSpline { get; }
    public float Depth { get; }
    public EditorMapGeometryKind BodyKind { get; }
    public EditorMapGeometryKind MaterialKind { get; }
    public IReadOnlyList<Vector2> MaterialIntervals { get; }
}

/// <summary>
/// Single-writer scoped registration, immutable lookup snapshots. Source loading and per-frame
/// geometry fingerprint checks read without locks or allocations. Owner changes bump Revision
/// so active rooms can invalidate their cached terrain on the next permitted refresh.
/// </summary>
public static class EditorMapTerrainContributors
{
    private sealed class Provider
    {
        internal readonly Func<PlacedObject, EditorMapTerrainBand> Describe;
        internal readonly Func<PlacedObject, PlacedObject.Data> CreateDetachedData;
        internal readonly Action<Exception> Report;
        internal Provider(Func<PlacedObject, EditorMapTerrainBand> describe,
            Action<Exception> report, Func<PlacedObject, PlacedObject.Data> createDetachedData)
        {
            Describe = describe;
            Report = report;
            CreateDetachedData = createDetachedData;
        }
    }

    private sealed class Snapshot
    {
        internal readonly Dictionary<string, Provider> Sources;
        internal readonly int Revision;
        internal Snapshot(Dictionary<string, Provider> sources, int revision)
        { Sources = sources; Revision = revision; }
    }

    private sealed class Lease : IDisposable
    {
        private readonly string id;
        private Provider source;
        internal Lease(string id, Provider source) { this.id = id; this.source = source; }
        public void Dispose()
        {
            Provider old = Interlocked.Exchange(ref source, null);
            if (old == null) return;
            Unregister(id, old);
        }
    }

    private static readonly object Sync = new();
    private static Snapshot current = new(new Dictionary<string, Provider>(StringComparer.OrdinalIgnoreCase), 0);
    public static int Revision => Volatile.Read(ref current).Revision;

    public static IDisposable Register(string placedObjectType,
        Func<PlacedObject, EditorMapTerrainBand> describe, Action<Exception> report = null,
        Func<PlacedObject, PlacedObject.Data> createDetachedData = null)
    {
        if (string.IsNullOrWhiteSpace(placedObjectType))
            throw new ArgumentException("PlacedObject type must be specified.", nameof(placedObjectType));
        if (describe == null) throw new ArgumentNullException(nameof(describe));
        string id = placedObjectType.Trim();
        Provider newProvider = new(describe, report, createDetachedData);
        lock (Sync)
        {
            Snapshot old = current;
            if (old.Sources.ContainsKey(id))
                throw new InvalidOperationException("Terrain contributor is already registered: " + id);
            Dictionary<string, Provider> next = new(old.Sources, StringComparer.OrdinalIgnoreCase)
            { [id] = newProvider };
            Volatile.Write(ref current, new Snapshot(next, unchecked(old.Revision + 1)));
        }
        return new Lease(id, newProvider);
    }

    public static bool HasContributor(string typeName)
    {
        return typeName != null && Volatile.Read(ref current).Sources.ContainsKey(typeName);
    }

    /// <summary>
    /// The detached room authoring parser never needs to construct a gameplay
    /// Data subclass itself. Mods optionally supply their own detached factory
    /// alongside geometry description, with the SAME scoped owner and revision.
    /// A failed factory is isolated and reported just like a failed geometry
    /// projection; the caller may retain original author text for diagnostics.
    /// </summary>
    public static bool TryPopulateDetachedData(PlacedObject target)
    {
        string id = target?.type?.value;
        if (id == null || !Volatile.Read(ref current).Sources.TryGetValue(id, out Provider provider) ||
            provider.CreateDetachedData == null)
            return false;

        try
        {
            if (target.data != null) return true;
            PlacedObject.Data result = provider.CreateDetachedData(target);
            if (result == null) return false;
            target.data = result;
            return true;
        }
        catch (Exception error)
        {
            Unregister(id, provider);
            try { provider.Report?.Invoke(error); } catch { }
            // This is an authoring parse failure, not a successful empty
            // geometry projection; report it through the parser as well.
            throw new InvalidOperationException(
                "Detached terrain data factory failed for " + id + ".", error);
        }
    }

    public static bool TryDescribe(PlacedObject target, out EditorMapTerrainBand band)
    {
        band = null;
        string id = target?.type?.value;
        if (id == null || !Volatile.Read(ref current).Sources.TryGetValue(id, out Provider provider))
            return false;
        try
        {
            band = provider.Describe(target);
            return band != null;
        }
        catch (Exception error)
        {
            // Bad third-party data cannot crash the map source compiler indefinitely.
            Unregister(id, provider);
            try { provider.Report?.Invoke(error); } catch { }
            return false;
        }
    }

    private static void Unregister(string id, Provider source)
    {
        lock (Sync)
        {
            Snapshot old = current;
            if (!old.Sources.TryGetValue(id, out Provider active) || !ReferenceEquals(active, source))
                return;
            Dictionary<string, Provider> next = new(old.Sources, StringComparer.OrdinalIgnoreCase);
            next.Remove(id);
            Volatile.Write(ref current, new Snapshot(next, unchecked(old.Revision + 1)));
        }
    }
}
