using System.Reflection;

internal static class ConnectionSyntaxProbe
{
    internal static int Run(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var syntax = assembly.GetType("DryCycle.DevUI.DevTool.World.WorldConnectionSyntax", true)!;
        object Call(string method, object[] values) => syntax.GetMethod(method, flags)!.Invoke(null, values);
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        Check((string)Call("FormatDestination", new object[] { "BB", 3 }) == "BB<3>", "Compiled game adapter must write community suffixes.");
        foreach (string token in new[] { "BB<3>", "<3>BB" })
        {
            object[] parse = { token, null, -1 };
            Check((bool)Call("TryParseDestination", parse) && (string)parse[1] == "BB" && (int)parse[2] == 3,
                "Compiled adapter must read old and community tokens with the same target endpoint.");
            object[] rewrite = { "PROBE", "AA : " + token + ", DISCONNECTED : SHELTER", null };
            bool changed = (bool)Call("TryRewriteRoomLine", rewrite);
            string[] parts = ((string)rewrite[2]).Split(" : ");
            Check(changed && parts.Length == 3 && parts[0] == "AA" && parts[2] == "SHELTER" &&
                  parts[1].Split(',').Select(token => token.Trim()).SequenceEqual(new[] { "BB", "DISCONNECTED" }),
                "Native loader must receive plain room names without losing other connection slots or tags.");
            object[] route = { "PROBE", "AA", 0, null };
            Check((bool)Call("TryGetLoadedRoute", route), "Stripping a token for native loading must retain its exact route.");
            object endpoint = route[3]!;
            var type = endpoint.GetType();
            Check((string)type.GetProperty("Room", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(endpoint) == "BB" &&
                  (int)type.GetProperty("NodeIndex", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(endpoint) == 3,
                "Recorded runtime route must still point to BB:3.");
        }
        Check((string)Call("FormatForSource", new object[] { "PROBE", "AA", 0, "<3>BB" }) == "BB<3>", "Old caller tokens must be canonicalized before source persistence.");
        Console.WriteLine($"PASS: {checks} compiled ConnectionExtensions write/read, native loader and exact-route checks.");
        return checks;
    }
}
