using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Logging;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Map;
using DryCycle.DevUI.DevTool.Map.PlayerMap;
using DryCycle.DevUI.DevTool.Map.Cartography;
using ImGuiNET;
using UnityEngine;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Rebuilt Canon / Player Map workspace. The frontend consumes immutable PlayerMap snapshots and
/// submits commands; it never calls vanilla MiniMap, RoomPanel.Update, or MapRenderOutput.
/// </summary>
internal static class PlayerMapWorkspaceView
{
    private const float MinZoom = 0.12f;
    private const float MaxZoom = 5f;
    private static readonly bool[] LayerVisible = { true, true, true };
    private static Num.Vector2 pan;
    private static readonly bool depthEnabled = false;
    private static int depthFocus = -1;
    private static MapPlaneFrame planeFrame;
    private static string frameRegion;
    internal static MapPlaneFrame ProjectionFrame => planeFrame;
    internal static bool DepthEnabled => depthEnabled;
    internal static Num.Vector2 ProjectPosition(Vector2 position, int layer) =>
        MapDepthProjection.Project(PlayerMapCanvasCoordinates.Project(position.x, position.y), layer, depthEnabled, planeFrame);
    private static float zoom = 1f;
    private static bool fitRequested = true;
    private static bool showConnections = true;
    private static bool showRawOutput;
    private static int draggingRoom = -1;
    private static bool roomDragMoved;
    private static PlayerMapRoomSnapshot[] indexedRooms;
    private static readonly Dictionary<int, PlayerMapRoomSnapshot> roomsByIndex = new();
    private static Num.Vector2 dragStartMouse;
    private static Vector2 dragStartPosition;
    private static Num.Vector2 dragStartPointer;
    private static Vector2 dragPreviewPosition;
    private static int inspectorRoom = -1;
    private static Num.Vector2 inspectorEffective;
    private static Num.Vector2 inspectorOffset;
    private static int selectedDefMaterial = -1;
    private static Num.Vector2 defA;
    private static Num.Vector2 defB;

    internal static Num.Vector2 Pan => pan;
    internal static float Zoom => zoom;
    internal static bool[] LayerVisibility => LayerVisible;
    internal static int SelectedDefMaterial
    {
        get => selectedDefMaterial;
        set => selectedDefMaterial = value;
    }
    internal static int DraggingRoom => draggingRoom;
    internal static Vector2 DragPreviewPosition => dragPreviewPosition;

    internal static void ResetRetainedState()
    {
        planeFrame = default;
        frameRegion = null;
        pan = default;
        zoom = 1f;
        fitRequested = true;
        showConnections = true;
        showRawOutput = false;
        LayerVisible[0] = true;
        LayerVisible[1] = true;
        LayerVisible[2] = true;
        draggingRoom = -1;
        roomDragMoved = false; indexedRooms = null; roomsByIndex.Clear();
        inspectorRoom = -1;
        selectedDefMaterial = -1;
        inspectorEffective = default;
        inspectorOffset = default;
        defA = default;
        defB = default;
    }

    private static int interactionPresentationRevision = -1;
    internal static void SynchronizePresentationInteraction()
    {
        int revision = EditorUiModeState.PresentationRevision;
        if (interactionPresentationRevision == revision) return;
        interactionPresentationRevision = revision;
        draggingRoom = -1;
        roomDragMoved = false;
        PlayerMapMultiSelection.CancelInteraction();
        PlayerMapCanvasAuthoring.CancelInteraction();
    }

    internal static void DrawBody(EditorPresentationSnapshot editor, EditorMapPresentationSnapshot worldSnapshot)
    {
        SynchronizePresentationInteraction();
        PlayerMapPresentationSnapshot snapshot =
            PlayerMapWorkspaceRuntime.GetPresentation(DevToolRuntime.ActiveSession);
        if (snapshot?.Available != true)
        {
            DevToolWidgets.MutedText(
                DevToolUiSettings.T("玩家地图工作区正在初始化。", "Player Map workspace is initializing."),
                true);
            return;
        }

        if(!string.Equals(frameRegion,snapshot.RegionName,StringComparison.OrdinalIgnoreCase))
        { frameRegion=snapshot.RegionName; fitRequested=true; planeFrame=default; }
        DrawToolbar(snapshot);
        ImGui.Separator();

        Num.Vector2 available = ImGui.GetContentRegionAvail();
        float left = Math.Max(280f, Math.Min(340f, available.X * 0.25f));
        float right = Math.Max(270f, Math.Min(355f, available.X * 0.30f));
        float center = Math.Max(260f, available.X - left - right - 16f);

        if (ImGui.BeginChild("##PlayerMapExplorer", new Num.Vector2(left, available.Y), ImGuiChildFlags.Borders))
        {
            // Player Map deliberately reuses the World Map browser instead of maintaining a second
            // room list/search/selection implementation. Both workspaces now share subregion groups,
            // issues, search state, compact region summary and room selection semantics.
            WorldWorkspaceView.DrawSharedExplorer(worldSnapshot);
            ScopedScrollChrome.Draw("PlayerMapExplorer");
        }
        ImGui.EndChild();
        ImGui.SameLine(0f, 8f);

        if (ImGui.BeginChild("##PlayerMapCanvas", new Num.Vector2(center, available.Y), ImGuiChildFlags.Borders))
            DrawCanvas(snapshot, worldSnapshot);
        ImGui.EndChild();
        ImGui.SameLine(0f, 8f);

        if (ImGui.BeginChild("##PlayerMapInspector", new Num.Vector2(0f, available.Y), ImGuiChildFlags.Borders))
            DrawInspector(snapshot);
        ImGui.EndChild();
    }

