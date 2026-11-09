using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Num = System.Numerics;
using DryCycle.DevUI.DevTool.RWImGui;

namespace DryCycle.DevUI.DevTool.RWImGui
{
    // The production adapter consumes these exact game-host member names.
    internal sealed class WorldMapScene
    {
        internal sealed class RoomNode
        {
            internal int RoomIndex, Layer;
            internal Num.Vector2 Min, Max;
        }
        internal readonly Dictionary<int, RoomNode> Rooms = new();
        internal bool TryGetRoom(int id, out RoomNode room) => Rooms.TryGetValue(id, out room);
    }
    internal sealed class WorldMapRoomResourceStore { }
    internal static class WorldMapWorldSpaceRouter
    {
        internal static void GetRoomBounds(WorldMapScene.RoomNode room,
            WorldMapRoomResourceStore resources,
            out Num.Vector2 min, out Num.Vector2 max)
        {
            min = room.Min; max = room.Max;
        }
    }

    internal sealed class ConnectionRouteResource
    {
        internal string ConnectionId;
        internal Num.Vector2[] Points;
        internal int FromLayer = 1, ToLayer = 1;
        internal WorldMapOrthogonalRouter.RouteKind Kind;
    }
}

internal static class Program
{
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
    }

    private static void Main()
    {
        var index = new WorldMapRouteSpatialIndex();
        Check(typeof(WorldMapRouteSpatialIndex).Assembly !=
              typeof(WorldMapRouteSpatialCore).Assembly,
            "The game-facing adapter and Rainwright spatial core have duplicate ownership.");
        index.Upsert(new ConnectionRouteResource
        {
            ConnectionId = "SU_01->SU_02",
            Points = new[] { new Num.Vector2(0, 0), new Num.Vector2(30, 0) }
        });
        Check(index.Count == 1, "Game route resource was not published to Rainwright.");
        var found = new List<string>();
        Check(index.Query(new Num.Vector2(8, -2), new Num.Vector2(15, 2), found) &&
              found.Count == 1 && found[0] == "SU_01->SU_02",
            "Host scene bridge did not expose the Rainwright route spatial query.");
        var onlyWrongId = new HashSet<string>(StringComparer.Ordinal) { "unrelated" };
        Check(!index.TryHit(new Num.Vector2(10, 1), 3, onlyWrongId, out _, out _),
            "Hover selection ignored the visible-route filter.");
        Check(index.TryHit(new Num.Vector2(10, 1), 3, null, out string id, out _) &&
              id == "SU_01->SU_02",
            "Host-side route hover could not reach the independently compiled index.");
        index.Upsert((ConnectionRouteResource)null);
        Check(index.Count == 1, "Null game route should be a no-op.");
        index.Remove("SU_01->SU_02");
        Check(index.Count == 0, "Host route removal did not reach the Rainwright index.");
        // Compile the real game-scene index adapter while consuming the
        // already compiled Rainwright.dll room backend.
        var scene = new WorldMapScene();
        scene.Rooms[10] = new WorldMapScene.RoomNode
        {
            RoomIndex = 10, Layer = 0,
            Min = new Num.Vector2(-32, 0), Max = new Num.Vector2(32, 60)
        };
        scene.Rooms[12] = new WorldMapScene.RoomNode
        {
            RoomIndex = 12, Layer = 2,
            Min = new Num.Vector2(-32, 0), Max = new Num.Vector2(32, 60)
        };
        var resourceStore = new WorldMapRoomResourceStore();
        var dirtyRooms = new WorldMapDirtySet { FullRebuild = true };
        var roomIndex = new WorldMapSpatialIndex();
        Check(typeof(WorldMapRoomSpatialCore).Assembly !=
              typeof(WorldMapSpatialIndex).Assembly,
            "Host room index cannot duplicate the Rainwright spatial core.");
        roomIndex.ApplyDirty(scene, resourceStore, dirtyRooms);
        Check(roomIndex.Count == 2 &&
              roomIndex.TryHitRoom(new Num.Vector2(0, 30), 1 << 2, out int selected) &&
              selected == 12,
            "Room hover lost visible layer selection or room bounds.");
        scene.Rooms[12].Min = new Num.Vector2(500, 500);
        scene.Rooms[12].Max = new Num.Vector2(550, 550);
        var changed = new WorldMapDirtySet();
        changed.RoomTransforms.Add(12);
        roomIndex.ApplyDirty(scene, resourceStore, changed);
        Check(roomIndex.TryHitRoom(new Num.Vector2(0, 30), int.MaxValue, out selected) &&
              selected == 10,
            "Room move left the old spatial cells occupied.");
        // RoomPorts alone only fills an absent index, preserving stable bounds.
        scene.Rooms[10].Min = new Num.Vector2(800, 800);
        scene.Rooms[10].Max = new Num.Vector2(850, 850);
        changed.Clear();
        changed.RoomPorts.Add(10);
        roomIndex.ApplyDirty(scene, resourceStore, changed);
        Check(roomIndex.TryHitRoom(new Num.Vector2(0, 30), 1, out selected) &&
              selected == 10,
            "Port-only update unexpectedly replaced stable spatial bounds.");
        roomIndex.InvalidateRooms(scene, resourceStore, new[] { 10 });
        Check(!roomIndex.TryHitRoom(new Num.Vector2(0, 30), 1, out _) &&
              roomIndex.TryHitRoom(new Num.Vector2(810, 810), 1, out selected) &&
              selected == 10,
            "Explicit room invalidation failed to update bounds.");
        changed.Clear();
        changed.RemovedRooms.Add(10);
        scene.Rooms.Remove(10);
        roomIndex.ApplyDirty(scene, resourceStore, changed);
        Check(roomIndex.Count == 1 && !roomIndex.TryHitRoom(
                  new Num.Vector2(810, 810), 1, out _),
            "Deleted room still occupies retained spatial buckets.");
        roomIndex.Reset();
        Check(roomIndex.Count == 0, "Room index reset leaked spatial cells.");

        // The game-only route wrapper exposes final point arrays; crossing
        // budgets/detection must run inside the independently compiled DLL.
        var crossings = new Dictionary<string, ConnectionRouteResource>
        {
            ["horizontal"] = new ConnectionRouteResource
            {
                ConnectionId = "horizontal",
                Points = new[]
                {
                    new Num.Vector2(0, 80), new Num.Vector2(20, 80),
                    new Num.Vector2(160, 80), new Num.Vector2(180, 80)
                }
            },
            ["vertical"] = new ConnectionRouteResource
            {
                ConnectionId = "vertical",
                Points = new[]
                {
                    new Num.Vector2(80, 0), new Num.Vector2(80, 20),
                    new Num.Vector2(80, 160), new Num.Vector2(80, 180)
                }
            }
        };
        Check(typeof(WorldMapRouteCrossingCore).Assembly.GetName().Name == "RainWright" &&
              typeof(WorldMapRouteCrossingCore).Assembly !=
              typeof(WorldMapRouteCrossingResolver).Assembly,
            "Crossing marker solver must be owned by Rainwright, not by host.");
        var bridgeMarks = WorldMapRouteCrossingResolver.Build(crossings);
        Check(bridgeMarks.CandidateChecks > 0 &&
              bridgeMarks.Marks.Length > 0 && !bridgeMarks.BudgetLimited,
            "Production crossing adapter did not detect an eligible internal route intersection.");
        Check(WorldMapRouteCrossingResolver.Build(
                  new Dictionary<string, ConnectionRouteResource>()).Marks.Length == 0,
            "Empty map connections must not fabricate crossing marks.");

        // An overview can cover billions of empty hash cells. Broad objects and wide views must
        // retain exact filtering/hit results without indexing/scanning their complete grid area.
        var wide = new WorldMapRouteSpatialIndex();
        var timer = Stopwatch.StartNew();
        wide.Upsert(new ConnectionRouteResource {ConnectionId="long",Points=new[] {new Num.Vector2(-10000000),new Num.Vector2(10000000)}});
        wide.Upsert(new ConnectionRouteResource {ConnectionId="local",Points=new[] {Num.Vector2.Zero,new Num.Vector2(20,0)}});
        Check(wide.Query(new Num.Vector2(-1e12f),new Num.Vector2(1e12f),found) && found.SequenceEqual(new[] {"local","long"}),
            "Wide overview dropped a route from the direct-scan path.");
        Check(wide.TryHit(new Num.Vector2(5000000),3,null,out id,out _) && id=="long",
            "An unindexed long route is no longer selectable.");
        wide.Remove("long");
        Check(!wide.TryHit(new Num.Vector2(5000000),3,null,out _,out _),"Removing an unindexed route left it selectable.");
        // Integer cell saturation at far coordinates must not wrap an int loop forever.
        var far=new Num.Vector2(1e12f);
        wide.Upsert(new ConnectionRouteResource {ConnectionId="far",Points=new[] {far,far+new Num.Vector2(262144,0)}});
        Check(wide.Query(far-new Num.Vector2(65536),far+new Num.Vector2(524288),found) && found.Contains("far"),
            "A far finite route was lost at the cell-index boundary.");

        var broadRooms=new WorldMapRoomSpatialCore();
        broadRooms.ApplyBatch(true,null,new[] {new WorldMapRoomIndexUpdate(1,0,new Num.Vector2(-10000000),new Num.Vector2(10000000)),
            new WorldMapRoomIndexUpdate(2,2,new Num.Vector2(-10),new Num.Vector2(10))});
        var roomHits=new List<int>();
        Check(broadRooms.Query(new Num.Vector2(-1e12f),new Num.Vector2(1e12f),1,roomHits) && roomHits.SequenceEqual(new[] {1}),
            "Large-room broad query lost layer filtering.");
        Check(broadRooms.TryHitRoom(new Num.Vector2(2000000),1,out selected) && selected==1,"Large-room picking ignored unindexed rooms.");
        Check(broadRooms.TryHitRoom(Num.Vector2.Zero,4,out selected) && selected==2,"Local-grid picking changed layer priority.");
        Check(timer.ElapsedMilliseconds<1000,"Sparse-world queries still scale with empty grid area.");
        Console.WriteLine("Wide sparse queries and long-object updates: "+timer.Elapsed.TotalMilliseconds.ToString("F2")+" ms.");

        // Compare both query strategies against exhaustive bounds filtering over many viewport sizes.
        var random=new Random(519);
        var updates=new List<WorldMapRoomIndexUpdate>();
        for(int n=0;n<150;n++) {var p=new Num.Vector2(random.Next(-5000,5000),random.Next(-5000,5000));updates.Add(new WorldMapRoomIndexUpdate(n,n%3,p,p+new Num.Vector2(120)));}
        broadRooms.ApplyBatch(true,null,updates);
        for(int n=0;n<80;n++)
        {
            var p=new Num.Vector2(random.Next(-5000,5000),random.Next(-5000,5000));var q=p+new Num.Vector2(n%2==0?180:20000);
            broadRooms.Query(p,q,3,roomHits);
            var expected=updates.Where(u=>u.Layer<2 && u.Max.X>=p.X && u.Min.X<=q.X && u.Max.Y>=p.Y && u.Min.Y<=q.Y).OrderBy(u=>u.Layer).ThenBy(u=>u.RoomIndex).Select(u=>u.RoomIndex);
            Check(roomHits.SequenceEqual(expected),"Adaptive room query differs from exhaustive filtering.");
        }

        SparseCrossings();
        Console.WriteLine("PASS: room/route spatial and crossing adapters use Rainwright.dll.");
    }

    private static void SparseCrossings()
    {
        ConnectionRouteResource Horizontal(string id, float x, float y, float radius) => new()
        { ConnectionId = id, Points = new[] { new Num.Vector2(x-radius-100f,y), new Num.Vector2(x-radius,y),
            new Num.Vector2(x+radius,y), new Num.Vector2(x+radius+100f,y) } };
        ConnectionRouteResource Vertical(string id, float x, float y, float radius) => new()
        { ConnectionId = id, Points = new[] { new Num.Vector2(x,y-radius-100f), new Num.Vector2(x,y-radius),
            new Num.Vector2(x,y+radius), new Num.Vector2(x,y+radius+100f) } };
        var timer = Stopwatch.StartNew();
        foreach (var fixture in new[] { (0f,0f,1e8f,150f), (0f,0f,150f,1e8f),
            (0f,0f,1e12f,1e12f), (1e12f,1e12f,1e7f,1e7f), (-1e12f,-1e12f,1e7f,1e7f) })
        {
            var paths = new Dictionary<string, ConnectionRouteResource>
            { ["horizontal"] = Horizontal("horizontal",fixture.Item1,fixture.Item2,fixture.Item3),
              ["vertical"] = Vertical("vertical",fixture.Item1,fixture.Item2,fixture.Item4) };
            var result = WorldMapRouteCrossingResolver.Build(paths);
            Check(result.Marks.Length == 1 && !result.BudgetLimited, "Sparse long/far routes lost a real crossing.");
            Check(result.Marks[0].Point == new Num.Vector2(fixture.Item1,fixture.Item2), "Far crossing was moved by cell saturation.");
        }
        Check(timer.ElapsedMilliseconds < 3000, "Crossing insertion scaled with empty world-space cells.");

        // Regular mixed lengths must match exhaustive axis intersection, not lose
        // markers at the adaptive indexing boundary.
        var random = new Random(20261014);
        for (int fixture = 0; fixture < 30; fixture++)
        {
            var paths = new Dictionary<string, ConnectionRouteResource>();
            var expected = new HashSet<Num.Vector2>();
            float[] xs = new float[8], ys = new float[8];
            for (int i = 0; i < 8; i++)
            {
                xs[i] = random.Next(-200, 201) + i * .03125f;
                ys[i] = random.Next(-200, 201) + i * .03125f;
                paths.Add("v"+i,Vertical("v"+i,xs[i],0f,i%2==0?500f:1e8f));
                paths.Add("h"+i,Horizontal("h"+i,0f,ys[i],i%2==0?500f:1e8f));
            }
            foreach (float x in xs) foreach (float y in ys) expected.Add(new Num.Vector2(x,y));
            var output = WorldMapRouteCrossingResolver.Build(paths);
            Check(!output.BudgetLimited && expected.SetEquals(output.Marks.Select(mark => mark.Point)), "Adaptive crossings differ from exhaustive intersections.");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool stopped = false;
        try { WorldMapRouteCrossingResolver.Build(new Dictionary<string, ConnectionRouteResource>(), canceled.Token); }
        catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Crossing worker ignored cancellation.");
        var dense = new Dictionary<string, ConnectionRouteResource>();
        for (int i = 0; i < 200; i++)
        {
            dense.Add("horizontal-"+i, Horizontal("horizontal-"+i,0f,i*300f,1e8f));
            dense.Add("vertical-"+i, Vertical("vertical-"+i,i*300f,0f,1e8f));
        }
        var unchanged = dense["horizontal-0"].Points;
        var limited = WorldMapRouteCrossingResolver.Build(dense);
        Check(limited.BudgetLimited && limited.Marks.Length <= 1024, "Sparse dense crossings exceeded the presentation budget.");
        Check(dense.Count == 400 && ReferenceEquals(dense["horizontal-0"].Points, unchanged), "Crossing LOD changed source connections or geometry.");
        Console.WriteLine("Sparse crossing stress completed in " + timer.Elapsed.TotalMilliseconds.ToString("F2") + " ms.");
    }
}
