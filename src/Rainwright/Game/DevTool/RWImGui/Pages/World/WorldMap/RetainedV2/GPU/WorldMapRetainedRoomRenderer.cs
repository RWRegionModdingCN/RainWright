using System;
using System.Collections.Generic;
using System.Diagnostics;
using DryCycle.DevUI.DevTool.Map;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Retained Unity room renderer for the V2 off-screen surface.
///
/// Room mesh/resource rebuilds follow room-resource generations; moving a room updates only its
/// GameObject transform. Pan/zoom never rebuilds room meshes.
/// </summary>
internal sealed class WorldMapRetainedRoomRenderer
{
    private sealed class RoomObject
    {
        internal GameObject Root;
        internal MeshFilter BaseFilter;
        internal MeshRenderer BaseRenderer;
        internal MeshFilter OverlayFilter;
        internal MeshRenderer OverlayRenderer;
        internal long GeometryGeneration = long.MinValue;
        internal long ThumbnailGeneration = long.MinValue;
        internal long TransformRevision = long.MinValue;
        internal int Layer = int.MinValue;
        internal float WidthTiles, HeightTiles;
        internal bool Textured;
        internal bool DepthEnabled;
    }

    private const float TileDisplaySize = 2f;

    private readonly Dictionary<int, RoomObject> roomObjects = new();
    private readonly Dictionary<int, Material> textureMaterials = new();
    private readonly HashSet<int> visibleNow = new();
    private readonly HashSet<int> visiblePrevious = new();

    private GameObject root;
    private Material colorMaterial;
    private Shader spriteShader;

    internal int RetainedRoomCount => roomObjects.Count;
    internal bool HasPendingUploads { get; private set; }

    internal void ApplyDirty(WorldMapDirtySet dirty)
    {
        if (dirty == null) return;
        foreach (int roomIndex in dirty.RemovedRooms)
        {
            visibleNow.Remove(roomIndex);
            visiblePrevious.Remove(roomIndex);
            RemoveRoom(roomIndex);
        }
    }

    internal bool SynchronizeVisible(
        WorldMapScene scene,
        WorldMapRoomResourceStore resources,
        IReadOnlyList<int> visibleRoomIds,
        Transform renderScene)
    {
        if (scene == null || resources == null || visibleRoomIds == null)
            return false;
        if (!EnsureResources(renderScene)) return false;

        visibleNow.Clear();
        HasPendingUploads = false;
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * .0015d);
        int uploads = 0;

