using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Objects;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Sound;

/// <summary>
/// Canonical Rain World room-sound defaults. These mirror DefaultRoomSettings:
/// BkgDroneVolume = 0.3 and BkgDroneNoThreatVolume = 1.0.
/// </summary>
internal static class SoundEditorDefaults
{
    internal const float BackgroundDroneVolume = 0.3f;
    internal const float NoThreatDroneVolume = 1f;
}

internal sealed class SoundEditorState
{
    internal int SelectedIndex = -1;
    internal long Revision = 1L;

    internal bool SetSelectedIndex(int value)
    {
        if (SelectedIndex == value) return false;
        SelectedIndex = value;
        Revision = Revision >= long.MaxValue ? 1L : Revision + 1L;
        return true;
    }
}

internal static class SoundEditorStateHub
{
    private static ConditionalWeakTable<EditorSession, SoundEditorState> states = new();

    internal static SoundEditorState Get(EditorSession session) =>
        session == null ? null : states.GetValue(session, _ => new SoundEditorState());

    internal static void Reset() => states = new ConditionalWeakTable<EditorSession, SoundEditorState>();
}

public static class SoundEditorPresentationHub
{
    private static EditorSoundPresentationSnapshot current
    {
        get => EditorSoundPresentationState.Current;
        set => EditorSoundPresentationState.Publish(value);
    }
    private static EditorSession observedSession;
    private static global::RoomSettings observedSettings;
    private static string[] observedFileNames;
    private static long observedRevision;
    private static long observedSelectionRevision;
    private static int observedSoundCount = -1;
    private static int observedSelectedIndex = int.MinValue;

    public static EditorSoundPresentationSnapshot Current => current;

    internal static void Publish(EditorSession session)
    {
        if (session?.ToolMode != EditorToolMode.Sound || session.RoomSettings?.ambientSounds == null)
        {
            Clear();
            return;
        }

        SoundEditorState state = SoundEditorStateHub.Get(session);
        int count = session.RoomSettings.ambientSounds.Count;
        if (state.SelectedIndex >= count) state.SetSelectedIndex(count - 1);
        if (state.SelectedIndex < -1) state.SetSelectedIndex(-1);

        if (EditorRevisionHub.RequiresLiveWorkspaceRefresh(session))
        {
            EditorRevisionHub.Mark(session, EditorRevisionKind.Sound);
            SoundPresentationChangeHintHub.MarkFull(session);
        }

        long revision = EditorRevisionHub.Get(session, EditorRevisionKind.Sound);
        long selectionRevision = state.Revision;
        string[] fileNames = SoundFileNameCatalog.CurrentNames ?? Array.Empty<string>();

        bool sameIdentity =
            ReferenceEquals(observedSession, session) &&
            ReferenceEquals(observedSettings, session.RoomSettings) &&
            current.Available;
        bool resourceCatalogStable =
            sameIdentity &&
            ReferenceEquals(observedFileNames, fileNames) &&
            current.SampleEntries != null &&
            current.Samples != null;
        bool modelStable =
            resourceCatalogStable &&
            observedRevision == revision &&
            observedSoundCount == count;

        if (modelStable &&
            observedSelectionRevision == selectionRevision &&
            observedSelectedIndex == state.SelectedIndex)
        {
            DevToolPerformanceMonitor.RecordPresentation(
                DevToolPresentationChannel.Sound,
                DevToolPresentationOutcome.CacheHit);
            return;
        }

        if (modelStable)
        {
            PublishSelectionOnly(state.SelectedIndex, selectionRevision);
            DevToolPerformanceMonitor.RecordPresentation(
                DevToolPresentationChannel.Sound,
                DevToolPresentationOutcome.PartialRebuild);
            return;
        }

        SoundPresentationChangeHint hint = SoundPresentationChangeHintHub.Consume(session);
        if (resourceCatalogStable && hint.HasChanges && !hint.Full)
        {
            if (PublishSemanticPartial(session, state, count, revision, selectionRevision, hint))
            {
                DevToolPerformanceMonitor.RecordPresentation(
                    DevToolPresentationChannel.Sound,
                    DevToolPresentationOutcome.PartialRebuild);
                return;
            }
        }

        EditorSoundSampleSnapshot[] sampleEntries;
        string[] samples;
        if (resourceCatalogStable)
        {
            sampleEntries = current.SampleEntries;
            samples = current.Samples;
        }
        else if (SoundFileNameCatalog.IsReady && SoundSampleCatalog.IsReadyForNames(fileNames))
        {
            sampleEntries = NativeSoundResourceSnapshot.Capture(fileNames);
            samples = new string[sampleEntries.Length];
            for (int i = 0; i < sampleEntries.Length; i++)
                samples[i] = sampleEntries[i].Sample;
        }
        else
        {
            sampleEntries = Array.Empty<EditorSoundSampleSnapshot>();
            samples = Array.Empty<string>();
        }

        SoundGroupLibrary.EnsureLoaded();
        EditorSoundSnapshot[] sounds = CaptureAllSounds(session, state.SelectedIndex);

        current = new EditorSoundPresentationSnapshot
        {
            Available = true,
            RoomKey = session.Room?.abstractRoom?.name ?? string.Empty,
            BackgroundDroneVolume = session.RoomSettings.BkgDroneVolume,
            NoThreatDroneVolume = session.RoomSettings.BkgDroneNoThreatVolume,
            Samples = samples,
            SampleEntries = sampleEntries,
            Sounds = sounds,
            SelectedIndex = state.SelectedIndex
        };

        Observe(session, fileNames, revision, selectionRevision, count, state.SelectedIndex);

        DevToolPerformanceMonitor.RecordPresentation(
            DevToolPresentationChannel.Sound,
            resourceCatalogStable
                ? DevToolPresentationOutcome.PartialRebuild
                : DevToolPresentationOutcome.FullRebuild);
    }

