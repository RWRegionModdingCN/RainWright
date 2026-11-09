using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Game/frontend page adapter. Rainwright owns registration, navigation order,
/// active-page transitions and retained lifecycle; this adapter supplies the
/// concrete built-in pages and gameplay EditorToolMode contract.
/// </summary>
internal static class DevToolPageViewRegistry
{
    private static readonly FrontendPageRegistryCore<EditorToolMode, IDevToolFrontendPage> Registry =
        new(page => page.Activate(), page => page.Deactivate(), page => page.Reset());

    static DevToolPageViewRegistry()
    {
        Register(new RoomDevToolPage());
        Register(new ObjectsDevToolPage());
        Register(new SoundDevToolPage());
        Register(new TriggersDevToolPage());
        Register(new MapDevToolPage());
        Register(new BackgroundDevToolPage());
        Register(new DialogDevToolPage());
        Register(new RelationshipsDevToolPage());
        Register(new PaletteDevToolPage());
        ValidateBuiltinCoverage();
    }

    internal static IDevToolPageView Get(EditorToolMode mode)
    {
        SynchronizeActive(mode);
        TryGet(mode, out IDevToolPageView page);
        return page;
    }

    internal static bool TryGet(EditorToolMode mode, out IDevToolPageView view)
    {
        view = null;
        if (!Registry.TryGet(mode, out IDevToolFrontendPage page)) return false;
        view = page;
        return true;
    }

    internal static bool TryGet(string id, out IDevToolPageView view)
    {
        view = null;
        if (!Registry.TryGet(id, out IDevToolFrontendPage page)) return false;
        view = page;
        return true;
    }

    internal static bool SupportsSceneSurface(EditorToolMode mode) =>
        Registry.TryGet(mode, out IDevToolFrontendPage page) && page.SupportsSceneSurface;

    internal static IReadOnlyList<IDevToolFrontendPage> NavigationPages => Registry.NavigationPages;
    internal static string ActivePageId => Registry.ActivePage?.Id ?? string.Empty;
    internal static int RegisteredPageCount => Registry.Count;

    internal static void SynchronizeActive(EditorToolMode mode) => Registry.SynchronizeActive(mode);
    internal static void DeactivateActive() => Registry.DeactivateActive();

    internal static void Register(IDevToolFrontendPage page)
    {
        if (page == null) throw new ArgumentNullException(nameof(page));
        Registry.Register(page.Mode, page.Id, page.NavigationLabel, page.NavigationOrder, page);
    }

    internal static void ResetAll() => Registry.ResetAll();

    private static void ValidateBuiltinCoverage()
    {
        Array modes = Enum.GetValues(typeof(EditorToolMode));
        for (int i = 0; i < modes.Length; i++)
        {
            EditorToolMode mode = (EditorToolMode)modes.GetValue(i);
            if (!Registry.TryGet(mode, out IDevToolFrontendPage _))
                throw new InvalidOperationException(
                    "No RWImGui DevTool page is registered for mode " + mode + ".");
        }
    }
}
