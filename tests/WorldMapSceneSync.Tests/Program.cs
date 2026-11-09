using System;
using System.Collections.Generic;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.RWImGui;
using Num = System.Numerics;

internal static class Program
{
    private static int assertions;
    private static void Check(bool value, string message)
    { assertions++; if (!value) throw new InvalidOperationException(message); }

    private static EditorMapPresentationSnapshot Snapshot(int selected = -1, int layer = 1,
        string portType = "Exit", int connected = 1)
    {
        var rooms = new EditorMapRoomSnapshot[90];
        for (int i = 0; i < rooms.Length; i++)
            rooms[i] = new EditorMapRoomSnapshot { RoomIndex = i, Name = "SH_" + i,
                X = i * 100, Layer = i == 0 ? layer : 1, Selected = i == selected,
                Nodes = new[] { new EditorMapRoomNodeSnapshot { NodeIndex = 0, Exit = true,
                    Type = i == 0 ? portType : "Exit", ConnectedRoomIndex = i == 0 ? connected : (i + 1) % 90 } } };
        return new EditorMapPresentationSnapshot {Available = true, RegionName = "SH", Rooms = rooms,
            Connections = new[] { new EditorMapConnectionSnapshot {ConnectionId = "first", FromRoomIndex = 0,
                FromNodeIndex = 0, ToRoomIndex = 1, ToNodeIndex = 0} } };
    }

