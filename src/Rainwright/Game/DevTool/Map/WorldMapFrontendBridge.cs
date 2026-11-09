using System;
using System.Collections.Generic;
using System.Threading;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Map;

/// <summary>
/// Optional frontend callbacks for the core World Map model/presentation layer.
/// DryCycle.dll never requires a reference back to the editor assembly.
/// Each callback slot has one explicit owner; the persistent cache uses one
/// atomic bundle so readers can never observe only part of a cache provider.
/// </summary>
internal static class WorldMapFrontendBridge
{
    private static Func<EditorSession, bool> shouldPublish;
    private static Func<EditorSession, bool> shouldPrimeGeometry;
    private static Func<int, EditorMapRoomVisualSnapshot, bool, EditorMapRoomVisualSnapshot> enhanceRaster;
    private static Func<global::World, bool> shouldProcessGeometryBackground;
    private static Action<EditorSession> restoreLegacyPresentation;

    internal static void RestoreLegacyPresentation(EditorSession session) =>
        Volatile.Read(ref restoreLegacyPresentation)?.Invoke(session);

    internal static void RegisterLegacyPresentationRestore(Action<EditorSession> callback) =>
        RegisterExclusive(ref restoreLegacyPresentation, callback, "LegacyPresentationRestore");

    internal static void UnregisterLegacyPresentationRestore(Action<EditorSession> callback) =>
        UnregisterExclusive(ref restoreLegacyPresentation, callback);

    private sealed class PersistentCallbacks
    {
        internal readonly Action<MapViewPersistentSnapshot> Capture;
        internal readonly Action<MapViewPersistentSnapshot> Restore;
        internal readonly Action<int, MapViewPersistentRoom, bool> RestoreRoom;
        internal readonly Action<IReadOnlyCollection<int>> CompleteValidation;
        internal readonly Action Clear;

        internal PersistentCallbacks(
            Action<MapViewPersistentSnapshot> capture,
            Action<MapViewPersistentSnapshot> restore,
            Action<int, MapViewPersistentRoom, bool> restoreRoom,
            Action<IReadOnlyCollection<int>> completeValidation,
            Action clear)
        {
            Capture = capture;
            Restore = restore;
            RestoreRoom = restoreRoom;
            CompleteValidation = completeValidation;
            Clear = clear;
        }

        internal bool Matches(
            Action<MapViewPersistentSnapshot> capture,
            Action<MapViewPersistentSnapshot> restore,
            Action<int, MapViewPersistentRoom, bool> restoreRoom,
            Action<IReadOnlyCollection<int>> completeValidation,
            Action clear) =>
            Delegate.Equals(Capture, capture) &&
            Delegate.Equals(Restore, restore) &&
            Delegate.Equals(RestoreRoom, restoreRoom) &&
            Delegate.Equals(CompleteValidation, completeValidation) &&
            Delegate.Equals(Clear, clear);
    }

    private static PersistentCallbacks persistentCallbacks;

    internal static bool ShouldPublish(EditorSession session) =>
        Volatile.Read(ref shouldPublish)?.Invoke(session) ?? true;

    internal static bool ShouldPrimeGeometry(EditorSession session) =>
        Volatile.Read(ref shouldPrimeGeometry)?.Invoke(session) ?? true;

    internal static EditorMapRoomVisualSnapshot EnhanceRaster(
        int roomIndex,
        EditorMapRoomVisualSnapshot original,
        bool allowReadback) =>
        Volatile.Read(ref enhanceRaster)?.Invoke(roomIndex, original, allowReadback) ??
        original ??
        EditorMapRoomVisualSnapshot.Empty;

    internal static bool ShouldProcessGeometryBackground(global::World world) =>
        Volatile.Read(ref shouldProcessGeometryBackground)?.Invoke(world) ?? true;

    internal static void CapturePersistentSnapshot(MapViewPersistentSnapshot snapshot) =>
        Volatile.Read(ref persistentCallbacks)?.Capture?.Invoke(snapshot);

    internal static void RestorePersistentSnapshot(MapViewPersistentSnapshot snapshot) =>
        Volatile.Read(ref persistentCallbacks)?.Restore?.Invoke(snapshot);

    internal static void RestorePersistentRoom(int roomIndex, MapViewPersistentRoom room, bool sourceValid) =>
        Volatile.Read(ref persistentCallbacks)?.RestoreRoom?.Invoke(roomIndex, room, sourceValid);

