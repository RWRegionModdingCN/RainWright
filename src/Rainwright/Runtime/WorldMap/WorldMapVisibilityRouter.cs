using System;
using System.Collections.Generic;
using System.Threading;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

internal static partial class WorldMapOrthogonalRouter
{
    private static long ObstacleFingerprint(List<Obstacle> obstacles)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            foreach (Obstacle obstacle in obstacles)
            {
                hash = (hash ^ (uint)obstacle.RoomIndex) * 1099511628211UL;
                hash = (hash ^ (uint)obstacle.Layer) * 1099511628211UL;
                hash = (hash ^ (uint)obstacle.Min.X.GetHashCode()) * 1099511628211UL;
                hash = (hash ^ (uint)obstacle.Min.Y.GetHashCode()) * 1099511628211UL;
                hash = (hash ^ (uint)obstacle.Max.X.GetHashCode()) * 1099511628211UL;
                hash = (hash ^ (uint)obstacle.Max.Y.GetHashCode()) * 1099511628211UL;
            }
            return (long)hash;
        }
    }

    internal static bool RouteEnvelopeTouches(Num.Vector2[] points, Obstacle obstacle)
    {
        if (points == null || points.Length < 2) return false;
        Num.Vector2 min = points[0], max = points[0];
        for (int i = 1; i < points.Length; i++)
        { min = Num.Vector2.Min(min, points[i]); max = Num.Vector2.Max(max, points[i]); }
        return obstacle.IntersectsBounds(min, max, 32f);
    }

    // A room on another native map layer does not obstruct this corridor. Cross-layer links
    // leave the plane at their source socket and re-enter it at their destination socket.
    internal static List<Obstacle> SelectObstacles(IReadOnlyList<Obstacle> obstacles,
        int startRoom, int endRoom, int startLayer, int endLayer)
    {
        var selected = new List<Obstacle>();
        if (obstacles == null) return selected;
        foreach (Obstacle obstacle in obstacles)
            if (obstacle.RoomIndex == startRoom || obstacle.RoomIndex == endRoom ||
                startLayer == endLayer && obstacle.Layer == startLayer)
                selected.Add(obstacle);
        return selected;
    }

    // Used for intentional depth transitions, and as an explicitly marked recovery when no
    // clear planar corridor exists. It never moves author sockets or retries each frame.
    private static Route BuildUnobstructedLink(Request request, RouteKind kind)
    {
        Num.Vector2 bend = Math.Abs(request.StartDirection.X) > .5f
            ? new Num.Vector2(request.End.X, request.Start.Y)
            : new Num.Vector2(request.Start.X, request.End.Y);
        return NewRoute(request, kind, new[] { request.Start, bend, request.End },
            request.StartDirection, request.EndDirection);
    }

    // World-map presentation may cross room silhouettes. Ten fixed candidates replace obstacle
    // search, while short boundary stubs keep both port directions and every socket anchor intact.
    private static Route BuildRoomOverlayLink(Request request)
    {
        Num.Vector2 startDirection = Cardinalize(request.StartDirection, request.End - request.Start);
        Num.Vector2 endDirection = Cardinalize(request.EndDirection, request.Start - request.End);
        Num.Vector2 a = NearBoundary(request.Start, startDirection, request.StartRoomMin, request.StartRoomMax);
        Num.Vector2 b = NearBoundary(request.End, endDirection, request.EndRoomMin, request.EndRoomMax);
        Num.Vector2 min = Num.Vector2.Min(a, b), max = Num.Vector2.Max(a, b);
        Num.Vector2[] best = null;
        float bestScore = float.PositiveInfinity;
        void Consider(Num.Vector2[] middle)
        {
            var full = new Num.Vector2[middle.Length + 2];
            full[0] = request.Start; Array.Copy(middle, 0, full, 1, middle.Length); full[full.Length - 1] = request.End;
            var candidate = Simplify(full);
            if (HasReversal(candidate)) return;
            float score = PathLength(candidate) + Math.Max(0, candidate.Length - 2) * 8f;
            if (score < bestScore) { best = SimplifyRoute(full); bestScore = score; }
        }
        foreach (float x in new[] { a.X, b.X, (a.X + b.X) * .5f, min.X - 12f, max.X + 12f })
            Consider(new[] { a, new Num.Vector2(x, a.Y), new Num.Vector2(x, b.Y), b });
        foreach (float y in new[] { a.Y, b.Y, (a.Y + b.Y) * .5f, min.Y - 12f, max.Y + 12f })
            Consider(new[] { a, new Num.Vector2(a.X, y), new Num.Vector2(b.X, y), b });
        if (best == null)
            throw new InvalidOperationException("Connection '" + request.Id + "' has no valid orthogonal port-direction candidate.");
        return NewRoute(request, request.StartLayer == request.EndLayer ? RouteKind.Compact : RouteKind.LayerBridge,
            best, startDirection, endDirection);
    }

    // Exact obstacle edges form a compressed rectilinear visibility grid. Unlike the coarse
    // raster search it can use narrow gaps and cannot miss a corridor through cell rounding.
    // Search has finite states, strictly positive edge costs, cancellation and a fixed work cap.
    private static Num.Vector2[] SearchVisibilityCorridor(Num.Vector2 start, Num.Vector2 end,
        int startRoom, int endRoom, List<Obstacle> obstacles, CancellationToken cancellation)
    {
        const int coordinateLimit = 96, expansionLimit = 8192;
        Num.Vector2 min = Num.Vector2.Min(start, end) - new Num.Vector2(SearchPadding);
        Num.Vector2 max = Num.Vector2.Max(start, end) + new Num.Vector2(SearchPadding);
        var xs = new SortedSet<float> { start.X, end.X };
        var ys = new SortedSet<float> { start.Y, end.Y };
        foreach (Obstacle obstacle in obstacles)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!obstacle.IntersectsBounds(min, max)) continue;
            Num.Vector2 lo = obstacle.Min, hi = obstacle.Max;
            if (obstacle.RoomIndex == startRoom || obstacle.RoomIndex == endRoom)
            { lo += new Num.Vector2(ObstacleMargin); hi -= new Num.Vector2(ObstacleMargin); }
            xs.Add(lo.X - 1f); xs.Add(hi.X + 1f);
            ys.Add(lo.Y - 1f); ys.Add(hi.Y + 1f);
            if (xs.Count > coordinateLimit || ys.Count > coordinateLimit)
                return Array.Empty<Num.Vector2>(); // The existing bounded raster solver follows.
        }
        var x = new float[xs.Count]; xs.CopyTo(x);
        var y = new float[ys.Count]; ys.CopyTo(y);
        int width = x.Length, height = y.Length;
        int sx = Array.BinarySearch(x, start.X), sy = Array.BinarySearch(y, start.Y);
        int ex = Array.BinarySearch(x, end.X), ey = Array.BinarySearch(y, end.Y);
        var nodes = new List<SearchNode>();
        var best = new Dictionary<int, int>();
        var open = new MinHeap(nodes);
        nodes.Add(new SearchNode { X = sx, Y = sy, Direction = 4,
            F = Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) });
        best[StateKey(sx, sy, 4, width, height)] = 0;
        open.Push(0);
        // Cache each undirected edge once, independent of incoming direction.
        var horizontal = new byte[width * height];
        var vertical = new byte[width * height];
        int[] dx = { 1, 0, -1, 0 }, dy = { 0, 1, 0, -1 };
        int goal = -1, expanded = 0;
        while (open.Count > 0 && expanded < expansionLimit)
        {
            cancellation.ThrowIfCancellationRequested();
            int index = open.Pop();
            SearchNode current = nodes[index];
            int state = StateKey(current.X, current.Y, current.Direction, width, height);
            if (current.Closed || best[state] != index) continue;
            current.Closed = true;
            expanded++;
            if (current.X == ex && current.Y == ey) { goal = index; break; }
            for (int direction = 0; direction < 4; direction++)
            {
                int nx = current.X + dx[direction], ny = current.Y + dy[direction];
                if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                Num.Vector2 a = new(x[current.X], y[current.Y]), b = new(x[nx], y[ny]);
                byte[] edges = dx[direction] != 0 ? horizontal : vertical;
                int edge = Math.Min(current.Y, ny) * width + Math.Min(current.X, nx);
                if (edges[edge] == 0)
                    edges[edge] = SegmentBlocked(a, b, startRoom, endRoom, obstacles) ? (byte)2 : (byte)1;
                if (edges[edge] == 2) continue;
                float g = current.G + Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) +
                    (current.Direction != 4 && current.Direction != direction ? 12f : 0f);
                int key = StateKey(nx, ny, direction, width, height);
                if (best.TryGetValue(key, out int old) && nodes[old].G <= g) continue;
                var next = new SearchNode { X = nx, Y = ny, Direction = direction, G = g,
                    F = g + Math.Abs(end.X - b.X) + Math.Abs(end.Y - b.Y), Parent = index };
                best[key] = nodes.Count;
                nodes.Add(next); open.Push(nodes.Count - 1);
            }
        }
        if (goal < 0) return Array.Empty<Num.Vector2>();
        var points = new List<Num.Vector2>();
        for (int index = goal; index >= 0; index = nodes[index].Parent)
            points.Add(new Num.Vector2(x[nodes[index].X], y[nodes[index].Y]));
        points.Reverse();
        return Simplify(points.ToArray());
    }
}
