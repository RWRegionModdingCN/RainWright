using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DryCycle.DevUI.DevTool.Background;

/// <summary>Community TXT element and inheritance semantics, independent of any editor or renderer.</summary>
internal static class BackgroundRecordCodec
{
    internal static string Header(object record)
    {
        var schema = BackgroundDataAccess.Schema(record) ?? throw new NotSupportedException("Unrecognized background data; source preserved: " + record.GetType().FullName);
        string args = BackgroundDataAccess.Arguments(record, schema);
        if (schema.Record == "BG_ElementGroup")
        {
            string[] values = args.Split(',').Select(s => s.Trim()).ToArray();
            if (values[3] == "0") args = values[1] == "0" && values[2] == "0" ? values[0] : string.Join(", ", values.Take(3));
        }
        return schema.Name + ": " + args + BackgroundDataAccess.Tags(record);
    }
    internal static string Text(object record, string prefix = "")
    {
        var visited = new HashSet<object>(); int budget = 4096;
        string Block(object value, string indent, int depth)
        {
            if (depth > 64 || --budget < 0 || !visited.Add(value)) throw new InvalidDataException("Invalid or oversized background group tree.");
            string result = indent + Header(value);
            if (BackgroundDataAccess.Get(value, "elements") is IEnumerable children)
                foreach (object child in children) result += "\n" + Block(child, indent + "- ", depth + 1);
            return result;
        }
        return Block(record, prefix, 0);
    }
    internal static IEnumerable<string> Delta(IEnumerable own, IEnumerable parent)
    {
        var inherited = parent == null ? new List<string>() : parent.Cast<object>().Select(r => Text(r)).ToList();
        var additions = new List<string>();
        if (own != null)
            foreach (object record in own)
            {
                string block = Text(record);
                if (!inherited.Remove(block)) additions.Add(block);
            }
        // Removals precede replacements, and a removed group has only a header.
        foreach (string block in inherited) yield return "REMOVE_" + block.Split('\n')[0];
        foreach (string block in additions) yield return block;
    }
    internal static void Apply(BackgroundDocument.Line line, BackgroundElementSchema schema, IList roots, Stack<object> groups)
    {
        while (groups.Count > line.Depth) groups.Pop();
        if (line.Depth != groups.Count || line.Depth > 64) throw new InvalidDataException("Background group indentation is invalid.");
        object parent = groups.Count > 0 ? groups.Peek() : null;
        var list = parent == null ? roots : (IList)BackgroundDataAccess.Get(parent, "elements");
        object record = BackgroundDataAccess.AllocateRecord(schema);
        string[] parts = line.Value.Split(new[] { " : " }, StringSplitOptions.None);
        BackgroundDataAccess.SetArguments(record, schema, parts[0]);
        foreach (string tag in parts.Skip(1)) ApplyTag(record, tag);
        if (line.Key.StartsWith("REMOVE_", StringComparison.Ordinal))
        {
            string expected = Header(record);
            foreach (object candidate in list.Cast<object>().ToArray())
                if (BackgroundDataAccess.Schema(candidate)?.Record == schema.Record && Header(candidate) == expected) list.Remove(candidate);
            return;
        }
        if (schema.Record == "BG_ElementGroup")
        {
            // A repeated named group in a child file reopens the inherited group.
            object existing = list.Cast<object>().FirstOrDefault(r => BackgroundDataAccess.Get(r, "elements") is IList && Equals(BackgroundDataAccess.Get(r, "name"), BackgroundDataAccess.Get(record, "name")));
            if (existing != null) { groups.Push(existing); return; }
        }
        BackgroundDataAccess.Set(record, "group", parent); list.Add(record);
        if (schema.Record == "BG_ElementGroup") groups.Push(record);
    }
    private static void ApplyTag(object record, string tag)
    {
        int separator = tag.IndexOf('|');
        if (separator < 0) { ((IList)BackgroundDataAccess.Get(record, "unrecognizedTags")).Add(tag); return; }
        string key = tag.Substring(0, separator), value = tag.Substring(separator + 1);
        string fieldName = key.ToLowerInvariant() switch { "anchor" => "anchorPos", "scale" => "spriteScale", "alpha" => "spriteAlpha", "shader" => "spriteShader", "color" => "spriteColor", "container" => "container", "spritename" => "spriteName", _ => null };
        if (key.Equals("lock", StringComparison.OrdinalIgnoreCase) && value is "X" or "Y") { BackgroundDataAccess.Set(record, value == "X" ? "lockX" : "lockY", true); return; }
        var field = fieldName == null ? null : BackgroundDataAccess.Field(record.GetType(), fieldName);
        if (field == null) { ((IList)BackgroundDataAccess.Get(record, "unrecognizedTags")).Add(tag); return; }
        if (fieldName == "spriteScale" && !value.Contains(",")) value += ", " + value;
        field.SetValue(record, BackgroundDataAccess.Parse(value, field.FieldType));
    }
}