        for (int i = 0; i < visibleRoomIds.Count; i++)
        {
            int roomIndex = visibleRoomIds[i];
            if (!scene.TryGetRoom(roomIndex, out WorldMapScene.RoomNode room))
                continue;

            visibleNow.Add(roomIndex);
            if (!roomObjects.TryGetValue(roomIndex, out RoomObject obj))
            {
                if (uploads >= 6 || (uploads > 0 && Stopwatch.GetTimestamp() >= deadline))
                { HasPendingUploads = true; continue; }
                obj = GetOrCreate(roomIndex);
            }

            var view = scene.ViewTransform;
            bool projectionChanged = obj.Layer != room.Layer || obj.DepthEnabled != view.DepthEnabled;
            if (resources.TryGet(roomIndex, out WorldMapRoomResourceStore.RoomResource resource))
            {
                bool geometryChanged = obj.GeometryGeneration != resource.GeometryGeneration;
                // Unity may destroy an atlas texture without advancing our descriptor generation.
                // Leaving the old textured mesh/material active then displays its white fallback.
                bool thumbnailChanged = obj.ThumbnailGeneration != resource.Thumbnail.Generation ||
                    obj.Textured != resource.Thumbnail.HasCommitted;
                if (geometryChanged || thumbnailChanged || projectionChanged)
                {
                    if (uploads >= 6 || (uploads > 0 && Stopwatch.GetTimestamp() >= deadline))
                    { HasPendingUploads = true; if (projectionChanged) obj.Root.SetActive(false); continue; }
                    else
                    {
                        RebuildRoom(obj, resource, geometryChanged, thumbnailChanged, room, view, projectionChanged);
                        obj.GeometryGeneration = resource.GeometryGeneration;
                        obj.ThumbnailGeneration = resource.Thumbnail.Generation;
                        uploads++;
                    }
                }
            }
            else if (obj.GeometryGeneration != -1L || projectionChanged)
            {
                if (uploads >= 6 || (uploads > 0 && Stopwatch.GetTimestamp() >= deadline))
                { HasPendingUploads = true; continue; }
                // Resource capture may lag the first visible frame. Render a visible neutral room
                // rather than omitting it or showing an uninitialised/black surface.
                RoomGeometryBlob neutral = RoomGeometryBuilder.BuildNeutral(roomIndex);
                ReplaceMesh(obj.BaseFilter, BuildColorMesh(neutral, customOnly: false, room, view));
                obj.BaseRenderer.sharedMaterial = colorMaterial;
                ReplaceMesh(obj.OverlayFilter, null);
                obj.GeometryGeneration = -1L;
                obj.ThumbnailGeneration = -1L;
                obj.Textured = false;
                uploads++;
            }

            obj.Root.transform.localPosition = new Vector3(room.WorldPosition.X, -room.WorldPosition.Y, room.Layer * .02f);
            obj.Root.transform.localScale = Vector3.one;
            obj.BaseRenderer.sortingOrder = (2 - room.Layer) * 4;
            obj.OverlayRenderer.sortingOrder = (2 - room.Layer) * 4 + 1;
            obj.TransformRevision = room.TransformRevision;
            obj.Layer = room.Layer;
            obj.DepthEnabled = view.DepthEnabled;

            if (!obj.Root.activeSelf)
                obj.Root.SetActive(true);
        }

        foreach (int roomIndex in visiblePrevious)
        {
            if (visibleNow.Contains(roomIndex)) continue;
            if (roomObjects.TryGetValue(roomIndex, out RoomObject obj) && obj.Root.activeSelf)
                obj.Root.SetActive(false);
        }

