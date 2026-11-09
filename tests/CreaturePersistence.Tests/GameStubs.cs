using DryCycle.DevUI.DevTool.World;

namespace BepInEx { }
namespace BepInEx.Logging
{
    internal sealed class ManualLogSource
    {
        internal void LogInfo(string message) { }
        internal void LogWarning(string message) { }
    }
}
namespace DryCycle.DevUI.DevTool.Core
{
    internal sealed class Session { internal global::World World; }
    internal static class DevToolRuntime { internal static Session ActiveSession; }
}
namespace DryCycle.DevUI.DevTool.World
{
    internal static class WorldConnectionSyntax
    {
        internal static void SynchronizeRegion(string region, WorldDocument doc) { }
        internal static void SynchronizeRoute(string region, string room, int node, string destination) { }
        internal static string FormatForSource(string region, string room, int node, string destination) => destination;
        internal static bool TryParseDestination(string text, out string room, out int node) { room = text; node = -1; return true; }
    }
    internal static class WorldTopologyRuntime { internal static void NotifyTopologyChanged() { } }
    internal static class WorldTopologyLiveTraversalFix { internal static void OnConnectionChanged(string region, string room, int node, string destination) { } }
}
internal sealed class World
{
    internal string name = "B5";
    internal RainWorldGame game = new();
    internal AbstractRoom[] abstractRooms;
    internal CreatureSpawner[] spawners = Array.Empty<CreatureSpawner>();
    internal Region region = new();
    internal AbstractRoom GetAbstractRoom(string name) => abstractRooms.FirstOrDefault(r => r.name == name);
    internal AbstractRoom GetAbstractRoom(int index) => abstractRooms.FirstOrDefault(r => r.index == index);
    internal class CreatureSpawner { internal WorldCoordinate den; internal int SpawnerID; }
    internal sealed class SimpleSpawner : CreatureSpawner { internal CreatureTemplate.Type creatureType; internal int amount; }
}
internal sealed class Region { internal int regionNumber = 0; }
internal sealed class RainWorldGame
{
    internal bool IsStorySession = true;
    internal SlugcatStats.Timeline TimelinePoint = new("Inv");
    internal SlugcatStats.Name StoryCharacter = new("Sofanthiel");
    internal object session = new StoryGameSession();
    private int next;
    internal EntityID GetNewID() => new(-1, ++next);
}
internal sealed class StoryGameSession { internal SaveState saveState = new(); }
internal sealed class SaveState { internal RegionState[] regionStates = { new() }; }
internal sealed class RegionState { internal Dictionary<string, int> lineageCounters = new(); }
internal sealed class SlugcatStats
{
    internal sealed class Timeline
    {
        internal string value;
        internal Timeline(string value) => this.value = value;
        internal static readonly Timeline Sofanthiel = new("Inv");
        public override string ToString() => value;
    }
    internal sealed class Name { internal string value; internal Name(string value) => this.value = value; }
    internal static Timeline SlugcatToTimeline(Name name) => new(name.value switch
    {
        "Sofanthiel" => "Inv",
        "White" or "Yellow" or "Red" or "Gourmand" or "Artificer" or "Spear" or "Rivulet" or "Saint" or "Watcher" => name.value,
        _ => "White"
    });
}
internal static class WorldLoader
{
    internal static CreatureTemplate.Type CreatureTypeFromString(string name) => new(name);
    internal static class Preprocessing
    {
        internal static bool TimelineMatch(string text, SlugcatStats.Timeline timeline)
        {
            bool exclude = text.StartsWith("X-");
            if (timeline == null) return exclude;
            return (exclude ? text.Substring(2) : text).Split(',').Any(t => (t.Trim() switch
            { "0" => "White", "1" => "Yellow", "2" => "Red", _ => t.Trim() }) == timeline.value) != exclude;
        }
    }
}
internal readonly record struct WorldCoordinate(int room, int x, int y, int abstractNode)
{ internal string SaveToString() => room + ":" + abstractNode; }
internal readonly record struct EntityID(int spawner, int number);
internal class AbstractWorldEntity { }
internal sealed class Creature { internal void Destroy() { } }
internal sealed class AbstractCreature : AbstractWorldEntity
{
    internal static int Created;
    internal Creature realizedCreature;
    internal EntityID ID;
    internal string spawnData;
    internal bool nightCreature;
    internal CreatureTemplate Template;
    internal bool Destroyed;
    internal AbstractCreature(World world, CreatureTemplate template, object body, WorldCoordinate den, EntityID id)
    { ID = id; Template = template; Created++; }
    internal void setCustomFlags() { }
    internal void Destroy() => Destroyed = true;
}
internal sealed class AbstractRoom
{
    internal string name;
    internal int index;
    internal object realizedRoom;
    internal AbstractRoomNode[] nodes = Enumerable.Range(0, 8).Select(_ => new AbstractRoomNode()).ToArray();
    internal List<AbstractWorldEntity> entities = new(), entitiesInDens = new();
    internal int Quantified;
    internal void RemoveEntity(AbstractCreature creature) { entities.Remove(creature); entitiesInDens.Remove(creature); }
    internal void MoveEntityToDen(AbstractCreature creature) => entitiesInDens.Add(creature);
    internal void AddQuantifiedCreature(int node, CreatureTemplate.Type type, int count) => Quantified += count;
    internal void RemoveQuantifiedCreature(int node, CreatureTemplate.Type type) => Quantified--;
}
internal sealed class AbstractRoomNode
{
    internal sealed class Type
    {
        internal static readonly Type Den = new(), GarbageHoles = new();
    }
    internal Type type = Type.Den;
}
internal sealed class CreatureTemplate
{
    internal sealed record Type(string value);
    internal Type type;
    internal bool quantified;
}
internal static class StaticWorld
{
    internal static CreatureTemplate GetCreatureTemplate(CreatureTemplate.Type type) => type == null ? null : new() { type = type, quantified = type.value == "Leech" };
}
