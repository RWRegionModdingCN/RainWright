using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Deliberately small opt-in policy. No wildcard matching over game/UI/Hook code.
internal static class CustomProtectionPass
{
    internal sealed record Change(int Token, string Original, int Strings, int Blocks,
        Dictionary<int, int> OriginalToFinalOffsets);
    internal sealed record Result(string SeedId, string Helper, List<Change> Changes);
    private sealed record Anchor(Instruction Instruction, int Offset);
    private static readonly Dictionary<string, HashSet<string>> Allowed = new(StringComparer.Ordinal)
    {
        ["DryCycle.DevUI.DevTool.World.WorldConnectionToken"] = new(new[] { "TryParse", "Format", "NormalizeLegacy" }),
        ["DryCycle.DevUI.DevTool.Background.BackgroundDocument"] = new(new[] { "ValidateValue", "FileName", "Number" })
    };
    private static readonly HashSet<string> DiagnosticStrings = new(StringComparer.Ordinal)
    {
        "Background values must occupy one line.",
        "Use a background name without directories or special characters.",
        "Reserved background filename.", "Invalid background number: "
    };
    internal static byte[] Derive(string seed, string purpose) => HMACSHA256.HashData(Convert.FromHexString(seed), Encoding.UTF8.GetBytes(purpose));
    internal static string SeedId(string seed) => Convert.ToHexString(SHA256.HashData(Convert.FromHexString(seed)));
    internal static string Alphabet(string seed) => new("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"
        .OrderBy(c => Convert.ToHexString(Derive(seed, "alphabet/" + c)), StringComparer.Ordinal).ToArray());

    internal static Result Apply(AssemblyDefinition original, AssemblyDefinition target, string seed, ISet<int> excluded)
    {
        pending.Clear();
        if (seed.Length != 64 || !seed.All(Uri.IsHexDigit)) throw new InvalidDataException("A private 256-bit build seed is required.");
        var module = target.MainModule;
        MethodDefinition? decoder = null;
        var changes = new List<Change>();
        foreach (var type in original.MainModule.Types)
        {
            if (!Allowed.TryGetValue(type.FullName, out var methods)) continue;
            foreach (var source in type.Methods.Where(m => methods.Contains(m.Name)))
            {
                // Exceptions, generics and nested lexical scopes need dedicated transforms. V1
                // leaves them unchanged rather than widening scopes or weakening verification.
                var method = (MethodDefinition)module.LookupToken(source.MetadataToken);
                if (excluded.Contains(source.MetadataToken.ToInt32()) || !Eligible(method)) continue;
                var originals = method.Body.Instructions.Select(i => new Anchor(i, i.Offset)).ToArray();
                var opcodes = originals.ToDictionary(a => a.Instruction, a => a.Instruction.OpCode);
                var byOffset = originals.ToDictionary(a => a.Offset, a => a.Instruction);
                var points = method.DebugInformation.SequencePoints.Select(p => (Instruction: byOffset[p.Offset], Point: p)).ToArray();
                int literals = 0;
                foreach (var anchor in originals)
                {
                    var instruction = anchor.Instruction;
                    if (instruction.OpCode.Code != Code.Ldstr || instruction.Operand is not string text || !DiagnosticStrings.Contains(text)) continue;
                    decoder ??= CreateDecoder(module, seed);
                    uint state = BitConverter.ToUInt32(Derive(seed, source.MetadataToken.ToInt32() + "/literal/" + anchor.Offset), 0) | 1u;
                    byte[] bytes = new UTF8Encoding(false, true).GetBytes(text);
                    uint cursor = state;
                    for (int i = 0; i < bytes.Length; i++) { cursor = Step(cursor); bytes[i] ^= (byte)cursor; }
                    instruction.Operand = Convert.ToBase64String(bytes);
                    var il = method.Body.GetILProcessor();
                    var key = Instruction.Create(OpCodes.Ldc_I4, unchecked((int)state));
                    il.InsertAfter(instruction, key); il.InsertAfter(key, Instruction.Create(OpCodes.Call, decoder));
                    literals++;
                }
                WidenBranches(method);
                int blocks = ReorderBlocks(method, seed, source.MetadataToken.ToInt32());
                if (literals == 0 && blocks == 0)
                { foreach (var entry in opcodes) entry.Key.OpCode = entry.Value; continue; }
                // Attach every original sequence point to the same semantic instruction. Physical
                // order may change; source lines/columns and document checksums do not.
                var indices = method.Body.Instructions.Select((i, index) => (i, index)).ToDictionary(p => p.i, p => p.index);
                method.DebugInformation.SequencePoints.Clear();
                foreach (var point in points.OrderBy(p => indices[p.Instruction]))
                    method.DebugInformation.SequencePoints.Add(new SequencePoint(point.Instruction, point.Point.Document)
                    { StartLine = point.Point.StartLine, EndLine = point.Point.EndLine, StartColumn = point.Point.StartColumn, EndColumn = point.Point.EndColumn });
                if (method.DebugInformation.Scope is { } scope)
                { scope.Start = new InstructionOffset(method.Body.Instructions[0]); scope.End = default; }
                ValidateStack(method);
                // Offsets are recalculated by Cecil's writer. Capture provenance afterwards.
                pending.Add((method, originals, literals, blocks, source.FullName));
            }
        }
        if (decoder == null || pending.Count == 0) throw new InvalidDataException("Custom protection policy matched no tested methods/literals; refusing an unprotected success report.");
        ValidateStack(decoder);
        module.Mvid = new Guid(Derive(seed, "mvid/" + original.MainModule.Mvid).AsSpan(0, 16));
        return new Result(SeedId(seed), decoder.FullName, changes);
    }

