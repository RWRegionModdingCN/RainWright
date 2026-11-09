using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using DryCycle.DevUI.DevTool.Objects;
using UnityEngine;
using P = Pom.Pom;



internal static partial class Program
{
    private static int checks;
    private static string game, library, pom;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); checks++; }
    private static int Main(string[] args)
    {
        if (args.Length != 3) { Console.Error.WriteLine("Usage: RainWright.dll RainWorldDir Pom.dll"); return 2; }
        library = Path.GetFullPath(args[0]); game = Path.GetFullPath(args[1]); pom = Path.GetFullPath(args[2]);
        AppDomain.CurrentDomain.AssemblyResolve += (_, request) =>
        {
            string name = new AssemblyName(request.Name).Name;
            string file = name == "RainWright" ? library : name == "Pom" ? pom : null;
            if (file != null) return Assembly.LoadFrom(file);
            foreach (string directory in new[] { "BepInEx/utils", "BepInEx/core", "BepInEx/plugins", "RainWorld_Data/Managed" })
            {
                file = Path.Combine(game, directory, name == "Assembly-CSharp" && directory == "BepInEx/utils" ? "PUBLIC-Assembly-CSharp.dll" : name + ".dll");
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        try
        {
            Run();
            Console.WriteLine("PASS: " + checks + " compatibility assertions against installed POM and RainWright DLLs."); return 0;
        }
        catch (Exception failure)
        {
            for (Exception error = failure; error != null; error = error.InnerException)
            {
                Console.Error.WriteLine(error.GetType().FullName + ": " + error.Message);
                try { Console.Error.WriteLine(error.StackTrace); } catch { }
            }
            return 1;
        }
    }
    private static void Run()
    {
        var inspectorType = typeof(IObjectInspectorAdapter).Assembly.GetType("DryCycle.DevUI.DevTool.Objects.ManagedObjectProtocolInspector", true);
        var inspector = (IObjectInspectorAdapter)inspectorType.GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        ObjectInspectorRegistry.Register(inspector, -100);
        ObjectInspectorRegistry.Register((IObjectInspectorAdapter)inspectorType.Assembly.GetType("DryCycle.DevUI.DevTool.Objects.NativeDataReflectionInspector", true)
            .GetField("Instance", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), -200);
        var target = Owner("Fields");
        var fields = new P.ManagedField[]
        {
            new P.FloatField("amount", 0, 1, .4f, .05f), new P.IntegerField("count", 1, 20, 4),
            new P.BooleanField("enabled", true), new P.StringField("label", "original"),
            new P.EnumField<Mode>("mode", Mode.First),
            new P.Vector2Field("offset", new Vector2(20, 30)),
            new P.IntVector2Field("tile", new RWCustom.IntVector2(1, 2)),
            new P.ColorField("color", new Color(.1f, .2f, .3f, 1))
        };
        target.data = new P.ManagedData(target, fields);
        Check(inspector.CanInspect(target), "Actual POM 3.2 ManagedData is detected.");
        var properties = inspector.Capture(target);
        Check(properties.Count(property => property.Key.StartsWith("managed.")) == fields.Length, "Every standard POM field appears in the inspector.");
        Check(properties.Single(property => property.Key == "managed.amount").Step == .05f, "POM float increments remain available to the UI.");
        Check(inspector.TrySetValue(target, "managed.amount", new(EditorPropertyKind.Float, x: 4)), "Standard float mutation is routed to real ManagedData.");
        Check(((P.ManagedData)target.data).GetValue<float>("amount") == 1, "Framework range prevents out-of-range data.");
        Check(inspector.TrySetValue(target, "managed.enabled", new(EditorPropertyKind.Boolean, boolean: false))
            && !((P.ManagedData)target.data).GetValue<bool>("enabled"), "Boolean field is editable.");
        Check(inspector.TrySetValue(target, "managed.label", new(EditorPropertyKind.String, text: "中文 edit"))
            && ((P.ManagedData)target.data).GetValue<string>("label") == "中文 edit", "String field preserves Unicode.");
        var subset = new RestrictedEnum("restricted", Mode.First);
        var enumTarget = Owner("Enum"); enumTarget.data = new P.ManagedData(enumTarget, new P.ManagedField[] { subset });
        Check(inspector.Capture(enumTarget).Single(property => property.Key == "managed.restricted").Options.SequenceEqual(new[] { "First", "Last" }),
            "A descriptor's restricted enum list is preserved.");
        subset.Entries = new[] { Mode.Middle, Mode.Last };
        Check(inspector.Capture(enumTarget).Single(property => property.Key == "managed.restricted").Options.SequenceEqual(new[] { "Middle", "Last" }),
            "Changed descriptor enum options refresh without rebuilding the data schema.");
        var aliasTarget = Owner("Aliases"); aliasTarget.data = new AliasData(aliasTarget);
        var aliasProperties = inspector.Capture(aliasTarget);
        Check(aliasProperties.Count(property => property.Kind == EditorPropertyKind.Float) == 1,
            "POM attribute key aliases do not duplicate the backed CLR field in the native inspector.");
        Check(((IObjectInspectorCoverageProvider)inspector).GetCoverage(aliasTarget).IsComplete,
            "An attribute-backed CLR alias does not create a false compatibility gap.");
        Check(inspector.TrySetValue(aliasTarget, "managed.00amount", new(EditorPropertyKind.Float, x: .7f))
            && ((AliasData)aliasTarget.data).AuthoredAmount == .7f, "A key alias edits the actual attribute-backed author field.");
        var broken = Owner("BrokenEnum");
        broken.data = new P.ManagedData(broken, new P.ManagedField[] { new BrokenEnum("broken", Mode.First) });
        var brokenProperty = inspector.Capture(broken).Single(property => property.Key == "managed.broken");
        Check(brokenProperty.Kind == EditorPropertyKind.ReadOnly && brokenProperty.StringValue.Contains("original enum callback failed"),
            "A failing original list callback remains visible with its original exception, rather than offering an invalid CLR fallback list.");
        Check(!((IObjectInspectorCoverageProvider)inspector).GetCoverage(broken).InspectorComplete, "Callback failure cannot claim complete inspector coverage.");
        var custom = Owner("CustomControl");
        custom.data = new P.ManagedData(custom, new P.ManagedField[] { new CoupledFloat("coupled", 0, 1, .5f) });
        Check(inspector.Capture(custom).Single(property => property.Key == "managed.coupled").Kind == EditorPropertyKind.ReadOnly
            && !inspector.TrySetValue(custom, "managed.coupled", new(EditorPropertyKind.Float, x: .8f)),
            "A custom control callback is not bypassed by direct writes from a duplicate native field.");
        Check(!((IObjectInspectorCoverageProvider)inspector).GetCoverage(custom).IsComplete,
            "Custom callback fields retain the isolated original control boundary.");
        var arrayTarget = Owner("Array");
        var decorated = Owner("DecoratedData");
        decorated.data = new P.ManagedData(decorated, new P.ManagedField[] { new DecorativeFloat("value", 0, 1, .5f) });
        Check(inspector.Capture(decorated).Single(p => p.Key == "managed.value").Kind == EditorPropertyKind.Float &&
            inspector.TrySetValue(decorated, "managed.value", new(EditorPropertyKind.Float, x: .7f)),
            "A custom UI-only decoration does not block ordinary data editing or invoke its editor constructor.");
        var shared = new[] { Vector2.zero, new Vector2(10, 20), new Vector2(30, 40) };
        var arrayField = new P.Vector2ArrayField("points", 3, true, P.Vector2ArrayField.Vector2ArrayRepresentationType.Polygon, shared);
        arrayTarget.data = new P.ManagedData(arrayTarget, new P.ManagedField[] { arrayField });
        Check(inspector.Capture(arrayTarget).Count(property => property.Key.StartsWith("managed.points[")) == 2, "Parent anchor is not presented as an editable authored point.");
        var previous = ((P.ManagedData)arrayTarget.data).GetValue<Vector2[]>("points");
        Check(inspector.TrySetValue(arrayTarget, "managed.points[1]", new(EditorPropertyKind.Vector2, x: 55, y: 66)), "Array point can be edited through the native setter.");
        Check(previous[1].x == 10 && ((P.ManagedData)arrayTarget.data).GetValue<Vector2[]>("points")[1].x == 55,
            "Point edits clone framework arrays rather than mutate shared defaults or history.");
        var empty = Owner("ParentOnly");
        empty.data = new P.ManagedData(empty, new P.ManagedField[] {
            new P.Vector2ArrayField("points", 1, true, P.Vector2ArrayField.Vector2ArrayRepresentationType.Polygon, new[] { Vector2.zero }) });
        Check(inspector.Capture(empty).Any(property => property.Key == "managed.points"), "A parent-only multi-point field must remain visible, not disappear.");
        Check(!inspector.TrySetValue(target, "managed.amount", new(EditorPropertyKind.Float, x: float.NaN)), "NaN must not enter third-party author data.");
        Check(!inspector.TrySetValue(target, "managed.offset", new(EditorPropertyKind.Vector2, x: float.PositiveInfinity))
            && !inspector.TrySetValue(target, "managed.color", new(EditorPropertyKind.Color, w: float.NaN)),
            "Non-finite vectors and colors cannot enter third-party author data.");
        Check(!inspector.TrySetValue(target,"managed.tile",new(EditorPropertyKind.Vector2,x:float.MaxValue,y:1))&&
            ((P.ManagedData)target.data).GetValue<RWCustom.IntVector2>("tile").x==1,
            "Finite but out-of-range tile coordinates cannot wrap into unrelated integer author values.");
        VerifyRepresentationMetadata(aliasTarget, fields, inspectorType.Assembly);
        VerifySpriteGeometry(inspectorType.Assembly);
        VerifyGizmoMerge(inspectorType.Assembly);
        VerifyInvalidManagedData(inspector);
        VerifyMeshGeometry(inspectorType.Assembly);
        VerifyIndependentPlacement(inspectorType.Assembly);
        VerifyOrdinaryEffects(inspectorType.Assembly);
        VerifySelectionCaptureCost(inspectorType.Assembly);
        VerifyNativeGeometry(inspectorType.Assembly, inspector);
        VerifyCustomDecal(inspectorType.Assembly);
        VerifyBuiltinObjectInspectors(inspectorType.Assembly);
        VerifyBuiltinObjectGizmos(inspectorType.Assembly);
        VerifyPanelCoordinates(inspectorType.Assembly);
        VerifyLegacySelectionCycles(inspectorType.Assembly);

    }
    private sealed class PlacementFactory : P.ManagedObjectType
    {
        internal int DataCalls, RuntimeCalls, RepresentationCalls;
        internal bool FailRuntime, AddHelperBeforeFailure;
        internal PlacementFactory(PlacedObject.Type type) : base(type, new DevInterface.ObjectsPage.DevObjectCategories("IndependentPlacement", true), null, null, null) { }
        public override PlacedObject.Data MakeEmptyData(PlacedObject target)
        { DataCalls++; return new P.ManagedData(target, new P.ManagedField[] { new P.IntegerField("value", 0, 20, 7) }); }
        public override UpdatableAndDeletable MakeObject(PlacedObject target, Room room)
        {
            RuntimeCalls++;
            Check(room.abstractRoom.firstTimeRealized, "Runtime factory receives native first-realization semantics.");
            if (AddHelperBeforeFailure) room.AddObject(new PlacementRuntime(target, room));
            if (FailRuntime) throw new InvalidOperationException("placement runtime original error");
            return null; // A data-only registration is valid.
        }
        public override DevInterface.PlacedObjectRepresentation MakeRepresentation(PlacedObject target, DevInterface.ObjectsPage page)
        { RepresentationCalls++; throw new Exception("Editor representations must not participate in placement."); }
    }
    private sealed class PlacementRuntime : UpdatableAndDeletable
    {
        public readonly PlacedObject Source;
        public PlacementRuntime(PlacedObject target, Room room) { Source = target; }
    }
    private static void VerifyOrdinaryEffects(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        var settings = (RoomSettings)FormatterServices.GetUninitializedObject(typeof(RoomSettings)); settings.Reset();
        settings.parent = (RoomSettings)FormatterServices.GetUninitializedObject(typeof(RoomSettings)); settings.parent.Reset(); settings.parent.isAncestor = true;
        var room = (Room)FormatterServices.GetUninitializedObject(typeof(Room)); room.roomSettings = settings;
        var gameState = (RainWorldGame)FormatterServices.GetUninitializedObject(typeof(RainWorldGame));
        var camera = (RoomCamera)FormatterServices.GetUninitializedObject(typeof(RoomCamera)); camera.room = room; gameState.cameras = new[] { camera };
        var owner = (DevInterface.DevUI)FormatterServices.GetUninitializedObject(typeof(DevInterface.DevUI)); owner.game = gameState;
        var sessionType = assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorSession", true);
        var session = FormatterServices.GetUninitializedObject(sessionType);
        sessionType.GetField("<Owner>k__BackingField", flags).SetValue(session, owner);
        var history = new DryCycle.DevUI.DevTool.History.EditorHistoryService(16);
        var journal = history.GetType().GetField("journal", flags).GetValue(history);
        // A detached CLR has no Unity frame clock. Keep the real journal, omitting only its presentation notification.
        journal.GetType().GetField("changed", flags).SetValue(journal, null);
        history.ActivateDocument(new DryCycle.DevUI.DevTool.Core.EditorDocumentKey(DryCycle.DevUI.DevTool.Core.EditorDocumentKind.Room, "EffectFixture"));
        sessionType.GetField("<History>k__BackingField", flags).SetValue(session, history);
        var actions = assembly.GetType("DryCycle.DevUI.DevTool.Room.RoomEditorActions", true);
        bool Call(string name, params object[] arguments) => (bool)actions.GetMethod(name, flags).Invoke(null, (new[] { session }).Concat(arguments).ToArray());
        Check(Call("AddRoomEffect", "Fog") && settings.effects.Count == 1 && settings.effects[0].save, "Ordinary effect creation remains authored and saveable with preview retired.");
        Check(!Call("AddRoomEffect", "Fog") && settings.effects.Count == 1, "Normal duplicate-effect protection remains intact.");
        Check(Call("SetRoomEffectAmount", 0, 0, .65f) && Math.Abs(settings.effects[0].amount - .65f) < .001f, "Actual effect slider editing remains available.");
        var presentation = assembly.GetType("DryCycle.DevUI.DevTool.Room.RoomSettingsPresentation", true);
        var rows = (Array)presentation.GetMethod("CaptureEffects", flags).Invoke(null, new[] { session, settings });
        Check(rows.Length == 1, "Committed effects remain in the detached presentation snapshot.");
        string path = Path.Combine(Path.GetTempPath(), "RainWright-effect-fixture-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            settings.Save(path, false);
            Check(File.ReadAllText(path).Contains("Effects: Fog-0.65-"), "The unchanged native serializer writes the committed effect and edited amount.");
            Check(Call("DeleteRoomEffect", 0) && settings.effects.Count == 0, "Ordinary effect deletion remains functional.");
            settings.Save(path, false);
            Check(!File.ReadAllText(path).Contains("Fog-"), "Deleted effects are removed from the saved author TXT.");
            Check(history.CanUndo && history.UndoLabel == "Delete effect Fog", "Ordinary effect edits still publish undo history.");
            Check(owner.activePage == null, "Normal effects do not construct a hidden DevTool page.");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private static void VerifyIndependentPlacement(Assembly assembly)
    {
        var log = (BepInEx.Logging.ManualLogSource)assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorDiagnostics", true).GetProperty("LogSource", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        log.LogEvent += (_, e) => Console.WriteLine("  diagnostic: " + e.Data);
        var bridge = assembly.GetType("DryCycle.DevUI.DevTool.Compatibility.ManagedObjectPlacement", true);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var init = bridge.GetMethod("InitializeData", flags);
        var ensure = bridge.GetMethod("EnsureRuntime", flags);
        var remove = bridge.GetMethod("RemoveRuntime", flags);
        var target = Owner("IndependentPlacement"); var factory = new PlacementFactory(target.type);
        var registry = (IList)typeof(P).GetField("managedObjectTypes", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        registry.Add(factory);
        var room = (Room)FormatterServices.GetUninitializedObject(typeof(Room));
        room.abstractRoom = (AbstractRoom)FormatterServices.GetUninitializedObject(typeof(AbstractRoom));
        try
        {
            Check((bool)init.Invoke(null, new object[] { target }) && target.data is P.ManagedData,
                "A real POM registration creates data without a DevUI, ObjectsPage or representation.");
            Check(((P.ManagedData)target.data).GetValue<int>("value") == 7, "Registered descriptor defaults are preserved.");
            init.Invoke(null, new object[] { target });
            Check(factory.DataCalls == 1, "Existing hook-created data is retained without initializing its factory twice.");
            ensure.Invoke(null, new object[] { room, target }); ensure.Invoke(null, new object[] { room, target });
            Check(factory.RuntimeCalls == 1 && !room.abstractRoom.firstTimeRealized, "Repeated mutation does not duplicate runtime, and restores the prior realization flag.");
            remove.Invoke(null, new object[] { room, target }); ensure.Invoke(null, new object[] { room, target });
            Check(factory.RuntimeCalls == 2, "Delete then undo can recreate the runtime through the registered factory.");
            remove.Invoke(null, new object[] { room, target }); factory.FailRuntime = true;
            bool original = false;
            try { ensure.Invoke(null, new object[] { room, target }); }
            catch (TargetInvocationException error) { original = error.InnerException is InvalidOperationException && error.InnerException.Message == "placement runtime original error"; }
            Check(original && !room.abstractRoom.firstTimeRealized, "A failing factory retains its original exception and restores native state.");
            Check(factory.RepresentationCalls == 0, "No hidden editor representation was used in creation, refresh, removal or restore.");
            // Exercise the actual model factory with a camera/room fixture and no constructed DevUI.
            var gameState = (RainWorldGame)FormatterServices.GetUninitializedObject(typeof(RainWorldGame));
            var camera = (RoomCamera)FormatterServices.GetUninitializedObject(typeof(RoomCamera));
            camera.room = room; gameState.cameras = new[] { camera };
            var owner = (DevInterface.DevUI)FormatterServices.GetUninitializedObject(typeof(DevInterface.DevUI)); owner.game = gameState;
            room.roomSettings = (RoomSettings)FormatterServices.GetUninitializedObject(typeof(RoomSettings));
            room.roomSettings.placedObjects = new List<PlacedObject>();
            var sessionType = assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorSession", true);
            object session = FormatterServices.GetUninitializedObject(sessionType);
            sessionType.GetField("<Owner>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(session, owner);
            var native = assembly.GetType("DryCycle.DevUI.DevTool.Factories.NativePlacedObjectFactory", true).GetMethod("TryCreate", flags);
            factory.FailRuntime = false;
            object[] request = { session, target.type, new Vector2(312, 456), null };
            Check((bool)native.Invoke(null, request) && request[3] is PlacedObject added && added.pos.x == 312 && added.pos.y == 456 && room.roomSettings.placedObjects.Count == 1,
                "The deployable native placement factory adds the registered object at the requested cursor position.");
            Check(owner.activePage == null && factory.RepresentationCalls == 0, "Actual placement leaves the native DevUI page untouched and needs no editor constructor.");
            var captureInspector = assembly.GetType("DryCycle.DevUI.DevTool.Core.EditorPresentationHub", true)
                .GetMethod("CaptureObjectInspector", BindingFlags.Static | BindingFlags.NonPublic);
            object inspectorSnapshot = captureInspector.Invoke(null, new object[] { session, room.roomSettings.placedObjects, 1, request[3] });
            var controls = (Array)inspectorSnapshot.GetType().GetProperty("LegacyControls").GetValue(inspectorSnapshot);
            var inspectorProperties = (IReadOnlyList<EditorPropertySnapshot>)inspectorSnapshot.GetType().GetProperty("Properties").GetValue(inspectorSnapshot);
            Check(inspectorProperties.All(p => p.Key != "__compatibilityNotice"), "The actual object inspector must not append compatibility filler.");
            Check(controls.Length == 0 && owner.activePage == null && factory.RepresentationCalls == 0,
                "Selecting a custom registered object captures a native inspector without constructing a proof representation or original controls.");
            factory.FailRuntime = true; request[3] = null;
            Check(!(bool)native.Invoke(null, request) && request[3] == null && room.roomSettings.placedObjects.Count == 1,
                "Failed runtime creation rolls back the new author item without removing earlier objects.");
            Check(((string)sessionType.GetProperty("PlacementError").GetValue(session)).Contains("placement runtime original error"),
                "The original placement error is published for the UI rather than silently ignoring the click.");
            string regionKitPath = Path.GetFullPath(Path.Combine(game, "../../workshop/content/312520/2920439476/newest/plugins/RegionKit.dll"));
            var kit = Assembly.LoadFrom(regionKitPath);
            foreach (string dataName in new[] { "RegionKit.Modules.RoomZones.CircleZoneData", "RegionKit.Modules.Objects.PlacedWaterfallData" })
            {
                var dataType = kit.GetType(dataName, true);
                var kitOwner = Owner(dataType.Name);
                var kitFactory = new P.ManagedObjectType(kitOwner.type, new DevInterface.ObjectsPage.DevObjectCategories("KitPlacementTests", true), null, dataType, typeof(P.ManagedRepresentation));
                registry.Add(kitFactory);
                try
                {
                    object[] kitRequest = { session, kitOwner.type, new Vector2(40, 80), null };
                    Check((bool)native.Invoke(null, kitRequest) && ((PlacedObject)kitRequest[3]).data.GetType() == dataType,
                        "Installed RegionKit data is placed through its registration without an editor: " + dataName);
                    Check(((P.ManagedData)((PlacedObject)kitRequest[3]).data).fields.Length > 0, "Real RegionKit descriptor defaults are initialized.");
                }
                finally { registry.Remove(kitFactory); }
            }
            room.game = gameState; room.updateList = new List<UpdatableAndDeletable>(); room.updateIndex = -1;
            var adopted = Owner("IndependentPlacement"); init.Invoke(null, new object[] { adopted });
            var existingRuntime = new PlacementRuntime(adopted, room); room.AddObject(existingRuntime);
            int callsBeforeAdoption = factory.RuntimeCalls;
            ensure.Invoke(null, new object[] { room, adopted }); init.Invoke(null, new object[] { adopted }); ensure.Invoke(null, new object[] { room, adopted });
            Check(room.updateList.Count == 1 && ReferenceEquals(room.updateList[0], existingRuntime) && factory.RuntimeCalls == callsBeforeAdoption,
                "An already realized runtime is adopted by its actual model link, and data reinitialization retains that ownership.");
            remove.Invoke(null, new object[] { room, adopted });
            Check(room.updateList.Count == 0, "Deleting an adopted object also removes the pre-existing live runtime.");
            var sentinel = new PlacementRuntime(Owner("Unrelated"), room); room.AddObject(sentinel);
            factory.FailRuntime = true; factory.AddHelperBeforeFailure = true;
            request[3] = null;
            Check(!(bool)native.Invoke(null, request) && room.updateList.Count == 1 && ReferenceEquals(room.updateList[0], sentinel) && !sentinel.slatedForDeletetion,
                "A factory that adds a helper then throws rolls back only its additions and preserves unrelated runtime objects.");
            factory.FailRuntime = false; factory.AddHelperBeforeFailure = false; room.RemoveObject(sentinel);
            var singleOwner = Owner("SingletonRuntime");
            var singleFactory = new P.ManagedObjectType(singleOwner.type, new DevInterface.ObjectsPage.DevObjectCategories("SinglePlacementTests", true), typeof(PlacementRuntime), null, null, singleInstance: true);
            registry.Add(singleFactory);
            try
            {
                object[] firstRequest = { session, singleOwner.type, new Vector2(10, 20), null };
                object[] secondRequest = { session, singleOwner.type, new Vector2(30, 40), null };
                Check((bool)native.Invoke(null, firstRequest) && (bool)native.Invoke(null, secondRequest) && room.updateList.Count == 1,
                    "Real POM singleInstance factories keep a single live controller for multiple placed records.");
                var firstTarget = (PlacedObject)firstRequest[3]; var secondTarget = (PlacedObject)secondRequest[3];
                remove.Invoke(null, new object[] { room, firstTarget }); room.roomSettings.placedObjects.Remove(firstTarget);
                Check(room.updateList.Count == 1 && !room.updateList[0].slatedForDeletetion,
                    "Deleting one singleton record keeps the shared runtime for remaining records.");
                remove.Invoke(null, new object[] { room, secondTarget }); room.roomSettings.placedObjects.Remove(secondTarget);
                Check(room.updateList.Count == 0, "Deleting the final singleton record removes its runtime exactly once.");
                room.roomSettings.placedObjects.Add(secondTarget); ensure.Invoke(null, new object[] { room, secondTarget });
                Check(room.updateList.Count == 1 && !room.updateList[0].slatedForDeletetion, "Undo restores the actual live runtime after deletion.");
                ensure.Invoke(null, new object[] { room, secondTarget });
                Check(room.updateList.Count == 1, "Repeated refresh cannot duplicate the restored controller.");
            }
            finally { registry.Remove(singleFactory); }
            Check(owner.activePage == null, "POM and installed RegionKit placement never constructed or replaced a native editor page.");
            VerifyLegacyDataPlacement(assembly, native, session, room, owner);
        }
        finally { registry.Remove(factory); }
    }
    private static void VerifyLegacyDataPlacement(Assembly assembly, MethodInfo native, object session, Room room, DevInterface.DevUI owner)
    {
        var type = new PlacedObject.Type("RainWrightCompatTest_LegacyModelHook", true);
        On.PlacedObject.hook_GenerateEmptyData dataHook = (orig, target) =>
        {
            orig(target);
            if (target.type == type) target.data = new LegacyData(target);
        };
        On.PlacedObject.GenerateEmptyData += dataHook;
        try
        {
            object[] request = { session, type, new Vector2(12, 34), null };
            Check((bool)native.Invoke(null, request) && ((PlacedObject)request[3]).data is LegacyData,
                "An actual non-POM GenerateEmptyData hook supplies its data to native placement, with no CreateObjRep call.");
            var target = (PlacedObject)request[3];
            var registry = assembly.GetType("DryCycle.DevUI.DevTool.Objects.ObjectInspectorRegistry", true);
            var properties = (IReadOnlyList<EditorPropertySnapshot>)registry.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null, new object[] { target });
            var amount = properties.Single(p => p.Kind == EditorPropertyKind.Float && p.DisplayName.Contains("Amount"));
            Check((bool)registry.GetMethod("TrySetValue", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(null,
                new object[] { target, amount.Key, new EditorPropertyValue(EditorPropertyKind.Float, x: .75f) }),
                "Legacy custom data is editable through RainWright's native field inspector.");
            string text = target.ToString();
            var restored = new PlacedObject(type, null);
            restored.FromString(text.Split(new[] { "><" }, StringSplitOptions.None));
            Check(((LegacyData)restored.data).Amount == .75f && restored.pos.x == 12 && restored.pos.y == 34 && owner.activePage == null,
                "Native legacy edits round-trip through the mod's own serializer without custom TXT markers or editor pages.");
        }
        finally { On.PlacedObject.GenerateEmptyData -= dataHook; }
    }
    private sealed class LegacyData : PlacedObject.Data
    {
        public float Amount = .25f;
        public LegacyData(PlacedObject owner) : base(owner) { }
        public override string ToString() => Amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public override void FromString(string text) => Amount = float.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    }
    private static void VerifyNativeGeometry(Assembly assembly, IObjectInspectorAdapter inspector)
    {
        var capture = assembly.GetType("DryCycle.DevUI.DevTool.Objects.NativeObjectGizmoPresentation", true)
            .GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        EditorObjectGizmoSnapshot Shape(PlacedObject target) => (EditorObjectGizmoSnapshot)capture.Invoke(null,
            new object[] { target, 0, inspector.Capture(target).ToArray() });
        var polygon = Owner("NativePolygon");
        polygon.data = new P.ManagedData(polygon, new P.ManagedField[] { new P.Vector2ArrayField("poly", 3, true,
            P.Vector2ArrayField.Vector2ArrayRepresentationType.Polygon, Vector2.zero, new Vector2(20, 0), new Vector2(20, 30)) });
        var shape = Shape(polygon);
        Check(shape.Handles.Length == 2 && shape.Lines.Length == 3, "Native polygons have editable points, a fixed parent, and a closed outline without any representation.");
        Check(shape.Handles.All(h => h.Id.StartsWith("property:managed.poly[")) && shape.Handles[0].X == 120,
            "Multi-point handles use native property commands and world coordinates.");
        Check(((IObjectInspectorGizmoAdapter)inspector).TryBuildGizmoValue(polygon, "managed.poly[1]", 50, 60, false, out var moved) &&
            inspector.TrySetValue(polygon, "managed.poly[1]", moved) && ((P.ManagedData)polygon.data).GetValue<Vector2[]>("poly")[1].x == 50,
            "Dragging a native polygon handle edits the real POM point data.");
        var chain = Owner("NativeChain");
        chain.data = new P.ManagedData(chain, new P.ManagedField[] { new P.Vector2ArrayField("chain", 3, false,
            P.Vector2ArrayField.Vector2ArrayRepresentationType.Chain, Vector2.zero, new Vector2(20, 0), new Vector2(20, 30)) });
        shape = Shape(chain);
        Check(shape.Handles.Length == 3 && shape.Lines.Length == 2, "An open chain exposes the first point and never closes its last segment.");
        foreach (P.DrivenVector2Field.DrivenControlType mode in Enum.GetValues(typeof(P.DrivenVector2Field.DrivenControlType)))
        {
            var target = Owner("Driven" + mode);
            target.data = new P.ManagedData(target, new P.ManagedField[] {
                new P.Vector2Field("axis", new Vector2(40, 0)),
                new P.DrivenVector2Field("radius", "axis", new Vector2(0, 30), mode) });
            var data = (P.ManagedData)target.data;
            string before = data.ToString();
            shape = Shape(target);
            var handle = shape.Handles.Single(h => h.Id == "property:managed.radius");
            Check(handle.X == (mode == P.DrivenVector2Field.DrivenControlType.relativeLine ? 140 : 100) && handle.Y == 230 && data.ToString() == before,
                "Driven geometry is projected without writing author data during capture: " + mode);
            Check(((IObjectInspectorGizmoAdapter)inspector).TryBuildGizmoValue(target, "managed.radius", mode == P.DrivenVector2Field.DrivenControlType.relativeLine ? 50 : 0, 60, false, out moved) &&
                inspector.TrySetValue(target, "managed.radius", moved) && data.GetValue<Vector2>("radius").y == 60,
                "Native dragging preserves the driven field's storage coordinate system: " + mode);
            if (mode == P.DrivenVector2Field.DrivenControlType.rectangle)
                Check(shape.Lines.Length == 4, "Driven rectangles use four native edges.");
            if (mode == P.DrivenVector2Field.DrivenControlType.perpendicularOval)
                Check(shape.Lines.Length == 48, "Driven ovals use a bounded native outline.");
            if (mode != P.DrivenVector2Field.DrivenControlType.relativeLine)
                Check(inspector.TrySetValue(target, "managed.axis", new(EditorPropertyKind.Vector2, x: 0, y: 40)) &&
                    Math.Abs(data.GetValue<Vector2>("radius").x + 60) < .001f && Math.Abs(data.GetValue<Vector2>("radius").y) < .001f,
                    "Changing a driver rotates its dependent radius inside the same data edit: " + mode);
        }
    }

    private static void VerifyPanelCoordinates(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var presentation = assembly.GetType("DryCycle.DevUI.DevTool.Compatibility.LegacyUiPresentationController", true);
        var panel = (DevInterface.Panel)FormatterServices.GetUninitializedObject(typeof(DevInterface.Panel));
        var child = (DevInterface.Panel)FormatterServices.GetUninitializedObject(typeof(DevInterface.Panel));
        panel.pos = new Vector2(35, 75); child.pos = new Vector2(5, 12);
        panel.subNodes = new List<DevInterface.DevUINode> { child };
        var beamOwner = Owner("PanelBeam"); var beam = new LightBeam.LightBeamData(beamOwner); beamOwner.data = beam;
        beam.panelPos = panel.pos;
        string original = beam.ToString();
        presentation.GetMethod("SuppressSubtree", flags).Invoke(null, new object[] { panel });
        beam.panelPos = panel.pos; // The original representation's Refresh writes this field.
        Check(beam.ToString() == original && panel.pos.x == 35 && child.pos.y == 12,
            "Hiding a native object panel never moves its authored position or its child controls, so original Refresh cannot serialize a suppression sentinel.");
        presentation.GetMethod("SuppressDraggablePanelKeepingHandles", flags).Invoke(null, new object[] { panel });
        Check(panel.pos.x == 35 && panel.pos.y == 75 && child.pos.x == 5,
            "Sound/trigger panel suppression also preserves parent and child coordinates.");
        panel.pos = new Vector2(50, 80);
        presentation.GetMethod("RestoreHiddenPage", flags).Invoke(null, null);
        Check(panel.pos.x == 50 && panel.pos.y == 80, "Restoring visibility cannot overwrite a newer valid panel position with an old cached value.");

        panel.pos = new Vector2(-100000, -100000);
        presentation.GetMethod("SuppressSubtree", flags).Invoke(null, new object[] { panel });
        presentation.GetMethod("RestoreHiddenPage", flags).Invoke(null, null);
        Check(panel.pos.x == -100000 && panel.pos.y == -100000,
            "A player's exact old sentinel coordinate is not proof of corruption and is never automatically reset.");
        panel.pos = new Vector2(-420, 1600);
        presentation.GetMethod("SuppressSubtree", flags).Invoke(null, new object[] { panel });
        presentation.GetMethod("RestoreHiddenPage", flags).Invoke(null, null);
        Check(panel.pos.x == -420 && panel.pos.y == 1600,
            "Offscreen author layouts remain intact across hide/show cycles without a recovery prompt or repositioning.");
    }
    private static PlacedObject Owner(string suffix)
    {
        var owner = (PlacedObject)FormatterServices.GetUninitializedObject(typeof(PlacedObject));
        owner.type = new PlacedObject.Type("RainWrightCompatTest_" + suffix, true);
        owner.pos = new Vector2(100, 200);
        return owner;
    }
    private enum Mode { First, Middle, Last }
    private sealed class RestrictedEnum : P.EnumField<Mode>
    {
        internal Mode[] Entries = new[] { Mode.First, Mode.Last };
        internal RestrictedEnum(string key, Mode value) : base(key, value) { }
        protected override Mode[] PossibleValues => Entries;
    }
    private sealed class BrokenEnum : P.EnumField<Mode>
    {
        internal BrokenEnum(string key, Mode value) : base(key, value) { }
        protected override Mode[] PossibleValues => throw new InvalidOperationException("original enum callback failed");
    }
    private sealed class AliasData : P.ManagedData
    {
        // Initialized by the actual POM attribute scanner.
#pragma warning disable CS0649
        [P.FloatField("00amount", 0, 1, .3f)] public float AuthoredAmount;
#pragma warning restore CS0649
        public AliasData(PlacedObject owner) : base(owner, null) { }
    }
    private sealed class CoupledFloat : P.FloatField
    {
        internal CoupledFloat(string key, float min, float max, float value) : base(key, min, max, value) { }
        public override void NewFactor(DevInterface.PositionedDevUINode node, P.ManagedData data, float factor)
            => throw new InvalidOperationException("custom controller must receive its original context");
    }
    private sealed class DecorativeFloat : P.FloatField
    {
        internal DecorativeFloat(string key, float min, float max, float value) : base(key, min, max, value) { }
        public override DevInterface.DevUINode MakeAditionalNodes(P.ManagedData data, P.ManagedRepresentation representation)
            => throw new Exception("Native data editing must never ask for UI decorations.");
    }
    private static void VerifyRepresentationMetadata(PlacedObject aliasTarget, P.ManagedField[] fields, Assembly assembly)
    {
        var metadata = assembly.GetType("DryCycle.DevUI.DevTool.Compatibility.ManagedObjectRegistration", true);
        var standard = metadata.GetMethod("HasStandardRepresentation", BindingFlags.Static | BindingFlags.NonPublic);
        var aliasFactory = new P.ManagedObjectType(aliasTarget.type, new DevInterface.ObjectsPage.DevObjectCategories("RainWrightTests", true),
            null, typeof(AliasData), typeof(P.ManagedRepresentation));
        // RegisterManagedObject also activates Unity hooks. Populate the real
        // framework registry directly for metadata tests without starting a game.
        var registry = (IList)typeof(P).GetField("managedObjectTypes", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        registry.Add(aliasFactory);
        try
        {
            Check((bool)standard.Invoke(null, new object[] { aliasTarget }), "Generic POM registration using the standard representation is recognized.");
            var reprField=typeof(P.ManagedObjectType).GetField("reprType",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            var originalRepresentation=reprField.GetValue(aliasFactory);
            try
            {
                reprField.SetValue(aliasFactory,typeof(P.ManagedData));
                Check(!(bool)standard.Invoke(null,new object[]{aliasTarget}),"Changing POM representation metadata without changing registry length invalidates cached assumptions.");
            }
            finally {reprField.SetValue(aliasFactory,originalRepresentation);}
            Check((bool)standard.Invoke(null,new object[]{aliasTarget}),"Restoring original representation metadata recovers compatibility without resetting the entire editor.");
            var fully = Owner("FullyManaged"); fully.data = new P.ManagedData(fully, fields);
            var factory = new P.FullyManagedObjectType(fully.type, new DevInterface.ObjectsPage.DevObjectCategories("RainWrightTests", true), null, fields);
            registry.Add(factory);
            try { Check((bool)standard.Invoke(null, new object[] { fully }), "The original FullyManaged factory shortcut remains compatible."); }
            finally { registry.Remove(factory); }
        }
        finally { registry.Remove(aliasFactory); }
    }
    private static void VerifySpriteGeometry(Assembly assembly)
    {
        var type = assembly.GetType("DryCycle.DevUI.DevTool.Compatibility.LegacySpriteGeometry", true);
        var capture = type.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        EditorObjectLineSegmentSnapshot[] Shape(string element, string shader, float width = 16, float height = 16,
            float sx = 10, float sy = 5, float rotation = 0, float anchorX = .5f, float anchorY = .5f, float alpha = .02f) =>
            (EditorObjectLineSegmentSnapshot[])capture.Invoke(null, new object[]
            { element, shader, width, height, 100f, 200f, sx, sy, rotation, anchorX, anchorY, .2f, .4f, .7f, alpha });
        var oval = Shape("Futile_White", "VectorCircle");
        Check(oval.Length == 32 && Math.Abs(oval.Max(p => p.X0) - 180) < .001f && Math.Abs(oval.Max(p => p.Y0) - 240) < .001f,
            "The real POM VectorCircle scale convention reproduces both ellipse axes.");
        Check(Math.Abs(oval[0].X0 - oval[oval.Length - 1].X1) < .001f && Math.Abs(oval[0].Y0 - oval[oval.Length - 1].Y1) < .001f,
            "Range outlines close without a missing final edge.");
        Check(oval.All(p => p.HasColor && p.Red == .2f && p.Green == .4f && p.Blue == .7f && p.Alpha == 1 && p.Thickness >= 1),
            "Shader stroke alpha is not incorrectly used as near-invisible ImGui opacity; range colors are preserved.");
        var rotated = Shape("Futile_White", "VectorCircle", rotation: 90);
        Check(Math.Abs(rotated.Max(p => p.X0) - 140) < .001f && Math.Abs(rotated.Max(p => p.Y0) - 280) < .001f,
            "Rotated perpendicular POM ellipses use the sprite's rotated axes.");
        var anchored = Shape("Futile_White", "VectorCircle", anchorX: 0, anchorY: 0);
        Check(Math.Abs(anchored.Min(p => p.X0) - 100) < .001f && Math.Abs(anchored.Min(p => p.Y0) - 200) < .001f,
            "Noncentral sprite anchors move the range center correctly.");
        var atlas = Shape("Circle20", "Basic", width: 40, height: 20, sx: 2, sy: 3, alpha: .6f);
        Check(Math.Abs(atlas.Max(p => p.X0) - 140) < .001f && Math.Abs(atlas.Max(p => p.Y0) - 230) < .001f && atlas[0].Alpha == .6f,
            "Atlas circles use actual element dimensions and ordinary opacity, not the VectorCircle fixed size.");
        var rectangle = Shape("pixel", "Basic", width: 1, height: 1, sx: -30, sy: 20, anchorX: 0, anchorY: 0, alpha: .4f);
        Check(rectangle.Length == 4 && rectangle.Min(p => p.X0) == 70 && rectangle.Max(p => p.X0) == 100 && rectangle[0].Alpha == .4f,
            "Mirrored anchored rectangle bounds and opacity match the original sprite.");
        var line = Shape("pixel", "Basic", width: 1, height: 1, sx: 1, sy: 40, rotation: 90, anchorY: 0);
        Check(line.Length == 1 && Math.Abs(line[0].X1 - 140) < .001f && Math.Abs(line[0].Y1 - 200) < .001f,
            "Thin rotated pixel sprites remain range lines rather than rectangle frames.");
        Check(Shape("CustomMesh", "CustomShader").Length == 0 && Shape("Circle20", "Basic", sx: float.NaN).Length == 0,
            "Unknown effects and nonfinite coordinates are not falsely reported as supported geometry.");
        Check(Shape("Futile_White", "VectorCircle", sx: 1000, sy: 1000).Length == 128,
            "Large range rings improve curve detail within a bounded tessellation budget.");
        Check(Shape("pixel","VectorCircle").Length==32,"Circle material semantics take precedence over a pixel atlas element.");
        Check(Shape("pixel","Basic",width:1,height:1,sx:100,sy:20,anchorX:float.MaxValue).Length==0,
            "Finite inputs whose projected positions overflow cannot enter scene snapshots.");
        Check(Shape("Futile_White","VectorCircle",sx:float.MaxValue).Length==0&&Shape("Futile_White","VectorCircle",sx:1e30f,sy:1e30f,alpha:1e30f).Length==0,
            "Overflowing range dimensions and stroke widths are rejected before rendering.");
    }

    private static void VerifyGizmoMerge(Assembly assembly)
    {
        var type = assembly.GetType("DryCycle.DevUI.DevTool.Objects.NativeObjectGizmoPresentation", true);
        var merge = type.GetMethod("Merge", BindingFlags.Static | BindingFlags.NonPublic);
        var native = new EditorObjectGizmoSnapshot { ObjectIndex = 0, Handles = new[] {
            new EditorObjectGizmoHandleSnapshot { Id = "native", X = 10, Y = 20, AnchorX = 2, AnchorY = 3 },
            new EditorObjectGizmoHandleSnapshot { Id = "unique", X = 50, Y = 60 } } };
        var original = new EditorObjectGizmoSnapshot { ObjectIndex = 0, Handles = new[] {
            new EditorObjectGizmoHandleSnapshot { Id = "property:managed.point", X = 10, Y = 20, AnchorX = 2, AnchorY = 3 } } };
        var merged = (EditorObjectGizmoSnapshot)merge.Invoke(null, new object[] { native, original });
        Check(merged.Handles.Length == 3 && merged.Handles.Any(h => h.Id == "unique") && merged.Handles.Any(h => h.Id == "property:managed.point"),
            "Coincident handles for different native fields remain independently editable.");
    }

    private static void VerifyInvalidManagedData(IObjectInspectorAdapter inspector)
    {
        var target=Owner("InvalidValues"); var data=new P.ManagedData(target,new P.ManagedField[]{new P.FloatField("bad",0,1,.5f),new P.FloatField("healthy",0,1,.4f)}); target.data=data;
        data.SetValue("bad",float.NaN);
        var properties=inspector.Capture(target);
        Check(properties.Single(p=>p.Key=="managed.bad").Kind==EditorPropertyKind.ReadOnly&&properties.Single(p=>p.Key=="managed.healthy").Kind==EditorPropertyKind.Float&&float.IsNaN(data.GetValue<float>("bad")),
            "Existing nonfinite author data remains untouched while healthy fields stay editable.");
        Check(!((IObjectInspectorCoverageProvider)inspector).GetCoverage(target).IsComplete,"Malformed fields cannot certify complete native compatibility.");
        data.SetValue("bad",.6f);
        Check(inspector.Capture(target).Single(p=>p.Key=="managed.bad").Kind==EditorPropertyKind.Float,"A repaired original field automatically recovers native editing.");
        data.fields[1]=data.fields[0]; properties=inspector.Capture(target);
        Check(properties.Count(p=>p.Key=="managed.bad")==1&&properties.Single(p=>p.Key=="managed.bad").Kind==EditorPropertyKind.ReadOnly,
            "Duplicate managed keys cannot expose ambiguous editable property IDs.");
        var points=Owner("BadPoints"); var arrays=new P.ManagedData(points,new P.ManagedField[]{new P.Vector2ArrayField("points",3,false,P.Vector2ArrayField.Vector2ArrayRepresentationType.Polygon,new[]{new Vector2(1,2),new Vector2(3,4),new Vector2(5,6)})}); points.data=arrays;
        arrays.SetValue("points",new[]{new Vector2(1,2),new Vector2(float.NaN,4),new Vector2(5,6)});
        properties=inspector.Capture(points);
        Check(properties.Single(p=>p.Key.StartsWith("managed.points")).Kind==EditorPropertyKind.ReadOnly,"A corrupt multi-point array does not leave partial duplicate editable point bindings.");
        arrays.SetValue("points",new Vector2[5000]); properties=inspector.Capture(points);
        Check(properties.Single(p=>p.Key=="managed.points").Kind==EditorPropertyKind.ReadOnly&&properties.Single(p=>p.Key=="managed.points").StringValue.Contains("4096"),
            "Oversized authored arrays are reported rather than generating unbounded controls or altering author data.");
    }

    private static void VerifyMeshGeometry(Assembly assembly)
    {
        var method=assembly.GetType("DryCycle.DevUI.DevTool.Compatibility.LegacyMeshGeometry",true).GetMethod("Capture",BindingFlags.Static|BindingFlags.NonPublic);
        var xy=new float[]{0,0,20,0,20,10,0,10}; var triangles=new[]{0,1,2,0,2,3};
        EditorObjectLineSegmentSnapshot[] Mesh(float[] vertices,int[] indices,float rotation=0)=>
            (EditorObjectLineSegmentSnapshot[])method.Invoke(null,new object[]{vertices,indices,100f,200f,2f,3f,rotation,.2f,.4f,.6f,.8f});
        var edges=Mesh(xy,triangles);
        Check(edges.Length==4&&!edges.Any(e=>e.X0!=e.X1&&e.Y0!=e.Y1),"Generic triangle meshes publish their outer boundary without internal triangle diagonals.");
        Check(edges.Max(e=>e.X0)==140&&edges.Max(e=>e.Y0)==230&&edges.All(e=>e.Alpha==.8f&&e.Red==.2f),"Mesh scale, position and color are preserved.");
        var rotated=Mesh(xy,triangles,90);
        Check(Math.Abs(rotated.Max(e=>e.X0)-130)<.001f&&Math.Abs(rotated.Min(e=>e.Y0)-160)<.001f,"Mesh boundary rotation follows the original Futile transform.");
        bool Reject(float[] vertices,int[] indices){try{Mesh(vertices,indices);return false;}catch(TargetInvocationException e){return e.InnerException is InvalidOperationException;}}
        Check(Reject(xy,new[]{0,1,999})&&Reject(new[]{float.NaN,0f,1f,1f,2f,2f},new[]{0,1,2}),"Invalid triangle indices and nonfinite vertices fail as isolated geometry errors.");
        Check(Reject(new float[8194],triangles),"Mesh geometry work is bounded before allocating projection output.");

    }
}
