using System;
using System.Collections;
using System.IO;
using System.Runtime.Serialization;
using UnityEngine;

public sealed partial class PlaneProbe
{
    private void CacheRetention()
    {
        Type Map(string name) => front.GetType("DryCycle.DevUI.DevTool.Map." + name, true);
        object Make(Type type) => Activator.CreateInstance(type, true);
        object Bare(Type type) => FormatterServices.GetUninitializedObject(type);
        object Static(Type type, string member) => type.GetField(member, F).GetValue(null);
        void Put(Type type, string member, object value) => type.GetField(member, F).SetValue(null, value);
        object Invoke(Type type, string method, params object[] args) => type.GetMethod(method, F).Invoke(null, args);

        var sessionType = front.GetType("DryCycle.DevUI.DevTool.Core.EditorSession", true);
        var ownerType = sessionType.GetProperty("Owner", F).PropertyType;
        var worldType = sessionType.GetProperty("World", F).PropertyType;
        var session = Bare(sessionType); var owner = Bare(ownerType);
        var game = Bare(ownerType.GetField("game", F).FieldType);
        var overWorld = Bare(game.GetType().GetField("overWorld", F).FieldType);
        var world = Bare(worldType); Set(world, "name", "CACHE_TEST");
        Set(overWorld, "activeWorld", world); Set(game, "overWorld", overWorld);
        Set(owner, "game", game); Set(session, "Owner", owner);
        Set(session, "ToolMode", Enum.Parse(sessionType.GetProperty("ToolMode", F).PropertyType, "Room"));

        var geometry = Map("MapRoomGeometryPresentationHub");
        var shortcuts = T("WorldMapShortcutPresentation"); var exact = T("WorldMapExactShortcuts");
        var throttle = T("WorldMapUpdateThrottle"); var throttleEnabled = Static(throttle, "enabled");
        Put(throttle, "enabled", false);
        Invoke(geometry, "Clear"); Invoke(shortcuts, "Clear"); Invoke(exact, "ClearForNoMap");
        var entry = Make(geometry.GetNestedType("CacheEntry", F));
        var visual = Make(Map("EditorMapRoomVisualSnapshot"));
        var geometryEntries = (IDictionary)Static(geometry, "cache");
        var geometryPublished = (IDictionary)Static(geometry, "published");
        geometryEntries[17] = entry; geometryPublished[17] = visual;
        Put(geometry, "persistentWorld", world);
        var shortcutEntry = Make(shortcuts.GetNestedType("CacheEntry", F));
        ((IDictionary)Static(shortcuts, "cache"))[17] = shortcutEntry;
        shortcuts.GetField("sourceWorld", F)?.SetValue(null, world);
        var exactEntry = Make(exact.GetNestedType("Entry", F));
        ((IDictionary)Static(exact, "entries"))[17] = exactEntry;
        Put(exact, "sourceWorld", world);
        var generation = Static(geometry, "publishedGeneration");
        try
        {
            for (int i = 0; i < 30; i++)
            {
                Invoke(geometry, "Prime", session);
                Check(ReferenceEquals(geometryEntries[17], entry) && ReferenceEquals(geometryPublished[17], visual) &&
                    Equals(generation, Static(geometry, "publishedGeneration")), "Leaving Map retains exact geometry objects and generation: " + i);
                Invoke(shortcuts, "Prime", session, -1);
                Put(exact, "enabled", true); Invoke(exact, "UpdateMainThread", session, -1);
                Put(exact, "lastUpdateFrame", -1); Invoke(exact, "UpdateExactCache", session, -1);
                Check(ReferenceEquals(((IDictionary)Static(shortcuts, "cache"))[17], shortcutEntry) &&
                    ReferenceEquals(((IDictionary)Static(exact, "entries"))[17], exactEntry), "Leaving Map retains shortcut caches: " + i);
            }

            var source = Make(Map("WorldMapRoomSource"));
            var texture = new Texture2D(4, 3, TextureFormat.RGBA32, false);
            Set(entry, "DecodedSource", source); Set(entry, "UploadedSource", source);
            Set(entry, "OwnedSourceTexture", texture); Set(entry, "RasterInitialized", true);
            Set(entry, "Snapshot", visual);
            var repType = entry.GetType().GetField("RoomRep", F).FieldType;
            int revision = (int)Get(entry, "Revision");
            for (int i = 0; i < 50; i++)
            {
                var rep = Bare(repType); Set(entry, "RoomRep", rep);
                Check((bool)Invoke(geometry, "TryRebindSourceTexture", entry) && ReferenceEquals(Get(rep, "texture"), texture) &&
                    (int)Get(entry, "Revision") == revision && ReferenceEquals(Get(entry, "Snapshot"), visual),
                    "Replacement page reuses the same GPU texture and geometry without upload/rebuild: " + i);
            }
            var pendingRep = Bare(repType); Set(entry, "RoomRep", pendingRep); Set(entry, "RasterInitialized", false);
            Check(!(bool)Invoke(geometry, "TryRebindSourceTexture", entry) && Get(pendingRep, "texture") == null,
                "An invalidated source is never rebound as a valid cached texture.");
            Set(entry, "RasterInitialized", true); UnityEngine.Object.DestroyImmediate(texture);
            Check(!(bool)Invoke(geometry, "TryRebindSourceTexture", entry), "A destroyed Unity texture is rejected for CPU-backed recovery.");

            var hub = Map("MapEditorPresentationHub");
            Put(hub, "retainedValid", true); Put(hub, "observedWorld", world);
            var snapshot = Make(Map("EditorMapPresentationSnapshot")); Set(snapshot, "Available", true);
            hub.GetProperty("current", F).SetValue(null, snapshot);
            Invoke(hub, "Publish", session);
            Check(ReferenceEquals(hub.GetProperty("Current", F).GetValue(null), snapshot), "Room mode retains the same detached map snapshot.");
            Check((bool)Invoke(hub, "CanRetainFor", session), "A shell-only frame can retain map data for its live world.");
            Set(overWorld, "activeWorld", Bare(worldType));
            Invoke(geometry, "Prime", session); Invoke(shortcuts, "Prime", session, -1);
            Put(exact, "lastUpdateFrame", -1); Invoke(exact, "UpdateExactCache", session, -1);
            Check(geometryEntries.Count == 0 && ((IDictionary)Static(shortcuts, "cache")).Count == 0 &&
                ((IDictionary)Static(exact, "entries")).Count == 0 && !(bool)Invoke(hub, "CanRetainFor", session),
                "A different world invalidates caches even if a page is temporarily absent.");
            Invoke(hub, "Clear"); Set(overWorld, "activeWorld", world);

            PlayerPageCache(Map, Make, Bare, world, sessionType);
        }
        finally
        {
            Invoke(geometry, "Clear"); Invoke(shortcuts, "Clear"); Invoke(exact, "ClearForNoMap");
            Put(throttle, "enabled", throttleEnabled);
        }
        results.Add("PASS " + assertions + " checks including map page cache suspension, texture reuse and source invalidation.");
    }