    // A pass instance is one command/process. Cleared after the writer has assigned final offsets.
    private static readonly List<(MethodDefinition Method, Anchor[] Anchors, int Strings, int Blocks, string Original)> pending = new();
    internal static void Complete(Result result)
    {
        foreach (var item in pending)
            result.Changes.Add(new(item.Method.MetadataToken.ToInt32(), item.Original, item.Strings, item.Blocks,
                item.Anchors.ToDictionary(a => a.Offset, a => a.Instruction.Offset)));
        pending.Clear();
    }

    private static bool Eligible(MethodDefinition method) => method.HasBody && method.IsStatic && !method.IsConstructor && !method.HasGenericParameters &&
        !method.DeclaringType.HasGenericParameters && method.Body.Instructions.Count is > 3 and < 700 && method.Body.ExceptionHandlers.Count == 0 &&
        (method.DebugInformation.Scope == null || method.DebugInformation.Scope.Scopes.Count == 0) &&
        !method.Body.Instructions.Any(i => i.OpCode.Code is Code.Jmp or Code.Calli or Code.Localloc or Code.Tail);
    private static uint Step(uint state) { state ^= state << 13; state ^= state >> 17; return state ^ (state << 5); }
    private static void WidenBranches(MethodDefinition method)
    {
        foreach (var instruction in method.Body.Instructions)
            if (instruction.OpCode.OperandType == OperandType.ShortInlineBrTarget)
                instruction.OpCode = instruction.OpCode.Code switch
                {
                    Code.Br_S => OpCodes.Br, Code.Brfalse_S => OpCodes.Brfalse, Code.Brtrue_S => OpCodes.Brtrue,
                    Code.Beq_S => OpCodes.Beq, Code.Bge_S => OpCodes.Bge, Code.Bge_Un_S => OpCodes.Bge_Un,
                    Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un, Code.Ble_S => OpCodes.Ble,
                    Code.Ble_Un_S => OpCodes.Ble_Un, Code.Blt_S => OpCodes.Blt, Code.Blt_Un_S => OpCodes.Blt_Un,
                    Code.Bne_Un_S => OpCodes.Bne_Un, Code.Leave_S => OpCodes.Leave,
                    _ => throw new InvalidDataException("Unsupported short branch: " + instruction)
                };
    }
    private static int ReorderBlocks(MethodDefinition method, string seed, int token)
    {
        var instructions = method.Body.Instructions.ToArray();
        var heights = ValidateStack(method);
        var indices = instructions.Select((i, index) => (i, index)).ToDictionary(p => p.i, p => p.index);
        var leaders = new HashSet<Instruction> { instructions[0] };
        for (int i = 0; i < instructions.Length; i++)
        {
            var instruction = instructions[i];
            if (instruction.Operand is Instruction branch) leaders.Add(branch);
            if (instruction.Operand is Instruction[] branches) foreach (var target in branches) leaders.Add(target);
            if (i + 1 < instructions.Length && instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw)
                leaders.Add(instructions[i + 1]);
        }
        // Mono/Cecil must not infer a live evaluation stack from a later physical predecessor.
        // Move only regions whose crossing edges all have empty stacks. Merge the complete
        // original span of every nonempty-stack edge (e.g. compiler null-coalescing/ternaries).
        var cuts = leaders.Where(i => heights.TryGetValue(i, out int height) && height == 0).Select(i => indices[i]).ToHashSet();
        foreach (var instruction in instructions)
        {
            var targets = instruction.Operand is Instruction branch ? new[] { branch } : instruction.Operand as Instruction[] ?? Array.Empty<Instruction>();
            foreach (var target in targets)
                if (heights.TryGetValue(target, out int height) && height != 0)
                {
                    int first = Math.Min(indices[instruction], indices[target]), last = Math.Max(indices[instruction], indices[target]);
                    cuts.RemoveWhere(cut => cut > first && cut <= last);
                }
        }
        var blocks = new List<List<Instruction>>();
        foreach (var instruction in instructions)
        { if (indices[instruction] == 0 || cuts.Contains(indices[instruction])) blocks.Add(new()); blocks[^1].Add(instruction); }
        if (blocks.Count < 3) return 0;
        for (int i = 0; i < blocks.Count - 1; i++)
            if (blocks[i][^1].OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
                blocks[i].Add(Instruction.Create(OpCodes.Br, blocks[i + 1][0]));
        var order = Enumerable.Range(1, blocks.Count - 1).OrderBy(i => Convert.ToHexString(Derive(seed, token + "/block/" + i)), StringComparer.Ordinal).ToArray();
        if (order.SequenceEqual(Enumerable.Range(1, blocks.Count - 1))) Array.Reverse(order);
        method.Body.Instructions.Clear();
        foreach (var index in new[] { 0 }.Concat(order)) foreach (var instruction in blocks[index]) method.Body.Instructions.Add(instruction);
        return blocks.Count;
    }

    private static MethodDefinition CreateDecoder(ModuleDefinition module, string seed)
    {
        string tag = Convert.ToHexString(Derive(seed, "helper")).Substring(0, 20);
        var type = new TypeDefinition("", "R" + tag, TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit, module.TypeSystem.Object);
        module.Types.Add(type); // Append: never shift any existing MethodDef/TypeDef token.
        var method = new MethodDefinition("D" + tag, MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.String);
        method.Parameters.Add(new("p0", ParameterAttributes.None, module.TypeSystem.String));
        method.Parameters.Add(new("p1", ParameterAttributes.None, module.TypeSystem.Int32)); type.Methods.Add(method);
        method.Body.InitLocals = true;
        var bytes = new VariableDefinition(new ArrayType(module.TypeSystem.Byte));
        var state = new VariableDefinition(module.TypeSystem.UInt32); var index = new VariableDefinition(module.TypeSystem.Int32);
        method.Body.Variables.Add(bytes); method.Body.Variables.Add(state); method.Body.Variables.Add(index);
        var convert = new TypeReference("System", "Convert", module, module.TypeSystem.CoreLibrary);
        var encoding = new TypeReference("System.Text", "Encoding", module, module.TypeSystem.CoreLibrary);
        var fromBase64 = new MethodReference("FromBase64String", bytes.VariableType, convert) { HasThis = false };
        fromBase64.Parameters.Add(new(module.TypeSystem.String));
        var getUtf8 = new MethodReference("get_UTF8", encoding, encoding) { HasThis = false };
        var getString = new MethodReference("GetString", module.TypeSystem.String, encoding) { HasThis = true };
        getString.Parameters.Add(new(bytes.VariableType));
        var intern = new MethodReference("Intern", module.TypeSystem.String, module.TypeSystem.String) { HasThis = false };
        intern.Parameters.Add(new(module.TypeSystem.String));
        var il = method.Body.GetILProcessor();
        void Emit(OpCode opcode) => il.Append(Instruction.Create(opcode));
        void Local(OpCode opcode, VariableDefinition variable) => il.Append(Instruction.Create(opcode, variable));
        void Int(int value) => il.Append(Instruction.Create(OpCodes.Ldc_I4, value));
        Emit(OpCodes.Ldarg_0); il.Append(Instruction.Create(OpCodes.Call, fromBase64)); Local(OpCodes.Stloc, bytes);
        Emit(OpCodes.Ldarg_1); Local(OpCodes.Stloc, state); Emit(OpCodes.Ldc_I4_0); Local(OpCodes.Stloc, index);
        var test = Instruction.Create(OpCodes.Ldloc, index); il.Append(Instruction.Create(OpCodes.Br, test));
        var loop = Instruction.Create(OpCodes.Ldloc, state); il.Append(loop);
        Local(OpCodes.Ldloc, state); Int(13); Emit(OpCodes.Shl); Emit(OpCodes.Xor); Local(OpCodes.Stloc, state);
        Local(OpCodes.Ldloc, state); Local(OpCodes.Ldloc, state); Int(17); Emit(OpCodes.Shr_Un); Emit(OpCodes.Xor); Local(OpCodes.Stloc, state);
        Local(OpCodes.Ldloc, state); Local(OpCodes.Ldloc, state); Int(5); Emit(OpCodes.Shl); Emit(OpCodes.Xor); Local(OpCodes.Stloc, state);
        Local(OpCodes.Ldloc, bytes); Local(OpCodes.Ldloc, index); Local(OpCodes.Ldloc, bytes); Local(OpCodes.Ldloc, index);
        Emit(OpCodes.Ldelem_U1); Local(OpCodes.Ldloc, state); Emit(OpCodes.Conv_U1); Emit(OpCodes.Xor); Emit(OpCodes.Conv_U1); Emit(OpCodes.Stelem_I1);
        Local(OpCodes.Ldloc, index); Emit(OpCodes.Ldc_I4_1); Emit(OpCodes.Add); Local(OpCodes.Stloc, index);
        il.Append(test); Local(OpCodes.Ldloc, bytes); Emit(OpCodes.Ldlen); Emit(OpCodes.Conv_I4); il.Append(Instruction.Create(OpCodes.Blt, loop));
        il.Append(Instruction.Create(OpCodes.Call, getUtf8)); Local(OpCodes.Ldloc, bytes); il.Append(Instruction.Create(OpCodes.Callvirt, getString));
        il.Append(Instruction.Create(OpCodes.Call, intern)); Emit(OpCodes.Ret);
        return method;
    }

    // Stack-height/dataflow validation, including branch joins, catches malformed emitted IL before
    // any candidate is exported. Execution on CLR and the game's Mono is a separate mandatory gate.
    internal static Dictionary<Instruction, int> ValidateStack(MethodDefinition method)
    {
        var instructions = method.Body.Instructions;
        var indices = instructions.Select((i, n) => (i, n)).ToDictionary(p => p.i, p => p.n);
        var heights = new Dictionary<Instruction, int>(); var queue = new Queue<(Instruction Instruction, int Height)>();
        queue.Enqueue((instructions[0], 0)); int maximum = 0;
        while (queue.Count > 0)
        {
            var (instruction, height) = queue.Dequeue();
            if (!indices.ContainsKey(instruction)) throw new InvalidDataException("Branch leaves its method.");
            if (heights.TryGetValue(instruction, out int prior))
            { if (prior != height) throw new InvalidDataException("Inconsistent branch stack in " + method.FullName); continue; }
            heights[instruction] = height;
            int pop = instruction.OpCode.StackBehaviourPop switch
            {
                StackBehaviour.Pop0 => 0,
                StackBehaviour.Varpop when instruction.OpCode.Code == Code.Ret => method.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
                StackBehaviour.Varpop when instruction.Operand is MethodReference call => call.Parameters.Count + (call.HasThis && instruction.OpCode.Code != Code.Newobj ? 1 : 0),
                StackBehaviour.PopAll => height,
                StackBehaviour.Varpop => throw new InvalidDataException("Unsupported dynamic stack operation."),
                _ => instruction.OpCode.StackBehaviourPop.ToString().Split('_').Length
            };
            int push = instruction.OpCode.StackBehaviourPush switch
            {
                StackBehaviour.Push0 => 0,
                StackBehaviour.Varpush when instruction.OpCode.Code == Code.Newobj => 1,
                StackBehaviour.Varpush when instruction.Operand is MethodReference call => call.ReturnType.MetadataType == MetadataType.Void ? 0 : 1,
                StackBehaviour.Varpush => throw new InvalidDataException("Unsupported dynamic stack operation."),
                _ => instruction.OpCode.StackBehaviourPush.ToString().Split('_').Length
            };
            if (height < pop) throw new InvalidDataException("Stack underflow in " + method.FullName);
            int next = height - pop + push; maximum = Math.Max(maximum, Math.Max(height, next));
            if (instruction.OpCode.FlowControl == FlowControl.Return && next != 0) throw new InvalidDataException("Nonempty return stack.");
            if (instruction.Operand is Instruction target) queue.Enqueue((target, next));
            if (instruction.Operand is Instruction[] targets) foreach (var targetItem in targets) queue.Enqueue((targetItem, next));
            if (instruction.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
            {
                int n = indices[instruction] + 1;
                if (n == instructions.Count) throw new InvalidDataException("Method falls off its end.");
                queue.Enqueue((instructions[n], next));
            }
        }
        method.Body.MaxStackSize = maximum;
        return heights;
    }

    // Independent re-read must exactly match the expected final program, not merely a claimed
    // transform count. Instruction indices make this independent of Cecil-assigned byte offsets.
    internal static string Fingerprint(AssemblyDefinition assembly)
    {
        var text = new StringBuilder();
        void Add(object? value) => text.Append(JsonSerializer.Serialize(value)).Append('\n');
        Add(assembly.Name.FullName); Add(assembly.MainModule.Mvid);
        foreach (var reference in assembly.MainModule.AssemblyReferences) Add(reference.FullName);
        IEnumerable<TypeDefinition> Types(TypeDefinition t) => new[] { t }.Concat(t.NestedTypes.SelectMany(Types));
        foreach (var type in assembly.MainModule.Types.SelectMany(Types))
        {
            Add(type.FullName); Add(type.Attributes); Add(type.BaseType?.FullName);
            foreach (var field in type.Fields) { Add(field.FullName); Add(field.Attributes); }
            foreach (var method in type.Methods)
            {
                Add(method.FullName); Add(method.Attributes); Add(method.ImplAttributes);
                if (!method.HasBody) continue;
                var indices = method.Body.Instructions.Select((i, n) => (i, n)).ToDictionary(p => p.i, p => p.n);
                foreach (var local in method.Body.Variables) Add(local.VariableType.FullName);
                Add(method.Body.InitLocals);
                foreach (var i in method.Body.Instructions)
                {
                    Add(i.OpCode.Code.ToString());
                    Add(i.Operand switch
                    {
                        Instruction branch => "branch:" + indices[branch], Instruction[] targets => "switch:" + string.Join(",", targets.Select(t => indices[t])),
                        MemberReference member => member.FullName, VariableDefinition variable => "local:" + variable.Index,
                        ParameterDefinition argument => "arg:" + argument.Index,
                        _ => Convert.ToString(i.Operand, CultureInfo.InvariantCulture)
                    });
                }
                var byOffset = method.Body.Instructions.ToDictionary(i => i.Offset, i => indices[i]);
                foreach (var point in method.DebugInformation.SequencePoints)
                    Add(new { instruction = byOffset[point.Offset], point.Document.Url, point.StartLine, point.EndLine, point.StartColumn, point.EndColumn });
            }
        }
        foreach (var resource in assembly.MainModule.Resources.OfType<EmbeddedResource>())
        { Add(resource.Name); Add(Convert.ToHexString(SHA256.HashData(resource.GetResourceData()))); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