    private static bool PublishSemanticPartial(
        EditorSession session,
        SoundEditorState state,
        int count,
        long revision,
        long selectionRevision,
        SoundPresentationChangeHint hint)
    {
        EditorSoundSnapshot[] source = current.Sounds ?? Array.Empty<EditorSoundSnapshot>();
        if (!hint.Collection && source.Length != count)
            return false;

        EditorSoundSnapshot[] sounds;
        if (hint.Collection || hint.AllMembers)
        {
            sounds = CaptureAllSounds(session, state.SelectedIndex);
        }
        else if (hint.MemberIndex >= 0)
        {
            if (hint.MemberIndex >= count || hint.MemberIndex >= source.Length)
                return false;

            sounds = (EditorSoundSnapshot[])source.Clone();
            sounds[hint.MemberIndex] = CaptureSound(session, hint.MemberIndex, state.SelectedIndex);
            sounds = EditorSoundPresentationState.ApplySelection(sounds, state.SelectedIndex);
        }
        else
        {
            sounds = EditorSoundPresentationState.ApplySelection(source, state.SelectedIndex);
        }

        current = new EditorSoundPresentationSnapshot
        {
            Available = true,
            RoomKey = current.RoomKey,
            BackgroundDroneVolume = hint.RoomValues
                ? session.RoomSettings.BkgDroneVolume
                : current.BackgroundDroneVolume,
            NoThreatDroneVolume = hint.RoomValues
                ? session.RoomSettings.BkgDroneNoThreatVolume
                : current.NoThreatDroneVolume,
            Samples = current.Samples,
            SampleEntries = current.SampleEntries,
            Sounds = sounds,
            SelectedIndex = state.SelectedIndex
        };

        Observe(
            session,
            SoundFileNameCatalog.CurrentNames ?? Array.Empty<string>(),
            revision,
            selectionRevision,
            count,
            state.SelectedIndex);
        return true;
    }

    private static EditorSoundSnapshot[] CaptureAllSounds(EditorSession session, int selectedIndex)
    {
        int count = session?.RoomSettings?.ambientSounds?.Count ?? 0;
        if (count == 0) return Array.Empty<EditorSoundSnapshot>();

        EditorSoundSnapshot[] sounds = new EditorSoundSnapshot[count];
        for (int i = 0; i < count; i++)
            sounds[i] = CaptureSound(session, i, selectedIndex);
        return sounds;
    }

