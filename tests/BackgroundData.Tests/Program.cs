using System.Globalization;
using System.Runtime.Serialization;
using DryCycle.DevUI.DevTool.Background;
using Mono.Cecil;
using RegionKit.Modules.BackgroundBuilder;
using UnityEngine;

static class Program
{
    static int count;
    static void Check(bool ok, string message) { count++; if (!ok) throw new Exception(message); }
    static void Reject(Action action, string message) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, message); }
    static T Blank<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    static IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
    static void Main(string[] args)
    {
        string source = "UNLISTED\r\nPROTECTED\r\n// comment\r\nType: AboveCloudsView\r\nParent: parent-file\r\nGROUP: A, 0, 0, 0\r\n- SimpleElement: sprite, 1, 2, 3 : future|token\r\n(X-Saint,Rivulet)futureValue: 17\r\nunknown-extension: preserved\r\n";
        var doc = BackgroundDocument.Parse(source);
        Check(doc.Serialize() == source, "Unchanged source must roundtrip byte-for-byte including CRLF, conditionals, comments, unknown fields and groups.");
        int line = doc.Lines.FindIndex(l => l.Key == "SimpleElement");
        doc.SetLine(line, "sprite, 4, 5, 6 : future|token");
        Check(doc.Serialize() == source.Replace("sprite, 1, 2, 3", "sprite, 4, 5, 6"), "Editing one row must preserve the other source lines.");
        doc.Set("Type", "RoofTopView");
        Check(doc.Get("Type") == "RoofTopView" && doc.Serialize().Contains("(X-Saint,Rivulet)futureValue: 17"), "Header edits must preserve conditional extensions.");
        var conditional = BackgroundDocument.Parse("(Saint)- SimpleElement: a, 0, 0, 1\n- (X-Saint)SimpleElement: b, 0, 0, 1");
        Check(conditional.Lines.All(l => l.Depth == 1 && l.Key == "SimpleElement") && BackgroundDocument.Applies(conditional.Lines[0].Prefix, "Saint") && !BackgroundDocument.Applies(conditional.Lines[1].Prefix, "Saint"), "Conditional prefixes must coexist with community group indentation.");
        foreach (string path in new[] { "../bad", "a/b", "a\\b", "CON", "con.txt", "COM1", "a\nb", "a.", " trailing", "x:y" }) Reject(() => BackgroundDocument.FileName(path), "Unsafe filename accepted: " + path);
        Check(BackgroundDocument.FileName("MyBackground.txt") == "MyBackground", "Normal background name must be usable.");
        foreach (string value in new[] { "NaN", "Infinity", "-Infinity", "1e20", "bad" }) Reject(() => BackgroundDocument.Number(value), "Invalid numeric value accepted.");
        var culture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = new CultureInfo("de-DE"); Check(BackgroundDocument.Number("1.25") == 1.25f && BackgroundDataAccess.Text(1.25f) == "1.25", "Background TXT numbers must be culture independent."); }
        finally { CultureInfo.CurrentCulture = culture; }
        var settings = new RoomSettings();
        var root = Blank<Data.RoomBGData>();
        root.sceneData = Blank<Data.BGSceneData>(); root.sceneData.backgroundElements = new();
        Data.Seed(settings, root);
        Check(ReferenceEquals(BackgroundDataAccess.Root(settings), root), "Read the existing weak-table data without invoking the API.");
        var parameters = BackgroundDataAccess.Parameters(root.sceneData);
        Check(parameters.Length == 1 && parameters[0].Name == "Altitude", "Read metadata without creating attribute instances.");
        root.sceneData.parent = Blank<Data.BGSceneData>(); root.sceneData.parent._height = 123;
        Check((float)BackgroundDataAccess.Effective(root.sceneData, parameters[0]) == 123, "Inherited values must read backing fields without invoking properties.");
        parameters[0].Field.SetValue(root.sceneData, 456f);
        Check((float)BackgroundDataAccess.Effective(root.sceneData, parameters[0]) == 456, "Own overrides must take priority.");
        var simpleSchema = BackgroundElementSchema.Find("SimpleElement", "AboveCloudsView")!;
        var element = (BackgroundElementData.BG_SimpleElement)BackgroundDataAccess.AllocateRecord(simpleSchema);
        Check(element.assetName == "Futile_White" && element.depth == 100 && element.unrecognizedTags != null, "Data-only allocation must initialize defaults without a mod constructor.");
        BackgroundDataAccess.SetArguments(element, simpleSchema, "sprite, 3.5, -4, 50");
        var readAsset = BackgroundDataAccess.StringReader(element, "assetName");
        element.assetName = "legacy-edited";
        Check(readAsset() == "legacy-edited", "Cached native renderer field access must follow original DevTool asset edits without an API call.");
        element.assetName = "sprite";
        Check(element.pos.x == 3.5f && element.pos.y == -4 && element.depth == 50, "Parse element arguments independently.");
        string before = BackgroundDataAccess.Arguments(element, simpleSchema);
        Reject(() => BackgroundDataAccess.SetArguments(element, simpleSchema, "other, 7, 8, -1"), "Reject negative depth.");
        Check(before == BackgroundDataAccess.Arguments(element, simpleSchema), "Failed element edits must be atomic.");
        var group = (BackgroundElementData.BG_ElementGroup)BackgroundDataAccess.AllocateRecord(BackgroundElementSchema.Find("GROUP", "AboveCloudsView")!);
        group.elements.Add(element); element.group = group; element.unrecognizedTags.Add("future|preserved"); root.sceneData.backgroundElements.Add(group);
        Check(BackgroundDataAccess.Elements(root.sceneData).Count() == 2, "Traverse groups without invoking enumerators on mod classes.");
        Reject(() => BackgroundDataAccess.Elements(new NullElementScene()).Count(), "Null background slots cannot bypass the existing traversal budget.");
        var clone = (Data.RoomBGData)BackgroundDataAccess.CloneDataGraph(root);
        var clonedGroup = (BackgroundElementData.BG_ElementGroup)clone.sceneData.backgroundElements[0];
        Check(!ReferenceEquals(clone, root) && !ReferenceEquals(clone.sceneData, root.sceneData) && ReferenceEquals(clonedGroup.elements[0].group, clonedGroup), "History clones must preserve grouping cycles without sharing mutable data.");
        clonedGroup.elements[0].pos = new(77, 88);
        Check(element.pos.x == 3.5f && BackgroundDataAccess.Tags(element).Contains("future|preserved"), "History edits and extension tags must not corrupt source state.");
        var native = new BackgroundScene.BackgroundSceneElement(); BackgroundDataAccess.BindRuntimeMetadata(native, element);
        CodecContracts();
        Check(Data.Calls == 0, "A RegionKit method, constructor, getter or attribute was invoked.");
        if (args.Length >= 2) BinaryContracts(args[0], args[1]);
        if (args.Length >= 3) { count += OriginalInputProbe.Run(args[0], args[2], args[1]); Console.WriteLine("PASS: compiled original-mode handlers forwarded to orig exactly once; installed protected asset data followed original edits."); }
        Console.WriteLine($"PASS: {count} background codec, data-only interop, history isolation and binary contracts.");
    }
    static void CodecContracts()
    {
        List<BackgroundElementData.CustomBgElement> Parse(string text, List<BackgroundElementData.CustomBgElement> records = null)
        {
            records ??= new(); var groups = new Stack<object>();
            foreach (var line in BackgroundDocument.Parse(text).Lines.Where(l => l.Key.Length > 0))
            {
                string key = line.Key.StartsWith("REMOVE_") ? line.Key.Substring(7) : line.Key;
                BackgroundRecordCodec.Apply(line, BackgroundElementSchema.Find(key, "AboveCloudsView"), records, groups);
            }
            return records;
        }
        var parent = Parse("GROUP: skyline\n- SimpleElement: a, 1, 2, 100 : future|yes\n- GROUP: nested, 3, 4\n- - SimpleElement: b, 5, 6, 200");
        string canonical = BackgroundRecordCodec.Text(parent[0]);
        var roundtrip = Parse(canonical);
        Check(canonical == BackgroundRecordCodec.Text(roundtrip[0]), "Nested community groups, optional coordinates, and extension tags must roundtrip.");
        Check(ReferenceEquals(((BackgroundElementData.BG_ElementGroup)((BackgroundElementData.BG_ElementGroup)roundtrip[0]).elements[1]).elements[0].group, ((BackgroundElementData.BG_ElementGroup)roundtrip[0]).elements[1]), "Imported nested groups must bind the actual data parent.");
        Parse("GROUP: skyline\n- REMOVE_SimpleElement: a, 1, 2, 100 : future|yes\n- SimpleElement: c, 0, 0, 300", roundtrip);
        Check(roundtrip.Count == 1 && ((BackgroundElementData.BG_ElementGroup)roundtrip[0]).elements.Count == 2, "Child presets must reopen a named inherited group and apply scoped, tagged removals.");
        string delta = string.Join("\n", BackgroundRecordCodec.Delta(roundtrip, parent));
        Check(delta.StartsWith("REMOVE_GROUP: skyline\nGROUP: skyline\n") && !delta.StartsWith("REMOVE_GROUP: skyline\n- "), "Group replacements must emit the community header-only tombstone before additions.");
        var inherited = Parse(canonical);
        Parse(delta, inherited);
        Check(inherited.Count == 1 && BackgroundRecordCodec.Text(inherited[0]) == BackgroundRecordCodec.Text(roundtrip[0]), "Applying a saved inheritance delta must produce the edited group without duplicating old children.");
        var tagged = Parse("SimpleElement: a, 0, 0, 100 : future|keep\nSimpleElement: a, 0, 0, 100 : future|remove");
        Parse("REMOVE_SimpleElement: a, 0, 0, 100 : future|remove", tagged);
        Check(tagged.Count == 1 && BackgroundDataAccess.Tags(tagged[0]).Contains("future|keep"), "A tombstone must not remove differently tagged sibling data.");
        Reject(() => Parse("- SimpleElement: a, 0, 0, 100"), "Reject orphan group indentation.");
        var loop = (BackgroundElementData.BG_ElementGroup)parent[0]; loop.elements.Add(loop);
        Reject(() => BackgroundRecordCodec.Text(loop), "Reject a cyclic group without recursing indefinitely.");
    }
    static void BinaryContracts(string editorPath, string kitPath)
    {
        using var editor = AssemblyDefinition.ReadAssembly(editorPath);
        using var kit = AssemblyDefinition.ReadAssembly(kitPath);
        var compiledTypes = editor.MainModule.Types.SelectMany(Types).ToArray();
        var previewTypes = compiledTypes.Where(t => t.Namespace == "DryCycle.DevUI.DevTool.Preview").ToArray();
        Check(previewTypes.Length == 1 && previewTypes[0].Name == "EffectPreviewIntentHub", "Retired preview hooks, probes, state machines and runtime drivers must not ship.");
        Check(!previewTypes[0].Fields.Any(), "The retired public preview API must have no retained state.");
        foreach (var method in previewTypes[0].Methods.Where(m => m.HasBody))
            Check(!method.Body.Instructions.Any(i => i.Operand is MethodReference || i.OpCode.Code is Mono.Cecil.Cil.Code.Stfld or Mono.Cecil.Cil.Code.Stsfld), "Retired preview API must neither call into the game nor mutate state: " + method.Name);
        foreach (var method in compiledTypes.Where(t => !previewTypes.Contains(t)).SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is MethodReference call && call.DeclaringType.Namespace == "DryCycle.DevUI.DevTool.Preview")
                    throw new Exception("Production path still drives retired Effect preview: " + method.FullName);
        Check(!editor.MainModule.AssemblyReferences.Any(a => a.Name == "RegionKit"), "RegionKit must remain an optional data provider, without a binary dependency.");
        foreach (var method in editor.MainModule.Types.SelectMany(Types).Where(t => t.Namespace.Contains("DevTool.Background")).SelectMany(t => t.Methods).Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
                if (instruction.Operand is MethodReference called) Check(!called.DeclaringType.FullName.StartsWith("RegionKit."), "Direct mod method call: " + called.FullName);
        var records = kit.MainModule.Types.SelectMany(Types).ToDictionary(t => t.FullName);
        FieldDefinition Field(TypeDefinition type, string name)
        {
            for (TypeDefinition current = type; current != null;)
            {
                var field = current.Fields.FirstOrDefault(f => f.Name == name); if (field != null) return field;
                current = current.BaseType != null && records.TryGetValue(current.BaseType.FullName, out var parent) ? parent : null;
            }
            return null;
        }
        foreach (var schema in BackgroundElementSchema.All)
        {
            Check(records.TryGetValue("RegionKit.Modules.BackgroundBuilder.BackgroundElementData/" + schema.Record, out var type), "Missing installed data record: " + schema.Record);
            foreach (string name in schema.Fields.Select(f => f.Split('.')[0]).Distinct()) Check(Field(type!, name) != null, "Installed field mismatch: " + schema.Record + "." + name);
        }
        var dataType = records["RegionKit.Modules.BackgroundBuilder.Data"];
        Check(dataType.Fields.Any(f => f.Name == "table" && f.FieldType.FullName.Contains("ConditionalWeakTable")), "Installed weak-table contract changed.");
    }
    private sealed class NullElementScene
    {
        public IEnumerable<object> backgroundElements = Enumerable.Repeat<object>(null, 4097);
    }
}
