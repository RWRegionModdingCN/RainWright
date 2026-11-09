using System;
using System.Linq;
using DryCycle.DevUI.DevTool.RWImGui;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Core
{
    internal enum EditorToolMode
    {
        Room, Objects, Sound, Triggers, Map, Dialog, Relationships, Background, Palette
    }
}

namespace DryCycle.DevUI.DevTool.RWImGui
{
    internal interface IDevToolPageView { }
    internal interface IDevToolFrontendPage : IDevToolPageView
    {
        string Id { get; }
        string NavigationLabel { get; }
        int NavigationOrder { get; }
        EditorToolMode Mode { get; }
        bool SupportsSceneSurface { get; }
        void Activate();
        void Deactivate();
        void Reset();
    }

    internal abstract class PageStub : IDevToolFrontendPage
    {
        internal static int Activations, Deactivations, Resets;
        public abstract string Id { get; }
        public abstract EditorToolMode Mode { get; }
        public virtual string NavigationLabel => Id;
        public virtual int NavigationOrder => 1;
        public virtual bool SupportsSceneSurface => true;
        public void Activate() { Activations++; }
        public void Deactivate() { Deactivations++; }
        public void Reset() { Resets++; }
    }
    internal sealed class RoomDevToolPage : PageStub
    {
        public override string Id => "rooms";
        public override EditorToolMode Mode => EditorToolMode.Room;
    }
    internal sealed class ObjectsDevToolPage : PageStub
    {
        public override string Id => "objects";
        public override EditorToolMode Mode => EditorToolMode.Objects;
    }
    internal sealed class SoundDevToolPage : PageStub
    {
        public override string Id => "sound";
        public override EditorToolMode Mode => EditorToolMode.Sound;
    }
    internal sealed class TriggersDevToolPage : PageStub
    {
        public override string Id => "triggers";
        public override EditorToolMode Mode => EditorToolMode.Triggers;
    }
    internal sealed class MapDevToolPage : PageStub
    {
        public override string Id => "map";
        public override EditorToolMode Mode => EditorToolMode.Map;
    }
    internal sealed class DialogDevToolPage : PageStub
    {
        public override string Id => "dialog";
        public override EditorToolMode Mode => EditorToolMode.Dialog;
    }
    internal sealed class RelationshipsDevToolPage : PageStub
    {
        public override string Id => "relationships";
        public override EditorToolMode Mode => EditorToolMode.Relationships;
    }
    internal sealed class BackgroundDevToolPage : PageStub
    {
        public override string Id => "background";
        public override EditorToolMode Mode => EditorToolMode.Background;
    }
    internal sealed class PaletteDevToolPage : PageStub
    {
        public override string Id => "palette";
        public override EditorToolMode Mode => EditorToolMode.Palette;
    }
}

internal static class Program
{
    private static int count;
    private static void Check(bool ok, string message)
    {
        count++;
        if (!ok) throw new InvalidOperationException(message);
    }

    private static void Main()
    {
        Check(typeof(FrontendPageRegistryCore<EditorToolMode, IDevToolFrontendPage>)
                  .Assembly.GetName().Name == "RainWright" &&
              typeof(DevToolPageViewRegistry).Assembly !=
              typeof(FrontendPageRegistryCore<EditorToolMode, IDevToolFrontendPage>).Assembly,
            "The real page bridge must delegate to an independently compiled Rainwright.dll.");

        Check(DevToolPageViewRegistry.RegisteredPageCount == 9,
            "All nine editor pages must be registered exactly once.");
        Check(DevToolPageViewRegistry.NavigationPages.Count == 9 &&
              DevToolPageViewRegistry.NavigationPages.First().Id == "background" &&
              DevToolPageViewRegistry.NavigationPages.Last().Id == "triggers",
            "Default navigation order must be case-insensitively stable.");
        Check(DevToolPageViewRegistry.TryGet("  SOUND ", out var sound) &&
              ReferenceEquals(sound, DevToolPageViewRegistry.Get(EditorToolMode.Sound)) &&
              DevToolPageViewRegistry.ActivePageId == "sound" &&
              PageStub.Activations == 1,
            "Frontend page lookup must trim IDs and activate the correct concrete page.");
        DevToolPageViewRegistry.SynchronizeActive(EditorToolMode.Sound);
        Check(PageStub.Activations == 1 && PageStub.Deactivations == 0,
            "Repeated active mode may not reactivate or detach existing view.");
        DevToolPageViewRegistry.SynchronizeActive(EditorToolMode.Map);
        Check(PageStub.Activations == 2 && PageStub.Deactivations == 1 &&
              DevToolPageViewRegistry.ActivePageId == "map",
            "Navigation must deactivate exactly once before activating the next page.");
        DevToolPageViewRegistry.DeactivateActive();
        Check(PageStub.Deactivations == 2 &&
              DevToolPageViewRegistry.ActivePageId.Length == 0,
            "Explicit deactivation must release the active page identity.");
        DevToolPageViewRegistry.ResetAll();
        Check(PageStub.Resets == 9,
            "Workspace reset must reset every registered built-in page.");

        var registry = new FrontendPageRegistryCore<string, TestPage>(
            page => page.Activate(), page => page.Deactivate(), page => page.Reset());
        var a = new TestPage();
        var b = new TestPage();
        registry.Register("one", "Example", "First", 3, a);
        registry.Register("two", "Second", "Next", 1, b);
        Check(registry.NavigationPages[0] == b && registry.NavigationPages[1] == a,
            "Rainwright must sort by navigation order before stable page ID.");
        Check(registry.TryGet(" example ", out TestPage matched) &&
              ReferenceEquals(matched, a),
            "Page IDs must compare without case or outer whitespace.");
        bool duplicateMode = false, duplicateId = false;
        try { registry.Register("one", "other", "Other", 0, new TestPage()); }
        catch (InvalidOperationException) { duplicateMode = true; }
        try { registry.Register("other", "EXAMPLE", "Other", 0, new TestPage()); }
        catch (InvalidOperationException) { duplicateId = true; }
        Check(duplicateMode && duplicateId && registry.Count == 2,
            "Mode and case-insensitive ID uniqueness must reject duplicates atomically.");
        a.FailActivate = true;
        bool threw = false;
        try { registry.SynchronizeActive("one"); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && registry.ActivePage == null,
            "Failed page activation must not advertise the page as active.");
        a.FailActivate = false;
        registry.SynchronizeActive("two");
        b.FailDeactivate = true;
        threw = false;
        try { registry.SynchronizeActive("one"); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && registry.ActivePage == null,
            "Failed deactivation must drop stale active-page ownership.");
        b.FailDeactivate = false;
        registry.SynchronizeActive("one");
        registry.ResetAll();
        Check(a.Resets == 1 && b.Resets == 1 &&
              registry.ActivePage == null,
            "Registry reset must release an active page and all page caches.");

        Console.WriteLine("PASS: " + count +
            " page ownership, navigation, activation and failure rollbacks via Rainwright.");
    }

    private sealed class TestPage
    {
        internal bool FailActivate, FailDeactivate;
        internal int Resets;
        internal void Activate()
        {
            if (FailActivate) throw new InvalidOperationException("activate");
        }
        internal void Deactivate()
        {
            if (FailDeactivate) throw new InvalidOperationException("deactivate");
        }
        internal void Reset() => Resets++;
    }
}
