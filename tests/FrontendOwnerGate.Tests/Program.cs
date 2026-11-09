using System;
using System.Linq;
using System.Threading.Tasks;
using DryCycle.DevUI.DevTool.RWImGui;

namespace DryCycle.DevUI.DevTool.RWImGui
{
    // The production bridge adapter only references the plugin's identity.
    internal sealed class BridgePlugin { }
}

internal static class Program
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        var reference = typeof(FrontendPluginOwnerGate<BridgePlugin>).Assembly;
        Check(reference.GetName().Name == "RainWright" &&
              reference != typeof(BridgePluginOwnership).Assembly,
            "The production frontend must consume the Rainwright-owned gate.");

        var first = new BridgePlugin();
        var duplicate = new BridgePlugin();
        Check(BridgePluginOwnership.TryClaim(first) &&
              BridgePluginOwnership.TryClaim(first) &&
              BridgePluginOwnership.IsOwner(first),
            "Idempotent startup from the same plugin instance failed.");
        Check(!BridgePluginOwnership.TryClaim(duplicate) &&
              !BridgePluginOwnership.IsOwner(duplicate),
            "A second BepInEx plugin instance was incorrectly admitted.");
        Check(!BridgePluginOwnership.TryRelease(duplicate, true) &&
              !BridgePluginOwnership.TryRelease(first, false) &&
              BridgePluginOwnership.IsOwner(first),
            "Pending native cleanup or another instance must not release the owner.");
        Check(BridgePluginOwnership.TryRelease(first, true) &&
              !BridgePluginOwnership.IsOwner(first) &&
              BridgePluginOwnership.TryClaim(duplicate) &&
              BridgePluginOwnership.TryRelease(duplicate, true),
            "A successful shutdown must enable the next plugin instance.");

        var contested = new FrontendPluginOwnerGate<object>();
        object[] candidates = Enumerable.Range(0, 32).Select(_ => new object()).ToArray();
        bool[] acquired = new bool[candidates.Length];
        Parallel.For(0, candidates.Length, i =>
        {
            acquired[i] = contested.TryClaim(candidates[i]);
        });
        Check(acquired.Count(ok => ok) == 1,
            "Concurrent BepInEx starts must elect exactly one callback owner.");
        int winner = Array.FindIndex(acquired, ok => ok);
        Check(contested.IsOwner(candidates[winner]) &&
              !contested.TryRelease(candidates[winner], false),
            "Failed cleanup must retain the elected owner.");
        Check(contested.TryRelease(candidates[winner], true) &&
              contested.TryClaim(new object()),
            "A completed cleanup must permit later plugin activation.");

        bool rejectedNull = false;
        try { contested.TryClaim(null); }
        catch (ArgumentNullException) { rejectedNull = true; }
        Check(rejectedNull, "Null plugin instances must be rejected.");

        Console.WriteLine("PASS: Rainwright native frontend ownership, retry, rollback and concurrency.");
    }
}
