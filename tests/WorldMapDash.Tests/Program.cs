using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using DryCycle.DevUI.DevTool.RWImGui;
using Num = System.Numerics;

internal static class Program
{
    private static int checks;
    // Rounded route captured read-only from the frozen SU process on 2026-10-13.
    private static readonly Num.Vector2[] CapturedPath =
    {
        new(24f,4f), new(51f,4f), new(53.625f,4.375f), new(55.5f,5.5f),
        new(56.625f,7.375f), new(57f,10f), new(57f,42f), new(56.625f,44.625f),
        new(55.5f,46.5f), new(53.625f,47.625f), new(51f,48f),
        new(-197.6881103515625f,48f), new(-200.3131103515625f,48.375f),
        new(-202.1881103515625f,49.5f), new(-203.3131103515625f,51.375f),
        new(-203.6881103515625f,54f), new(-203.6881103515625f,216.9239044189453f),
        new(-204.0631103515625f,219.5489044189453f),
        new(-205.1881103515625f,221.4239044189453f),
        new(-207.0631103515625f,222.54891967773438f),
        new(-209.6881103515625f,222.9239044189453f),
        new(-232.6881103515625f,222.9239044189453f)
    };

    private static int Main(string[] args)
    {
        try
        {
            var timer = Stopwatch.StartNew();
            ReproduceStagnantFloat();
            Boundaries();
            LongPathLod();
            ValidatePath(CapturedPath);
            var random = new Random(20261013);
            for (int fixture = 0; fixture < 200; fixture++)
            {
                var path = new Num.Vector2[24];
                for (int i = 1; i < path.Length; i++)
                    path[i] = path[i - 1] + new Num.Vector2((float)(random.NextDouble() * 100), (float)(random.NextDouble() * 100));
                ValidatePath(path);
            }
            if (args.Length == 2) VerifyProductionRenderer(args[0], args[1]);
            Console.WriteLine("PASS: " + checks + " dash assertions, captured SU route and 200 split paths; " + timer.ElapsedMilliseconds + " ms.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Check(bool value, string message)
    {
        checks++;
        if (!value) throw new Exception(message);
    }

    private static void ReproduceStagnantFloat()
    {
        float phase = 0.7176542282104492f, distance = 31.282344818115234f;
        // Mono retains the promoted float addition through the remainder call.
        double position = ((double)phase + distance) % 16d;
        Check(position == 15.999999046325684d, "Captured remainder no longer reproduces.");
        Check((float)(distance + (16d - position)) == distance, "Captured gap should round back to the same cursor.");
        var spans = new WorldMapDashSegmentEnumerator(162.9239044189453f, phase, 10d, 6d);
        int count = 0;
        double previous = -1d;
        while (spans.MoveNext())
        {
            Check(spans.Start > previous && spans.End > spans.Start, "Captured dash must advance.");
            previous = spans.Start;
            Check(++count <= 12, "Captured segment produced excess intervals.");
        }
        Check(count == 11, "Captured segment dash count changed.");
    }

    private static void Boundaries()
    {
        foreach (double phase in new[] { 0d, 10d, 16d, 15.999999046325684d, -4d, 48d })
        {
            var empty = new WorldMapDashSegmentEnumerator(0d, phase, 10d, 6d);
            Check(!empty.MoveNext(), "Empty segment produced geometry.");
            var spans = new WorldMapDashSegmentEnumerator(40d, phase, 10d, 6d);
            int count = 0;
            while (spans.MoveNext())
            {
                Check(spans.Start >= 0d && spans.End <= 40d && spans.End > spans.Start, "Boundary dash is outside segment.");
                Check(++count <= 4, "Boundary enumeration did not terminate.");
            }
        }
        var gap = new WorldMapDashSegmentEnumerator(40d, 12d, 10d, 6d);
        foreach (double start in new[] { 4d, 20d, 36d })
            Check(gap.MoveNext() && gap.Start == start && gap.End == Math.Min(start + 10d, 40d), "Leading gap/last dash clipping failed.");
        Check(!gap.MoveNext(), "Leading gap produced extra span.");
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1d, double.MaxValue })
        {
            bool rejected = false;
            try { _ = new WorldMapDashSegmentEnumerator(invalid, 0d, 10d, 6d); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "Invalid/unrepresentable segment length accepted.");
        }
    }

    private static void ValidatePath(Num.Vector2[] path)
    {
        double distance = 0d, phase = 0d;
        var starts = new List<double>();
        var ends = new List<double>();
        for (int segment = 0; segment < path.Length - 1; segment++)
        {
            float length = (path[segment + 1] - path[segment]).Length();
            if (length <= 0.001f) continue;
            var spans = new WorldMapDashSegmentEnumerator(length, phase, 10d, 6d);
            int count = 0;
            while (spans.MoveNext())
            {
                Check(++count <= Math.Ceiling(length / 16d) + 1, "Split segment did not terminate.");
                double start = distance + spans.Start, end = distance + spans.End;
                if (ends.Count > 0 && Math.Abs(start - ends[ends.Count - 1]) < 1e-7)
                    ends[ends.Count - 1] = end;
                else { starts.Add(start); ends.Add(end); }
            }
            phase = spans.NextPhase;
            Check(phase >= 0d && phase < 16d, "Phase not normalized.");
            distance += length;
        }
        int expected = (int)Math.Ceiling(distance / 16d);
        Check(starts.Count == expected, "Splitting the path changed its dash count.");
        for (int i = 0; i < expected; i++)
        {
            Check(Math.Abs(starts[i] - i * 16d) < 1e-7, "Dash start depends on path corners.");
            Check(Math.Abs(ends[i] - Math.Min(i * 16d + 10d, distance)) < 1e-7, "Dash end depends on path corners.");
        }
    }

    private static void LongPathLod()
    {
        foreach (double length in new[] { 100d, 100000d, 1e8d, 1e12d })
        {
            double scale = WorldMapDashSegmentEnumerator.ScaleForLength(length, 10d, 6d);
            Check(length <= 100000d ? scale == 1d : scale > 1d, "Normal routes changed dash size or long-route LOD did not engage.");
            var spans = new WorldMapDashSegmentEnumerator(length, 0d, 10d * scale, 6d * scale);
            int count = 0;
            while (spans.MoveNext())
            {
                Check(spans.End > spans.Start && spans.End <= length, "Long-path dash left the source segment.");
                Check(++count <= WorldMapDashSegmentEnumerator.MaxPatternCycles + 1, "Long-path dash generation exceeded its geometry budget.");
            }
        }
    }

    private static void VerifyProductionRenderer(string dll, string game)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            string name = new AssemblyName(request.Name).Name;
            foreach (string directory in new[] { "RainWorld_Data/Managed", "BepInEx/core", "BepInEx/utils" })
            {
                string file = Path.Combine(game, directory, name + ".dll");
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        var library = Assembly.LoadFrom(Path.GetFullPath(dll));
        var renderer = library.GetType("DryCycle.DevUI.DevTool.RWImGui.WorldMapRetainedConnectionRenderer", true);
        var build = renderer.GetMethod("AddDashedPath", BindingFlags.Static | BindingFlags.NonPublic);
        var parameters = build.GetParameters();
        var vertices = (IList)Activator.CreateInstance(parameters[0].ParameterType);
        var colors = (IList)Activator.CreateInstance(parameters[1].ParameterType);
        var indices = (IList)Activator.CreateInstance(parameters[2].ParameterType);
        var color = Activator.CreateInstance(parameters[5].ParameterType);
        build.Invoke(null, new object[] { vertices, colors, indices, CapturedPath, 1.95f, color, 0.08f });
        Check(vertices.Count > 0 && vertices.Count % 4 == 0, "Actual renderer produced invalid dash vertices.");
        Check(colors.Count == vertices.Count && indices.Count == vertices.Count / 4 * 6, "Actual renderer buffers differ.");
        Check(vertices.Count < 512, "Captured route generated excessive geometry.");
        foreach (int index in indices) Check(index >= 0 && index < vertices.Count, "Actual mesh index is invalid.");
        Console.WriteLine("Actual net48 renderer: " + vertices.Count + " vertices / " + indices.Count + " indices, captured SU path completed.");
        var longPath = new[] { Num.Vector2.Zero, new Num.Vector2(100000000f, 0f) };
        vertices.Clear(); colors.Clear(); indices.Clear();
        build.Invoke(null, new object[] { vertices, colors, indices, longPath, 1.95f, color, 0.08f });
        Check(vertices.Count > 0 && vertices.Count <= (WorldMapDashSegmentEnumerator.MaxPatternCycles + 1) * 4,
            "Actual retained renderer expanded a huge path into unbounded dash geometry.");
        Check(longPath[1].X == 100000000f, "Presentation LOD changed the source endpoint.");
        Console.WriteLine("Actual long-path renderer: " + vertices.Count + " vertices, source endpoints preserved.");

        // Exercise the actual production crossing code under the same Mono host too,
        // including saturated cells where an int loop used to wrap at its last cell.
        Type crossingType = library.GetType("DryCycle.DevUI.DevTool.RWImGui.WorldMapRouteCrossingCore", true);
        MethodInfo crossingBuild = crossingType.GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)
            .MakeGenericMethod(typeof(Num.Vector2[]));
        foreach (float origin in new[] { 0f, 1e12f, -1e12f })
        {
            float radius = origin == 0f ? 1e12f : 1e7f;
            var paths = new Dictionary<string, Num.Vector2[]>
            {
                ["horizontal"] = new[] { new Num.Vector2(origin-radius-100f, origin), new Num.Vector2(origin-radius, origin),
                    new Num.Vector2(origin+radius, origin), new Num.Vector2(origin+radius+100f, origin) },
                ["vertical"] = new[] { new Num.Vector2(origin, origin-radius-100f), new Num.Vector2(origin, origin-radius),
                    new Num.Vector2(origin, origin+radius), new Num.Vector2(origin, origin+radius+100f) }
            };
            object output = crossingBuild.Invoke(null, new object[] { paths,
                new Func<Num.Vector2[], Num.Vector2[]>(points => points), CancellationToken.None });
            var marks = (Array)output.GetType().GetProperty("Marks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(output);
            Check(marks.Length == 1, "Actual Mono crossing solver lost a long/far crossing.");
        }
        Console.WriteLine("Actual net48 crossing solver: huge sparse and saturated-cell paths completed.");
    }
}
