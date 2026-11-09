namespace DryCycle.DevUI.DevTool.Map.PlayerMap;

/// <summary>
/// Game-neutral revision guard for one frozen Player Map render job.
/// The game adapter supplies current revisions; this core never observes
/// Unity objects, installs hooks or reads mutable game state directly.
/// </summary>
internal sealed class PlayerMapRenderRevisionSnapshotGuard
{
    private long mapRevision;
    private int bakeRevision;
    private int terrainRevision;
    private bool captured;

    internal bool Captured => captured;

    internal void Capture(long map, int bake, int terrain)
    {
        mapRevision = map;
        bakeRevision = bake;
        terrainRevision = terrain;
        captured = true;
    }

    internal bool HasChanged(long map, int bake, int terrain) =>
        !captured || map != mapRevision ||
        bake != bakeRevision || terrain != terrainRevision;

    internal void Clear()
    {
        mapRevision = 0;
        bakeRevision = 0;
        terrainRevision = 0;
        captured = false;
    }
}
