using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Best-effort release of independently owned retained-map resources.
/// Every owner is attempted before the error reaches the higher-level
/// BridgePlugin cleanup ledger, which retains failed shutdown for retry.
/// </summary>
internal static class RetainedCleanupBatch
{
    // Release may synchronize with the graphics thread, whose Present callback needs this gate.
    // Wait for existing readers and unpublish under the gate; perform native release after it.
    internal static void DetachAndRelease(object presentationGate, Action detach, Action release)
    {
        if (presentationGate == null) throw new ArgumentNullException(nameof(presentationGate));
        if (detach == null) throw new ArgumentNullException(nameof(detach));
        if (release == null) throw new ArgumentNullException(nameof(release));
        lock (presentationGate) detach();
        release();
    }

    internal readonly struct Step
    {
        internal readonly string Name;
        internal readonly Action Release;
        internal Step(string name, Action release)
        {
            Name = name ?? "unnamed resource";
            Release = release ?? throw new ArgumentNullException(nameof(release));
        }
    }

    internal static void Run(params Step[] steps)
    {
        if (steps == null) throw new ArgumentNullException(nameof(steps));
        List<Exception> failures = null;
        for (int i = 0; i < steps.Length; i++)
        {
            Step step = steps[i];
            try { step.Release(); }
            catch (Exception error)
            {
                failures ??= new List<Exception>();
                failures.Add(new InvalidOperationException(
                    "Retained map cleanup failed: " + step.Name, error));
            }
        }

        if (failures != null)
            throw new AggregateException(
                "Some retained-map owners could not release; cleanup remains pending.",
                failures);
    }
}
