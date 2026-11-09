using System.Runtime.CompilerServices;
using UnityEngine;
namespace RegionKit.Modules.BackgroundBuilder;
public static class Data
{
    public static int Calls;
    private static readonly ConditionalWeakTable<RoomSettings, RoomBGData> table = new();
    public static void Seed(RoomSettings room, RoomBGData data) => table.Add(room, data);
    public static RoomBGData BackgroundData(RoomSettings room) { Calls++; throw new Exception("Must not call RegionKit API."); }
    public sealed class BackgroundDataAttribute : Attribute
    {
        public string backingFieldName, name;
        public BackgroundDataAttribute() { Calls++; throw new Exception("Must not instantiate mod attributes."); }
    }
    public class RoomBGData
    {
        public RoomBGData() { Calls++; throw new Exception("Must not call mod constructors."); }
        public string backgroundName;
        public Vector2 roomOffset;
        public bool roomOffsetInit;
        public RoomBGData parent;
        public BGSceneData sceneData;
    }
    public class BGSceneData
    {
        public BGSceneData() { Calls++; throw new Exception("Must not call mod constructors."); }
        public BGSceneData parent;
        public BackgroundScene _Scene;
        public bool sceneInitialized;
        public List<BackgroundElementData.CustomBgElement> backgroundElements;
        public float? _height;
        [BackgroundData(backingFieldName = "_height", name = "Altitude")]
        public float Height { get { Calls++; throw new Exception("Must not call mod getter."); } set { Calls++; throw new Exception("Must not call mod setter."); } }
        public void UpdateSceneElement(string message) { Calls++; throw new Exception("Must not call mod updater."); }
        public string Serialize() { Calls++; throw new Exception("Must not call mod serializer."); }
    }
}
public static class BackgroundElementData
{
    private static readonly ConditionalWeakTable<BackgroundScene.BackgroundSceneElement, InstanceData> table = new();
    public class InstanceData { public CustomBgElement dataElement; }
    public class CustomBgElement
    {
        public CustomBgElement() { Data.Calls++; throw new Exception("Must not call data constructors."); }
        public Vector2 pos;
        public float depth;
        public string assetName;
        public bool deleted, lockX, lockY;
        public Vector2? anchorPos, spriteScale;
        public float? spriteAlpha;
        public string spriteShader;
        public Color? spriteColor;
        public int? container;
        public BackgroundScene.BackgroundSceneElement sceneElement;
        public BG_ElementGroup group;
        public List<string> unrecognizedTags;
    }
    public sealed class BG_SimpleElement : CustomBgElement { }
    public sealed class BG_ElementGroup : CustomBgElement
    {
        public string name;
        public List<CustomBgElement> elements;
    }
}
