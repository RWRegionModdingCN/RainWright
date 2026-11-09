using System;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.AutoSave;

/// <summary>
/// Vanilla Relationships 'Save' only prints code into the log. Preserve the actual override
/// source lines in a durable, editable export instead of reporting a fictitious file save.
/// This is a code export, not an automatic patch to another mod's creature registration.
/// </summary>
internal static class RelationshipSaveAdapter
{
    private static string Target =>
        RelationshipExportPaths.Current(Paths.ConfigPath);

    internal static bool TryGetText(out string path, out string text)
    {
        path = Target;
        text = string.Empty;
        var changes = RelationshipPage.changedRelationships;
        StringBuilder result = new();
        result.AppendLine("// Rainwright Relationships - generated source export.");
        result.AppendLine("// Paste entries into the owning mod's relationship initialization code.");
        result.AppendLine("// An empty export means all editor overrides were cleared.");
        result.AppendLine();
        if (changes != null)
        {
            foreach (var pair in changes.OrderBy(x => x.Key.value, StringComparer.Ordinal))
            {
                if (pair.Value == null) continue;
                foreach (var relationship in pair.Value.OrderBy(x => x.Key.value, StringComparer.Ordinal))
                    result.AppendLine(RelationshipPage.EstablishRelationshipCodeString(
                        pair.Key, relationship.Key, relationship.Value));
                result.AppendLine();
            }
        }
        text = result.ToString();
        return true;
    }

    internal static bool Export()
    {
        try
        {
            if (!TryGetText(out string path, out string text))
            {
                EditorDiagnostics.Error("[Rainwright.Relationships] Failed to prepare relationship source export.");
                return false;
            }
            // Read-only migration. Never delete, rename or rewrite the old
            // DryCycle relationship source, including on later exports.
            RelationshipExportPaths.ImportLegacyIfNeeded(Paths.ConfigPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text, new UTF8Encoding(false));
            if (File.Exists(path))
                DryCycle.DevUI.DevTool.Persistence.EditorBackupFiles.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
            EditorDiagnostics.Info(
                "[Rainwright.Relationships] Exported relationship source lines to " + path +
                ". These code lines must be integrated into the owning mod to persist in gameplay.");
            return true;
        }
        catch (Exception error)
        {
            EditorDiagnostics.Error(
                "[Rainwright.Relationships] Export of authoring relationship code failed", error);
            return false;
        }
    }
}
