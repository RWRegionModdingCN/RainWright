using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.History;
using DryCycle.DevUI.DevTool.Input;
using DryCycle.DevUI.DevTool.World;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Background;

internal sealed class BackgroundFieldSnapshot
{
    internal int Id;
    internal string Name, Value, Kind, Key;
    internal bool Inherited, CanInherit;
    internal string[] Choices = Array.Empty<string>();
}
internal sealed class BackgroundElementSnapshot
{
    internal int Id, ParentId;
    internal string Name;
    internal bool IsGroup;
    internal BackgroundFieldSnapshot[] Fields;
}
internal sealed class BackgroundEditorSnapshot
{
    internal static readonly BackgroundEditorSnapshot Empty = new();
    internal long Context, Revision;
    internal bool Available, HasRegionKit, Dirty, Failed;
    internal string Scene = "None", Name = "", Parent = "", Status = "", Error = "";
    internal string[] Files = Array.Empty<string>();
    internal string[] Illustrations = Array.Empty<string>(), Sprites = Array.Empty<string>(), Shaders = Array.Empty<string>();
    internal BackgroundFieldSnapshot[] Settings = Array.Empty<BackgroundFieldSnapshot>();
    internal BackgroundElementSnapshot[] Elements = Array.Empty<BackgroundElementSnapshot>();
    internal string[] AddTypes = Array.Empty<string>();
}
internal readonly struct BackgroundEditorCommand
{
    internal readonly long Context, Revision;
    internal readonly string Kind, Value;
    internal readonly int Id;
    internal BackgroundEditorCommand(BackgroundEditorSnapshot source, string kind, int id = 0, string value = "")
    { Context = source.Context; Revision = source.Revision; Kind = kind; Id = id; Value = value; }
}

