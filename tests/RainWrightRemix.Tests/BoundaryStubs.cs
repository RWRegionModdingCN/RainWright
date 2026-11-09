using Menu.Remix.MixedUI;
using UnityEngine;

namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public List<string> Errors = new(), Warnings = new();
        public void LogError(object value) => Errors.Add(value.ToString());
        public void LogWarning(object value) => Warnings.Add(value.ToString());
        public void LogInfo(object value) { }
    }
}
namespace Menu.Remix
{
    public static class OptionalText { public static string[] engText; }
    public class MenuModList
    {
        public class ModButton(ModManager.Mod mod)
        { public ModInterface itf = new() { mod = mod }; }
        public class ModInterface { public ModManager.Mod mod; }
    }
    public class InternalOI_Stats : OptionInterface
    {
        public OpLabel lblName, lblID, lblVersion, lblTargetVersion, lblAuthors;
        public OpLabelLong lblDescription, lblRequirements;
        public UIelement btnAuthorExtra, btnWorkshopUpload;
        public OpImage imgThumbnail;
        public bool HasThumbnail;
        public OpScrollBox boxDescription;
        public int NativeCalls;
        public InternalOI_Stats()
        {
            Tabs = new[] { new OpTab(this, "Stats"), new OpTab(this, "Details") };
            lblName = Label(); lblID = Label(); lblVersion = Label(); lblTargetVersion = Label(); lblAuthors = Label();
            lblDescription = new(Vector2.zero, new(560, 100), "Native description", false, FLabelAlignment.Center);
            lblRequirements = new(Vector2.zero, new(560, 100), "(需要附加模组：Rain World ImGUI API, Library Loader)", false, FLabelAlignment.Center);
            btnAuthorExtra = new UIelement(); imgThumbnail = new OpImage(Vector2.zero, "native-cover"); btnWorkshopUpload = new UIelement();
            Tabs[1].AddItems(lblName, lblID, lblVersion, lblTargetVersion, lblAuthors, lblDescription, lblRequirements,
                btnAuthorExtra, imgThumbnail, btnWorkshopUpload);
        }
        private static OpLabel Label() => new(Vector2.zero, new(560, 30), "Native", FLabelAlignment.Left);
        public static void NativePreview(InternalOI_Stats self, MenuModList.ModButton button)
        {
            self.NativeCalls++;
            if (self.boxDescription != null)
            {
                self.boxDescription.items.Remove(self.lblDescription);
                self.lblDescription.scrollBox = null;
                self.Tabs[1].RemoveItems(self.boxDescription);
                self.boxDescription.Unload();
                self.boxDescription = null;
            }
            if (button.itf.mod.id == "rainwright")
            {
                self.boxDescription = new OpScrollBox(new(5, 300), new(590, 100), 400);
                self.Tabs[1].AddItems(self.boxDescription);
                self.boxDescription.AddItems(self.lblDescription);
            }
            foreach (var element in self.Tabs[1].items) element.Show();
            self.lblTargetVersion.Hide(); if (!self.HasThumbnail) self.imgThumbnail.Hide(); self.btnAuthorExtra.Hide();
        }
    }
}
namespace On.Menu.Remix
{
    public static class InternalOI_Stats
    {
        public delegate void orig__PreviewMod(global::Menu.Remix.InternalOI_Stats self, global::Menu.Remix.MenuModList.ModButton button);
        public delegate void hook__PreviewMod(orig__PreviewMod orig, global::Menu.Remix.InternalOI_Stats self, global::Menu.Remix.MenuModList.ModButton button);
        public static event hook__PreviewMod _PreviewMod;
        public static void Invoke(global::Menu.Remix.InternalOI_Stats self, global::Menu.Remix.MenuModList.ModButton button)
        {
            if (_PreviewMod == null) global::Menu.Remix.InternalOI_Stats.NativePreview(self, button);
            else _PreviewMod(global::Menu.Remix.InternalOI_Stats.NativePreview, self, button);
        }
    }
}