    private static void DrawToolbar(PlayerMapPresentationSnapshot snapshot)
    {
        ImGui.TextUnformatted(DevToolUiSettings.T("玩家地图", "PLAYER MAP"));
        ImGui.SameLine();
        DevToolWidgets.MutedText(snapshot.RegionName + " | " + snapshot.Rooms.Length +
                                 DevToolUiSettings.T(" 个房间", " rooms"));
        ImGui.SameLine(0f, 16f);

        if (DevToolWidgets.ActionButton(DevToolUiSettings.T("适配", "Fit"), "PlayerMapFit", DevToolButtonTone.Subtle))
            fitRequested = true;
        ImGui.SameLine();
        if (DevToolWidgets.ActionButton("100%", "PlayerMap100", DevToolButtonTone.Subtle)) zoom = 1f;
        ImGui.SameLine(0f, 12f);

        for (int i = 0; i < 3; i++)
        {
            bool visible = LayerVisible[i];
            ImGui.PushStyleColor(ImGuiCol.Text, WorldMapLayerStyle.Color(i));
            if (ImGui.Checkbox(MapRoomLayer.Label(i) + "##PlayerMapLayer" + i, ref visible))
            {
                LayerVisible[i] = visible;
                fitRequested = true;
            }
            ImGui.PopStyleColor();
            if (i < 2) ImGui.SameLine();
        }

        ImGui.SameLine(0f, 12f);
        ImGui.Checkbox(DevToolUiSettings.T("连接##PlayerMapLinks", "Links##PlayerMapLinks"), ref showConnections);
        ImGui.SameLine(0f, 12f);
        bool raw = showRawOutput;
        if (ImGui.Checkbox(DevToolUiSettings.T("输出预览##PlayerMapRaw", "Output Preview##PlayerMapRaw"), ref raw))
            showRawOutput = raw;

        ImGui.SameLine(0f, 14f);
        if (DevToolWidgets.ActionButton(
                DevToolUiSettings.T("构建预览", "Build Preview"),
                "PlayerMapPreview",
                DevToolButtonTone.Normal))
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.BuildPreview));
        ImGui.SameLine();
        if (DevToolWidgets.ActionButton(
                "Render & Export",
                "PlayerMapExport",
                DevToolButtonTone.Primary))
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.RenderAndExport));

        PlayerMapLayoutAssist.DrawToolbar(snapshot);
        PlayerMapGroupLayerControls.DrawToolbar(snapshot);
        PlayerMapMigrationStreamView.DrawToolbar(snapshot);
        DrawHiddenRooms(snapshot);
    }

    private static void DrawHiddenRooms(PlayerMapPresentationSnapshot snapshot)
    {
        int count = 0;
        foreach (PlayerMapRoomSnapshot room in snapshot.Rooms) if (room.Hidden) count++;
        if (ImGui.Button(DevToolUiSettings.T("隐藏房间", "Hidden rooms") + " (" + count + ")##PlayerMapHidden"))
            ImGui.OpenPopup("PlayerMapHiddenRooms");
        if (!ImGui.BeginPopup("PlayerMapHiddenRooms")) return;
        if (count == 0) ImGui.TextDisabled(DevToolUiSettings.T("没有手动隐藏的房间", "No manually hidden rooms"));
        foreach (PlayerMapRoomSnapshot room in snapshot.Rooms)
        {
            if (!room.Hidden) continue;
            if (ImGui.Selectable(DevToolUiSettings.T("恢复显示 · ", "Show · ") + room.Name + "##Restore" + room.RoomIndex))
                PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetRoomHidden, roomIndex: room.RoomIndex, flag: false));
        }
        ImGui.EndPopup();
    }

    private static void DrawCanvas(PlayerMapPresentationSnapshot snapshot, EditorMapPresentationSnapshot worldSnapshot)
    {
        Num.Vector2 canvasMin = ImGui.GetCursorScreenPos();
        Num.Vector2 canvasSize = ImGui.GetContentRegionAvail();
        if (canvasSize.X < 80f || canvasSize.Y < 80f) return;
        ImGui.InvisibleButton("##PlayerMapCanvasInput", canvasSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        bool canvasActive = ImGui.IsItemActive();
        ImGuiIOPtr io = ImGui.GetIO();
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        Num.Vector2 canvasMax = canvasMin + canvasSize;
        draw.AddRectFilled(canvasMin, canvasMax, ImGui.GetColorU32(ImGuiCol.ChildBg));
        draw.AddRect(canvasMin, canvasMax, ImGui.GetColorU32(ImGuiCol.Border));

        if (showRawOutput && snapshot.Preview.Available)
        {
            DrawRawPreview(draw, canvasMin, canvasSize, snapshot.Preview);
            return;
        }

        if (fitRequested)
        {
            Fit(snapshot, canvasSize);
            fitRequested = false;
        }


        bool roomGesture = draggingRoom >= 0 || PlayerMapMultiSelection.GroupDragging || PlayerMapCanvasAuthoring.OwnsCanvas;
        if (hovered && Math.Abs(io.MouseWheel) > 0.0001f && !roomGesture)
        {
            float oldZoom = zoom;
            float next = Math.Max(MinZoom, Math.Min(MaxZoom, zoom * ((RainWrightPersonalSettings.ReverseZoom ? -io.MouseWheel : io.MouseWheel) > 0f ? 1.12f : 0.89f)));
            Num.Vector2 mouse = io.MousePos - canvasMin;
            Num.Vector2 worldAtMouse = (mouse - pan) / oldZoom;
            zoom = next;
            pan = mouse - worldAtMouse * zoom;
        }
        if (canvasActive && !roomGesture && io.MouseDelta != Num.Vector2.Zero &&
            (ImGui.IsMouseDragging(ImGuiMouseButton.Middle) || ImGui.IsMouseDragging(ImGuiMouseButton.Right)))
            pan += io.MouseDelta * RainWrightPersonalSettings.DragSensitivity;

        DrawGrid(draw, canvasMin, canvasSize);
        DrawDefMaterials(draw, snapshot, canvasMin);
        PlayerMapCanvasAuthoring.DrawCanvasOverlay(draw, snapshot, canvasMin);
        if (showConnections) DrawConnections(draw, snapshot, worldSnapshot, canvasMin);

        PlayerMapRoomSnapshot hoveredRoom = hovered ? HitRoom(snapshot, canvasMin, io.MousePos) : null;
        DrawRooms(draw, snapshot, canvasMin, hoveredRoom);
        HandleRoomInteraction(snapshot, hovered, hoveredRoom, canvasMin, io);
    }

    private static void DrawGrid(ImDrawListPtr draw, Num.Vector2 canvasMin, Num.Vector2 canvasSize)
    {
        float grid = 48f * zoom;
        if (grid < 14f) grid *= 4f;
        uint color = ImGui.GetColorU32(ImGuiCol.Border);
        float startX = PositiveModulo(pan.X, grid);
        float startY = PositiveModulo(pan.Y, grid);
        for (float x = startX; x < canvasSize.X; x += grid)
            draw.AddLine(canvasMin + new Num.Vector2(x, 0f), canvasMin + new Num.Vector2(x, canvasSize.Y), color);
        for (float y = startY; y < canvasSize.Y; y += grid)
            draw.AddLine(canvasMin + new Num.Vector2(0f, y), canvasMin + new Num.Vector2(canvasSize.X, y), color);
    }

    private static void DrawRooms(
        ImDrawListPtr draw,
        PlayerMapPresentationSnapshot snapshot,
        Num.Vector2 canvasMin,
        PlayerMapRoomSnapshot hovered)
    {
        for (int plane = 2; plane >= 0; plane--)
        {
            for (int i = 0; i < snapshot.Rooms.Length; i++)
            {
                PlayerMapRoomSnapshot room = snapshot.Rooms[i];
                if (room.Layer != plane) continue;
                if (room.Disabled || !LayerVisible[Math.Max(0, Math.Min(2, room.Layer))]) continue;
                GetRoomRect(room, canvasMin, out Num.Vector2 min, out Num.Vector2 max);
                bool active = room.Selected || ReferenceEquals(room, hovered);

                if (room.Bake.Status == RoomMapBakeStatus.Ready && room.Bake.Runs.Length > 0)
                {
                    float tile = PlayerMapCoordinateSystem.CanonPixelsPerTile;
                    var center = PlayerMapCanvasCoordinates.Project(DisplayPosition(room).x,DisplayPosition(room).y);
                    var nativeMin=center-new Num.Vector2(room.Bake.Width*tile,room.Bake.Height*tile)*.5f;
                    for(int r=0;r<room.Bake.Runs.Length;r++)
                    {
                        var run=room.Bake.Runs[r];
                        float row=room.Bake.Height-1f-run.Y;
                        var a=nativeMin+new Num.Vector2(run.X*tile,row*tile);
                        var b=a+new Num.Vector2(run.Length*tile,tile);
                        MapPlaneDrawing.Quad(draw,a,b,room.Layer,planeFrame,canvasMin,pan,zoom,PreviewColor(run.Kind,run.Water,room.Layer),true);
                    }
                }
                else
                {
                    DrawRoomQuad(draw,room,DisplayPosition(room),canvasMin,ImGui.GetColorU32(ImGuiCol.FrameBg),true);
                }

                uint outline = active ? ImGui.GetColorU32(ImGuiCol.HeaderActive) : WorldMapLayerStyle.Color(room.Layer);
                DrawRoomQuad(draw,room,DisplayPosition(room),canvasMin,outline,false,active?2.5f:1f);
                if ((max - min).X > 42f && (max - min).Y > 18f)
                    draw.AddText(RoomLabelPoint(room,canvasMin) + new Num.Vector2(4f, 3f), WorldMapLayerStyle.Color(room.Layer),
                        room.Name + " · L" + room.Layer);
            }

        }
        PlayerMapMultiSelection.DrawOverlay(draw, snapshot, canvasMin, hovered);
        PlayerMapMigrationStreamView.DrawOverlay(draw, snapshot, canvasMin, hovered);
    }

    private static void DrawConnections(
        ImDrawListPtr draw,
        PlayerMapPresentationSnapshot snapshot,
        EditorMapPresentationSnapshot worldSnapshot,
        Num.Vector2 canvasMin)
    {
        if (PlayerMapMultiPipeConnections.Draw(draw, snapshot, worldSnapshot, canvasMin))
            return;
        if (worldSnapshot?.Connections == null) return;
        uint color = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        for (int i = 0; i < worldSnapshot.Connections.Length; i++)
        {
            EditorMapConnectionSnapshot link = worldSnapshot.Connections[i];
            PlayerMapRoomSnapshot a = FindRoom(snapshot, link.FromRoomIndex);
            PlayerMapRoomSnapshot b = FindRoom(snapshot, link.ToRoomIndex);
            if (a == null || b == null || a.Disabled || b.Disabled ||
                !LayerVisible[Math.Max(0, Math.Min(2, a.Layer))] || !LayerVisible[Math.Max(0, Math.Min(2, b.Layer))]) continue;
            Vector2 positionA = DisplayPosition(a);
            Vector2 positionB = DisplayPosition(b);
            Num.Vector2 pa = canvasMin + pan + ProjectPosition(positionA, a.Layer) * zoom;
            Num.Vector2 pb = canvasMin + pan + ProjectPosition(positionB, b.Layer) * zoom;
            draw.AddLine(pa, pb, MapDepthProjection.Fade(color,Math.Max(MapDepthProjection.Opacity(a.Layer,true),MapDepthProjection.Opacity(b.Layer,true))), 1f);
        }
    }

    private static void DrawDefMaterials(ImDrawListPtr draw, PlayerMapPresentationSnapshot snapshot, Num.Vector2 canvasMin)
    {
        for (int i = 0; i < snapshot.DefaultMaterials.Length; i++)
        {
            PlayerMapDefMaterialSnapshot item = snapshot.DefaultMaterials[i];
            Num.Vector2 a = canvasMin + pan + PlayerMapCanvasCoordinates.Project(item.Left, item.Top) * zoom;
            Num.Vector2 b = canvasMin + pan + PlayerMapCanvasCoordinates.Project(item.Right, item.Bottom) * zoom;
            uint fill = item.Air ? 0x443366FFu : 0x44FF5533u;
            uint line = item.Id == selectedDefMaterial ? ImGui.GetColorU32(ImGuiCol.HeaderActive) : (item.Air ? 0xCC5577FFu : 0xCCFF7755u);
            draw.AddRectFilled(a, b, fill);
            draw.AddRect(a, b, line, 0f, ImDrawFlags.None, item.Id == selectedDefMaterial ? 2.5f : 1.5f);
        }
    }

    private static void HandleRoomInteraction(
        PlayerMapPresentationSnapshot snapshot,
        bool canvasHovered,
        PlayerMapRoomSnapshot hoveredRoom,
        Num.Vector2 canvasMin,
        ImGuiIOPtr io)
    {
        PlayerMapGroupLayerControls.HandleShortcuts(snapshot, canvasHovered, io);
        if (PlayerMapCanvasAuthoring.OwnsCanvas)
            return;
        if (PlayerMapMultiSelection.HandleInteraction(snapshot, canvasHovered, hoveredRoom, canvasMin, io))
            return;

        if (canvasHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && hoveredRoom != null)
        {
            MapEditorCommandQueue.Enqueue(new MapEditorCommand(MapEditorCommandKind.SelectRoom, roomIndex: hoveredRoom.RoomIndex));
            draggingRoom = hoveredRoom.RoomIndex;
            roomDragMoved = false;
            dragStartMouse = io.MousePos;
            dragStartPointer=MapDepthProjection.Unproject((io.MousePos-canvasMin-pan)/zoom,hoveredRoom.Layer,depthEnabled,planeFrame);
            dragStartPosition = hoveredRoom.EffectivePosition;
            dragPreviewPosition = dragStartPosition;
        }

        PlayerMapRoomSnapshot target = draggingRoom >= 0 ? FindRoom(snapshot, draggingRoom) : null;
        if (draggingRoom >= 0 && (target == null || ImGui.IsKeyPressed(ImGuiKey.Escape)))
        { draggingRoom = -1; roomDragMoved = false; return; }
        if (draggingRoom >= 0 && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            roomDragMoved |= Num.Vector2.DistanceSquared(io.MousePos, dragStartMouse) >= 9f;
            if (roomDragMoved && ImGui.IsMousePosValid() && MapDepthProjection.TryUnproject((io.MousePos-canvasMin-pan)/zoom, target.Layer,depthEnabled,planeFrame,out var dragPointer))
            {
                Vector2 position = dragStartPosition + ToUnity(PlayerMapCanvasCoordinates.Unproject(dragPointer-dragStartPointer));
                if (!float.IsNaN(position.x) && !float.IsInfinity(position.x) && !float.IsNaN(position.y) && !float.IsInfinity(position.y))
                    dragPreviewPosition = new Vector2(RainWrightPersonalSettings.Snap(position.x), RainWrightPersonalSettings.Snap(position.y));
            }
        }
        else if (draggingRoom >= 0)
        {
            if (roomDragMoved && (dragPreviewPosition - dragStartPosition).sqrMagnitude > .0001f)
                PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetEffectivePosition,
                    roomIndex: draggingRoom, value: dragPreviewPosition));
            draggingRoom = -1; roomDragMoved = false;
        }
    }

    private static void DrawInspector(PlayerMapPresentationSnapshot snapshot)
    {
        DevToolWidgets.PaneTitle(DevToolUiSettings.T("玩家地图检查器", "PLAYER MAP INSPECTOR"));
        PlayerMapRoomSnapshot room = FindRoom(snapshot, snapshot.SelectedRoomIndex);
        if (room != null)
            DrawRoomInspector(room);
        else
            DrawRegionInspector(snapshot);

        ImGui.Separator();
        DrawDefaultMaterialInspector(snapshot, room);
        PlayerMapCanvasAuthoring.DrawInspectorTools(snapshot, room);
        ImGui.Separator();
        PlayerMapRenderProgressView.Draw(snapshot);
        PlayerMapGroupPlacementControls.Draw(snapshot);
        PlayerMapGroupLayoutControls.Draw(snapshot);
        PlayerMapMigrationStreamView.DrawInspector(snapshot);
        PlayerMapPreflightPanel.Draw(snapshot);
    }

    private static void DrawRegionInspector(PlayerMapPresentationSnapshot snapshot)
    {
        ImGui.TextUnformatted(snapshot.RegionName);
        DevToolWidgets.MutedText(
            DevToolUiSettings.T(
                "Player Map 默认由 World Layout x1.5 派生；单房间拖动只记录 Offset，不会反向污染 World Layout。",
                "Player Map derives from World Layout x1.5 by default; room drags store offsets without mutating World Layout."),
            true);
        DrawMetric("Rooms", snapshot.Rooms.Length.ToString());
        DrawMetric("Def_Mat", snapshot.DefaultMaterials.Length.ToString());
        DrawMetric("State", snapshot.Dirty ? "Modified" : "Saved");
    }

    private static void DrawRoomInspector(PlayerMapRoomSnapshot room)
    {
        if (inspectorRoom != room.RoomIndex)
        {
            inspectorRoom = room.RoomIndex;
            inspectorEffective = ToNum(room.EffectivePosition);
            inspectorOffset = ToNum(room.Offset);
        }
        else if (!ImGui.IsAnyItemActive())
        {
            inspectorEffective = ToNum(room.EffectivePosition);
            inspectorOffset = ToNum(room.Offset);
        }

        ImGui.TextUnformatted(room.Name);
        ImGui.SameLine();
        ImGui.TextDisabled(MapRoomLayer.Label(room.Layer));
        ImGui.Separator();

        if (DevToolWidgets.ActionButton(
                room.Hidden ? DevToolUiSettings.T("恢复显示房间", "Show room") : DevToolUiSettings.T("隐藏房间", "Hide room"),
                "PlayerMapRoomVisibility", DevToolButtonTone.Subtle))
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetRoomHidden, roomIndex: room.RoomIndex, flag: !room.Hidden));
        if (room.Hidden)
            DevToolWidgets.MutedText(DevToolUiSettings.T("此房间及相关连接不参与预览和导出。", "This room and its connections are excluded from preview and export."), true);
        else if (room.Disabled)
            DevToolWidgets.MutedText(DevToolUiSettings.T("此房间在当前时间线中被游戏禁用。", "The game disables this room in the current timeline."), true);

        DevToolWidgets.SectionHeader(DevToolUiSettings.T("位置来源", "PLACEMENT"));
        bool derived = room.Mode == PlayerMapPlacementMode.Derived;
        if (DevToolWidgets.ActionButton(
                DevToolUiSettings.T("跟随 World Layout", "Follow World Layout"),
                "PlayerMapDerived",
                derived ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle))
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetPlacementMode, roomIndex: room.RoomIndex, integer: 0));
        ImGui.SameLine();
        if (DevToolWidgets.ActionButton(
                DevToolUiSettings.T("独立", "Independent"),
                "PlayerMapAbsolute",
                !derived ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle))
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetPlacementMode, roomIndex: room.RoomIndex, integer: 1));

        DrawVectorMetric(DevToolUiSettings.T("World 位置", "World position"), room.WorldPosition);
        DrawVectorMetric(DevToolUiSettings.T("派生基底", "Derived base"), room.DerivedBasePosition);

        Num.Vector2 effective = inspectorEffective;
        bool effectiveChanged = ImGui.InputFloat2(
            DevToolUiSettings.T("最终位置##PlayerMapEffective", "Final position##PlayerMapEffective"),
            ref effective,
            "%.1f");
        inspectorEffective = effective;
        if (ImGui.IsItemDeactivatedAfterEdit())
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(
                PlayerMapCommandKind.SetEffectivePosition,
                roomIndex: room.RoomIndex,
                value: ToUnity(effective)));
        else if (!effectiveChanged && !ImGui.IsItemActive()) inspectorEffective = ToNum(room.EffectivePosition);

        if (derived)
        {
            Num.Vector2 offset = inspectorOffset;
            bool offsetChanged = ImGui.InputFloat2(
                DevToolUiSettings.T("微调 Offset##PlayerMapOffset", "Fine offset##PlayerMapOffset"),
                ref offset,
                "%.1f");
            inspectorOffset = offset;
            if (ImGui.IsItemDeactivatedAfterEdit())
                PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(
                    PlayerMapCommandKind.SetOffset,
                    roomIndex: room.RoomIndex,
                    value: ToUnity(offset)));
            else if (!offsetChanged && !ImGui.IsItemActive()) inspectorOffset = ToNum(room.Offset);

            if (DevToolWidgets.ActionButton(
                    DevToolUiSettings.T("清零 Offset", "Reset Offset"),
                    "PlayerMapResetOffset",
                    DevToolButtonTone.Subtle))
                PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.ResetOffset, roomIndex: room.RoomIndex));
        }

        int layer = room.Layer;
        if (ImGui.BeginCombo(DevToolUiSettings.T("地图图层##PlayerMapLayerCombo", "Map layer##PlayerMapLayerCombo"), MapRoomLayer.Label(layer)))
        {
            for (int i = 0; i < 3; i++)
            {
                bool selected = i == layer;
                if (ImGui.Selectable(MapRoomLayer.Label(i), selected))
                    PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetLayer, roomIndex: room.RoomIndex, integer: i));
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        if (room.Bake.Status != RoomMapBakeStatus.Ready)
            DevToolWidgets.MutedText(room.Bake.Status == RoomMapBakeStatus.Pending
                ? DevToolUiSettings.T("正在载入房间预览……", "Loading room preview...")
                : DevToolUiSettings.T("房间预览不可用。", "Room preview is unavailable."));
        if (!string.IsNullOrWhiteSpace(room.Bake.Error))
            DevToolWidgets.MutedText(room.Bake.Error, true);
    }

    private static void DrawDefaultMaterialInspector(PlayerMapPresentationSnapshot snapshot, PlayerMapRoomSnapshot room)
    {
        DevToolWidgets.SectionHeader("DEF_MAT");
        if (DevToolWidgets.ActionButton(
                DevToolUiSettings.T("新建区域", "New Region"),
                "PlayerMapNewDef",
                DevToolButtonTone.Normal))
        {
            Vector2 center = room?.EffectivePosition ?? Vector2.zero;
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(
                PlayerMapCommandKind.CreateDefaultMaterial,
                value: center - new Vector2(45f, 30f),
                valueB: center + new Vector2(45f, 30f),
                flag: false));
        }

        for (int i = 0; i < snapshot.DefaultMaterials.Length; i++)
        {
            PlayerMapDefMaterialSnapshot item = snapshot.DefaultMaterials[i];
            bool selected = selectedDefMaterial == item.Id;
            ImGui.PushID(item.Id);
            if (ImGui.Selectable((item.Air ? "Air " : "Material ") + "#" + item.Id, selected))
            {
                selectedDefMaterial = item.Id;
                defA = ToNum(item.A);
                defB = ToNum(item.B);
            }
            ImGui.PopID();
        }

        PlayerMapDefMaterialSnapshot? current = FindDef(snapshot, selectedDefMaterial);
        if (!current.HasValue) return;
        PlayerMapDefMaterialSnapshot def = current.Value;
        if (!ImGui.IsAnyItemActive())
        {
            defA = ToNum(def.A);
            defB = ToNum(def.B);
        }

        Num.Vector2 a = defA;
        ImGui.InputFloat2("A##PlayerMapDefA", ref a, "%.1f");
        defA = a;
        bool commitA = ImGui.IsItemDeactivatedAfterEdit();
        Num.Vector2 b = defB;
        ImGui.InputFloat2("B##PlayerMapDefB", ref b, "%.1f");
        defB = b;
        bool commitB = ImGui.IsItemDeactivatedAfterEdit();
        if (commitA || commitB)
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(
                PlayerMapCommandKind.SetDefaultMaterialRect,
                integer: def.Id,
                value: ToUnity(defA),
                valueB: ToUnity(defB)));

        bool air = def.Air;
        if (ImGui.Checkbox(DevToolUiSettings.T("Air 区域", "Air region"), ref air))
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.SetDefaultMaterialAir, integer: def.Id, flag: air));
        if (DevToolWidgets.ActionButton(DevToolUiSettings.T("删除", "Delete"), "PlayerMapDeleteDef", DevToolButtonTone.Danger))
        {
            PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(PlayerMapCommandKind.DeleteDefaultMaterial, integer: def.Id));
            selectedDefMaterial = -1;
        }
    }

    internal static void DrawRenderReportBase(PlayerMapPresentationSnapshot snapshot)
    {
        DevToolWidgets.SectionHeader(DevToolUiSettings.T("输出状态", "OUTPUT STATUS"));
        PlayerMapRenderReport report = snapshot.RenderReport;
        if (!report.Attempted)
        {
            DevToolWidgets.MutedText(
                DevToolUiSettings.T("尚未生成地图。", "No map generated yet."),
                true);
            return;
        }

        DrawMetric(DevToolUiSettings.T("结果", "Result"), report.Success
            ? (report.Exported ? DevToolUiSettings.T("已导出", "Exported") : DevToolUiSettings.T("预览已生成", "Preview ready"))
            : DevToolUiSettings.T("生成失败", "Failed"));
        if (report.Width > 0 && report.Height > 0) DrawMetric(DevToolUiSettings.T("尺寸", "Size"), report.Width + " x " + report.Height);
        if (report.IncludedRooms > 0) DrawMetric(DevToolUiSettings.T("房间", "Rooms"), report.IncludedRooms.ToString());
        if (!string.IsNullOrWhiteSpace(report.OutputPath)) DevToolWidgets.MutedText(report.OutputPath, true);
        if (!string.IsNullOrWhiteSpace(report.Message)) DevToolWidgets.MutedText(report.Message, true);
        for (int i = 0; i < report.Errors.Length; i++)
            ImGui.TextWrapped("ERROR | " + report.Errors[i]);
        for (int i = 0; i < report.Warnings.Length; i++)
            ImGui.TextWrapped("WARN | " + report.Warnings[i]);
    }

    private static void DrawRawPreview(
        ImDrawListPtr draw,
        Num.Vector2 canvasMin,
        Num.Vector2 canvasSize,
        PlayerMapRenderPreview preview)
    {
        if (!preview.Available || preview.Width <= 0 || preview.Height <= 0) return;
        float scale = Math.Min((canvasSize.X - 24f) / preview.Width, (canvasSize.Y - 24f) / preview.Height);
        scale = Math.Max(0.02f, scale);
        Num.Vector2 size = new(preview.Width * scale, preview.Height * scale);
        Num.Vector2 origin = canvasMin + (canvasSize - size) * 0.5f;
        draw.AddRect(origin, origin + size, ImGui.GetColorU32(ImGuiCol.Border));
        for (int i = 0; i < preview.Runs.Length; i++)
        {
            PlayerMapPreviewRun run = preview.Runs[i];
            Num.Vector2 a = origin + new Num.Vector2(run.X * scale, (preview.Height - run.Y - 1) * scale);
            Num.Vector2 b = a + new Num.Vector2(run.Length * scale, Math.Max(1f, scale));
            draw.AddRectFilled(a, b, Pack(run.Color));
        }
    }

    private static PlayerMapRoomSnapshot HitRoom(PlayerMapPresentationSnapshot snapshot, Num.Vector2 canvasMin, Num.Vector2 mouse)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            int plane = depthEnabled && depthFocus >= 0 ? (depthFocus + pass) % 3 : pass;
            for (int i = snapshot.Rooms.Length - 1; i >= 0; i--)
            {
                PlayerMapRoomSnapshot room = snapshot.Rooms[i];
                if (room.Layer != plane) continue;
                if (room.Disabled || !LayerVisible[Math.Max(0, Math.Min(2, room.Layer))]) continue;
                GetRoomRect(room, canvasMin, out Num.Vector2 min, out Num.Vector2 max);
                if(MapDepthProjection.TryUnproject((mouse-canvasMin-pan)/zoom,room.Layer,depthEnabled,planeFrame,out var native))
                {
                    NativeRoomRect(room,DisplayPosition(room),out var a,out var b);
                    if(native.X>=a.X && native.X<=b.X && native.Y>=a.Y && native.Y<=b.Y) return room;
                }
            }
        }
        return null;
    }

    internal static void NativeRoomRect(PlayerMapRoomSnapshot room, Vector2 position, out Num.Vector2 min,out Num.Vector2 max)
    {
        var center=PlayerMapCanvasCoordinates.Project(position.x,position.y);
        var half=new Num.Vector2(Math.Max(1,room.Bake.Width),Math.Max(1,room.Bake.Height))*PlayerMapCoordinateSystem.CanonPixelsPerTile*.5f;
        min=center-half; max=center+half;
    }

    internal static void DrawRoomQuad(ImDrawListPtr draw,PlayerMapRoomSnapshot room,Vector2 position,Num.Vector2 canvasMin,
        uint color,bool filled,float thickness=1f)
    {
        NativeRoomRect(room,position,out var min,out var max);
        MapPlaneDrawing.Quad(draw,min,max,room.Layer,planeFrame,canvasMin,pan,zoom,color,filled,thickness);
    }

    private static void GetRoomRect(PlayerMapRoomSnapshot room,Num.Vector2 canvasMin,out Num.Vector2 min,out Num.Vector2 max)
    {
        NativeRoomRect(room,DisplayPosition(room),out var a,out var b);
        MapDepthProjection.Bounds(a,b,room.Layer,depthEnabled,planeFrame,out min,out max);
        min=canvasMin+pan+min*zoom; max=canvasMin+pan+max*zoom;
    }

    private static Vector2 DisplayPosition(PlayerMapRoomSnapshot room) =>
        PlayerMapMultiSelection.TryGetPreviewPosition(room.RoomIndex,out var preview) ? preview :
            draggingRoom == room.RoomIndex ? dragPreviewPosition : room.EffectivePosition;

    private static Num.Vector2 RoomLabelPoint(PlayerMapRoomSnapshot room,Num.Vector2 canvasMin)
    {
        NativeRoomRect(room,DisplayPosition(room),out var min,out var max);
        return canvasMin+pan+MapDepthProjection.Project(min,room.Layer,true,planeFrame)*zoom;
    }

    private static void Fit(PlayerMapPresentationSnapshot snapshot,Num.Vector2 canvasSize)
    {
        var min=new Num.Vector2(float.MaxValue); var max=new Num.Vector2(float.MinValue);
        foreach(var room in snapshot.Rooms)
        {
            if(room.Disabled || !LayerVisible[Math.Max(0,Math.Min(2,room.Layer))]) continue;
            NativeRoomRect(room,room.EffectivePosition,out var a,out var b);
            MapDepthProjection.Bounds(a,b,room.Layer,depthEnabled,planeFrame,out a,out b);
            min=Num.Vector2.Min(min,a); max=Num.Vector2.Max(max,b);
        }
        if(min.X>max.X) { zoom=1f; pan=canvasSize*.5f; return; }
        var size=Num.Vector2.Max(max-min,new Num.Vector2(1f));
        zoom=Math.Max(MinZoom,Math.Min(MaxZoom,Math.Min((canvasSize.X-36f)/size.X,(canvasSize.Y-36f)/size.Y)));
        pan=canvasSize*.5f-(min+max)*.5f*zoom;
    }

    internal static PlayerMapRoomSnapshot FindRoom(PlayerMapPresentationSnapshot snapshot, int roomIndex)
    {
        var rooms = snapshot?.Rooms;
        if (!ReferenceEquals(indexedRooms, rooms))
        {
            indexedRooms = rooms; roomsByIndex.Clear();
            if (rooms != null) foreach (var room in rooms)
                if (room != null && !roomsByIndex.ContainsKey(room.RoomIndex)) roomsByIndex.Add(room.RoomIndex, room);
        }
        return roomsByIndex.TryGetValue(roomIndex, out var found) ? found : null;
    }

    private static PlayerMapDefMaterialSnapshot? FindDef(PlayerMapPresentationSnapshot snapshot, int id)
    {
        if (id < 0) return null;
        for (int i = 0; i < snapshot.DefaultMaterials.Length; i++)
            if (snapshot.DefaultMaterials[i].Id == id) return snapshot.DefaultMaterials[i];
        return null;
    }

    private static uint PreviewColor(RoomMapPixelKind kind, bool water, int layer)
    {
        Num.Vector4 c = kind switch
        {
            RoomMapPixelKind.Solid => new Num.Vector4(0.20f, 0.22f, 0.25f, 1f),
            RoomMapPixelKind.Structure => new Num.Vector4(0.55f, 0.30f, 0.28f, 1f),
            RoomMapPixelKind.BackWall => new Num.Vector4(0.38f, 0.40f, 0.43f, 1f),
            RoomMapPixelKind.RoomExit => new Num.Vector4(0.25f, 0.85f, 0.36f, 1f),
            RoomMapPixelKind.CreatureHole => new Num.Vector4(0.85f, 0.25f, 0.85f, 1f),
            RoomMapPixelKind.NormalShortcut => new Num.Vector4(0.85f, 0.85f, 0.85f, 1f),
            RoomMapPixelKind.NpcTransport => new Num.Vector4(0.75f, 0.28f, 0.20f, 1f),
            RoomMapPixelKind.RegionTransport => new Num.Vector4(0.12f, 0.12f, 0.12f, 1f),
            RoomMapPixelKind.UnknownShortcut => new Num.Vector4(0.35f, 0.35f, 0.35f, 1f),
            _ => new Num.Vector4(0.30f, 0.32f, 0.35f, 1f)
        };
        if (water)
        {
            c.X *= 0.72f;
            c.Y = c.Y * 0.72f + 0.08f;
            c.Z = Math.Min(1f, c.Z * 0.72f + 0.32f);
        }
        c.W = MapDepthProjection.Opacity(layer, depthEnabled, depthFocus);
        return ImGui.ColorConvertFloat4ToU32(c);
    }

    private static uint Pack(Color32 color) =>
        (uint)(color.r | color.g << 8 | color.b << 16 | color.a << 24);

    private static void DrawMetric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value ?? string.Empty);
    }

    private static void DrawVectorMetric(string label, Vector2 value) =>
        DrawMetric(label, value.x.ToString("0.##") + ", " + value.y.ToString("0.##"));

    private static float PositiveModulo(float value, float modulus)
    {
        if (modulus <= 0f) return 0f;
        float result = value % modulus;
        return result < 0f ? result + modulus : result;
    }

    private static Num.Vector2 ToNum(Vector2 value) => new(value.x, value.y);
    private static Vector2 ToUnity(Num.Vector2 value) => new(value.X, value.Y);
}

