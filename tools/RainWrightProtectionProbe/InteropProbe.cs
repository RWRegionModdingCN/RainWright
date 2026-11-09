using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Xml.Linq;
using UnityEngine;
using P = Pom.Pom;

internal static class InteropProbe
{
    private sealed class RuntimeObject : UpdatableAndDeletable
    { public RuntimeObject(PlacedObject target, Room room) { } }
    private sealed class Factory : P.ManagedObjectType
    {
        internal int Representations;
        internal bool Fail;
        internal Factory(PlacedObject.Type type) : base(type, new DevInterface.ObjectsPage.DevObjectCategories("ProtectedProbe", true), typeof(RuntimeObject), null, null, singleInstance: true) { }
        public override UpdatableAndDeletable MakeObject(PlacedObject target, Room room)
        { if (Fail) throw new InvalidOperationException("original runtime diagnostic"); return base.MakeObject(target, room); }
        public override DevInterface.PlacedObjectRepresentation MakeRepresentation(PlacedObject target, DevInterface.ObjectsPage page)
        { Representations++; throw new InvalidOperationException("Hidden editor UI must not participate in managed placement."); }
    }
    internal static string Run(Assembly assembly, XElement plan, string game)
    {
        int checks = 0;
        void Check(bool ok) { if (!ok) throw new InvalidOperationException("Protected interop failed at assertion " + checks); checks++; }
        int Token(string kind, string id) => Convert.ToInt32(((string)plan.Elements(kind).Single(e => (string)e.Attribute("id") == id).Attribute("token")).Substring(2), 16);
        MethodInfo Method(string id) => (MethodInfo)assembly.ManifestModule.ResolveMethod(Token("method", id));
        T Raw<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
        var room = Raw<Room>(); room.abstractRoom = Raw<AbstractRoom>();
        room.roomSettings = Raw<RoomSettings>(); room.roomSettings.placedObjects = new List<PlacedObject>();
        var gameState = Raw<RainWorldGame>(); var camera = Raw<RoomCamera>(); camera.room = room;
        gameState.cameras = new[] { camera }; room.game = gameState; room.updateList = new List<UpdatableAndDeletable>(); room.updateIndex = -1;
        var owner = Raw<DevInterface.DevUI>(); owner.game = gameState;
        var sessionType = assembly.ManifestModule.ResolveType(Token("type", "session"));
        object session = FormatterServices.GetUninitializedObject(sessionType);
        assembly.ManifestModule.ResolveField(Token("field", "session.owner")).SetValue(session, owner);
        var type = new PlacedObject.Type("ProtectedProbeRuntime", true); var factory = new Factory(type);
        var registry = (IList)typeof(P).GetField("managedObjectTypes", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        registry.Add(factory);
        try
        {
            var create = Method("interop.create"); var remove = Method("interop.remove"); var ensure = Method("interop.ensure");
            object[] a = { session, type, new Vector2(20, 40), null }, b = { session, type, new Vector2(50, 80), null };
            Check((bool)create.Invoke(null, a) && (bool)create.Invoke(null, b));
            Check(room.updateList.Count == 1 && room.roomSettings.placedObjects.Count == 2);
            Check(owner.activePage == null && factory.Representations == 0);
            var first = (PlacedObject)a[3]; var second = (PlacedObject)b[3];
            remove.Invoke(null, new object[] { room, first }); room.roomSettings.placedObjects.Remove(first);
            Check(room.updateList.Count == 1 && !room.updateList[0].slatedForDeletetion);
            remove.Invoke(null, new object[] { room, second }); room.roomSettings.placedObjects.Remove(second);
            Check(room.updateList.Count == 0);
            room.roomSettings.placedObjects.Add(second); ensure.Invoke(null, new object[] { room, second });
            Check(room.updateList.Count == 1 && !room.abstractRoom.firstTimeRealized);
            ensure.Invoke(null, new object[] { room, second }); Check(room.updateList.Count == 1);
            // A live singleton is deliberately reused without calling its factory. Remove it
            // before injecting a factory failure, retaining the existing authored record.
            remove.Invoke(null, new object[] { room, second });
            Check(room.updateList.Count == 0 && room.roomSettings.placedObjects.Count == 1);
            factory.Fail = true; a[3] = null;
            Check(!(bool)create.Invoke(null, a) && a[3] == null && room.roomSettings.placedObjects.Count == 1);
            Check(((string)Method("interop.error").Invoke(session, null)).Contains("original runtime diagnostic"));
            var kit = Assembly.LoadFrom(Path.GetFullPath(Path.Combine(game, "../../workshop/content/312520/2920439476/newest/plugins/RegionKit.dll")));
            foreach (string dataName in new[] { "RegionKit.Modules.RoomZones.CircleZoneData", "RegionKit.Modules.Objects.PlacedWaterfallData" })
            {
                Type data = kit.GetType(dataName, true);
                var kitType = new PlacedObject.Type("ProtectedProbe_" + data.Name, true);
                var manager = new P.ManagedObjectType(kitType, new DevInterface.ObjectsPage.DevObjectCategories("ProtectedKitProbe", true), null, data, typeof(P.ManagedRepresentation));
                registry.Add(manager);
                try
                {
                    object[] request = { session, kitType, new Vector2(90, 120), null };
                    Check((bool)create.Invoke(null, request) && ((PlacedObject)request[3]).data.GetType() == data);
                    Check(((P.ManagedData)((PlacedObject)request[3]).data).fields.Length > 0 && owner.activePage == null);
                }
                finally { registry.Remove(manager); }
            }
            Method("ui.vanilla").Invoke(null, new object[] { true });
            Check((bool)Method("ui.allowNativeKeys").Invoke(null, new object[] { true, null }));
            Method("ui.vanilla").Invoke(null, new object[] { false });
            return checks + " successful assertions; no game/DevUI constructor invoked";
        }
        finally { registry.Remove(factory); }
    }
}
