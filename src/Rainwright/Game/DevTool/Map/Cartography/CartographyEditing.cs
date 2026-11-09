using System;

namespace DryCycle.DevUI.DevTool.Map.Cartography;

// Game/scene adapter only. The command types, mutation semantics, validation
// and edit identity are compiled into RainWright.dll. Existing frontend calls
// keep the historical signature while runtime geometry remains host-owned.
internal static class CartographyEditing
{
    internal static bool SameItem(CartographyItem a, CartographyItem b) =>
        CartographyEditEngine.SameItem(a, b);

    internal static bool SameLayer(CartographyLayer a, CartographyLayer b) =>
        CartographyEditEngine.SameLayer(a, b);

    internal static CartographyDocument Apply(CartographyDocument original, CartographySource source, CartographyCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        switch (command.Kind)
        {
            case CartographyCommandKind.Align:
                return CartographyEditEngine.Apply(original, command,
                    bounds: item => CartographySceneBuilder.Bounds(item, source));
            case CartographyCommandKind.AlignPorts:
                return CartographyEditEngine.Apply(original, command,
                    ports: (CartographyItem item, int port, out float x, out float y) =>
                        CartographySceneBuilder.Port(item, port, source, out x, out y));
            case CartographyCommandKind.AddMissingRooms:
                return CartographyEditEngine.Apply(original, command,
                    addMissingRooms: source.AddMissingRooms);
            default:
                // Ordinary drag/paint/layer operations do not allocate source callbacks.
                return CartographyEditEngine.Apply(original, command);
        }
    }
}