internal static class PlayerMapWorkspaceIntegration
{
    private static ManualLogSource log;
    private static bool enabled;
    private static bool playerMapActive;
    private static bool cartographyActive;

    internal static bool Active => enabled && playerMapActive;
    internal static bool CartographyActive => enabled && cartographyActive;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map integrated into World Workspace through direct view calls; no self-detour attached.");
    }

    internal static void Disable()
    {
        ExitSpecialView();
        playerMapActive = false;
        PlayerMapActivityGate.Reset();
        enabled = false;
        log = null;
    }

    internal static void DrawToolbar(
        EditorPresentationSnapshot editor,
        EditorMapPresentationSnapshot snapshot)
    {
        if (!enabled || snapshot?.Available != true) return;

        if ((playerMapActive || cartographyActive) && WorldWorkspaceView.WorkspaceModeValue != 0)
        {
            ExitSpecialView();
        }

        ImGui.SameLine(0f, 6f);
        if (DevToolWidgets.ToolbarButton(
                DevToolUiSettings.T("玩家地图", "Player Map"),
                "WorldWorkspacePlayerMap",
                DevToolToolbarTone.PlayerMap,
                playerMapActive))
        {
            if (cartographyActive) CartographyView.Leave();
            cartographyActive = false;
            playerMapActive = !playerMapActive;
            if (playerMapActive)
                WorldWorkspaceView.WorkspaceModeValue = 0;
            else
                PlayerMapActivityGate.Reset();
        }

        ImGui.SameLine(0f, 6f);
        if (DevToolWidgets.ToolbarButton(
                DevToolUiSettings.T("制图", "Cartography"),
                "WorldWorkspaceCartography",
                DevToolToolbarTone.Cartography,
                cartographyActive))
        {
            if (cartographyActive) ExitSpecialView();
            else { playerMapActive = false; cartographyActive = true; WorldWorkspaceView.WorkspaceModeValue = 0; }
        }
        CartographyRuntime.SetActive(cartographyActive);
    }

    internal static void ExitSpecialView()
    {
        if (cartographyActive) CartographyView.Leave();
        cartographyActive = false; playerMapActive = false;
        CartographyRuntime.SetActive(false);
        PlayerMapActivityGate.Reset();
    }

    internal static bool DrawBodyIfActive(
        EditorPresentationSnapshot editor,
        EditorMapPresentationSnapshot snapshot)
    {
        if (!enabled || (!playerMapActive && !cartographyActive))
            return false;

        PlayerMapActivityGate.MarkVisible();
        if (cartographyActive) { CartographyView.Draw(editor); return true; }
        PlayerMapWorkspaceView.DrawBody(editor, snapshot);
        return true;
    }
}

