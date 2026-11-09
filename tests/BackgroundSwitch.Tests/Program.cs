using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Input;
using DevInterface;

static class Program
{
    static int count;
    static void Check(bool condition, string message) { count++; if (!condition) throw new Exception(message); }
    static void Main()
    {
        var ui = new DevUI(); var session = new EditorSession { Owner = ui };
        EditorInputRouter.FrontendAttached = true;
        EditorUiModeState.SetVanilla(false);
        Check(NativeToolScheduler.SwitchTool(session, EditorToolMode.Background), "Background must be a native workspace.");
        Check(ui.activePage is NativeToolAnchorPage && ui.Switches == 0 && RegionKit.Modules.BackgroundBuilder.BackgroundPage.Constructions == 0,
            "New UI must not construct or run RegionKit's old page.");
        var anchor = ui.activePage;
        NativeToolScheduler.SwitchTool(session, EditorToolMode.Background);
        Check(ReferenceEquals(ui.activePage, anchor), "Repeated native selection must be a no-op.");
        EditorUiModeState.SetVanilla(true);
        NativeToolScheduler.SynchronizePresentationOwnership(session);
        Check(ui.activePage is RegionKit.Modules.BackgroundBuilder.BackgroundPage && ui.LastIndex == 9 && ui.Switches == 1,
            "Vanilla handoff must resolve RegionKit's actual dynamic page index.");
        var native = ui.activePage;
        NativeToolScheduler.SynchronizePresentationOwnership(session);
        Check(ReferenceEquals(native, ui.activePage) && ui.Switches == 1, "Vanilla pages must not be rebuilt every frame.");
        var custom = new ThirdPartyPage(ui); ui.activePage = custom; session.ToolMode = EditorToolMode.Room;
        NativeToolScheduler.SynchronizePresentationOwnership(session);
        Check(ReferenceEquals(custom, ui.activePage), "Unknown native pages must retain full ownership.");
        session.ToolMode = EditorToolMode.Background; ui.activePage = native;
        EditorUiModeState.SetVanilla(false); NativeToolScheduler.SynchronizePresentationOwnership(session);
        Check(ui.activePage is NativeToolAnchorPage && RegionKit.Modules.BackgroundBuilder.BackgroundPage.Constructions == 1,
            "Returning to rebuilt mode must retire presentation without constructing another legacy background page.");
        NativeToolScheduler.SwitchTool(session, EditorToolMode.Map);
        Check(ui.activePage is MapPage && session.ToolMode == EditorToolMode.Map, "Background must hand off cleanly to Map.");
        ui.pages = new[] { "Room Settings", "Objects", "Sound", "Map", "Triggers", "Dialog", "Relationships" };
        NativeToolScheduler.SwitchTool(session, EditorToolMode.Background); EditorUiModeState.SetVanilla(true);
        NativeToolScheduler.SynchronizePresentationOwnership(session);
        Check(ui.activePage is RoomSettingsPage && session.ToolMode == EditorToolMode.Room,
            "Without RegionKit, Vanilla must return to its real room-settings page.");
        EditorUiModeState.SetVanilla(false);
        EditorInputRouter.FrontendAttached = false;
        Check(!NativeToolScheduler.IsVirtualToolActive(session), "A detached frontend must never claim a native page.");
        foreach (var mode in new[] { EditorToolMode.Objects, EditorToolMode.Sound, EditorToolMode.Triggers, EditorToolMode.Relationships })
        {
            EditorInputRouter.FrontendAttached = true; NativeToolScheduler.SwitchTool(session, mode);
            EditorUiModeState.SetVanilla(true); NativeToolScheduler.SynchronizePresentationOwnership(session);
            Check(ui.activePage is not NativeToolAnchorPage, "Original page unavailable after handoff: " + mode);
            EditorUiModeState.SetVanilla(false);
        }
        ui.pages = new[] { "Room Settings", "Objects", "Sound", "Map", "Triggers", "Dialog", "Relationships", "Extra A", "Extra B", "Background" };
        NativeToolScheduler.SwitchTool(session, EditorToolMode.Background);
        RegionKit.Modules.BackgroundBuilder.BackgroundPage.Fail = true;
        EditorUiModeState.SetVanilla(true); NativeToolScheduler.SynchronizePresentationOwnership(session);
        Check(ui.activePage is RoomSettingsPage && session.ToolMode == EditorToolMode.Room && EditorDiagnostics.Warnings == 1, "A broken optional background page must restore the working native editor and report the original error.");
        Console.WriteLine($"PASS: {count} native/vanilla ownership, dynamic background page, detach and existing-tool handoffs.");
    }
}
sealed class ThirdPartyPage : Page { public ThirdPartyPage(DevUI owner) : base(owner, "third", null, "third") { } }

