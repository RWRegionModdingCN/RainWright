using System;
using System.Collections.Concurrent;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.History;
using UnityEngine;

namespace DryCycle.DevUI.DevTool.Compatibility;

public static class UniversalDevUiPresentationHub
{
    private const int RefreshIntervalFrames = 4;
    private static volatile UniversalDevUiPresentationSnapshot current = UniversalDevUiPresentationSnapshot.Empty;
    private static Page lastPage;
    private static int lastCaptureFrame = int.MinValue / 2;

    /// <summary>
    /// Pure O(1) snapshot read for the RWImGui frontend. Backend capture/publication is owned by
    /// DevToolSubsystemCoordinator and occurs only while compatibility diagnostics are enabled.
    /// </summary>
    public static UniversalDevUiPresentationSnapshot Current => current;

    internal static void Publish(global::DevInterface.DevUI owner)
    {
        Page page = owner?.activePage;
        if (page == null)
        {
            Clear();
            return;
        }

        int frame = Time.frameCount;
        bool pageChanged = !ReferenceEquals(page, lastPage);
        if (!pageChanged && frame - lastCaptureFrame < RefreshIntervalFrames)
            return;

        lastPage = page;
        lastCaptureFrame = frame;

        try
        {
            LegacyControlSnapshot[] controls = LegacyDevInterfaceBridge.CaptureRoot(page);
            DevUiMigrationCoverageSnapshot coverage = DevUiMigrationCoverage.CurrentPage;
            string pageType = page.GetType().FullName ?? page.GetType().Name;
            int unmapped = coverage != null && string.Equals(coverage.PageType, pageType, StringComparison.Ordinal)
                ? coverage.UnmappedTypeCount
                : 0;

            current = new UniversalDevUiPresentationSnapshot
            {
                Available = true,
                PageType = pageType,
                PageId = page.IDstring ?? string.Empty,
                Controls = controls ?? Array.Empty<LegacyControlSnapshot>(),
                UnmappedProtocolCount = unmapped
            };
        }
        catch (Exception error)
        {
            global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool universal DevUI mirror capture failed: " + error.Message);
            current = new UniversalDevUiPresentationSnapshot
            {
                Available = true,
                PageType = page.GetType().FullName ?? page.GetType().Name,
                PageId = page.IDstring ?? string.Empty,
                Controls = Array.Empty<LegacyControlSnapshot>(),
                UnmappedProtocolCount = DevUiMigrationCoverage.CurrentPage?.UnmappedTypeCount ?? 0
            };
        }
    }

    internal static void Invalidate() => lastCaptureFrame = int.MinValue / 2;

    internal static void Clear()
    {
        current = UniversalDevUiPresentationSnapshot.Empty;
        lastPage = null;
        lastCaptureFrame = int.MinValue / 2;
    }
}

internal static class UniversalDevUiCommandProcessor
{
    internal static void Process(EditorSession session)
    {
        if (session?.Owner == null) return;

        // The native renderer is a concurrent producer. Keep the game's
        // legacy reflection/Undo work within one frame's execution budget.
        for (int processed = 0;
             processed < UniversalDevUiCommandQueue.MaxCommandsPerFrame &&
             UniversalDevUiCommandQueue.TryDequeue(out UniversalDevUiCommand command);
             processed++)
        {
            try
            {
                ExecuteWithHistory(session, command);
            }
            catch (Exception error)
            {
                EditorDiagnostics.Warning(
                    "DevTool universal DevUI command failed (" + command.Kind + " @ " + command.Path + "): " +
                    error.Message);
            }
        }

        int rejected = UniversalDevUiCommandQueue.ConsumeRejectedCount();
        if (rejected > 0)
            EditorDiagnostics.Warning(
                "DevTool third-party DevUI command inbox rejected " + rejected +
                " command(s) while full; remaining queued: " +
                UniversalDevUiCommandQueue.PendingCount);
    }

    internal static void Clear()
    {
        UniversalDevUiCommandQueue.Clear();
        UniversalDevUiPresentationHub.Invalidate();
    }

    private static bool ExecuteWithHistory(EditorSession session, UniversalDevUiCommand command)
    {
        IEditorStateSnapshot before = LegacySnapshotFactory.CaptureForPointer(session);
        bool changed = Execute(session.Owner, command);
        if (!changed) return false;

        if (before != null)
        {
            IEditorStateSnapshot after = before.CaptureCurrent(session);
            if (SnapshotHistoryEntry.TryCreate(Label(command), before, after, out SnapshotHistoryEntry entry))
                session.History.Push(entry);
        }

        UniversalDevUiPresentationHub.Invalidate();
        return true;
    }

    private static bool Execute(global::DevInterface.DevUI owner, UniversalDevUiCommand command)
    {
        return command.Kind switch
        {
            UniversalDevUiCommandKind.Click => UniversalDevUiActionBridge.Click(owner, command.Path),
            UniversalDevUiCommandKind.SetBoolean => UniversalDevUiActionBridge.SetBoolean(owner, command.Path, command.Boolean),
            UniversalDevUiCommandKind.SetSlider => UniversalDevUiActionBridge.SetSlider(owner, command.Path, command.X),
            UniversalDevUiCommandKind.ResetSlider => UniversalDevUiActionBridge.ResetSlider(owner, command.Path),
            UniversalDevUiCommandKind.SetChoice => UniversalDevUiActionBridge.SetChoice(owner, command.Path, command.Integer),
            UniversalDevUiCommandKind.IncrementInteger => UniversalDevUiActionBridge.IncrementInteger(owner, command.Path, command.Integer),
            UniversalDevUiCommandKind.SetText => UniversalDevUiActionBridge.SetText(owner, command.Path, command.Text),
            UniversalDevUiCommandKind.SetDirection => UniversalDevUiActionBridge.SetDirection(owner, command.Path, command.X, command.Y),
            UniversalDevUiCommandKind.SetColor => UniversalDevUiActionBridge.SetColor(owner, command.Path, command.X, command.Y, command.Z, command.W),
            _ => false
        };
    }

    private static string Label(UniversalDevUiCommand command) => command.Kind switch
    {
        UniversalDevUiCommandKind.Click => "DevUI action",
        UniversalDevUiCommandKind.SetBoolean => "DevUI toggle",
        UniversalDevUiCommandKind.SetSlider => "DevUI slider",
        UniversalDevUiCommandKind.ResetSlider => "DevUI slider reset",
        UniversalDevUiCommandKind.SetChoice => "DevUI choice",
        UniversalDevUiCommandKind.IncrementInteger => "DevUI integer",
        UniversalDevUiCommandKind.SetText => "DevUI text",
        UniversalDevUiCommandKind.SetDirection => "DevUI direction",
        UniversalDevUiCommandKind.SetColor => "DevUI color",
        _ => "DevUI edit"
    };
}
