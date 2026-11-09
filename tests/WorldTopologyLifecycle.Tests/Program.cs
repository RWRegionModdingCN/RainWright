using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.World;

internal static class Program
{
    private static int assertions;
    private static readonly List<string> calls = new();

    private static void Check(bool condition, string reason)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(reason);
    }

    private static void Run()
    {
        var failures = new List<string>();
        var hooks = new WorldTopologyHookLedger(
            (name, error) => failures.Add(name + ": " + error));

        hooks.Install("WorldLoader", () => calls.Add("+world"), () => calls.Add("-world"));
        hooks.Install("Shortcut", () => calls.Add("+shortcut"), () => calls.Add("-shortcut"));
        Check(hooks.HasActiveRegistrations, "Gameplay HookGen subscriptions are tracked.");
        Check(hooks.ReleaseAll() &&
              string.Join(",", calls) == "+world,+shortcut,-shortcut,-world",
            "Gameplay hooks detach in reverse order without touching third-party subscriptions.");
        Check(hooks.ReleaseAll() && calls.Count == 4,
            "Repeated gameplay Disable is idempotent.");

        hooks.Install("WorldLoader", () => calls.Add("+world2"), () => calls.Add("-world2"));
        bool duplicate = false;
        try { hooks.Install("WorldLoader", () => calls.Add("DUP"), () => { }); }
        catch (InvalidOperationException) { duplicate = true; }
        Check(duplicate && !calls.Contains("DUP"),
            "A second source may not install an identical shortcut hook.");
        Check(hooks.ReleaseAll(), "Clean restart releases all tracked hooks.");

        Exception original = new InvalidOperationException("partially attached");
        bool sameException = false;
        try
        {
            hooks.Install("Partial", () => { calls.Add("+partial"); throw original; },
                () => calls.Add("-partial"));
        }
        catch (Exception error) { sameException = ReferenceEquals(original, error); }
        Check(sameException && !hooks.HasActiveRegistrations && calls.Contains("-partial"),
            "Partial HookGen installation rolls back and rethrows the original exception.");

        // HookGen can attach a callback and then throw while its immediate
        // compensating remove also fails. This retains the entry for retry.
        int partialRemoveAttempts = 0;
        Exception combinedOriginal = new InvalidOperationException("attach performed, then failed");
        bool originalPreserved = false;
        try
        {
            hooks.Install("PartialCleanupFailure",
                () => { calls.Add("+partial-failed"); throw combinedOriginal; },
                () =>
                {
                    partialRemoveAttempts++;
                    if (partialRemoveAttempts == 1)
                        throw new InvalidOperationException("detach failed during partial install");
                    calls.Add("-partial-failed");
                });
        }
        catch (Exception error) { originalPreserved = ReferenceEquals(error, combinedOriginal); }
        Check(originalPreserved && hooks.HasActiveRegistrations &&
              failures.Exists(x => x.Contains("PartialCleanupFailure") &&
                                   x.Contains("detach failed during partial install")),
            "An attach failure preserves its original exception and its unsuccessfully rolled back lease.");
        Check(hooks.ReleaseAll() && !hooks.HasActiveRegistrations &&
              partialRemoveAttempts == 2 && calls.Contains("-partial-failed"),
            "Later cleanup retries the failed partial-attach rollback exactly once.");

        int detachAttempts = 0;
        hooks.Install("CleanupRetry", () => calls.Add("+retry"), () =>
        {
            detachAttempts++;
            if (detachAttempts == 1)
                throw new InvalidOperationException("injected HookGen detach failure");
            calls.Add("-retry");
        });
        Check(!hooks.ReleaseAll() && hooks.HasActiveRegistrations &&
              failures.Exists(f => f.Contains("CleanupRetry") &&
                                    f.Contains("injected HookGen detach failure")),
            "Failed gameplay hook cleanup retains the entry and full diagnostic.");
        duplicate = false;
        try { hooks.Install("CleanupRetry", () => calls.Add("DUP"), () => { }); }
        catch (InvalidOperationException) { duplicate = true; }
        Check(duplicate && !calls.Contains("DUP"),
            "An orphaned shortcut callback blocks another install until cleanup succeeds.");
        Check(hooks.ReleaseAll() && !hooks.HasActiveRegistrations &&
              detachAttempts == 2 && calls.Contains("-retry"),
            "A later Disable retries the exact failed hook without stacking another.");

        bool cleanupFailed = false;
        var hard = new WorldTopologyHookLedger((name, error) => { throw new InvalidOperationException("broken logger"); });
        hard.Install("Faulty", () => { }, () =>
        {
            if (!cleanupFailed)
            {
                cleanupFailed = true;
                throw new InvalidOperationException("real cleanup");
            }
        });
        Check(!hard.ReleaseAll() && hard.HasActiveRegistrations,
            "A diagnostic sink exception cannot swallow failed gameplay hook cleanup.");
        Check(hard.ReleaseAll() && !hard.HasActiveRegistrations,
            "A recovered cleanup still detaches after a broken diagnostic listener.");

        Console.WriteLine("PASS: " + assertions +
            " assertions; production topology gameplay hook ledger rollback and retry.");
    }

    private static int Main()
    {
        try { Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
