using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DryCycle.DevUI.DevTool.World;

/// <summary>Produces ordinary Remix modification directives; no runtime patch format or loader.</summary>
internal static class ModificationFileComposer
{
    private sealed class Edit
    {
        internal int Index;
        internal string Replacement;
        internal bool Before;
    }

    internal static string Compose(string baseline, string edited, string existingPatch)
    {
        string[] a = Lines(baseline), b = Lines(edited);
        var matches = new List<(int A, int B)>();
        Match(a, 0, a.Length, b, 0, b.Length, matches);
        matches.Add((a.Length, b.Length));
        var edits = new List<Edit>();
        var added = new List<string>();
        int ai = 0, bi = 0;
        foreach (var match in matches)
        {
            string inserted = string.Join("\\n", Slice(b, bi, match.B));
            if (match.A > ai)
            {
                edits.Add(new Edit { Index = ai, Replacement = inserted });
                for (int i = ai + 1; i < match.A; i++)
                    edits.Add(new Edit { Index = i, Replacement = "" });
            }
            else if (match.B > bi)
            {
                if (match.A < a.Length)
                    edits.Add(new Edit { Index = match.A, Replacement = inserted + "\\n", Before = true });
                else added.AddRange(Slice(b, bi, match.B));
            }
            ai = match.A + 1; bi = match.B + 1;
        }
        if (edits.Count == 0 && added.Count == 0) return existingPatch ?? "";

        // Native replacements operate sequentially on each line. Refuse a structural swap whose
        // replacement would also match another edit, rather than silently applying it twice.
        foreach (Edit edit in edits)
            foreach (Edit other in edits)
                if (!ReferenceEquals(edit, other) && edit.Replacement == a[other.Index])
                    throw new InvalidDataException("The edit swaps identical lines; split this structural change into separate saves.");

        string prefix = existingPatch ?? "";
        // [ADD] runs at the end of the native modification pass, while multi-line replacement
        // results remain one string during that pass. Amend such an existing directive's output
        // in place; a later FIND on its already-expanded output would otherwise silently miss it.
        var pendingEdits = new List<Edit>();
        foreach (Edit edit in edits)
        {
            string original = a[edit.Index];
            MatchCollection commands = Regex.Matches(prefix,
                @"(?m)^(\[(?:ADD(?:_\d+)?|REPLACE(?:_\d+)?|ADDBEFORE(?:_\d+)?|ADDAFTER(?:_\d+)?)\])([^\r\n]*)");
            System.Text.RegularExpressions.Match command = null;
            string[] fragments = null;
            int fragment = -1, count = 0;
            foreach (System.Text.RegularExpressions.Match candidate in commands)
            {
                string[] parts = candidate.Groups[2].Value.Split(new[] { "\\n" }, StringSplitOptions.None);
                for (int j = 0; j < parts.Length; j++)
                    if (parts[j] == original) { count++; command = candidate; fragments = parts; fragment = j; }
            }
            if (count == 0) { pendingEdits.Add(edit); continue; }
            int occurrences = 0;
            foreach (string line in a) if (line == original) occurrences++;
            if (count != 1 || occurrences != 1)
                throw new InvalidDataException("An existing modification adds duplicate identical lines. Edit that ambiguous directive explicitly before saving.");
            fragments[fragment] = edit.Before ? edit.Replacement + original : edit.Replacement;
            string changed = command.Groups[1].Value + string.Join("\\n", fragments);
            prefix = prefix.Remove(command.Index, command.Length).Insert(command.Index, changed);
        }
        if (pendingEdits.Count > 0 && Regex.IsMatch(prefix,
                @"(?m)^\[(?:REPLACE(?:_\d+)?|ADDBEFORE(?:_\d+)?|ADDAFTER(?:_\d+)?)\][^\r\n]*\\n"))
            throw new InvalidDataException("The existing multi-line modification cannot be changed losslessly by this save. Its original text was preserved.");

        var delta = new StringBuilder();
        var removed = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Edit edit in pendingEdits)
        {
            string original = a[edit.Index];
            int occurrence = 0;
            for (int i = 0; i <= edit.Index; i++) if (a[i] == original) occurrence++;
            if (removed.TryGetValue(original, out int earlier)) occurrence -= earlier;
            // Exact whole-line anchors avoid matching a room in another section or timeline.
            delta.Append("[FINDLINEREGEX]^").Append(Regex.Escape(original).Replace("\\\\n", "\\x5cn")).AppendLine("$");
            delta.Append(edit.Before ? "[ADDBEFORE_" : "[REPLACE_").Append(occurrence).Append(']')
                .AppendLine(edit.Replacement);
            removed[original] = earlier + 1;
        }
        foreach (string line in added) delta.Append("[ADD]").AppendLine(line);

