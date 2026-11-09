using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.IO;
using System.Globalization;
using System.Diagnostics;
using Num = System.Numerics;
using DryCycle.DevUI.DevTool.RWImGui;

internal static class Program
{
    private static int assertions;

    private static int Main(string[] args)
    {
        try
        {
            StraightAndStaggered();
            ObstacleDetour();
            NearbyRoutesDoNotOvershoot();
            ParallelShortLanes();
            DenseObstacleLanes();
            CacheReuse();
            PresentedRouteSpatialSnapshot();
            CancellationAndInvalidInputs();
            ClippedStableSampling();
            NativeLayersAndSocketRecovery();
            ExactVisibilityGap();
            LayerLaneAndCrossingIsolation();
            RemovingObstaclesShortensCachedRoutes();
            RoomOverlayRoutes();
            if (args.Length > 0) ReadOnlyCachedSocketFixture(args[0]);

            Console.WriteLine(
                "PASS: " + assertions +
                " assertions; World Map routing keeps short/direct paths, obstacle avoidance, " +
                "parallel lane separation, bounded detours, cache reuse and frame-stable retained hit testing.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void RoomOverlayRoutes()
    {
        var request = new WorldMapOrthogonalRouter.Request { Id = "B5-overlay", AllowRoomCrossing = true,
            StartRoom = 1, EndRoom = 2, Start = new(140,105), End = new(140,915),
            StartDirection = new(0,-1), EndDirection = new(-1,0),
            StartRoomMin = new(0,92), StartRoomMax = new(157,180),
            EndRoomMin = new(119,715), EndRoomMax = new(400,950) };
        var obstacles = new List<WorldMapOrthogonalRouter.Obstacle> { new(3, new(-500,-500),new(500,1500)) };
        var context = new WorldMapOrthogonalRouter.CacheContext();
        WorldMapOrthogonalRouter.Route Build() => WorldMapOrthogonalRouter.BuildRoutesWithContext(
            new[] { request }, obstacles, context: context, shortestOnly: true)[0];
        var route = Build();
        Check(route.RoutingFailure == null && route.Kind != WorldMapOrthogonalRouter.RouteKind.Obstructed,
            "Intentional room-overlay paths must not be treated as obstructed.");
        Check(route.Points[0] == request.Start && route.Points[route.Points.Length - 1] == request.End,
            "Overlay paths must preserve both original sockets.");
        Check(Length(route.Points) < 950f, "B5 overlay must use a short terminal dogleg and a long trunk, without a large outer detour.");
        Check(Num.Vector2.Dot(route.Points[1] - route.Points[0], request.StartDirection) > 0 &&
            Num.Vector2.Dot(route.Points[route.Points.Length-2] - request.End, request.EndDirection) > 0,
            "Overlay routing must leave both ports in their own directions.");
        obstacles[0] = new(3,new(-800,-800),new(800,1800));
        Check(Build().Reused, "Unrelated room movement must reuse overlay routes.");
        request.StartRoomMin -= new Num.Vector2(0,20);
        Check(!Build().Reused, "Changed terminal room bounds must invalidate cached terminal stubs.");
        var directions = new[] { new Num.Vector2(1,0), new Num.Vector2(0,1), new Num.Vector2(-1,0), new Num.Vector2(0,-1) };
        foreach (var from in directions)
        foreach (var to in directions)
        {
            request.StartDirection = from; request.EndDirection = to;
            route = Build();
            Check(route.Points.Length <= 8, "Port direction handling must stay bounded to a few orthogonal segments.");
            for (int i=1;i<route.Points.Length;i++)
            {
                var delta = route.Points[i] - route.Points[i-1];
                Check(Math.Abs(delta.X) < .01f || Math.Abs(delta.Y) < .01f, "Overlay segment must stay orthogonal.");
                if (i+1 < route.Points.Length)
                    Check(Num.Vector2.Dot(delta,route.Points[i+1]-route.Points[i]) >= -.01f,
                        "Overlay route must not reverse over the previous segment.");
            }
        }
        request.StartLayer = 0; request.EndLayer = 2;
        Check(Build().Kind == WorldMapOrthogonalRouter.RouteKind.LayerBridge, "Overlay policy must retain native layer transition semantics.");
    }

    private static void NativeLayersAndSocketRecovery()
    {
        var request = Request("native-depth", new Num.Vector2(75, 50), new Num.Vector2(265, 50), 260, 0);
        request.StartLayer = request.EndLayer = 0;
        var obstacles = BaseObstacles(260);
        obstacles.Add(new WorldMapOrthogonalRouter.Obstacle(3, new Num.Vector2(110, -100),
            new Num.Vector2(230, 200), 2));
        var context = new WorldMapOrthogonalRouter.CacheContext();
        var route = WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { request }, obstacles,
            context: context, shortestOnly: true)[0];
        Check(Math.Abs(Length(route.Points) - 190f) < .1f, "Foreign native layer forced a planar detour.");
        request.EndLayer = 2;
        route = WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { request }, obstacles,
            context: context, shortestOnly: true)[0];
        Check(!route.Reused && route.Kind == WorldMapOrthogonalRouter.RouteKind.LayerBridge &&
            route.StartLayer == 0 && route.EndLayer == 2, "Layer change reused a planar cache entry.");
        Check(route.Points[0] == request.Start && route.Points[route.Points.Length - 1] == request.End &&
            Math.Abs(Length(route.Points) - 190f) < .1f, "Depth bridge moved sockets or looped around other layers.");
        request.Start += new Num.Vector2(.1f, 0);
        route = WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { request }, obstacles, context: context)[0];
        Check(!route.Reused && route.Points[0] == request.Start, "Subpixel socket edit left a detached old route.");

        request.StartLayer = request.EndLayer = 1;
        obstacles.Add(new WorldMapOrthogonalRouter.Obstacle(4, new Num.Vector2(50, 20), new Num.Vector2(95, 80), 1));
        var second = Request("unaffected", new Num.Vector2(75, 300), new Num.Vector2(265, 300), 260, 0);
        route = WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { request, second }, obstacles,
            context: new(), shortestOnly: true)[0];
        Check(route.Kind == WorldMapOrthogonalRouter.RouteKind.Obstructed && route.RoutingFailure != null,
            "Enclosed socket did not preserve the routing failure with a marked recovery.");
        Check(route.Points[0] == request.Start && route.Points[route.Points.Length - 1] == request.End,
            "Obstructed recovery did not connect the current sockets.");
        Check(WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { second }, obstacles, context: new())[0].RoutingFailure == null,
            "One obstructed socket prevented an unrelated valid link from completing.");

        // Geometry read from the SH screenshot, with the intervening room on a different native
        // layer. The gate/C11 link should not wrap around SH_S03 or terminate at an old gate position.
        var sh = new WorldMapOrthogonalRouter.Request { Id = "SH_C11->GATE_HI_SH", StartRoom = 10, EndRoom = 11,
            Start = new(606,190), End = new(451,720), StartDirection = new(-1,0), EndDirection = new(1,0),
            StartRoomMin = new(588,85), StartRoomMax = new(828,685),
            EndRoomMin = new(222,596), EndRoomMax = new(464,773), StartLayer = 1, EndLayer = 0 };
        route = WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { sh }, new[] {
            new WorldMapOrthogonalRouter.Obstacle(10, sh.StartRoomMin, sh.StartRoomMax,1),
            new WorldMapOrthogonalRouter.Obstacle(11, sh.EndRoomMin, sh.EndRoomMax,0),
            new WorldMapOrthogonalRouter.Obstacle(12,new(343,262),new(583,437),2) }, context:new(), shortestOnly:true)[0];
        Check(route.Points[route.Points.Length-1] == sh.End && Math.Abs(Length(route.Points)-685f) < .1f,
            "SH screenshot regression: depth transition must retain the live gate anchor and shortest length.");
    }

    private static void ExactVisibilityGap()
    {
        var obstacles = new List<WorldMapOrthogonalRouter.Obstacle> {
            new(10, new Num.Vector2(20,-100), new Num.Vector2(100,49)),
            new(11, new Num.Vector2(20,51), new Num.Vector2(100,200)) };
        MethodInfo solve = typeof(WorldMapOrthogonalRouter).GetMethod("SearchVisibilityCorridor",
            BindingFlags.Static | BindingFlags.NonPublic);
        var path = (Num.Vector2[])solve.Invoke(null, new object[] {
            new Num.Vector2(0,45),new Num.Vector2(150,55),1,2,obstacles,CancellationToken.None });
        Check(path.Length >= 2 && Length(path) <= 162f,
            "Exact visibility search missed a two-unit gap and chose a large raster detour.");
        Check(WorldMapOrthogonalRouter.IsDerivedRouteClear(path,1,2,obstacles), "Visibility search crossed an obstacle.");
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        bool stopped = false;
        try { solve.Invoke(null, new object[] {new Num.Vector2(0,45),new Num.Vector2(150,55),1,2,obstacles,canceled.Token}); }
        catch (TargetInvocationException error) { stopped = error.InnerException is OperationCanceledException; }
        Check(stopped, "Exact visibility search ignored cancellation.");
    }

    private static void LayerLaneAndCrossingIsolation()
    {
        ConnectionRouteResource Path(string id, int layer, Num.Vector2[] points) => new() {
            ConnectionId = id, FromLayer = layer, ToLayer = layer, BasePoints = points,
            Points = (Num.Vector2[])points.Clone(), Kind = WorldMapOrthogonalRouter.RouteKind.Orthogonal };
        var horizontal = new[] {new Num.Vector2(-60,0),new Num.Vector2(-40,0),new Num.Vector2(40,0),new Num.Vector2(60,0)};
        var vertical = new[] {new Num.Vector2(0,-60),new Num.Vector2(0,-40),new Num.Vector2(0,40),new Num.Vector2(0,60)};
        var routes = new Dictionary<string,ConnectionRouteResource> {
            ["lower"] = Path("lower",0,horizontal), ["upper"] = Path("upper",2,vertical) };
        Check(WorldMapRouteCrossingResolver.Build(routes).Marks.Length == 0,
            "Projected crossings on different layers invented a planar junction bridge.");
        routes["upper"].FromLayer = routes["upper"].ToLayer = 0;
        Check(WorldMapRouteCrossingResolver.Build(routes).Marks.Length == 1,
            "Real same-layer crossing lost its explicit over/under semantics.");
        routes["upper"].ToLayer = 2;
        Check(WorldMapRouteCrossingResolver.Build(routes).Marks.Length == 0,
            "Depth transition was inserted into planar crossing layout.");

        routes["upper"] = Path("upper",2,horizontal);
        long revision = 0;
        var changed = new HashSet<string>();
        WorldMapCorridorLaneAllocator.Apply(routes, Array.Empty<WorldMapOrthogonalRouter.Obstacle>(), changed, ref revision);
        Check(revision == 0 && changed.Count == 0 && routes.Values.All(r => r.Points.SequenceEqual(horizontal)),
            "Overlapping projections on different layers displaced each other's lanes.");
        routes["upper"].FromLayer = routes["upper"].ToLayer = 0;
        WorldMapCorridorLaneAllocator.Apply(routes, Array.Empty<WorldMapOrthogonalRouter.Obstacle>(), changed, ref revision);
        Check(revision > 0 && routes.Values.All(r => r.Points[0] == horizontal[0] &&
            r.Points[r.Points.Length-1] == horizontal[horizontal.Length-1]),
            "Same-layer lane separation must keep both original sockets attached.");
        long stable = revision;
        WorldMapCorridorLaneAllocator.Apply(routes, Array.Empty<WorldMapOrthogonalRouter.Obstacle>(), changed, ref revision);
        Check(revision == stable, "Unchanged layer layout repeatedly rebuilt route resources.");
    }

    private static void ReadOnlyCachedSocketFixture(string path)
    {
        var obstacles = new List<WorldMapOrthogonalRouter.Obstacle>();
        var requests = new List<WorldMapOrthogonalRouter.Request>();
        int obsolete = 0;
        foreach (string line in File.ReadLines(path))
        {
            string[] fields = line.Split('\t');
            int Int(int at) => int.Parse(fields[at], CultureInfo.InvariantCulture);
            Num.Vector2 Point(int at) => new(float.Parse(fields[at], CultureInfo.InvariantCulture),
                float.Parse(fields[at+1], CultureInfo.InvariantCulture));
            if (fields[0] == "O")
                obstacles.Add(new WorldMapOrthogonalRouter.Obstacle(Int(1),Point(3),Point(5),Int(2)));
            else if (fields[0] == "R")
            {
                var request = new WorldMapOrthogonalRouter.Request {Id=fields[1],StartRoom=Int(2),EndRoom=Int(3),
                    StartLayer=Int(4),EndLayer=Int(5),Start=Point(6),End=Point(8),
                    StartRoomMin=Point(10),StartRoomMax=Point(12),EndRoomMin=Point(14),EndRoomMax=Point(16)};
                request.StartDirection = WorldMapOrthogonalRouter.InferPortDirection(request.Start,request.StartRoomMin,request.StartRoomMax);
                request.EndDirection = WorldMapOrthogonalRouter.InferPortDirection(request.End,request.EndRoomMin,request.EndRoomMax);
                if (request.Start != Point(18) || request.End != Point(20)) obsolete++;
                requests.Add(request);
            }
        }
        var timer=Stopwatch.StartNew();
        var routes=WorldMapOrthogonalRouter.BuildRoutesWithContext(requests,obstacles,context:new(),shortestOnly:true);
        int recovered=0;
        for(int i=0;i<routes.Length;i++)
        {
            Check(routes[i].Points[0]==requests[i].Start && routes[i].Points[routes[i].Points.Length-1]==requests[i].End,
                "Read-only SH cache replay restored an obsolete port anchor.");
            if(routes[i].RoutingFailure!=null) recovered++;
            if(requests[i].Id=="legacy:419:1:420:0:both")
            {
                Check(routes[i].RoutingFailure==null,"Gate/C11 screenshot link still cannot find a valid corridor.");
                Check(Length(routes[i].Points)<420f,"Gate/C11 screenshot link still takes a screen-spanning loop.");
                Console.WriteLine("SH gate/C11 repaired length: "+Length(routes[i].Points).ToString("F2",CultureInfo.InvariantCulture));
            }
        }
        Check(timer.ElapsedMilliseconds<5000,"SH cache replay exceeded its bounded worker budget.");
        Console.WriteLine("Read-only SH cache replay: "+routes.Length+" links, "+obsolete+" obsolete cached anchors, "+
            recovered+" explicitly obstructed recoveries; "+timer.ElapsedMilliseconds+" ms.");
    }

    private static void RemovingObstaclesShortensCachedRoutes()
    {
        var request = Request("obstacle-moved",new Num.Vector2(75,50),new Num.Vector2(265,50),260,0);
        var obstacles=BaseObstacles(260);
        var blocking=new WorldMapOrthogonalRouter.Obstacle(3,new Num.Vector2(135,-10),new Num.Vector2(205,110));
        obstacles.Add(blocking);
        var context=new WorldMapOrthogonalRouter.CacheContext();
        var detour=WorldMapOrthogonalRouter.BuildRoutesWithContext(new[]{request},obstacles,context:context,shortestOnly:true)[0];
        Check(Length(detour.Points)>190f && WorldMapOrthogonalRouter.RouteEnvelopeTouches(detour.Points,blocking),
            "Blocking room was not indexed by its route's detour envelope.");
        obstacles.RemoveAt(obstacles.Count-1);
        var shortRoute=WorldMapOrthogonalRouter.BuildRoutesWithContext(new[]{request},obstacles,context:context,shortestOnly:true)[0];
        Check(!shortRoute.Reused && Math.Abs(Length(shortRoute.Points)-190f)<.1f,
            "Moving/removing the obstruction reused a needlessly long cached corridor.");
    }

    private static void StraightAndStaggered()
    {
        WorldMapOrthogonalRouter.Clear();
        var obstacles = BaseObstacles(144f);
        var request = Request(
            "straight",
            new Num.Vector2(75f, 50f),
            new Num.Vector2(149f, 50f),
            144f,
            0f);

        WorldMapOrthogonalRouter.Route route =
            Solve(new[] { request }, obstacles)[0];

        Check(
            route.Points.All(p => Math.Abs(p.Y - 50f) < 0.01f),
            "Facing aligned sockets must remain a straight line.");
        Check(
            Math.Abs(Length(route.Points) - 74f) < 0.01f,
            "Straight route must use the exact socket-to-socket distance.");

        request = Request(
            "staggered",
            new Num.Vector2(75f, 50f),
            new Num.Vector2(149f, 72f),
            144f,
            0f);
        route = Solve(new[] { request }, obstacles)[0];

        Check(
            route.Points[0] == new Num.Vector2(75f, 50f) &&
            route.Points[route.Points.Length - 1] == new Num.Vector2(149f, 72f),
            "Staggered route must preserve the real socket anchors.");
        Check(
            Math.Abs(Length(route.Points) - 96f) < 0.01f,
            "Staggered nearby sockets must choose a shortest orthogonal corridor.");
        Check(!HasReversal(route.Points), "Staggered route must not reverse direction.");
    }

    private static void ObstacleDetour()
    {
        WorldMapOrthogonalRouter.Clear();
        var obstacles = BaseObstacles(260f);
        obstacles.Add(
            new WorldMapOrthogonalRouter.Obstacle(
                2,
                new Num.Vector2(130f, 20f),
                new Num.Vector2(210f, 100f)));

        var request = Request(
            "blocked",
            new Num.Vector2(75f, 60f),
            new Num.Vector2(265f, 60f),
            260f,
            0f);

        WorldMapOrthogonalRouter.Route route =
            Solve(new[] { request }, obstacles)[0];

        Check(!HasReversal(route.Points), "Blocked route must not reverse toward its source.");
        Check(
            !Crosses(route.Points, new Num.Vector2(130f, 20f), new Num.Vector2(210f, 100f)),
            "Blocked route must not cut through the obstacle room.");
        Check(
            Length(route.Points) < 520f,
            "Blocked route must stay locally bounded instead of taking a screen-spanning detour.");
    }

    private static void NearbyRoutesDoNotOvershoot()
    {
        WorldMapOrthogonalRouter.Clear();

        var request = new WorldMapOrthogonalRouter.Request
        {
            Id = "nearby-left-down",
            StartRoom = 0,
            EndRoom = 1,
            StartRoomMin = new Num.Vector2(200f, 0f),
            StartRoomMax = new Num.Vector2(280f, 100f),
            EndRoomMin = new Num.Vector2(0f, 140f),
            EndRoomMax = new Num.Vector2(80f, 240f),
            Start = new Num.Vector2(205f, 50f),
            End = new Num.Vector2(75f, 190f),
            StartDirection = -Num.Vector2.UnitX,
            EndDirection = Num.Vector2.UnitX,
            LaneOffset = 0f,
            StartTerminalLaneIndex = 0,
            StartTerminalLaneCount = 1,
            EndTerminalLaneIndex = 0,
            EndTerminalLaneCount = 1
        };

        var obstacles = new List<WorldMapOrthogonalRouter.Obstacle>
        {
            new(
                0,
                request.StartRoomMin,
                request.StartRoomMax),
            new(
                1,
                request.EndRoomMin,
                request.EndRoomMax)
        };

        Num.Vector2[] points =
            Solve(new[] { request }, obstacles)[0].Points;

        float minX = points.Min(p => p.X);
        float maxX = points.Max(p => p.X);
        float minY = points.Min(p => p.Y);
        float maxY = points.Max(p => p.Y);
        float manhattan =
            Math.Abs(request.Start.X - request.End.X) +
            Math.Abs(request.Start.Y - request.End.Y);

        Check(
            minX >= Math.Min(request.Start.X, request.End.X) - 0.01f &&
            maxX <= Math.Max(request.Start.X, request.End.X) + 0.01f,
            "A clear nearby link must not travel horizontally away from both endpoints before coming back.");
        Check(
            minY >= Math.Min(request.Start.Y, request.End.Y) - 0.01f &&
            maxY <= Math.Max(request.Start.Y, request.End.Y) + 0.01f,
            "A clear nearby link must not travel vertically away from both endpoints before coming back.");
        Check(
            Length(points) <= manhattan + 0.01f,
            "A clear nearby link must use a Manhattan-shortest route instead of an unnecessary outer detour.");
    }

    private static void ParallelShortLanes()
    {
        WorldMapOrthogonalRouter.Clear();
        var obstacles = BaseObstacles(144f);
        float[] offsets = { -12f, 0f, 12f };
        var requests = offsets
            .Select((offset, i) =>
                Request(
                    "short-lane-" + i,
                    new Num.Vector2(75f, 50f),
                    new Num.Vector2(149f, 50f),
                    144f,
                    offset))
            .ToArray();

        WorldMapOrthogonalRouter.Route[] routes =
            Solve(requests, obstacles);

        var ys = new List<float>();
        for (int i = 0; i < routes.Length; i++)
        {
            Num.Vector2[] points = routes[i].Points;
            Check(
                points[0] == new Num.Vector2(75f, 50f) &&
                points[points.Length - 1] == new Num.Vector2(149f, 50f),
                "Parallel short routes must retain real socket anchors.");
            Check(!HasReversal(points), "Parallel short routes must never backtrack.");

            float longestHorizontal = -1f;
            float corridorY = points[0].Y;
            for (int p = 0; p + 1 < points.Length; p++)
            {
                if (Math.Abs(points[p].Y - points[p + 1].Y) > 0.01f)
                    continue;

                float length = Math.Abs(points[p + 1].X - points[p].X);
                if (length <= longestHorizontal)
                    continue;

                longestHorizontal = length;
                corridorY = points[p].Y;
            }
            ys.Add(corridorY);
        }

        Check(
            ys.Max() - ys.Min() >= 18f,
            "Parallel short links must preserve visibly separated lane corridors.");
    }

    private static void DenseObstacleLanes()
    {
        WorldMapOrthogonalRouter.Clear();
        var obstacles = BaseObstacles(260f);
        obstacles.Add(
            new WorldMapOrthogonalRouter.Obstacle(
                2,
                new Num.Vector2(130f, 20f),
                new Num.Vector2(210f, 100f)));

        float[] offsets = { -18f, -6f, 6f, 18f };
        var requests = offsets
            .Select((offset, i) =>
                Request(
                    "dense-" + i,
                    new Num.Vector2(75f, 60f),
                    new Num.Vector2(265f, 60f),
                    260f,
                    offset))
            .ToArray();

        WorldMapOrthogonalRouter.Route[] routes =
            Solve(requests, obstacles);

        var signatures = new HashSet<string>(StringComparer.Ordinal);
        foreach (WorldMapOrthogonalRouter.Route route in routes)
        {
            Num.Vector2[] points = route.Points;
            Check(!HasReversal(points), "Dense obstacle routes must not reverse direction.");
            Check(
                !Crosses(points, new Num.Vector2(130f, 20f), new Num.Vector2(210f, 100f)),
                "Dense obstacle routes must not enter the blocking room.");
            Check(
                Length(points) < 520f,
                "Dense obstacle routes must stay inside the bounded local detour envelope.");

            signatures.Add(
                string.Join(
                    ";",
                    points.Select(
                        point =>
                            Math.Round(point.X, 1) + "," +
                            Math.Round(point.Y, 1))));
        }

        Console.WriteLine(
            "dense lanes: " +
            string.Join(
                " | ",
                routes.Select(
                    route =>
                        string.Join(
                            " -> ",
                            route.Points.Select(
                                point =>
                                    "(" + point.X.ToString("F1") + "," +
                                    point.Y.ToString("F1") + ")")))));

        Check(
            signatures.Count >= 3,
            "Four dense parallel connections must retain multiple distinct corridors.");
    }

    private static void PresentedRouteSpatialSnapshot()
    {
        var presented = new WorldMapRouteSpatialCore();
        var live = new WorldMapRouteSpatialCore();

        Num.Vector2[] oldFrame =
        {
            new Num.Vector2(10f, 20f),
            new Num.Vector2(110f, 20f)
        };
        Num.Vector2[] newLive =
        {
            new Num.Vector2(10f, 80f),
            new Num.Vector2(110f, 80f)
        };

        presented.Upsert("route", oldFrame);
        live.Upsert("route", oldFrame);
        live.Upsert("route", newLive);

        Check(
            presented.TryHit(
                new Num.Vector2(60f, 20f),
                4f,
                allowedIds: null,
                out string presentedId,
                out float _) &&
            presentedId == "route",
            "Presented-route hit testing must remain on the route geometry that produced the visible surface.");

        Check(
            !presented.TryHit(
                new Num.Vector2(60f, 80f),
                4f,
                allowedIds: null,
                out string _,
                out float _),
            "Presented-route hit testing must not jump to a newer live route before the surface redraws.");

        Check(
            live.TryHit(
                new Num.Vector2(60f, 80f),
                4f,
                allowedIds: null,
                out string liveId,
                out float _) &&
            liveId == "route",
            "Live route spatial state may advance independently while the presented frame stays stable.");

        Check(
            presented.TryGetPoints("route", out Num.Vector2[] presentedPoints) &&
            ReferenceEquals(presentedPoints, oldFrame),
            "Presented-route marker lookup must retain the exact immutable point array for the rendered frame.");
    }

    private static void CacheReuse()
    {
        WorldMapOrthogonalRouter.Clear();
        var obstacles = BaseObstacles(144f);
        var request = Request(
            "cache",
            new Num.Vector2(75f, 50f),
            new Num.Vector2(149f, 72f),
            144f,
            0f);

        _ = Solve(new[] { request }, obstacles)[0];
        WorldMapOrthogonalRouter.Route second =
            Solve(new[] { request }, obstacles)[0];

        Check(second.Reused, "Stable route must be reused from the router cache.");
    }

    private static void CancellationAndInvalidInputs()
    {
        var context = new WorldMapOrthogonalRouter.CacheContext();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool canceled = false;
        try { WorldMapOrthogonalRouter.BuildRoutesWithContext(Array.Empty<WorldMapOrthogonalRouter.Request>(),
            Array.Empty<WorldMapOrthogonalRouter.Obstacle>(), context: context, cancellation: stop.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Check(canceled && context.Generation == 0, "Canceled solve mutated the route cache.");

        var request = Request("invalid", new Num.Vector2(float.NaN, 0f), new Num.Vector2(149f, 72f), 144f, 0f);
        bool rejected = false;
        try { WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { request }, BaseObstacles(144f), context: context); }
        catch (ArgumentException error) { rejected = error.Message.Contains("invalid") && error.Message.Contains("non-finite"); }
        Check(rejected && context.Routes.Count == 0, "Invalid geometry was published or its cause was lost.");
        rejected = false;
        request = Request("overflow", new Num.Vector2(75f, 50f), new Num.Vector2(1e25f, 72f), 1e25f, 0f);
        try { WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { request }, BaseObstacles(1e25f), context: context); }
        catch (InvalidOperationException error) { rejected = error.Message.Contains("overflow"); }
        Check(rejected && context.Routes.Count == 0, "Overflowing derived geometry reached the cache or renderer.");

        // Cancel while the real occupancy sampler is walking a very long seed path.
        using var activeStop = new CancellationTokenSource();
        using var seedRead = new ManualResetEventSlim();
        var seeds = new SignalingSeedList(seedRead);
        var work = System.Threading.Tasks.Task.Run(() =>
            WorldMapOrthogonalRouter.BuildRoutesWithContext(new[] { Request("cancel-seed", new(75f, 50f), new(149f, 72f), 144f, 0f) },
                BaseObstacles(144f), occupancySeedPaths: seeds, context: new(), cancellation: activeStop.Token), activeStop.Token);
        Check(seedRead.Wait(3000), "Occupancy worker never started."); activeStop.Cancel();
        Check(SpinWait.SpinUntil(() => work.IsCompleted, 3000), "Long-segment sampling ignored cancellation.");
        Check(work.IsCanceled, "Canceled seed solve reported success or an unrelated failure.");
    }

    private sealed class SignalingSeedList : IReadOnlyList<Num.Vector2[]>
    {
        private readonly ManualResetEventSlim read;
        internal SignalingSeedList(ManualResetEventSlim read) => this.read = read;
        public int Count => 1;
        public Num.Vector2[] this[int index]
        {
            get { read.Set(); return new[] { new Num.Vector2(-100000000f, 0f), new Num.Vector2(100000000f, 0f) }; }
        }
        public IEnumerator<Num.Vector2[]> GetEnumerator() { yield return this[0]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private static void ClippedStableSampling()
    {
        MethodInfo sample = typeof(WorldMapOrthogonalRouter).GetMethod("BuildStableCells", BindingFlags.Static | BindingFlags.NonPublic);
        HashSet<long> Run(float y)
        {
            var old = new WorldMapOrthogonalRouter.Route
            { Points = new[] { new Num.Vector2(-100000000f, y), new Num.Vector2(100000000f, y) } };
            return (HashSet<long>)sample.Invoke(null, new object[] { old, Num.Vector2.Zero, 14f, 112, 112, CancellationToken.None });
        }
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Check(Run(50f).Count == 112, "Clipped sampling lost cells inside the bounded search grid.");
        Check(Run(-1000f).Count == 0, "An outside route incorrectly painted the search-grid border as stable.");
        Check(timer.ElapsedMilliseconds < 3000, "Stable sampling scales with the complete old route length.");
    }

    private static WorldMapOrthogonalRouter.Request Request(
        string id,
        Num.Vector2 start,
        Num.Vector2 end,
        float endRoomX,
        float laneOffset)
    {
        return new WorldMapOrthogonalRouter.Request
        {
            Id = id,
            StartRoom = 0,
            EndRoom = 1,
            StartRoomMin = new Num.Vector2(0f, 0f),
            StartRoomMax = new Num.Vector2(80f, 120f),
            EndRoomMin = new Num.Vector2(endRoomX, 0f),
            EndRoomMax = new Num.Vector2(endRoomX + 80f, 120f),
            Start = start,
            End = end,
            StartDirection = Num.Vector2.UnitX,
            EndDirection = -Num.Vector2.UnitX,
            LaneOffset = laneOffset,
            StartTerminalLaneIndex = 0,
            StartTerminalLaneCount = 1,
            StartTerminalExtraDepth = 0f,
            EndTerminalLaneIndex = 0,
            EndTerminalLaneCount = 1,
            EndTerminalExtraDepth = 0f
        };
    }

    private static List<WorldMapOrthogonalRouter.Obstacle> BaseObstacles(
        float endRoomX)
    {
        return new List<WorldMapOrthogonalRouter.Obstacle>
        {
            new(
                0,
                new Num.Vector2(0f, 0f),
                new Num.Vector2(80f, 120f)),
            new(
                1,
                new Num.Vector2(endRoomX, 0f),
                new Num.Vector2(endRoomX + 80f, 120f))
        };
    }

    private static WorldMapOrthogonalRouter.Route[] Solve(
        IReadOnlyList<WorldMapOrthogonalRouter.Request> requests,
        IReadOnlyList<WorldMapOrthogonalRouter.Obstacle> obstacles) =>
        WorldMapOrthogonalRouter.BuildRoutesCore(
            requests,
            obstacles,
            sourceObstaclesAlreadyInflated: false,
            occupancySeedPaths: null,
            avoidanceSeedPaths: null);

    private static bool HasReversal(Num.Vector2[] points)
    {
        for (int i = 1; i + 1 < points.Length; i++)
        {
            Num.Vector2 before = points[i] - points[i - 1];
            Num.Vector2 after = points[i + 1] - points[i];
            if (Num.Vector2.Dot(before, after) < -0.01f)
                return true;
        }
        return false;
    }

    private static bool Crosses(
        Num.Vector2[] points,
        Num.Vector2 min,
        Num.Vector2 max)
    {
        for (int i = 0; i + 1 < points.Length; i++)
        {
            if (SegmentIntersectsRect(points[i], points[i + 1], min, max))
                return true;
        }
        return false;
    }

    private static bool SegmentIntersectsRect(
        Num.Vector2 a,
        Num.Vector2 b,
        Num.Vector2 min,
        Num.Vector2 max)
    {
        if (Math.Abs(a.X - b.X) < 0.01f)
        {
            if (a.X <= min.X || a.X >= max.X) return false;
            return Math.Max(a.Y, b.Y) > min.Y &&
                   Math.Min(a.Y, b.Y) < max.Y;
        }

        if (Math.Abs(a.Y - b.Y) < 0.01f)
        {
            if (a.Y <= min.Y || a.Y >= max.Y) return false;
            return Math.Max(a.X, b.X) > min.X &&
                   Math.Min(a.X, b.X) < max.X;
        }

        // Router output is expected to be orthogonal. Treat any unexpected diagonal conservatively
        // using segment AABB overlap so this test still fails if it enters the blocker.
        Num.Vector2 segMin = Num.Vector2.Min(a, b);
        Num.Vector2 segMax = Num.Vector2.Max(a, b);
        return segMax.X > min.X && segMin.X < max.X &&
               segMax.Y > min.Y && segMin.Y < max.Y;
    }

    private static float Length(Num.Vector2[] points)
    {
        float length = 0f;
        for (int i = 0; i + 1 < points.Length; i++)
            length += Num.Vector2.Distance(points[i], points[i + 1]);
        return length;
    }

    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value)
            throw new InvalidOperationException(message);
    }
}
