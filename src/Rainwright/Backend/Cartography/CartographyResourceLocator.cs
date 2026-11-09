using System;
using System.IO;
using System.Reflection;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

/// <summary>
/// Resource namespace independent of the assembly's legacy DryCycle identity.
/// Embedded sprite sheets travel with whichever module compiles Cartography.
/// Older assemblies with DryCycle.Cartography.* resource names remain readable.
/// This method never searches unrelated mods' resource assemblies.
/// </summary>
internal static class CartographyResourceLocator
{
    internal static Stream Open(Assembly owner, string spriteSheet)
    {
        if (owner == null) throw new ArgumentNullException(nameof(owner));
        if (string.IsNullOrWhiteSpace(spriteSheet) ||
            spriteSheet.IndexOfAny(new[] { '/', '\\', '.' }) >= 0)
            throw new ArgumentException("Cartography sprite sheet name is invalid.", nameof(spriteSheet));

        string suffix = spriteSheet + ".png";
        return owner.GetManifestResourceStream("Rainwright.Cartography." + suffix)
            ?? owner.GetManifestResourceStream("DryCycle.Cartography." + suffix);
    }
}
