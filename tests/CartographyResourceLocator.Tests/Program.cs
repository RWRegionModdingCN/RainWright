using System;
using System.IO;
using System.Reflection;
using DryCycle.DevUI.DevTool.Map.Cartography;

internal static class Program
{
    private static int checks;
    private static void Check(bool result, string message)
    {
        checks++;
        if (!result) throw new InvalidOperationException(message);
    }

    private static string Read(string sheet)
    {
        using Stream stream = CartographyResourceLocator.Open(Assembly.GetExecutingAssembly(), sheet);
        if (stream == null) return null;
        using StreamReader reader = new(stream);
        return reader.ReadToEnd().Trim();
    }

    private static int Main()
    {
        try
        {
            Check(Read("Objects") == "Rainwright sprite-sheet fixture",
                "When both prefixes exist, the editor-owned Rainwright sheet always takes precedence.");
            Check(Read("NewOnly") == "Rainwright-only sprite-sheet fixture",
                "Standalone Rainwright builds can load their sprite sheets with no DryCycle resource.");
            Check(Read("LegacyOnly") == "Legacy-only sprite-sheet fixture",
                "Old DryCycle-named embedded sprite sheets remain readable through a bounded fallback.");
            Check(Read("Missing") == null, "Missing sheets report absence rather than silently returning unrelated mod assets.");
            bool rejected = false;
            try { CartographyResourceLocator.Open(Assembly.GetExecutingAssembly(), "../Objects"); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "Resource lookups refuse path-like or qualified sheet names.");
            Console.WriteLine("PASS: " + checks +
                " assertions; production embedded Cartography resource identity, fallback and isolation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
