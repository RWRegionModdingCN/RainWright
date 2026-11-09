using System;
using System.Numerics;
using DryCycle.DevUI.DevTool.Map.PlayerMap;

internal static class Program
{
    private static int Main()
    {
        // Canon placements from the installed official Downpour SU map. A29 is above B01 in
        // the game/editor, regardless of canvas origin, pan or zoom.
        Vector2 a29 = new(18.0978f, 177.8377f);
        Vector2 b01 = new(-399.9018f, -195.1628f);
        Vector2 origin = new(300, 180), pan = new(450, 220);
        foreach (float zoom in new[] { 0.2f, 1f, 2f })
        {
            Vector2 Screen(Vector2 p) => origin + pan + PlayerMapCanvasCoordinates.Project(p.X, p.Y) * zoom;
            if (Screen(a29).Y >= Screen(b01).Y) throw new Exception("Official SU layout is vertically inverted.");
            Vector2 recovered = PlayerMapCanvasCoordinates.Unproject((Screen(a29) - origin - pan) / zoom);
            if (Vector2.Distance(recovered, a29) > 0.001f) throw new Exception("Hit testing changes author coordinates.");
            // Dragging down must decrease the stored Y; moving Up increases Y and goes up on screen.
            Vector2 drag = PlayerMapCanvasCoordinates.Unproject(new Vector2(0, 60) / zoom);
            if (Math.Abs(Screen(a29 + drag).Y - Screen(a29).Y - 60) > 0.001f)
                throw new Exception("Drag direction or zoom compensation differs from the room preview.");
            if (Screen(a29 + new Vector2(0, 3)).Y >= Screen(a29).Y)
                throw new Exception("Up-arrow movement goes down on the canvas.");
            // A top-edge exit stays above its room centre, matching the mirrored room-local raster.
            Vector2 exit = a29 + new Vector2(0, 35 * 3 * 0.5f);
            if (Screen(exit).Y >= Screen(a29).Y) throw new Exception("Pipe endpoint is on the wrong room edge.");
        }
        Console.WriteLine("PASS: official SU orientation, author-coordinate round trips, drag, keyboard and exit projection at three zoom levels.");
        return 0;
    }
}