    private static EditorSoundSnapshot CaptureSound(EditorSession session, int index, int selectedIndex)
    {
        AmbientSound sound = session?.RoomSettings?.ambientSounds != null &&
                             index >= 0 && index < session.RoomSettings.ambientSounds.Count
            ? session.RoomSettings.ambientSounds[index]
            : null;
        float doppler = sound is DopplerAffectedSound dopplerSound ? dopplerSound.dopplerFac : 0f;
        float taper = sound is SpotSound spotForTaper ? spotForTaper.taper : 0f;
        Vector2 pos = sound is SpotSound spot ? spot.pos : Vector2.zero;
        float radius = sound is SpotSound spotForRadius ? spotForRadius.rad : 0f;
        Vector2 direction = sound is DirectionalSound directional ? directional.direction : Vector2.zero;
        EditorSoundSampleSnapshot resource = SoundSampleCatalog.Resolve(sound?.sample ?? string.Empty);

        return new EditorSoundSnapshot
        {
            Index = index,
            Type = sound?.type?.value ?? string.Empty,
            Sample = sound?.sample ?? string.Empty,
            Inherited = sound?.inherited ?? false,
            OverWrite = sound?.overWrite ?? false,
            Volume = sound?.volume ?? 0f,
            Pitch = sound?.pitch ?? 1f,
            Doppler = doppler,
            Taper = taper,
            X = pos.x,
            Y = pos.y,
            Radius = radius,
            DirectionX = direction.x,
            DirectionY = direction.y,
            Selected = index == selectedIndex,
            ResourceAvailable = resource.Available,
            ResourceSourceKind = resource.SourceKind,
            ResourceSourceName = resource.SourceName,
            ResourceSourceId = resource.SourceId
        };
    }

    private static void PublishSelectionOnly(int selectedIndex, long selectionRevision)
    {
        current = EditorSoundPresentationState.WithSelection(current, selectedIndex);
        observedSelectionRevision = selectionRevision;
        observedSelectedIndex = selectedIndex;
    }

    private static void Observe(
        EditorSession session,
        string[] fileNames,
        long revision,
        long selectionRevision,
        int count,
        int selectedIndex)
    {
        observedSession = session;
        observedSettings = session?.RoomSettings;
        observedFileNames = fileNames;
        observedRevision = revision;
        observedSelectionRevision = selectionRevision;
        observedSoundCount = count;
        observedSelectedIndex = selectedIndex;
    }

    internal static void Clear()
    {
        SoundPresentationChangeHintHub.Clear(observedSession);
        current = EditorSoundPresentationSnapshot.Empty;
        observedSession = null;
        observedSettings = null;
        observedFileNames = null;
        observedRevision = 0L;
        observedSelectionRevision = 0L;
        observedSoundCount = -1;
        observedSelectedIndex = int.MinValue;
    }
}