internal static class PlayerMapPreflightPanel
{
    private static ManualLogSource log;
    private static bool enabled;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map live preflight inspector enabled through direct view calls; no self-detour attached.");
    }

    internal static void Disable()
    {
        enabled = false;
        log = null;
    }

    internal static void Draw(PlayerMapPresentationSnapshot snapshot)
    {
        if (!enabled || snapshot?.Available != true) return;

        EditorMapPresentationSnapshot world = MapEditorPresentationHub.Current;
        PlayerMapPreflightSnapshot preflight = PlayerMapPreflightDiagnostics.Evaluate(snapshot, world);
        if (!preflight.Available) return;

        ImGui.Separator();
        DevToolWidgets.SectionHeader(DevToolUiSettings.T("地图检查", "MAP CHECK"));
        if (preflight.Computing)
        {
            ImGui.TextDisabled(DevToolUiSettings.T("预检计算中…", "Checking map…"));
            return;
        }
        string state = preflight.CanRender
            ? DevToolUiSettings.T("可以生成地图", "Ready to generate")
            : DevToolUiSettings.T("需要处理", "Needs Attention");
        ImGui.TextUnformatted(state);

        if (preflight.PendingRooms > 0)
            DrawMetric(DevToolUiSettings.T("正在载入的房间", "Loading rooms"), preflight.PendingRooms.ToString());
        if (preflight.MissingRooms + preflight.FailedRooms > 0)
            DrawMetric(DevToolUiSettings.T("不可用的房间", "Unavailable rooms"), (preflight.MissingRooms + preflight.FailedRooms).ToString());
        if (preflight.AmbiguousConnections > 0)
            DrawMetric(DevToolUiSettings.T("待确认的连接", "Unresolved connections"), preflight.AmbiguousConnections.ToString());
        if (preflight.InvalidEndpoints > 0)
            DrawMetric(DevToolUiSettings.T("无效出口", "Invalid exits"), preflight.InvalidEndpoints.ToString());
        if (preflight.DuplicateEndpointClaims > 0)
            DrawMetric(DevToolUiSettings.T("出口冲突", "Exit conflicts"), preflight.DuplicateEndpointClaims.ToString());
        if (preflight.Width > 0 && preflight.Height > 0)
            DrawMetric(DevToolUiSettings.T("输出尺寸", "Output size"), preflight.Width + " x " + preflight.Height);

        int errorLimit = Math.Min(6, preflight.Errors.Length);
        for (int i = 0; i < errorLimit; i++)
            ImGui.TextWrapped("ERROR | " + preflight.Errors[i]);
        if (preflight.Errors.Length > errorLimit)
            ImGui.TextDisabled("... +" + (preflight.Errors.Length - errorLimit) + " errors");

        int warningLimit = Math.Min(5, preflight.Warnings.Length);
        for (int i = 0; i < warningLimit; i++)
            ImGui.TextWrapped("WARN | " + preflight.Warnings[i]);
        if (preflight.Warnings.Length > warningLimit)
            ImGui.TextDisabled("... +" + (preflight.Warnings.Length - warningLimit) + " warnings");
    }

    private static void DrawMetric(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine();
        ImGui.TextUnformatted(value ?? string.Empty);
    }

}

