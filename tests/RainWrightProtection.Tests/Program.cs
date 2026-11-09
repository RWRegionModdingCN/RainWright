using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Mono.Cecil.Cil;

int checks = 0;
void Check(bool ok, string reason) { if (!ok) throw new Exception(reason); checks++; }
bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
string root = Path.Combine(Path.GetTempPath(), "RainWright-protection-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
const string seedA = "6EB8E9A8F43A2DAD32E3D557B5CF0E190F8E9B501650551F4EF419BA4465D67D";
const string seedB = "EE8FA008C1E08EE215F624AEF553DBE1280D6C1027EC138D29FAE57257B7BEEF";
const string diagnostic = "Background values must occupy one line.";
try
{
    string fixturePath = Path.Combine(root, "fixture.dll");
    using (var fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("ProtectionFixture", new Version(1, 0)), "fixture", ModuleKind.Dll))
    {
        var module = fixture.MainModule;
        var type = new TypeDefinition("DryCycle.DevUI.DevTool.World", "WorldConnectionToken", Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        var method = new MethodDefinition("Format", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.String);
        type.Methods.Add(method); method.Parameters.Add(new("value", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
        var equality = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String) { HasThis = false };
        equality.Parameters.Add(new(module.TypeSystem.String)); equality.Parameters.Add(new(module.TypeSystem.String));
        var fallback = Instruction.Create(OpCodes.Ldstr, "fallback"); var special = Instruction.Create(OpCodes.Ldstr, diagnostic);
        var instructions = new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Brfalse_S, fallback), Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldstr, "special"), Instruction.Create(OpCodes.Call, equality), Instruction.Create(OpCodes.Brtrue_S, special),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ret), special, Instruction.Create(OpCodes.Ret), fallback, Instruction.Create(OpCodes.Ret) };
        method.Body.InitLocals = true;
        var normalizedValue = new VariableDefinition(module.TypeSystem.String); method.Body.Variables.Add(normalizedValue);
        var normalized = Instruction.Create(OpCodes.Stloc, normalizedValue);
        var prefix = new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Dup), Instruction.Create(OpCodes.Brtrue_S, normalized),
            Instruction.Create(OpCodes.Pop), Instruction.Create(OpCodes.Ldstr, "fallback"), normalized };
        foreach (var instruction in instructions) if (instruction.OpCode.Code == Code.Ldarg_0) { instruction.OpCode = OpCodes.Ldloc; instruction.Operand = normalizedValue; }
        instructions = prefix.Concat(instructions).ToArray();
        foreach (var instruction in instructions) method.Body.Instructions.Add(instruction);
        var document = new Document("private/source.cs");
        for (int i = 0; i < instructions.Length; i++) method.DebugInformation.SequencePoints.Add(new SequencePoint(instructions[i], document) { StartLine = i + 10, EndLine = i + 10, StartColumn = 1, EndColumn = 15 });
        method.DebugInformation.Scope = new ScopeDebugInformation(instructions[0], null);
        fixture.Write(fixturePath, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider() });
    }
    AssemblyDefinition Read() => AssemblyDefinition.ReadAssembly(fixturePath, new ReaderParameters { ReadSymbols = true, InMemory = true });
    var fingerprints = new List<string>();
    foreach (var run in new[] { ("a", seedA), ("a-repeat", seedA), ("b", seedB) })
    {
        using var original = Read(); using var target = Read();
        var method = target.MainModule.Types.Single(t => t.Name == "WorldConnectionToken").Methods.Single();
        var result = CustomProtectionPass.Apply(original, target, run.Item2, new HashSet<int>());
        string candidate = Path.Combine(root, run.Item1 + ".dll");
        target.Write(candidate, new WriterParameters { WriteSymbols = true, SymbolWriterProvider = new PortablePdbWriterProvider() });
        CustomProtectionPass.Complete(result);
        Check(result.Changes.Count == 1 && result.Changes[0].Strings == 1 && result.Changes[0].Blocks >= 3, "Fixture was not actually transformed.");
        Check(method.Body.Instructions.All(i => i.Operand as string != diagnostic), "Protected diagnostic remained plaintext in the target method.");
        using var reread = AssemblyDefinition.ReadAssembly(candidate, new ReaderParameters { ReadSymbols = true });
        string fingerprint = CustomProtectionPass.Fingerprint(target);
        Check(fingerprint == CustomProtectionPass.Fingerprint(reread), "Independent re-read changed IL or source mappings.");
        fingerprints.Add(fingerprint);
        var final = (MethodDefinition)reread.MainModule.LookupToken(method.MetadataToken);
        var source = original.MainModule.Types.Single(t => t.Name == "WorldConnectionToken").Methods.Single();
        foreach (var point in source.DebugInformation.SequencePoints)
            Check(final.DebugInformation.SequencePoints.Any(p => p.Offset == result.Changes[0].OriginalToFinalOffsets[point.Offset] && p.StartLine == point.StartLine), "A source line moved to a different semantic instruction.");
        var context = new AssemblyLoadContext("protection-fixture", true);
        try
        {
            using var candidateStream = File.OpenRead(candidate);
            var assembly = context.LoadFromStream(candidateStream);
            var invoke = assembly.GetType("DryCycle.DevUI.DevTool.World.WorldConnectionToken", true)!.GetMethod("Format")!;
            foreach (var input in new string?[] { null, "", "special", "普通字符串 😀", "other" })
            {
                string actual = (string)invoke.Invoke(null, new object?[] { input })!;
                string expected = input == null ? "fallback" : input == "special" ? diagnostic : input;
                Check(actual == expected, "Block permutation changed production behavior.");
                if (input == "special") Check(ReferenceEquals(actual, diagnostic), "String-literal interning semantics were lost.");
            }
        }
        finally { context.Unload(); }
        final.Body.Instructions[0].OpCode = OpCodes.Nop;
        Check(fingerprint != CustomProtectionPass.Fingerprint(reread), "Tampered code was not detected by the final fingerprint.");
    }
    Check(fingerprints[0] == fingerprints[1], "The same seed did not reproduce the same transforms.");
    Check(fingerprints[0] != fingerprints[2], "Different seeds produced the same transformed program.");
    Check(CustomProtectionPass.Alphabet(seedA) != CustomProtectionPass.Alphabet(seedB), "Release seed did not diversify the name alphabet.");
    using (var original = Read()) using (var target = Read())
    {
        var method = target.MainModule.Types.Single(t => t.Name == "WorldConnectionToken").Methods.Single();
        Check(Reject(() => CustomProtectionPass.Apply(original, target, seedA, new HashSet<int> { method.MetadataToken.ToInt32() })), "Excluded contracts were transformed or coverage was falsely reported.");
        Check(Reject(() => CustomProtectionPass.Apply(original, target, "bad-seed", new HashSet<int>())), "Invalid build seed was accepted.");
        method.Body.Instructions.Clear(); method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Check(Reject(() => CustomProtectionPass.ValidateStack(method)), "Malformed emitted stack was accepted.");
        method.Body.Instructions.Clear(); method.Body.Instructions.Add(Instruction.Create(OpCodes.Br, Instruction.Create(OpCodes.Ret)));
        Check(Reject(() => CustomProtectionPass.ValidateStack(method)), "A branch outside its method was accepted.");
    }
    Console.WriteLine("PASS: " + checks + " custom protection execution, seeds, source mapping, literal semantics and failure-gate checks.");
}
finally { Directory.Delete(root, true); }
