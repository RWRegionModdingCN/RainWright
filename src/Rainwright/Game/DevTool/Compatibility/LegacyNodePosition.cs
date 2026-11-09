using System;
using System.Collections.Concurrent;
using System.Reflection;
using DevInterface;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Compatibility;

internal static class LegacyNodePosition
{
    private static readonly ConcurrentDictionary<Type, bool> Overrides = new();
    private static readonly MethodInfo BaseGetter = typeof(PositionedDevUINode).GetProperty("absPos").GetGetMethod();

    // The base property recursively adds parent positions. Do that presentation math iteratively;
    // preserve real custom virtual overrides as the authoritative coordinate boundary.
    internal static Vector2 Absolute(PositionedDevUINode node)
    {
        if (!LegacyDevInterfaceBridge.IsValidParentChain(node)) throw new InvalidOperationException("Invalid original gizmo parent chain.");
        double x = 0, y = 0;
        for (var current = node; current != null; current = current.parentNode as PositionedDevUINode)
        {
            bool custom = Overrides.GetOrAdd(current.GetType(), type =>
            {
                for (; type != null && type != typeof(PositionedDevUINode); type = type.BaseType)
                {
                    var getter = type.GetMethod("get_absPos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (getter != null && getter.GetBaseDefinition() == BaseGetter) return true;
                }
                return false;
            });
            Vector2 position = custom ? current.absPos : current.pos;
            x += position.x; y += position.y;
            if (custom) break;
        }
        if (double.IsNaN(x) || double.IsNaN(y) || Math.Abs(x) > float.MaxValue || Math.Abs(y) > float.MaxValue)
            throw new InvalidOperationException("Original gizmo position is not finite.");
        return new Vector2((float)x, (float)y);
    }
}