    internal static void CompletePersistentRoomValidation(IReadOnlyCollection<int> roomIndices) =>
        Volatile.Read(ref persistentCallbacks)?.CompleteValidation?.Invoke(roomIndices);

    internal static void ClearPersistentState() =>
        Volatile.Read(ref persistentCallbacks)?.Clear?.Invoke();

    private static void RegisterExclusive<T>(ref T slot, T callback, string name) where T : class
    {
        if (callback == null)
            throw new ArgumentNullException(nameof(callback));
        while (true)
        {
            T owner = Volatile.Read(ref slot);
            if (owner != null)
            {
                if (Delegate.Equals(owner, callback)) return; // Idempotent same-owner setup.
                throw new InvalidOperationException(
                    "World Map frontend callback is already owned: " + name);
            }
            if (Interlocked.CompareExchange(ref slot, callback, null) == null)
                return;
        }
    }

    private static void UnregisterExclusive<T>(ref T slot, T callback) where T : class
    {
        if (callback == null) return;
        while (true)
        {
            T owner = Volatile.Read(ref slot);
            if (owner == null || !Delegate.Equals(owner, callback)) return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref slot, null, owner), owner))
                return;
        }
    }

    internal static void RegisterShouldPublish(Func<EditorSession, bool> callback) =>
        RegisterExclusive(ref shouldPublish, callback, "ShouldPublish");

    internal static void UnregisterShouldPublish(Func<EditorSession, bool> callback) =>
        UnregisterExclusive(ref shouldPublish, callback);

    internal static void RegisterShouldPrimeGeometry(Func<EditorSession, bool> callback) =>
        RegisterExclusive(ref shouldPrimeGeometry, callback, "ShouldPrimeGeometry");

    internal static void UnregisterShouldPrimeGeometry(Func<EditorSession, bool> callback) =>
        UnregisterExclusive(ref shouldPrimeGeometry, callback);

    internal static void RegisterRasterEnhancer(
        Func<int, EditorMapRoomVisualSnapshot, bool, EditorMapRoomVisualSnapshot> callback) =>
        RegisterExclusive(ref enhanceRaster, callback, "RasterEnhancer");

    internal static void UnregisterRasterEnhancer(
        Func<int, EditorMapRoomVisualSnapshot, bool, EditorMapRoomVisualSnapshot> callback) =>
        UnregisterExclusive(ref enhanceRaster, callback);

    internal static void RegisterGeometryBackgroundBudget(Func<global::World, bool> callback) =>
        RegisterExclusive(ref shouldProcessGeometryBackground, callback, "GeometryBackgroundBudget");

    internal static void UnregisterGeometryBackgroundBudget(Func<global::World, bool> callback) =>
        UnregisterExclusive(ref shouldProcessGeometryBackground, callback);

    internal static void RegisterPersistentCacheCallbacks(
        Action<MapViewPersistentSnapshot> capture,
        Action<MapViewPersistentSnapshot> restore,
        Action<int, MapViewPersistentRoom, bool> restoreRoom,
        Action<IReadOnlyCollection<int>> completeValidation,
        Action clear)
    {
        if (capture == null || restore == null || restoreRoom == null ||
            completeValidation == null || clear == null)
            throw new ArgumentNullException(nameof(capture),
                "Persistent World Map cache callback bundle must be complete.");

        PersistentCallbacks candidate = new(capture, restore, restoreRoom, completeValidation, clear);
        while (true)
        {
            PersistentCallbacks owner = Volatile.Read(ref persistentCallbacks);
            if (owner != null)
            {
                if (owner.Matches(capture, restore, restoreRoom, completeValidation, clear))
                    return;
                throw new InvalidOperationException(
                    "World Map persistent cache callback bundle is already owned.");
            }
            if (Interlocked.CompareExchange(ref persistentCallbacks, candidate, null) == null)
                return;
        }
    }

    internal static void UnregisterPersistentCacheCallbacks(
        Action<MapViewPersistentSnapshot> capture,
        Action<MapViewPersistentSnapshot> restore,
        Action<int, MapViewPersistentRoom, bool> restoreRoom,
        Action<IReadOnlyCollection<int>> completeValidation,
        Action clear)
    {
        while (true)
        {
            PersistentCallbacks owner = Volatile.Read(ref persistentCallbacks);
            if (owner == null ||
                !owner.Matches(capture, restore, restoreRoom, completeValidation, clear))
                return;
            if (ReferenceEquals(
                    Interlocked.CompareExchange(ref persistentCallbacks, null, owner), owner))
                return;
        }
    }
}
