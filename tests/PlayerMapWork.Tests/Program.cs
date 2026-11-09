using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Map.PlayerMap;

internal static class Program
{
    private static string root;
    private static int checks;
    private static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    private static string Room(string name, int width, int height)
    {
        string path=Path.Combine(root, name+".txt");
        string[] lines=Enumerable.Repeat("",12).ToArray(); lines[1]=width+"*"+height+"|-1|";
        lines[11]=string.Concat(Enumerable.Repeat("0|",width*height)); File.WriteAllLines(path,lines);
        WorldLoader.Paths[name]=path; return path;
    }
    private static void Pump(Func<bool> done)
    {
        var timer=Stopwatch.StartNew();
        while (!done() && timer.ElapsedMilliseconds<5000) { RoomMapBakeCache.ProcessPending(4); UnityEngine.Time.frameCount++; Thread.Sleep(1); }
        Check(done(),"Detached work did not complete within five seconds.");
    }
    private static int Main()
    {
        root=Path.Combine(Path.GetTempPath(),"rainwright-player-map-work-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            Room("OLD1",400,125); Room("OLD2",400,125); Room("THIRD",8,8); Room("NEW",9,7);
            RoomPreprocessor.Started=new CountdownEvent(2); RoomPreprocessor.Release.Reset();
            RoomMapBakeCache.Request(1,"OLD1"); RoomMapBakeCache.Request(2,"OLD2"); RoomMapBakeCache.Request(3,"THIRD");
            var timer=Stopwatch.StartNew(); RoomMapBakeCache.ProcessPending(20); timer.Stop();
            Check(timer.ElapsedMilliseconds<250,"Scheduling waited for the blocked room decoder.");
            // A cold metadata lookup may consume its first frame's deadline; subsequent pumps
            // must schedule the second worker without waiting for the first decoder.
            for (int i=0;i<5 && RoomMapBakeCache.ActiveWorkers<2;i++) RoomMapBakeCache.ProcessPending(20);
            Check(RoomPreprocessor.Started.Wait(2000),"Both bounded background workers did not start.");
            Check(RoomMapBakeCache.ActiveWorkers==2,"More than two room workers started.");
            timer.Restart(); RoomMapBakeCache.Clear(); RoomMapBakeCache.Request(1,"NEW"); RoomMapBakeCache.ProcessPending(20); timer.Stop();
            Check(timer.ElapsedMilliseconds<250 && RoomMapBakeCache.ActiveWorkers==2,"Region reset waited for or multiplied old workers.");
            RoomPreprocessor.Started=null; RoomPreprocessor.Release.Set();
            Pump(()=>RoomMapBakeCache.GetSnapshot(1).Status==RoomMapBakeStatus.Ready);
            Check(RoomMapBakeCache.GetSnapshot(1).Width==9,"Retired room completion overwrote the reused room index.");

            string changing=Room("CHANGING",5,5); RoomPreprocessor.Started=new CountdownEvent(1); RoomPreprocessor.Release.Reset();
            RoomMapBakeCache.Request(4,"CHANGING"); RoomMapBakeCache.ProcessPending(4);
            Check(RoomPreprocessor.Started.Wait(2000),"Changing-source fixture did not begin.");
            Room("CHANGING",17,11); RoomPreprocessor.Started=null; RoomPreprocessor.Release.Set();
            Pump(()=>RoomMapBakeCache.GetSnapshot(4).Status==RoomMapBakeStatus.Ready);
            Check(RoomMapBakeCache.GetSnapshot(4).Width==17,"Source changes were not retried before publication.");
            Check(WorldLoader.Errors.Any(e=>e is IOException),"Source-change failure/recovery discarded the original exception.");
            Check(RoomMapBakeCache.TryGetReady(4,out RoomMapBake original),"Completed static bake is unavailable.");
            var expected=RoomMapSemanticCompiler.Compile(4,"CHANGING",RoomMapTextDecoder.Parse(File.ReadAllLines(changing)),changing);
            Check(original.Pixels.Length==expected.Pixels.Length && original.Pixels.Zip(expected.Pixels,(a,b)=>a.Kind==b.Kind && a.Water==b.Water).All(x=>x),"Background decoding changed the pixel semantics.");

            MapRoomGeometryPresentationHub.Shared=new WorldMapRoomSource {Bake=expected};
            RoomMapBakeCache.Request(6,"CHANGING"); RoomMapBakeCache.ProcessPending(4);
            Check(RoomMapBakeCache.TryGetReady(6,out RoomMapBake shared) && shared.RoomIndex==6 && ReferenceEquals(shared.Pixels,expected.Pixels),"World Map decode was duplicated or published with the wrong room identity.");

            PlayerMapTerrainBakeBridge.Enable(new BepInEx.Logging.ManualLogSource());
            MapRoomGeometryPresentationHub.Runs=new[] {new EditorMapRectSnapshot {X=1,Y=1,Width=2,Height=2,Kind=EditorMapGeometryKind.Solid}};
            Check(!RoomMapBakeCache.TryGetReady(4,out _),"An unmerged terrain request claimed to be ready.");
            int before=RoomMapBakeCache.Revision;
            Pump(()=>RoomMapBakeCache.GetSnapshot(4).Status==RoomMapBakeStatus.Ready);
            Check(RoomMapBakeCache.TryGetReady(4,out RoomMapBake enhanced) && enhanced.Pixels[18].Kind==RoomMapPixelKind.Solid,"Detached terrain overlay was not committed.");
            Check(original.Pixels[18].Kind==RoomMapPixelKind.Air && RoomMapBakeCache.Revision>before,"Terrain work changed its source or failed to invalidate readiness.");
            MapRoomGeometryPresentationHub.Runs=Array.Empty<EditorMapRectSnapshot>(); MapRoomGeometryPresentationHub.Revision++;
            Check(RoomMapBakeCache.TryGetReady(4,out RoomMapBake restored) && ReferenceEquals(restored,original),"Removing terrain failed to restore the unchanged source bake.");

            string broken=Path.Combine(root,"BROKEN.txt"); File.WriteAllText(broken,"malformed"); WorldLoader.Paths["BROKEN"]=broken;
            RoomMapBakeCache.Request(5,"BROKEN"); Pump(()=>RoomMapBakeCache.GetSnapshot(5).Status==RoomMapBakeStatus.Failed);
            Check(RoomMapBakeCache.GetSnapshot(5).Error.Contains(nameof(InvalidDataException)),"Worker failure was hidden or reported as success.");

            var player=new PlayerMapPresentationSnapshot {Available=true,RegionName="SU",Revision=1,Rooms=new[] {new PlayerMapRoomSnapshot {RoomIndex=4,Name="CHANGING",Bake=RoomMapBakeCache.GetSnapshot(4)}}};
            var world=new EditorMapPresentationSnapshot {Available=true,RegionName="SU"};
            Check(PlayerMapPreflightDiagnostics.Evaluate(player,world).Computing,"Preflight performed its full solve synchronously.");
            PlayerMapPreflightDiagnostics.Reset();
            player=new PlayerMapPresentationSnapshot {Available=true,RegionName="B5",Revision=2,Rooms=player.Rooms};
            world=new EditorMapPresentationSnapshot {Available=true,RegionName="B5"};
            PlayerMapPreflightSnapshot preflight=null;
            Pump(()=>!(preflight=PlayerMapPreflightDiagnostics.Evaluate(player,world)).Computing);
            Check(preflight.CanRender && preflight.RegionName=="B5","Obsolete preflight was accepted after reset.");
            Check(ReferenceEquals(preflight,PlayerMapPreflightDiagnostics.Evaluate(player,world)),"Stable preflight results were rebuilt.");
            player=new PlayerMapPresentationSnapshot {Available=true,RegionName="B5",Revision=3,Rooms=new[] {
                new PlayerMapRoomSnapshot {RoomIndex=4,Name="A",Layer=1,Bake=RoomMapBakeCache.GetSnapshot(4)},
                new PlayerMapRoomSnapshot {RoomIndex=7,Name="B",Layer=1,Bake=RoomMapBakeCache.GetSnapshot(4)},
                new PlayerMapRoomSnapshot {RoomIndex=8,Name="C",Layer=2,Bake=RoomMapBakeCache.GetSnapshot(4)}}};
            Pump(()=>!(preflight=PlayerMapPreflightDiagnostics.Evaluate(player,world)).Computing);
            Check(preflight.CanRender && preflight.OverlapPairs==0 && preflight.OverlapRoomIndices.Length==0 && !preflight.Warnings.Any(w=>w.Contains("Overlap")),"Intentional overlapping rooms must remain renderable without overlap diagnostics, on the same or different native layers.");
            Console.WriteLine("PASS: "+checks+" assertions; nonblocking room/terrain/preflight work, worker bounds, region resets, source changes and original errors.");
            return 0;
        }
        finally
        {
            RoomPreprocessor.Release.Set();
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) throw new Exception("Fixture cleanup escaped temp.");
            Directory.Delete(root,true);
        }
    }
}
