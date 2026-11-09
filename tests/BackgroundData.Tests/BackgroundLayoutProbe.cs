using System.Collections;
using System.Reflection;

internal static class BackgroundLayoutProbe
{
    internal static int Run(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        const BindingFlags member = BindingFlags.Instance | BindingFlags.NonPublic;
        var layout = assembly.GetType("DryCycle.DevUI.DevTool.Background.BackgroundEditorLayout", true)!;
        var section = layout.GetMethod("Section", flags)!;
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        foreach (var item in new[] { ("daySky", "Day"), ("duskAtmosphereColor", "Dusk"), ("nightMultiplyColor", "Night"),
            ("distantCloudsEndDepth", "Clouds"), ("startFogAltitude", "Fog"), ("rubbleCount", "Ground"), ("_backgroundOffset", "Placement"), ("future_extension", "Other") })
            Check(section.Invoke(null, new object[] { item.Item1, false })!.ToString() == item.Item2, "Scene relationship lost for " + item.Item1);
        foreach (var item in new[] { ("pos", "Placement"), ("spriteScale", "Geometry"), ("spriteColor", "Appearance"), ("container", "Layer"), ("extension", "Other") })
            Check(section.Invoke(null, new object[] { item.Item1, true })!.ToString() == item.Item2, "Element relationship lost for " + item.Item1);
        var elementType = assembly.GetType("DryCycle.DevUI.DevTool.Background.BackgroundElementSnapshot", true)!;
        var rowType = layout.GetNestedType("Row", BindingFlags.NonPublic)!;
        var rows = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        object Element(int id, int parent, string name, bool group)
        {
            object element = Activator.CreateInstance(elementType, true)!;
            elementType.GetField("Id", member)!.SetValue(element, id);
            elementType.GetField("ParentId", member)!.SetValue(element, parent);
            elementType.GetField("Name", member)!.SetValue(element, name);
            elementType.GetField("IsGroup", member)!.SetValue(element, group);
            return element;
        }
        // Deliberately shuffled source order: parentage, rather than a flat array's order, owns indentation.
        var array = Array.CreateInstance(elementType, 4);
        array.SetValue(Element(3, 2, "Tower", false), 0); array.SetValue(Element(2, 1, "City", true), 1);
        array.SetValue(Element(1, 0, "Horizon", true), 2); array.SetValue(Element(4, 0, "Fog", false), 3);
        var collapsed = new HashSet<int>();
        void Build(string search) => layout.GetMethod("BuildRows", flags)!.Invoke(null, new object[] { array, search, collapsed, rows });
        int Id(int index) => (int)elementType.GetField("Id", member)!.GetValue(rowType.GetField("Element", member)!.GetValue(rows[index]))!;
        int Depth(int index) => (int)rowType.GetField("Depth", member)!.GetValue(rows[index])!;
        Build("");
        Check(rows.Count == 4 && Id(0) == 1 && Id(1) == 2 && Id(2) == 3 && Depth(2) == 2, "Nested groups were flattened or their authored parentage was lost.");
        collapsed.Add(1); Build("");
        Check(rows.Count == 2 && Id(0) == 1 && Id(1) == 4, "Collapsed groups must hide descendants while retaining unrelated roots.");
        Build("Tower");
        Check(rows.Count == 3 && Id(0) == 1 && Id(1) == 2 && Id(2) == 3, "A search must retain the matching child's complete ancestor path and expand it temporarily.");
        Build("missing"); Check(rows.Count == 0, "An unmatched search must not show unrelated elements.");
        Build(""); Check(rows.Count == 2, "Clearing a search must restore the user's collapsed tree.");
        var catalog = assembly.GetType("DryCycle.DevUI.DevTool.Background.BackgroundAssetCatalog", true)!;
        string[] illustrations = { "atc_sky", "RF_Hall", "pnk_wall", "au_ruin", "otr_gate", "extra", "duplicate" };
        string[] sprites = { "atc_cloud", "rf_light", "pnk_glow", "au_lamp", "otr_fire", "DUPLICATE" };
        string[] Assets(int category, string query = "") => (string[])catalog.GetMethod("Select", flags)!.Invoke(null, new object[] { illustrations, sprites, category, query })!;
        foreach (int category in Enumerable.Range(0, 5))
            Check(Assets(category).Length == 2, "Community asset category must include both illustration files and loaded sprites: " + category);
        Check(Assets(5).Length == illustrations.Length && Assets(6).Length == sprites.Length, "Illustration and sprite source categories must remain separate.");
        Check(Assets(7).Length == illustrations.Length + sprites.Length - 1, "Combined asset lists must deduplicate case-insensitively.");
        Check(Assets(7, "SKY").SequenceEqual(new[] { "atc_sky" }), "Asset search must ignore casing without changing resource names.");
        foreach (string key in new[] { "daySky", "duskSky", "nightSky", "assetName", "spriteName" })
            Check((bool)catalog.GetMethod("IsAsset", flags)!.Invoke(null, new object[] { key })!, "Background image field lost its picker: " + key);
        Check(!(bool)catalog.GetMethod("IsAsset", flags)!.Invoke(null, new object[] { "name" })!, "User-defined group names must remain free text.");
        Console.WriteLine("PASS: " + checks + " compiled background grouping, hierarchy, collapse and search checks.");
        return checks;
    }
}
