using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Map.Cartography;

internal static partial class Program
{
    private static void CartographyProjectOwnerMigration()
    {
        const string identity = "legacy-migration|Yellow";
        const string region = "SU";
        string owned = CartographyProjectPaths.Owned(BepInEx.Paths.ConfigPath, identity, region);
        string legacy = CartographyProjectPaths.Legacy(BepInEx.Paths.ConfigPath, identity, region);
        Check(owned.Contains(Path.Combine("Rainwright", "Cartography")) &&
              legacy.Contains(Path.Combine("DryCycle", "Cartography")) &&
              owned != legacy, "Cartography's default author project now belongs to Rainwright.");

        Directory.CreateDirectory(Path.GetDirectoryName(legacy));
        File.WriteAllText(legacy, "legacy-user-content");
        Check(CartographyProjectPaths.ResolveReadable(owned, legacy) == legacy &&
              !File.Exists(owned), "Missing Rainwright project reads a legacy source without creating a new file.");

        Directory.CreateDirectory(Path.GetDirectoryName(owned));
        File.WriteAllText(owned, "current-editor-content");
        Check(CartographyProjectPaths.ResolveReadable(owned, legacy) == owned &&
              File.ReadAllText(legacy) == "legacy-user-content",
            "A Rainwright project overrides legacy content without modifying the older source.");

        File.WriteAllText(owned, "invalid-new-owned-file");
        Check(CartographyProjectPaths.ResolveReadable(owned, legacy) == owned,
            "A present owned project remains authoritative even when invalid; stale legacy fallback is forbidden.");
        File.Delete(owned);
        Check(CartographyProjectPaths.ResolveReadable(owned, legacy) == legacy,
            "Deleting the owned project re-exposes the read-only legacy source.");
        Check(CartographyProjectPaths.IsLegacyDefaultPath(
                  BepInEx.Paths.ConfigPath, legacy) &&
              !CartographyProjectPaths.IsLegacyDefaultPath(
                  BepInEx.Paths.ConfigPath, owned),
            "Only historical Cartography author paths receive read-only import behavior.");
    }

