using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.RWImGui;
using UnityEngine;
using Num = System.Numerics;

internal static class Program
{
    private static int checks;
    private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Check(bool ok, string text) { checks++; if (!ok) throw new Exception(text); }
    private static void Set(object owner, string field, object value) => owner.GetType().GetField(field, Fields).SetValue(owner, value);
    private static T Get<T>(object owner, string field) => (T)owner.GetType().GetField(field, Fields).GetValue(owner);
    private static readonly WorldMapViewTransform View = new(Num.Vector2.Zero, new Num.Vector2(400, 300), Num.Vector2.Zero, 1);
    private static WorldMapRenderTextureSurface Surface()
    {
        var surface = new WorldMapRenderTextureSurface();
        Check(surface.Render(View, _ => { }), "Initial surface renders.");
        return surface;
    }
    private static void Main()
    {
        var surface = Surface(); object gate = Get<object>(surface, "gate");
        int nativeCallsUnderGate = 0;
        UnityEngine.Object.NullProbe = () => { if (Monitor.IsEntered(gate)) nativeCallsUnderGate++; };
        Time.Probe = () => { if (Monitor.IsEntered(gate)) nativeCallsUnderGate++; return 100; };
        bool renderThreadResponded = false;
        Task renderThread = null;
        RenderTexture.Validate = target =>
        {
            if (Monitor.IsEntered(gate)) nativeCallsUnderGate++;
            renderThread = Task.Run(() => surface.TryPresent(default, Num.Vector2.Zero, View.CanvasSize, View));
            renderThreadResponded = renderThread.Wait(1000);
            return target.Created;
        };
        surface.Initialize(null);
        Check(renderThreadResponded, "Native IsCreated waiting for Present must not block the Present state lock.");
        Check(renderThread.Wait(1000), "The render task finishes without waiting for validation to return.");
        _ = surface.Ready; _ = surface.NeedsRender; _ = surface.CoversView(View);
        Check(surface.TryPresent(default, Num.Vector2.Zero, View.CanvasSize, View), "Validated surface remains presentable.");
        Check(nativeCallsUnderGate == 0, "Unity null operators, frame counters and texture queries never execute inside the shared state lock.");
        RenderTexture.Validate = null; UnityEngine.Object.NullProbe = null; Time.Probe = null;

        // Device-loss fallback still restores the matching size/transform and queues the old target.
        var failed = Get<RenderTexture>(surface, "presented"); failed.Created = false;
        var backup = new RenderTexture(600, 450, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); backup.Create();
        Set(surface, "retired", backup); Set(surface, "retiredWidth", 600); Set(surface, "retiredHeight", 450);
        Set(surface, "retiredTransform", View); Set(surface, "retiredTransformValid", true);
        surface.Initialize(null);
        Check(ReferenceEquals(Get<RenderTexture>(surface, "presented"), backup) && surface.Ready && surface.NeedsRender,
            "Invalid candidate restores the valid fallback and requests a fresh render.");
        Check(!failed.Created && failed.Destroyed, "Detached invalid target is released after validation.");
        Check(Get<int>(surface, "width") == 600 && Get<int>(surface, "height") == 450, "Fallback metadata matches its texture.");

        // Reset can detach the pair while the native query is outside the lock.
        RenderTexture.Validate = _ => { surface.Reset(); return true; };
        surface.Initialize(null);
        Check(!surface.Ready && Get<RenderTexture>(surface, "presented") is null,
            "A completed stale query cannot resurrect a surface detached by Reset.");
        RenderTexture.Validate = null;

        var log = new ManualLogSource(); surface = Surface();
        surface.Initialize(log);
        RenderTexture.Validate = _ => throw new InvalidOperationException("original native validation failure");
        surface.Initialize(log); surface.Initialize(log);
        Check(!surface.Ready && log.Warnings.Count == 1 && log.Warnings[0].Contains("original native validation failure"),
            "Native validation errors retain the original exception and do not flood logs per frame.");
        RenderTexture.Validate = null;
        surface.Initialize(log); Check(surface.Ready, "A subsequent valid native target recovers.");
        surface.Reset();
        Console.WriteLine("PASS: " + checks + " production surface concurrency, native lock boundary, device-loss, stale-reset and recovery assertions.");
    }
}
