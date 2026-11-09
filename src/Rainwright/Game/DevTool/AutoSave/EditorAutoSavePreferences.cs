using System;
using System.Threading;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Independent AutoSave settings boundary. During migration the DryCycle Remix options
/// provide read-only fallback values until Rainwright's own preferences are saved.
/// Stable clock polling never needs to access DryCycle's option interface directly.
/// </summary>
public static class EditorAutoSavePreferences
{
    private sealed class Provider
    {
        internal readonly Func<bool> Enabled;
        internal readonly Func<int> Interval;
        internal readonly Action<Exception> Report;
        internal Provider(Func<bool> enabled, Func<int> interval, Action<Exception> report)
        {
            Enabled = enabled;
            Interval = interval;
            Report = report;
        }
    }

    private sealed class Lease : IDisposable
    {
        private Provider registered;
        internal Lease(Provider registered) => this.registered = registered;
        public void Dispose()
        {
            Provider candidate = Interlocked.Exchange(ref registered, null);
            if (candidate == null) return;
            lock (Gate)
            {
                if (ReferenceEquals(provider, candidate))
                    Volatile.Write(ref provider, null);
            }
        }
    }

    private static readonly object Gate = new();
    private static Provider provider;

    /// <summary>
    /// Load the editor-owned config before the DevTool starts. A missing file
    /// deliberately keeps the existing Remix provider live and read-only.
    /// </summary>
    public static void LoadOwned(string configRoot) =>
        RainwrightAutoSaveSettingsStore.Load(configRoot,
            error => global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "Rainwright AutoSave configuration is invalid; unattended saving is disabled.", error));

    public static bool HasOwnedSettings => RainwrightAutoSaveSettingsStore.HasOwner;

    /// <summary>
    /// Used by the Rainwright settings panel; always persist successfully
    /// before applying. Original DryCycle Remix options are never rewritten.
    /// </summary>
    public static bool TrySaveOwned(bool enabled, int intervalSeconds, out string error) =>
        RainwrightAutoSaveSettingsStore.TrySave(enabled, intervalSeconds, out error);

    public static bool TryQueueOwned(bool enabled, int intervalSeconds, out string error) =>
        RainwrightAutoSaveSettingsStore.TryQueueSave(enabled, intervalSeconds, out error);

    public static bool OwnedWritePending => RainwrightAutoSaveSettingsStore.HasPendingWrite;
    public static long OwnedWriteRevision => RainwrightAutoSaveSettingsStore.CompletedRevision;
    public static bool LastOwnedWriteSucceeded => RainwrightAutoSaveSettingsStore.LastWriteSucceeded;
    public static string LastOwnedWriteError => RainwrightAutoSaveSettingsStore.LastWriteError;

    /// <summary>Unity main-thread file writer, never called from ImGui.</summary>
    internal static void ProcessPendingOwnedWrites()
    {
        if (!RainwrightAutoSaveSettingsStore.HasPendingWrite) return;
        long oldRevision = RainwrightAutoSaveSettingsStore.CompletedRevision;
        RainwrightAutoSaveSettingsStore.ProcessPendingSave();
        if (RainwrightAutoSaveSettingsStore.CompletedRevision != oldRevision &&
            !RainwrightAutoSaveSettingsStore.LastWriteSucceeded)
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "Rainwright AutoSave preference write failed: " +
                RainwrightAutoSaveSettingsStore.LastWriteError);
    }

    public static IDisposable Register(Func<bool> isEnabled, Func<int> intervalSeconds,
        Action<Exception> report = null)
    {
        if (isEnabled == null) throw new ArgumentNullException(nameof(isEnabled));
        if (intervalSeconds == null) throw new ArgumentNullException(nameof(intervalSeconds));
        Provider replacement = new(isEnabled, intervalSeconds, report);
        lock (Gate)
        {
            if (provider != null)
                throw new InvalidOperationException("AutoSave preferences already have an owner.");
            Volatile.Write(ref provider, replacement);
        }
        return new Lease(replacement);
    }

    public static bool Enabled
    {
        get
        {
            if (RainwrightAutoSaveSettingsStore.HasOwner)
                return RainwrightAutoSaveSettingsStore.Enabled;
            Provider current = Volatile.Read(ref provider);
            if (current == null) return true;
            try { return current.Enabled(); }
            catch (Exception error)
            {
                ReportFailure(current, error);
                return false; // Never write unexpectedly when a live preference source fails.
            }
        }
    }

    public static int IntervalSeconds
    {
        get
        {
            if (RainwrightAutoSaveSettingsStore.HasOwner)
                return RainwrightAutoSaveSettingsStore.IntervalSeconds;
            Provider current = Volatile.Read(ref provider);
            if (current == null) return 150;
            try { return Math.Max(30, Math.Min(1800, current.Interval())); }
            catch (Exception error)
            {
                ReportFailure(current, error);
                return 150;
            }
        }
    }

    private static void ReportFailure(Provider source, Exception error)
    {
        try { source.Report?.Invoke(error); }
        catch { /* A third-party diagnostic callback cannot interrupt the game. */ }
    }
}