    private static void RuntimeLoading()
    {
        string root = Path.Combine(output, "runtime-" + Guid.NewGuid().ToString("N")), assets = Path.Combine(root, "StreamingAssets");
        RWCustom.Custom.Root = assets;
        BepInEx.Paths.ConfigPath = Path.Combine(root, "config");
        CartographyProjectOwnerMigration();
        foreach (string code in new[] { "B5", "CC", "SU", "HI", "DS", "GW" })
        {
            string folder = Path.Combine(assets, "world", code.ToLowerInvariant()); Directory.CreateDirectory(folder);
            string roomName = code + "_A01";
            File.WriteAllText(Path.Combine(folder, "world_" + code.ToLowerInvariant() + ".txt"), "ROOMS\n" + roomName + " : DISCONNECTED\nEND ROOMS\n");
            File.WriteAllText(Path.Combine(folder, "map_" + code.ToLowerInvariant() + ".txt"), roomName + ": 0><0><20><0><0><\n");
            File.WriteAllText(Path.Combine(folder, "displayname.txt"), "区域全称 " + code);
            string[] room = Enumerable.Repeat("", 12).ToArray(); room[0] = roomName; room[1] = "2*2|-1|0"; room[11] = "0|1|0|1|";
            File.WriteAllLines(Path.Combine(folder, roomName.ToLowerInvariant() + ".txt"), room);
        }
        var session = new EditorSession { World = new World { name = "B5" } };
        File.WriteAllText(Path.Combine(assets, "world", "cc", "displayname.txt"), "Chimney Canopy");
        session.World.game.rainWorld.inGameTranslator.Translations.Add("Chimney Canopy", "烟囱天棚");
        void Finish()
        {
            Stopwatch timeout = Stopwatch.StartNew();
            do
            {
                CartographyRuntime.Process(session);
                if (!CartographyRuntime.LoadingSource) break;
                Thread.Sleep(5);
            } while (timeout.Elapsed.TotalSeconds < 20);
            Check(!CartographyRuntime.LoadingSource && !CartographyRuntime.SourcePicker.Failed, "Runtime source preparation completes: " + CartographyRuntime.SourcePicker.Status);
        }
        void Select(string region, bool refresh = false) => CartographyRuntime.Enqueue(new CartographyCommand { Kind = CartographyCommandKind.SelectRegion, LayerId = region, RefreshSource = refresh });
        CartographyRuntime.SetActive(true); Finish();
        Check(CartographyRuntime.Presentation.Document.Region == "B5" && CartographyRuntime.SourcePicker.Campaign == "Yellow", "First open uses the player's region and campaign, never CC/White defaults.");
        Check(CartographyRuntime.SourcePicker.Regions.Length == 6, "The selector is populated from available world files.");
        Check(CartographyRuntime.SourcePicker.Regions.Single(region => region.Code == "CC").Label(true) == "CC · 烟囱天棚", "Localized region labels use the game's translator.");
        Check(CartographyRuntime.SourcePicker.Regions.Single(region => region.Code == "B5").Label(true) == "B5 · 区域全称 B5", "Custom Chinese names remain intact when no translation exists.");
        var first = CartographyRuntime.Presentation;
        var firstScene = first.Scene;
        CartographyRuntime.Enqueue(new CartographyCommand { Kind = CartographyCommandKind.Move, DocumentId = first.Identity,
            Revision = first.Revision, Ids = new[] { "room:B5_A01" }, X = 50 });
        CartographyRuntime.Process(session);
        var edited = CartographyRuntime.Presentation;
        Check(edited.Dirty && edited.Document.Items[0].X == first.Document.Items[0].X + 50, "The runtime records an unsaved author edit.");
        Select("CC"); Finish(); Select("B5"); Finish();
        Check(CartographyRuntime.Presentation.Document.Items[0].X == edited.Document.Items[0].X && CartographyRuntime.Presentation.Dirty, "Switching away/back retains unsaved edits.");
        Check(ReferenceEquals(CartographyRuntime.Presentation.Scene, edited.Scene), "Warm region switches retain prepared raster scenes.");
        Check(CartographyRuntime.HistoryFor(session).Undo(session), "Region switching retains the real undo history.");
        Check(CartographyRuntime.Presentation.Document.Items[0].X == first.Document.Items[0].X, "Undo after a region switch restores author position.");

        Select("CC"); CartographyRuntime.Process(session); // Queue first load.
        Select("SU"); Select("HI"); Finish();
        Check(CartographyRuntime.Presentation.Document.Region == "HI", "Rapid switching publishes only the final requested region.");
        Select("missing"); CartographyRuntime.Process(session);
        Check(CartographyRuntime.SourcePicker.Failed && CartographyRuntime.Presentation.Document.Region == "HI", "A failed load leaves the last valid workspace visible.");
        Check(EditorDiagnostics.Errors.Any(error => error.Contains("FileNotFoundException")),
            "The original region load exception is logged by Rainwright diagnostics.");
        CartographyRuntime.SetActive(false); CartographyRuntime.SetActive(true); Finish();
        Check(CartographyRuntime.Presentation.Document.Region == "B5", "Reopening returns to the player's region after browsing elsewhere.");
        foreach (string code in new[] { "CC", "SU", "HI", "DS", "GW" }) { Select(code); Finish(); }
        Select("B5"); Finish();
        Check(CartographyRuntime.HistoryFor(session).Redo(session), "Evicting derived previews retains the author's redo history.");
        Check(CartographyRuntime.Presentation.Document.Items[0].X == edited.Document.Items[0].X, "Rebuilt source preserves authored positions after eviction.");

        // Regression: Ctrl+S must be a complete authoring-state barrier. A style draft can still be
        // focused/staged while an unrelated room move advances the document revision; save must
        // preserve both the layout and subregion palette instead of rejecting the stale style draft
        // or serializing the older document.
        var saveBase = CartographyRuntime.Presentation;
        var saveStyle = saveBase.Document.Clone();
        saveStyle.Palettes.RemoveAll(palette => palette.Name == "__CtrlSSaveProbe");
        saveStyle.Palettes.Add(new CartographyPalette
        {
            Name = "__CtrlSSaveProbe",
            Background = 0xFF123456,
            Wall = 0xFF0A0B0C,
            Water = 0xFF654321,
            Visible = true
        });
        CartographyRuntime.StageDraft(
            saveBase.Identity + "/style",
            new CartographyCommand
            {
                DocumentId = saveBase.Identity,
                Revision = saveBase.Revision,
                Kind = CartographyCommandKind.Style,
                Style = saveStyle
            });
        CartographyRuntime.Enqueue(
            new CartographyCommand
            {
                DocumentId = saveBase.Identity,
                Revision = saveBase.Revision,
                Kind = CartographyCommandKind.Move,
                Ids = new[] { "room:B5_A01" },
                X = 17
            });
        CartographyRuntime.Process(session);
        var preSave = CartographyRuntime.Presentation;
        Check(preSave.ProjectPath.Contains(Path.Combine("Rainwright", "Cartography")),
            "Ctrl+S persists the runtime Cartography document under the editor-owned path.");
        bool saveBarrierCalled = false;
        CartographyRuntime.SetFrontendSaveBarrier(() => saveBarrierCalled = true);
        Check(CartographyRuntime.SaveActive(session), "Ctrl+S save commits queued layout and staged style state before writing.");
        CartographyRuntime.SetFrontendSaveBarrier(null);
        Check(saveBarrierCalled, "Ctrl+S invokes the Cartography frontend save barrier for still-focused UI drafts.");
        string savedXml = File.ReadAllText(preSave.ProjectPath);
        CartographyDocument savedDocument = CartographyStorage.Deserialize(savedXml, preSave.Identity);
        Check(
            savedDocument.Items.Find(item => item.Id == "room:B5_A01").X ==
            preSave.Document.Items.Find(item => item.Id == "room:B5_A01").X,
            "Ctrl+S persists the current Cartography room layout.");
        CartographyPalette savedPalette = savedDocument.Palettes.Find(palette => palette.Name == "__CtrlSSaveProbe");
        Check(
            savedPalette != null &&
            savedPalette.Background == 0xFF123456 &&
            savedPalette.Wall == 0xFF0A0B0C &&
            savedPalette.Water == 0xFF654321,
            "Ctrl+S persists subregion palette colors.");

        // Manually reopening a historical project must not convert Ctrl+S
        // into an accidental write to the game Mod's old author directory.
        string oldProject = CartographyProjectPaths.Legacy(
            BepInEx.Paths.ConfigPath, preSave.Identity, preSave.Document.Region);
        Directory.CreateDirectory(Path.GetDirectoryName(oldProject));
        File.WriteAllText(oldProject, savedXml);
        CartographyRuntime.Enqueue(new CartographyCommand
        {
            Kind = CartographyCommandKind.Open,
            DocumentId = preSave.Identity,
            Revision = CartographyRuntime.Presentation.Revision,
            Path = oldProject
        });
        CartographyRuntime.Process(session);
        Check(CartographyRuntime.Presentation.ProjectPath == preSave.ProjectPath &&
              File.ReadAllText(oldProject) == savedXml,
            "Open legacy Cartography project keeps ordinary Save bound to the owned Rainwright file.");
        Check(!CartographyRuntime.SaveActive(session) &&
              File.ReadAllText(preSave.ProjectPath) == savedXml &&
              File.ReadAllText(oldProject) == savedXml,
            "Legacy Open cannot overwrite a pre-existing Rainwright project, and keeps both author files intact.");

        Select("B5", true); CartographyRuntime.Process(session);
        Check(CartographyRuntime.SaveActive(session), "Saving while a refresh is pending succeeds.");
        Finish();
        Check(!CartographyRuntime.Presentation.Dirty, "Background refresh does not overwrite the save baseline.");
        session.World = new World { name = "CC" }; Finish();
        Check(CartographyRuntime.Presentation.Document.Region == "CC", "A changed player world follows the new current region.");
        CartographyRuntime.ResetView();
    }
}
