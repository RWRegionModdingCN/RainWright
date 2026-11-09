using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Map;

internal sealed class World { }

namespace DryCycle.DevUI.DevTool.Core
{
    internal sealed class EditorSession { }
}
namespace DryCycle.DevUI.DevTool.Map
{
    internal sealed class EditorMapRoomVisualSnapshot
    {
        internal static readonly EditorMapRoomVisualSnapshot Empty = new();
    }
    internal sealed class MapViewPersistentSnapshot { }
    internal sealed class MapViewPersistentRoom { }
}

internal static class WorldMapFrontendBridgeTests
{
    private static int assertions;
    private static void Check(bool value, string reason)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(reason);
    }

    internal static void Run()
    {
        // Tests use the actual backend callback bridge linked from production.
        Func<EditorSession, bool> block = _ => false;
        Func<EditorSession, bool> allow = _ => true;
        Check(WorldMapFrontendBridge.ShouldPublish(null), "Missing publish policy uses core default.");
        WorldMapFrontendBridge.RegisterShouldPublish(block);
        WorldMapFrontendBridge.RegisterShouldPublish(block);
        Check(!WorldMapFrontendBridge.ShouldPublish(null), "Matching owner registers idempotently.");
        bool rejected = false;
        try { WorldMapFrontendBridge.RegisterShouldPublish(allow); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !WorldMapFrontendBridge.ShouldPublish(null),
            "Another publish owner cannot displace the first registration.");
        WorldMapFrontendBridge.UnregisterShouldPublish(allow);
        Check(!WorldMapFrontendBridge.ShouldPublish(null),
            "An unrelated frontend cannot detach an existing callback.");
        WorldMapFrontendBridge.UnregisterShouldPublish(block);
        Check(WorldMapFrontendBridge.ShouldPublish(null), "Removing matching owner restores default.");
        WorldMapFrontendBridge.RegisterShouldPublish(allow);
        Check(WorldMapFrontendBridge.ShouldPublish(null),
            "After proper release a new callback owner may register.");
        WorldMapFrontendBridge.UnregisterShouldPublish(allow);

        Func<EditorSession, bool> primeA = _ => false;
        Func<EditorSession, bool> primeB = _ => true;
        WorldMapFrontendBridge.RegisterShouldPrimeGeometry(primeA);
        rejected = false;
        try { WorldMapFrontendBridge.RegisterShouldPrimeGeometry(primeB); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !WorldMapFrontendBridge.ShouldPrimeGeometry(null),
            "Geometry priming rejects a foreign owner.");
        WorldMapFrontendBridge.UnregisterShouldPrimeGeometry(primeA);
        Check(WorldMapFrontendBridge.ShouldPrimeGeometry(null), "Geometry priming cleanly detaches.");

        EditorMapRoomVisualSnapshot rasterA = new();
        EditorMapRoomVisualSnapshot rasterB = new();
        Func<int, EditorMapRoomVisualSnapshot, bool, EditorMapRoomVisualSnapshot> raster1 = (_, _, _) => rasterA;
        Func<int, EditorMapRoomVisualSnapshot, bool, EditorMapRoomVisualSnapshot> raster2 = (_, _, _) => rasterB;
        WorldMapFrontendBridge.RegisterRasterEnhancer(raster1);
        rejected = false;
        try { WorldMapFrontendBridge.RegisterRasterEnhancer(raster2); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && ReferenceEquals(WorldMapFrontendBridge.EnhanceRaster(1, null, false), rasterA),
            "Raster enhancement cannot be stolen.");
        WorldMapFrontendBridge.UnregisterRasterEnhancer(raster1);
        Check(ReferenceEquals(WorldMapFrontendBridge.EnhanceRaster(1, rasterB, false), rasterB),
            "Missing raster enhancer falls back to original.");
        Func<World, bool> backgroundA = _ => false;
        Func<World, bool> backgroundB = _ => true;
        WorldMapFrontendBridge.RegisterGeometryBackgroundBudget(backgroundA);
        rejected = false;
        try { WorldMapFrontendBridge.RegisterGeometryBackgroundBudget(backgroundB); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !WorldMapFrontendBridge.ShouldProcessGeometryBackground(null),
            "Background budget cannot be replaced by a foreign callback.");
        WorldMapFrontendBridge.UnregisterGeometryBackgroundBudget(backgroundB);
        Check(!WorldMapFrontendBridge.ShouldProcessGeometryBackground(null),
            "Mismatched background removal cannot detach owner.");
        WorldMapFrontendBridge.UnregisterGeometryBackgroundBudget(backgroundA);
        Check(WorldMapFrontendBridge.ShouldProcessGeometryBackground(null),
            "Background budget release restores the core default.");

        int captureCount = 0, restoreCount = 0, roomCount = 0, validatedCount = 0, clearCount = 0;
        Action<MapViewPersistentSnapshot> capture = _ => captureCount++;
        Action<MapViewPersistentSnapshot> restore = _ => restoreCount++;
        Action<int, MapViewPersistentRoom, bool> room = (_, _, _) => roomCount++;
        Action<IReadOnlyCollection<int>> validated = _ => validatedCount++;
        Action clear = () => clearCount++;
        Action foreignClear = () => clearCount += 100;
        WorldMapFrontendBridge.RegisterPersistentCacheCallbacks(capture, restore, room, validated, clear);
        WorldMapFrontendBridge.RegisterPersistentCacheCallbacks(capture, restore, room, validated, clear);
        rejected = false;
        try { WorldMapFrontendBridge.RegisterPersistentCacheCallbacks(
            capture, restore, room, validated, foreignClear); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Persistent cache rejects different bundle ownership.");
        WorldMapFrontendBridge.UnregisterPersistentCacheCallbacks(
            capture, restore, room, validated, foreignClear);
        WorldMapFrontendBridge.CapturePersistentSnapshot(null);
        WorldMapFrontendBridge.RestorePersistentSnapshot(null);
        WorldMapFrontendBridge.RestorePersistentRoom(1, null, true);
        WorldMapFrontendBridge.CompletePersistentRoomValidation(null);
        WorldMapFrontendBridge.ClearPersistentState();
        Check(captureCount == 1 && restoreCount == 1 && roomCount == 1 &&
              validatedCount == 1 && clearCount == 1,
            "The original five cache callbacks survive conflicting install and mismatched removal.");
        rejected = false;
        try { WorldMapFrontendBridge.RegisterPersistentCacheCallbacks(
            capture, restore, room, validated, null); }
        catch (ArgumentNullException) { rejected = true; }
        Check(rejected, "Incomplete persistent cache bundle fails closed.");
        WorldMapFrontendBridge.UnregisterPersistentCacheCallbacks(
            capture, restore, room, validated, clear);
        WorldMapFrontendBridge.ClearPersistentState();
        Check(clearCount == 1, "Releasing a persistent cache bundle clears all callbacks atomically.");
        WorldMapFrontendBridge.RegisterPersistentCacheCallbacks(
            capture, restore, room, validated, foreignClear);
        WorldMapFrontendBridge.ClearPersistentState();
        Check(clearCount == 101, "New persistent owner can register after full bundle release.");
        WorldMapFrontendBridge.UnregisterPersistentCacheCallbacks(
            capture, restore, room, validated, foreignClear);
        Console.WriteLine("  PASS " + assertions +
            " assertions; production World Map callback ownership and atomic cache registration.");
    }
}
