using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Watcher;

namespace DryCycle.DevUI.DevTool.Background;

/// <summary>Game-native rendering only. RegionKit data is read as fields, never called.</summary>
internal static class BackgroundSceneAdapter
{
    internal static readonly string[] SceneNames = { "None", "AboveCloudsView", "RoofTopView", "AncientUrbanView", "RotWormScene", "VoidSeaScene" };
    internal static BackgroundScene Find(global::Room room) => room?.updateList?.OfType<BackgroundScene>().FirstOrDefault(s => !s.slatedForDeletetion);
    internal static string Name(BackgroundScene scene) => scene == null || scene.GetType() == typeof(BackgroundScene) ? "None" : scene.GetType().Name;

    internal static BackgroundScene Switch(global::Room room, string name)
    {
        if (!SceneNames.Contains(name)) throw new NotSupportedException("Unknown background scene: " + name);
        var oldScenes = room.updateList.OfType<BackgroundScene>().Where(s => !s.slatedForDeletetion).ToArray();
        BackgroundScene next = null;
        var random = UnityEngine.Random.state;
        try
        {
            next = name switch
            {
                "AboveCloudsView" => new AboveCloudsView(room, new RoomSettings.RoomEffect(RoomSettings.RoomEffect.Type.AboveCloudsView, 0f, false)),
                "RoofTopView" => new RoofTopView(room, new RoomSettings.RoomEffect(RoomSettings.RoomEffect.Type.RoofTopView, 0f, false)),
                "AncientUrbanView" => new AncientUrbanView(room, new RoomSettings.RoomEffect(new RoomSettings.RoomEffect.Type("AncientUrbanView", false), 0f, false)),
                "RotWormScene" => new RotWormScene(room),
                "VoidSeaScene" => new VoidSea.VoidSeaScene(room),
                _ => new BackgroundScene(room)
            };
        }
        finally { UnityEngine.Random.state = random; }
        // Retire previous renderers only after construction succeeds.
        foreach (var scene in oldScenes) { scene.Destroy(); room.RemoveObject(scene); }
        if (next != null) room.AddObject(next);
        BackgroundDataAccess.InitializePlainScene(BackgroundDataAccess.Root(room.roomSettings), next, name);
        room.roomSettings.effects.RemoveAll(e => e != null && SceneNames.Contains(e.type.value));
        if (name is "AboveCloudsView" or "RoofTopView" or "AncientUrbanView")
            room.roomSettings.effects.Add(new RoomSettings.RoomEffect(new RoomSettings.RoomEffect.Type(name, false), 0f, false));
        return next;
    }

