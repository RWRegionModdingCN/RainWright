using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BepInEx;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Extensions;
using DryCycle.DevUI.DevTool.History;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Palette;

internal sealed class PaletteSnapshot
{
    internal static readonly PaletteSnapshot Empty = new();
    internal bool Available, Edited, DraftDirty;
    internal bool Listing;
    internal long Revision;
    internal string Directory = "", Filename = "palette.png", Error = "", Status = "", ConfirmPath = "";
    internal int[] Pixels = Array.Empty<int>();
    internal int[] SourcePixels = Array.Empty<int>();
    internal string SourceLabel = "";
    internal string[] Files = Array.Empty<string>();
}

internal sealed class PaletteCommand
{
    internal string Kind, Value;
    internal long Revision;
    internal int X, Y, Color, Other;
    internal int[] Targets;
    internal float Start = 1, End = 1;
    internal bool Reverse;
    internal Exception Failure;
}

// All image decoding, author mutations and disk access run on the game thread at command boundaries.
// The frontend receives detached snapshots; no file probing or catalogue discovery occurs per frame.
internal static class PaletteEditorRuntime
{
    private static readonly ConcurrentQueue<PaletteCommand> commands = new();
    private static PaletteSnapshot current = PaletteSnapshot.Empty;
    private static PaletteWorkspace workspace;
    private static PaletteDocument sourcePalette;
    private static string sourceLabel = "";
    private static byte[] savedDraft, pendingOverwrite;
    private static string pendingPath = "", error = "", status = "";
    private static bool initialized, dirty;
    private static long revision;
    private static int queued, rejected;
    private static IDisposable registration;
    private static string[] files = Array.Empty<string>();
    private static readonly PaletteFileListing listing = new();
    private static bool listingPending;
    internal static PaletteSnapshot Current => Volatile.Read(ref current);
    internal static string DraftPath => Path.Combine(Paths.ConfigPath, "RainWright.Palette.xml");

    internal static void Enqueue(PaletteCommand command)
    {
        if (Interlocked.Increment(ref queued) > 64) { Interlocked.Decrement(ref queued); Interlocked.Exchange(ref rejected, 1); return; }
        commands.Enqueue(command);
    }

    internal static void Process(EditorSession session)
    {
        bool active = session?.ToolMode == EditorToolMode.Palette;
        if (active && !initialized) Initialize();
        if (listing.TryTake(out var listed))
        {
            listingPending = false;
            files = listed.Files;
            if (listed.Error != null) { error = listed.Error.Message; EditorDiagnostics.Warning("Palette folder listing failed: " + listed.Error); }
            else if (listed.Truncated) status = "仅列出前 1024 个 PNG；也可直接输入文件名。 / Listing limited to 1024 PNGs; you can enter a filename.";
            Publish();
        }
        long batchRevision = revision;
        for (int count = 0; count < 32 && commands.TryDequeue(out var command); count++)
        {
            Interlocked.Decrement(ref queued);
            if (!active && command.Kind is not ("pixel" or "stroke")) continue;
            try
            {
                if (command.Kind == "retry") { initialized = false; Initialize(); continue; }
                if (workspace == null) throw new InvalidOperationException("色板草稿未加载。 / Palette workspace is unavailable.");
                if (command.Revision != revision && command.Revision != batchRevision && command.Kind is not ("filename" or "directory" or "export" or "import" or "cancel" or "save" or "folder-error"))
                    throw new InvalidOperationException("色板已更新，请重试。 / Palette changed; please retry.");
                Execute(session, command);
            }
            catch (Exception failure) { error = failure.Message; EditorDiagnostics.Warning("Palette command failed: " + failure); }
            Publish();
        }
        if (active && Interlocked.Exchange(ref rejected, 0) != 0) { error = "色板编辑队列已满，请重试。 / Palette edit queue is full; retry."; Publish(); }
    }

    private static void Initialize()
    {
        initialized = true;
        try
        {
            // A corrupt or externally changed draft is authoritative: never replace it with defaults.
            if (workspace == null)
            {
                if (File.Exists(DraftPath)) { savedDraft = PaletteStorage.Read(DraftPath); workspace = PaletteWorkspace.Parse(new UTF8Encoding(false, true).GetString(savedDraft)); }
                else { workspace = new PaletteWorkspace { Document = LoadPng(AssetManager.ResolveFilePath("palettes/palette0.png")) }; dirty = true; }
            }
            registration ??= EditorAuthoringParticipants.Register("RainWright.Palette", () => dirty, SaveDraft,
                (out string path, out string text) => { path = DraftPath; text = workspace?.Serialize(); return workspace != null; },
                (step, exception) => EditorDiagnostics.Warning("Palette " + step + ": " + exception));
            if (dirty && !SaveDraft()) return;
            RefreshFiles();
            error = "";
        }
        catch (Exception failure) { error = failure.Message; EditorDiagnostics.Warning("Palette initialization failed: " + failure); }
        Publish();
    }

