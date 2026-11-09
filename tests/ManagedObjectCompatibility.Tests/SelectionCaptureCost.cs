using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;
using P = Pom.Pom;

internal static partial class Program
{
    private sealed class CountedData : P.ManagedData
    {
        internal static int Reads;
        internal CountedData(PlacedObject owner, P.ManagedField[] fields) : base(owner, fields) { }
        public new T GetValue<T>(string key) { Reads++; return base.GetValue<T>(key); }
    }
    private sealed class CountedInspector : IObjectInspectorAdapter, IObjectInspectorCoverageProvider
    {
        internal PlacedObject Target;
        internal int Captures, Resolutions, Coverages;
        public bool CanInspect(PlacedObject target) { Resolutions++; return ReferenceEquals(Target, target); }
        public IReadOnlyList<EditorPropertySnapshot> Capture(PlacedObject target)
        { Captures++; return new[] { new EditorPropertySnapshot { Key = "value", Kind = EditorPropertyKind.Float, X = .5f } }; }
        public ObjectInspectorCoverage GetCoverage(PlacedObject target) { Coverages++; return ObjectInspectorCoverage.Complete(); }
        public bool TrySetValue(PlacedObject target, string key, EditorPropertyValue value) => false;
    }
    private static void VerifySelectionCaptureCost(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var target = Owner("SelectionCost");
        target.data = new CountedData(target, Enumerable.Range(0, 64).Select(i => (P.ManagedField)new P.FloatField("value" + i, 0, 1, .4f)).ToArray());
        var registry = typeof(ObjectInspectorRegistry);
        var capture = registry.GetMethod("Capture", flags);
        var coverage = registry.GetMethod("GetCoverage", flags);
        var combined = registry.GetMethod("CaptureWithCoverage", flags);
        CountedData.Reads = 0;
        var baselineProperties = (EditorPropertySnapshot[])capture.Invoke(null, new object[] { target });
        var baselineCoverage = (ObjectInspectorCoverage)coverage.Invoke(null, new object[] { target });
        int separateReads = CountedData.Reads;
        CountedData.Reads = 0; object[] request = { target, null };
        var properties = (EditorPropertySnapshot[])combined.Invoke(null, request);
        int combinedReads = CountedData.Reads;
        Check(separateReads > 0 && combinedReads * 2 == separateReads, "Combined managed property/coverage capture must halve actual GetValue calls.");
        Check(properties.Select(p => p.Key).SequenceEqual(baselineProperties.Select(p => p.Key)) &&
            ((ObjectInspectorCoverage)request[1]).IsComplete == baselineCoverage.IsComplete, "Combined capture must preserve inspector fields and coverage semantics.");
        Console.WriteLine("PROFILE managed 64-field selection: " + separateReads + " -> " + combinedReads + " model reads; no value cache.");

        var room = (Room)FormatterServices.GetUninitializedObject(typeof(Room));
        room.roomSettings = (RoomSettings)FormatterServices.GetUninitializedObject(typeof(RoomSettings));
        room.roomSettings.placedObjects = new List<PlacedObject> { target };
        var gameState = (RainWorldGame)FormatterServices.GetUninitializedObject(typeof(RainWorldGame));
        var camera = (RoomCamera)FormatterServices.GetUninitializedObject(typeof(RoomCamera)); camera.room = room; gameState.cameras = new[] { camera };
        var owner = (DevInterface.DevUI)FormatterServices.GetUninitializedObject(typeof(DevInterface.DevUI)); owner.game = gameState;
        var session = (EditorSession)FormatterServices.GetUninitializedObject(typeof(EditorSession));
        typeof(EditorSession).GetField("<Owner>k__BackingField", flags).SetValue(session, owner);
        typeof(EditorSession).GetField("<Selection>k__BackingField", flags).SetValue(session, new EditorSelection());
        var presentation = typeof(EditorPresentationHub);
        var current = presentation.GetProperty("current", flags); object saved = current.GetValue(null);
        var counted = new CountedInspector { Target = target }; ObjectInspectorRegistry.Register(counted, int.MaxValue);
        var catalog = typeof(ObjectCatalog);
        var catalogFields = new[] { "cached", "cachedTypeCount", "cachedByType" }.Select(name => catalog.GetField(name, flags)).ToArray();
        var savedCatalog = catalogFields.Select(field => field.GetValue(null)).ToArray();
        // Supply detached catalog metadata: a CLR test has no initialized BepInEx plugin registry.
        catalogFields[0].SetValue(null, new List<ObjectDescriptor>());
        catalogFields[1].SetValue(null, ExtEnum<PlacedObject.Type>.values.Count);
        catalogFields[2].SetValue(null, new Dictionary<string, ObjectDescriptor>());
        try
        {
            var scene = presentation.GetMethod("CaptureObjectScene", flags).Invoke(null, new object[] { session, room.roomSettings.placedObjects });
            current.SetValue(null, new EditorPresentationSnapshot { SceneObjects = (EditorObjectSnapshot[])scene });
            session.Selection.SelectOnly(target);
            var hints = assembly.GetType("DryCycle.DevUI.DevTool.Objects.ObjectPresentationChangeHintHub", true);
            foreach (string method in new[] { "MarkMember", "MarkAllMembers" })
            {
                if (method == "MarkMember") hints.GetMethod(method, flags).Invoke(null, new object[] { session, target });
                else hints.GetMethod(method, flags).Invoke(null, new object[] { session });
                object hint = hints.GetMethod("Consume", flags).Invoke(null, new object[] { session });
                counted.Captures = counted.Resolutions = counted.Coverages = 0;
                object[] args = { session, room.roomSettings.placedObjects, 1, target, session.Selection.Revision, hint, true, true, false, null, null };
                Check((bool)presentation.GetMethod("TryCaptureObjectPartial", flags).Invoke(null, args), "Known member/model+selection changes must remain a partial capture.");
                Check(counted.Captures == 1 && counted.Resolutions == 1 && counted.Coverages == 1,
                    "Simultaneous model+selection changes must capture the inspector and resolve its adapter exactly once: " + method);
                Check(((EditorObjectSnapshot[])args[9])[0].Selected && ((EditorInspectorSnapshot)args[10]).HasSelection,
                    "Coalesced capture must preserve selected scene rows and inspector state.");
            }
        }
        finally
        {
            current.SetValue(null, saved); ObjectInspectorRegistry.Unregister(counted);
            for (int i = 0; i < catalogFields.Length; i++) catalogFields[i].SetValue(null, savedCatalog[i]);
        }
    }
}
