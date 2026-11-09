using System;
using System.Threading;

namespace DryCycle.DevUI.DevTool.Input;

/// <summary>
/// The editor owns keyboard suppression. A game-specific Remix page may supply the
/// three explicit gameplay unlock preferences without giving the router a gameplay DLL link.
/// Default false reserves Ctrl+Z/S/Y for authoring, matching the legacy behavior.
/// </summary>
public static class EditorShortcutGameplayPolicy
{
    private sealed class Policy
    {
        internal readonly Func<bool> AllowZ, AllowS, AllowY;
        internal readonly Action<Exception> Report;
        internal Policy(Func<bool> z, Func<bool> s, Func<bool> y, Action<Exception> report)
        {
            AllowZ = z; AllowS = s; AllowY = y; Report = report;
        }
    }
    private sealed class Lease : IDisposable
    {
        private Policy source;
        internal Lease(Policy source) => this.source = source;
        public void Dispose()
        {
            Policy previous = Interlocked.Exchange(ref source, null);
            if (previous == null) return;
            lock (Sync)
            {
                if (ReferenceEquals(policy, previous))
                    Volatile.Write(ref policy, null);
            }
        }
    }

    private static readonly object Sync = new();
    private static Policy policy;

    public static void LoadOwned(string configRoot) =>
        RainwrightShortcutInputSettingsStore.Load(configRoot,
            error => global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "Rainwright shortcut input configuration is invalid; player input unlocks are disabled.", error));

    public static bool HasOwnedSettings => RainwrightShortcutInputSettingsStore.HasOwner;
    public static bool TryQueueOwned(bool z, bool s, bool y, out string error) =>
        RainwrightShortcutInputSettingsStore.TryQueue(z, s, y, out error);
    public static bool OwnedWritePending => RainwrightShortcutInputSettingsStore.Pending;
    public static long OwnedWriteRevision => RainwrightShortcutInputSettingsStore.CompletedRevision;
    public static bool LastOwnedWriteSucceeded => RainwrightShortcutInputSettingsStore.LastSuccess;
    public static string LastOwnedWriteError => RainwrightShortcutInputSettingsStore.LastError;

    internal static void ProcessPendingOwnedWrites()
    {
        if (!RainwrightShortcutInputSettingsStore.Pending) return;
        long before = RainwrightShortcutInputSettingsStore.CompletedRevision;
        RainwrightShortcutInputSettingsStore.ProcessPending();
        if (RainwrightShortcutInputSettingsStore.CompletedRevision != before &&
            !RainwrightShortcutInputSettingsStore.LastSuccess)
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "Rainwright shortcut input settings could not be saved: " +
                RainwrightShortcutInputSettingsStore.LastError);
    }

    public static IDisposable Register(Func<bool> allowZ, Func<bool> allowS,
        Func<bool> allowY, Action<Exception> report = null)
    {
        if (allowZ == null) throw new ArgumentNullException(nameof(allowZ));
        if (allowS == null) throw new ArgumentNullException(nameof(allowS));
        if (allowY == null) throw new ArgumentNullException(nameof(allowY));
        Policy current = new(allowZ, allowS, allowY, report);
        lock (Sync)
        {
            if (policy != null)
                throw new InvalidOperationException("An editor shortcut gameplay policy is already active.");
            Volatile.Write(ref policy, current);
        }
        return new Lease(current);
    }

    public static bool AllowZ => GetAllowed(0);
    public static bool AllowS => GetAllowed(1);
    public static bool AllowY => GetAllowed(2);

    private static bool GetAllowed(int key)
    {
        if (RainwrightShortcutInputSettingsStore.HasOwner)
            return key == 0 ? RainwrightShortcutInputSettingsStore.Z :
                key == 1 ? RainwrightShortcutInputSettingsStore.S :
                RainwrightShortcutInputSettingsStore.Y;
        Policy current = Volatile.Read(ref policy);
        if (current == null) return false;
        try
        {
            return key == 0 ? current.AllowZ() : key == 1 ? current.AllowS() : current.AllowY();
        }
        catch (Exception error)
        {
            try { current.Report?.Invoke(error); }
            catch { }
            return false;
        }
    }
}
