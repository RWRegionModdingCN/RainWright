using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Rainwright-owned backend lifetime ledger. The compatibility host supplies
/// HookGen / editor delegates, but may not silently reinstall an active or
/// incompletely released service. No Unity or gameplay dependencies.
/// </summary>
internal sealed class EditorBackendServiceLedger
{
    private sealed class Entry
    {
        internal readonly string Name;
        internal readonly Action Stop;
        internal Entry(string name, Action stop) { Name = name; Stop = stop; }
    }

    private readonly List<Entry> steps = new();

    internal int Count => steps.Count;
    internal bool CoreEnabled { get; set; }
    internal bool ExtrasReady { get; set; }
    internal bool ExtrasWarmupAttempted { get; set; }
    internal bool PendingCleanup => steps.Count > 0 && !CoreEnabled;

    internal bool Contains(string name)
    {
        for (int i = 0; i < steps.Count; i++)
            if (string.Equals(steps[i].Name, name, StringComparison.Ordinal))
                return true;
        return false;
    }

    internal void Start(string name, Action start, Action stop)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A backend service name is required.", nameof(name));
        if (start == null) throw new ArgumentNullException(nameof(start));
        if (stop == null) throw new ArgumentNullException(nameof(stop));
        if (Contains(name))
            throw new InvalidOperationException(
                "Backend service is already active or awaiting cleanup: " + name);

        // Track BEFORE invoking external HookGen APIs. A partial add must
        // remain owned even if the installer throws after the side effect.
        steps.Add(new Entry(name, stop));
        start();
    }

    internal bool Release(string name, Action<string, Action> invoke, Action<string, Exception> report)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
            if (string.Equals(steps[i].Name, name, StringComparison.Ordinal))
                return ReleaseAt(i, invoke, report);
        return true;
    }

    internal bool ReleaseAt(int index, Action<string, Action> invoke, Action<string, Exception> report)
    {
        Entry entry = steps[index];
        try
        {
            if (invoke == null) entry.Stop();
            else invoke(entry.Name, entry.Stop);
            steps.RemoveAt(index);
            return true;
        }
        catch (Exception error)
        {
            try { report?.Invoke(entry.Name, error); }
            catch (Exception) { /* The diagnostic sink cannot release a failed lease. */ }
            return false;
        }
    }

    internal bool ReleaseAll(Action<string, Action> invoke, Action<string, Exception> report)
    {
        bool complete = true;
        for (int i = steps.Count - 1; i >= 0; i--)
            if (!ReleaseAt(i, invoke, report))
                complete = false;
        return complete && steps.Count == 0;
    }

    internal void ResetModeState()
    {
        CoreEnabled = false;
        ExtrasReady = false;
        ExtrasWarmupAttempted = false;
        // Never clear failed-detach entries here. Retry is mandatory.
    }
}
