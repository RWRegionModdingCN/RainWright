using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DryCycle.DevUI.DevTool.Persistence;

namespace DryCycle.DevUI.DevTool.Palette;

internal sealed class PaletteWorkspace
{
    internal PaletteDocument Document;
    internal string Directory = "", Filename = "palette.png", Identity = Guid.NewGuid().ToString("N");
    internal bool Edited;

    internal string Serialize() => new XElement("RainWrightPalette", new XAttribute("version", 1),
        new XAttribute("id", Identity), new XAttribute("edited", Edited),
        new XElement("Directory", Directory), new XElement("Filename", Filename),
        new XElement("Pixels", Document.ToHex())).ToString(SaveOptions.DisableFormatting);

    internal static PaletteWorkspace Parse(string text)
    {
        using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XElement.Load(reader);
        if (root.Name != "RainWrightPalette" || (string)root.Attribute("version") != "1") throw new InvalidDataException("Unsupported palette workspace.");
        string id = (string)root.Attribute("id");
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid palette workspace identity.");
        return new PaletteWorkspace { Document = PaletteDocument.FromHex((string)root.Element("Pixels")),
            Directory = (string)root.Element("Directory") ?? "", Filename = (string)root.Element("Filename") ?? "palette.png",
            Identity = id, Edited = (bool?)root.Attribute("edited") ?? true };
    }
}

internal static class PaletteStorage
{
    internal const int MaximumFileBytes = 1024 * 1024;

    internal static byte[] Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > MaximumFileBytes) throw new InvalidDataException("Palette file exceeds 1 MB.");
        var result = new byte[checked((int)input.Length)];
        int offset = 0;
        while (offset < result.Length) { int count = input.Read(result, offset, result.Length - offset); if (count == 0) throw new EndOfStreamException(); offset += count; }
        return result;
    }

    internal static string ExportPath(string directory, string filename)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory)) throw new ArgumentException("请选择导出文件夹。 / Choose an export folder.");
        if (string.IsNullOrWhiteSpace(filename) || filename != filename.Trim() || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            filename.IndexOfAny(new[] { '/', '\\', ':', '<', '>', '"', '|', '?', '*' }) >= 0 || filename.Any(char.IsControl) || filename.EndsWith(".", StringComparison.Ordinal))
            throw new ArgumentException("请输入有效的 PNG 文件名。 / Enter a valid PNG filename.");
        if (!filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) filename += ".png";
        string stem = Path.GetFileNameWithoutExtension(filename).Split('.')[0].TrimEnd(' ', '.').ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] >= '0' && stem[3] <= '9')
            throw new ArgumentException("This filename is reserved by Windows.");
        if (filename.Length > 200) throw new ArgumentException("Filename is too long.");
        return Path.Combine(Path.GetFullPath(directory), filename);
    }

    // expected == null means create only; callers must explicitly approve the exact existing bytes.
    internal static void Write(string path, byte[] bytes, byte[] expected)
    {
        if (bytes == null) throw new ArgumentNullException(nameof(bytes));
        string staged = path + ".pending-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { output.Write(bytes, 0, bytes.Length); output.Flush(true); }
            if (expected == null) File.Move(staged, path);
            else
            {
                if (!Read(path).SequenceEqual(expected)) throw new IOException("文件已被其他程序更改，请重新导出。 / File changed externally; export again.");
                EditorBackupFiles.Replace(staged, path, path + ".bak");
            }
        }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }

    internal static byte[] TextBytes(string value) => new UTF8Encoding(false, true).GetBytes(value);
}
