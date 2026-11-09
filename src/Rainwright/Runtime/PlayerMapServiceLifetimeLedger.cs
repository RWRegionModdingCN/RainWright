using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Rainwright-owned Player Map service registration and takeover tokens.
/// Registration is recorded before running a possible HookGen installer.
/// A failed detach remains tracked and cannot be overwritten by re-enable.
/// </summary>
internal sealed class PlayerMapServiceLifetimeLedger
{
    private readonly EditorBackendServiceLedger registrations = new();
    private readonly Dictionary<string, int> generations = new(StringComparer.Ordinal);

    internal int Count => registrations.Count;

    internal bool Contains(string name) => registrations.Contains(name);

    internal int Generation(string name) =>
        name != null && generations.TryGetValue(name, out int value) ? value : 0;

    internal void Start(string name, Action start, Action stop)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Player Map service name is required.", nameof(name));
        if (start == null) throw new ArgumentNullException(nameof(start));
        if (stop == null) throw new ArgumentNullException(nameof(stop));
        if (registrations.Contains(name))
            throw new InvalidOperationException("Player Map service still has a registration: " + name);

        // A partially completed install must advance generation to invalidate
        // stale auxiliary plugin leases even if rollback succeeds immediately.
        generations[name] = unchecked(Generation(name) + 1);
        registrations.Start(name, start, stop);
    }

    internal bool ReleaseAll(
        Action<string, Action> detach,
        Action<string, Exception> report) =>
        registrations.ReleaseAll(detach, report);
}
