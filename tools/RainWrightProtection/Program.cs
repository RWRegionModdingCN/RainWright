using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class Program
{
    internal static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    internal sealed record Entry(string Kind, string Token, string Owner, string Original, string Obfuscated, string Reason = "");
    internal sealed record SourceMap(string Original, string Obfuscated);
    internal sealed record PlanData(string Input, string Work, string Game, string[] Consumers, string[] References,
        Dictionary<int, string> ProtectedTypes, Dictionary<int, string> ProtectedMethods, string Seed);
    internal sealed record MethodLocation(string Token, string Source, int[] Lines);
    internal sealed record SymbolData(string AssemblySha256, Guid Mvid, List<Entry> Names, List<SourceMap> Sources, List<MethodLocation>? Locations = null);
    private static string Token(IMetadataTokenProvider item) => "0x" + item.MetadataToken.ToInt32().ToString("X8");
    private static string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
    private static string Hash(EmbeddedResource resource) => Convert.ToHexString(SHA256.HashData(resource.GetResourceData()));
    private static IEnumerable<TypeDefinition> Types(ModuleDefinition module) => module.Types.SelectMany(Types);
    private static IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));
    private static bool Exported(TypeDefinition t) => t.IsPublic || (t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly) && Exported(t.DeclaringType);
    private static bool Public(MethodDefinition m) => Exported(m.DeclaringType) && (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly);
    private static bool Public(FieldDefinition f) => Exported(f.DeclaringType) && (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly);
    private static string[] Lines(string text) => text.Replace("\r\n", "\n").Split('\n');
    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
    private static PlanData ReadPlan(string work) => JsonSerializer.Deserialize<PlanData>(File.ReadAllText(Path.Combine(work, "plan.json")))!;

    private static DefaultAssemblyResolver Resolver(IEnumerable<string> references)
    {
        var resolver = new DefaultAssemblyResolver();
        foreach (string path in references.Distinct(StringComparer.OrdinalIgnoreCase))
            if (Directory.Exists(path)) resolver.AddSearchDirectory(path);
        return resolver;
    }
    private static AssemblyDefinition Read(string path, DefaultAssemblyResolver resolver, bool symbols = true) =>
        AssemblyDefinition.ReadAssembly(path, new ReaderParameters
        {
            AssemblyResolver = resolver, InMemory = true, ReadSymbols = symbols,
            SymbolReaderProvider = symbols ? new PortablePdbReaderProvider() : null,
            ReadingMode = ReadingMode.Immediate
        });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }

    public static int Main(string[] args)
    {
        try
        {
            switch (args[0])
            {
                case "plan": Plan(args[1], args[2], args[3], args[4], args.Skip(5).ToArray()); break;
                case "finish": Finish(args[1], args[2]); break;
                case "smoke": Smoke(args[1]); break;
                case "decode": Decode(args[1], args[2], args[3]); break;
                default: throw new ArgumentException("Expected plan, finish, smoke or decode.");
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Plan(string input, string work, string game, string seed, string[] consumers)
    {
        Require(seed.Length == 64 && seed.All(Uri.IsHexDigit), "Seed must be exactly 64 hexadecimal characters.");
        input = Path.GetFullPath(input); work = Path.GetFullPath(work); game = Path.GetFullPath(game);
        string stage = Path.Combine(work, "input"), raw = Path.Combine(work, "raw"), privateDir = Path.Combine(work, "private");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(raw); Directory.CreateDirectory(privateDir);
        string[] references = new[] { stage, Path.Combine(game, "RainWorld_Data", "Managed"), Path.Combine(game, "BepInEx", "core"),
            Path.Combine(game, "BepInEx", "plugins"), Path.Combine(game, "BepInEx", "utils"),
            Path.Combine(game, "..", "..", "workshop", "content", "312520", "3417372413", "plugins"),
            Path.Combine(game, "RainWorld_Data", "StreamingAssets", "mods", "rwimgui", "plugins"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319") }
            .Concat(consumers.Select(p => Path.GetDirectoryName(Path.GetFullPath(p))!)).Select(Path.GetFullPath).Distinct().Where(Directory.Exists).ToArray();
        // The publicized game's assembly retains Assembly-CSharp identity under a different filename.
        string publicGame = Path.Combine(game, "BepInEx", "utils", "PUBLIC-Assembly-CSharp.dll");
        if (File.Exists(publicGame)) File.Copy(publicGame, Path.Combine(stage, "Assembly-CSharp.dll"), true);
        File.Copy(input, Path.Combine(stage, "RainWright.dll"), true);
        File.Copy(Path.ChangeExtension(input, ".pdb"), Path.Combine(stage, "RainWright.pdb"), true);
        using var resolver = Resolver(references);
        // Cecil writes nested types in traversal order. Normalize the isolated baseline once so
        // that its tokens and the obfuscator's output use the same ordering. Never touch input.
        string stagedInput = Path.Combine(stage, "RainWright.dll");
        using (var baseline = Read(input, resolver))
            baseline.Write(stagedInput, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider() });
        using var asm = Read(stagedInput, resolver);
        Require(asm.Name.Name == "RainWright", "Only RainWright is an allowed input.");
        Require(Types(asm.MainModule).Sum(t => t.Methods.Sum(m => m.DebugInformation.SequencePoints.Count)) > 0,
            "Input PDB is missing or contains no sequence points.");
        var strings = new HashSet<string>(StringComparer.Ordinal);
        var externalTypes = new HashSet<string>(StringComparer.Ordinal);
        var externalMembers = new HashSet<string>(StringComparer.Ordinal);
        void Inspect(AssemblyDefinition assembly, bool consumer)
        {
            foreach (var type in Types(assembly.MainModule))
                foreach (var method in type.Methods.Where(m => m.HasBody))
                    foreach (var instruction in method.Body.Instructions)
                        if (instruction.OpCode.Code == Code.Ldstr && instruction.Operand is string text) strings.Add(text);
            if (!consumer) return;
            foreach (var type in assembly.MainModule.GetTypeReferences())
                if (type.GetElementType().Scope is AssemblyNameReference scope && scope.Name == "RainWright") externalTypes.Add(type.GetElementType().FullName);
            foreach (var member in assembly.MainModule.GetMemberReferences())
                if (member.DeclaringType.GetElementType().Scope is AssemblyNameReference scope && scope.Name == "RainWright")
                { externalTypes.Add(member.DeclaringType.GetElementType().FullName); externalMembers.Add(member.DeclaringType.GetElementType().FullName + "::" + member.Name); }
        }
        Inspect(asm, false);
        foreach (string file in consumers)
        {
            using var dependency = Read(file, resolver, false);
            Require(dependency.Name.Name != "RainWright", "A consumer cannot be the input assembly.");
            Inspect(dependency, true);
        }
        var protectedTypes = new Dictionary<int, string>();
        var protectedMethods = new Dictionary<int, string>();
        var exclusions = new List<object>();
        var module = new XElement("Module", new XAttribute("file", Path.Combine(stage, "RainWright.dll")));
        var callbacks = new HashSet<string>(new[] { "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "OnGUI",
            "OnApplicationQuit", "OnApplicationFocus", "OnApplicationPause", "Reset", "OnValidate", "OnBeforeSerialize", "OnAfterDeserialize",
            "Prefix", "Postfix", "Transpiler", "Finalizer", "TargetMethod", "TargetMethods", "Prepare", "Cleanup" }, StringComparer.Ordinal);
        bool Serialized(TypeDefinition type) => type.IsSerializable || type.IsEnum || type.CustomAttributes.Any(a =>
            a.AttributeType.FullName.Contains("DataContract") || a.AttributeType.FullName.Contains("JsonObject") ||
            a.AttributeType.FullName.Contains("XmlRoot") || a.AttributeType.FullName.Contains("XmlType"));
        bool LiteralType(TypeDefinition type) => strings.Contains(type.FullName) || strings.Contains(type.FullName.Replace('/', '+')) ||
            strings.Any(s => s.StartsWith(type.FullName.Replace('/', '+') + ",", StringComparison.Ordinal));
        var recordContracts = new HashSet<string>(new[] { "CartographyAppearance", "CartographyOptions", "CartographyPalette", "CartographyPoint" }
            .Select(n => "DryCycle.DevUI.DevTool.Map.Cartography." + n), StringComparer.Ordinal);
        foreach (var type in Types(asm.MainModule).Where(t => t.Name != "<Module>"))
        {
            bool serial = Serialized(type) || type.DeclaringType != null && Serialized(type.DeclaringType);
            string? reason = serial ? "serialization/enum contract" : Exported(type) ? "public API" : externalTypes.Contains(type.FullName) ? "referenced by unchanged consumer" :
                LiteralType(type) || strings.Contains(type.Name) ? "name used as string (reflection compatibility)" : null;
            if (reason != null)
            {
                protectedTypes[type.MetadataToken.ToInt32()] = reason;
                var skip = new XElement("SkipType", new XAttribute("name", type.FullName));
                if (serial) foreach (string attribute in new[] { "skipMethods", "skipFields", "skipProperties", "skipEvents" }) skip.Add(new XAttribute(attribute, "true"));
                module.Add(skip); exclusions.Add(new { kind = "type", name = type.FullName, reason });
            }
            foreach (var method in type.Methods)
            {
                string? why = serial ? "serialization/enum contract" : Public(method) ? "public API" : callbacks.Contains(method.Name) ? "Unity/plugin callback convention" :
                    externalMembers.Contains(type.FullName + "::" + method.Name) ? "referenced by unchanged consumer" :
                    strings.Contains(method.Name) ? "name used as string (reflection compatibility)" :
                    method.CustomAttributes.Any(a => a.AttributeType.FullName.Contains("Harmony") || a.AttributeType.FullName.Contains("MonoPInvokeCallback")) ? "runtime callback attribute" : null;
                if (why == null) continue;
                protectedMethods[method.MetadataToken.ToInt32()] = why;
                module.Add(new XElement("SkipMethod", new XAttribute("type", type.FullName), new XAttribute("name", method.Name)));
                exclusions.Add(new { kind = "method", name = method.FullName, reason = why });
            }
            foreach (var field in type.Fields)
                if (serial || recordContracts.Contains(type.FullName) || Public(field) || strings.Contains(field.Name) || externalMembers.Contains(type.FullName + "::" + field.Name) ||
                    field.CustomAttributes.Any(a => a.AttributeType.Name is "SerializeField" or "SerializeReference"))
                { module.Add(new XElement("SkipField", new XAttribute("type", type.FullName), new XAttribute("name", field.Name)));
                  exclusions.Add(new { kind = "field", name = field.FullName, reason = "API, serialization, consumer or literal-name contract" }); }
            foreach (var property in type.Properties)
                if (serial || strings.Contains(property.Name) || property.CustomAttributes.Count > 0 ||
                    new[] { property.GetMethod, property.SetMethod }.Any(m => m != null && protectedMethods.ContainsKey(m.MetadataToken.ToInt32())) ||
                    (property.GetMethod != null && Public(property.GetMethod)) || (property.SetMethod != null && Public(property.SetMethod)))
                { module.Add(new XElement("SkipProperty", new XAttribute("type", type.FullName), new XAttribute("name", property.Name)));
                  foreach (var accessor in new[] { property.GetMethod, property.SetMethod }.Where(m => m != null))
                  {
                      protectedMethods[accessor!.MetadataToken.ToInt32()] = "property contract";
                      module.Add(new XElement("SkipMethod", new XAttribute("type", type.FullName), new XAttribute("name", accessor.Name)));
                  }
                  exclusions.Add(new { kind = "property", name = property.FullName, reason = "API, serialization or literal-name contract" }); }
        }
        // A retained nested type's full name includes every declaring type.
        foreach (var type in Types(asm.MainModule).Where(t => protectedTypes.ContainsKey(t.MetadataToken.ToInt32())).ToArray())
            for (var parent = type.DeclaringType; parent != null; parent = parent.DeclaringType)
                if (protectedTypes.TryAdd(parent.MetadataToken.ToInt32(), "declaring type of retained nested contract"))
                {
                    module.Add(new XElement("SkipType", new XAttribute("name", parent.FullName)));
                    exclusions.Add(new { kind = "type", name = parent.FullName, reason = "declaring type of retained nested contract" });
                }
        var xml = new XElement("Obfuscator");
        var variables = new Dictionary<string, string> { ["InPath"] = stage, ["OutPath"] = raw, ["CustomChars"] = CustomProtectionPass.Alphabet(seed),
            ["LogFile"] = Path.Combine(privateDir, "obfuscar-map.xml"), ["XmlMapping"] = "true", ["KeepPublicApi"] = "true", ["HidePrivateApi"] = "true",
            ["RenameFields"] = "true", ["RenameProperties"] = "true", ["KeepProperties"] = "true", ["RenameEvents"] = "false",
            ["ReuseNames"] = "false", ["HideStrings"] = "false", ["OptimizeMethods"] = "false", ["SuppressIldasm"] = "false",
            ["RegenerateDebugInfo"] = "true", ["SkipGenerated"] = "true", ["AnalyzeXaml"] = "false" };
        foreach (var item in variables) xml.Add(new XElement("Var", new XAttribute("name", item.Key), new XAttribute("value", item.Value)));
        foreach (string path in references) xml.Add(new XElement("AssemblySearchPath", new XAttribute("path", path)));
        xml.Add(module); new XDocument(xml).Save(Path.Combine(work, "obfuscar.xml"));
        WriteJson(Path.Combine(work, "plan.json"), new PlanData(stagedInput, work, game, consumers, references, protectedTypes, protectedMethods, seed));
        WriteJson(Path.Combine(privateDir, "compatibility-exclusions.json"), exclusions);
        Console.WriteLine($"PLAN: {Types(asm.MainModule).Count()} types; {protectedTypes.Count} type-name contracts and {protectedMethods.Count} method contracts retained.");
    }

    private static IEnumerable<ScopeDebugInformation> Scopes(ScopeDebugInformation? scope)
    {
        if (scope == null) yield break;
        yield return scope;
        foreach (var child in scope.Scopes.SelectMany(Scopes)) yield return child;
    }

    private static void Finish(string work, string output)
    {
        var plan = ReadPlan(work); output = Path.GetFullPath(output);
        string releaseDir = Path.Combine(output, "candidate"), privateDir = Path.Combine(output, "debug-private");
        Directory.CreateDirectory(releaseDir); Directory.CreateDirectory(privateDir);
        using var resolver = Resolver(plan.References);
        using var before = Read(plan.Input, resolver);
        using var after = Read(Path.Combine(work, "raw", "RainWright.dll"), resolver);
        var names = new List<Entry>();
        var sources = new Dictionary<string, string>(StringComparer.Ordinal);
        var locations = new List<MethodLocation>();
        var beforeTypes = Types(before.MainModule).ToDictionary(t => t.MetadataToken.ToInt32());
        var afterTypes = Types(after.MainModule).ToDictionary(t => t.MetadataToken.ToInt32());
        Require(beforeTypes.Count == afterTypes.Count, "Type count changed; refuse a token-based map.");
        int localId = 0, sequencePoints = 0, parameterChanges = 0;
        foreach (var item in beforeTypes)
        {
            TypeDefinition oldType = item.Value, type = afterTypes[item.Key];
            Require(type.Fields.Count == oldType.Fields.Count && type.Methods.Count == oldType.Methods.Count && type.Properties.Count == oldType.Properties.Count,
                "Metadata rows changed; refuse a token-based map for " + oldType.FullName);
            plan.ProtectedTypes.TryGetValue(item.Key, out string? typeReason);
            if (typeReason != null) Require(type.FullName == oldType.FullName, "A retained type contract was renamed: " + oldType.FullName);
            names.Add(new("type", Token(oldType), "", oldType.FullName, type.FullName, typeReason ?? ""));
            foreach (var oldField in oldType.Fields)
            {
                var field = type.Fields.Single(f => f.MetadataToken == oldField.MetadataToken);
                names.Add(new("field", Token(oldField), oldType.FullName, oldField.FullName, field.FullName));
            }
            foreach (var oldProperty in oldType.Properties)
            {
                var property = type.Properties.Single(p => p.MetadataToken == oldProperty.MetadataToken);
                names.Add(new("property", Token(oldProperty), oldType.FullName, oldProperty.FullName, property.FullName));
            }
            foreach (var oldMethod in oldType.Methods)
            {
                var method = type.Methods.Single(m => m.MetadataToken == oldMethod.MetadataToken);
                Require(oldMethod.Parameters.Count == method.Parameters.Count && oldMethod.IsStatic == method.IsStatic && oldMethod.GenericParameters.Count == method.GenericParameters.Count,
                    "Method identity changed for " + oldMethod.FullName);
                plan.ProtectedMethods.TryGetValue(oldMethod.MetadataToken.ToInt32(), out string? reason);
                if (reason != null) Require(method.Name == oldMethod.Name, "A retained method contract was renamed: " + oldMethod.FullName);
                for (int i = 0; i < oldMethod.Parameters.Count; i++)
                {
                    string original = oldMethod.Parameters[i].Name;
                    method.Parameters[i].Name = reason != null || oldMethod.Parameters[i].HasCustomAttributes ? original : "p" + i;
                    if (method.Parameters[i].Name != original) parameterChanges++;
                    names.Add(new("parameter", Token(method) + ":arg" + i, oldMethod.FullName, original, method.Parameters[i].Name, reason ?? ""));
                }
                names.Add(new("method", Token(oldMethod), oldType.FullName, oldMethod.FullName, method.FullName, reason ?? ""));
                var oldPoints = oldMethod.DebugInformation.SequencePoints;
                var newPoints = method.DebugInformation.SequencePoints;
                Require(oldPoints.Count == newPoints.Count, "Sequence points were lost in " + oldMethod.FullName);
                for (int i = 0; i < oldPoints.Count; i++)
                {
                    var a = oldPoints[i]; var b = newPoints[i];
                    Require(a.Offset == b.Offset && a.StartLine == b.StartLine && a.EndLine == b.EndLine && a.StartColumn == b.StartColumn && a.EndColumn == b.EndColumn,
                        "An IL offset or source line/column changed in " + oldMethod.FullName);
                    string original = a.Document.Url;
                    if (!sources.TryGetValue(original, out string? alias)) sources.Add(original, alias = "src/s" + sources.Count.ToString("D5") + ".cs");
                    b.Document.Url = alias;
                    b.Document.CustomDebugInformations.Clear();
                    sequencePoints++;
                }
                foreach (var source in oldPoints.Where(p => !p.IsHidden).GroupBy(p => p.Document.Url))
                    locations.Add(new MethodLocation(Token(method), sources[source.Key], source.Select(p => p.StartLine).Distinct().Order().ToArray()));
                var oldScopes = Scopes(oldMethod.DebugInformation.Scope).ToArray();
                var scopes = Scopes(method.DebugInformation.Scope).ToArray();
                Require(oldScopes.Length == scopes.Length, "Debug scopes changed in " + oldMethod.FullName);
                for (int i = 0; i < scopes.Length; i++)
                {
                    Require(oldScopes[i].Variables.Count == scopes[i].Variables.Count && oldScopes[i].Constants.Count == scopes[i].Constants.Count, "PDB local rows changed.");
                    for (int j = 0; j < scopes[i].Variables.Count; j++)
                    {
                        var variable = scopes[i].Variables[j]; string original = oldScopes[i].Variables[j].Name;
                        variable.Name = "v" + localId++;
                        names.Add(new("local", Token(method) + ":scope" + i + ":local" + variable.Index, oldMethod.FullName, original, variable.Name));
                    }
                    for (int j = 0; j < scopes[i].Constants.Count; j++)
                    {
                        string original = oldScopes[i].Constants[j].Name;
                        scopes[i].Constants[j].Name = "c" + localId++;
                        names.Add(new("local-constant", Token(method) + ":scope" + i + ":const" + j, oldMethod.FullName, original, scopes[i].Constants[j].Name));
                    }
                }
            }
        }
        // SourceLink/embedded source can reveal paths or the source text; remove them, keeping all
        // sequence points, scopes, async stepping records and document checksums.
        foreach (var info in after.MainModule.CustomDebugInformations.ToArray())
            if (info.Kind is CustomDebugInformationKind.SourceLink or CustomDebugInformationKind.EmbeddedSource) after.MainModule.CustomDebugInformations.Remove(info);
        // First validate the mature rename pass under the original strict IL/offset contract.
        // Only then run our small, explicit, deterministic transform policy.
        string privateDll = Path.Combine(privateDir, "RainWright.dll");
        after.Write(privateDll, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider() });
        Verify(before, plan, privateDir, names, sources, resolver);
        var custom = CustomProtectionPass.Apply(before, after, plan.Seed, plan.ProtectedMethods.Keys.ToHashSet());
        after.Write(privateDll, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider() });
        CustomProtectionPass.Complete(custom);
        string expectedFingerprint = CustomProtectionPass.Fingerprint(after);
        using (var reread = Read(privateDll, resolver))
        {
            Require(CustomProtectionPass.Fingerprint(reread) == expectedFingerprint, "Re-read candidate differs from the expected protected program.");
            Require(before.MainModule.AssemblyReferences.Select(r => r.FullName).SequenceEqual(reread.MainModule.AssemblyReferences.Select(r => r.FullName)), "Custom pass introduced a runtime dependency.");
            var offsets = custom.Changes.ToDictionary(c => c.Token, c => c.OriginalToFinalOffsets);
            foreach (var originalMethod in Types(before.MainModule).SelectMany(t => t.Methods))
            {
                var finalMethod = (MethodDefinition)reread.MainModule.LookupToken(originalMethod.MetadataToken);
                var points = finalMethod.DebugInformation.SequencePoints;
                Require(points.Count == originalMethod.DebugInformation.SequencePoints.Count, "Custom pass lost source sequence points.");
                foreach (var point in originalMethod.DebugInformation.SequencePoints)
                {
                    int offset = offsets.TryGetValue(originalMethod.MetadataToken.ToInt32(), out var relocation) ? relocation[point.Offset] : point.Offset;
                    Require(points.Any(p => p.Offset == offset && p.StartLine == point.StartLine && p.EndLine == point.EndLine && p.StartColumn == point.StartColumn && p.EndColumn == point.EndColumn && p.Document.Url == sources[point.Document.Url]),
                        "Custom pass changed a source line/column or its instruction mapping: " + originalMethod.FullName);
                }
            }
        }
        WriteJson(Path.Combine(privateDir, "custom-protection.json"), custom);
        WriteJson(Path.Combine(privateDir, "build-recipe.json"), new { version = 1, seed = plan.Seed, seedId = custom.SeedId, inputSha256 = Hash(plan.Input), expectedFingerprint });
        foreach (string file in Directory.GetFiles(Path.Combine(work, "private"))) File.Copy(file, Path.Combine(privateDir, Path.GetFileName(file)), true);
        string finalDll = Path.Combine(releaseDir, "RainWright.dll");
        File.Copy(privateDll, finalDll, true);
        var map = new SymbolData(Hash(finalDll), after.MainModule.Mvid, names, sources.Select(s => new SourceMap(s.Key, s.Value)).ToList(), locations);
        WriteJson(Path.Combine(privateDir, "symbol-map.json"), map);
        static string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        File.WriteAllLines(Path.Combine(privateDir, "name-map.csv"), new[] { "Kind,Token,Owner,Original,Obfuscated,Reason" }.Concat(
            names.Select(e => string.Join(",", new[] { e.Kind, e.Token, e.Owner, e.Original, e.Obfuscated, e.Reason }.Select(Cell)))), new UTF8Encoding(true));
        WriteProbePlan(before, privateDir);
        static string DirectName(string full)
        {
            int separator = full.IndexOf("::", StringComparison.Ordinal);
            if (separator < 0) return full;
            string member = full[(separator + 2)..]; int paren = member.IndexOf('(');
            return paren < 0 ? member : member[..paren];
        }
        var counts = names.GroupBy(n => n.Kind).ToDictionary(g => g.Key, g => new { total = g.Count(),
            renamed = g.Count(n => DirectName(n.Original) != DirectName(n.Obfuscated)),
            qualifiedIdentityChanged = g.Count(n => n.Original != n.Obfuscated) });
        WriteJson(Path.Combine(privateDir, "validation.json"), new { success = true, dllSha256 = map.AssemblySha256, mvid = map.Mvid,
            counts, sourceDocuments = sources.Count, sequencePointsPreserved = sequencePoints, parameterChanges,
            runtimeGameTested = false, readyForPublication = false, custom.SeedId, customMethods = custom.Changes.Count, protectedStrings = custom.Changes.Sum(c => c.Strings), reorderedBlocks = custom.Changes.Sum(c => c.Blocks),
            policy = "Retain compatibility contracts. Seeded renaming; opt-in cold-method string encoding and block permutation; remap all original source points; mandatory execution gates." });
        Console.WriteLine(JsonSerializer.Serialize(new { success = true, counts, documents = sources.Count, sequencePoints }, Json));
    }

    private static void Verify(AssemblyDefinition before, PlanData plan, string debugDir, List<Entry> names,
        Dictionary<string, string> sources, DefaultAssemblyResolver resolver)
    {
        using var finalResolver = Resolver(new[] { debugDir }.Concat(plan.References));
        using var final = Read(Path.Combine(debugDir, "RainWright.dll"), finalResolver);
        Require(final.Name.FullName == before.Name.FullName, "Assembly identity changed.");
        Require(final.MainModule.Mvid == before.MainModule.Mvid, "Assembly MVID changed unexpectedly.");
        var aTypes = Types(before.MainModule).ToDictionary(t => t.MetadataToken.ToInt32());
        var bTypes = Types(final.MainModule).ToDictionary(t => t.MetadataToken.ToInt32());
        var typeNames = names.Where(n => n.Kind == "type").ToDictionary(n => n.Obfuscated, n => n.Original, StringComparer.Ordinal);
        string Signature(TypeReference reference, bool changed)
        {
            if (reference is GenericParameter parameter) return (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position;
            if (reference is GenericInstanceType generic) return Signature(generic.ElementType, changed) + "<" + string.Join(",", generic.GenericArguments.Select(a => Signature(a, changed))) + ">";
            if (reference is ArrayType array) return Signature(array.ElementType, changed) + "[" + new string(',', array.Rank - 1) + "]";
            if (reference is ByReferenceType byref) return Signature(byref.ElementType, changed) + "&";
            if (reference is PointerType pointer) return Signature(pointer.ElementType, changed) + "*";
            if (reference is RequiredModifierType required) return Signature(required.ElementType, changed) + " modreq(" + Signature(required.ModifierType, changed) + ")";
            if (reference is OptionalModifierType optional) return Signature(optional.ElementType, changed) + " modopt(" + Signature(optional.ModifierType, changed) + ")";
            if (reference is FunctionPointerType function) return "fn " + function.CallingConvention + " " + Signature(function.ReturnType, changed) + "(" + string.Join(",", function.Parameters.Select(p => Signature(p.ParameterType, changed))) + ")";
            return changed && typeNames.TryGetValue(reference.FullName, out string? original) ? original : reference.FullName;
        }
        var originalMemberNames = names.Where(n => n.Kind is "field" or "method").ToDictionary(n => Convert.ToInt32(n.Token[2..], 16), n =>
        {
            string member = n.Original[(n.Original.IndexOf("::", StringComparison.Ordinal) + 2)..];
            int paren = member.IndexOf('('); return paren < 0 ? member : member[..paren];
        });
        bool Own(TypeReference type) => type.GetElementType().Scope is ModuleDefinition module && module.Assembly.Name.Name == "RainWright" ||
            type.GetElementType().Scope is AssemblyNameReference assembly && assembly.Name == "RainWright";
        string MethodId(MethodReference method, bool changed)
        {
            if (method is GenericInstanceMethod generic)
                return MethodId(generic.ElementMethod, changed) + "<" + string.Join(",", generic.GenericArguments.Select(t => Signature(t, changed))) + ">";
            string name = method.Name;
            // Multidimensional array constructors/Get/Set are runtime intrinsics, with no
            // MethodDef row to resolve even when their element type belongs to this DLL.
            if (changed && Own(method.DeclaringType) && method.DeclaringType is not ArrayType)
            {
                var resolved = method.Resolve();
                Require(resolved != null, "Unresolved own method: " + method.FullName);
                name = originalMemberNames[resolved!.MetadataToken.ToInt32()];
            }
            return Signature(method.DeclaringType, changed) + "::" + name + "`" + method.GenericParameters.Count + " " +
                method.CallingConvention + " " + method.HasThis + " " + Signature(method.ReturnType, changed) + "(" +
                string.Join(",", method.Parameters.Select(p => Signature(p.ParameterType, changed))) + ")";
        }
        string FieldId(FieldReference field, bool changed)
        {
            string name = field.Name;
            if (changed && Own(field.DeclaringType))
            {
                var resolved = field.Resolve();
                Require(resolved != null, "Unresolved own field: " + field.FullName);
                name = originalMemberNames[resolved!.MetadataToken.ToInt32()];
            }
            return Signature(field.DeclaringType, changed) + "::" + name + " " + Signature(field.FieldType, changed);
        }
        object? Operand(object? operand, bool changed) => operand switch
        {
            MethodReference method => MethodId(method, changed), FieldReference field => FieldId(field, changed),
            TypeReference type => Signature(type, changed), Instruction branch => "IL:" + branch.Offset,
            Instruction[] branches => "switch:" + string.Join(",", branches.Select(i => i.Offset)),
            ParameterDefinition parameter => "arg:" + parameter.Index, VariableDefinition local => "local:" + local.Index,
            CallSite site => site.CallingConvention + " " + site.HasThis + " " + Signature(site.ReturnType, changed) + "(" +
                string.Join(",", site.Parameters.Select(p => Signature(p.ParameterType, changed))) + ")",
            _ => operand
        };
        string Handler(ExceptionHandler handler, bool changed) => handler.HandlerType + ":" + handler.TryStart?.Offset + ":" + handler.TryEnd?.Offset + ":" +
            handler.HandlerStart?.Offset + ":" + handler.HandlerEnd?.Offset + ":" + handler.FilterStart?.Offset + ":" +
            (handler.CatchType == null ? "" : Signature(handler.CatchType, changed));
        // References, including embedded resources and untouched consumer contracts, must retain
        // their actual identity; naming coverage is measured independently from this verification.
        Require(before.MainModule.AssemblyReferences.Select(r => r.FullName).SequenceEqual(final.MainModule.AssemblyReferences.Select(r => r.FullName)), "Assembly dependency identities changed.");
        foreach (var resource in before.MainModule.Resources.OfType<EmbeddedResource>())
            Require(Hash(resource) == Hash(final.MainModule.Resources.OfType<EmbeddedResource>().Single(r => r.Name == resource.Name)), "Embedded resource changed: " + resource.Name);
        foreach (var item in aTypes)
        {
            var old = item.Value; var type = bTypes[item.Key];
            Require(typeNames[type.FullName] == old.FullName, "Type-token correspondence failed.");
            Require(old.BaseType == null ? type.BaseType == null : type.BaseType != null && Signature(old.BaseType, false) == Signature(type.BaseType, true), "Base type changed: " + old.FullName);
            Require(old.Interfaces.Select(i => Signature(i.InterfaceType, false)).SequenceEqual(type.Interfaces.Select(i => Signature(i.InterfaceType, true))), "Interface identity changed: " + old.FullName);
            foreach (var field in old.Fields)
                Require(Signature(field.FieldType, false) == Signature(type.Fields.Single(f => f.MetadataToken == field.MetadataToken).FieldType, true), "Field signature changed: " + field.FullName);
            foreach (var method in old.Methods)
            {
                var current = type.Methods.Single(m => m.MetadataToken == method.MetadataToken);
                Require(Signature(current.ReturnType, true) == Signature(method.ReturnType, false) &&
                    current.Parameters.Select(p => Signature(p.ParameterType, true)).SequenceEqual(method.Parameters.Select(p => Signature(p.ParameterType, false))), "Method signature changed: " + method.FullName);
                if (method.HasBody)
                {
                    Require(current.HasBody && current.Body.Instructions.Count == method.Body.Instructions.Count, "Method IL count changed: " + method.FullName);
                    Require(method.Body.Variables.Select(v => Signature(v.VariableType, false)).SequenceEqual(current.Body.Variables.Select(v => Signature(v.VariableType, true))), "Local variable types changed: " + method.FullName);
                    Require(method.Body.ExceptionHandlers.Select(h => Handler(h, false)).SequenceEqual(current.Body.ExceptionHandlers.Select(h => Handler(h, true))), "Exception handlers changed: " + method.FullName);
                    for (int i = 0; i < method.Body.Instructions.Count; i++)
                    {
                        Require(method.Body.Instructions[i].OpCode == current.Body.Instructions[i].OpCode && method.Body.Instructions[i].Offset == current.Body.Instructions[i].Offset,
                            "Method IL opcode/offset changed: " + method.FullName);
                        Require(Equals(Operand(method.Body.Instructions[i].Operand, false), Operand(current.Body.Instructions[i].Operand, true)),
                            "Method IL target/value changed: " + method.FullName + " at " + method.Body.Instructions[i].Offset);
                    }
                }
                var points = current.DebugInformation.SequencePoints;
                Require(points.Count == method.DebugInformation.SequencePoints.Count, "Final PDB lost source lines.");
                for (int i = 0; i < points.Count; i++)
                    Require(points[i].StartLine == method.DebugInformation.SequencePoints[i].StartLine &&
                        points[i].EndLine == method.DebugInformation.SequencePoints[i].EndLine &&
                        points[i].Document.Url == sources[method.DebugInformation.SequencePoints[i].Document.Url], "Final PDB line or source alias mismatch.");
                foreach (var local in Scopes(current.DebugInformation.Scope).SelectMany(s => s.Variables))
                    Require(local.Name.StartsWith('v') && local.Name.Skip(1).All(char.IsDigit), "A PDB local name was retained unexpectedly.");
            }
        }
        foreach (string path in plan.Consumers)
        {
            using var consumer = Read(path, finalResolver, false);
            foreach (var reference in consumer.MainModule.GetTypeReferences().Where(t => t.Scope is AssemblyNameReference s && s.Name == "RainWright"))
                Require(Types(final.MainModule).Any(t => t.FullName == reference.FullName), "Unchanged consumer type reference broken: " + reference.FullName);
            foreach (var reference in consumer.MainModule.GetMemberReferences().Where(m => m.DeclaringType.GetElementType().Scope is AssemblyNameReference s && s.Name == "RainWright"))
            {
                var target = Types(final.MainModule).Single(t => t.FullName == reference.DeclaringType.GetElementType().FullName);
                if (reference is MethodReference method)
                    Require(method.Resolve() is { } resolved && resolved.Module.Assembly.Name.Name == "RainWright" && resolved.Name == method.Name,
                        "Unchanged consumer method reference broken: " + reference.FullName);
                else if (reference is FieldReference field)
                    Require(field.Resolve() is { } resolved && resolved.Module.Assembly.Name.Name == "RainWright" && resolved.Name == field.Name,
                        "Unchanged consumer field reference broken: " + reference.FullName);
            }
        }
    }

    private static void WriteProbePlan(AssemblyDefinition original, string directory)
    {
        var methods = Types(original.MainModule).SelectMany(t => t.Methods).ToArray();
        var root = new XElement("probe");
        foreach (var item in new[] {
            ("world.parse", "WorldConnectionToken::TryParse("), ("world.format", "WorldConnectionToken::Format("),
            ("world.normalize", "WorldConnectionToken::NormalizeLegacy("), ("background.file", "BackgroundDocument::FileName("),
            ("background.number", "BackgroundDocument::Number("), ("background.validate", "BackgroundDocument::ValidateValue("),
            ("background.parse", "BackgroundDocument::Parse("), ("background.serialize", "BackgroundDocument::Serialize("),
            ("modify.compose", "ModificationFileComposer::Compose("), ("record.write", "CartographyRecord::Write("), ("record.read", "CartographyRecord::Read("),
            ("interop.create", "NativePlacedObjectFactory::TryCreate("), ("interop.remove", "ManagedObjectPlacement::RemoveRuntime("),
            ("interop.ensure", "ManagedObjectPlacement::EnsureRuntime("), ("interop.error", "EditorSession::get_PlacementError("),
            ("ui.vanilla", "EditorUiModeState::SetVanilla("), ("ui.allowNativeKeys", "EditorInputRouter::AllowVanillaDevToolsHotkeys(") })
        {
            var method = methods.Single(m => m.FullName.Contains(item.Item2, StringComparison.Ordinal));
            root.Add(new XElement("method", new XAttribute("id", item.Item1), new XAttribute("token", Token(method))));
        }
        foreach (string name in new[] { "CartographyAppearance", "CartographyOptions", "CartographyPalette", "CartographyPoint" })
        {
            var type = Types(original.MainModule).Single(t => t.FullName == "DryCycle.DevUI.DevTool.Map.Cartography." + name);
            root.Add(new XElement("type", new XAttribute("id", name), new XAttribute("token", Token(type))));
        }
        var session = Types(original.MainModule).Single(t => t.FullName == "DryCycle.DevUI.DevTool.Core.EditorSession");
        root.Add(new XElement("type", new XAttribute("id", "session"), new XAttribute("token", Token(session))));
        root.Add(new XElement("field", new XAttribute("id", "session.owner"), new XAttribute("token", Token(session.Fields.Single(f => f.Name == "<Owner>k__BackingField")))));
        new XDocument(root).Save(Path.Combine(directory, "probe-plan.xml"));
    }

    private static void Smoke(string output)
    {
        string privateDir = Path.Combine(output, "debug-private"), dll = Path.Combine(privateDir, "RainWright.dll");
        var map = JsonSerializer.Deserialize<SymbolData>(File.ReadAllText(Path.Combine(privateDir, "symbol-map.json")))!;
        Require(Hash(dll) == map.AssemblySha256, "Symbols do not match this build.");
        // Invoke a filesystem-only production method on CoreCLR. Unity and Rain World are not started.
        var entry = map.Names.Single(n => n.Kind == "method" && n.Original.Contains("CartographyProjectPaths::Owned("));
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
        MethodInfo MappedMethod(string fragment) => (MethodInfo)assembly.ManifestModule.ResolveMethod(Convert.ToInt32(
            map.Names.Single(n => n.Kind == "method" && n.Original.Contains(fragment, StringComparison.Ordinal)).Token[2..], 16))!;
        var writeRecord = MappedMethod("CartographyRecord::Write(");
        var readRecord = MappedMethod("CartographyRecord::Read(");
        var recordResults = new List<object>();
        foreach (string shortName in new[] { "CartographyAppearance", "CartographyOptions", "CartographyPalette", "CartographyPoint" })
        {
            string originalType = "DryCycle.DevUI.DevTool.Map.Cartography." + shortName;
            var mappedType = map.Names.Single(n => n.Kind == "type" && n.Original == originalType);
            Type recordType = assembly.ManifestModule.ResolveType(Convert.ToInt32(mappedType.Token[2..], 16));
            object record = Activator.CreateInstance(recordType, true)!;
            var writable = recordType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            var sample = writable.FirstOrDefault(f => f.FieldType == typeof(string)) ?? writable.First(f => f.FieldType == typeof(float));
            sample.SetValue(record, sample.FieldType == typeof(string) ? "compatibility-smoke" : 7.25f);
            var xml = (XElement)writeRecord.Invoke(null, new[] { "record", record })!;
            var expectedFields = map.Names.Where(n => n.Kind == "field" && n.Owner == originalType).Select(n => n.Original[(n.Original.IndexOf("::", StringComparison.Ordinal) + 2)..]).Order().ToArray();
            Require(expectedFields.SequenceEqual(xml.Attributes().Select(a => a.Name.LocalName).Order()), "Cartography XML attribute contract changed: " + shortName);
            object reloaded = Activator.CreateInstance(recordType, true)!;
            readRecord.Invoke(null, new[] { xml, reloaded });
            var reencoded = (XElement)writeRecord.Invoke(null, new[] { "record", reloaded })!;
            Require(XNode.DeepEquals(xml, reencoded), "Cartography XML roundtrip failed: " + shortName);
            recordResults.Add(new { type = originalType, fieldsPreserved = expectedFields.Length, roundtrip = true });
        }
        WriteJson(Path.Combine(privateDir, "author-data-smoke.json"), recordResults);
        var method = (MethodInfo)assembly.ManifestModule.ResolveMethod(Convert.ToInt32(entry.Token[2..], 16))!;
        try { method.Invoke(null, new object?[] { null, "smoke", "SU" }); throw new Exception("Expected the native Path.Combine null-root error."); }
        catch (TargetInvocationException error) when (error.InnerException is ArgumentNullException)
        {
            string trace = error.InnerException.ToString();
            Require(trace.Contains("src/s", StringComparison.Ordinal) && trace.Contains(":line ", StringComparison.Ordinal), "Scrubbed PDB did not provide a source alias and line number.");
            string raw = Path.Combine(privateDir, "example-obfuscated-stacktrace.txt");
            string decoded = Path.Combine(privateDir, "example-restored-stacktrace.txt");
            File.WriteAllText(raw, trace); Decode(Path.Combine(privateDir, "symbol-map.json"), raw, decoded);
            Require(File.ReadAllText(decoded).Contains("CartographyProjectPaths.Owned", StringComparison.Ordinal), "The symbol map did not restore the method name.");
            Require(!File.ReadAllText(decoded).Contains("[candidates:", StringComparison.Ordinal), "Smoke stack trace restoration was ambiguous.");
            Console.WriteLine("PASS: obfuscated production method executed, matching scrubbed PDB resolved a source line, and the map restored its name/path. This is not an in-game runtime test.");
        }
        var transformed = MappedMethod("BackgroundDocument::FileName(");
        try { transformed.Invoke(null, new object[] { "CON" }); throw new Exception("Expected background validation failure."); }
        catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
        {
            Require(error.InnerException.Message == "Reserved background filename.", "Protected diagnostic did not decode exactly.");
            string trace = error.InnerException.ToString();
            Require(trace.Contains("src/s", StringComparison.Ordinal) && trace.Contains(":line ", StringComparison.Ordinal), "A transformed method lost its source line.");
            string raw = Path.Combine(privateDir, "example-custom-stacktrace.txt"), decoded = Path.Combine(privateDir, "example-custom-restored-stacktrace.txt");
            File.WriteAllText(raw, trace); Decode(Path.Combine(privateDir, "symbol-map.json"), raw, decoded);
            Require(File.ReadAllText(decoded).Contains("BackgroundDocument.FileName", StringComparison.Ordinal) && !File.ReadAllText(decoded).Contains("[candidates:", StringComparison.Ordinal), "Transformed source mapping could not restore a real exception.");
            Console.WriteLine("PASS: custom-transformed production code retained its diagnostic, source line and restored method name.");
        }
    }

    private static void Decode(string mapPath, string input, string output)
    {
        var map = JsonSerializer.Deserialize<SymbolData>(File.ReadAllText(mapPath))!;
        string text = File.ReadAllText(input);
        static string Method(string signature)
        {
            int separator = signature.IndexOf("::", StringComparison.Ordinal);
            int paren = signature.IndexOf('(', separator + 2);
            int space = signature.LastIndexOf(' ', separator);
            return signature[(space + 1)..separator].Replace('/', '+') + "." + signature[(separator + 2)..paren];
        }
        static int ParameterCount(string signature, int paren)
        {
            int depth = 0, count = 0; bool any = false;
            for (int i = paren + 1; i < signature.Length; i++)
            {
                char c = signature[i];
                if (c == ')' && depth == 0) return any ? count + 1 : 0;
                if (c is '<' or '[' or '(') depth++;
                if (c is '>' or ']' or ')') depth--;
                if (c == ',' && depth == 0) count++;
                if (!char.IsWhiteSpace(c)) any = true;
            }
            return -1;
        }
        var groups = map.Names.Where(n => n.Kind == "method").GroupBy(n => Method(n.Obfuscated)).OrderByDescending(g => g.Key.Length).ToArray();
        var methodLocations = (map.Locations ?? new()).ToLookup(l => l.Token);
        text = string.Join(Environment.NewLine, Lines(text).Select(line =>
        {
            var location = Regex.Match(line, @"\bin (?<source>src[/\\]s\d+\.cs):line (?<line>\d+)");
            foreach (var group in groups)
            {
                int index = line.IndexOf(group.Key + "(", StringComparison.Ordinal);
                if (index < 0 || index > 0 && (char.IsLetterOrDigit(line[index - 1]) || line[index - 1] is '.' or '+' or '_')) continue;
                int count = ParameterCount(line, index + group.Key.Length);
                var candidates = group.Where(n => count < 0 || ParameterCount(n.Original, n.Original.IndexOf('(', n.Original.IndexOf("::", StringComparison.Ordinal) + 2)) == count).ToArray();
                if (candidates.Length == 0) candidates = group.ToArray();
                if (location.Success)
                {
                    string source = location.Groups["source"].Value.Replace('\\', '/');
                    int sourceLine = int.Parse(location.Groups["line"].Value);
                    var exact = candidates.Where(n => methodLocations[n.Token].Any(l => l.Source == source && l.Lines.Contains(sourceLine))).ToArray();
                    if (exact.Length > 0) candidates = exact;
                }
                string[] originals = candidates.Select(n => Method(n.Original)).Distinct().ToArray();
                string replacement = originals.Length == 1 ? originals[0] : group.Key + " [candidates: " + string.Join(" | ", originals) + "]";
                line = line[..index] + replacement + line[(index + group.Key.Length)..];
                if (candidates.Length == 1)
                    foreach (var parameter in map.Names.Where(n => n.Kind == "parameter" && n.Owner == candidates[0].Original && n.Original != n.Obfuscated && n.Obfuscated.Length > 0))
                        line = Regex.Replace(line, @"\b" + Regex.Escape(parameter.Obfuscated) + @"\b", _ => parameter.Original);
                break;
            }
            return line;
        }));
        foreach (var source in map.Sources.OrderByDescending(s => s.Obfuscated.Length)) text = text.Replace(source.Obfuscated, source.Original, StringComparison.Ordinal);
        File.WriteAllText(output, text);
    }
}
