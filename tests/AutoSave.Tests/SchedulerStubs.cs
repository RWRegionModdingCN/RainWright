using System;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DayNight
{
    internal static class RegionDayNightOptions
    {
        internal static bool DevToolAutoSaveEnabled = true;
        internal static int DevToolAutoSaveIntervalSeconds = 150;
    }
}

namespace DryCycle.DevUI.DevTool.Core
{
    internal enum EditorDocumentKind { Room, RegionMap, Relationships }

    internal readonly struct EditorDocumentKey : IEquatable<EditorDocumentKey>
    {
        internal readonly EditorDocumentKind Kind;
        internal readonly string Name;
        internal EditorDocumentKey(EditorDocumentKind kind, string name = "test")
        {
            Kind = kind;
            Name = name;
        }
        public bool Equals(EditorDocumentKey other) => Kind == other.Kind && Name == other.Name;
        public override bool Equals(object obj) => obj is EditorDocumentKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Kind, Name);
        public override string ToString() => Kind + "/" + Name;
    }

    internal sealed class EditorHistory
    {
        private readonly History.EditorHistoryJournal<EditorDocumentKey, object> journal = new(64, _ => "edit", (_, _) => new object());
        internal long Revision => journal.Revision;
        internal long AuthoringRevision => journal.AuthoringRevision;
        internal void CommitEdit() => journal.Push(new object());
        internal void ActivateDocument(EditorDocumentKey document) => journal.ActivateDocument(document);
        internal bool Undo() => journal.Undo(_ => true);
        internal bool Redo() => journal.Redo(_ => true);
    }

    internal sealed class LegacyTransactionState
    {
        internal bool HasPendingTransaction;
        internal void CommitPending(EditorSession session) { if (HasPendingTransaction) session.History.CommitEdit(); HasPendingTransaction = false; }
    }

    internal sealed class EditorSession
    {
        private EditorDocumentKey documentKey = new(EditorDocumentKind.Room);
        internal EditorDocumentKey DocumentKey
        {
            get => documentKey;
            set { documentKey = value; History.ActivateDocument(value); }
        }
        internal readonly EditorHistory History = new();
        internal EditorSession() { History.ActivateDocument(documentKey); }
        internal readonly LegacyTransactionState LegacyTransactions = new();
        internal RoomSettings RoomSettings;
        internal global::DevUI Owner;
    }

    internal static class DevToolSessionHub
    {
        internal static bool IsCurrentSessionLive = true;
        internal static EditorSession Current;
    }
}

namespace DryCycle.DevUI.DevTool.History
{
    internal static class EditorContinuousTransactionHub
    {
        internal static bool Active;
        internal static bool IsActive(Core.EditorSession session) => Active;
        internal static void CommitAny(Core.EditorSession session) { if (Active) session.History.CommitEdit(); Active = false; }
    }
}

namespace DryCycle.DevUI.DevTool.Commands
{
    internal static class EditorActions
    {
        internal static bool SaveResult = true;
        internal static int SaveCount;
        internal static bool LastIncludedActiveDocument;
        internal static bool SavePending(EditorSession session, bool includeActiveDocument)
        { LastIncludedActiveDocument = includeActiveDocument; return Save(session); }

        internal static bool Save(EditorSession session)
        {
            SaveCount++;
            if (SaveResult)
            {
                World.WorldTextRegistry.Dirty = false;
                World.WorldRoomAttractionRegistry.Dirty = false;
                World.WorldTopologyRegistry.Dirty = false;
                Map.PlayerMap.PlayerMapWorkspaceRuntime.DirtyFlag = false;
                Background.BackgroundEditorRuntime.Dirty = false;
                AutoSave.AutoSaveCoordinator.ObserveSaveResult(session, true);
            }
            return SaveResult;
        }
    }
}

namespace DryCycle.DevUI.DevTool.Input
{
    internal static class EditorInputRouter
    {
        internal static bool WantsTextInput;
        internal static bool CapturesNativeDevUiKeys(global::RainWorldGame game) => WantsTextInput;
    }
}

namespace UnityEngine
{
    internal static class Input
    {
        internal static bool MousePressed;
        internal static bool CloseHeld;
        internal static bool GetKey(KeyCode key) => CloseHeld && key == KeyCode.H;
        internal static bool GetMouseButton(int button) => button == 0 && MousePressed;
    }
}

namespace DryCycle.DevUI.DevTool.Map.Cartography
{
    internal static class CartographyRuntime
    {
        internal static bool ActiveFor(EditorSession session) => false;
        internal static bool HasPendingAuthorChanges(EditorSession session) => false;
        internal static bool TryGetRecoveryText(EditorSession session, out string path, out string content)
        {
            path = null;
            content = null;
            return false;
        }
    }
}

namespace DryCycle.DevUI.DevTool.Map
{
    internal static class NativeMapAuthoringStateHub
    {
        internal static bool HasPendingPositionSave(EditorSession session) => false;
    }
}

namespace DryCycle.DevUI.DevTool.Map.PlayerMap
{
    internal static class PlayerMapWorkspaceRuntime
    {
        internal static bool DirtyFlag;
        internal static bool IsDirty(EditorSession session) => DirtyFlag;
        internal static bool TryGetRecoveryText(EditorSession session, out string path, out string text)
        {
            path = null;
            text = null;
            return false;
        }
    }
}

namespace DryCycle.DevUI.DevTool.World
{
    internal static class WorldFileVariantStore
    {
        internal static void CaptureRecovery(string document, string source, string text, System.Action<string, string, string> capture) => capture(document + "/world.txt", source, text);
    }
    internal static class WorldTextRegistry
    {
        internal static bool Dirty;
        internal static int Revision;
        internal static int AuthoringRevision => Revision;
        internal static bool TryGetRecoveryText(out string path, out string text)
        {
            path = null; text = null; return false;
        }
    }
    internal static class WorldRoomAttractionRegistry
    {
        internal static bool Dirty;
        internal static bool TryGetRecoveryText(out string path, out string text)
        {
            path = null; text = null; return false;
        }
    }
    internal static class WorldTopologyRegistry
    {
        internal static bool Dirty;
        internal static bool TryGetRecoveryText(out string path, out string text)
        {
            path = null; text = null; return false;
        }
    }
}

namespace DryCycle.TemperatureSystem
{
    internal static class TemperatureSetsLoader
    {
        internal static bool Dirty;
        internal static bool TryGetRecoveryText(out string path, out string text)
        {
            path = null; text = null; return false;
        }
    }
}

namespace DryCycle.Weather.Spatial
{
    internal static class WeatherSpatialRegistry
    {
        internal static bool Dirty;
        internal static bool TryGetRecoveryText(out string path, out string text)
        {
            path = null; text = null; return false;
        }
    }
}

namespace DryCycle.DevUI.DevTool.AutoSave
{
    internal static class RelationshipSaveAdapter
    {
        internal static bool TryGetText(out string path, out string text)
        {
            path = null; text = null; return false;
        }
    }
}
