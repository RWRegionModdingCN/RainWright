using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using BepInEx;
using DryCycle.DevUI.DevTool.World;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Crash-safe, non-destructive author-data snapshots. Snapshots are never written to an author
/// file until the developer explicitly requests recovery; snapshots themselves are immutable.
/// </summary>
internal static class RecoverySnapshotStore
{
    internal sealed class Candidate
    {
        internal string Id;
        internal string Document;
        internal string Target;
        internal DateTime CreatedUtc;
        internal string Sha256;
        internal string DataPath;
        // The author source as it existed when this candidate was offered.
        // Explicit recovery must not erase changes made while the dialog is open.
        internal bool TargetExistedWhenListed;
        internal string TargetHashWhenListed;
        // Optional v1+ author identity; missing on historical DryCycle snapshots.
        internal string CapturedOwnerModId;
    }

    private static readonly ConcurrentQueue<string> restoreRequests = new();
    private static readonly Dictionary<string, string> lastHashes = new(StringComparer.Ordinal);
    private static Candidate[] pending = Array.Empty<Candidate>();
    private static string status = string.Empty;
    private static bool started;
    private static bool restoreRequiresReload;
    private static int KeepPerDocument => global::DryCycle.DevUI.DevTool.Core.RainWrightPersonalSettings.SnapshotCount;
    private const long MaxSnapshotBytes = 8L * 1024 * 1024;

    // Rainwright must not share a crash marker or mutate recovery snapshots owned by
    // the previously bundled DryCycle editor. The old tree is imported read-only.
    private static string Root => Path.Combine(Paths.ConfigPath, "Rainwright", "AutoSave");
    private static string Marker => Path.Combine(Root, "active.lock");
    private static string LegacyRoot => Path.Combine(Paths.ConfigPath, "DryCycle", "AutoSave");
    private static string LegacyMarker => Path.Combine(LegacyRoot, "active.lock");
    private static string LegacyImportMarker => Path.Combine(Root, "legacy-recovery-imported.marker");

    internal static Candidate[] Pending => pending;
    internal static string Status => status;
    internal static bool RestoreRequiresReload => restoreRequiresReload;

    internal static void Begin()
    {
        if (started) return;
        started = true;
        try
        {
            Directory.CreateDirectory(Root);
            bool interrupted = File.Exists(Marker);
            // First Rainwright session: preserve any recovery candidates produced
            // by an interrupted legacy DryCycle DevTool session.
            bool imported = TryImportLegacyCrashRecovery();
            if (interrupted || imported)
                pending = LoadCandidates(Root);
            // Create the marker before any editor edits. A hard kill cannot execute End().
            File.WriteAllText(Marker, DateTime.UtcNow.ToString("O"));
            if ((interrupted || imported) && pending.Length > 0)
                status = imported
                    ? "Legacy DryCycle recovery snapshots were safely copied to Rainwright. Recovery is available."
                    : "A previous DevTool session ended unexpectedly. Recovery snapshots are available.";
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error("[DevTool.AutoSave] Recovery startup failed: " + error);
        }
    }

