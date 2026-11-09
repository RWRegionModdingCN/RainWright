using System;
using System.Collections.Generic;
using System.Linq;
using Menu.Remix.MixedUI;

namespace RainWright.Remix;

/// <summary>Resolve the current game language when this method is called.</summary>
public interface IRemixModDetailsProvider
{
    IRemixAboutPageContent CreateContent(ModManager.Mod mod);
}

/// <summary>A layout mounted directly in the Mod list's right-hand details panel.</summary>
public interface IRemixModDetailsLayout
{
    RemixModDetailsView CreateView(OpTab target, IRemixAboutPageContent content, RemixModDetailsMetadata metadata);
}

/// <summary>Native metadata and requirement warnings, captured after the game's own validation.</summary>
public sealed class RemixModDetailsMetadata
{
    public string Id { get; }
    public string Version { get; }
    public string Authors { get; }
    public string Requirements { get; }
    public bool RequirementsWarning { get; }
    /// <summary>Borrowed native Mod thumbnail atlas element; no texture ownership is transferred.</summary>
    public string CoverAtlasElement { get; }
    public RemixModDetailsMetadata(string id, string version, string authors, string requirements, bool requirementsWarning = false)
        : this(id, version, authors, requirements, requirementsWarning, null) { }
    public RemixModDetailsMetadata(string id, string version, string authors, string requirements, bool requirementsWarning, string coverAtlasElement)
    {
        Id = id ?? string.Empty;
        Version = version ?? string.Empty;
        Authors = authors ?? string.Empty;
        Requirements = requirements ?? string.Empty;
        RequirementsWarning = requirementsWarning;
        CoverAtlasElement = coverAtlasElement;
    }
}

/// <summary>
/// Opt-in presentation only. Call on the game thread after RainWright has loaded;
/// keep and dispose the registration when your plugin disables. Your BepInEx
/// plugin must declare its dependency on "rainwright". Unregistered Mods retain
/// their native details UI. No author/configuration data is changed.
/// </summary>
public static class RemixModDetails
{
    internal sealed class Entry
    {
        internal readonly IRemixModDetailsProvider Provider;
        internal readonly IRemixModDetailsLayout Layout;
        internal Entry(IRemixModDetailsProvider provider, IRemixModDetailsLayout layout)
        { Provider = provider; Layout = layout; }
    }
    private static readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    public static IDisposable Register(string modId, IRemixModDetailsProvider provider, IRemixModDetailsLayout layout = null)
    {
        if (string.IsNullOrWhiteSpace(modId)) throw new ArgumentException("A Mod ID is required.", nameof(modId));
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        if (entries.ContainsKey(modId)) throw new InvalidOperationException("Mod details are already registered: " + modId);
        var entry = new Entry(provider, layout ?? new RemixModDetailsLayout());
        entries.Add(modId, entry);
        return new Registration(modId, entry);
    }
    internal static bool TryGet(string modId, out Entry entry) => entries.TryGetValue(modId, out entry);
    private sealed class Registration : IDisposable
    {
        private string id;
        private readonly Entry entry;
        internal Registration(string id, Entry entry) { this.id = id; this.entry = entry; }
        public void Dispose()
        {
            if (id == null) return;
            if (entries.TryGetValue(id, out var current) && ReferenceEquals(entry, current))
            {
                RemixModDetailsBridge.ReleaseMod(id);
                entries.Remove(id);
            }
            id = null;
        }
    }
}

/// <summary>
/// Owns only newly-created elements. Native cached controls and borrowed cover
/// atlases are not owned. Dispose before the hosting tab is unloaded.
/// </summary>
public sealed class RemixModDetailsView : IDisposable
{
    private readonly List<UIelement> owned;
    public IReadOnlyList<UIelement> Elements { get; }
    public OpScrollBox Body { get; }
    private readonly float[] anchors;
    /// <summary>Adopt controls created by a custom layout; callers retain ownership of the tab.</summary>
    public RemixModDetailsView(IEnumerable<UIelement> elements, OpScrollBox body, IEnumerable<float> sectionOffsets = null)
        : this(new List<UIelement>(elements ?? throw new ArgumentNullException(nameof(elements))),
            body ?? throw new ArgumentNullException(nameof(body)), (sectionOffsets ?? Array.Empty<float>()).ToArray()) { }
    internal RemixModDetailsView(List<UIelement> owned, OpScrollBox body, float[] anchors)
    { this.owned = owned; Elements = owned.AsReadOnly(); Body = body; this.anchors = anchors; }
    public void ScrollToSection(int index)
    {
        if (owned.Count == 0) throw new ObjectDisposedException(nameof(RemixModDetailsView));
        if (index < 0 || index >= anchors.Length) throw new ArgumentOutOfRangeException(nameof(index));
        Body.targetScrollOffset = UnityEngine.Mathf.Clamp(Body.MaxScroll + anchors[index], Body.MaxScroll, 0);
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        for (int i = owned.Count - 1; i >= 0; i--)
        {
            var element = owned[i];
            try
            {
                element.tab?.RemoveItems(element);
                element.Unload();
                owned.RemoveAt(i);
            }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Mod details UI cleanup is incomplete; remaining elements are retained for retry.", failures);
    }
}
