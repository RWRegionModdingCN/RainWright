using System;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Detached render-thread viewport snapshot, owned by Rainwright.dll.
/// The gameplay adapter supplies camera identity and scalar values only;
/// native frontend consumers never dereference Unity camera objects.
/// </summary>
public sealed class EditorViewportSnapshot
{
    public static readonly EditorViewportSnapshot Empty = new();

    public bool Available { get; init; }
    public float CameraX { get; init; }
    public float CameraY { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
}

public static class EditorViewportPresentationHub
{
    private static volatile EditorViewportSnapshot current = EditorViewportSnapshot.Empty;
    private static object observedCamera;
    private static float observedX;
    private static float observedY;
    private static float observedWidth;
    private static float observedHeight;

    public static EditorViewportSnapshot Current => current;

    internal static void Publish(
        object cameraIdentity, float x, float y, float width, float height)
    {
        if (cameraIdentity == null)
        {
            Clear();
            return;
        }

        if (ReferenceEquals(observedCamera, cameraIdentity) &&
            observedX == x && observedY == y &&
            observedWidth == width && observedHeight == height &&
            current.Available)
            return;

        current = new EditorViewportSnapshot
        {
            Available = width > 0f && height > 0f,
            CameraX = x,
            CameraY = y,
            Width = width,
            Height = height
        };
        observedCamera = cameraIdentity;
        observedX = x;
        observedY = y;
        observedWidth = width;
        observedHeight = height;
    }

    internal static void Clear()
    {
        current = EditorViewportSnapshot.Empty;
        observedCamera = null;
        observedX = 0f;
        observedY = 0f;
        observedWidth = 0f;
        observedHeight = 0f;
    }
}
