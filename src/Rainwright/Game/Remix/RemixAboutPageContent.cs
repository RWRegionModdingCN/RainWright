using System;
using System.Collections.Generic;
using System.Linq;

namespace RainWright.Remix;

/// <summary>Presentation data only. Resolve localization before creating the page.</summary>
public interface IRemixAboutPageContent
{
    string Title { get; }
    string Subtitle { get; }
    IReadOnlyList<RemixAboutSection> Sections { get; }
    string CreditsHeading { get; }
    string CreditsText { get; }
    /// <summary>
    /// Optional, already-loaded Futile atlas element. Its owner must keep the atlas
    /// alive until Remix unloads the tab; this page never unloads the borrowed atlas.
    /// </summary>
    string CoverAtlasElement { get; }
}

public sealed class RemixAboutSection
{
    public string Heading { get; }
    public string Text { get; }

    public RemixAboutSection(string heading, string text)
    {
        Heading = heading ?? string.Empty;
        Text = text ?? string.Empty;
    }
}

/// <summary>Immutable convenience implementation for callers that do not need a custom provider.</summary>
public sealed class RemixAboutPageContent : IRemixAboutPageContent
{
    public string Title { get; }
    public string Subtitle { get; }
    public IReadOnlyList<RemixAboutSection> Sections { get; }
    public string CreditsHeading { get; }
    public string CreditsText { get; }
    public string CoverAtlasElement { get; }

    public RemixAboutPageContent(string title, string subtitle, IEnumerable<RemixAboutSection> sections,
        string creditsHeading = null, string creditsText = null, string coverAtlasElement = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("A page title is required.", nameof(title));
        if (sections == null) throw new ArgumentNullException(nameof(sections));
        var snapshot = sections.ToArray();
        if (snapshot.Any(section => section == null)) throw new ArgumentException("Sections cannot contain null.", nameof(sections));
        Title = title;
        Subtitle = subtitle ?? string.Empty;
        Sections = Array.AsReadOnly(snapshot);
        CreditsHeading = creditsHeading ?? string.Empty;
        CreditsText = creditsText ?? string.Empty;
        CoverAtlasElement = coverAtlasElement;
    }
}