        visiblePrevious.Clear();
        visiblePrevious.UnionWith(visibleNow);
        return true;
    }

    internal void Reset()
    {
        HasPendingUploads = false;
        foreach (RoomObject room in roomObjects.Values)
            DestroyRoomObject(room);
        roomObjects.Clear();

        foreach (Material material in textureMaterials.Values)
            if (material != null) UnityEngine.Object.Destroy(material);
        textureMaterials.Clear();

        if (colorMaterial != null) UnityEngine.Object.Destroy(colorMaterial);
        colorMaterial = null;
        spriteShader = null;

        if (root != null) UnityEngine.Object.Destroy(root);
        root = null;
        visibleNow.Clear();
        visiblePrevious.Clear();
    }

    private bool EnsureResources(Transform renderScene)
    {
        if (renderScene == null) throw new ArgumentNullException(nameof(renderScene));
        if (root == null)
        {
            root = new GameObject("DryCycle.WorldMapV2.Rooms")
            {
                hideFlags = HideFlags.HideAndDontSave,
                layer = WorldMapRenderTextureSurface.RenderLayer
            };
        }
        if (root.transform.parent != renderScene)
            root.transform.SetParent(renderScene, false);

        if (spriteShader == null)
        {
            spriteShader =
                Shader.Find("Sprites/Default") ??
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("UI/Default");
            if (spriteShader == null) return false;
        }

        if (colorMaterial == null)
        {
            colorMaterial = new Material(spriteShader)
            {
                name = "DryCycle.WorldMapV2.Color",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = Texture2D.whiteTexture
            };
        }

        return true;
    }

    private RoomObject GetOrCreate(int roomIndex)
    {
        if (roomObjects.TryGetValue(roomIndex, out RoomObject existing))
            return existing;

        GameObject roomRoot = new("Room_" + roomIndex)
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = WorldMapRenderTextureSurface.RenderLayer
        };
        roomRoot.transform.SetParent(root.transform, false);

        GameObject baseObject = new("Base")
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = WorldMapRenderTextureSurface.RenderLayer
        };
        baseObject.transform.SetParent(roomRoot.transform, false);
        MeshFilter baseFilter = baseObject.AddComponent<MeshFilter>();
        MeshRenderer baseRenderer = baseObject.AddComponent<MeshRenderer>();

        GameObject overlayObject = new("Overlay")
        {
            hideFlags = HideFlags.HideAndDontSave,
            layer = WorldMapRenderTextureSurface.RenderLayer
        };
        overlayObject.transform.SetParent(roomRoot.transform, false);
        overlayObject.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        MeshFilter overlayFilter = overlayObject.AddComponent<MeshFilter>();
        MeshRenderer overlayRenderer = overlayObject.AddComponent<MeshRenderer>();
        overlayRenderer.sharedMaterial = colorMaterial;

        RoomObject created = new()
        {
            Root = roomRoot,
            BaseFilter = baseFilter,
            BaseRenderer = baseRenderer,
            OverlayFilter = overlayFilter,
            OverlayRenderer = overlayRenderer
        };
        roomObjects.Add(roomIndex, created);
        return created;
    }

    private void RebuildRoom(
        RoomObject obj,
        WorldMapRoomResourceStore.RoomResource resource,
        bool geometryChanged,
        bool thumbnailChanged, WorldMapScene.RoomNode room, WorldMapViewTransform view, bool projectionChanged)
    {
        RoomGeometryBlob geometry = resource.Geometry ?? RoomGeometryBlob.Empty;
        if (resource.Thumbnail.HasCommitted)
        {
            RoomThumbnailResource.Descriptor thumbnail = resource.Thumbnail.Committed;
            if (!obj.Textured || thumbnailChanged || projectionChanged ||
                obj.WidthTiles != geometry.WidthTiles || obj.HeightTiles != geometry.HeightTiles)
            {
                ReplaceMesh(obj.BaseFilter, BuildThumbnailMesh(geometry, thumbnail, room, view));
                obj.BaseRenderer.sharedMaterial = GetTextureMaterial(thumbnail.Texture);
            }

            // Vanilla map textures do not contain authored RoomSettings terrain (TerrainHandle,
            // LocalTerrain, CurvedSlope/SuperSlope, DryCycle custom curved terrain). Draw that
            // semantic geometry as a retained overlay using the same terrain colors as normal tiles.
            if (!obj.Textured || geometryChanged || projectionChanged)
            {
                Mesh authoredOverlay = BuildAuthoredTerrainMesh(geometry, room, view);
                if (authoredOverlay == null)
                    authoredOverlay = BuildColorMesh(geometry, customOnly: true, room, view);
                ReplaceMesh(obj.OverlayFilter, authoredOverlay);
                obj.OverlayRenderer.sharedMaterial = colorMaterial;
            }
        }
        else
        {
            ReplaceMesh(obj.BaseFilter, BuildColorMesh(geometry, customOnly: false, room, view));
            obj.BaseRenderer.sharedMaterial = colorMaterial;
            ReplaceMesh(obj.OverlayFilter, null);
        }
        obj.WidthTiles = geometry.WidthTiles;
        obj.HeightTiles = geometry.HeightTiles;
        obj.Textured = resource.Thumbnail.HasCommitted;
    }

    private Material GetTextureMaterial(Texture2D texture)
    {
        int id = texture != null ? texture.GetInstanceID() : 0;
        if (textureMaterials.TryGetValue(id, out Material material) && material != null)
            return material;

        material = new Material(spriteShader)
        {
            name = "DryCycle.WorldMapV2.Texture_" + id,
            hideFlags = HideFlags.HideAndDontSave,
            mainTexture = texture ?? Texture2D.whiteTexture
        };
        textureMaterials[id] = material;
        return material;
    }

    private static Mesh BuildThumbnailMesh(RoomGeometryBlob geometry,
        RoomThumbnailResource.Descriptor thumbnail, WorldMapScene.RoomNode room, WorldMapViewTransform view)
    {
        float width = Math.Max(1f, geometry.WidthTiles) * TileDisplaySize;
        float height = Math.Max(1f, geometry.HeightTiles) * TileDisplaySize;
        // Four native corners: layer depth is alpha/order only. Dragging changes the root
        // transform; navigation and moving a room do not upload another mesh.
        var vertices = new[] { new Vector3(0,0,0), new Vector3(width,0,0),
            new Vector3(width,-height,0), new Vector3(0,-height,0) };
        Rect rect = thumbnail.Uv;
        var uv = new[] { new Vector2(rect.xMin,rect.yMax), new Vector2(rect.xMax,rect.yMax),
            new Vector2(rect.xMax,rect.yMin), new Vector2(rect.xMin,rect.yMin) };
        var color = LayerColor(new Color32(255,255,255,255),room,view);
        var colors = new[] { color,color,color,color };
        var triangles = new[] { 0,1,2,0,2,3 };
        Mesh mesh = NewMesh("DryCycle WorldMap V2 Thumbnail");
        mesh.vertices = vertices; mesh.uv = uv; mesh.colors32 = colors; mesh.triangles = triangles;
        mesh.RecalculateBounds(); return mesh;
    }

    private static Color32 LayerColor(Color32 color, WorldMapScene.RoomNode room, WorldMapViewTransform view)
    {
        color.a = (byte)Math.Round(color.a * MapDepthProjection.Opacity(room.Layer,view.DepthEnabled));
        return color;
    }

    private static Mesh BuildAuthoredTerrainMesh(RoomGeometryBlob geometry, WorldMapScene.RoomNode room, WorldMapViewTransform view)
    {
        RoomGeometryBlob.Vertex[] source =
            geometry.AuthoredTerrainVertices ?? Array.Empty<RoomGeometryBlob.Vertex>();
        int[] sourceIndices =
            geometry.AuthoredTerrainTriangleIndices ?? Array.Empty<int>();
        if (source.Length == 0 || sourceIndices.Length < 3)
            return null;

        List<Vector3> vertices = new();
        List<Color32> colors = new();
        List<int> indices = new();
        float heightTiles = Math.Max(1f, geometry.HeightTiles);

        for (int i = 0; i + 2 < sourceIndices.Length; i += 3)
        {
            RoomGeometryBlob.Vertex a = source[sourceIndices[i]];
            RoomGeometryBlob.Vertex b = source[sourceIndices[i + 1]];
            RoomGeometryBlob.Vertex c = source[sourceIndices[i + 2]];

            int first = vertices.Count;
            AddVertex(vertices, colors, a, heightTiles, room, view);
            AddVertex(vertices, colors, b, heightTiles, room, view);
            AddVertex(vertices, colors, c, heightTiles, room, view);
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
        }

        if (indices.Count == 0)
            return null;

        Mesh mesh = NewMesh("DryCycle WorldMap V2 Authored Terrain");
        mesh.vertices = vertices.ToArray();
        mesh.colors32 = colors.ToArray();
        mesh.triangles = indices.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Mesh BuildColorMesh(RoomGeometryBlob geometry, bool customOnly, WorldMapScene.RoomNode room, WorldMapViewTransform view)
    {
        RoomGeometryBlob.Vertex[] source = geometry.Vertices ?? Array.Empty<RoomGeometryBlob.Vertex>();
        int[] sourceIndices = geometry.TriangleIndices ?? Array.Empty<int>();
        if (source.Length == 0 || sourceIndices.Length < 3)
            return null;

        List<Vector3> vertices = new();
        List<Color32> colors = new();
        List<int> indices = new();
        float heightTiles = Math.Max(1f, geometry.HeightTiles);

        for (int i = 0; i + 2 < sourceIndices.Length; i += 3)
        {
            RoomGeometryBlob.Vertex a = source[sourceIndices[i]];
            RoomGeometryBlob.Vertex b = source[sourceIndices[i + 1]];
            RoomGeometryBlob.Vertex c = source[sourceIndices[i + 2]];
            EditorMapGeometryKind kind = a.Kind;
            if (customOnly && !IsCustomOverlay(kind)) continue;

            int first = vertices.Count;
            AddVertex(vertices, colors, a, heightTiles, room, view);
            AddVertex(vertices, colors, b, heightTiles, room, view);
            AddVertex(vertices, colors, c, heightTiles, room, view);
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
        }

        if (indices.Count == 0) return null;

        Mesh mesh = NewMesh("DryCycle WorldMap V2 Geometry");
        mesh.vertices = vertices.ToArray();
        mesh.colors32 = colors.ToArray();
        mesh.triangles = indices.ToArray();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddVertex(
        List<Vector3> vertices,
        List<Color32> colors,
        RoomGeometryBlob.Vertex vertex,
        float heightTiles, WorldMapScene.RoomNode room, WorldMapViewTransform view)
    {
        vertices.Add(new Vector3(vertex.X * TileDisplaySize, -(heightTiles - vertex.Y) * TileDisplaySize, 0f));
        colors.Add(LayerColor(GeometryColor(vertex.Kind),room,view));
    }

    private static Color32 GeometryColor(EditorMapGeometryKind kind) => kind switch
    {
        EditorMapGeometryKind.Air => new Color32(148, 150, 153, 255),
        EditorMapGeometryKind.BackWall => new Color32(120, 122, 125, 255),
        EditorMapGeometryKind.Solid => new Color32(74, 77, 79, 255),
        EditorMapGeometryKind.Structure => new Color32(148, 79, 79, 255),
        EditorMapGeometryKind.Shortcut => new Color32(214, 217, 214, 255),
        EditorMapGeometryKind.Transport => new Color32(184, 51, 71, 255),
        EditorMapGeometryKind.Water => new Color32(31, 87, 199, 90),
        // Curved terrain is terrain, not a special visualization category. Keep its semantic
        // colors identical to the ordinary terrain it represents.
        EditorMapGeometryKind.LocalTerrain => new Color32(148, 79, 79, 255),
        EditorMapGeometryKind.CurvedSlope => new Color32(74, 77, 79, 255),
        EditorMapGeometryKind.QuicksandBody => new Color32(94, 70, 50, 220),
        EditorMapGeometryKind.QuicksandMaterial => new Color32(180, 126, 72, 245),
        _ => new Color32(180, 180, 180, 255)
    };

    private static bool IsCustomOverlay(EditorMapGeometryKind kind) =>
        kind == EditorMapGeometryKind.LocalTerrain ||
        kind == EditorMapGeometryKind.CurvedSlope ||
        kind == EditorMapGeometryKind.QuicksandBody ||
        kind == EditorMapGeometryKind.QuicksandMaterial;

    private static Mesh NewMesh(string name) =>
        new()
        {
            name = name,
            hideFlags = HideFlags.HideAndDontSave
        };

    private static void ReplaceMesh(MeshFilter filter, Mesh next)
    {
        Mesh previous = filter.sharedMesh;
        filter.sharedMesh = next;
        if (previous != null) UnityEngine.Object.Destroy(previous);
    }

    private void RemoveRoom(int roomIndex)
    {
        if (!roomObjects.TryGetValue(roomIndex, out RoomObject room)) return;
        roomObjects.Remove(roomIndex);
        DestroyRoomObject(room);
    }

    private static void DestroyRoomObject(RoomObject room)
    {
        if (room == null) return;
        if (room.BaseFilter?.sharedMesh != null)
            UnityEngine.Object.Destroy(room.BaseFilter.sharedMesh);
        if (room.OverlayFilter?.sharedMesh != null)
            UnityEngine.Object.Destroy(room.OverlayFilter.sharedMesh);
        if (room.Root != null)
            UnityEngine.Object.Destroy(room.Root);
    }
}