internal static class PlayerMapRenderProgressView
{
    private static ManualLogSource log;
    private static bool enabled;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map live Render progress enabled through direct view calls; no self-detour attached.");
    }

    internal static void Disable()
    {
        enabled = false;
        log = null;
    }

    internal static void Draw(PlayerMapPresentationSnapshot snapshot)
    {
        if (!enabled)
        {
            PlayerMapWorkspaceView.DrawRenderReportBase(snapshot);
            return;
        }

        PlayerMapRenderProgressSnapshot render = PlayerMapRenderProgressState.Current;
        if (render?.Running == true)
        {
            DrawRenderProgress(render);
            return;
        }

        PlayerMapRenderPreparationSnapshot preparation = PlayerMapRenderPreparationState.Current;
        if (preparation?.Running == true)
        {
            DrawPreparationProgress(preparation);
            return;
        }

        PlayerMapWorkspaceView.DrawRenderReportBase(snapshot);
    }

    private static void DrawPreparationProgress(PlayerMapRenderPreparationSnapshot progress)
    {
        DevToolWidgets.SectionHeader(DevToolUiSettings.T("Render 进度", "RENDER PROGRESS"));
        ImGui.TextUnformatted(DevToolUiSettings.T("准备房间 Bake", "Preparing room bakes"));
        DrawProgressBar(progress.Progress);

        string percent = (progress.Progress * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        string units = progress.ReadyRooms.ToString(CultureInfo.InvariantCulture) + " / " +
                       progress.TotalRooms.ToString(CultureInfo.InvariantCulture) + " rooms";
        ImGui.TextDisabled(percent + "  |  " + units);
        if (!string.IsNullOrWhiteSpace(progress.Detail))
            ImGui.TextWrapped(progress.Detail);

        if (progress.CanCancel && DevToolWidgets.ActionButton(
                DevToolUiSettings.T("取消 Render", "Cancel Render"),
                "PlayerMapCancelRenderPreparation",
                DevToolButtonTone.Danger))
            PlayerMapRenderPreparationController.RequestCancel();
    }

    private static void DrawRenderProgress(PlayerMapRenderProgressSnapshot progress)
    {
        DevToolWidgets.SectionHeader(DevToolUiSettings.T("Render 进度", "RENDER PROGRESS"));
        ImGui.TextUnformatted(progress.StageLabel ?? string.Empty);
        DrawProgressBar(progress.Progress);

        string percent = (progress.Progress * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";
        string stagePercent = (progress.StageProgress * 100f).ToString("0", CultureInfo.InvariantCulture) + "%";
        ImGui.TextDisabled(percent + "  |  " + stagePercent + " " + DevToolUiSettings.T("阶段", "stage"));

        if (progress.TotalUnits > 1)
        {
            string units = progress.CompletedUnits.ToString("N0", CultureInfo.InvariantCulture) + " / " +
                           progress.TotalUnits.ToString("N0", CultureInfo.InvariantCulture);
            ImGui.TextDisabled(units);
        }
        if (!string.IsNullOrWhiteSpace(progress.Detail))
            ImGui.TextWrapped(progress.Detail);

        if (progress.CanCancel)
        {
            if (DevToolWidgets.ActionButton(
                    DevToolUiSettings.T("取消 Render", "Cancel Render"),
                    "PlayerMapCancelRender",
                    DevToolButtonTone.Danger))
                PlayerMapRenderScheduler.RequestCancel();
        }
        else
        {
            ImGui.TextDisabled(DevToolUiSettings.T("正在提交文件，已不可取消。", "Committing files; cancellation is disabled."));
        }
    }

    private static void DrawProgressBar(float fraction)
    {
        fraction = Math.Max(0f, Math.Min(1f, fraction));
        float width = Math.Max(80f, ImGui.GetContentRegionAvail().X);
        float height = Math.Max(8f, ImGui.GetFrameHeight() * 0.62f);
        Num.Vector2 min = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##PlayerMapRenderProgressBar", new Num.Vector2(width, height));
        Num.Vector2 max = min + new Num.Vector2(width, height);
        ImDrawListPtr draw = ImGui.GetWindowDrawList();
        uint bg = ImGui.GetColorU32(ImGuiCol.FrameBg);
        uint fill = ImGui.GetColorU32(ImGuiCol.HeaderActive);
        uint border = ImGui.GetColorU32(ImGuiCol.Border);
        draw.AddRectFilled(min, max, bg, 2f);
        if (fraction > 0f)
        {
            Num.Vector2 fillMax = new(min.X + width * fraction, max.Y);
            draw.AddRectFilled(min, fillMax, fill, 2f);
        }
        draw.AddRect(min, max, border, 2f);
    }

}

internal static class PlayerMapCanvasAuthoring
{
    private enum DragKind
    {
        None,
        Create,
        Move,
        HandleA,
        HandleB
    }

    private static ManualLogSource log;
    private static bool enabled;

    private static bool createArmed;
    private static DragKind dragKind;
    private static int dragDefId = -1;
    private static Vector2 dragStartA;
    private static Vector2 dragStartB;
    private static Vector2 dragStartMouseCanon;
    private static Vector2 previewA;
    private static Vector2 previewB;
    private static bool consumedCanvasInput;

    internal static bool OwnsCanvas =>
        enabled && (consumedCanvasInput || createArmed || dragKind != DragKind.None);

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map direct Def_Mat authoring/output bounds enabled through direct view calls; no self-detour attached.");
    }

    internal static void Disable()
    {
        createArmed = false;
        ResetDrag();
        enabled = false;
        log = null;
    }

    internal static void DrawInspectorTools(
        PlayerMapPresentationSnapshot snapshot,
        PlayerMapRoomSnapshot room)
    {
        if (!enabled) return;

        ImGui.Spacing();
        string label = createArmed
            ? DevToolUiSettings.T("取消画矩形", "Cancel Rectangle Tool")
            : DevToolUiSettings.T("画 Def_Mat 矩形", "Draw Def_Mat Rectangle");
        if (DevToolWidgets.ActionButton(
                label,
                "PlayerMapDefRectTool",
                createArmed ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle))
        {
            createArmed = !createArmed;
            ResetDrag();
        }
        if (ImGui.IsItemHovered())
            DevToolTooltip.Show(DevToolUiSettings.T(
                "在中央画布拖拽创建矩形；选中矩形后可直接拖两个角点或拖矩形内部整体移动。",
                "Drag on the center canvas to create a rectangle. Select one, then drag either corner or the body to move it."));
    }

    internal static void DrawCanvasOverlay(
        ImDrawListPtr draw,
        PlayerMapPresentationSnapshot snapshot,
        Num.Vector2 canvasMin)
    {
        consumedCanvasInput = false;
        if (!enabled || snapshot?.Available != true)
            return;

        Num.Vector2 pan = PlayerMapWorkspaceView.Pan;
        float zoom = PlayerMapWorkspaceView.Zoom;
        if (zoom <= 0f || float.IsNaN(zoom) || float.IsInfinity(zoom))
            return;

        DrawOutputBounds(draw, snapshot, canvasMin, pan, zoom);

        bool canvasHovered = ImGui.IsItemHovered();
        Num.Vector2 mouse = ImGui.GetIO().MousePos;
        Vector2 mouseCanon = ScreenToCanon(mouse, canvasMin, pan, zoom);
        int selectedId = PlayerMapWorkspaceView.SelectedDefMaterial;
        PlayerMapDefMaterialSnapshot? selected = FindDef(snapshot, selectedId);

        // Arm/Create mode owns the canvas until mouse-up so room dragging can never start beneath it.
        if (createArmed)
        {
            consumedCanvasInput = canvasHovered || dragKind == DragKind.Create;
            if (canvasHovered && dragKind == DragKind.None && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                dragKind = DragKind.Create;
                dragStartMouseCanon = mouseCanon;
                previewA = mouseCanon;
                previewB = mouseCanon;
            }

            if (dragKind == DragKind.Create)
            {
                previewB = mouseCanon;
                DrawPreviewRect(draw, canvasMin, pan, zoom, previewA, previewB, true);
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    if ((previewB - previewA).sqrMagnitude >= 9f)
                    {
                        PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(
                            PlayerMapCommandKind.CreateDefaultMaterial,
                            value: previewA,
                            valueB: previewB,
                            flag: false));
                    }
                    createArmed = false;
                    ResetDrag();
                }
            }
            return;
        }

        if (selected.HasValue)
            DrawSelectedHandles(draw, selected.Value, canvasMin, pan, zoom);

        if (dragKind == DragKind.None && canvasHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            PlayerMapDefMaterialSnapshot? hit = HitDef(snapshot, mouseCanon, zoom);
            if (hit.HasValue)
            {
                PlayerMapDefMaterialSnapshot def = hit.Value;
                PlayerMapWorkspaceView.SelectedDefMaterial = def.Id;
                selected = def;
                selectedId = def.Id;
                dragDefId = def.Id;
                dragStartA = def.A;
                dragStartB = def.B;
                previewA = def.A;
                previewB = def.B;
                dragStartMouseCanon = mouseCanon;
                dragKind = HitHandle(def.A, mouse, canvasMin, pan, zoom)
                    ? DragKind.HandleA
                    : HitHandle(def.B, mouse, canvasMin, pan, zoom)
                        ? DragKind.HandleB
                        : DragKind.Move;
                consumedCanvasInput = true;
            }
        }

        if (dragKind != DragKind.None && dragKind != DragKind.Create && dragDefId >= 0)
        {
            consumedCanvasInput = true;
            Vector2 delta = mouseCanon - dragStartMouseCanon;
            switch (dragKind)
            {
                case DragKind.HandleA:
                    previewA = dragStartA + delta;
                    previewB = dragStartB;
                    break;
                case DragKind.HandleB:
                    previewA = dragStartA;
                    previewB = dragStartB + delta;
                    break;
                case DragKind.Move:
                    previewA = dragStartA + delta;
                    previewB = dragStartB + delta;
                    break;
            }

            DrawPreviewRect(draw, canvasMin, pan, zoom, previewA, previewB, false);
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                if ((previewA - dragStartA).sqrMagnitude > 0.0001f ||
                    (previewB - dragStartB).sqrMagnitude > 0.0001f)
                {
                    PlayerMapCommandQueue.Enqueue(new PlayerMapCommand(
                        PlayerMapCommandKind.SetDefaultMaterialRect,
                        integer: dragDefId,
                        value: previewA,
                        valueB: previewB));
                }
                ResetDrag();
            }
        }
    }

    private static void DrawOutputBounds(
        ImDrawListPtr draw,
        PlayerMapPresentationSnapshot snapshot,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom)
    {
        bool any = false;
        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;
        PlayerMapRoomSnapshot[] rooms = snapshot.Rooms ?? Array.Empty<PlayerMapRoomSnapshot>();
        for (int i = 0; i < rooms.Length; i++)
        {
            PlayerMapRoomSnapshot room = rooms[i];
            if (room == null || room.Disabled) continue;
            float halfW = Math.Max(1, room.Bake?.Width ?? 1) * PlayerMapCoordinateSystem.CanonPixelsPerTile * 0.5f;
            float halfH = Math.Max(1, room.Bake?.Height ?? 1) * PlayerMapCoordinateSystem.CanonPixelsPerTile * 0.5f;
            minX = Math.Min(minX, room.EffectivePosition.x - halfW);
            minY = Math.Min(minY, room.EffectivePosition.y - halfH);
            maxX = Math.Max(maxX, room.EffectivePosition.x + halfW);
            maxY = Math.Max(maxY, room.EffectivePosition.y + halfH);
            any = true;
        }
        if (!any) return;

        float pad = PlayerMapCoordinateSystem.OutputPadding * PlayerMapCoordinateSystem.CanonPixelsPerTile;
        Vector2 a = new(minX - pad, minY - pad);
        Vector2 b = new(maxX + pad, maxY + pad);
        Num.Vector2 sa = CanonToScreen(a, canvasMin, pan, zoom);
        Num.Vector2 sb = CanonToScreen(b, canvasMin, pan, zoom);
        Num.Vector2 min = new(Math.Min(sa.X, sb.X), Math.Min(sa.Y, sb.Y));
        Num.Vector2 max = new(Math.Max(sa.X, sb.X), Math.Max(sa.Y, sb.Y));
        uint color = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        draw.AddRect(min, max, color, 0f, ImDrawFlags.None, 1.2f);

        int width = (int)((maxX - minX) / PlayerMapCoordinateSystem.CanonPixelsPerTile) +
                    PlayerMapCoordinateSystem.OutputPadding * 2;
        int layerHeight = (int)((maxY - minY) / PlayerMapCoordinateSystem.CanonPixelsPerTile) +
                          PlayerMapCoordinateSystem.OutputPadding * 2;
        int height = layerHeight * PlayerMapCoordinateSystem.LayerCount;
        string label = "Render " + width + " x " + height + "  (" + PlayerMapCoordinateSystem.LayerCount + " layers)";
        draw.AddText(min + new Num.Vector2(5f, 4f), color, label);
    }

    private static void DrawSelectedHandles(
        ImDrawListPtr draw,
        PlayerMapDefMaterialSnapshot def,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom)
    {
        uint color = ImGui.GetColorU32(ImGuiCol.HeaderActive);
        DrawHandle(draw, CanonToScreen(def.A, canvasMin, pan, zoom), color);
        DrawHandle(draw, CanonToScreen(def.B, canvasMin, pan, zoom), color);
    }

    private static void DrawPreviewRect(
        ImDrawListPtr draw,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom,
        Vector2 a,
        Vector2 b,
        bool creating)
    {
        Num.Vector2 sa = CanonToScreen(a, canvasMin, pan, zoom);
        Num.Vector2 sb = CanonToScreen(b, canvasMin, pan, zoom);
        Num.Vector2 min = new(Math.Min(sa.X, sb.X), Math.Min(sa.Y, sb.Y));
        Num.Vector2 max = new(Math.Max(sa.X, sb.X), Math.Max(sa.Y, sb.Y));
        uint color = ImGui.GetColorU32(creating ? ImGuiCol.HeaderHovered : ImGuiCol.HeaderActive);
        draw.AddRect(min, max, color, 0f, ImDrawFlags.None, 2f);
        DrawHandle(draw, sa, color);
        DrawHandle(draw, sb, color);
    }

    private static void DrawHandle(ImDrawListPtr draw, Num.Vector2 point, uint color)
    {
        const float r = 4.5f;
        draw.AddRectFilled(point - new Num.Vector2(r, r), point + new Num.Vector2(r, r), color);
    }

    private static PlayerMapDefMaterialSnapshot? HitDef(
        PlayerMapPresentationSnapshot snapshot,
        Vector2 point,
        float zoom)
    {
        PlayerMapDefMaterialSnapshot[] defs = snapshot.DefaultMaterials ?? Array.Empty<PlayerMapDefMaterialSnapshot>();
        // Reverse order: last authored rect is visually/topologically dominant in the compositor.
        for (int i = defs.Length - 1; i >= 0; i--)
        {
            PlayerMapDefMaterialSnapshot def = defs[i];
            float handleCanon = Math.Max(4f, 8f / Math.Max(0.05f, zoom));
            if ((def.A - point).sqrMagnitude <= handleCanon * handleCanon ||
                (def.B - point).sqrMagnitude <= handleCanon * handleCanon ||
                (point.x >= def.Left && point.x <= def.Right && point.y >= def.Bottom && point.y <= def.Top))
                return def;
        }
        return null;
    }

    private static bool HitHandle(
        Vector2 handle,
        Num.Vector2 mouse,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom)
    {
        Num.Vector2 point = CanonToScreen(handle, canvasMin, pan, zoom);
        Num.Vector2 delta = mouse - point;
        return delta.X * delta.X + delta.Y * delta.Y <= 64f;
    }

    private static PlayerMapDefMaterialSnapshot? FindDef(PlayerMapPresentationSnapshot snapshot, int id)
    {
        PlayerMapDefMaterialSnapshot[] defs = snapshot?.DefaultMaterials ?? Array.Empty<PlayerMapDefMaterialSnapshot>();
        for (int i = 0; i < defs.Length; i++)
            if (defs[i].Id == id) return defs[i];
        return null;
    }

    private static Vector2 ScreenToCanon(
        Num.Vector2 screen,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom)
    {
        Num.Vector2 local = (screen - canvasMin - pan) / Math.Max(0.0001f, zoom);
        Num.Vector2 canonical = PlayerMapCanvasCoordinates.Unproject(local);
        return new Vector2(canonical.X, canonical.Y);
    }

    private static Num.Vector2 CanonToScreen(
        Vector2 point,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom) =>
        canvasMin + pan + PlayerMapCanvasCoordinates.Project(point.x, point.y) * zoom;

    internal static void CancelInteraction()
    {
        createArmed = false;
        ResetDrag();
    }

    private static void ResetDrag()
    {
        dragKind = DragKind.None;
        dragDefId = -1;
        dragStartA = default;
        dragStartB = default;
        dragStartMouseCanon = default;
        previewA = default;
        previewB = default;
        consumedCanvasInput = false;
    }

}