    private static PaletteDocument LoadPng(string path)
    {
        byte[] bytes = PaletteStorage.Read(path);
        PaletteDocument.ValidatePngHeader(bytes); // Check dimensions before the decoder can allocate.
        var texture = new Texture2D(PaletteDocument.Width, PaletteDocument.Height, TextureFormat.RGBA32, false);
        try
        {
            if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidDataException("PNG 解码失败。 / PNG decoding failed.");
            if (texture.width != PaletteDocument.Width || texture.height != PaletteDocument.Height) throw new InvalidDataException("Unexpected palette dimensions.");
            var source = texture.GetPixels32(); var pixels = new int[PaletteDocument.Count];
            for (int y = 0; y < PaletteDocument.Height; y++)
                for (int x = 0; x < PaletteDocument.Width; x++)
                {
                    Color32 c = source[(PaletteDocument.Height - 1 - y) * PaletteDocument.Width + x];
                    pixels[y * PaletteDocument.Width + x] = unchecked((int)((uint)c.a << 24 | (uint)c.r << 16 | (uint)c.g << 8 | c.b));
                }
            return new PaletteDocument(pixels);
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }

    private static void Execute(EditorSession session, PaletteCommand command)
    {
        if (AutoSave.RecoverySnapshotStore.RestoreRequiresReload)
            throw new InvalidOperationException("恢复完成后，请先重启游戏再编辑。 / Restart after recovery before editing.");
        error = ""; status = "";
        if (command.Kind == "folder-error") throw command.Failure ?? new IOException(command.Value);
        if (command.Kind == "source-current" || command.Kind == "source-file" || command.Kind == "source-palette")
        {
            if (command.Kind == "source-current") { sourcePalette = workspace.Document.Clone(); sourceLabel = workspace.Filename; }
            else if (command.Kind == "source-file") { sourcePalette = LoadPng(PaletteStorage.ExportPath(workspace.Directory, command.Value)); sourceLabel = command.Value; }
            else
            {
                if (command.X < 0 || command.X > 999999) throw new ArgumentOutOfRangeException("palette");
                string relative = "palettes/palette" + command.X + ".png";
                sourcePalette = LoadPng(AssetManager.ResolveFilePath(relative)); sourceLabel = "palette" + command.X + ".png";
            }
            return;
        }
        if (command.Kind == "export" || command.Kind == "overwrite")
        {
            if (command.Kind == "export" && command.Value != null && workspace.Filename != command.Value)
            { workspace.Filename = command.Value; dirty = true; if (!SaveDraft()) return; }
            Export(command.Kind == "overwrite"); return;
        }
        if (command.Kind == "cancel") { pendingPath = ""; pendingOverwrite = null; return; }
        if (command.Kind == "refresh") { RefreshFiles(); return; }
        if (command.Kind == "save") { SaveDraft(); return; }
        if (command.Kind is "undo" or "redo")
        {
            if (dirty && !SaveDraft()) return;
            if (command.Kind == "undo") Commands.EditorActions.Undo(session); else Commands.EditorActions.Redo(session);
            return;
        }
        if (command.Kind == "directory" || command.Kind == "filename")
        {
            if (command.Kind == "directory")
            {
                string selected = Path.GetFullPath(command.Value);
                workspace.Directory = selected;
            }
            else workspace.Filename = command.Value ?? "";
            dirty = true; pendingPath = ""; pendingOverwrite = null; SaveDraft();
            if (command.Kind == "directory") RefreshFiles();
            return;
        }
        // Preserve the old durable draft before replacing its pixels with another source.
        if (dirty && !SaveDraft()) return;
        PaletteDocument before = workspace.Document.Clone(), after = before.Clone();
        bool beforeEdited = workspace.Edited;
        switch (command.Kind)
        {
            case "pixel": after.Set(command.X, command.Y, command.Color); break;
            case "row": after.FillRow(command.Y, command.Color); break;
            case "gradient": after.Gradient(command.Y, command.Color, command.Other); break;
            case "stroke": after = PaletteDocument.FromHex(command.Value); break;
            case "stops": PaletteEditing.Gradient(after, command.Y, PaletteEditing.ParseStops(command.Value)); break;
            case "blend":
                if (sourcePalette == null) throw new InvalidOperationException("请先选择源色板。 / Choose a source palette first.");
                if (command.Targets == null || command.Targets.Length == 0 || command.Targets.Length > PaletteChannels.All.Length) throw new ArgumentException("Invalid target channels.");
                foreach (int target in command.Targets) PaletteEditing.MapChannel(after, sourcePalette, command.X, target, (PaletteBlendMode)command.Color, command.Start, command.End, command.Reverse);
                break;
            case "cycle": after.CopyCycle(command.X != 0); break;
            case "new": after = LoadPng(AssetManager.ResolveFilePath("palettes/palette0.png")); break;
            case "room": after = LoadPng(AssetManager.ResolveFilePath("palettes/palette" + (session.RoomSettings?.Palette ?? 0) + ".png")); break;
            case "import":
                after = LoadPng(PaletteStorage.ExportPath(workspace.Directory, command.Value));
                workspace.Filename = command.Value; dirty = true;
                break;
            default: throw new ArgumentException("Unknown palette command: " + command.Kind);
        }
        if (after.ToHex() == before.ToHex()) { if (dirty) SaveDraft(); return; }
        Apply(after, true);
        if (session.ToolMode == EditorToolMode.Palette) session.History.Push(new DelegateHistoryEntry("Palette " + command.Kind,
            s => s.ToolMode == EditorToolMode.Palette && Apply(before, beforeEdited),
            s => s.ToolMode == EditorToolMode.Palette && Apply(after, true)));
    }

    private static bool Apply(PaletteDocument document, bool edited)
    {
        workspace.Document = document.Clone(); workspace.Edited = edited;
        dirty = true; revision++; pendingPath = ""; pendingOverwrite = null; status = "";
        SaveDraft(); Publish();
        return true; // The history mutation succeeded even if persistence is still pending.
    }

    internal static bool SaveDraft()
    {
        if (AutoSave.RecoverySnapshotStore.RestoreRequiresReload) return false;
        if (workspace == null) return false;
        if (!dirty) return true;
        try
        {
            Directory.CreateDirectory(Paths.ConfigPath);
            byte[] bytes = PaletteStorage.TextBytes(workspace.Serialize());
            PaletteStorage.Write(DraftPath, bytes, savedDraft);
            savedDraft = bytes; dirty = false; error = ""; Publish(); return true;
        }
        catch (Exception failure) { error = "草稿保存失败： / Draft save failed: " + failure.Message; EditorDiagnostics.Warning("Palette draft save failed: " + failure); Publish(); return false; }
    }

    private static void Export(bool confirmed)
    {
        string path = PaletteStorage.ExportPath(workspace.Directory, workspace.Filename);
        byte[] expected = null;
        if (confirmed)
        {
            if (pendingPath != path || pendingOverwrite == null) throw new InvalidOperationException("请重新确认导出。 / Confirm export again.");
            expected = pendingOverwrite;
        }
        else if (File.Exists(path))
        { pendingOverwrite = PaletteStorage.Read(path); pendingPath = path; return; }
        PaletteStorage.Write(path, workspace.Document.EncodePng(), expected);
        pendingPath = ""; pendingOverwrite = null; workspace.Edited = false; dirty = true;
        if (SaveDraft()) status = "已导出 / Exported: " + path;
        RefreshFiles();
    }

    private static void RefreshFiles()
    {
        files = Array.Empty<string>();
        listingPending = true;
        listing.Refresh(workspace?.Directory);
    }

    private static void Publish()
    {
        Volatile.Write(ref current, new PaletteSnapshot { Available = workspace != null, Revision = revision,
            Pixels = workspace?.Document.CopyPixels() ?? Array.Empty<int>(), Directory = workspace?.Directory ?? "", Filename = workspace?.Filename ?? "palette.png",
            SourcePixels = sourcePalette?.CopyPixels() ?? Array.Empty<int>(), SourceLabel = sourceLabel,
            Edited = workspace?.Edited ?? false, DraftDirty = dirty, Listing = listingPending, Error = error, Status = status, ConfirmPath = pendingPath, Files = files });
    }
}