    private void PlayerPageCache(Func<string, Type> map, Func<Type, object> make, Func<Type, object> bare, object world, Type sessionType)
    {
        var runtime = map("PlayerMap.PlayerMapWorkspaceRuntime");
        var pageType = map("PlayerMap.PlayerMapSessionState").GetField("Page", F).FieldType;
        var config = Path.Combine(output, "isolated-map-cache-test.txt"); File.WriteAllText(config, "");
        object Page()
        {
            var p = bare(pageType); Set(p, "world", world); Set(p, "filePath", config);
            Set(p, "subNodes", make(pageType.GetField("subNodes", F).FieldType)); return p;
        }
        var stampType = map("MapViewFileStamp");
        var state = runtime.GetMethod("GetOrCreateState", F).Invoke(null, new[] { bare(sessionType), Page() });
        Set(state, "Page", Page()); Set(state, "Region", "CACHE_TEST"); Set(state, "Initialized", true);
        Set(state, "ConfigSource", stampType.GetMethod("Capture", F).Invoke(null, new object[] { config }));
        var preview = make(map("PlayerMap.PlayerMapRenderPreview")); Set(preview, "Available", true); Set(state, "Preview", preview);
        for (int i = 0; i < 25; i++)
        {
            var page = Page();
            var nextState = runtime.GetMethod("GetOrCreateState", F).Invoke(null, new[] { bare(sessionType), page });
            Check(ReferenceEquals(nextState, state), "Room changes retain the Player Map cache across replacement editor sessions: " + i);
            Check((bool)runtime.GetMethod("EnsureStateForPage", F).Invoke(null, new[] { page, state }) &&
                ReferenceEquals(Get(state, "Preview"), preview), "Clean replacement Player Map retains the generated preview: " + i);
        }
        File.WriteAllText(config, "// External edit changes the config source length.\n");
        Check((bool)runtime.GetMethod("EnsureStateForPage", F).Invoke(null, new[] { Page(), state }) &&
            !ReferenceEquals(Get(state, "Preview"), preview), "An external map config edit invalidates the old clean preview.");
        File.Delete(config);
    }
}
