using System.Numerics;

namespace RainWright.Remix;

// One palette for the native Remix adapter and the existing ImGui title renderer.
internal static class RemixTitleGold
{
    internal static readonly Vector4 Primary = new(1.00f, .88f, .56f, 1f);
    internal static readonly Vector4 Highlight = new(1.00f, .97f, .82f, .96f);
    internal static readonly Vector4 PrimaryRelief = new(.31f, .17f, .045f, .96f);
    internal static readonly Vector4 Secondary = new(.96f, .79f, .45f, 1f);
    internal static readonly Vector4 SecondaryRelief = new(.32f, .18f, .055f, .82f);
    internal static readonly Vector4 Tertiary = new(.90f, .72f, .40f, 1f);
    internal static readonly Vector4 TertiaryRelief = new(.28f, .16f, .05f, .74f);
}
