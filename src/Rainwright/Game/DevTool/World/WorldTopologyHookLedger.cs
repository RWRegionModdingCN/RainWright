using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>
/// Small lifetime ledger for DryCycle's *gameplay* topology hooks. This is not
/// an editor/frontend component: shortcut traversal continues with DevTools off.
/// Record before add, reverse-remove and retain any failed removal for retry.
/// </summary>
internal sealed class WorldTopologyHookLedger
{
    private sealed class Entry
    {
        internal readonly string Name;
        internal readonly Action Detach;
        internal Entry(string name, Action detach) { Name = name; Detach = detach; }
    }

    private readonly List<Entry> active = new();
    private readonly Action<string, Exception> report;

    internal WorldTopologyHookLedger(Action<string, Exception> report)
    {
        this.report = report;
    }

    internal bool HasActiveRegistrations => active.Count > 0;

    internal void Install(string name, Action attach, Action detach)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Topology hook name is required.", nameof(name));
        if (attach == null) throw new ArgumentNullException(nameof(attach));
        if (detach == null) throw new ArgumentNullException(nameof(detach));

        for (int i = 0; i < active.Count; i++)
            if (string.Equals(active[i].Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "World topology gameplay hook already installed or awaiting cleanup: " + name);

        Entry entry = new(name, detach);
        active.Add(entry);
        try
        {
            attach();
        }
        catch
        {
            // HookGen may have already attached before reporting an exception.
            // Keep a failed detachment in the ledger, preserving the original
            // exception identity for the caller's full startup rollback.
            TryDetach(entry);
            throw;
        }
    }

    internal bool ReleaseAll()
    {
        bool ok = true;
        for (int i = active.Count - 1; i >= 0; i--)
            if (!TryDetach(active[i]))
                ok = false;
        return ok && active.Count == 0;
    }

    private bool TryDetach(Entry entry)
    {
        try
        {
            entry.Detach();
            active.Remove(entry);
            return true;
        }
        catch (Exception error)
        {
            try { report?.Invoke(entry.Name, error); }
            catch (Exception) { /* Do not conceal the failed hook cleanup. */ }
            return false;
        }
    }
}