internal static class SoundEditorCommandProcessor
{
    internal static void Process(EditorSession session)
    {
        long historyBeforeBatch = session?.History.Revision ?? 0L;
        bool nonHistorySceneDirty = false;

        for (int processed = 0;
             processed < SoundEditorCommandQueue.MaxCommandsPerFrame &&
             SoundEditorCommandQueue.TryDequeue(out SoundEditorCommand command);
             processed++)
        {
            try
            {
                long historyBeforeCommand = session?.History.Revision ?? 0L;
                SoundEditorState state = SoundEditorStateHub.Get(session);
                int selectedBefore = state?.SelectedIndex ?? -1;
                int soundCountBefore = session?.RoomSettings?.ambientSounds?.Count ?? 0;
                bool changed = false;
                bool sceneCommand = false;

                switch (command.Kind)
                {
                    case SoundEditorCommandKind.Select:
                        SoundEditorActions.Select(session, command.Index);
                        changed = (state?.SelectedIndex ?? -1) != selectedBefore;
                        break;
                    case SoundEditorCommandKind.Create:
                        changed = SoundEditorActions.Create(session, command.Text, command.SecondaryIndex);
                        sceneCommand = true;
                        break;
                    case SoundEditorCommandKind.CreateFromLibrary:
                        changed = SoundEditorActions.CreateFromLibrary(
                            session,
                            command.Text,
                            command.SecondaryIndex,
                            command.Key,
                            command.Index);
                        sceneCommand = command.Index != 1;
                        break;
                    case SoundEditorCommandKind.Delete:
                        changed = SoundEditorActions.Delete(session, command.Index);
                        sceneCommand = true;
                        break;
                    case SoundEditorCommandKind.SetRoomValue:
                        changed = SoundEditorActions.SetRoomValue(session, command.Key, command.Value);
                        sceneCommand = true;
                        break;
                    case SoundEditorCommandKind.SetSoundValue:
                        changed = SoundEditorActions.SetSoundValue(session, command.Index, command.Key, command.Value);
                        sceneCommand = true;
                        break;
                    case SoundEditorCommandKind.ReloadGroups:
                        SoundGroupLibrary.Reload();
                        break;
                    case SoundEditorCommandKind.SetGroupDirectory:
                        SoundGroupLibrary.SetLocalDirectory(command.Text);
                        break;
                    case SoundEditorCommandKind.ResetGroupDirectory:
                        SoundGroupLibrary.ResetLocalDirectory();
                        break;
                    case SoundEditorCommandKind.CreateGroup:
                        SoundGroupLibrary.CreateLocalGroup(command.Key, command.Text);
                        break;
                    case SoundEditorCommandKind.DeleteGroup:
                        SoundGroupLibrary.DeleteGroup(command.Key, command.Text);
                        break;
                    case SoundEditorCommandKind.AddSoundToGroup:
                        SoundEditorActions.AddSoundToGroup(session, command.Index, command.Key);
                        break;
                    case SoundEditorCommandKind.AddSoundsToGroup:
                        SoundEditorActions.AddSoundsToGroup(session, command.Indices, command.Key);
                        break;
                    case SoundEditorCommandKind.CreateGroupFromSounds:
                        SoundEditorActions.CreateGroupFromSounds(
                            session,
                            command.Indices,
                            command.Key,
                            command.Text);
                        break;
                    case SoundEditorCommandKind.ApplyGroup:
                        changed = SoundEditorActions.ApplyGroup(session, command.Key);
                        sceneCommand = true;
                        break;
                }

                if (!sceneCommand)
                    continue;

                int soundCountAfter = session?.RoomSettings?.ambientSounds?.Count ?? 0;
                long historyAfterCommand = session?.History.Revision ?? 0L;
                bool historyChanged = historyAfterCommand != historyBeforeCommand;
                bool membershipChanged = soundCountAfter != soundCountBefore;
                bool directSceneMutation =
                    changed && command.Kind != SoundEditorCommandKind.CreateFromLibrary;
                bool observableModelChange = historyChanged || membershipChanged || directSceneMutation;
                if (!observableModelChange)
                    continue;

                MarkPresentationChange(session, command, membershipChanged);
                if (!historyChanged)
                    nonHistorySceneDirty = true;
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool sound command failed: " + error.Message);
            }
        }

        int rejected = SoundEditorCommandQueue.ConsumeRejectedCount();
        if (rejected > 0)
            EditorDiagnostics.Warning("Sound editor command inbox rejected " + rejected +
                " edit(s); pending=" + SoundEditorCommandQueue.PendingCount);

        if (nonHistorySceneDirty && (session?.History.Revision ?? 0L) == historyBeforeBatch)
            EditorRevisionHub.Mark(session, EditorRevisionKind.Sound);
    }

    private static void MarkPresentationChange(
        EditorSession session,
        SoundEditorCommand command,
        bool membershipChanged)
    {
        switch (command.Kind)
        {
            case SoundEditorCommandKind.SetRoomValue:
                SoundPresentationChangeHintHub.MarkRoomValues(session);
                break;
            case SoundEditorCommandKind.SetSoundValue:
                SoundPresentationChangeHintHub.MarkMember(session, command.Index);
                break;
            case SoundEditorCommandKind.Create:
            case SoundEditorCommandKind.Delete:
            case SoundEditorCommandKind.ApplyGroup:
                SoundPresentationChangeHintHub.MarkCollection(session);
                break;
            case SoundEditorCommandKind.CreateFromLibrary:
                if (membershipChanged)
                    SoundPresentationChangeHintHub.MarkCollection(session);
                break;
            default:
                SoundPresentationChangeHintHub.MarkFull(session);
                break;
        }
    }

    internal static void Clear() => SoundEditorCommandQueue.Clear();
}
