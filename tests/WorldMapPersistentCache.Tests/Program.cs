using System;
using System.IO;
using System.Linq;
using BepInEx;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.RWImGui;
using UnityEngine;

internal static class Program
{
    private const int CacheMagic = 0x44434D56;
    private static int assertions;
    private static string root;

    private static int Main()
    {
        root = Path.Combine(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../artifacts")),
            "drycycle-worldmap-cache-tests-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Paths.CachePath = root;
        Application.persistentDataPath = root;

        try
        {
            V3RoundTripAndFlush();
            BoundedFlush();
            CoalescedLatestRevisionWins();
            FailedWriteIsReportedAndCanRecover();
            ExactCurvedTerrainGeometry();
            V2FallbackMigrationRead();
            LegacyV3ReadOnlyFallbackAndNewOwnerPriority();
            TruncatedCacheRejected();
            WrongContextRejected();
            FutureVersionRejected();

            Console.WriteLine(
                "PASS: " + assertions +
                " assertions; World Map V2/V3 cache compatibility, " +
                "route/thumbnail round-trip, corruption rejection and writer flush.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private static void BoundedFlush()
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        Type store = typeof(MapViewPersistentCacheStore);
        object sync = store.GetField("writeSync", flags).GetValue(null);
        var running = store.GetField("writerRunning", flags);
        Check(MapViewPersistentCacheStore.Flush(1000), "Flush boundary fixture starts with no active writer.");
        lock (sync) running.SetValue(null, true);
        try
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Check(!MapViewPersistentCacheStore.Flush(40), "A stalled cache writer cannot make shutdown wait indefinitely.");
            Check(timer.ElapsedMilliseconds >= 20 && timer.ElapsedMilliseconds < 1500, "The cache flush deadline is bounded by elapsed time.");
            var waiting = System.Threading.Tasks.Task.Run(() => MapViewPersistentCacheStore.Flush(5000));
            Check(!waiting.Wait(30), "A pending writer still owns an ordinary flush.");
            lock (sync) { running.SetValue(null, false); System.Threading.Monitor.PulseAll(sync); }
            Check(waiting.Wait(1000) && waiting.Result, "Writer completion wakes a waiting flush without polling or waiting for its deadline.");
        }
        finally { lock (sync) { running.SetValue(null, false); System.Threading.Monitor.PulseAll(sync); } }
    }

    private static void V3RoundTripAndFlush()
    {
        string path = Path.Combine(root, "roundtrip-v3.bin");
        MapViewPersistentSnapshot snapshot = SampleV3("world|Survivor");

        MapViewPersistentCacheStore.QueueWrite(path, snapshot);
        Check(
            MapViewPersistentCacheStore.Flush(5000),
            "Flush must wait for queued persistent writes.");
        Check(File.Exists(path), "V3 cache file must exist after flush.");
        Check(
            !Directory.GetFiles(
                root,
                "*.tmp-*",
                SearchOption.TopDirectoryOnly).Any(),
            "Atomic writer must not leave temp files after success.");

        MapViewPersistentSnapshot loaded =
            MapViewPersistentCacheStore.Load(
                path,
                snapshot.ContextKey);
        Check(loaded != null, "V3 cache must load.");
        Check(
            loaded.FormatVersion ==
            MapViewPersistentCacheStore.CurrentCacheVersion,
            "Loaded V3 format version must be reported.");
        Check(
            loaded.TemplateFingerprint == 123456789L &&
            loaded.FrontendTopologyFingerprint == 987654321L,
            "V3 fingerprints must round-trip.");
        Check(
            loaded.Rooms.Count == 1,
            "V3 room count must round-trip.");

        MapViewPersistentRoom room = loaded.Rooms[0];
        Check(
            room.RoomName == "SU_A01" &&
            room.ThumbnailElementName == "Map_SU_A01",
            "Room identity and persistent thumbnail element must round-trip.");
        Check(
            Math.Abs(room.ThumbnailUvX - 0.125f) < 0.0001f &&
            Math.Abs(room.ThumbnailUvHeight - 0.25f) < 0.0001f &&
            Math.Abs(room.ThumbnailPixelWidth - 320f) < 0.0001f,
            "Thumbnail UV/pixel descriptor must round-trip.");
        Check(
            room.BaseRasterRuns.Length == 2 &&
            room.Curves.Length == 1 &&
            room.Nodes.Length == 1,
            "Geometry payload must round-trip.");

        Check(
            loaded.Routes.Count == 1,
            "V3 route list must round-trip.");
        MapViewPersistentRoute route = loaded.Routes[0];
        Check(
            route.ConnectionId == "SU_A01->SU_A02:0:1" &&
            route.PolicyVersion == 3 &&
            route.Kind == 2,
            "Route metadata must round-trip.");
        Check(
            route.Points.Length == 3 &&
            Math.Abs(route.Points[1].X - 80f) < 0.0001f &&
            Math.Abs(route.Points[2].Y - 45f) < 0.0001f,
            "Route points must round-trip.");
    }

    private static void CoalescedLatestRevisionWins()
    {
        const string context = "queue-coalesce|Survivor";
        string cache = MapViewPersistentCacheStore.ResolveCachePath(context);
        string other = MapViewPersistentCacheStore.ResolveCachePath("queue-other|Survivor");
        for (int revision = 1; revision <= 60; revision++)
        {
            MapViewPersistentSnapshot next = SampleV3(context);
            next.TemplateFingerprint = revision;
            MapViewPersistentCacheStore.QueueWrite(cache, next);
            Check(MapViewPersistentCacheStore.PendingWriteRequestCount <= 1,
                "An in-flight destination has at most one pending latest snapshot.");
        }

        MapViewPersistentCacheStore.QueueWrite(other, SampleV3("queue-other|Survivor"));
        Check(MapViewPersistentCacheStore.PendingWriteRequestCount <= 2,
            "A different destination maintains its own bounded queued entry.");
        Check(MapViewPersistentCacheStore.Flush(5000),
            "Coalesced cache writer drains all destinations.");
        Check(MapViewPersistentCacheStore.PendingWriteRequestCount == 0,
            "Successful Flush empties the pending snapshot inventory.");
        MapViewPersistentSnapshot final = MapViewPersistentCacheStore.Load(cache, context);
        Check(final != null && final.TemplateFingerprint == 60L,
            "Latest snapshot wins even when older same-path writes were already running.");
        Check(MapViewPersistentCacheStore.Load(other, "queue-other|Survivor") != null,
            "Coalescing same-path revisions never loses other author destinations.");
    }

    private static void FailedWriteIsReportedAndCanRecover()
    {
        const string context = "write-error|Monk";
        string destination = MapViewPersistentCacheStore.ResolveCachePath(context);
        Directory.CreateDirectory(Path.GetDirectoryName(destination));

        // Existing *directory* in place of the destination forces the
        // writer to fail after it has completed its unique staging file.
        // This is deterministic on Windows and Linux, unlike a readonly bit.
        Directory.CreateDirectory(destination);
        MapViewPersistentCacheStore.QueueWrite(destination, SampleV3(context));
        Check(!MapViewPersistentCacheStore.Flush(5000),
            "A drained writer with an unsuccessful cache replace is not a successful Flush.");
        Check(MapViewPersistentCacheStore.FailedWriteCount == 1 &&
              MapViewPersistentCacheStore.HasFailedWrite(destination) &&
              !MapViewPersistentCacheStore.HasFailedWrite(
                  MapViewPersistentCacheStore.ResolveCachePath("independent|Monk")),
            "Cache write failure is tracked for the exact affected destination.");
        Check(Directory.Exists(destination) &&
              !Directory.GetFiles(Path.GetDirectoryName(destination),
                  "*.tmp-*", SearchOption.TopDirectoryOnly).Any(),
            "Failed cache replacement neither destroys the previous destination nor leaves staging files.");

        string independent = MapViewPersistentCacheStore.ResolveCachePath("independent|Monk");
        MapViewPersistentCacheStore.QueueWrite(independent, SampleV3("independent|Monk"));
        Check(!MapViewPersistentCacheStore.Flush(5000) && File.Exists(independent),
            "A successful unrelated cache cannot mask an outstanding failed destination.");

        Directory.Delete(destination);
        MapViewPersistentSnapshot repaired = SampleV3(context);
        repaired.TemplateFingerprint = 556677L;
        MapViewPersistentCacheStore.QueueWrite(destination, repaired);
        Check(MapViewPersistentCacheStore.Flush(5000) &&
              MapViewPersistentCacheStore.FailedWriteCount == 0 &&
              !MapViewPersistentCacheStore.HasFailedWrite(destination),
            "Rewriting the failed destination clears the error and restores true Flush success.");
        Check(MapViewPersistentCacheStore.Load(destination, context)?.TemplateFingerprint == 556677L,
            "Recovered cache is readable after a previously failed atomic write.");
    }

    private static void ExactCurvedTerrainGeometry()
    {
        EditorMapRoomVisualSnapshot exact = new()
        {
            Available = true,
            DetailedRasterAvailable = true,
            WidthTiles = 20f,
            HeightTiles = 10f,
            RasterRuns = new[]
            {
                new EditorMapRectSnapshot(
                    0f, 0f, 20f, 10f,
                    EditorMapGeometryKind.Air)
            },
            // This deliberately represents the old coarse approximation. When exact closed fill
            // polygons exist, retained geometry must ignore this room-wide rectangle.
            TerrainRuns = new[]
            {
                new EditorMapRectSnapshot(
                    0f, 0f, 20f, 8f,
                    EditorMapGeometryKind.Solid)
            },
            Curves = new[]
            {
                new EditorMapPolylineSnapshot
                {
                    Kind = EditorMapGeometryKind.Solid,
                    Closed = true,
                    Points = new[]
                    {
                        new EditorMapPointSnapshot(2f, 8f),
                        new EditorMapPointSnapshot(8f, 6f),
                        new EditorMapPointSnapshot(8f, 5f),
                        new EditorMapPointSnapshot(2f, 7f)
                    }
                },
                new EditorMapPolylineSnapshot
                {
                    Kind = EditorMapGeometryKind.CurvedSlope,
                    Closed = false,
                    Points = new[]
                    {
                        new EditorMapPointSnapshot(2f, 8f),
                        new EditorMapPointSnapshot(8f, 6f)
                    }
                }
            }
        };

        RoomGeometryBlob exactBlob =
            RoomGeometryBuilder.Build(
                7,
                exact,
                1);

        Check(
            exactBlob.AuthoredTerrainVertices.Length == 4 &&
            exactBlob.AuthoredTerrainTriangleIndices.Length == 6,
            "Exact curved terrain must build one polygon quad instead of rectangle fill runs.");

        float exactMinX =
            exactBlob.AuthoredTerrainVertices.Min(vertex => vertex.X);
        float exactMaxX =
            exactBlob.AuthoredTerrainVertices.Max(vertex => vertex.X);
        float exactMinY =
            exactBlob.AuthoredTerrainVertices.Min(vertex => vertex.Y);
        float exactMaxY =
            exactBlob.AuthoredTerrainVertices.Max(vertex => vertex.Y);

        Check(
            Math.Abs(exactMinX - 2f) < 0.0001f &&
            Math.Abs(exactMaxX - 8f) < 0.0001f &&
            Math.Abs(exactMinY - 5f) < 0.0001f &&
            Math.Abs(exactMaxY - 8f) < 0.0001f,
            "Exact curved terrain bounds must follow the real curve strip, not its legacy room-wide AABB.");

        Check(
            exactBlob.CurveSegments.Length == 1 &&
            Math.Abs(exactBlob.CurveSegments[0].AX - 2f) < 0.0001f &&
            Math.Abs(exactBlob.CurveSegments[0].BX - 8f) < 0.0001f,
            "Closed fill polygons must not pollute routing curve segments while the visible surface remains available.");

        EditorMapRoomVisualSnapshot legacy = new()
        {
            Available = true,
            DetailedRasterAvailable = true,
            WidthTiles = 20f,
            HeightTiles = 10f,
            TerrainRuns = new[]
            {
                new EditorMapRectSnapshot(
                    3f, 2f, 5f, 4f,
                    EditorMapGeometryKind.Solid)
            },
            Curves = Array.Empty<EditorMapPolylineSnapshot>()
        };

        RoomGeometryBlob legacyBlob =
            RoomGeometryBuilder.Build(
                8,
                legacy,
                1);

        Check(
            legacyBlob.AuthoredTerrainVertices.Length == 4 &&
            Math.Abs(legacyBlob.AuthoredTerrainVertices.Min(vertex => vertex.X) - 3f) < 0.0001f &&
            Math.Abs(legacyBlob.AuthoredTerrainVertices.Max(vertex => vertex.X) - 8f) < 0.0001f,
            "Legacy cached TerrainRuns remain a bounded compatibility fallback when no exact polygons exist.");
    }

    private static void V2FallbackMigrationRead()
    {
        const string context = "legacy|Monk";
        string v3Path =
            MapViewPersistentCacheStore.ResolveCachePath(context);
        string v2Path = Path.Combine(
            root,
            "DryCycle",
            "DevTool",
            "MapView",
            "map-view-v2-" +
            StableHash(context).ToString("x16") +
            ".bin");

        Directory.CreateDirectory(Path.GetDirectoryName(v2Path));
        WriteV2(v2Path, context);

        MapViewPersistentSnapshot loaded =
            MapViewPersistentCacheStore.Load(
                v3Path,
                context);

        Check(
            loaded != null && loaded.FormatVersion == 2,
            "Missing V3 path must fall back to the matching V2 cache.");
        Check(
            loaded.Rooms.Count == 1 &&
            loaded.Rooms[0].RoomName == "SU_LEGACY",
            "V2 room payload must remain readable.");
        Check(
            string.IsNullOrEmpty(
                loaded.Rooms[0].ThumbnailElementName) &&
            loaded.Routes.Count == 0 &&
            loaded.FrontendTopologyFingerprint == 0L,
            "V2 load must default V3-only frontend metadata.");
    }

    private static void LegacyV3ReadOnlyFallbackAndNewOwnerPriority()
    {
        const string context = "old-v3|Survivor";
        string rainwrightPath = MapViewPersistentCacheStore.ResolveCachePath(context);
        Check(rainwrightPath.Replace('\\', '/').Contains("/Rainwright/MapView/"),
            "New persistent World Map caches are owned by Rainwright.");

        string oldV3 = Path.Combine(root, "DryCycle", "DevTool", "MapView",
            "map-view-v3-" + StableHash(context).ToString("x16") + ".bin");
        Directory.CreateDirectory(Path.GetDirectoryName(oldV3));
        MapViewPersistentCacheStore.QueueWrite(oldV3, SampleV3(context));
        Check(MapViewPersistentCacheStore.Flush(5000),
            "Historical DryCycle V3 fixture finishes before read-only import.");
        byte[] originalBytes = File.ReadAllBytes(oldV3);

        MapViewPersistentSnapshot imported =
            MapViewPersistentCacheStore.Load(rainwrightPath, context);
        Check(imported != null && imported.FormatVersion == 3 &&
              imported.Routes.Count == 1 &&
              imported.FrontendTopologyFingerprint == 987654321L,
            "Rainwright reads historical DryCycle V3 routes and thumbnail metadata.");
        Check(!File.Exists(rainwrightPath) &&
              File.ReadAllBytes(oldV3).SequenceEqual(originalBytes),
            "Legacy read does not rewrite its source or create a phantom new cache.");

        MapViewPersistentSnapshot fresh = SampleV3(context);
        fresh.TemplateFingerprint = 99887766L;
        MapViewPersistentCacheStore.QueueWrite(rainwrightPath, fresh);
        Check(MapViewPersistentCacheStore.Flush(5000),
            "Rainwright-owned cache finishes before ownership priority test.");
        imported = MapViewPersistentCacheStore.Load(rainwrightPath, context);
        Check(imported != null && imported.TemplateFingerprint == 99887766L &&
              File.ReadAllBytes(oldV3).SequenceEqual(originalBytes),
            "Current Rainwright V3 wins over historical DryCycle V3 without changing legacy bytes.");

        File.WriteAllBytes(rainwrightPath, new byte[] { 0x12, 0x34, 0x56 });
        Check(MapViewPersistentCacheStore.Load(rainwrightPath, context) == null,
            "An existing corrupt current cache does not silently fall back to stale DryCycle data.");
    }

    private static void TruncatedCacheRejected()
    {
        string path = Path.Combine(root, "truncated.bin");
        MapViewPersistentCacheStore.QueueWrite(
            path,
            SampleV3("truncated|Survivor"));
        Check(
            MapViewPersistentCacheStore.Flush(5000),
            "Writer must flush before corruption test.");

        byte[] bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(
            path,
            bytes.Take(Math.Max(1, bytes.Length / 3)).ToArray());

        Check(
            MapViewPersistentCacheStore.Load(
                path,
                "truncated|Survivor") == null,
            "Truncated cache must be rejected instead of partially restored.");
    }

    private static void WrongContextRejected()
    {
        string path = Path.Combine(root, "wrong-context.bin");
        MapViewPersistentCacheStore.QueueWrite(
            path,
            SampleV3("source|Survivor"));
        Check(
            MapViewPersistentCacheStore.Flush(5000),
            "Writer must flush before context test.");

        Check(
            MapViewPersistentCacheStore.Load(
                path,
                "other|Survivor") == null,
            "Cache from another world/context must be rejected.");
    }

    private static void FutureVersionRejected()
    {
        string path = Path.Combine(root, "future.bin");
        using (FileStream stream = File.Create(path))
        using (BinaryWriter writer = new(stream))
        {
            writer.Write(CacheMagic);
            writer.Write(999);
        }

        Check(
            MapViewPersistentCacheStore.Load(
                path,
                "future|Survivor") == null,
            "Unknown future cache versions must be rejected.");
    }

    private static MapViewPersistentSnapshot SampleV3(
        string context)
    {
        MapViewPersistentSnapshot snapshot = new()
        {
            FormatVersion =
                MapViewPersistentCacheStore.CurrentCacheVersion,
            ContextKey = context,
            TemplateFingerprint = 123456789L,
            FrontendTopologyFingerprint = 987654321L
        };

        snapshot.Rooms.Add(
            new MapViewPersistentRoom
            {
                RoomName = "SU_A01",
                RoomSource =
                    new MapViewFileStamp(
                        "rooms/SU_A01.txt",
                        1234L,
                        5678L),
                SettingsSource =
                    new MapViewFileStamp(
                        "rooms/SU_A01_settings.txt",
                        2345L,
                        6789L),
                RasterSignature = "atlas:SU_A01",
                RasterInitialized = true,
                NodesInitialized = true,
                CurvesInitialized = true,
                RasterWidth = 40,
                RasterHeight = 20,
                NodeFingerprint = 99,
                SettingsFingerprint = 101,
                WidthTiles = 40f,
                HeightTiles = 20f,
                ThumbnailElementName = "Map_SU_A01",
                ThumbnailUvX = 0.125f,
                ThumbnailUvY = 0.5f,
                ThumbnailUvWidth = 0.5f,
                ThumbnailUvHeight = 0.25f,
                ThumbnailPixelWidth = 320f,
                ThumbnailPixelHeight = 160f,
                BaseRasterRuns = new[]
                {
                    new EditorMapRectSnapshot(
                        0f, 0f, 40f, 1f,
                        EditorMapGeometryKind.Solid),
                    new EditorMapRectSnapshot(
                        0f, 1f, 40f, 19f,
                        EditorMapGeometryKind.Air)
                },
                TerrainFillRuns = new[]
                {
                    new EditorMapRectSnapshot(
                        4f, 2f, 6f, 3f,
                        EditorMapGeometryKind.Structure)
                },
                Curves = new[]
                {
                    new EditorMapPolylineSnapshot
                    {
                        Kind = EditorMapGeometryKind.Solid,
                        Closed = false,
                        Points = new[]
                        {
                            new EditorMapPointSnapshot(0f, 3f),
                            new EditorMapPointSnapshot(8f, 7f)
                        }
                    }
                },
                Nodes = new[]
                {
                    new EditorMapNodeVisualSnapshot(
                        2,
                        39f,
                        10f)
                }
            });

        snapshot.Routes.Add(
            new MapViewPersistentRoute
            {
                ConnectionId = "SU_A01->SU_A02:0:1",
                FromRoomIndex = 1,
                FromNodeIndex = 0,
                ToRoomIndex = 2,
                ToNodeIndex = 1,
                FromRoomName = "SU_A01",
                ToRoomName = "SU_A02",
                Direction = 1,
                Ambiguous = false,
                PolicyVersion = 3,
                Kind = 2,
                FromRoomX = 10f,
                FromRoomY = 20f,
                ToRoomX = 140f,
                ToRoomY = 50f,
                StartDirectionX = 1f,
                StartDirectionY = 0f,
                EndDirectionX = -1f,
                EndDirectionY = 0f,
                Points = new[]
                {
                    new EditorMapPointSnapshot(30f, 25f),
                    new EditorMapPointSnapshot(80f, 25f),
                    new EditorMapPointSnapshot(80f, 45f)
                }
            });

        return snapshot;
    }

    private static void WriteV2(
        string path,
        string context)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);

        writer.Write(CacheMagic);
        writer.Write(2);
        writer.Write(context);
        writer.Write(4242L);
        writer.Write(1);

        writer.Write("SU_LEGACY");
        WriteStamp(
            writer,
            new MapViewFileStamp(
                "rooms/SU_LEGACY.txt",
                100L,
                200L));
        WriteStamp(
            writer,
            new MapViewFileStamp(
                "rooms/SU_LEGACY_settings.txt",
                50L,
                80L));
        writer.Write("legacy-raster");
        writer.Write(true);
        writer.Write(true);
        writer.Write(false);
        writer.Write(20);
        writer.Write(10);
        writer.Write(123);
        writer.Write(456);
        writer.Write(20f);
        writer.Write(10f);

        writer.Write(1);
        writer.Write(0f);
        writer.Write(0f);
        writer.Write(20f);
        writer.Write(10f);
        writer.Write((byte)EditorMapGeometryKind.Air);

        writer.Write(0); // terrain fill runs
        writer.Write(0); // curves
        writer.Write(1); // nodes
        writer.Write(0);
        writer.Write(19f);
        writer.Write(5f);
    }

    private static void WriteStamp(
        BinaryWriter writer,
        MapViewFileStamp stamp)
    {
        writer.Write(stamp.Path ?? string.Empty);
        writer.Write(stamp.Length);
        writer.Write(stamp.WriteTicks);
    }

    private static ulong StableHash(string value)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            if (!string.IsNullOrEmpty(value))
            {
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    hash =
                        (hash ^ (byte)c) *
                        1099511628211UL;
                    hash =
                        (hash ^ (byte)(c >> 8)) *
                        1099511628211UL;
                }
            }
            return hash;
        }
    }

    private static void Check(
        bool value,
        string message)
    {
        assertions++;
        if (!value)
            throw new InvalidOperationException(message);
    }
}
