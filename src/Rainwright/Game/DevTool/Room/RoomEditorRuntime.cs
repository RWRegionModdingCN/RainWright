using System;
using System.Collections.Concurrent;
using DryCycle.DevUI.DevTool.Compatibility;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;

namespace DryCycle.DevUI.DevTool.Room;

public static class RoomEditorPresentationHub
{
    private static EditorRoomSettingsSnapshot current
    {
        get => EditorRoomPresentationState.Current;
        set => EditorRoomPresentationState.Publish(value);
    }
    private static EditorSession observedSession;
    private static global::RoomSettings observedSettings;
    private static long observedRevision;

    public static EditorRoomSettingsSnapshot Current => current;
    internal static DevToolPresentationOutcome LastOutcome { get; private set; } = DevToolPresentationOutcome.FullRebuild;

    internal static void Publish(EditorSession session)
    {
        if (session?.ToolMode != EditorToolMode.Room || session.RoomSettings == null)
        {
            Clear();
            return;
        }

        // Opaque legacy/third-party writers deliberately do not provide semantic hints. Mark their
        // Room revision and force the safe full-capture path rather than assuming what they changed.
        if (EditorRevisionHub.RequiresLiveWorkspaceRefresh(session))
        {
            EditorRevisionHub.Mark(session, EditorRevisionKind.Room);
            RoomPresentationChangeHintHub.MarkFull(session);
        }

        long revision = EditorRevisionHub.Get(session, EditorRevisionKind.Room);
        bool sameContext =
            ReferenceEquals(observedSession, session) &&
            ReferenceEquals(observedSettings, session.RoomSettings) &&
            current.Available;

        if (sameContext && observedRevision == revision)
        {
            LastOutcome = DevToolPresentationOutcome.CacheHit;
            return;
        }

        RoomPresentationChangeHint hint = RoomPresentationChangeHintHub.Consume(session);
        bool partial = sameContext && hint.HasChanges && !hint.Full;
        current = partial
            ? RoomSettingsPresentation.Capture(session, current, hint)
            : RoomSettingsPresentation.Capture(session);
        LastOutcome = partial
            ? DevToolPresentationOutcome.PartialRebuild
            : DevToolPresentationOutcome.FullRebuild;

        observedSession = session;
        observedSettings = session.RoomSettings;
        observedRevision = revision;
    }

    internal static void Clear()
    {
        RoomPresentationChangeHintHub.Clear(observedSession);
        current = EditorRoomSettingsSnapshot.Empty;
        observedSession = null;
        observedSettings = null;
        observedRevision = 0L;
        LastOutcome = DevToolPresentationOutcome.FullRebuild;
    }
}

internal static class RoomEditorCommandProcessor
{




    internal static void Process(EditorSession session)
    {
        if (session == null) return;

        long historyBeforeBatch = session.History.Revision;
        bool nonHistoryDirty = false;
        for (int processed = 0;
             processed < RoomEditorCommandQueue.MaxCommandsPerFrame &&
             RoomEditorCommandQueue.TryDequeue(out RoomEditorCommand command);
             processed++)
        {
            try
            {
                long historyBeforeCommand = session.History.Revision;
                bool changed = false;
                switch (command.Kind)
                {
                    case RoomEditorCommandKind.SetSetting:
                        changed = RoomEditorActions.SetRoomSetting(session, command.Key, command.Value);
                        break;
                    case RoomEditorCommandKind.ResetSetting:
                        changed = RoomEditorActions.ResetRoomSetting(session, command.Key);
                        break;
                    case RoomEditorCommandKind.SetPaletteFade:
                        changed = RoomEditorActions.SetPaletteFade(session, command.Index, command.Value.X);
                        break;
                    case RoomEditorCommandKind.SetTerrainPaletteFade:
                        changed = RoomEditorActions.SetTerrainPaletteFade(session, command.Index, command.Value.X);
                        break;
                    case RoomEditorCommandKind.SetTemplate:
                        changed = RoomEditorActions.SetRoomTemplate(session, command.Key);
                        if (changed)
                            RoomEffectLiveCompatibility.Reconcile(session);
                        break;
                    case RoomEditorCommandKind.SaveAsTemplate:
                        changed = RoomEditorActions.SaveRoomAsTemplate(session, command.Key);
                        break;
                    case RoomEditorCommandKind.AddEffect:
                        changed = RoomEditorActions.AddRoomEffect(session, command.Key);
                        if (changed)
                            RoomEffectLiveCompatibility.Reconcile(session);
                        break;
                    case RoomEditorCommandKind.DeleteEffect:
                        changed = RoomEditorActions.DeleteRoomEffect(session, command.Index);
                        if (changed)
                            RoomEffectLiveCompatibility.Reconcile(session);
                        break;
                    case RoomEditorCommandKind.SetEffectAmount:
                        changed = RoomEditorActions.SetRoomEffectAmount(session, command.Index, command.SecondaryIndex, command.Value.X);
                        if (changed)
                            RoomEffectLiveCompatibility.Reconcile(session);
                        break;
                }

                if (!changed) continue;

                MarkPresentationChange(session, command);
                if (session.History.Revision == historyBeforeCommand)
                    nonHistoryDirty = true;
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool room command failed: " + error.Message);
            }
        }

        // Normal model edits push document history; CorePresentation converts that one history edge
        // into Shell + active-workspace invalidation for the entire batch. Operations such as saving
        // a template can update visible Room catalogs without history, so preserve one direct Room
        // revision only when the whole batch stayed outside history.
        int rejected = RoomEditorCommandQueue.ConsumeRejectedCount();
        if (rejected > 0)
            EditorDiagnostics.Warning(
                "Room editor command inbox refused " + rejected +
                " edit(s); pending=" + RoomEditorCommandQueue.PendingCount);

        if (nonHistoryDirty && session.History.Revision == historyBeforeBatch)
            EditorRevisionHub.Mark(session, EditorRevisionKind.Room);
    }

    private static void MarkPresentationChange(EditorSession session, RoomEditorCommand command)
    {
        switch (command.Kind)
        {
            case RoomEditorCommandKind.SetSetting:
            case RoomEditorCommandKind.ResetSetting:
                RoomPresentationChangeHintHub.MarkSetting(session, command.Key);
                break;
            case RoomEditorCommandKind.SetPaletteFade:
                RoomPresentationChangeHintHub.MarkPaletteFade(session, terrain: false);
                break;
            case RoomEditorCommandKind.SetTerrainPaletteFade:
                RoomPresentationChangeHintHub.MarkPaletteFade(session, terrain: true);
                break;
            case RoomEditorCommandKind.SetEffectAmount:
                RoomPresentationChangeHintHub.MarkEffectAmount(session, command.Index);
                break;
            case RoomEditorCommandKind.AddEffect:
            case RoomEditorCommandKind.DeleteEffect:
                RoomPresentationChangeHintHub.MarkEffects(session);
                break;
            case RoomEditorCommandKind.SetTemplate:
            case RoomEditorCommandKind.SaveAsTemplate:
                RoomPresentationChangeHintHub.MarkFull(session);
                break;
        }
    }

    internal static void Clear()
    {
        RoomEditorCommandQueue.Clear();
    }
}
