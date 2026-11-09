using System;
using System.Threading;
using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace DryCycle.DevUI.DevTool.Input;

/// <summary>
/// Rainwright-owned gameplay input exceptions for Ctrl+Z/S/Y in the editor.
/// Defaults reserve all three keys for authoring. Until explicitly saved,
/// the existing DryCycle Remix provider supplies read-only legacy values.
/// No native ImGui callback performs file IO.
/// </summary>
internal static class RainwrightShortcutInputSettingsStore
{
    private const string FileName = "Rainwright.ShortcutInput.xml";
    private static readonly object Sync = new();
    private static string path;
    private static XElement preservedDocument;
    private static bool hasOwner;
    // Atomic 3-bit snapshot avoids three locks in the high-frequency
    // player checkInput/gameplay-hotkey suppression path.
    private static int gameplayFlags;

    private sealed class Request
    {
        internal string TargetPath;
        internal bool Z, S, Y;
    }

    private static Request pending;
    private static bool writing;
    private static long completedRevision;
    private static bool lastSuccess;
    private static string lastError = string.Empty;

    internal static bool HasOwner => Volatile.Read(ref hasOwner);
    internal static bool Z => (Volatile.Read(ref gameplayFlags) & 1) != 0;
    internal static bool S => (Volatile.Read(ref gameplayFlags) & 2) != 0;
    internal static bool Y => (Volatile.Read(ref gameplayFlags) & 4) != 0;
    internal static bool Pending { get { lock (Sync) return pending != null || writing; } }
    // Native rendering reads the completion snapshot every frame; never
    // acquire the config writer lock from the ImGui callback.
    internal static long CompletedRevision => Volatile.Read(ref completedRevision);
    internal static bool LastSuccess => Volatile.Read(ref lastSuccess);
    internal static string LastError => Volatile.Read(ref lastError);

    internal static string PathForRoot(string root) =>
        Path.Combine(root ?? throw new ArgumentNullException(nameof(root)), FileName);

    internal static void Load(string root, Action<Exception> report = null)
    {
        string next = PathForRoot(root);
        lock (Sync)
        {
            if (pending != null && !string.IsNullOrWhiteSpace(path) &&
                !string.Equals(path, next, StringComparison.Ordinal))
            {
                pending = null;
                Volatile.Write(ref lastError,
                    "Queued shortcut input settings were cancelled after the configuration root changed.");
                Volatile.Write(ref lastSuccess, false);
                Volatile.Write(ref completedRevision, completedRevision + 1);
            }

            path = next;
            preservedDocument = null;
            Volatile.Write(ref gameplayFlags, 0);
            Volatile.Write(ref hasOwner, File.Exists(path));
            if (!hasOwner) return;

            try
            {
                var options = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 65536
                };
                using XmlReader reader = XmlReader.Create(path, options);
                XElement data = XDocument.Load(reader).Root;
                if (data?.Name.LocalName != "RainwrightShortcutInput" ||
                    (string)data.Attribute("version") != "1" ||
                    !bool.TryParse((string)data.Attribute("allowZ"), out bool nz) ||
                    !bool.TryParse((string)data.Attribute("allowS"), out bool ns) ||
                    !bool.TryParse((string)data.Attribute("allowY"), out bool ny))
                    throw new InvalidDataException("Invalid Rainwright shortcut-input preference document.");

                preservedDocument = new XElement(data);
                Volatile.Write(ref gameplayFlags,
                    (nz ? 1 : 0) | (ns ? 2 : 0) | (ny ? 4 : 0));
            }
            catch (Exception error)
            {
                // Do not silently unlock player movement when config is corrupt.
                Volatile.Write(ref gameplayFlags, 0);
                try { report?.Invoke(error); }
                catch { }
            }
        }
    }

    internal static bool TryQueue(bool nextZ, bool nextS, bool nextY, out string error)
    {
        error = null;
        // Native ImGui callbacks must not wait for game-thread XML/disk IO.
        if (!Monitor.TryEnter(Sync))
        {
            error = "Rainwright shortcut input settings are busy; retry after the current write.";
            return false;
        }
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Rainwright shortcut input settings have not been initialized.";
                return false;
            }
            if (pending != null || writing)
            {
                error = "A shortcut input settings write is still pending.";
                return false;
            }

            pending = new Request { TargetPath = path, Z = nextZ, S = nextS, Y = nextY };
            return true;
        }
        finally
        {
            Monitor.Exit(Sync);
        }
    }

    internal static bool TrySave(bool nextZ, bool nextS, bool nextY, out string error,
        string expectedPath = null)
    {
        error = null;
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Rainwright shortcut input settings have not been initialized.";
                return false;
            }
            if (expectedPath != null &&
                !string.Equals(expectedPath, path, StringComparison.Ordinal))
            {
                error = "Shortcut input settings write rejected: configuration root changed.";
                return false;
            }

            string temp = path + ".pending-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                XElement data = preservedDocument == null
                    ? new XElement("RainwrightShortcutInput") : new XElement(preservedDocument);
                data.SetAttributeValue("version", 1);
                data.SetAttributeValue("allowZ", nextZ);
                data.SetAttributeValue("allowS", nextS);
                data.SetAttributeValue("allowY", nextY);
                new XDocument(data).Save(temp);

                if (File.Exists(path))
                    DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(temp, path, path + ".bak");
                else
                    File.Move(temp, path);

                Volatile.Write(ref gameplayFlags,
                    (nextZ ? 1 : 0) | (nextS ? 2 : 0) | (nextY ? 4 : 0));
                preservedDocument = data;
                Volatile.Write(ref hasOwner, true);
                return true;
            }
            catch (Exception errorThrown)
            {
                error = errorThrown + " Recovery staging path: " + temp;
                return false;
            }

        }
    }

    internal static void ProcessPending()
    {
        Request request;
        lock (Sync)
        {
            if (pending == null || writing) return;
            request = pending;
            pending = null;
            writing = true;
        }

        string error;
        bool success;
        try
        {
            success = TrySave(request.Z, request.S, request.Y, out error, request.TargetPath);
        }
        catch (Exception unexpected)
        {
            success = false;
            error = unexpected.ToString();
        }

        lock (Sync)
        {
            writing = false;
            Volatile.Write(ref lastError, error ?? string.Empty);
            Volatile.Write(ref lastSuccess, success);
            Volatile.Write(ref completedRevision, completedRevision + 1);
        }
    }
}