internal static class PlayerMapLayoutAssist
{
    private static ManualLogSource log;
    private static bool enabled;
    private static int snapMode = 1; // 0=off, 1=one output pixel, 2=ten output pixels

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        PlayerMapGroupCommandQueue.RegisterMoveTransformer(TransformGroupCommand);
        logger?.LogInfo("Player Map movement snap enabled through direct view/queue APIs; no self-detour attached.");
    }

    internal static void Disable()
    {
        PlayerMapGroupCommandQueue.UnregisterMoveTransformer(TransformGroupCommand);
        enabled = false;
        log = null;
    }

    internal static void DrawToolbar(PlayerMapPresentationSnapshot snapshot)
    {
        if (!enabled) return;

        ImGui.SameLine(0f, 12f);
        ImGui.TextDisabled("Snap");
        ImGui.SameLine(0f, 5f);
        DrawSnapButton("Off", 0);
        ImGui.SameLine(0f, 3f);
        DrawSnapButton("1px", 1);
        ImGui.SameLine(0f, 3f);
        DrawSnapButton("10px", 2);
    }

    private static void DrawSnapButton(string label, int mode)
    {
        if (DevToolWidgets.ActionButton(
                label,
                "PlayerMapSnap" + mode,
                snapMode == mode ? DevToolButtonTone.Primary : DevToolButtonTone.Subtle))
            snapMode = mode;
    }

    private static PlayerMapGroupMoveCommand TransformGroupCommand(PlayerMapGroupMoveCommand command)
    {
        if (!enabled || snapMode == 0 || command.RoomIndices == null || command.EffectivePositions == null ||
            command.RoomIndices.Length == 0 || command.RoomIndices.Length != command.EffectivePositions.Length)
        {
            return command;
        }

        PlayerMapPresentationSnapshot snapshot = PlayerMapWorkspaceRuntime.GetPresentation(DevToolRuntime.ActiveSession);
        PlayerMapRoomSnapshot anchor = FindRoom(snapshot, command.RoomIndices[0]);
        if (anchor == null)
        {
            return command;
        }

        // GroupMove guarantees a shared translation. Quantize that translation rather than the
        // destination coordinate itself, preserving legacy Canon phase and all relative offsets.
        Vector2 requestedDelta = command.EffectivePositions[0] - anchor.EffectivePosition;
        Vector2 snappedDelta = SnapDelta(requestedDelta, SnapGrid);
        Vector2[] positions = new Vector2[command.RoomIndices.Length];
        for (int i = 0; i < command.RoomIndices.Length; i++)
        {
            PlayerMapRoomSnapshot room = FindRoom(snapshot, command.RoomIndices[i]);
            positions[i] = room == null
                ? command.EffectivePositions[i]
                : room.EffectivePosition + snappedDelta;
        }

        return new PlayerMapGroupMoveCommand(command.RoomIndices, positions, command.Label);
    }

    internal static Vector2 AdjustPreviewDelta(Vector2 delta) =>
        enabled && snapMode != 0 ? SnapDelta(delta, SnapGrid) : delta;

    private static PlayerMapRoomSnapshot FindRoom(PlayerMapPresentationSnapshot snapshot, int roomIndex)
        => PlayerMapWorkspaceView.FindRoom(snapshot, roomIndex);

    private static float SnapGrid => PlayerMapCoordinateSystem.CanonPixelsPerTile * (snapMode == 2 ? 10f : 1f);

    private static Vector2 SnapDelta(Vector2 delta, float grid)
    {
        if (grid <= 0f) return delta;
        return new Vector2(
            Mathf.Round(delta.x / grid) * grid,
            Mathf.Round(delta.y / grid) * grid);
    }

}

