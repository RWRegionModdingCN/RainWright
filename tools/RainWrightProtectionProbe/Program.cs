using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            string game = Path.GetFullPath(args[3]);
            AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
            {
                string name = new AssemblyName(request.Name).Name;
                foreach (string directory in new[] { "BepInEx/utils", "BepInEx/core", "RainWorld_Data/Managed", "../../workshop/content/312520/2920439169/plugins", "../../workshop/content/312520/2920439476/newest/plugins" })
                {
                    string file = Path.Combine(game, directory, name == "Assembly-CSharp" && directory == "BepInEx/utils" ? "PUBLIC-Assembly-CSharp.dll" : name + ".dll");
                    if (File.Exists(file)) return Assembly.LoadFrom(file);
                }
                return null;
            };
            Assembly.LoadFrom(Path.Combine(game, "BepInEx/utils/PUBLIC-Assembly-CSharp.dll"));
            // No game objects, engine initialization, author files or network access.
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var plan = XDocument.Load(args[1]).Root;
            var methods = plan.Elements("method").ToDictionary(e => (string)e.Attribute("id"),
                e => (MethodInfo)assembly.ManifestModule.ResolveMethod(Convert.ToInt32(((string)e.Attribute("token")).Substring(2), 16)));
            var results = new XElement("results");
            void Run(string id, Func<string> action)
            {
                try { results.Add(new XElement("case", new XAttribute("id", id), new XAttribute("result", "ok"), action())); }
                catch (Exception error)
                {
                    while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                    if (!(error is InvalidDataException || error is ArgumentException || error is FormatException || error is OverflowException))
                        throw new InvalidOperationException("Unexpected runtime failure in case " + id, error);
                    results.Add(new XElement("case", new XAttribute("id", id), new XAttribute("result", "exception"),
                        new XAttribute("type", error.GetType().FullName), error.Message));
                }
            }
            var tokens = new List<string> { null, "", " ", "AA", "DISCONNECTED", "disconnected", "AA<3>", "<3>AA", "<0>AA", "AA<-1>", "AA<bad>", "AA<3>tail", "<3>", "AA<3><4>", "<3>DISCONNECTED", " 房间<12> " };
            var random = new Random(104729);
            for (int i = 0; i < 500; i++)
            {
                string room = "R" + random.Next(1, 4000); int node = random.Next(-3, 100);
                tokens.Add(i % 3 == 0 ? "<" + node + ">" + room : i % 3 == 1 ? room + "<" + node + ">" : room);
            }
            int n = 0;
            foreach (string text in tokens)
            {
                int index = n++;
                Run("parse-" + index, () => { object[] values = { text, null, 0 }; var ok = methods["world.parse"].Invoke(null, values); return ok + "|" + values[1] + "|" + values[2]; });
                Run("normalize-" + index, () => (string)methods["world.normalize"].Invoke(null, new object[] { text }) ?? "<null>");
                Run("format-" + index, () => (string)methods["world.format"].Invoke(null, new object[] { text, index % 8 - 2 }));
            }
            foreach (string name in new[] { null, "", " ", ".", "..", "CON", "NUL.txt", "COM1", "LPT8", "map/name", "map\\name", "foo:", "foo.", "name.txt", "My背景", "正常背景.txt" })
                Run("filename-" + (name ?? "null"), () => (string)methods["background.file"].Invoke(null, new object[] { name }));
            foreach (string value in new[] { null, "", "0", "-0", "12.25", "-1.5", "1E5", "NaN", "Infinity", "1E20", "bad", "1,2", "中文" })
                Run("number-" + (value ?? "null"), () => ((float)methods["background.number"].Invoke(null, new object[] { value })).ToString("R", CultureInfo.InvariantCulture));
            foreach (string value in new[] { null, "", "normal", "多语言 😀", "bad\nline", "bad\rline", "bad\0line" })
                Run("validate-" + results.Elements().Count(), () => { methods["background.validate"].Invoke(null, new object[] { value }); return "accepted"; });
            foreach (string text in new[] { "Type: AboveCloudsView\n// comment\nUnknown: keep\n", "Type: RoofTopView\r\nParent: Sample\r\nBG_ElementGroup: City, 0, 0, 1\r\n- BG_SimpleElement: 背景, 1, 2, 3\r\n", "(X-Inv)OffsetX: 4\n" })
                Run("background-roundtrip-" + results.Elements().Count(), () =>
                {
                    var document = methods["background.parse"].Invoke(null, new object[] { text });
                    string result = (string)methods["background.serialize"].Invoke(document, null);
                    if (result != text) throw new Exception("Author text changed in a parse/serialize roundtrip.");
                    return result;
                });
            foreach (var values in new[] { new[] { "A: 1\n", "A: 2\n", "" }, new[] { "ROOMS\nAA : DISCONNECTED\nEND ROOMS\n", "ROOMS\nAA : BB<3>\nEND ROOMS\n", "" },
                new[] { "A\nB\n", "A\nC\nB\n", "// retained\n" }, new[] { "A\nB\n", "B\nA\n", "" } })
                Run("modify-" + results.Elements().Count(), () => (string)methods["modify.compose"].Invoke(null, values));
            foreach (var entry in plan.Elements("type"))
            {
                if (!((string)entry.Attribute("id")).StartsWith("Cartography", StringComparison.Ordinal)) continue;
                string name = (string)entry.Attribute("id");
                Run("xml-" + name, () =>
                {
                    Type type = assembly.ManifestModule.ResolveType(Convert.ToInt32(((string)entry.Attribute("token")).Substring(2), 16));
                    object record = Activator.CreateInstance(type, true);
                    var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                    var field = fields.FirstOrDefault(f => f.FieldType == typeof(string)) ?? fields.First(f => f.FieldType == typeof(float));
                    field.SetValue(record, field.FieldType == typeof(string) ? (object)"兼容 😀 sample" : 7.25f);
                    var encoded = (XElement)methods["record.write"].Invoke(null, new[] { "record", record });
                    object restored = Activator.CreateInstance(type, true);
                    methods["record.read"].Invoke(null, new[] { encoded, restored });
                    var roundtrip = (XElement)methods["record.write"].Invoke(null, new[] { "record", restored });
                    if (!XNode.DeepEquals(encoded, roundtrip)) throw new Exception("Cartography XML roundtrip changed author data.");
                    return encoded.ToString(SaveOptions.DisableFormatting);
                });
            }
            Run("POM-RegionKit-runtime-and-native-mode", () => InteropProbe.Run(assembly, plan, game));
            // XML 1.0 cannot represent arbitrary NULs in case IDs or exception messages.
            foreach (var element in results.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes().ToArray()) attribute.Value = Safe(attribute.Value);
                foreach (var text in element.Nodes().OfType<XText>().ToArray()) text.Value = Safe(text.Value);
            }
            new XDocument(results).Save(args[2]);
            Console.WriteLine("PASS: " + results.Elements().Count() + " production execution cases on " + (Type.GetType("Mono.Runtime") == null ? "CLR" : "Mono"));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    private static string Safe(string text) => new string(text.Select(c => c == '\0' ? '\u2400' : c).ToArray());
}
