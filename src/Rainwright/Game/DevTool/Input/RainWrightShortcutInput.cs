using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Core;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Input;

internal static class RainWrightShortcutInput
{
    private sealed class Binding
    {
        internal string Chord;
        internal KeyCode Key;
        internal bool Ctrl, Shift, Alt;
    }
    private static readonly Dictionary<string, Binding> bindings = new();
    internal static bool PlainDeletePressed() => global::UnityEngine.Input.GetKeyDown(KeyCode.Delete) &&
        !global::UnityEngine.Input.GetKey(KeyCode.LeftControl) && !global::UnityEngine.Input.GetKey(KeyCode.RightControl) &&
        !global::UnityEngine.Input.GetKey(KeyCode.LeftCommand) && !global::UnityEngine.Input.GetKey(KeyCode.RightCommand) &&
        !global::UnityEngine.Input.GetKey(KeyCode.LeftShift) && !global::UnityEngine.Input.GetKey(KeyCode.RightShift) &&
        !global::UnityEngine.Input.GetKey(KeyCode.LeftAlt) && !global::UnityEngine.Input.GetKey(KeyCode.RightAlt);
    private static readonly string[] documentActions = { "save", "undo", "redo-shift", "redo-y" };
    private static readonly string[] viewActions = { "delete", "focus", "browser", "inspector", "presentation", "top-status", "objects-duplicate", "window-group" };
    internal static bool HasHeldViewChord()
    {
        foreach (string id in viewActions) if (Read(id, false, out _)) return true;
        return false;
    }
    internal static bool HasHeldDocumentChord()
    {
        foreach (string id in documentActions) if (Read(id, false, out _)) return true;
        return false;
    }
    internal static bool ReservesKey(KeyCode key)
    {
        if (key == KeyCode.Z && EditorShortcutGameplayPolicy.AllowZ || key == KeyCode.S && EditorShortcutGameplayPolicy.AllowS || key == KeyCode.Y && EditorShortcutGameplayPolicy.AllowY) return false;
        foreach (string id in documentActions) if (Read(id, false, out KeyCode binding) && key == binding) return true;
        return false;
    }
    internal static bool Pressed(string id)
        => Read(id, true, out _);
    private static bool Read(string id, bool pressed, out KeyCode boundKey)
    {
        string chord = RainWrightPersonalSettings.Chord(id);
        if (string.IsNullOrEmpty(chord))
        {
            boundKey = default;
            return false;
        }
        if (!bindings.TryGetValue(id, out var binding) || binding.Chord != chord)
        {
            string key = chord.Substring(chord.LastIndexOf('+') + 1);
            binding = new Binding { Chord = chord, Key = (KeyCode)Enum.Parse(typeof(KeyCode), key, true),
                Ctrl = chord.Contains("Ctrl+"), Shift = chord.Contains("Shift+"), Alt = chord.Contains("Alt+") };
            bindings[id] = binding;
        }
        bool ctrl = global::UnityEngine.Input.GetKey(KeyCode.LeftControl) || global::UnityEngine.Input.GetKey(KeyCode.RightControl) ||
            global::UnityEngine.Input.GetKey(KeyCode.LeftCommand) || global::UnityEngine.Input.GetKey(KeyCode.RightCommand);
        bool shift = global::UnityEngine.Input.GetKey(KeyCode.LeftShift) || global::UnityEngine.Input.GetKey(KeyCode.RightShift);
        bool alt = global::UnityEngine.Input.GetKey(KeyCode.LeftAlt) || global::UnityEngine.Input.GetKey(KeyCode.RightAlt);
        boundKey = binding.Key;
        return ctrl == binding.Ctrl && shift == binding.Shift && alt == binding.Alt &&
            (pressed ? global::UnityEngine.Input.GetKeyDown(binding.Key) : global::UnityEngine.Input.GetKey(binding.Key));
    }
}