    private static int Main()
    {
        try
        {
            ProjectionAndAnchors();
            var sync = new WorldMapSceneSynchronizer();
            var render = new WorldMapScene(); var main = new WorldMapScene();
            var dirty = sync.Synchronize(render, Snapshot(), null, 0, -1, default);
            Check(dirty.FullRebuild && dirty.RoomPorts.Count == 90, "Initial scene must capture all ports.");
            var applied = new WorldMapDirtySet();
            WorldMapSceneDelta.Capture(render, dirty).Apply(main, applied);
            long renderRevision = render.SceneRevision, mainRevision = main.SceneRevision;
            var ports = render.Rooms[0].Ports;
            var mailbox=new WorldMapViewTransformMailbox();
            for (int frame = 0; frame < 500; frame++)
            {
                // Every refresh allocates new snapshot, room, port and connection objects, just
                // like the real presentation publisher. Content is unchanged; view state varies.
                var view = new WorldMapViewTransform(new Num.Vector2(4, 5), new Num.Vector2(900, 600),
                    new Num.Vector2(frame, -frame), 1f + frame * .001f, frame % 2 == 0, frame % 4 - 1,
                    new MapPlaneFrame(new Num.Vector2(-1200,-800),new Num.Vector2(1200+frame,800+frame)));
                dirty = sync.Synchronize(render, Snapshot(), null, 0, -1, view);
                mailbox.Publish(view,render.ViewRevision);
                Check(mailbox.TryRead(out var mainView,out _) && mainView.Equals(view),
                    "Presentation thread lost the view/depth/frame while handing it to the render thread.");
                main.SetViewTransform(mainView);
                Check(main.ViewTransform.Equals(render.ViewTransform),"Visible room geometry and UI hit testing use different planes.");
                Check(dirty.IsEmpty, "Equivalent snapshot refresh dirtied the routing graph.");
                var delta = WorldMapSceneDelta.Capture(render, dirty);
                Check(delta == null || delta.IsEmpty,
                    "Equivalent refresh emitted cross-thread room/connection work.");
            }
            Check(render.SceneRevision == renderRevision && main.SceneRevision == mainRevision &&
                ReferenceEquals(ports, render.Rooms[0].Ports), "Stable refresh churned retained port ownership.");
            dirty = sync.Synchronize(render, Snapshot(selected: 0), null, 0, -1, default);
            Check(dirty.RoomMetadata.Count == 1 && dirty.RoomPorts.Count == 0 && dirty.RoomTransforms.Count == 0,
                "Selection must change only display metadata.");
            applied.Clear(); WorldMapSceneDelta.Capture(render, dirty).Apply(main, applied);
            Check(applied.RoomMetadata.Count == 1 && applied.RoomPorts.Count == 0 && main.Rooms[0].Selected,
                "Display-only delta changed main-thread ports.");
            dirty = sync.Synchronize(render, Snapshot(selected: 0, layer: 2), null, 0, -1, default);
            Check(dirty.RoomMetadata.Count == 1 && dirty.RoomPorts.Count == 0 && render.Rooms[0].Layer == 2,
                "Native layer edit must remain visible to routing metadata.");
            dirty = sync.Synchronize(render, Snapshot(selected: 0, layer: 2, portType: "Den"), null, 0, -1, default);
            Check(dirty.RoomPorts.Count == 1 && render.Rooms[0].Ports[0].Type == "Den",
                "Port type edit must invalidate the affected room.");
            dirty = sync.Synchronize(render, Snapshot(selected: 0, layer: 2, portType: "Den", connected: 2),
                null, 0, -1, default);
            Check(dirty.RoomPorts.Count == 1, "Connection target edit was hidden by snapshot equality.");
            var positions = new Dictionary<int, Num.Vector2> { [0] = new Num.Vector2(10, 20) };
            dirty = sync.Synchronize(render, Snapshot(selected: 0, layer: 2, portType: "Den", connected: 2),
                positions, 1, 0, default);
            Check(dirty.RoomTransforms.Count == 1 && dirty.RoomPorts.Count == 0,
                "Dragging a room must update one transform without reloading all ports.");
            Console.WriteLine("PASS: " + assertions + " scene-sync assertions; 500 fresh 90-room snapshots produce zero route invalidations.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void ProjectionAndAnchors()
    {
        var points = new[] { new Num.Vector2(-1234,567), new Num.Vector2(-1000,567),
            new Num.Vector2(-1000,789) };
        var original = (Num.Vector2[])points.Clone();
        for (int from = 0; from < 3; from++)
        for (int to = 0; to < 3; to++)
        {
            var projected = MapDepthProjection.ProjectRoute(points, from, to, true);
            Check(projected[0] == MapDepthProjection.Project(points[0], from, true) &&
                projected[2] == MapDepthProjection.Project(points[2], to, true),
                "Depth bridge endpoints diverge from their room/socket planes.");
            for (int i = 0; i < points.Length; i++)
                Check(points[i] == original[i], "Depth display modified canonical routing/export geometry.");
            Check(Num.Vector2.Distance(MapDepthProjection.Unproject(projected[0], from, true), points[0]) < .001f,
                "Mouse inverse did not recover the author coordinate on this layer.");
        }
        Check(ReferenceEquals(MapDepthProjection.ProjectRoute(points,0,2,false), points),
            "Flat view must use canonical geometry without allocating a projection.");
        Check(MapDepthProjection.Opacity(0,true)==1f && MapDepthProjection.Opacity(1,true)==1f &&
            MapDepthProjection.Opacity(2,true)==1f,
            "Both map canvases must keep every layer fully opaque, including old enabled descriptors.");
        var planeFrame=new MapPlaneFrame(new Num.Vector2(-1400,400),new Num.Vector2(-800,1000));
        foreach(int layer in new[]{0,1,2})
        {
            MapDepthProjection.Corners(planeFrame.Min,planeFrame.Max,layer,true,planeFrame,out var a,out var b,out var c,out var d);
            Check(a==planeFrame.Min && c==planeFrame.Max && b.Y==a.Y && d.X==a.X,
                "Layer style must not tilt, translate or scale any native room rectangle.");
            Check(ReferenceEquals(MapDepthProjection.ProjectRoute(points,layer,2,true,planeFrame),points),
                "Layer style must reuse the native route array without allocation or recomputation.");
        }
        foreach(int layer in new[]{0,1,2})
        {
            var moved=points[0]+new Num.Vector2(13,-23);
            var fromView=MapDepthProjection.Project(points[0],layer,true,planeFrame);
            var toView=MapDepthProjection.Project(moved,layer,true,planeFrame);
            var delta=MapDepthProjection.Unproject(toView,layer,true,planeFrame)-MapDepthProjection.Unproject(fromView,layer,true,planeFrame);
            Check(Num.Vector2.Distance(delta,moved-points[0])<.001f,"Dragging did not invert the full perspective plane.");
            var samePlane=MapDepthProjection.ProjectRoute(points,layer,layer,true,planeFrame);
            for(int i=0;i<points.Length;i++)
                Check(samePlane[i]==MapDepthProjection.Project(points[i],layer,true,planeFrame),"Route is not on its room's full plane.");
        }
        Check(MapDepthProjection.Fade(0xFF397BD4,MapDepthProjection.Opacity(2,true))==0xFF397BD4,"Layer presentation must preserve both terrain RGB and alpha.");
        for(int layer=0;layer<3;layer++)
        foreach(float zoom in new[]{.04f,.5f,1f,8f})
        {
            var viewport = new WorldMapViewTransform(new Num.Vector2(6,12),new Num.Vector2(900,600),
                new Num.Vector2(300,-100),zoom,true,-1,planeFrame);
            var screen=viewport.WorldToCanvas(MapDepthProjection.Project(points[0],layer,true,planeFrame));
            var recovered=MapDepthProjection.Unproject(viewport.CanvasToWorld(screen),layer,true,planeFrame);
            viewport.GetVisibleWorldBounds(out var visibleMin,out var visibleMax);
            // Bounds include only geometry visible in this viewport; horizon-crossing views fall
            // back to the finite detached frame instead of an unbounded spatial-index query.
            Check(!float.IsInfinity(visibleMin.X) && !float.IsNaN(visibleMax.Y),"Viewport query crossed the perspective horizon unsafely.");
            Check(Num.Vector2.Distance(recovered,points[0])<.01f,"Depth mouse inversion drifted during pan/zoom.");
        }
    }
}
