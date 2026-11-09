using System;
using System.Collections.Generic;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Extensions;

/// <summary>Optional authoring adapters supply serialized data without exposing gameplay types.</summary>
public static class EditorRegionVariantContributors
{
    private static Func<string, string> dryCycle;
    private static Action<string, string> applyDryCycle;
    public static IDisposable RegisterDryCycle(Func<string, string> capture, Action<string, string> apply = null)
    {
        if (capture == null) throw new ArgumentNullException(nameof(capture));
        if (Interlocked.CompareExchange(ref dryCycle, capture, null) != null) throw new InvalidOperationException("A DryCycle region contributor is already registered.");
        Volatile.Write(ref applyDryCycle, apply);
        return new Lease(capture);
    }
    internal static string CaptureDryCycle(string region) => Volatile.Read(ref dryCycle)?.Invoke(region);
    internal static bool HasDryCycle => Volatile.Read(ref dryCycle) != null;
    internal static void ApplyDryCycle(string region, string data) { if (data != null) Volatile.Read(ref applyDryCycle)?.Invoke(region, data); }
    private sealed class Lease : IDisposable
    {
        private Func<string, string> capture;
        internal Lease(Func<string, string> capture) => this.capture = capture;
        public void Dispose() { var previous = Interlocked.Exchange(ref capture, null); if (previous != null && ReferenceEquals(Interlocked.CompareExchange(ref dryCycle, null, previous), previous)) Volatile.Write(ref applyDryCycle, null); }
    }
}