/// <summary>Native authoring owner. Render callbacks consume detached state and enqueue commands only.</summary>
internal static class BackgroundEditorRuntime
{
    private sealed class Binding
    {
        internal object Target;
        internal FieldInfo Field;
        internal string Label;
        internal object Element;
        internal BackgroundDataAccess.Parameter Parameter;
    }
    private sealed class State
    {
        internal readonly long Id = Interlocked.Increment(ref nextId);
        internal long Revision;
        internal global::Room Room;
        internal object Root, SceneData;
        internal BackgroundScene Scene;
        internal string SavedText, Error = "", Status = "", OutputPath, ExpectedOutput;
        internal string[] Files = Array.Empty<string>();
        internal string[] Illustrations = Array.Empty<string>(), Sprites = Array.Empty<string>(), Shaders = Array.Empty<string>();
        internal readonly Dictionary<int, Binding> Bindings = new();
        internal readonly Dictionary<int, object> Elements = new();
        internal bool CloudsDirty, RubbleDirty, GridDirty, PositionsDirty;
        internal bool Dirty, Failed;
    }
    private sealed class Checkpoint
    {
        internal object Data;
        internal string Type;
        internal (int Index, RoomSettings.RoomEffect Effect)[] Effects;
    }
    private static readonly ConditionalWeakTable<RoomSettings, State> states = new();
    private static readonly ConcurrentQueue<BackgroundEditorCommand> commands = new();
    private static int pending;
    private static int rejected;
    private static long nextId;
    private static State active;
    private static bool wasActive;
    private static volatile BackgroundEditorSnapshot current = BackgroundEditorSnapshot.Empty;
    internal static BackgroundEditorSnapshot Current => current;
    internal static void Enqueue(BackgroundEditorCommand command)
    {
        if (Interlocked.Increment(ref pending) > 128) { Interlocked.Decrement(ref pending); Interlocked.Increment(ref rejected); return; }
        commands.Enqueue(command);
    }
    internal static void Process(EditorSession session)
    {
        try { ProcessCore(session); }
        catch (Exception error)
        {
            EditorDiagnostics.Warning("Background editor activation failed; original DevTool remains available: " + error);
            if (active != null) { active.Failed = true; active.Error = error.Message; Rebuild(active); }
            else current = new BackgroundEditorSnapshot { Available = true, Failed = true, Error = error.Message };
            wasActive = true;
        }
    }
    private static void ProcessCore(EditorSession session)
    {
        bool owns = session?.ToolMode == EditorToolMode.Background && session.RoomSettings != null && EditorRevisionHub.IsRebuiltPresentationActive(session);
        if (owns)
        {
            State state = states.GetValue(session.RoomSettings, _ => new State { Room = session.Room });
            if (state.Failed && (!ReferenceEquals(active, state) || !wasActive)) Rebuild(state);
            if (!state.Failed && (!ReferenceEquals(active, state) || !wasActive || !ReferenceEquals(state.Room, session.Room)))
            {
                active = state; state.Room = session.Room; Observe(state); RefreshFiles(state, assets: true);
                Rebuild(state);
            }
            active = state;
        }
        wasActive = owns;
        for (int processed = 0; processed < 32 && commands.TryDequeue(out var command); processed++)
        {
            Interlocked.Decrement(ref pending);
            if (!owns || active == null || command.Context != active.Id) continue;
            if (command.Revision != active.Revision)
            {
                active.Error = "界面数据已更新，请重试刚才的操作。 / Background changed; please retry the last edit.";
                Rebuild(active); continue;
            }
            Checkpoint before = null;
            try
            {
                bool mutation = command.Kind is "field" or "add" or "delete" or "type" or "load" or "parent" or "group";
                if (mutation) before = Capture(active);
                Execute(session, active, command);
                FlushNativeWork(active);
                if (mutation)
                {
                    var after = Capture(active); State target = active; RoomSettings owner = session.RoomSettings; global::Room ownerRoom = session.Room;
                    session.History.Push(new DelegateHistoryEntry("Background " + command.Kind,
                        s => ReferenceEquals(s.RoomSettings, owner) && ReferenceEquals(s.Room, ownerRoom) && Restore(target, before),
                        s => ReferenceEquals(s.RoomSettings, owner) && ReferenceEquals(s.Room, ownerRoom) && Restore(target, after)));
                }
                // Save reports its own failure and must keep the error visible.
                if (command.Kind != "save") active.Error = "";
            }
            catch (Exception error)
            {
                if (command.Kind == "retry") active.Failed = true;
                if (before != null)
                    try { Restore(active, before); } catch (Exception recovery) { EditorDiagnostics.Warning("Background transaction rollback failed: " + recovery); }
                active.Error = error.Message; EditorDiagnostics.Warning("Background editor command failed: " + error);
            }
            Rebuild(active);
        }
        if (owns && active != null && Interlocked.Exchange(ref rejected, 0) > 0)
        { active.Error = "编辑队列已满，请重试刚才的操作。 / Background edit queue was full; please retry."; Rebuild(active); }
        if (!owns) current = BackgroundEditorSnapshot.Empty;
    }
    private static void Observe(State state)
    {
        state.Root = BackgroundDataAccess.Root(state.Room.roomSettings);
        state.SceneData = BackgroundDataAccess.Get(state.Root, "sceneData");
        state.Scene = BackgroundSceneAdapter.Find(state.Room);
        foreach (object record in BackgroundDataAccess.Elements(state.SceneData))
            if (BackgroundDataAccess.Get(record, "sceneElement") is BackgroundScene.BackgroundSceneElement element) BackgroundAppearanceRuntime.Bind(element, record);
        if (state.SavedText == null) state.SavedText = Fingerprint(state);
    }
    private static Checkpoint Capture(State state) => new()
    {
        Data = BackgroundDataAccess.CloneDataGraph(state.Root), Type = BackgroundSceneAdapter.Name(state.Scene),
        Effects = state.Room.roomSettings.effects.Select((effect, index) => (Index: index, Effect: effect))
            .Where(e => BackgroundSceneAdapter.SceneNames.Contains(e.Effect.type.value)).ToArray()
    };
    private static bool Restore(State state, Checkpoint checkpoint)
    {
        if (checkpoint.Data != null && state.Root != null)
        { BackgroundDataAccess.Set(state.Root, "backgroundName", ""); BackgroundDataAccess.Set(state.Root, "parent", null); }
        state.Scene = BackgroundSceneAdapter.Switch(state.Room, checkpoint.Type);
        Observe(state);
        if (checkpoint.Data != null)
        {
            var generatedGrid = (BackgroundDataAccess.Get(state.SceneData, "theGrid") as IEnumerable)?.Cast<object>().ToArray();
            if (BackgroundDataAccess.Get(state.SceneData, "backgroundElements") is IEnumerable generated)
                foreach (object record in generated) RetireTree(state, record);
            object copy = BackgroundDataAccess.CloneDataGraph(checkpoint.Data);
            BackgroundDataAccess.CopyFields(copy, state.Root);
            if (state.OutputPath != null) BackgroundDataAccess.Set(state.Root, "backgroundName", Path.GetFileNameWithoutExtension(state.OutputPath));
            state.SceneData = BackgroundDataAccess.Get(state.Root, "sceneData");
            if (generatedGrid != null && BackgroundDataAccess.Get(state.SceneData, "theGrid") is IList grid)
            { grid.Clear(); foreach (object element in generatedGrid) grid.Add(element); }
            var visited = new HashSet<object>();
            for (object root = state.Root; root != null && visited.Add(root); root = BackgroundDataAccess.Get(root, "parent"))
                if (BackgroundDataAccess.Get(root, "sceneData") is object data) BackgroundDataAccess.Set(data, "_Scene", state.Scene);
            foreach (object record in BackgroundDataAccess.Elements(state.SceneData)) BackgroundDataAccess.Set(record, "sceneElement", null);
            if (BackgroundDataAccess.Get(state.SceneData, "backgroundElements") is IEnumerable roots)
                foreach (object record in roots) ReplaceTree(state, record);
            foreach (var parameter in BackgroundDataAccess.Parameters(state.SceneData)) ApplySceneParameter(state, new Binding { Target = state.SceneData, Field = parameter.Field, Parameter = parameter });
        }
        var effects = state.Room.roomSettings.effects;
        effects.RemoveAll(e => BackgroundSceneAdapter.SceneNames.Contains(e.type.value));
        foreach (var entry in checkpoint.Effects) effects.Insert(Math.Min(entry.Index, effects.Count), entry.Effect);
        FlushNativeWork(state); Rebuild(state); return true;
    }
    private static void RefreshFiles(State state, bool assets = false)
    {
        if (assets)
        {
            state.Illustrations = AssetManager.ListDirectory("Illustrations", false, false)
                .Where(p => p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            state.Sprites = Futile.atlasManager?._allElementsByName.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
            state.Shaders = state.Room?.game?.rainWorld?.Shaders.Keys.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();
        }
        state.Files = AssetManager.ListDirectory("assets/regionkit/backgrounds", false, false)
            .Where(p => p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileNameWithoutExtension)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static BackgroundDocument Export(State state)
    {
        var doc = BackgroundDocument.Parse("");
        doc.Set("Type", BackgroundSceneAdapter.Name(state.Scene));
        object parent = BackgroundDataAccess.Get(state.Root, "parent");
        if (BackgroundDataAccess.Get(parent, "backgroundName") is string name && name.Length > 0) doc.Set("Parent", name);
        if (state.Root != null)
        {
            Vector2 offset = TotalOffset(state.Root);
            doc.Set("OffsetX", BackgroundDataAccess.F(offset.x)); doc.Set("OffsetY", BackgroundDataAccess.F(offset.y));
        }
        foreach (var parameter in BackgroundDataAccess.Parameters(state.SceneData))
        {
            object value = parameter.Field.GetValue(state.SceneData);
            if (value != null) doc.Set(parameter.Name, BackgroundDataAccess.Text(value));
        }
        var inherited = BackgroundDataAccess.Get(BackgroundDataAccess.Get(parent, "sceneData"), "backgroundElements") as IEnumerable;
        var elements = BackgroundDataAccess.Get(state.SceneData, "backgroundElements") as IEnumerable;
        foreach (string block in BackgroundRecordCodec.Delta(elements, inherited)) doc.Lines.AddRange(BackgroundDocument.Parse(block).Lines);
        return doc;
    }
    private static string Fingerprint(State state)
    {
        string offset = BackgroundDataAccess.Text(BackgroundDataAccess.Get(state.Root, "roomOffset"));
        return Export(state).Serialize() + "\nroomOffset=" + offset;
    }
    private static void Execute(EditorSession session, State state, BackgroundEditorCommand command)
    {
        if (command.Kind == "retry") { state.Failed = false; Observe(state); RefreshFiles(state, assets: true); return; }
        if (state.Failed) throw new InvalidOperationException("Retry background initialization before editing.");
        if (command.Kind == "refresh") { RefreshFiles(state, assets: true); return; }
        if (command.Kind == "save") { Commands.EditorActions.Save(session, command.Value); return; }
        if (command.Kind == "load") { Load(state, BackgroundDocument.FileName(command.Value)); return; }
        if (command.Kind == "parent") { SetParent(state, command.Value); return; }
        if (command.Kind == "group" && state.Elements.TryGetValue(command.Id, out object member))
        {
            object parent = null;
            if (int.TryParse(command.Value, out int groupId) && groupId != 0 && !state.Elements.TryGetValue(groupId, out parent)) throw new InvalidOperationException("Group is no longer available.");
            if (parent != null && BackgroundDataAccess.Get(parent, "elements") is not IList) throw new InvalidOperationException("Not a background group.");
            var visited = new HashSet<object>();
            for (object cursor = parent; cursor != null && visited.Add(cursor); cursor = BackgroundDataAccess.Get(cursor, "group"))
                if (ReferenceEquals(cursor, member)) throw new InvalidOperationException("A group cannot contain itself or its ancestor.");
            object oldParent = BackgroundDataAccess.Get(member, "group");
            var oldList = (IList)BackgroundDataAccess.Get(oldParent ?? state.SceneData, oldParent == null ? "backgroundElements" : "elements");
            var newList = (IList)BackgroundDataAccess.Get(parent ?? state.SceneData, parent == null ? "backgroundElements" : "elements");
            oldList.Remove(member); newList.Add(member); BackgroundDataAccess.Set(member, "group", parent); ReplaceTree(state, member); return;
        }
        if (command.Kind == "type")
        {
            if (BackgroundDataAccess.Get(state.Root, "backgroundName") is string) BackgroundDataAccess.Set(state.Root, "backgroundName", "");
            if (state.Root != null) BackgroundDataAccess.Set(state.Root, "parent", null);
            state.Scene = BackgroundSceneAdapter.Switch(state.Room, command.Value); Observe(state); return;
        }
        if (command.Kind == "field" && state.Bindings.TryGetValue(command.Id, out Binding binding))
        {
            object value = BackgroundDataAccess.Parse(command.Value, binding.Field.FieldType);
            ValidateField(binding.Parameter?.Name ?? binding.Field.Name, value);
            if (value is string text && (binding.Field.Name == "assetName" || binding.Field.Name == "name") && (text.Contains(",") || text.Contains(" : ")))
                throw new FormatException("Element asset/group names cannot contain TXT argument delimiters.");
            if (binding.Field.Name == "spriteShader" && value is string shader && !state.Room.game.rainWorld.Shaders.ContainsKey(shader)) throw new ArgumentException("Unknown sprite shader: " + shader);
            binding.Field.SetValue(binding.Target, value);
            if (binding.Element != null) ReplaceTree(state, binding.Element);
            else if (value == null && binding.Parameter != null) Restore(state, Capture(state));
            else ApplySceneParameter(state, binding);
            return;
        }
        if (command.Kind == "add")
        {
            var schema = BackgroundElementSchema.Find(command.Value, BackgroundSceneAdapter.Name(state.Scene)) ?? throw new InvalidOperationException("Unknown element.");
            object record = BackgroundDataAccess.AllocateRecord(schema);
            if (state.Scene == null || BackgroundDataAccess.Get(state.SceneData, "backgroundElements") is not IList list) throw new InvalidOperationException("Select a background scene first.");
            if (schema.Record == "BG_ElementGroup")
            {
                int suffix = 1;
                var names = new HashSet<string>(BackgroundDataAccess.Elements(state.SceneData).Select(r => BackgroundDataAccess.Get(r, "name") as string));
                while (names.Contains("Group " + suffix)) suffix++;
                BackgroundDataAccess.Set(record, "name", "Group " + suffix);
            }
            BackgroundSceneAdapter.ReplaceElement(state.Room, state.Scene, record); list.Add(record);
            return;
        }
        if (command.Kind == "delete" && state.Elements.TryGetValue(command.Id, out object target))
        {
            if (BackgroundDataAccess.Schema(target) == null) throw new NotSupportedException("Unrecognized extension data is retained for the original editor.");
            object group = BackgroundDataAccess.Get(target, "group");
            var list = BackgroundDataAccess.Get(group ?? state.SceneData, group == null ? "backgroundElements" : "elements") as IList;
            if (list == null) throw new InvalidOperationException("Element owner is missing.");
            RetireTree(state, target); list.Remove(target); return;
        }
        throw new InvalidOperationException("Background control is no longer available.");
    }
    private static void SetParent(State state, string name)
    {
        if (state.Root == null) throw new InvalidOperationException("Background data is unavailable.");
        if (name.Length == 0)
        {
            Vector2 offset = TotalOffset(state.Root);
            foreach (var parameter in BackgroundDataAccess.Parameters(state.SceneData))
            {
                object effective = BackgroundDataAccess.Effective(state.SceneData, parameter);
                if (effective != null) parameter.Field.SetValue(state.SceneData, effective);
            }
            BackgroundDataAccess.Set(state.Root, "parent", null); BackgroundDataAccess.Set(state.SceneData, "parent", null);
            BackgroundDataAccess.Set(state.Root, "_backgroundOffset", offset);
            return;
        }
        name = BackgroundDocument.FileName(name);
        if (name.Equals(BackgroundDataAccess.Get(state.Root, "backgroundName") as string, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A background cannot inherit itself.");
        object child = BackgroundDataAccess.CloneDataGraph(state.Root);
        Vector2 childOffset = TotalOffset(state.Root);
        string oldType = BackgroundSceneAdapter.Name(state.Scene);
        Load(state, name);
        if (oldType != "None" && oldType != BackgroundSceneAdapter.Name(state.Scene)) throw new InvalidOperationException("Parent background type differs from the current scene.");
        object parent = BackgroundDataAccess.CloneDataGraph(state.Root);
        object childData = BackgroundDataAccess.Get(child, "sceneData");
        if (oldType == "None")
        {
            childData = BackgroundDataAccess.CloneDataGraph(state.SceneData);
            ((IList)BackgroundDataAccess.Get(childData, "backgroundElements")).Clear();
        }
        var own = (IList)BackgroundDataAccess.Get(childData, "backgroundElements");
        object oldParent = BackgroundDataAccess.Get(child, "parent");
        var oldInherited = BackgroundDataAccess.Get(BackgroundDataAccess.Get(oldParent, "sceneData"), "backgroundElements") as IEnumerable;
        if (oldInherited != null)
        {
            var inheritedText = oldInherited.Cast<object>().Select(r => BackgroundRecordCodec.Text(r)).ToList();
            foreach (object record in own.Cast<object>().ToArray())
                if (inheritedText.Remove(BackgroundRecordCodec.Text(record))) own.Remove(record);
        }
        var merged = (IList)Activator.CreateInstance(own.GetType());
        var parentRecords = (IList)BackgroundDataAccess.Get(BackgroundDataAccess.CloneDataGraph(BackgroundDataAccess.Get(parent, "sceneData")), "backgroundElements");
        foreach (object record in parentRecords) merged.Add(record);
        foreach (object record in own) merged.Add(record);
        BackgroundDataAccess.Set(childData, "backgroundElements", merged);
        BackgroundDataAccess.Set(childData, "parent", BackgroundDataAccess.Get(parent, "sceneData"));
        BackgroundDataAccess.Set(childData, "_Scene", state.Scene);
        BackgroundDataAccess.Set(child, "parent", parent); BackgroundDataAccess.Set(child, "sceneData", childData);
        BackgroundDataAccess.Set(child, "_backgroundOffset", childOffset - TotalOffset(parent));
        foreach (object record in BackgroundDataAccess.Elements(state.SceneData))
            if (BackgroundDataAccess.Get(record, "sceneElement") is BackgroundScene.BackgroundSceneElement element) { element.Destroy(); state.Scene.elements.Remove(element); state.Room.RemoveObject(element); }
        BackgroundDataAccess.CopyFields(child, state.Root); state.SceneData = childData;
        foreach (object record in BackgroundDataAccess.Elements(state.SceneData)) BackgroundDataAccess.Set(record, "sceneElement", null);
        foreach (object record in merged) ReplaceTree(state, record);
        foreach (var parameter in BackgroundDataAccess.Parameters(state.SceneData)) ApplySceneParameter(state, new Binding { Target = state.SceneData, Field = parameter.Field, Parameter = parameter });
    }
    private static Vector2 TotalOffset(object root)
    {
        var visited = new HashSet<object>(); Vector2 result = Vector2.zero;
        for (object cursor = root; cursor != null; cursor = BackgroundDataAccess.Get(cursor, "parent"))
        {
            if (!visited.Add(cursor)) throw new InvalidOperationException("Background parent cycle detected.");
            if (BackgroundDataAccess.Get(cursor, "_backgroundOffset") is Vector2 offset) result += offset;
        }
        return result;
    }
    private static void ReplaceTree(State state, object record)
    {
        if (BackgroundDataAccess.Get(record, "elements") is IEnumerable children)
        { foreach (object child in children) ReplaceTree(state, child); }
        else BackgroundSceneAdapter.ReplaceElement(state.Room, state.Scene, record);
    }
    private static void RetireTree(State state, object record)
    {
        if (BackgroundDataAccess.Get(record, "elements") is IEnumerable children)
            foreach (object child in children) RetireTree(state, child);
        if (BackgroundDataAccess.Get(record, "sceneElement") is BackgroundScene.BackgroundSceneElement element)
        { element.Destroy(); state.Scene.elements.Remove(element); state.Room.RemoveObject(element); }
    }
    private static void ApplySceneParameter(State state, Binding binding)
    {
        if (binding.Target == state.Root)
        {
            if (binding.Field.Name == "roomOffset") BackgroundDataAccess.Set(state.Root, "roomOffsetInit", true);
            return;
        }
        if (binding.Parameter == null || state.Scene == null) return;
        string name = binding.Parameter.Name;
        object value = BackgroundDataAccess.Effective(state.SceneData, binding.Parameter);
        var nativeField = BackgroundDataAccess.Field(state.Scene.GetType(), name);
        if (nativeField != null && value != null && nativeField.FieldType.IsInstanceOfType(value)) nativeField.SetValue(state.Scene, value);
        if (state.Scene is RotWormScene && value is float scalar)
        {
            if (name == "sceneScale") RotWormScene.sceneScale = scalar;
            if (name == "depthScale") RotWormScene.depthScale = scalar;
            if (name == "fogDepth") RotWormScene.fogDepth = scalar;
        }
        if (state.Scene is AboveCloudsView clouds)
        {
            clouds.sceneOrigo = new Vector2(2514f, (clouds.startAltitude + clouds.endAltitude) / 2f);
            Shader.SetGlobalVector(RainWorld.ShadPropSceneOrigoPosition, clouds.sceneOrigo);
        }
        if (name == "origin")
        {
            state.Scene.sceneOrigo = value is Vector2 origin ? origin : state.Scene.RoomToWorldPos(state.Room.abstractRoom.size.ToVector2() * 10f);
            Shader.SetGlobalVector(RainWorld.ShadPropSceneOrigoPosition, state.Scene.sceneOrigo);
        }
        if (name is "cloudsCount" or "distantCloudsCount" or "curveCloudDepth" or "overrideYStart" or "overrideYEnd") state.CloudsDirty = true;
        if (name is "rubbleCount" or "rubbleStartDepth" or "rubbleEndDepth" or "curveRubbleDepth") state.RubbleDirty = true;
        if (name is "seed" or "sceneScale" or "fogDepth" or "gridLayers" or "gridParallelDepth" or "gridPerpendicularDepth") state.GridDirty = true;
        if (name == "floorLevel") state.PositionsDirty = true;
        if (name == "DefaultContainer" && value != null)
        {
            var containers = new Dictionary<BackgroundScene.BackgroundSceneElement, string>();
            foreach (object record in BackgroundDataAccess.Elements(state.SceneData))
                if (BackgroundDataAccess.Get(record, "sceneElement") is BackgroundScene.BackgroundSceneElement element && BackgroundDataAccess.Get(record, "container") is object ownContainer)
                    containers[element] = ownContainer.ToString();
            foreach (var camera in state.Room.game.cameras.Where(c => c.room == state.Room))
                foreach (var leaser in camera.spriteLeasers)
                    if (leaser.drawableObject is BackgroundScene.BackgroundSceneElement element && ReferenceEquals(element.scene, state.Scene) && leaser.sprites != null)
                    {
                        string container = containers.TryGetValue(element, out string ownContainer) ? ownContainer : value.ToString();
                        var target = camera.ReturnFContainer(container);
                        foreach (var sprite in leaser.sprites) { sprite.RemoveFromContainer(); target.AddChild(sprite); }
                    }
        }
        if (BackgroundDataAccess.Field(state.SceneData.GetType(), "NeedColorUpdate") is FieldInfo flag) flag.SetValue(state.SceneData, true);
        if (name == "windDir" && value is float wind) Shader.SetGlobalFloat(RainWorld.ShadPropWindDir, wind);
        if (name is "daySky" or "duskSky" or "nightSky" && value is string image && nativeField?.GetValue(state.Scene) is BackgroundScene.Simple2DBackgroundIllustration sky)
        {
            if (!File.Exists(AssetManager.ResolveFilePath("illustrations/" + image + ".png"))) throw new FileNotFoundException("Background image not found: " + image);
            state.Scene.LoadGraphic(image, true, true); sky.illustrationName = image;
            foreach (var camera in state.Room.game.cameras)
                foreach (var leaser in camera.spriteLeasers)
                    if (ReferenceEquals(leaser.drawableObject, sky) && leaser.sprites?.Length > 0) leaser.sprites[0].element = Futile.atlasManager.GetElementWithName(image);
        }
    }
    internal static void ValidateField(string name, object value)
    {
        if (value is not float && value is not int) return;
        double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        if (name is "cloudsCount" or "distantCloudsCount" or "rubbleCount" && (number < 0 || number > 128 || number != Math.Truncate(number)))
            throw new ArgumentOutOfRangeException(name, "Layer count must be an integer from 0 to 128.");
        if (name is "gridLayers" or "gridParallelDepth" or "gridPerpendicularDepth" && (number < 0 || number > 64))
            throw new ArgumentOutOfRangeException(name, "Grid dimensions must be from 0 to 64.");
        if (name is "depth" or "fromDepth" or "toDepth" or "sceneScale" or "depthScale" or "fogDepth" or "cloudsStartDepth" or "cloudsEndDepth" or "distantCloudsEndDepth" && number <= 0)
            throw new ArgumentOutOfRangeException(name, "Depth and scale must be positive.");
    }
    private static void FlushNativeWork(State state)
    {
        if (state.Scene == null) return;
        float Value(string name, float fallback)
        {
            var parameter = BackgroundDataAccess.Parameters(state.SceneData).FirstOrDefault(p => p.Name == name);
            object value = parameter == null ? null : BackgroundDataAccess.Effective(state.SceneData, parameter);
            return value == null ? fallback : Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        void Remove(BackgroundScene.BackgroundSceneElement element)
        { element.Destroy(); state.Scene.elements.Remove(element); state.Room.RemoveObject(element); }
        void Add(BackgroundScene.BackgroundSceneElement element)
        { state.Scene.AddElement(element); if (state.Scene.elementsAddedToRoom) state.Room.AddObject(element); }
        var random = UnityEngine.Random.state;
        try
        {
            if (state.CloudsDirty && state.Scene is AboveCloudsView clouds)
            {
                foreach (var cloud in clouds.elements.OfType<AboveCloudsView.Cloud>().Where(c => c is not AboveCloudsView.FlyingCloud).ToArray()) Remove(cloud);
                clouds.clouds = new List<AboveCloudsView.Cloud>();
                int close = (int)Value("cloudsCount", 7), distant = (int)Value("distantCloudsCount", 11);
                for (int i = 0; i < close; i++) Add(new AboveCloudsView.CloseCloud(clouds, Vector2.zero, (float)i / Math.Max(1, close - 1), i));
                for (int i = 0; i < distant; i++)
                {
                    float fraction = Mathf.Pow((float)i / Math.Max(1, distant - 1), Value("curveCloudDepth", 1));
                    Add(new AboveCloudsView.DistantCloud(clouds, new Vector2(0, Mathf.Lerp(Value("overrideYStart", -40 * clouds.cloudsEndDepth), Value("overrideYEnd", 0), fraction)), fraction, i));
                }
            }
            if (state.RubbleDirty && state.Scene is RoofTopView rooftop)
            {
                foreach (var rubble in rooftop.elements.OfType<RoofTopView.Rubble>().ToArray()) Remove(rubble);
                int count = (int)Value("rubbleCount", 16);
                for (int i = 0; i < count; i++)
                {
                    float fraction = Mathf.Pow((float)i / Math.Max(1, count - 1), Value("curveRubbleDepth", 1.5f));
                    Add(new RoofTopView.Rubble(rooftop, "Rf_Rubble", new Vector2(0, rooftop.floorLevel), Mathf.Lerp(Value("rubbleStartDepth", 1.5f), Value("rubbleEndDepth", 8), fraction), i));
                }
            }
            if (state.GridDirty && state.Scene is RotWormScene gridScene)
            {
                int layers = (int)Value("gridLayers", 8), parallel = (int)Value("gridParallelDepth", 24), perpendicular = (int)Value("gridPerpendicularDepth", 2);
                int total = layers * (parallel + perpendicular);
                if (total > 2048) throw new InvalidOperationException("Grid exceeds the 2048-layer editing budget.");
                UnityEngine.Random.InitState((int)Value("seed", 123));
                RotWormScene.sceneScale = Value("sceneScale", RotWormScene.sceneScale);
                RotWormScene.depthScale = Value("depthScale", RotWormScene.depthScale);
                RotWormScene.fogDepth = Value("fogDepth", RotWormScene.fogDepth);
                var generated = BackgroundDataAccess.Get(state.SceneData, "theGrid") as IList;
                var old = new HashSet<BackgroundScene.BackgroundSceneElement>(generated == null ? Enumerable.Empty<BackgroundScene.BackgroundSceneElement>() : generated.Cast<BackgroundScene.BackgroundSceneElement>());
                foreach (var element in old) Remove(element);
                generated?.Clear();
                for (int i = 0; i < layers; i++)
                    for (int j = 0; j < parallel + perpendicular; j++)
                    {
                        float depth = Math.Max(.001f, (float)(i * (parallel + perpendicular) + j) / Math.Max(1, total) * RotWormScene.fogDepth);
                        var grid = new RotWormScene.PebbsGrid(gridScene, 0, 0, depth, 1, j > parallel);
                        Add(grid); generated?.Add(grid);
                    }
            }
            if (state.PositionsDirty && BackgroundDataAccess.Get(state.SceneData, "backgroundElements") is IEnumerable roots)
                foreach (object record in roots) ReplaceTree(state, record);
        }
        finally { UnityEngine.Random.state = random; state.CloudsDirty = state.RubbleDirty = state.GridDirty = state.PositionsDirty = false; }
    }
    private static void Load(State state, string name)
    {
        var chain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = new List<(string Name, BackgroundDocument Document)>();
        void Read(string item)
        {
            item = BackgroundDocument.FileName(item);
            if (!chain.Add(item) || chain.Count > 32) throw new InvalidDataException("Background parent cycle or excessive inheritance depth.");
            string path = AssetManager.ResolveFilePath("assets/regionkit/backgrounds/" + item + ".txt");
            if (!File.Exists(path)) throw new FileNotFoundException("Background file not found.", path);
            if (new FileInfo(path).Length > 8 * 1024 * 1024) throw new InvalidDataException("Background file exceeds the 8 MB editing budget.");
            var doc = BackgroundDocument.Parse(File.ReadAllText(path));
            string campaign = state.Room.game.GetStorySession?.saveStateNumber?.value ?? "White";
            string parent = doc.Lines.LastOrDefault(l => l.Key == "Parent" && BackgroundDocument.Applies(l.Prefix, campaign))?.Value ?? "";
            if (parent.Length > 0) Read(parent);
            documents.Add((item, doc));
        }
        Read(name);
        string campaign = state.Room.game.GetStorySession?.saveStateNumber?.value ?? "White";
        string type = documents.SelectMany(d => d.Document.Lines).Where(l => l.Key == "Type" && BackgroundDocument.Applies(l.Prefix, campaign)).Select(l => l.Value).LastOrDefault() ?? "None";
        // Game constructors provide vanilla rendering and an ordinary initialized data record.
        // File parsing and edits below are independent of RegionKit's loaders/factories.
        if (state.Root != null) { BackgroundDataAccess.Set(state.Root, "backgroundName", ""); BackgroundDataAccess.Set(state.Root, "parent", null); }
        state.Scene = BackgroundSceneAdapter.Switch(state.Room, type); Observe(state);
        if (state.Root == null || state.SceneData == null) throw new InvalidOperationException("RegionKit background data is not initialized for this room.");
        var roots = BackgroundDataAccess.Get(state.SceneData, "backgroundElements") as IList ?? throw new InvalidOperationException("Background data has no element list.");
        foreach (object element in roots) RetireTree(state, element);
        roots.Clear();
        var knownParameters = BackgroundDataAccess.Parameters(state.SceneData).ToDictionary(p => p.Name, StringComparer.Ordinal);
        var groups = new Stack<object>();
        foreach (var source in documents)
        {
            groups.Clear();
            foreach (var line in source.Document.Lines.Where(l => l.Key.Length > 0))
            {
                // Conditional source lines are retained on disk; apply only the active campaign.
                if (!BackgroundDocument.Applies(line.Prefix, campaign)) continue;
                if (knownParameters.TryGetValue(line.Key, out var parameter))
                {
                    object value = BackgroundDataAccess.Parse(line.Value, parameter.Field.FieldType); ValidateField(parameter.Name, value);
                    parameter.Field.SetValue(state.SceneData, value); continue;
                }
                if (line.Key is "OffsetX" or "OffsetY")
                {
                    Vector2 offset = BackgroundDataAccess.Get(state.Root, "_backgroundOffset") is Vector2 old ? old : Vector2.zero;
                    if (line.Key == "OffsetX") offset.x = BackgroundDocument.Number(line.Value); else offset.y = BackgroundDocument.Number(line.Value);
                    BackgroundDataAccess.Set(state.Root, "_backgroundOffset", offset); continue;
                }
                bool remove = line.Key.StartsWith("REMOVE_", StringComparison.Ordinal);
                string key = remove ? line.Key.Substring(7) : line.Key;
                var schema = BackgroundElementSchema.Find(key, type);
                if (schema == null)
                {
                    if (line.Key is "Type" or "Parent") continue;
                    throw new NotSupportedException("Unknown background directive; source preserved: " + line.Key);
                }
                BackgroundRecordCodec.Apply(line, schema, roots, groups);
            }
        }
        foreach (object record in roots) ReplaceTree(state, record);
        BackgroundDataAccess.Set(state.Root, "backgroundName", name);
        // Snapshot export is intentionally independent of the read-only source templates.
        BackgroundDataAccess.Set(state.Root, "parent", null);
        BackgroundDataAccess.Set(state.SceneData, "parent", null);
        foreach (var parameter in knownParameters.Values) ApplySceneParameter(state, new Binding { Target = state.SceneData, Field = parameter.Field, Parameter = parameter });
        state.Status = "Loaded " + name + " (source file preserved)";
    }
    internal static bool Save(EditorSession session, string name = null)
    {
        if (session?.RoomSettings == null || !states.TryGetValue(session.RoomSettings, out State state)) return false;
        try
        {
            state.Room = session.Room; Observe(state);
            if (state.Root == null)
            { WorldAuthoringPathResolver.SaveRoomSettings(session.RoomSettings); state.SavedText = Fingerprint(state); state.Error = ""; Rebuild(state); return true; }
            name = BackgroundDocument.FileName(string.IsNullOrWhiteSpace(name) ?
                state.OutputPath != null ? Path.GetFileNameWithoutExtension(state.OutputPath) : current.Name + "-rainwright" : name);
            string relative = "assets/regionkit/backgrounds/" + name + ".txt";
            string root = WorldModificationWorkspace.AuthorRoot(relative, session.World?.name);
            if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string target) ||
                !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative)) throw new IOException("Invalid background author path.");
            string text = Export(state).Serialize();
            var visited = new HashSet<object>();
            for (object parent = BackgroundDataAccess.Get(state.Root, "parent"); parent != null; parent = BackgroundDataAccess.Get(parent, "parent"))
            {
                if (!visited.Add(parent)) throw new IOException("Background parent cycle detected; save was canceled.");
                if (name.Equals(BackgroundDataAccess.Get(parent, "backgroundName") as string, StringComparison.OrdinalIgnoreCase)) throw new IOException("The output name would create a parent cycle.");
            }
            string resolved = AssetManager.ResolveFilePath(relative);
            if (File.Exists(resolved) && !Path.GetFullPath(resolved).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Another active Mod already provides this background name. Choose a unique name.");
            if (File.Exists(target) && (state.OutputPath != target || File.ReadAllText(target) != state.ExpectedOutput))
                throw new IOException("A different or externally edited file already uses this name. Choose a new name; its contents were preserved.");
            WorldAuthoringPathResolver.AtomicWriteAllText(target, text);
            state.OutputPath = target; state.ExpectedOutput = text;
            BackgroundDataAccess.Set(state.Root, "backgroundName", name);
            WorldAuthoringPathResolver.SaveRoomSettings(session.RoomSettings);
            state.SavedText = Fingerprint(state); state.Status = "Saved " + target; state.Error = "";
            RefreshFiles(state); Rebuild(state); return true;
        }
        catch (Exception error) { state.Error = error.Message; EditorDiagnostics.Warning("Background save failed: " + error); Rebuild(state); return false; }
    }
    internal static bool HasPendingChanges(EditorSession session) => session?.RoomSettings != null && states.TryGetValue(session.RoomSettings, out State state) && state.Dirty;
    internal static bool CaptureRecovery(EditorSession session, string document)
    {
        if (!HasPendingChanges(session) || !states.TryGetValue(session.RoomSettings, out State state) || state.Root == null) return false;
        string oldName = BackgroundDataAccess.Get(state.Root, "backgroundName") as string ?? "";
        string name = state.OutputPath == null ? (oldName.Length == 0 ? "background" : oldName) + "-rainwright" : Path.GetFileNameWithoutExtension(state.OutputPath);
        string relative = "assets/regionkit/backgrounds/" + BackgroundDocument.FileName(name) + ".txt";
        string root = WorldModificationWorkspace.AuthorRoot(relative, session.World?.name);
        if (!global::RainWright.IO.AuthoringSourcePaths.TryCombineUnderModRoot(root, relative, out string path) ||
            !global::RainWright.IO.AuthoringSourcePaths.IsUnredirectedModAuthorPath(root, relative)) throw new IOException("Invalid background recovery author path.");
        if (File.Exists(path) && (state.OutputPath != path || File.ReadAllText(path) != state.ExpectedOutput)) throw new IOException("Background recovery destination was edited externally; its contents were preserved.");
        try
        {
            // Keep the paired room snapshot pointing at the exact recovered background file.
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            BackgroundDataAccess.Set(state.Root, "backgroundName", name);
            AutoSave.RecoverySnapshotStore.CaptureRoom(document, session.RoomSettings);
            AutoSave.RecoverySnapshotStore.CaptureText(document + "/Background", path, Export(state).Serialize());
        }
        finally { BackgroundDataAccess.Set(state.Root, "backgroundName", oldName); }
        return true;
    }
    private sealed class ElementIdentity { internal int Id; }
    private static int nextElementIdentity;
    private static readonly ConditionalWeakTable<object, ElementIdentity> elementIdentities = new();

    private static void Rebuild(State state)
    {
        state.Revision++; state.Bindings.Clear(); state.Elements.Clear();
        if (state.Failed)
        {
            current = new BackgroundEditorSnapshot { Available = true, Failed = true, Context = state.Id, Revision = state.Revision, Error = state.Error };
            return;
        }
        int next = 0;
        BackgroundFieldSnapshot Bind(object target, FieldInfo field, string label, object element = null, BackgroundDataAccess.Parameter parameter = null)
        {
            int id = ++next; var type = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
            object value = parameter == null ? field.GetValue(target) : BackgroundDataAccess.Effective(target, parameter);
            state.Bindings.Add(id, new Binding { Target = target, Field = field, Label = label, Element = element, Parameter = parameter });
            if (value == null && parameter != null && state.Scene != null)
            {
                object native = BackgroundDataAccess.Get(state.Scene, parameter.Name);
                if (native is BackgroundScene.Simple2DBackgroundIllustration illustration) native = illustration.illustrationName;
                if (native != null && type.IsInstanceOfType(native)) value = native;
            }
            return new BackgroundFieldSnapshot { Id = id, Key = parameter?.Name ?? field.Name, Name = label, Value = BackgroundDataAccess.Text(value), Kind = type.Name,
                Inherited = parameter != null && field.GetValue(target) == null,
                CanInherit = parameter != null && (!field.FieldType.IsValueType || Nullable.GetUnderlyingType(field.FieldType) != null),
                Choices = type.IsEnum ? Enum.GetNames(type) : Array.Empty<string>() };
        }
        var settings = new List<BackgroundFieldSnapshot>();
        if (state.Root != null)
        {
            settings.Add(Bind(state.Root, BackgroundDataAccess.Field(state.Root.GetType(), "roomOffset"), "房间偏移 / Room offset"));
            settings.Add(Bind(state.Root, BackgroundDataAccess.Field(state.Root.GetType(), "_backgroundOffset"), "局部背景偏移 / Local background offset"));
            foreach (var parameter in BackgroundDataAccess.Parameters(state.SceneData)) settings.Add(Bind(state.SceneData, parameter.Field, parameter.Name, parameter: parameter));
        }
        var elements = new List<BackgroundElementSnapshot>();
        var elementIds = new Dictionary<object, int>();
        foreach (object element in BackgroundDataAccess.Elements(state.SceneData))
        {
            int id = elementIdentities.GetValue(element, _ => new ElementIdentity { Id = Interlocked.Increment(ref nextElementIdentity) }).Id; elementIds[element] = id; state.Elements[id] = element;
            var schema = BackgroundDataAccess.Schema(element);
            var edits = new List<BackgroundFieldSnapshot>();
            if (schema != null)
            {
                foreach (string fieldName in schema.Fields.Select(f => f.Split('.')[0]).Distinct())
                    if (BackgroundDataAccess.Field(element.GetType(), fieldName) is FieldInfo field) edits.Add(Bind(element, field, fieldName, element));
                foreach (string name in new[] { "anchorPos", "spriteScale", "spriteAlpha", "spriteColor", "spriteShader", "container", "lockX", "lockY", "spriteName" })
                    if (BackgroundDataAccess.Field(element.GetType(), name) is FieldInfo field) edits.Add(Bind(element, field, name, element));
            }
            object parent = BackgroundDataAccess.Get(element, "group");
            elements.Add(new BackgroundElementSnapshot { Id = id, ParentId = parent != null && elementIds.TryGetValue(parent, out int parentId) ? parentId : 0,
                IsGroup = BackgroundDataAccess.Get(element, "elements") is IList,
                Name = (schema?.Name ?? element.GetType().Name) + "  " + (BackgroundDataAccess.Get(element, "assetName") as string ?? BackgroundDataAccess.Get(element, "name") as string ?? ""), Fields = edits.ToArray() });
        }
        string serialized = "";
        try { serialized = Fingerprint(state); }
        catch (Exception error) { state.Error = error.Message; }
        state.Dirty = serialized != state.SavedText;
        current = new BackgroundEditorSnapshot { Available = true, Context = state.Id, Revision = state.Revision, HasRegionKit = state.Root != null,
            Scene = BackgroundSceneAdapter.Name(state.Scene), Name = string.IsNullOrWhiteSpace(BackgroundDataAccess.Get(state.Root, "backgroundName") as string) ? "background" : (string)BackgroundDataAccess.Get(state.Root, "backgroundName"),
            Parent = BackgroundDataAccess.Get(BackgroundDataAccess.Get(state.Root, "parent"), "backgroundName") as string ?? "",
            Settings = settings.ToArray(), Elements = elements.ToArray(), Files = state.Files,
            Illustrations = state.Illustrations, Sprites = state.Sprites, Shaders = state.Shaders,
            Error = state.Error, Status = state.Status,
            Dirty = state.Dirty, AddTypes = BackgroundElementSchema.All.Where(s => s.Allowed(BackgroundSceneAdapter.Name(state.Scene))).Select(s => s.Name).Distinct().ToArray() };
    }
    internal static void ResetView()
    { active = null; wasActive = false; current = BackgroundEditorSnapshot.Empty; while (commands.TryDequeue(out _)) Interlocked.Decrement(ref pending); }
    internal static void Shutdown() { ResetView(); BackgroundAppearanceRuntime.Disable(); }
}
