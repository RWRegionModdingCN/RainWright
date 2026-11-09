using System;
using System.Collections;
using System.IO;
using UnityEngine;
using Num=System.Numerics;

public sealed partial class PlaneProbe
{
    private void PresentationBackgroundOwnership()
    {
        var mode=front.GetType("DryCycle.DevUI.DevTool.Core.EditorUiModeState",true);
        mode.GetMethod("SetVanilla",F).Invoke(null,new object[]{true});
        try
        {
            var budget=T("WorldMapBackgroundBudget");
            Check(!(bool)budget.GetMethod("ShouldProcessGeometry",F).Invoke(null,new object[]{null}),"Vanilla ownership stops rebuilt geometry discovery on the real game thread.");
            Check(!(bool)budget.GetMethod("ShouldProcessShortcuts",F).Invoke(null,null),"Vanilla ownership stops rebuilt shortcut discovery on the real game thread.");
        }
        finally {mode.GetMethod("SetVanilla",F).Invoke(null,new object[]{false});}
        results.Add("PASS "+assertions+" game rendering/cache/presentation ownership checks.");
    }

    private void RouteAppearance()
    {
        var store=New("WorldMapConnectionResourceStore");var route=New("ConnectionRouteResource");
        var points=new[]{new Num.Vector2(20,120),new Num.Vector2(75,120),new Num.Vector2(75,40),new Num.Vector2(140,40)};
        Set(route,"ConnectionId","gold-style");Set(route,"Points",points);Set(route,"BasePoints",points);Set(route,"Revision",3L);
        Set(route,"FromLayer",1);Set(route,"ToLayer",2);
        ((IDictionary)Get(store,"routes"))["gold-style"]=route;
        var parent=new GameObject("isolated gold connection");parent.SetActive(false);var renderer=New("WorldMapRetainedConnectionRenderer");
        object View(float zoom,int offset=0)=>Activator.CreateInstance(T("WorldMapViewTransform"),F,null,
            new object[]{Num.Vector2.Zero,new Num.Vector2(720,560),new Num.Vector2(20+offset,20-offset),zoom},null);
        Call(renderer,"SynchronizeVisible",store,new[]{"gold-style"},true,parent.transform,View(1.1f));
        var obj=((IDictionary)Get(renderer,"routeObjects"))["gold-style"];
        var mesh=((MeshFilter)Get(obj,"Filter")).sharedMesh;
        for(int i=0;i<100;i++)
        {
            Call(renderer,"SynchronizeVisible",store,new[]{"gold-style"},true,parent.transform,View(1.1f+i*.007f,i));
            Check(ReferenceEquals(mesh,((MeshFilter)Get(obj,"Filter")).sharedMesh),"Panning/zooming within the stroke band reuses the retained mesh: "+i);
        }
        Call(renderer,"SynchronizeVisible",store,new[]{"gold-style"},true,parent.transform,View(4));
        var thin=((MeshFilter)Get(obj,"Filter")).sharedMesh;
        Check(!ReferenceEquals(mesh,thin),"A new zoom band updates only the line's presentation mesh.");
        Check(ReferenceEquals(Get(route,"Points"),points)&&(long)Get(route,"Revision")==3L,"Stroke width changes never reroute or mutate native connection points.");
        float low=float.MaxValue,high=float.MinValue;int gold=0;
        for(int i=0;i<thin.vertexCount;i++)
        {
            var c=thin.colors32[i];if(c.r!=212||c.g!=171||c.b!=102)continue;gold++;
            var v=thin.vertices[i];if(v.x<25) {low=Math.Min(low,v.y);high=Math.Max(high,v.y);}
        }
        Check(gold>0 && Math.Abs((high-low)*4-4.6f)<.03f,"Actual GPU mesh retains gold and a 4.6-pixel core at 4x zoom.");
        var cameraObject=new GameObject("gold connection camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.orthographic=true;camera.orthographicSize=70;camera.aspect=720f/560;camera.transform.position=new Vector3(85,-65,-100);
        camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.06f,.08f,1);
        var target=new RenderTexture(720,560,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
        parent.SetActive(true);try{camera.Render();}finally{parent.SetActive(false);}
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(720,560,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,720,560),0,0);image.Apply();RenderTexture.active=previous;
        var pixel=image.GetPixel(120,59);
        Check(pixel.r>pixel.g+.1f && pixel.g>pixel.b+.15f,"The real Rain World shader renders the connection gold.");
        File.WriteAllBytes(Path.Combine(output,"gold-connection-real-game.png"),image.EncodeToPNG());
        Call(renderer,"Reset");UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(cameraObject);
        target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
        OverlayRoutingOwnership();
        results.Add("PASS "+assertions+" checks including real GPU gold stroke, readable width and room-overlay routing.");
    }

    private void OverlayRoutingOwnership()
    {
        var scene = New("WorldMapScene"); var resources = New("WorldMapRoomResourceStore");
        for (int i=1;i<=3;i++)
        {
            var room=Call(scene,"GetOrCreateRoom",i);Set(room,"WorldPosition",new Num.Vector2(i*100,i*80));Set(room,"Layer",1);
        }
        var connection=New("WorldMapScene+ConnectionNode");Set(connection,"Id","overlay-owner");
        Set(connection,"FromRoomIndex",1);Set(connection,"ToRoomIndex",2);
        ((IDictionary)Get(scene,"connections"))["overlay-owner"]=connection;
        var links=(IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(connection.GetType()));links.Add(connection);
        var capture=T("WorldMapWorldSpaceRouter").GetMethod("Capture",F);
        var args=new object[capture.GetParameters().Length];args[0]=scene;args[1]=resources;args[2]=links;
        var input=capture.Invoke(null,args);var requests=(IList)Get(input,"Requests");
        Check(requests.Count==1 && (bool)Get(requests[0],"AllowRoomCrossing"),"Production World Map adapter enables room-overlay routing.");
        var output=(IDictionary)T("WorldMapWorldSpaceRouter").GetMethod("Execute",F).Invoke(null,
            new object[]{input,null,default(System.Threading.CancellationToken)});
        Check(output.Count==1 && Get(output["overlay-owner"],"RoutingFailure")==null,"Production adapter completes an overlay route without obstacle search.");
        var store=New("WorldMapConnectionResourceStore");var dirty=New("WorldMapDirtySet");Set(dirty,"FullRebuild",true);
        Call(store,"ApplyDirty",scene,resources,dirty);
        var retained=output["overlay-owner"];((IDictionary)Get(store,"routes"))["overlay-owner"]=retained;
        var metadata=New("WorldMapDirtySet");Call(Get(metadata,"RoomMetadata"),"Add",1);
        Call(store,"ApplyDirty",scene,resources,metadata);
        Check(ReferenceEquals(((IDictionary)Get(store,"routes"))["overlay-owner"],retained),"Selection-only metadata keeps the same route resource.");
        var unrelated=New("WorldMapDirtySet");Call(Get(unrelated,"RoomTransforms"),"Add",3);
        Call(store,"ApplyDirty",scene,resources,unrelated);
        Check(ReferenceEquals(((IDictionary)Get(store,"routes"))["overlay-owner"],retained),"Moving an unrelated room over a connection does not invalidate that route.");
        Set(Call(scene,"GetOrCreateRoom",1),"Layer",2);Call(store,"ApplyDirty",scene,resources,metadata);
        Check(!((IDictionary)Get(store,"routes")).Contains("overlay-owner"),"Changing an endpoint's native layer still invalidates its own route.");
        Call(store,"Reset");Call(resources,"Reset");
    }
}
