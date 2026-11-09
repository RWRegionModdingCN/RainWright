using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

// Execute the real deployed constructor and Remix ConfigHolder on the CLR.
// No RainWorld/Unity process, menu, files or game state are created.
internal static class Program
{
    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception failure)
        {
            // Formatting a loader exception's full stack can itself resolve
            // optional game assemblies. Preserve the original chain explicitly.
            for (Exception current = failure; current != null; current = current.InnerException)
                Console.Error.WriteLine(current.GetType().FullName + ": " + current.Message);
            return 1;
        }
    }
    private static int Run(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Pass RainWright.dll and the Rain World installation directory.");
        string game = Path.GetFullPath(args[1]);
        var directories = new[] { Path.GetDirectoryName(Path.GetFullPath(args[0])),
            Path.Combine(game, "BepInEx", "utils"), Path.Combine(game, "BepInEx", "core"),
            Path.Combine(game, "BepInEx", "plugins"), Path.Combine(game, "RainWorld_Data", "Managed") };
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            string filename = new AssemblyName(request.Name).Name + ".dll";
            string path = directories.Select(directory => Path.Combine(directory, filename)).FirstOrDefault(File.Exists);
            return path == null ? null : Assembly.LoadFrom(path);
        };
        Assembly rain = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Assembly native = Assembly.LoadFrom(Path.Combine(game, "BepInEx", "utils", "PUBLIC-Assembly-CSharp.dll"));
        native.GetType("Menu.Remix.ValueConverter", true).GetMethod("_Initialize").Invoke(null, null);
        Type options = rain.GetType("DryCycle.DevUI.DevTool.Personalization.RainWrightOptions", true);
        object instance;
        try { instance = Activator.CreateInstance(options, true); }
        catch (TargetInvocationException failure)
        {
            if (args.Contains("--expect-key-failure") && failure.InnerException is ArgumentException
                && failure.InnerException.Message.Contains("Key_redo-shift"))
            { Console.WriteLine("PASS: reproduced the original Key_redo-shift crash in the actual Remix ConfigHolder."); return 0; }
            throw;
        }
        if (args.Contains("--expect-key-failure")) throw new Exception("Expected the old binary's key failure.");
        object holder = options.BaseType.GetField("config").GetValue(instance);
        var bindings = (IDictionary)holder.GetType().GetField("configurables").GetValue(holder);
        if (bindings.Count == 0) throw new Exception("The native Remix configuration contains no bindings.");
        foreach (string key in bindings.Keys)
            if (key.Any(character => !char.IsLetterOrDigit(character) && character != '_')) throw new Exception("Invalid native key: " + key);
        foreach (string key in new[] { "Key_redo_shift", "Key_redo_y", "Key_objects_duplicate", "Key_window_group" })
            if (!bindings.Contains(key)) throw new Exception("Missing native shortcut binding: " + key);
        VerifyNumericRanges(bindings);
        var shortcuts = (IDictionary)options.GetField("shortcuts", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance);
        if (!shortcuts.Contains("redo-shift") || !shortcuts.Contains("objects-duplicate"))
            throw new Exception("Canonical shortcut/preset IDs changed.");
        Type connector = native.GetType("MachineConnector", true);
        var registered = (IDictionary)connector.GetField("_registeredOIs").GetValue(null);
        object mod = options.BaseType.GetProperty("mod").GetValue(instance);
        mod.GetType().GetField("id").SetValue(mod, "rainwright");
        object automatic = Activator.CreateInstance(native.GetType("Menu.Remix.InternalOI_Auto", true), new[] { mod });
        registered.Add("rainwright", automatic);
        options.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        if (registered["rainwright"].GetType() != options) throw new Exception("Registration did not replace the native no-interface placeholder.");
        object registeredOptions = registered["rainwright"];
        options.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        if (!ReferenceEquals(registered["rainwright"], registeredOptions)) throw new Exception("Repeated registration replaced the active options.");
        Type details = rain.GetType("RainWright.Remix.RemixModDetailsBridge", true);
        try
        {
            details.GetMethod("Enable", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { null });
            throw new Exception("Accepted native Remix setup before its text table was initialized.");
        }
        catch (TargetInvocationException failure)
        {
            if (!(failure.InnerException is InvalidOperationException) || !failure.InnerException.Message.Contains("PreModsInit")) throw;
        }
        if ((bool)details.GetProperty("HasActiveRegistrations", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))
            throw new Exception("An early setup attempt left a native HookGen registration.");
        Console.WriteLine("PASS: actual RainWright OptionInterface constructor, " + bindings.Count + " native config bindings, original preset IDs and MachineConnector registration.");
        Console.WriteLine("PASS: actual details bridge rejects pre-initialization setup before touching native HookGen or ModButton.");
        VerifyStartupOrder(args[0]);
        return 0;
    }
    private static void VerifyNumericRanges(IDictionary bindings)
    {
        var expected = new System.Collections.Generic.Dictionary<string, double[]>
        {
            ["ChineseFontSize"] = new[] { 12d, 72d }, ["EnglishFontSize"] = new[] { 12d, 72d },
            ["EnglishFontWeight"] = new[] { 100d, 900d }, ["WindowAlpha"] = new[] { .15d, 1d },
            ["PanSensitivity"] = new[] { .25d, 3d }, ["GridSize"] = new[] { 1d, 200d },
            ["AutoSaveSeconds"] = new[] { 30d, 1800d }, ["SnapshotCount"] = new[] { 1d, 30d },
            ["RemixGlassOpacity"] = new[] { .15d, .85d }, ["RemixReflection"] = new[] { 0d, 1d },
            ["RemixSectionGap"] = new[] { 8d, 40d }
        };
        foreach (var entry in expected)
        {
            object config = bindings[entry.Key];
            Type type = config.GetType();
            object info = type.GetField("info").GetValue(config);
            object accept = info.GetType().GetField("acceptable").GetValue(info);
            if (accept == null) throw new Exception("OpUpdown configuration has no range: " + entry.Key);
            Type valueType = (Type)type.GetField("settingType").GetValue(config);
            object minInput, maxInput;
            if (valueType == typeof(int)) { minInput = int.MinValue; maxInput = int.MaxValue; }
            else { minInput = float.MinValue; maxInput = float.MaxValue; }
            double min = Convert.ToDouble(accept.GetType().GetMethod("Clamp").Invoke(accept, new[] { minInput }));
            double max = Convert.ToDouble(accept.GetType().GetMethod("Clamp").Invoke(accept, new[] { maxInput }));
            if (Math.Abs(min - entry.Value[0]) > .00001 || Math.Abs(max - entry.Value[1]) > .00001)
                throw new Exception("Wrong native range: " + entry.Key);
            // This is the arithmetic performed by the real OpUpdown initializer,
            // after its real ConfigAcceptableRange has clamped the sentinels.
            if (valueType == typeof(int)) { _ = Math.Abs((int)min); _ = Math.Abs((int)max); }
        }
        Console.WriteLine("PASS: all eleven native numeric ConfigAcceptableRange bindings clamp before OpUpdown initialization.");
    }
    private static void VerifyStartupOrder(string path)
    {
        using (AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path))
        {
            var bridge = assembly.MainModule.Types.Single(type => type.Name == "BridgePlugin");
            var installers = bridge.Methods.Concat(bridge.NestedTypes.SelectMany(type => type.Methods))
                .Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                    instruction.Operand is MethodReference target && target.DeclaringType.FullName == "RainWright.Remix.RemixModDetailsBridge"
                    && target.Name == "Enable")).ToArray();
            if (installers.Length != 1 || !installers[0].Name.StartsWith("<RainWorld_OnModsInit>", StringComparison.Ordinal))
                throw new Exception("Details must install exclusively from the completed native ModsInit boundary.");
            var boundary = bridge.Methods.Single(method => method.Name == "RainWorld_OnModsInit");
            var original = boundary.Body.Instructions.Single(instruction => instruction.Operand is MethodReference method
                && method.DeclaringType.Name == "orig_OnModsInit" && method.Name == "Invoke");
            var optional = boundary.Body.Instructions.Single(instruction => instruction.Operand is MethodReference method
                && method.Name == "TryEnableOptionalFrontendFeature");
            if (original.Offset >= optional.Offset) throw new Exception("Details install before the original native initializer completes.");
        }
        Console.WriteLine("PASS: actual compiled startup order installs Remix details after native ModsInit, with no OnEnable installation.");
    }
}
