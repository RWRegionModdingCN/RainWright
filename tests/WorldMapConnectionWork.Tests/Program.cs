using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.RWImGui;
using Num = System.Numerics;

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static WorldMapScene Scene(params string[] ids)
    {
        var scene = new WorldMapScene();
        foreach (string id in ids) scene.Connections.Add(id, new WorldMapScene.ConnectionNode { Id = id });
        return scene;
    }
    private static ConnectionRouteResource Route(string id) => new()
    {
        ConnectionId = id, Revision = 7,
        BasePoints = new[] { Num.Vector2.Zero, new Num.Vector2(100f, 0f) },
        Points = new[] { Num.Vector2.Zero, new Num.Vector2(100f, 0f) }
    };
    private static void Finish(WorldMapConnectionResourceStore store, WorldMapScene scene)
    {
        Check(SpinWait.SpinUntil(() => store.Completed, TimeSpan.FromSeconds(3)), "Background work did not complete.");
        store.Drain(scene);
    }
    private static void Main()
    {
        WorldMapWorldSpaceRouter.Compute = (input, cancellation) =>
        {
            cancellation.ThrowIfCancellationRequested();
            var result = new Dictionary<string, ConnectionRouteResource>();
            foreach (var node in input.Accepted) result.Add(node.Id, Route(node.Id));
            return result;
        };
        var store = new WorldMapConnectionResourceStore();
        var scene = Scene("a", "b");
        Check(store.Begin("a", "b"), "Initial route job was not scheduled."); Finish(store, scene);
        var originalA = store.Routes["a"]; var originalB = store.Routes["b"];
        store.Changed.Clear(); int writes = MapRoomGeometryPresentationHub.Writes; long revision = store.Revision;
        store.Layout(false); Finish(store, scene);
        Check(ReferenceEquals(originalA, store.Routes["a"]) && ReferenceEquals(originalB, store.Routes["b"]), "Crossing-only work replaced unchanged routes.");
        Check(store.Changed.Count == 0 && store.Revision == revision && MapRoomGeometryPresentationHub.Writes == writes, "Crossing-only work invalidated meshes or saved route data.");
        store.Begin("a", "b"); Finish(store, scene);
        Check(ReferenceEquals(originalA, store.Routes["a"]) && store.Changed.Count == 0 && store.Revision == revision,
            "An identical route solve discarded derived geometry or rebuilt GPU meshes.");
        WorldMapCorridorLaneAllocator.ChangedId = "a";
        store.Layout(true); Finish(store, scene);
        Check(store.Changed.SetEquals(new[] { "a" }) && ReferenceEquals(originalB, store.Routes["b"]), "One changed lane rebuilt unrelated routes.");
        Check(originalA.Revision + 1 == store.Routes["a"].Revision, "Changed lane revision did not advance once.");

        using var started = new ManualResetEventSlim();
        WorldMapWorldSpaceRouter.Compute = (_, token) => { started.Set(); token.WaitHandle.WaitOne(); token.ThrowIfCancellationRequested(); throw new Exception("Cancellation was ignored."); };
        store = new WorldMapConnectionResourceStore(); Check(store.Begin("a", "b"), "Cancellation fixture not scheduled.");
        Check(started.Wait(3000), "Worker did not start."); store.Edit(); scene.Connections.Remove("b"); Finish(store, scene);
        Check(store.Queued("a") && !store.Queued("b") && !store.Failed, "Canceled routes were lost or removed topology was resurrected.");
        Check(store.Routes.Count == 0, "Canceled result was published.");

        // A worker may be inside a non-cancelable phase; region resets retain its slot
        // until it returns, rather than starting another worker against the same owner.
        started.Reset(); using var release = new ManualResetEventSlim();
        WorldMapWorldSpaceRouter.Compute = (_, _) => { started.Set(); release.Wait(); return new() { ["a"] = Route("a") }; };
        store = new(); store.Begin("a"); Check(started.Wait(3000), "Reset worker did not start.");
        for (int i = 0; i < 10; i++) { store.ResetRegion(); Check(!store.Begin("new"), "Reset spawned overlapping route workers."); }
        release.Set(); Finish(store, Scene("new"));
        Check(store.Routes.Count == 0 && !store.Queued("a"), "Old-region completion entered the new region.");

        WorldMapWorldSpaceRouter.Compute = (_, _) => throw new InvalidOperationException("original route failure");
        store = new(); scene = Scene("a"); store.Routes.Add("a", Route("a")); originalA = store.Routes["a"];
        store.Begin("a"); Finish(store, scene);
        Check(store.Failed && !store.Begin("a") && store.Queued("a"), "Failed work was lost or retried on every frame.");
        Check(ReferenceEquals(originalA, store.Routes["a"]), "Failure destroyed the previously valid route.");
        Check(RainwrightFrontendDiagnostics.Errors[^1].Contains("original route failure"), "Original exception was not recorded.");
        store.Edit(); Check(!store.Failed, "Changed inputs did not unlock a failed job.");
        WorldMapWorldSpaceRouter.Compute = (_, _) =>
        {
            var updated = Route("a"); updated.BasePoints[1] = new Num.Vector2(120f, 0f);
            return new() { ["a"] = updated };
        };
        Check(store.Begin("a"), "Changed input could not retry a failed job."); Finish(store, scene);
        Check(!ReferenceEquals(originalA, store.Routes["a"]) && store.Changed.Contains("a"), "Successful recovery was not published.");

        // Cancellation can race with successful task completion; edits must still win.
        WorldMapWorldSpaceRouter.Compute = (_, _) => new() { ["a"] = Route("a") };
        store = new(); store.Begin("a"); Check(SpinWait.SpinUntil(() => store.Completed, 3000), "Success race fixture incomplete.");
        store.Edit(); store.Drain(scene);
        Check(store.Routes.Count == 0 && store.Queued("a"), "Completed obsolete task overwrote a newer edit.");
        Console.WriteLine("PASS: " + checks + " route-worker assertions: cancellation, bounded reset, no-op layout, failures and stale completions.");
    }
}
