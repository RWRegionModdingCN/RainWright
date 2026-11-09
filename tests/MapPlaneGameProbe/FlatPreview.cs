using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Num=System.Numerics;

public sealed partial class PlaneProbe
{
    // Preview layouts only: all room meshes use the production flat (depth-disabled) path.
    // Changes to room offsets/opacity here never change installed game presentation or author data.
    private void FlatPreviews()
    {
        if(Argument("--single-plane-preview")!=null)
        {
            RenderFlatPreview(false,"C-single-plane-depth.png");
            results.Add("Single orthographic canvas with depth ordering/opacity preview generated. Production map code was unchanged.");
            return;
        }
        RenderFlatPreview(true,"A-flat-separated.png");
        RenderFlatPreview(false,"B-flat-overlaid.png");
        results.Add("Two orthographic 2D layout previews generated. Production map code was unchanged.");
    }

    private void RenderFlatPreview(bool separated,string filename)
    {
        bool single=filename.StartsWith("C-",StringComparison.Ordinal);
        const int width=1280,height=1000;
        var min=separated?new Num.Vector2(-30,-40):new Num.Vector2(-30,-40);
        var max=separated?new Num.Vector2(560,820):new Num.Vector2(620,400);
        float zoom=Math.Min(width/(max.X-min.X),height/(max.Y-min.Y))*.9f;
        var pan=new Num.Vector2(width,height)*.5f-(min+max)*.5f*zoom;
        var view=Activator.CreateInstance(T("WorldMapViewTransform"),F,null,new object[]{Num.Vector2.Zero,new Num.Vector2(width,height),pan,zoom},null);
        var scene=New("WorldMapScene");Call(scene,"SetViewTransform",view);
        var resources=New("WorldMapRoomResourceStore");var table=(IDictionary)Get(resources,"rooms");
        var textures=new List<Texture2D>();
        for(int layer=2;layer>=0;layer--)
        {
            var texture=new Texture2D(80,40,TextureFormat.RGBA32,false);textures.Add(texture);
            var pixels=new Color32[80*40];byte opacity=separated?(byte)255:layer==0?(byte)255:layer==1?(byte)185:(byte)115;
            for(int y=0;y<40;y++)for(int x=0;x<80;x++)
                pixels[y*80+x]=y<9?new Color32(32,90,200,opacity):x<18?new Color32(60,65,75,opacity):x>53?new Color32(180,70,70,opacity):new Color32(150,155,160,opacity);
            texture.SetPixels32(pixels);texture.filterMode=FilterMode.Point;texture.Apply();
            var offset=single?Num.Vector2.Zero:separated?new Num.Vector2(0,(2-layer)*280):new Num.Vector2((2-layer)*45,(2-layer)*45);
            for(int n=0;n<2;n++)
            {
                int id=layer*2+n;var pos=offset+(n==0?new Num.Vector2(40,45):new Num.Vector2(285,140));
                if(single)
                {
                    Num.Vector2[] positions={new(50,110),new(300,210),new(150,50),new(250,160),new(280,15),new(90,250)};
                    pos=positions[id];
                }
                var node=Call(scene,"GetOrCreateRoom",id);Set(node,"Layer",layer);Set(node,"WorldPosition",pos);Set(node,"TransformRevision",1L);
                var visual=Activator.CreateInstance(front.GetType("DryCycle.DevUI.DevTool.Map.EditorMapRoomVisualSnapshot",true),true);
                Set(visual,"WidthTiles",80f);Set(visual,"HeightTiles",40f);
                var geometry=T("RoomGeometryBuilder").GetMethod("BuildForThumbnail",F).Invoke(null,new object[]{id,visual,1});
                var resource=New("WorldMapRoomResourceStore+RoomResource");Set(resource,"Geometry",geometry);Set(resource,"GeometryGeneration",1L);
                var thumbnail=Get(resource,"Thumbnail");
                var source=Activator.CreateInstance(T("WorldMapLegacyRoomSourceService+RoomTextureSource"),F,null,new object[]{texture,new Rect(0,0,1,1),80f,40f,id+1,""},null);
                Call(thumbnail,"Stage",source);Call(thumbnail,"CommitPending");table[id]=resource;
            }
        }
        var parent=new GameObject("Flat layout preview");parent.SetActive(false);
        var renderer=New("WorldMapRetainedRoomRenderer");for(int i=0;i<8;i++)Call(renderer,"SynchronizeVisible",scene,resources,new[]{0,1,2,3,4,5},parent.transform);
        var material=(Material)Get(renderer,"colorMaterial");
        var font=Resources.GetBuiltinResource<Font>("Arial.ttf");
        for(int layer=2;layer>=0;layer--)
        {
            if(single) continue;
            var offset=separated?new Num.Vector2(0,(2-layer)*280):new Num.Vector2((2-layer)*45,(2-layer)*45);
            var a=offset;var b=offset+new Num.Vector2(520,240);
            var vertices=new List<Vector3>();var colors=new List<Color32>();var indices=new List<int>();
            void Quad(Num.Vector2 p,Num.Vector2 q,Num.Vector2 r,Num.Vector2 s,Color32 color)
            {
                int start=vertices.Count;foreach(var point in new[]{p,q,r,s}){vertices.Add(new Vector3(point.X,-point.Y,0));colors.Add(color);}
                indices.AddRange(new[]{start,start+1,start+2,start,start+2,start+3});
            }
            var corners=new[]{a,new Num.Vector2(b.X,a.Y),b,new Num.Vector2(a.X,b.Y)};
            Quad(corners[0],corners[1],corners[2],corners[3],new Color32(238,238,238,5));
            for(int i=0;i<4;i++)
            {
                var p=corners[i];var q=corners[(i+1)%4];var delta=q-p;var normal=Num.Vector2.Normalize(new Num.Vector2(-delta.Y,delta.X))*.7f;
                Quad(p-normal,q-normal,q+normal,p+normal,new Color32(165,165,165,180));
            }
            var plane=new GameObject("2D plane L"+layer);plane.layer=31;plane.transform.SetParent(parent.transform,false);
            var mesh=new Mesh();mesh.vertices=vertices.ToArray();mesh.colors32=colors.ToArray();mesh.triangles=indices.ToArray();mesh.RecalculateBounds();
            plane.AddComponent<MeshFilter>().sharedMesh=mesh;var mr=plane.AddComponent<MeshRenderer>();mr.sharedMaterial=material;mr.sortingOrder=-2;
            var label=new GameObject("Layer label");label.layer=31;label.transform.SetParent(parent.transform,false);
            label.transform.localPosition=new Vector3(a.X,-a.Y+20,-.1f);
            var text=label.AddComponent<TextMesh>();text.text="L"+layer;text.font=font;text.fontSize=28;text.characterSize=4f;text.anchor=TextAnchor.MiddleLeft;
            var tr=label.GetComponent<MeshRenderer>();tr.sharedMaterial=font.material;tr.sortingOrder=20;
        }
        if(single)
        {
            Num.Vector2[] positions={new(50,110),new(300,210),new(150,50),new(250,160),new(280,15),new(90,250)};
            for(int id=0;id<positions.Length;id++)
            {
                int layer=id/2;var pos=positions[id];
                var label=new GameObject("Room layer label");label.layer=31;label.transform.SetParent(parent.transform,false);
                label.transform.localPosition=new Vector3(pos.X,-pos.Y+13,-.1f);
                var text=label.AddComponent<TextMesh>();text.text="Room "+(id+1)+"  L"+layer;text.font=font;text.fontSize=28;text.characterSize=3f;text.anchor=TextAnchor.MiddleLeft;
                text.color=new Color(1,1,1,layer==0?1f:layer==1?.72f:.45f);
                var tr=label.GetComponent<MeshRenderer>();tr.sharedMaterial=font.material;tr.sortingOrder=20;
            }
        }
        var cameraObject=new GameObject("Flat preview camera");var camera=cameraObject.AddComponent<Camera>();camera.enabled=false;
        camera.orthographic=true;camera.orthographicSize=height/zoom*.5f;camera.aspect=width/(float)height;
        camera.transform.position=new Vector3((width*.5f-pan.X)/zoom,-(height*.5f-pan.Y)/zoom,-100);
        camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.05f,.06f,.08f,1);
        var target=new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;
        parent.SetActive(true);try{camera.Render();}finally{parent.SetActive(false);}
        var previous=RenderTexture.active;RenderTexture.active=target;
        var image=new Texture2D(width,height,TextureFormat.RGBA32,false);image.ReadPixels(new Rect(0,0,width,height),0,0);image.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.Combine(output,filename),image.EncodeToPNG());
        Call(renderer,"Reset");UnityEngine.Object.DestroyImmediate(parent);UnityEngine.Object.DestroyImmediate(cameraObject);
        foreach(var texture in textures)UnityEngine.Object.DestroyImmediate(texture);
        target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);
    }
}
