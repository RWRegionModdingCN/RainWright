using System.Reflection;

internal static class OwnerReadProjectionProbe
{
    internal static int Run(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        string root = Path.Combine(Path.GetTempPath(), "RainWright-owner-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string inherited = Path.Combine(root, "inherited.json"), target = Path.Combine(root, "not-created.json");
            string original = "{\"version\":1,\"regions\":{\"PROBE\":{\"connections\":[{\"id\":\"edge\",\"a\":{\"room\":\"AA\",\"node\":0},\"b\":{\"room\":\"BB\",\"node\":3}}]}}}";
            File.WriteAllText(inherited, original);
            var owner = assembly.GetType("DryCycle.DevUI.DevTool.World.WorldTopologyPathOwnership", true)!;
            var registry = assembly.GetType("DryCycle.DevUI.DevTool.World.WorldTopologyRegistry", true)!;
            using var lease = (IDisposable)owner.GetMethod("Register", flags)!.Invoke(null,
                new object[] { (Func<bool, string>)(forSave => forSave ? target : inherited) })!;
            registry.GetMethod("Reload", flags)!.Invoke(null, null);
            var edges = (Array)registry.GetMethod("GetRegionEdges", flags)!.Invoke(null, new object[] { "PROBE" })!;
            if (edges.Length != 1) throw new Exception("Inherited topology was not read while keeping a separate author destination.");
            if ((string)registry.GetProperty("LoadedPath", flags)!.GetValue(null) != target)
                throw new Exception("Reading inherited topology redirected the pending author destination.");
            if (!(bool)registry.GetMethod("Save", flags)!.Invoke(null, null)! || File.Exists(target))
                throw new Exception("A clean editor save materialized inherited author data without edits.");
            if (File.ReadAllText(inherited) != original) throw new Exception("Reading a region modified its inherited source.");
            Console.WriteLine("PASS: 4 compiled inherited-topology read, pinned author destination and clean-save checks.");
            return 4;
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
