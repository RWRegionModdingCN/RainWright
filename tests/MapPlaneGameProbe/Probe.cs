using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;
using Num = System.Numerics;

// Temporary test plugin, installed only for the dedicated command-line run. It uses Rain World's
// real Unity renderer/shaders and the production DLL; it never enters a save or opens an editor.
[BepInPlugin("rainwright.tests.plane-probe", "RainWright plane render probe", "1.0.0")]
public sealed partial class PlaneProbe : BaseUnityPlugin
{
    private const BindingFlags F = BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static;
    private Assembly front;
    private int assertions;
    private string output;
    private readonly List<string> results = new();
    private static string Argument(string name)
    {
        var args=Environment.GetCommandLineArgs();
        int i=Array.IndexOf(args,name); return i>=0&&i+1<args.Length?args[i+1]:null;
    }
    private void Awake()
    {
        output=Argument("--rainwright-plane-check");
        if(output!=null) StartCoroutine(Run());
    }
    private IEnumerator Run()
    {
        Directory.CreateDirectory(output);
        float until=Time.realtimeSinceStartup+60f;
        do
        {
            front=AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x=>x.GetName().Name=="RainWright");
            if(front!=null) break;
            yield return new WaitForSecondsRealtime(.2f);
        } while(Time.realtimeSinceStartup<until);
        yield return new WaitForSecondsRealtime(1f);
        int exit=0;
        try
        {
            if(front==null) throw new Exception("Production RainWright DLL did not load.");
            if(Array.IndexOf(Environment.GetCommandLineArgs(), "--cache-only") >= 0) CacheRetention();
            else if(Argument("--flat-previews")!=null) FlatPreviews(); else { Exercise(); RouteAppearance(); PresentationBackgroundOwnership(); CacheRetention(); }
            string routingAssembly = Argument("--routing-assembly");
            if (routingAssembly != null)
                Check((int)Assembly.LoadFrom(routingAssembly).GetType("Program", true).GetMethod("Main", F)
                    .Invoke(null, new object[] { Array.Empty<string>() }) == 0, "Pure routing regression also passes on the game's actual Mono runtime.");
        }
        catch(Exception ex) { exit=1; results.Add(ex.ToString()); Logger.LogError(ex); }
        File.WriteAllLines(Path.Combine(output,exit==0?"PASS.txt":"FAIL.txt"),results);
        Logger.LogInfo("Plane probe finished: "+exit+"; "+assertions+" checks.");
        Application.Quit(exit);
    }
    private Type T(string name)=>front.GetType("DryCycle.DevUI.DevTool.RWImGui."+name,true);
    private object New(string name)=>Activator.CreateInstance(T(name),true);
    private static object Call(object obj,string method,params object[] args)=>obj.GetType().GetMethod(method,F).Invoke(obj,args);
    private static object Get(object obj,string name)
    {
        var field=obj.GetType().GetField(name,F);return field!=null?field.GetValue(obj):obj.GetType().GetProperty(name,F).GetValue(obj);
    }
    private static void Set(object obj,string name,object value)
    {
        var field=obj.GetType().GetField(name,F);if(field!=null) field.SetValue(obj,value);else obj.GetType().GetProperty(name,F).SetValue(obj,value);
    }
    private void Check(bool ok,string reason) { assertions++;if(!ok)throw new Exception(reason);results.Add("PASS "+reason); }
    private Num.Vector2 Project(Num.Vector2 p,int layer,object frame)=>(Num.Vector2)T("MapDepthProjection").GetMethod("Project",F).Invoke(null,new[]{(object)p,layer,true,frame});

    private void Exercise()
    {
        Check(SystemInfo.graphicsDeviceType!=UnityEngine.Rendering.GraphicsDeviceType.Null,"Real game graphics device: "+SystemInfo.graphicsDeviceName);
        var frame=Activator.CreateInstance(T("MapPlaneFrame"),F,null,new object[]{new Num.Vector2(0,0),new Num.Vector2(500,420)},null);
        var low=new Num.Vector2(float.MaxValue); var high=new Num.Vector2(float.MinValue);
        for(int layer=0;layer<3;layer++)
        {
            object[] args={Get(frame,"Min"),Get(frame,"Max"),layer,true,frame,null,null};
            T("MapDepthProjection").GetMethod("Bounds",F).Invoke(null,args);
            low=Num.Vector2.Min(low,(Num.Vector2)args[5]);high=Num.Vector2.Max(high,(Num.Vector2)args[6]);
        }
        const int width=1280,height=1000;
        float zoom=Math.Min(width/(high.X-low.X),height/(high.Y-low.Y))*.92f;
        var pan=new Num.Vector2(width,height)*.5f-(low+high)*.5f*zoom;
        var uiView=Activator.CreateInstance(T("WorldMapViewTransform"),F,null,new object[]{Num.Vector2.Zero,new Num.Vector2(width,height),pan,zoom,true,-1,frame},null);
        var mailbox=New("WorldMapViewTransformMailbox");Call(mailbox,"Publish",uiView,12L);
        object[] read={null,null};Check((bool)Call(mailbox,"TryRead",read),"Complete view can cross the production mailbox.");
        var view=read[0];Check(view.Equals(uiView),"UI and game thread use identical depth/frame/pan/zoom.");
        var scene=New("WorldMapScene");Call(scene,"SetViewTransform",view);
        var resources=New("WorldMapRoomResourceStore");var rooms=(IDictionary)Get(resources,"rooms");
        var positions=new Dictionary<int,Num.Vector2>();
        var texture=new Texture2D(80,40,TextureFormat.RGBA32,false);
        var pixels=new Color32[80*40];
        for(int y=0;y<40;y++)for(int x=0;x<80;x++)
            pixels[y*80+x]= y<12?new Color32(32,90,200,255):x<20?new Color32(60,65,75,255):x>50?new Color32(180,70,70,255):new Color32(150,155,160,255);
        texture.SetPixels32(pixels);texture.filterMode=FilterMode.Point;texture.Apply();
        var builder=T("RoomGeometryBuilder");
        for(int layer=0;layer<3;layer++)for(int n=0;n<2;n++)
        {
            int id=layer*2+n;var position=new Num.Vector2(20+240*n,20+130*layer);
            positions[id]=position;var node=Call(scene,"GetOrCreateRoom",id);
            Set(node,"Layer",layer);Set(node,"WorldPosition",position);Set(node,"TransformRevision",1L);
            var visual=Activator.CreateInstance(front.GetType("DryCycle.DevUI.DevTool.Map.EditorMapRoomVisualSnapshot",true),true);
            Set(visual,"WidthTiles",80f);Set(visual,"HeightTiles",40f);
            var geometry=builder.GetMethod("BuildForThumbnail",F).Invoke(null,new object[]{id,visual,1});
            var resource=New("WorldMapRoomResourceStore+RoomResource");Set(resource,"Geometry",geometry);Set(resource,"GeometryGeneration",1L);
            var thumbnail=Get(resource,"Thumbnail");
            var source=Activator.CreateInstance(T("WorldMapLegacyRoomSourceService+RoomTextureSource"),F,null,new object[]{texture,new Rect(0,0,1,1),80f,40f,id+1,""},null);
            Call(thumbnail,"Stage",source);Call(thumbnail,"CommitPending");rooms[id]=resource;
        }
        var parent=new GameObject("RainWright isolated plane fixture");parent.SetActive(false);
        var renderer=New("WorldMapRetainedRoomRenderer");int[] ids={0,1,2,3,4,5};
        for(int i=0;i<8;i++) Call(renderer,"SynchronizeVisible",scene,resources,ids,parent.transform);
        Check(!(bool)Get(renderer,"HasPendingUploads"),"All six room meshes completed their bounded uploads.");
        var table=(IDictionary)Get(renderer,"roomObjects");
        var room0=table[0];var mesh0=((MeshFilter)Get(room0,"BaseFilter")).sharedMesh;
        for(int i=0;i<100;i++)
        {
            var navigation=Activator.CreateInstance(T("WorldMapViewTransform"),F,null,new object[]{Num.Vector2.Zero,new Num.Vector2(width,height),pan+new Num.Vector2(i,-i),zoom+.001f*i,true,-1,frame},null);
            Call(scene,"SetViewTransform",navigation);Call(renderer,"SynchronizeVisible",scene,resources,ids,parent.transform);
            Check(ReferenceEquals(mesh0,((MeshFilter)Get(table[0],"BaseFilter")).sharedMesh),"Navigation frame "+i+" reuses the production native mesh.");
        }
        Call(scene,"SetViewTransform",view);
        // Show the complete region planes in the native capture as well as the room thumbnails.
        // Same shared projection as the production UI; thin outlines, no room side faces/boxes.
        // A real move updates one transform and must not re-upload either raster or overlay.
        var moved=Call(scene,"GetOrCreateRoom",0);Set(moved,"WorldPosition",positions[0]+new Num.Vector2(5,-7));Set(moved,"TransformRevision",2L);
        Call(renderer,"SynchronizeVisible",scene,resources,ids,parent.transform);
        Check(ReferenceEquals(mesh0,((MeshFilter)Get(table[0],"BaseFilter")).sharedMesh),"Room dragging reuses the four-corner native mesh.");
        Check(((GameObject)Get(table[0],"Root")).transform.localPosition==new Vector3(positions[0].X+5,-positions[0].Y+7,0),"Dragging updates exactly the native room transform.");
        Set(moved,"WorldPosition",positions[0]);Set(moved,"TransformRevision",3L);Call(renderer,"SynchronizeVisible",scene,resources,ids,parent.transform);
        for(int id=0;id<6;id++)
        {
            var mesh=((MeshFilter)Get(table[id],"BaseFilter")).sharedMesh;
            Check(mesh.vertexCount==4,"Room "+id+" uses one flat quad, with no perspective tessellation.");
            Check(mesh.colors32[0].a==255,"Room "+id+" remains fully opaque regardless of layer or old depth flags.");
            Check(((MeshRenderer)Get(table[id],"BaseRenderer")).sortingOrder==(2-id/2)*4,"Room "+id+" has native foreground-over-background ordering.");
        }
        var cameraObject=new GameObject("RainWright probe camera");var camera=cameraObject.AddComponent<Camera>();
        camera.enabled=false;camera.orthographic=true;camera.orthographicSize=height/zoom*.5f;camera.aspect=width/(float)height;
        camera.transform.position=new Vector3((width*.5f-pan.X)/zoom,-(height*.5f-pan.Y)/zoom,-100f);
        camera.nearClipPlane=.1f;camera.farClipPlane=500f;camera.cullingMask=1<<31;
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.06f,.08f,1f);
        var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
        parent.SetActive(true);try{camera.Render();}finally{parent.SetActive(false);}
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(output,"single-canvas-real-game-render.png"),image.EncodeToPNG());
        foreach(var entry in positions)
        {
            int layer=entry.Key/2;
            Color Sample(Num.Vector2 local)
            { var p=pan+Project(entry.Value+local,layer,frame)*zoom;return image.GetPixel((int)p.X,height-1-(int)p.Y); }
            var blue=Sample(new Num.Vector2(60,68));var red=Sample(new Num.Vector2(130,40));var air=Sample(new Num.Vector2(70,40));
            Check(blue.b>blue.r+.2f,"Room "+entry.Key+" retains blue water through the actual shader/UV projection.");
            Check(red.r>red.g+.15f,"Room "+entry.Key+" retains red structure, not a blank grey block.");
            Check(air.r>.2f,"Room "+entry.Key+" contains its actual thumbnail at the UI-predicted location.");
        }
        // Overlapping all three layers must resolve to the opaque foreground, independent of upload order.
        for(int id=2;id<6;id+=2) { var node=Call(scene,"GetOrCreateRoom",id);Set(node,"WorldPosition",positions[0]);Set(node,"TransformRevision",2L); }
        Call(renderer,"SynchronizeVisible",scene,resources,ids,parent.transform);
        parent.SetActive(true);try{camera.Render();}finally{parent.SetActive(false);}
        RenderTexture.active=target;image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=previous;
        var sample=pan+(positions[0]+new Num.Vector2(70,40))*zoom;var top=image.GetPixel((int)sample.X,height-1-(int)sample.Y);
        Check(Math.Abs(top.r-150f/255f)<.03f && Math.Abs(top.g-155f/255f)<.03f,"Actual overlapping layers retain the opaque L0 thumbnail.");
        Check((bool)Get(table[0],"Textured"),"Atlas-backed rendering is active.");
        UnityEngine.Object.DestroyImmediate(texture);
        Call(renderer,"SynchronizeVisible",scene,resources,ids,parent.transform);
        Check(!(bool)Get(table[0],"Textured"),"Destroyed atlas transitions out of the old textured path.");
        Call(renderer,"Reset");UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(cameraObject);
        target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
        results.Add("PASS "+assertions+" assertions, actual Rain World Unity/graphics device and installed production DLL.");
    }
}
