using System;
using System.Collections.Generic;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// A small, frontend-only ledger for HookGen/event subscriptions. Installation is
/// recorded before invoking the external add operation, so even a partially
/// completed add can be cleaned up. Failed removals stay tracked for retry;
/// subsequent Enable must never stack an identical hook on top of an orphan.
/// </summary>
internal sealed class FrontendHookLedger
{
    private sealed class Entry
    {
        internal readonly string Name;
        internal readonly Action Detach;

        internal Entry(string name, Action detach)
        {
            Name = name;
            Detach = detach;
        }
    }

    private readonly List<Entry> active = new();
    private readonly Action<string, Exception> report;

    internal FrontendHookLedger(Action<string, Exception> report = null)
    {
        this.report = report;
    }

    internal bool HasActiveRegistrations => active.Count != 0;

    internal void Install(string name, Action attach, Action detach)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Hook name is required.", nameof(name));
        if (attach == null) throw new ArgumentNullException(nameof(attach));
        if (detach == null) throw new ArgumentNullException(nameof(detach));

        for (int i = 0; i < active.Count; i++)
        {
            if (string.Equals(active[i].Name, name, StringComparison.Ordinal))
                throw new InvalidOperationException("Frontend hook is already attached or awaiting cleanup: " + name);
        }

        Entry entry = new(name, detach);
        active.Add(entry);
        try
        {
            attach();
        }
        catch (Exception)
        {
            // HookGen add may have performed a side effect before throwing.
            // Always attempt removal; preserve the original attach exception.
            TryDetach(entry);
            throw;
        }
    }

    internal bool ReleaseAll()
    {
        bool complete = true;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (!TryDetach(active[i]))
                complete = false;
        }
        return complete && active.Count == 0;
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
            catch (Exception) { /* Logging failures must not conceal a hook cleanup failure. */ }
            return false;
        }
    }
}