    internal static void End(bool retainRecovery = false)
    {
        if (!started) return;
        try { if (!retainRecovery && File.Exists(Marker)) File.Delete(Marker); }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("[DevTool.AutoSave] Could not clear active marker: " + error);
        }
        started = false;
        restoreRequiresReload = false;
        status = string.Empty;
        pending = Array.Empty<Candidate>();
        lastHashes.Clear();
        while (restoreRequests.TryDequeue(out _)) { }
    }

    internal static void CaptureRoom(string document, RoomSettings settings)
    {
        if (settings == null || string.IsNullOrWhiteSpace(settings.filePath)) return;
        try
        {
            string target = WorldModificationWorkspace.ResolveRoomSettings(settings.filePath);
            Capture(document, target, temp => settings.Save(temp, false));
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("[DevTool.AutoSave] Room recovery author resolution failed: " + error);
        }
    }

    internal static void CaptureText(string document, string target, string content)
    {
        if (string.IsNullOrEmpty(content)) return;
        Capture(document, target, null, content);
    }

    private static void Capture(string document, string target, Action<string> writer, string text = null)
    {
        string temp = null;
        try
        {
            if (!started || restoreRequiresReload || string.IsNullOrWhiteSpace(target) ||
                WorldAuthoringPathResolver.IsMergedCachePath(target))
                return;
            string normalizedTarget = Path.GetFullPath(target);
            if (text != null)
            {
                text = WorldModificationWorkspace.Encode(target, text);
                if (text.Length == 0 && WorldModificationWorkspace.IsPatch(target))
                    text = "// No modifications.\n";
            }
            string key = document + "|" + normalizedTarget;
            byte[] prepared = null;
            string preparedHash = null;
            if (text != null)
            {
                var encoding = new UTF8Encoding(false);
                int byteCount = encoding.GetByteCount(text);
                if (byteCount == 0 || byteCount > MaxSnapshotBytes)
                    throw new InvalidDataException("Recovery snapshot size is invalid: " + byteCount);
                prepared = encoding.GetBytes(text);
                using SHA256 sha = SHA256.Create();
                preparedHash = BitConverter.ToString(sha.ComputeHash(prepared)).Replace("-", "").ToLowerInvariant();
                if (lastHashes.TryGetValue(key, out string known) && known == preparedHash) return;
            }
            Directory.CreateDirectory(Root);
            temp = Path.Combine(Root, Guid.NewGuid().ToString("N") + ".tmp");
            if (prepared == null)
            {
                writer(temp);
                if (WorldModificationWorkspace.IsPatch(target))
                {
                    string encoded = WorldModificationWorkspace.Encode(target, File.ReadAllText(temp));
                    File.WriteAllText(temp, encoded.Length == 0 ? "// No modifications.\n" : encoded);
                }
            }
            else File.WriteAllBytes(temp, prepared);
            FileInfo info = new(temp);
            if (info.Length == 0 || info.Length > MaxSnapshotBytes)
                throw new InvalidDataException("Recovery snapshot size is invalid: " + info.Length);

            string hash = HashFile(temp);
            if (preparedHash != null && hash != preparedHash)
                throw new InvalidDataException("Recovery snapshot bytes differ from the captured text.");
            if (lastHashes.TryGetValue(key, out string previous) && previous == hash)
                return;

            string id = Guid.NewGuid().ToString("N");
            string data = Path.Combine(Root, "snapshot-" + id + ".data");
            string meta = Path.Combine(Root, "snapshot-" + id + ".xml");
            File.Move(temp, data);
            temp = null;
            // A manifest is published only after its data file is complete and verified.
            XDocument xml = new(new XElement("Recovery",
                new XAttribute("version", 1),
                new XAttribute("document", document),
                new XAttribute("target", normalizedTarget),
                new XAttribute("utc", DateTime.UtcNow.ToString("O")),
                new XAttribute("sha256", hash)));
            // Preserve the original active Mod identity where one is available.
            // This is optional so old manifests remain readable without mutation.
            if (!string.Equals(Path.GetFileName(target), "WorldTopology.json",
                    StringComparison.OrdinalIgnoreCase) &&
                !IsPaletteDraftTarget(target) &&
                WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(
                    target, out string capturedOwnerModId, out _) &&
                !string.IsNullOrWhiteSpace(capturedOwnerModId))
                xml.Root.Add(new XAttribute("ownerModId", capturedOwnerModId));
            string metaTemp = meta + ".tmp";
            xml.Save(metaTemp);
            File.Move(metaTemp, meta);
            lastHashes[key] = hash;
            Prune(document, normalizedTarget);
        }
        catch (Exception error)
        {
            status = "Recovery snapshot failed: " + error.Message;
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error("[DevTool.AutoSave] " + status + " " + error);
        }
        finally
        {
            try { if (temp != null && File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private static void Prune(string document, string target)
    {
        // Metadata-only pruning: do not hash every stored document on the editor main thread.
        List<(string manifest, string data, DateTime utc)> matches = new();
        foreach (string manifest in Directory.GetFiles(Root, "snapshot-*.xml"))
        {
            try
            {
                XElement root = XDocument.Load(manifest).Root;
                if (root == null || (string)root.Attribute("document") != document ||
                    (string)root.Attribute("target") != target) continue;
                string id = Path.GetFileNameWithoutExtension(manifest).Substring("snapshot-".Length);
                if (id.Length != 32 || !id.All(Uri.IsHexDigit)) continue;
                DateTime utc = DateTime.Parse((string)root.Attribute("utc"),
                    null, System.Globalization.DateTimeStyles.RoundtripKind);
                matches.Add((manifest, Path.Combine(Root, "snapshot-" + id + ".data"), utc));
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning(
                    "[DevTool.AutoSave] Recovery pruning ignored invalid metadata: " + error.Message);
            }
        }
        matches.Sort((a, b) => b.utc.CompareTo(a.utc));
        for (int i = KeepPerDocument; i < matches.Count; i++)
        {
            try
            {
                File.Delete(matches[i].manifest);
                File.Delete(matches[i].data);
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("[DevTool.AutoSave] Snapshot pruning failed: " + error);
            }
        }
    }

    private static Candidate[] LoadCandidates(string sourceRoot)
    {
        List<Candidate> result = new();
        foreach (string file in Directory.GetFiles(sourceRoot, "snapshot-*.xml"))
        {
            try
            {
                string id = Path.GetFileNameWithoutExtension(file).Substring("snapshot-".Length);
                if (id.Length != 32 || !id.All(Uri.IsHexDigit)) continue;
                XElement root = XDocument.Load(file).Root;
                if (root?.Name.LocalName != "Recovery") continue;
                string hash = (string)root.Attribute("sha256");
                string target = (string)root.Attribute("target");
                string document = (string)root.Attribute("document");
                string data = Path.Combine(sourceRoot, "snapshot-" + id + ".data");
                if (string.IsNullOrWhiteSpace(hash) || hash.Length != 64 ||
                    string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(document) ||
                    !DateTime.TryParse((string)root.Attribute("utc"), null,
                        System.Globalization.DateTimeStyles.RoundtripKind, out DateTime date) ||
                    !File.Exists(data) || new FileInfo(data).Length > MaxSnapshotBytes)
                    continue;
                if (HashFile(data) != hash) continue;
                // An identical author file needs no recovery. Retain the
                // observed source hash for a restore-time external-change guard.
                bool targetExists = File.Exists(target);
                string observedTargetHash = targetExists ? HashFile(target) : null;
                if (string.Equals(observedTargetHash, hash, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new Candidate { Id = id, Document = document, Target = target,
                    CreatedUtc = date.ToUniversalTime(), Sha256 = hash, DataPath = data,
                    TargetExistedWhenListed = targetExists, TargetHashWhenListed = observedTargetHash,
                    CapturedOwnerModId = (string)root.Attribute("ownerModId") });
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("[DevTool.AutoSave] Invalid recovery manifest '" +
                    file + "': " + error.Message);
            }
        }
        return result.OrderByDescending(c => c.CreatedUtc).ToArray();
    }

    private static bool TryImportLegacyCrashRecovery()
    {
        // Only a legacy session that ended without clearing its own lock is
        // eligible. Do not import ordinary stale recovery files from clean exits.
        if (File.Exists(LegacyImportMarker) || !File.Exists(LegacyMarker))
            return false;

        bool copiedAny = false;
        try
        {
            Candidate[] recoverable = LoadCandidates(LegacyRoot);
            foreach (Candidate candidate in recoverable)
            {
                string prefix = "snapshot-" + candidate.Id;
                string destinationData = Path.Combine(Root, prefix + ".data");
                string destinationManifest = Path.Combine(Root, prefix + ".xml");

                // Never overwrite an existing Rainwright snapshot, even if an
                // old manifest reused an id or an earlier migration was partial.
                if (File.Exists(destinationData) || File.Exists(destinationManifest))
                {
                    global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning(
                        "[Rainwright.AutoSave] Legacy snapshot " + candidate.Id +
                        " was not imported because a destination with the same id already exists.");
                    continue;
                }

                string legacyManifest = Path.Combine(LegacyRoot, prefix + ".xml");
                string stagedData = destinationData + ".import.tmp";
                string stagedManifest = destinationManifest + ".import.tmp";
                bool createdData = false;
                try
                {
                    File.Copy(candidate.DataPath, stagedData, overwrite: false);
                    if (!string.Equals(HashFile(stagedData), candidate.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Legacy snapshot changed during import: " + candidate.Id);
                    File.Copy(legacyManifest, stagedManifest, overwrite: false);
                    // Publish the manifest only after the immutable payload is complete.
                    File.Move(stagedData, destinationData);
                    createdData = true;
                    File.Move(stagedManifest, destinationManifest);
                    copiedAny = true;
                }
                catch
                {
                    // Only remove the copy created by this transaction; the
                    // historical recovery bytes and marker are never modified.
                    if (createdData && !File.Exists(destinationManifest))
                    {
                        try { File.Delete(destinationData); }
                        catch (Exception cleanupError)
                        {
                            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning(
                                "[Rainwright.AutoSave] Could not remove incomplete imported copy: " +
                                cleanupError);
                        }
                    }
                    throw;
                }
                finally
                {
                    try { if (File.Exists(stagedData)) File.Delete(stagedData); }
                    catch (Exception cleanupError)
                    {
                        global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning(
                            "[Rainwright.AutoSave] Could not clear staged import data: " + cleanupError);
                    }
                    try { if (File.Exists(stagedManifest)) File.Delete(stagedManifest); }
                    catch (Exception cleanupError)
                    {
                        global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning(
                            "[Rainwright.AutoSave] Could not clear staged import metadata: " + cleanupError);
                    }
                }
            }

            // Import is complete only after all valid candidates were handled.
            // An interrupted copy can be retried at the next session start.
            File.WriteAllText(LegacyImportMarker, DateTime.UtcNow.ToString("O"));
            return copiedAny;
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error(
                "[Rainwright.AutoSave] Read-only legacy crash-recovery import failed: " + error);
            return copiedAny;
        }
    }

    // The recovery format and integrity hash now have one RainWright.dll owner.
    private static bool IsPaletteDraftTarget(string path) => string.Equals(Path.GetFullPath(path),
        Path.GetFullPath(Path.Combine(Paths.ConfigPath, "RainWright.Palette.xml")),
        Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string HashFile(string path) => EditorRecoveryFileTransaction.HashFile(path);

    internal static void RequestRestore(string id)
    {
        if (!string.IsNullOrEmpty(id)) restoreRequests.Enqueue(id);
    }

    internal static void Dismiss(string id)
    {
        // UI may render on a different thread; publish a fresh immutable list.
        pending = pending.Where(c => c.Id != id).ToArray();
    }

    internal static void ProcessRequests()
    {
        if (!started) return;
        while (restoreRequests.TryDequeue(out string id))
        {
            Candidate candidate = pending.FirstOrDefault(c => c.Id == id);
            if (candidate == null) continue;
            try
            {
                if (!File.Exists(candidate.DataPath) || HashFile(candidate.DataPath) != candidate.Sha256)
                    throw new InvalidDataException("Snapshot integrity check failed.");
                if (WorldAuthoringPathResolver.IsMergedCachePath(candidate.Target))
                    throw new InvalidOperationException("Recovery into mergedmods is forbidden.");
                // A snapshot records the path that *used* to be authored, not
                // permission to write there indefinitely. WorldTopology is a
                // gameplay-owned NR.B5 file, even when restored via Rainwright.
                // Check the current scoped owner at the actual restore point;
                // imported legacy manifests must obey this rule as well.
                if (string.Equals(Path.GetFileName(candidate.Target),
                        "WorldTopology.json", StringComparison.OrdinalIgnoreCase))
                {
                    string currentTarget = WorldTopologyPathOwnership.Resolve(forSave: true);
                    if (string.IsNullOrWhiteSpace(currentTarget) ||
                        !string.Equals(Path.GetFullPath(currentTarget),
                            Path.GetFullPath(candidate.Target), StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "WorldTopology recovery refused: the current RainWright plugin owner " +
                            "does not authorize this author file. The snapshot remains available.");
                }
                else if (IsPaletteDraftTarget(candidate.Target))
                {
                    // The editor owns this exact private working document; exported PNGs are never
                    // recovery targets. All other files retain active-Mod source authorization.
                }
                else
                {
                    if (!WorldAuthoringPathResolver.TryAuthorizeRecoveryTarget(
                            candidate.Target, out string currentOwnerModId, out string ownershipError))
                        throw new InvalidOperationException(ownershipError);
                    if (!string.IsNullOrWhiteSpace(candidate.CapturedOwnerModId) &&
                        !string.Equals(candidate.CapturedOwnerModId, currentOwnerModId,
                            StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Recovery refused: the original author Mod identity changed. " +
                            "The snapshot remains available.");
                }
                // Path authorization above remains game-specific. Atomic file
                // restoration and external-edit protection belong to Rainwright.
                EditorRecoveryFileTransaction.Restore(
                    candidate.DataPath, candidate.Sha256, candidate.Target,
                    candidate.TargetExistedWhenListed, candidate.TargetHashWhenListed);
                restoreRequiresReload = true;
                status = "Recovered " + candidate.Target +
                    ". The editor is write-suspended; restart Rain World before further editing.";
                pending = pending.Where(c => c.Id != id).ToArray();
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Warning("[DevTool.AutoSave] " + status);
            }
            catch (Exception error)
            {
                status = "Recovery failed for " + candidate.Target + ": " + error.Message;
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.Error("[DevTool.AutoSave] " + status + " " + error);
            }

        }
    }
}
