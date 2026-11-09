using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;

internal static class OriginalInputProbe
{
    private static int originalCalls = 0;
    internal static int Run(string editor, string game, string regionKit)
    {
        var paths = new[] { Path.GetDirectoryName(editor), Path.Combine(game, "RainWorld_Data", "Managed"),
            Path.Combine(game, "BepInEx", "core"), Path.Combine(game, "BepInEx", "plugins"), Path.Combine(game, "BepInEx", "utils"),
            Path.GetFullPath(Path.Combine(game, "../../workshop/content/312520/3417372413/plugins")) };
        var context = new ProbeContext(paths);
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(editor));
        if (assembly.GetType("DryCycle.DevUI.DevTool.RWImGui.AuthoringModWindow") != null)
            throw new Exception("The obsolete manual author-Mod selection window is still shipped.");
        var state = assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorUiModeState", true)!;
        int revision = (int)state.GetProperty("PresentationRevision")!.GetValue(null)!;
        state.GetMethod("ToggleTopStatus")!.Invoke(null, null);
        if (!(bool)state.GetProperty("TopStatusHidden")!.GetValue(null)! || (int)state.GetProperty("PresentationRevision")!.GetValue(null)! <= revision)
            throw new Exception("Top status toggle did not publish presentation-only visibility.");
        state.GetMethod("SetVanilla")!.Invoke(null, new object[] { true });
        var router = assembly.GetType("DryCycle.DevUI.DevTool.Input.EditorInputRouter", true)!;
        foreach (string name in new[] { "RainWorldGame_RawUpdate", "RainWorldGame_Update", "Player_checkInput" })
        {
            var handler = router.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            Type originalType = handler.GetParameters()[0].ParameterType;
            var invoke = originalType.GetMethod("Invoke")!;
            var callback = new DynamicMethod("CountOriginal", typeof(void), invoke.GetParameters().Select(p => p.ParameterType).ToArray(), typeof(OriginalInputProbe).Module, true);
            var il = callback.GetILGenerator();
            var counter = typeof(OriginalInputProbe).GetField(nameof(originalCalls), BindingFlags.Static | BindingFlags.NonPublic)!;
            il.Emit(OpCodes.Ldsfld, counter); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stsfld, counter); il.Emit(OpCodes.Ret);
            object[] args = handler.GetParameters().Select(p => p.ParameterType == typeof(float) ? (object).1f : null).ToArray();
            args[0] = callback.CreateDelegate(originalType);
            int before = originalCalls;
            // A null game/player is deliberate: any hidden editor work after orig would fail.
            handler.Invoke(null, args);
            if (originalCalls != before + 1) throw new Exception(name + " did not forward exactly once.");
        }
        if (!(bool)router.GetMethod("AllowVanillaDevToolsHotkeys", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { true, null })!)
            throw new Exception("Vanilla native hotkeys were swallowed.");
        // Exercise the deployable adapter against the installed mod's actual protected field.
        // Allocate raw data only; never invoke a RegionKit constructor or editor method.
        var kit = context.LoadFromAssemblyPath(Path.GetFullPath(regionKit));
        var recordType = kit.GetType("RegionKit.Modules.BackgroundBuilder.BackgroundElementData+BG_SimpleElement", true)!;
        object record = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(recordType);
        FieldInfo asset = null;
        for (Type cursor = recordType; cursor != null && asset == null; cursor = cursor.BaseType)
            asset = cursor.GetField("assetName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
        asset!.SetValue(record, "before");
        var access = assembly.GetType("DryCycle.DevUI.DevTool.Background.BackgroundDataAccess", true)!;
        var reader = (Func<string>)access.GetMethod("StringReader", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new[] { record, "assetName" })!;
        if (reader() != "before") throw new Exception("Installed protected data field could not be read.");
        asset.SetValue(record, "edited-by-original");
        if (reader() != "edited-by-original") throw new Exception("Compiled data reader did not follow original editor changes.");
        int selectionChecks = WorldSelectionProbe.Run(assembly);
        int connectionChecks = ConnectionSyntaxProbe.Run(assembly);
        int ownerChecks = OwnerReadProjectionProbe.Run(assembly);
        int backgroundChecks = BackgroundLayoutProbe.Run(assembly);
        state.GetMethod("SetVanilla")!.Invoke(null, new object[] { false });
        state.GetMethod("ToggleTopStatus")!.Invoke(null, null);
        if ((bool)state.GetProperty("TopStatusHidden")!.GetValue(null)!) throw new Exception("Top status could not be restored.");
        context.Unload();
        return 9 + selectionChecks + connectionChecks + ownerChecks + backgroundChecks;
    }
    private sealed class ProbeContext : AssemblyLoadContext
    {
        private readonly string[] paths;
        internal ProbeContext(string[] paths) : base("OriginalInputIsolation", true) { this.paths = paths; }
        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name is "mscorlib" or "netstandard" or "System" || name.Name.StartsWith("System.", StringComparison.Ordinal)) return null;
            foreach (string path in paths)
            {
                string file = Path.Combine(path, name.Name + ".dll");
                if (File.Exists(file)) return LoadFromAssemblyPath(file);
            }
            return null;
        }
    }
}
