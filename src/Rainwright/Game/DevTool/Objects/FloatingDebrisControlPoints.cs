using System;
using System.Collections.Generic;
using UnityEngine;
using Watcher;

namespace DryCycle.DevUI.DevTool.Objects;

// The native data remains the owner of these points and offsets. Both inspector rows and scene
// handles use the same keys, then flow through the normal property/history/runtime boundary.
internal static class FloatingDebrisControlPoints
{
    private const string Prefix = "builtin.floatingDebris.point.";

    internal static void Append(List<EditorPropertySnapshot> result, FloatingDebrisData data, FloatingDebris.UIText ui)
    {
        for (int i = 0; i < data.controlPointPosX.Count; i++)
        {
            string key = Prefix + i + ".";
            string group = "Control Point " + (i + 1);
            result.Add(new EditorPropertySnapshot
            {
                Key = key + "position", DisplayName = "Position", Group = group, Source = "Rain World model",
                Kind = EditorPropertyKind.Vector2, X = data.controlPointPosX[i], Y = data.controlPointPosY[i]
            });
            AddOffset(result, key + "scatter", group, ui?.scatter, "Scatter", data.controlPointOffsetAmount[i]);
            AddOffset(result, key + "depth", group, ui?.depthOffset, "Depth Offset", data.controlPointDepthOffset[i]);
            AddOffset(result, key + "scale", group, ui?.scaleOffset, "Scale Offset", data.controlPointScaleOffset[i]);
            Vector4 extra = data.controlPointExtraOffset[i];
            if (ui?.extraSlider1?.hide == false) AddOffset(result, key + "extra0", group, ui.extraSlider1, "Extra Offset 1", extra.x);
            if (ui?.extraSlider2?.hide == false) AddOffset(result, key + "extra1", group, ui.extraSlider2, "Extra Offset 2", extra.y);
            if (ui?.extraSlider3?.hide == false) AddOffset(result, key + "extra2", group, ui.extraSlider3, "Extra Offset 3", extra.z);
            if (ui?.extraSlider4?.hide == false) AddOffset(result, key + "extra3", group, ui.extraSlider4, "Extra Offset 4", extra.w);
        }
    }

    private static void AddOffset(List<EditorPropertySnapshot> result, string key, string group,
        FloatingDebris.UIText.Text metadata, string title, float value)
    {
        if (metadata?.hide == true) return;
        result.Add(new EditorPropertySnapshot
        {
            Key = key, DisplayName = metadata?.title ?? title, Group = group, Source = "Rain World model",
            Kind = EditorPropertyKind.Float, X = value, Step = 0.01f
        });
    }

    private static bool TryParse(FloatingDebrisData data, string key, out int index, out string member)
    {
        index = -1;
        member = null;
        if (key == null || !key.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int separator = key.IndexOf('.', Prefix.Length);
        if (separator < 0 || !int.TryParse(key.Substring(Prefix.Length, separator - Prefix.Length), out index) ||
            index < 0 || index >= data.controlPointPosX.Count) return false;
        member = key.Substring(separator + 1);
        return member == "position" || member == "scatter" || member == "depth" || member == "scale" ||
            member == "extra0" || member == "extra1" || member == "extra2" || member == "extra3";
    }

    internal static bool TrySetValue(FloatingDebrisData data, string key, EditorPropertyValue value)
    {
        if (!TryParse(data, key, out int index, out string member)) return false;
        if (member == "position")
        {
            if (value.Kind != EditorPropertyKind.Vector2 || !Finite(value.X) || !Finite(value.Y)) return false;
            var x = new List<float>(data.controlPointPosX);
            var y = new List<float>(data.controlPointPosY);
            x[index] = value.X;
            y[index] = value.Y;
            data.controlPointPosX = x;
            data.controlPointPosY = y;
            return true;
        }
        if (value.Kind != EditorPropertyKind.Float || !Finite(value.X)) return false;
        if (member.StartsWith("extra", StringComparison.Ordinal))
        {
            var values = new List<Vector4>(data.controlPointExtraOffset);
            Vector4 extra = values[index];
            extra[member[5] - '0'] = value.X;
            values[index] = extra;
            data.controlPointExtraOffset = values;
            return true;
        }
        List<float> offsets = new(member == "scatter" ? data.controlPointOffsetAmount :
            member == "depth" ? data.controlPointDepthOffset : data.controlPointScaleOffset);
        offsets[index] = value.X;
        if (member == "scatter") data.controlPointOffsetAmount = offsets;
        else if (member == "depth") data.controlPointDepthOffset = offsets;
        else data.controlPointScaleOffset = offsets;
        return true;
    }

    internal static bool TryBuildGizmoValue(FloatingDebrisData data, string key, float x, float y, out EditorPropertyValue value)
    {
        value = default;
        if (!Finite(x) || !Finite(y) || !TryParse(data, key, out int index, out string member)) return false;
        value = member == "position"
            ? new EditorPropertyValue(EditorPropertyKind.Vector2, x: x, y: y)
            : new EditorPropertyValue(EditorPropertyKind.Float,
                x: (member == "scatter" ? y - data.controlPointPosY[index] : x - data.controlPointPosX[index]) * 0.01f);
        return true;
    }

    internal static void CaptureGeometry(PlacedObject target, FloatingDebrisData data, EditorPropertySnapshot[] properties,
        List<EditorObjectGizmoHandleSnapshot> handles, List<EditorObjectLineSegmentSnapshot> lines)
    {
        foreach (EditorPropertySnapshot property in properties ?? Array.Empty<EditorPropertySnapshot>())
        {
            if (property == null || !TryParse(data, property.Key, out int index, out string member)) continue;
            Vector2 point = target.pos + new Vector2(data.controlPointPosX[index], data.controlPointPosY[index]);
            Vector2 anchor = point;
            Vector2 endpoint = point;
            if (member == "scatter") endpoint.y += property.X * 100f;
            else if (member != "position")
            {
                int row = member == "depth" ? 1 : member == "scale" ? 2 : 3 + member[5] - '0';
                anchor.y -= 30f * row;
                endpoint = anchor + new Vector2(property.X * 100f, 0f);
            }
            handles.Add(new EditorObjectGizmoHandleSnapshot
            {
                Id = "property:" + property.Key, X = endpoint.x, Y = endpoint.y,
                AnchorX = anchor.x, AnchorY = anchor.y, DrawAnchorLine = member != "position"
            });
        }
        for (int i = 1; i < data.controlPointPosX.Count; i++)
            lines.Add(new EditorObjectLineSegmentSnapshot
            {
                X0 = target.pos.x + data.controlPointPosX[i - 1], Y0 = target.pos.y + data.controlPointPosY[i - 1],
                X1 = target.pos.x + data.controlPointPosX[i], Y1 = target.pos.y + data.controlPointPosY[i]
            });
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