internal static class PlayerMapMultiPipeConnections
{
    private static ManualLogSource log;
    private static bool enabled;

    internal static void Enable(ManualLogSource logger)
    {
        if (enabled) return;
        enabled = true;
        log = logger;
        logger?.LogInfo("Player Map exact multi-pipe connection rendering enabled through direct view calls; no self-detour attached.");
    }

    internal static void Disable()
    {
        enabled = false;
        log = null;
    }

    internal static bool Draw(
        ImDrawListPtr draw,
        PlayerMapPresentationSnapshot snapshot,
        EditorMapPresentationSnapshot worldSnapshot,
        Num.Vector2 canvasMin)
    {
        if (!enabled || snapshot?.Available != true || worldSnapshot?.Connections == null)
            return false;

        Num.Vector2 pan = PlayerMapWorkspaceView.Pan;
        float zoom = PlayerMapWorkspaceView.Zoom;
        bool[] layers = PlayerMapWorkspaceView.LayerVisibility;
        int draggingRoom = PlayerMapWorkspaceView.DraggingRoom;
        Vector2 dragPreviewPosition = PlayerMapWorkspaceView.DragPreviewPosition;
        if (zoom <= 0f || float.IsNaN(zoom) || float.IsInfinity(zoom) || layers == null)
            return false;

        EditorMapConnectionSnapshot[] links = worldSnapshot.Connections;
        Dictionary<string, int> pairCounts = CountRoomPairs(links);
        uint exactColor = ImGui.GetColorU32(ImGuiCol.TextDisabled);
        uint fallbackColor = ImGui.GetColorU32(ImGuiCol.Border);
        uint unresolvedColor = ImGui.GetColorU32(ImGuiCol.Text);

        for (int i = 0; i < links.Length; i++)
        {
            EditorMapConnectionSnapshot link = links[i];
            if (link == null) continue;
            PlayerMapRoomSnapshot a = FindRoom(snapshot, link.FromRoomIndex);
            PlayerMapRoomSnapshot b = FindRoom(snapshot, link.ToRoomIndex);
            if (!Visible(a, layers) || !Visible(b, layers)) continue;

            string pair = PairKey(link.FromRoomIndex, link.ToRoomIndex);
            bool repeatedPair = pairCounts.TryGetValue(pair, out int pairCount) && pairCount > 1;
            bool trustedExact = !link.Ambiguous && link.FromNodeIndex >= 0 && link.ToNodeIndex >= 0;
            Num.Vector2 pa = default;
            Num.Vector2 pb = default;
            bool exactA = trustedExact && TryEndpointScreen(
                a, link.FromNodeIndex, canvasMin, pan, zoom, draggingRoom, dragPreviewPosition, out pa);
            bool exactB = trustedExact && TryEndpointScreen(
                b, link.ToNodeIndex, canvasMin, pan, zoom, draggingRoom, dragPreviewPosition, out pb);

            float opacity = Math.Max(MapDepthProjection.Opacity(a.Layer,true),MapDepthProjection.Opacity(b.Layer,true));
            uint lineColor = MapDepthProjection.Fade(exactColor,opacity);
            if (exactA && exactB)
            {
                draw.AddLine(pa, pb, lineColor, 1.35f);
                draw.AddCircleFilled(pa, Math.Max(1.6f, 2.15f * zoom), lineColor);
                draw.AddCircleFilled(pb, Math.Max(1.6f, 2.15f * zoom), lineColor);
                continue;
            }

            if (!repeatedPair)
            {
                // Compatibility only: one unresolved legacy link cannot be confused with another
                // pipe, so a centre line remains safe. Repeated unresolved links never get this path.
                Num.Vector2 ca = RoomCenter(a, canvasMin, pan, zoom, draggingRoom, dragPreviewPosition);
                Num.Vector2 cb = RoomCenter(b, canvasMin, pan, zoom, draggingRoom, dragPreviewPosition);
                draw.AddLine(ca, cb, MapDepthProjection.Fade(fallbackColor,opacity), 1f);
                continue;
            }

            if (exactA) DrawUnresolvedEndpoint(draw, pa, unresolvedColor);
            if (exactB) DrawUnresolvedEndpoint(draw, pb, unresolvedColor);
        }
        return true;
    }