        bool merge = false, destination = false;
        foreach (string line in prefix.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("[FILEDESTINATION]", StringComparison.Ordinal)) destination = true;
            if (line.Contains("[ENDFILEDESTINATION]")) destination = false;
            if (destination) continue;
            if (line.StartsWith("[MERGE]", StringComparison.Ordinal)) merge = true;
            if (line.Contains("[ENDMERGE]")) merge = false;
        }
        if (destination) throw new InvalidDataException("Close the existing [FILEDESTINATION] block before editing this modification file.");
        if (prefix.Length > 0 && !prefix.EndsWith("\n", StringComparison.Ordinal)) prefix += Environment.NewLine;
        if (merge) prefix += "[ENDMERGE]" + Environment.NewLine;
        return prefix + delta;
    }

    // Hirschberg LCS: linear storage even for large regional world/map files.
    private static void Match(string[] a, int lo, int hi, string[] b, int start, int end,
        List<(int A, int B)> result)
    {
        while (lo < hi && start < end && a[lo] == b[start]) { result.Add((lo++, start++)); }
        int suffix = 0;
        while (hi > lo && end > start && a[hi - 1] == b[end - 1]) { hi--; end--; suffix++; }
        if (lo < hi && start < end)
        {
            if (hi - lo == 1)
            {
                for (int j = start; j < end; j++) if (a[lo] == b[j]) { result.Add((lo, j)); break; }
            }
            else
            {
                int mid = (lo + hi) / 2;
                int[] left = Score(a, lo, mid, b, start, end, false);
                int[] right = Score(a, mid, hi, b, start, end, true);
                int best = -1, split = 0, length = end - start;
                for (int j = 0; j <= length; j++)
                    if (left[j] + right[length - j] > best) { best = left[j] + right[length - j]; split = j; }
                Match(a, lo, mid, b, start, start + split, result);
                Match(a, mid, hi, b, start + split, end, result);
            }
        }
        for (int k = 0; k < suffix; k++) result.Add((hi + k, end + k));
    }

    private static int[] Score(string[] a, int lo, int hi, string[] b, int start, int end, bool reverse)
    {
        int[] row = new int[end - start + 1];
        for (int i = 0; i < hi - lo; i++)
        {
            int previous = 0;
            for (int j = 1; j < row.Length; j++)
            {
                int old = row[j];
                row[j] = a[reverse ? hi - 1 - i : lo + i] == b[reverse ? end - j : start + j - 1]
                    ? previous + 1 : Math.Max(row[j], row[j - 1]);
                previous = old;
            }
        }
        return row;
    }

    private static List<string> Slice(string[] lines, int start, int end)
    {
        var result = new List<string>();
        for (int i = start; i < end; i++)
        {
            if (lines[i].Contains("\\n")) throw new InvalidDataException("A literal backslash-n cannot be represented losslessly by Remix replacement directives.");
            result.Add(lines[i]);
        }
        return result;
    }

    private static string[] Lines(string text)
    {
        var result = new List<string>();
        foreach (string line in (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            if (!string.IsNullOrWhiteSpace(line)) result.Add(line);
        return result.ToArray();
    }
}
