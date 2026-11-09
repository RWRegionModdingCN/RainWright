namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Game-only camera projection: passes detached scalar values to Rainwright.
/// The view cache and immutable published snapshot are owned by the editor DLL.
/// </summary>
internal static class EditorViewportCameraBridge
{
    internal static void Publish(EditorSession session)
    {
        RoomCamera camera = session?.Owner?.game?.cameras != null && session.Owner.game.cameras.Length > 0
            ? session.Owner.game.cameras[0]
            : null;
        if (camera == null)
        {
            EditorViewportPresentationHub.Clear();
            return;
        }

        UnityEngine.Vector2 size = camera.sSize;
        EditorViewportPresentationHub.Publish(
            camera, camera.pos.x, camera.pos.y, size.x, size.y);
    }
}
