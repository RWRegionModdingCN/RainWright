// Only engine/graphics calls are simulated. The complete production surface owner is compiled
// unchanged so tests can make a native call wait for the render thread deterministically.
using System;
using System.Collections.Generic;
using Num = System.Numerics;
namespace BepInEx.Logging
{
    public sealed class ManualLogSource
    {
        public readonly List<string> Warnings = new();
        public void LogWarning(object value) => Warnings.Add(value.ToString());
        public void LogError(object value) => Warnings.Add(value.ToString());
    }
}
namespace ImGuiNET { public struct ImDrawListPtr { } }
namespace UnityEngine
{
    public class Object
    {
        internal bool Destroyed;
        public static Action NullProbe;
        public static bool operator ==(Object a, Object b)
        {
            NullProbe?.Invoke();
            bool x = ReferenceEquals(a, null) || a.Destroyed, y = ReferenceEquals(b, null) || b.Destroyed;
            return x && y || !x && !y && ReferenceEquals(a, b);
        }
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
        public static void Destroy(Object target) { if (target is not null) target.Destroyed = true; }
    }
    public sealed class Transform { public Vector3 position; public Quaternion rotation; }
    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public HideFlags hideFlags; public int layer; public readonly Transform transform = new();
        public void SetActive(bool active) { }
        public T AddComponent<T>() where T : new() => new T();
    }
    public sealed class Camera : Object
    {
        public RenderTexture targetTexture;
        public bool enabled, orthographic, allowHDR, allowMSAA, useOcclusionCulling;
        public float nearClipPlane, farClipPlane, aspect, orthographicSize;
        public int cullingMask; public Color backgroundColor; public CameraClearFlags clearFlags;
        public readonly Transform transform = new();
        public void Render() { }
    }
    public sealed class RenderTexture : Object
    {
        public static Func<RenderTexture, bool> Validate;
        public static RenderTexture active;
        public int width, height; public string name;
        public HideFlags hideFlags; public FilterMode filterMode; public TextureWrapMode wrapMode;
        public bool useMipMap, autoGenerateMips; internal bool Created;
        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite) { this.width = width; this.height = height; }
        public bool Create() { Created = true; return true; }
        public bool IsCreated() => Validate?.Invoke(this) ?? Created;
        public void Release() { Created = false; }
    }
    public static class Time { public static Func<int> Probe; public static int frameCount => Probe?.Invoke() ?? 1; }
    public static class GL { public static void Clear(bool depth, bool color, Color value) { } }
    public struct Color { public Color(float r, float g, float b, float a) { } }
    public struct Vector3 { public Vector3(float x, float y, float z) { } }
    public struct Quaternion { public static Quaternion identity => default; }
    public enum HideFlags { HideAndDontSave }
    public enum CameraClearFlags { SolidColor }
    public enum FilterMode { Bilinear }
    public enum TextureWrapMode { Clamp }
    public enum RenderTextureFormat { ARGB32 }
    public enum RenderTextureReadWrite { Linear }
}
namespace DryCycle.DevUI.DevTool.RWImGui
{
    internal readonly struct WorldMapViewTransform
    {
        internal WorldMapViewTransform(Num.Vector2 origin, Num.Vector2 size, Num.Vector2 pan, float zoom, bool depth = false, int focus = -1, int plane = 0)
        { CanvasOrigin=origin; CanvasSize=size; Pan=pan; Zoom=zoom; DepthEnabled=depth; FocusLayer=focus; PlaneFrame=plane; }
        internal readonly Num.Vector2 CanvasOrigin, CanvasSize, Pan;
        internal readonly float Zoom; internal readonly bool DepthEnabled; internal readonly int FocusLayer, PlaneFrame;
        internal void GetVisibleWorldBounds(out Num.Vector2 min, out Num.Vector2 max) { min=-Pan/Zoom; max=(CanvasSize-Pan)/Zoom; }
    }
    internal sealed class WorldMapTextureBridge
    {
        internal string Error => string.Empty;
        internal void Initialize(BepInEx.Logging.ManualLogSource log) { }
        internal bool Upload(UnityEngine.RenderTexture target) => true;
        internal bool TryPresent(ImGuiNET.ImDrawListPtr draw, UnityEngine.RenderTexture target, Num.Vector2 min, Num.Vector2 max) => true;
        internal void Release(UnityEngine.RenderTexture target) { }
        internal void Reset() { }
    }
}
