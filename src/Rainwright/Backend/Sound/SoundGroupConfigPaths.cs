using System;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;

namespace DryCycle.DevUI.DevTool.Sound;

/// <summary>
/// One-time, read-only import of the previous DryCycle Sound Groups preferences.
/// An explicit Rainwright directory choice or already-written Rainwright group file
/// always takes priority. Import never points Rainwright writes at a DryCycle file,
/// including when the old settings selected a custom/external group folder.
/// </summary>
internal static class SoundGroupConfigPaths
{
    internal const string GroupFileName = "sound-groups.xml";
    internal const string LocationFileName = "sound-groups.location.txt";
    internal const string ImportMarkerName = "legacy-sound-groups-imported.marker";

    internal static string OwnedRoot(string configRoot) =>
        Path.Combine(configRoot, "Rainwright", "SoundGroups");

    internal static string LegacyRoot(string configRoot) =>
        Path.Combine(configRoot, "DryCycle", "DevTool");

    internal static bool TryImportLegacy(string configRoot, Action<string, Exception> report = null)
    {
        string owned = OwnedRoot(configRoot);
        string legacy = LegacyRoot(configRoot);
        string ownedGroups = Path.Combine(owned, GroupFileName);
        string ownedLocation = Path.Combine(owned, LocationFileName);
        string marker = Path.Combine(owned, ImportMarkerName);

        // Explicit user preferences are authoritative, and resetting the location
        // cannot trigger an unwanted import from the former DryCycle installation.
        if (File.Exists(ownedGroups) || File.Exists(ownedLocation) || File.Exists(marker))
            return false;

        string staged = ownedGroups + ".import.tmp";
        try
        {
            string legacyRootGroups = Path.Combine(legacy, GroupFileName);
            string sourceRoot = legacy;
            string legacyLocation = Path.Combine(legacy, LocationFileName);
            if (File.Exists(legacyLocation))
            {
                string configured = File.ReadAllText(legacyLocation)?.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    configured = Environment.ExpandEnvironmentVariables(configured);
                    if (string.Equals(Path.GetExtension(configured), ".xml",
                            StringComparison.OrdinalIgnoreCase))
                        configured = Path.GetDirectoryName(configured);
                    sourceRoot = Path.GetFullPath(configured);
                }
            }

            string source = Path.Combine(sourceRoot, GroupFileName);
            if (!File.Exists(source))
            {
                // A missing custom folder is not permission to silently substitute
                // a possibly stale group file from the old default location.
                if (File.Exists(legacyLocation) && File.Exists(legacyRootGroups))
                    report?.Invoke("Legacy sound-group custom path has no group file: " + source, null);
                return false;
            }

            FileInfo info = new(source);
            if (info.Length == 0 || info.Length > 8 * 1024 * 1024)
                throw new InvalidDataException("Invalid legacy Sound Groups document length: " + info.Length);

            XmlReaderSettings secure = new()
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 8 * 1024 * 1024
            };
            using (XmlReader reader = XmlReader.Create(source, secure))
            {
                XElement root = XDocument.Load(reader, LoadOptions.None).Root;
                if (root?.Name.LocalName != "SoundGroups")
                    throw new InvalidDataException("Legacy Sound Groups XML has an unexpected root: " + source);
            }

            Directory.CreateDirectory(owned);
            File.Copy(source, staged, overwrite: false);
            if (!string.Equals(Hash(source), Hash(staged), StringComparison.Ordinal))
                throw new InvalidDataException("Legacy Sound Groups changed during migration: " + source);

            // Preserve bytes, including custom third-party metadata, rather than
            // reserializing or modifying the old source file.
            File.Move(staged, ownedGroups);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
            return true;
        }
        catch (Exception error)
        {
            try { report?.Invoke("Legacy Sound Groups import failed; original data remains untouched.", error); }
            catch { /* Diagnostics must not hide the original legacy file. */ }
            return false;
        }
        finally
        {
            try { if (File.Exists(staged)) File.Delete(staged); }
            catch (Exception error)
            {
                try { report?.Invoke("Failed to clear staged Sound Groups import file.", error); }
                catch { }
            }
        }
    }

    internal static void MarkOptedOut(string configRoot)
    {
        string owned = OwnedRoot(configRoot);
        Directory.CreateDirectory(owned);
        File.WriteAllText(Path.Combine(owned, ImportMarkerName),
            DateTime.UtcNow.ToString("O"));
    }

    private static string Hash(string path)
    {
        using SHA256 sha = SHA256.Create();
        using FileStream stream = File.OpenRead(path);
        return BitConverter.ToString(sha.ComputeHash(stream));
    }
}
