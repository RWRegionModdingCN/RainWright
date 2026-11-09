using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Rainwright-owned AutoSave preferences. No DryCycle Remix type or mod ID
/// appears here. Absence of the file intentionally falls back to the optional,
/// read-only DryCycle legacy provider until the user explicitly applies settings.
/// A corrupt owned file never silently falls back to a different mod's values
/// or enables unattended writes.
/// </summary>
internal static class RainwrightAutoSaveSettingsStore
{
    private static readonly object Sync = new();
    private const string Name = "Rainwright.AutoSave.xml";
    private const int Version = 1;
    private static string path;
    private static XElement preservedDocument;
    private static bool hasOwner;
    private static bool enabled = true;
    private static int intervalSeconds = 150;

    private sealed class PendingWrite
    {
        internal bool Enabled;
        internal int IntervalSeconds;
        internal string TargetPath;
    }

    private static PendingWrite pendingWrite;
    private static bool writeInFlight;
    private static long completedRevision;
    private static bool completedSuccess;
    private static string completedError = string.Empty;

    internal static bool HasPendingWrite
    {
        get { lock (Sync) return pendingWrite != null || writeInFlight; }
    }

    // The native ImGui callback reads these every frame. Do not block
    // its render thread behind main-thread disk writes or XML parsing.
    // Writers publish error/success first and revision last.
    internal static long CompletedRevision => Volatile.Read(ref completedRevision);
    internal static bool LastWriteSucceeded => Volatile.Read(ref completedSuccess);
    internal static string LastWriteError => Volatile.Read(ref completedError);

    /// <summary>
    /// Called by the native RWImGUI callback. Only validate and queue; all
    /// disk writes occur in the game's managed main-thread AutoSave pump.
    /// </summary>
    internal static bool TryQueueSave(bool newEnabled, int newIntervalSeconds, out string error)
    {
        error = null;
        // Native ImGui callbacks must not wait for game-thread XML/disk IO.
        if (!Monitor.TryEnter(Sync))
        {
            error = "Rainwright AutoSave settings are busy; retry after the current write.";
            return false;
        }
        try
        {
            if (newIntervalSeconds < 30 || newIntervalSeconds > 1800)
            {
                error = "AutoSave interval must be between 30 and 1800 seconds.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Rainwright AutoSave preferences have not been loaded.";
                return false;
            }
            if (pendingWrite != null || writeInFlight)
            {
                error = "A Rainwright AutoSave settings write is already pending.";
                return false;
            }
            pendingWrite = new PendingWrite
            {
                Enabled = newEnabled,
                IntervalSeconds = newIntervalSeconds,
                TargetPath = path
            };
            return true;
        }
        finally
        {
            Monitor.Exit(Sync);
        }
    }

    internal static void ProcessPendingSave()
    {
        PendingWrite task;
        lock (Sync)
        {
            task = pendingWrite;
            if (task == null || writeInFlight) return;
            pendingWrite = null;
            writeInFlight = true;
        }

        string error;
        bool success;
        try
        {
            success = TrySave(task.Enabled, task.IntervalSeconds, out error, task.TargetPath);
        }
        catch (Exception exception)
        {
            success = false;
            error = exception.ToString();
        }
        lock (Sync)
        {
            writeInFlight = false;
            Volatile.Write(ref completedError, error ?? string.Empty);
            Volatile.Write(ref completedSuccess, success);
            Volatile.Write(ref completedRevision, completedRevision + 1);
        }
    }

    internal static bool HasOwner => Volatile.Read(ref hasOwner);
    internal static bool Enabled => Volatile.Read(ref enabled);
    internal static int IntervalSeconds => Volatile.Read(ref intervalSeconds);

    internal static string PathForRoot(string configRoot) =>
        Path.Combine(configRoot ?? throw new ArgumentNullException(nameof(configRoot)), Name);

    internal static void Load(string configRoot, Action<Exception> report = null)
    {
        string location = PathForRoot(configRoot);
        lock (Sync)
        {
            // A pending request belongs to the config root at which it was
            // submitted. Never let a Mod reload/changed config root redirect
            // that write to another installation's settings file.
            if (pendingWrite != null &&
                !string.IsNullOrEmpty(path) &&
                !string.Equals(path, location, StringComparison.Ordinal))
            {
                pendingWrite = null;
                Volatile.Write(ref completedError,
                    "Pending AutoSave settings write cancelled: configuration root changed.");
                Volatile.Write(ref completedSuccess, false);
                Volatile.Write(ref completedRevision, completedRevision + 1);
            }
            path = location;
            preservedDocument = null;
            Volatile.Write(ref enabled, true);
            Volatile.Write(ref intervalSeconds, 150);
            // Presence is authoritative even if the new file cannot be read:
            // never recover a potentially disabled AutoSave from legacy Remix.
            Volatile.Write(ref hasOwner, File.Exists(location));
            if (!hasOwner) return;
            try
            {
                var options = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 65536
                };
                using XmlReader reader = XmlReader.Create(location, options);
                XElement root = XDocument.Load(reader).Root;
                if (root?.Name.LocalName != "RainwrightAutoSave" ||
                    (string)root.Attribute("version") != Version.ToString(CultureInfo.InvariantCulture) ||
                    !bool.TryParse((string)root.Attribute("enabled"), out bool parsedEnabled) ||
                    !int.TryParse((string)root.Attribute("intervalSeconds"),
                        NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInterval) ||
                    parsedInterval < 30 || parsedInterval > 1800)
                    throw new InvalidDataException("Invalid Rainwright.AutoSave.xml version or preference values.");

                preservedDocument = new XElement(root);
                Volatile.Write(ref enabled, parsedEnabled);
                Volatile.Write(ref intervalSeconds, parsedInterval);
            }
            catch (Exception error)
            {
                Volatile.Write(ref enabled, false); // fail closed; never unexpectedly save author data
                Volatile.Write(ref intervalSeconds, 150);
                try { report?.Invoke(error); } catch { }
            }
        }
    }

    internal static bool TrySave(bool newEnabled, int newIntervalSeconds,
        out string error, string expectedPath = null)
    {
        error = null;
        if (newIntervalSeconds < 30 || newIntervalSeconds > 1800)
        {
            error = "AutoSave interval must be between 30 and 1800 seconds.";
            return false;
        }

        lock (Sync)
        {
            // Check the captured target under the same lock as the actual
            // filesystem write, not in a racy preliminary UI callback.
            if (expectedPath != null &&
                !string.Equals(path, expectedPath, StringComparison.Ordinal))
            {
                error = "AutoSave preferences were not written: configuration root changed before IO.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Rainwright AutoSave preferences have not been loaded.";
                return false;
            }
            string staged = path + ".pending-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                XElement data = preservedDocument == null
                    ? new XElement("RainwrightAutoSave") : new XElement(preservedDocument);
                data.SetAttributeValue("version", Version);
                data.SetAttributeValue("enabled", newEnabled);
                data.SetAttributeValue("intervalSeconds", newIntervalSeconds);
                new XDocument(data).Save(staged);

                if (File.Exists(path))
                    DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(staged, path, path + ".bak");
                else
                    File.Move(staged, path);
                // Publish only after successful persistence. A failed write
                // never lies to the UI or changes the active save policy.
                Volatile.Write(ref enabled, newEnabled);
                Volatile.Write(ref intervalSeconds, newIntervalSeconds);
                preservedDocument = data;
                Volatile.Write(ref hasOwner, true);
                return true;
            }
            catch (Exception exception)
            {
                error = exception + " Recovery staging path: " + staged;
                return false;
            }

        }
    }
}