    private static Vector2 Position(object record)
    {
        Vector2 result = Vector2.zero;
        var visited = new HashSet<object>();
        for (object cursor = record; cursor != null && visited.Add(cursor); cursor = BackgroundDataAccess.Get(cursor, "group"))
            if (BackgroundDataAccess.Get(cursor, "pos") is Vector2 value) result += value;
        return result;
    }
    private static float Depth(object record)
    {
        float result = 0;
        var visited = new HashSet<object>();
        for (object cursor = record; cursor != null && visited.Add(cursor); cursor = BackgroundDataAccess.Get(cursor, "group"))
            if (BackgroundDataAccess.Get(cursor, "depth") is float value) result += value;
        return Math.Max(.001f, result);
    }
    internal static BackgroundScene.BackgroundSceneElement Create(BackgroundScene scene, object record)
    {
        var schema = BackgroundDataAccess.Schema(record) ?? throw new NotSupportedException("Unrecognized background element data: " + record.GetType().FullName);
        if (!schema.Allowed(Name(scene))) throw new InvalidOperationException(schema.Name + " is not available in " + Name(scene));
        float F(string field) => BackgroundDataAccess.Get(record, field) is float value ? value : 0;
        bool B(string field) => BackgroundDataAccess.Get(record, field) is true;
        string asset = BackgroundDataAccess.Get(record, "assetName") as string;
        Vector2 pos = Position(record); float depth = Depth(record);
        float floor = scene is RoofTopView rooftop ? rooftop.floorLevel : scene is AncientUrbanView urban ? urban.floorLevel : 0;
        var random = UnityEngine.Random.state;
        try
        {
            return schema.Record switch
            {
                "BG_ElementGroup" => null,
                "BG_SimpleElement" => new NativeSpriteElement(scene, record, pos, depth),
                "BG_Illustration" => new BackgroundScene.Simple2DBackgroundIllustration(scene, asset, pos) { depth = depth },
                "ACV_DistantBuilding" => new AboveCloudsView.DistantBuilding((AboveCloudsView)scene, asset, pos * depth, depth, F("atmoDepthAdd")),
                "ACV_DistantLightning" => new AboveCloudsView.DistantLightning((AboveCloudsView)scene, asset, pos * depth, depth, F("minusDepthForLayering")),
                "ACV_FlyingCloud" => new AboveCloudsView.FlyingCloud((AboveCloudsView)scene, pos * depth, depth, 0, F("flattened"), F("alpha"), F("shaderInputColor")),
                "ACV_HorizonFog" => new AboveCloudsView.HorizonFog(scene, asset, pos, depth),
                "RTV_DistantBuilding" => new RoofTopView.DistantBuilding((RoofTopView)scene, asset, new Vector2(pos.x * depth, floor + pos.y), depth, F("atmoDepthAdd")),
                "RTV_Building" => new RoofTopView.Building((RoofTopView)scene, asset, new Vector2(pos.x * depth, floor + pos.y), depth, F("scale")),
                "RTV_Floor" => new RoofTopView.Floor((RoofTopView)scene, asset, new Vector2(pos.x, floor + pos.y), F("fromDepth"), F("toDepth")),
                "RTV_DistantGhost" => new RoofTopView.DistantGhost((RoofTopView)scene, pos * depth, depth, 0),
                "RTV_DustWave" => new RoofTopView.DustWave((RoofTopView)scene, asset, new Vector2(pos.x, floor + pos.y), depth, 0),
                "RTV_Smoke" when scene is RoofTopView roof => new RoofTopView.Smoke(roof, new Vector2(pos.x, floor + pos.y), depth, 0, F("flattened"), F("alpha"), F("shaderInputColor"), B("shaderType")),
                "RTV_Smoke" when scene is AncientUrbanView city => new AncientUrbanView.Smoke(city, new Vector2(pos.x, floor + pos.y), depth, 0, F("flattened"), F("alpha"), F("shaderInputColor"), B("shaderType")),
                "AUV_Building" => new AncientUrbanView.Building((AncientUrbanView)scene, asset, new Vector2(pos.x * depth, floor + pos.y), depth, F("scale"), F("rotation"), F("thickness")),
                "AUV_SmokeGradient" => new AncientUrbanView.SmokeGradient((AncientUrbanView)scene, new Vector2(pos.x, floor + pos.y), depth),
                "RWS_RotWorm" => new RotWorm(scene, pos, depth),
                "RWS_PebbsGrid" => new RotWormScene.PebbsGrid((RotWormScene)scene, pos.x, pos.y, depth, F("scale"), B("perpendicular")),
                _ => throw new NotSupportedException("Unsupported native background element: " + schema.Record)
            };
        }
        finally { UnityEngine.Random.state = random; }
    }
    internal static void ReplaceElement(global::Room room, BackgroundScene scene, object record)
    {
        var next = Create(scene, record);
        if (BackgroundDataAccess.Get(record, "sceneElement") is BackgroundScene.BackgroundSceneElement old)
        { old.Destroy(); scene.elements.Remove(old); room.RemoveObject(old); }
        BackgroundDataAccess.Set(record, "sceneElement", next);
        if (next == null) return;
        scene.AddElement(next);
        // AddElement does not add new elements to an already-initialized Room by itself.
        if (scene.elementsAddedToRoom) room.AddObject(next);
        BackgroundDataAccess.BindRuntimeMetadata(next, record);
        BackgroundAppearanceRuntime.Bind(next, record);
    }

    private sealed class NativeSpriteElement : BackgroundScene.BackgroundSceneElement
    {
        private string asset;
        private readonly Func<string> readAsset;
        internal NativeSpriteElement(BackgroundScene scene, object record, Vector2 pos, float depth) : base(scene, pos, depth)
        {
            readAsset = BackgroundDataAccess.StringReader(record, "assetName"); asset = readAsset();
            if (!Futile.atlasManager.DoesContainElementWithName(asset)) scene.LoadGraphic(asset, true, true);
        }
        public override void InitiateSprites(RoomCamera.SpriteLeaser leaser, RoomCamera camera)
        {
            leaser.sprites = new[] { new FSprite(asset) };
            leaser.sprites[0].shader = camera.game.rainWorld.Shaders["Background"];
            AddToContainer(leaser, camera, null);
        }
        public override void DrawSprites(RoomCamera.SpriteLeaser leaser, RoomCamera camera, float timeStacker, Vector2 camPos)
        {
            // The original asset control updates BG_SimpleElement's field. Its renderer type check
            // cannot recognize our independent sprite, so observe that shared field directly.
            string desired = readAsset();
            if (desired != asset)
            {
                if (!Futile.atlasManager.DoesContainElementWithName(desired)) scene.LoadGraphic(desired, true, true);
                asset = desired;
            }
            if (leaser.sprites[0].element.name != asset) leaser.sprites[0].element = Futile.atlasManager.GetElementWithName(asset);
            Vector2 draw = DrawPos(camPos, camera.hDisplace);
            leaser.sprites[0].SetPosition(draw);
            base.DrawSprites(leaser, camera, timeStacker, camPos);
        }
    }
}
