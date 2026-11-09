using System.IO.Compression;
using System.Text;
using DryCycle.DevUI.DevTool.Palette;

int assertions = 0;
void Check(bool ok, string reason) { assertions++; if (!ok) throw new Exception(reason); }
void Reject(Action action, string reason) { try { action(); } catch { assertions++; return; } throw new Exception(reason); }
int[] source = Enumerable.Range(0, 512).Select(i => unchecked((int)((uint)(i * 73 % 256) << 24 | (uint)(i % 256) << 16 | (uint)(i * 7 % 256) << 8 | (uint)(i * 19 % 256)))).ToArray();
var doc = new PaletteDocument(source);
source[0] = 123;
Check(doc.Get(0, 0) == 0, "Input pixels must be detached.");
Check(PaletteDocument.FromHex(doc.ToHex()).ToHex() == doc.ToHex(), "Draft must preserve all RGBA including transparent RGB.");
Reject(() => PaletteDocument.FromHex("FF"), "Malformed drafts must fail.");
Reject(() => new PaletteDocument(new int[1]), "Wrong pixel count must fail.");
Reject(() => doc.Set(32, 0, 0), "Out-of-range edits must fail.");
Reject(() => doc.Get(0, -1), "Negative coordinates must fail.");
var copied = doc.Clone(); copied.Set(0, 0, -1); Check(doc.Get(0, 0) == 0, "Snapshots and history must be detached.");
var rain = doc.Clone(); rain.CopyCycle(false);
for (int y = 0; y < 8; y++) for (int x = 0; x < 32; x++) Check(rain.Get(x, y + 8) == doc.Get(x, y), "Normal-to-rain preserves row order.");
rain = doc.Clone(); rain.CopyCycle(true);
for (int y = 0; y < 8; y++) Check(rain.Get(5, y) == doc.Get(5, y + 8), "Rain-to-normal preserves row order.");
var gradient = doc.Clone(); gradient.Gradient(4, unchecked((int)0xff000000), -1);
Check(gradient.Get(0, 4) == unchecked((int)0xff000000) && gradient.Get(29, 4) == -1, "Gradient endpoints must be exact.");
Check(gradient.Get(30, 4) == doc.Get(30, 4) && gradient.Get(31, 4) == doc.Get(31, 4), "Gradient preserves auxiliary columns.");
Check(gradient.Get(0, 3) == doc.Get(0, 3), "Gradient leaves other rows intact.");
var png = doc.EncodePng(); PaletteDocument.ValidatePngHeader(png);
Check(PaletteChannels.All.Length == 44 && PaletteChannels.Get(0).Y == 0 && PaletteChannels.Get(22).Y == 8, "Named channels use PNG top-left coordinates for both cycles.");
Check(PaletteChannels.At(30, 0) == 14 && PaletteChannels.At(29, 2) == 16 && PaletteChannels.At(31, 7) == -1, "Parameters, depth colors and reserved auxiliary cells are distinguished.");
var segmented = doc.Clone();
PaletteEditing.Gradient(segmented, 3, new[] { new PaletteGradientStop(2, unchecked((int)0xff000000)), new PaletteGradientStop(8, unchecked((int)0xffff0000)), new PaletteGradientStop(27, -1) });
Check(segmented.Get(2, 3) == unchecked((int)0xff000000) && segmented.Get(8, 3) == unchecked((int)0xffff0000) && segmented.Get(27, 3) == -1, "Multi-stop gradients preserve each exact authored stop.");
Check(segmented.Get(0, 3) == doc.Get(0, 3) && segmented.Get(28, 3) == doc.Get(28, 3) && segmented.Get(30, 3) == doc.Get(30, 3), "Multi-stop gradients touch only the selected depth interval.");
Reject(() => PaletteEditing.Gradient(segmented, 3, new[] { new PaletteGradientStop(8, 0), new PaletteGradientStop(8, -1) }), "Duplicate stops cannot divide by zero.");
Reject(() => PaletteEditing.ParseStops("0:FFFF;999999999999999999:FF"), "Malformed stop metadata cannot overflow.");
int gray = unchecked((int)0xff808080);
Check(PaletteEditing.Blend(gray, gray, PaletteBlendMode.Multiply, 1) == unchecked((int)0xff404040), "Multiply has independently known channel values.");
Check(PaletteEditing.Blend(gray, gray, PaletteBlendMode.Screen, 1) == unchecked((int)0xffc0c0c0), "Screen has independently known channel values.");
foreach (PaletteBlendMode mode in Enum.GetValues<PaletteBlendMode>()) Check(PaletteEditing.Blend(gray, -1, mode, 0) == gray, "Zero influence leaves source bytes intact.");
Reject(() => PaletteEditing.Blend(gray, -1, PaletteBlendMode.Normal, float.NaN), "Nonfinite blend strength is rejected.");
var mapped = doc.Clone(); var paletteSource = doc.Clone(); paletteSource.Set(0, 0, -1);
PaletteEditing.MapChannel(mapped, paletteSource, 0, 16, PaletteBlendMode.Normal, 1, 0, false);
Check(mapped.Get(0, 2) == -1 && mapped.Get(29, 2) == doc.Get(29, 2) && mapped.Get(30, 2) == doc.Get(30, 2), "Single-color mapping supports graded influence without touching auxiliary cells.");
Check(mapped.Get(0, 0) == doc.Get(0, 0) && paletteSource.Get(1, 2) == doc.Get(1, 2), "Channel blending cannot mutate the source or unrelated target channels.");
var stroke = new PaletteDocument(Enumerable.Repeat(gray, 512).ToArray()); var visited = new bool[512];
PaletteEditing.Paint(stroke, 0, 4, 29, 4, 0, -1, PaletteBlendMode.Normal, .5f, visited);
for (int x = 0; x < 30; x++) Check(stroke.Get(x, 4) == unchecked((int)0xffc0c0c0), "Fast pointer movement paints every crossed cell exactly once.");
PaletteEditing.Paint(stroke, 29, 4, 0, 4, 0, -1, PaletteBlendMode.Normal, .5f, visited);
Check(stroke.Get(4, 4) == unchecked((int)0xffc0c0c0), "A single gesture cannot accumulate opacity again on revisiting cells.");
PaletteEditing.Paint(stroke, 28, 7, 29, 7, 6, -1, PaletteBlendMode.Normal, 1, new bool[512]);
Check(stroke.Get(29, 8) == gray && stroke.Get(30, 7) == gray && stroke.Get(28, 0) == gray, "Large brushes preserve the other cycle, special parameters and auxiliary depth columns.");
Reject(() => PaletteEditing.Paint(stroke, 1, 7, 1, 8, 0, -1, PaletteBlendMode.Normal, 1, new bool[512]), "A stroke cannot cross cycle boundaries.");
Reject(() => PaletteDocument.ValidatePngHeader(new byte[33]), "Non-PNG inputs must fail before decoding.");
var wrong = (byte[])png.Clone(); wrong[19] = 255; Reject(() => PaletteDocument.ValidatePngHeader(wrong), "Wrong dimensions must fail before decoder allocation.");
// Independent zlib consumer checks the actual emitted PNG bytes and top-to-bottom scanline order.
using var compressed = new MemoryStream();
for (int offset = 8; offset < png.Length;)
{
    int count = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4)));
    string kind = Encoding.ASCII.GetString(png, offset + 4, 4);
    if (kind == "IDAT") compressed.Write(png, offset + 8, count);
    uint crc = uint.MaxValue;
    for (int i = offset + 4; i < offset + 8 + count; i++)
    { crc ^= png[i]; for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320 : 0); }
    Check((crc ^ uint.MaxValue) == System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + count, 4)), "Every PNG chunk CRC must validate.");
    offset += count + 12;
}
compressed.Position = 0;
using var decoded = new MemoryStream(); using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress, true)) zlib.CopyTo(decoded);
var raw = decoded.ToArray(); Check(raw.Length == 16 * 129, "Expected exactly 16 RGBA scanlines.");
for (int y = 0; y < 16; y++)
{
    Check(raw[y * 129] == 0, "PNG row filter must be None.");
    for (int x = 0; x < 32; x++)
    {
        int i = y * 129 + 1 + x * 4;
        uint pixel = (uint)raw[i + 3] << 24 | (uint)raw[i] << 16 | (uint)raw[i + 1] << 8 | raw[i + 2];
        Check(pixel == unchecked((uint)doc.Get(x, y)), "Pixel coordinates, channels and transparent RGB must survive PNG export.");
    }
}
string folder = Path.Combine(Path.GetTempPath(), "RainWright-Palette-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string path = PaletteStorage.ExportPath(folder, "1232131.png");
    Check(Path.GetFileName(path) == "1232131.png", "Custom PNG filenames must remain unchanged.");
    Check(PaletteStorage.ExportPath(folder, "abc").EndsWith("abc.png"), "Missing PNG extension is added.");
    foreach (string bad in new[] { "../escape", "x/y.png", "x\\y.png", "CON.png", "con.foo.png", "LPT1.png", "x?.png", "trailing.", " " }) Reject(() => PaletteStorage.ExportPath(folder, bad), "Reject invalid filename: " + bad);
    var workspace = new PaletteWorkspace { Directory = folder, Filename = "1232131.png", Document = doc, Edited = true };
    var loaded = PaletteWorkspace.Parse(workspace.Serialize());
    var listing = new PaletteFileListing();
    string latestFolder = Path.Combine(folder, "latest"); Directory.CreateDirectory(latestFolder);
    File.WriteAllText(Path.Combine(latestFolder, "latest.png"), "listing only");
    for (int request = 0; request < 1000; request++) listing.Refresh(request % 2 == 0 ? folder : latestFolder);
    PaletteFileListing.Result listed = null;
    var deadline = System.Diagnostics.Stopwatch.StartNew();
    while (!listing.TryTake(out listed) && deadline.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(1);
    Check(listed?.Directory == latestFolder && listed.Error == null && listed.Files.SequenceEqual(new[] { "latest.png" }), "Asynchronous listing coalesces refreshes and publishes only the newest selected folder.");
    listing.Refresh(Path.Combine(folder, "missing")); listed = null; deadline.Restart();
    while (!listing.TryTake(out listed) && deadline.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(1);
    Check(listed?.Error is DirectoryNotFoundException, "Directory listing preserves the actual IO exception without blocking the editor.");
    Directory.Delete(latestFolder, true);
    Check(loaded.Directory == folder && loaded.Filename == workspace.Filename && loaded.Identity == workspace.Identity && loaded.Document.ToHex() == doc.ToHex() && loaded.Edited, "Settings and unsaved export work must survive restart.");
    Reject(() => PaletteWorkspace.Parse(workspace.Serialize().Replace("version=\"1\"", "version=\"99\"")), "Unsupported draft schema is protected.");
    Reject(() => PaletteWorkspace.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///secret'>]><RainWrightPalette>&e;</RainWrightPalette>"), "Draft loading must refuse DTDs.");
    PaletteStorage.Write(path, png, null);
    Reject(() => PaletteStorage.Write(path, new byte[] { 1 }, null), "Existing authors' files require explicit overwrite.");
    Check(File.ReadAllBytes(path).SequenceEqual(png), "Refused overwrite preserves source.");
    Reject(() => PaletteStorage.Write(path, new byte[] { 2 }, new byte[] { 3 }), "External changes invalidate approval.");
    Check(File.ReadAllBytes(path).SequenceEqual(png), "External-change rejection preserves source.");
    byte[] replacement = copied.EncodePng(); PaletteStorage.Write(path, replacement, png);
    Check(File.ReadAllBytes(path).SequenceEqual(replacement) && File.ReadAllBytes(path + ".bak").SequenceEqual(png), "Atomic replacement preserves original in backup.");
    if (OperatingSystem.IsWindows()) Check((File.GetAttributes(path + ".bak") & FileAttributes.Hidden) != 0, "Backup must remain hidden on Windows.");
    Check(Directory.GetFiles(folder, "*.tmp").Length == 0, "Failed and successful transactions clean staging files.");
    Reject(() => PaletteStorage.Write(Path.Combine(folder, "missing", "palette.png"), png, null), "Missing destination folder must report failure.");
}
finally
{
    foreach (var file in Directory.EnumerateFiles(folder)) File.SetAttributes(file, FileAttributes.Normal);
    Directory.Delete(folder, true);
}
Console.WriteLine($"PASS {assertions} palette model, PNG, settings and persistence assertions.");