namespace DevInterface
{
    public class DevUINode { public List<DevUINode> subNodes = new(); public virtual void ClearSprites() { } }
    public class Page : DevUINode
    {
        public bool initRefresh;
        public List<DevUINode> tempNodes;
        public Page(DevUI owner, string id, DevUINode parent, string name) { subNodes.Add(new DevUINode()); }
        public virtual void Refresh() { }
    }
    public class DevUI
    {
        public Page activePage;
        public string[] pages = { "Room Settings", "Objects", "Sound", "Map", "Triggers", "Dialog", "Relationships", "Extra A", "Extra B", "Background" };
        public int LastIndex = -1, Switches;
        public void ClearSprites() { }
        public void SwitchPage(int index)
        {
            LastIndex = index; Switches++;
            activePage = index switch
            { 0 => new RoomSettingsPage(this), 1 => new ObjectsPage(this), 2 => new SoundPage(this), 3 => new MapPage(this), 4 => new TriggersPage(this), 5 => new DialogPage(this), 6 => new RelationshipPage(this), 9 => new RegionKit.Modules.BackgroundBuilder.BackgroundPage(this), _ => throw new Exception("Wrong page index.") };
        }
    }
    public class RoomSettingsPage : Page { public RoomSettingsPage(DevUI o) : base(o, "", null, "") { } }
    public class ObjectsPage : Page { public ObjectsPage(DevUI o) : base(o, "", null, "") { } }
    public class SoundPage : Page { public SoundPage(DevUI o) : base(o, "", null, "") { } }
    public class TriggersPage : Page { public TriggersPage(DevUI o) : base(o, "", null, "") { } }
    public class MapPage : Page { public MapPage(DevUI o) : base(o, "", null, "") { } }
    public class DialogPage : Page { public DialogPage(DevUI o) : base(o, "", null, "") { } }
    public class RelationshipPage : Page { public RelationshipPage(DevUI o) : base(o, "", null, "") { } }
}
namespace RegionKit.Modules.BackgroundBuilder
{
    public class BackgroundPage : Page
    {
        public static int Constructions;
        public static bool Fail;
        public BackgroundPage(DevUI o) : base(o, "", null, "") { Constructions++; if (Fail) throw new InvalidOperationException("Simulated optional page failure."); }
    }
}
namespace DryCycle.DevUI.DevTool.Input { static class EditorInputRouter { internal static bool FrontendAttached; } }
namespace DryCycle.DevUI.DevTool.Compatibility
{
    static class LegacyUiPresentationController { internal static void Restore(Page page) { } }
    static class DevUiDiagnosticsPolicy { internal static bool Enabled = false; }
}
namespace DryCycle.DevUI.DevTool.Core
{
    sealed class Transactions { internal void Reset() { } }
    sealed class Log { internal void LogInfo(string message) { } }
    static class EditorDiagnostics { internal static Log LogSource = null; internal static int Warnings; internal static void Info(string message) { } internal static void Warning(string message) { Warnings++; } }
    sealed class EditorSession
    {
        internal global::DevInterface.DevUI Owner;
        internal EditorToolMode ToolMode;
        internal bool LegacyUiVisible;
        internal Transactions LegacyTransactions = new();
        internal void AdoptMaterializedToolMode(EditorToolMode mode, bool legacyUiVisible) { ToolMode = mode; LegacyUiVisible = legacyUiVisible; }
        internal void AdoptVirtualToolMode(EditorToolMode mode) { ToolMode = mode; LegacyUiVisible = false; }
        internal void Synchronize(global::DevInterface.DevUI owner)
        {
            Owner = owner;
            ToolMode = owner.activePage switch
            { NativeToolAnchorPage p => p.ToolMode, ObjectsPage => EditorToolMode.Objects, SoundPage => EditorToolMode.Sound, TriggersPage => EditorToolMode.Triggers,
              MapPage => EditorToolMode.Map, DialogPage => EditorToolMode.Dialog, RelationshipPage => EditorToolMode.Relationships,
              RegionKit.Modules.BackgroundBuilder.BackgroundPage => EditorToolMode.Background, _ => EditorToolMode.Room };
        }
    }
}