    private static Dictionary<string, int> CountRoomPairs(EditorMapConnectionSnapshot[] links)
    {
        Dictionary<string, int> result = new(StringComparer.Ordinal);
        for (int i = 0; i < links.Length; i++)
        {
            EditorMapConnectionSnapshot link = links[i];
            if (link == null) continue;
            string key = PairKey(link.FromRoomIndex, link.ToRoomIndex);
            result.TryGetValue(key, out int count);
            result[key] = count + 1;
        }
        return result;
    }

    private static string PairKey(int a, int b)
    {
        int low = Math.Min(a, b);
        int high = Math.Max(a, b);
        return low + ":" + high;
    }

    private static bool TryEndpointScreen(
        PlayerMapRoomSnapshot room,
        int nodeIndex,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom,
        int draggingRoom,
        Vector2 dragPreviewPosition,
        out Num.Vector2 screen)
    {
        screen = default;
        if (room?.Bake == null || room.Bake.Status != RoomMapBakeStatus.Ready ||
            room.Bake.Width <= 0 || room.Bake.Height <= 0 ||
            !room.Bake.TryGetNodeAnchor(nodeIndex, out RoomMapNodeAnchorSnapshot anchor) ||
            anchor.Kind != RoomMapPixelKind.RoomExit)
            return false;

        Vector2 center = ResolveCenter(room, draggingRoom, dragPreviewPosition);
        // Keep the endpoint in the same Y-up coordinates as its room centre. Project the complete
        // point once, so the exit remains on the matching edge of the locally mirrored room raster.
        Vector2 local = new(
            (anchor.EntranceX - room.Bake.Width * 0.5f) * PlayerMapCoordinateSystem.CanonPixelsPerTile,
            (anchor.EntranceY - room.Bake.Height * 0.5f) * PlayerMapCoordinateSystem.CanonPixelsPerTile);
        Vector2 point = center + local;
        screen = canvasMin + pan + PlayerMapWorkspaceView.ProjectPosition(point, room.Layer) * zoom;
        return true;
    }

    private static Num.Vector2 RoomCenter(
        PlayerMapRoomSnapshot room,
        Num.Vector2 canvasMin,
        Num.Vector2 pan,
        float zoom,
        int draggingRoom,
        Vector2 dragPreviewPosition)
    {
        Vector2 center = ResolveCenter(room, draggingRoom, dragPreviewPosition);
        return canvasMin + pan + PlayerMapWorkspaceView.ProjectPosition(center, room.Layer) * zoom;
    }

    private static Vector2 ResolveCenter(
        PlayerMapRoomSnapshot room,
        int draggingRoom,
        Vector2 dragPreviewPosition)
    {
        if (room != null && PlayerMapMultiSelection.TryGetPreviewPosition(room.RoomIndex, out Vector2 groupPreview))
            return groupPreview;
        return draggingRoom == room?.RoomIndex ? dragPreviewPosition : room?.EffectivePosition ?? Vector2.zero;
    }

    private static bool Visible(PlayerMapRoomSnapshot room, bool[] layers)
    {
        if (room == null || room.Disabled) return false;
        int layer = Math.Max(0, Math.Min(PlayerMapCoordinateSystem.LayerCount - 1, room.Layer));
        return layers != null && layer < layers.Length && layers[layer];
    }

    private static PlayerMapRoomSnapshot FindRoom(PlayerMapPresentationSnapshot snapshot, int roomIndex)
        => PlayerMapWorkspaceView.FindRoom(snapshot, roomIndex);

    private static void DrawUnresolvedEndpoint(ImDrawListPtr draw, Num.Vector2 point, uint color)
    {
        const float radius = 4f;
        draw.AddLine(point + new Num.Vector2(-radius, -radius), point + new Num.Vector2(radius, radius), color, 1.4f);
        draw.AddLine(point + new Num.Vector2(-radius, radius), point + new Num.Vector2(radius, -radius), color, 1.4f);
    }

}
