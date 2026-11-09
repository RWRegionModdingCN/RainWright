using System;
using System.Threading;
using DryCycle.DevUI.DevTool.Extensions;
using DryCycle.DevUI.DevTool.Core;
using DryCycle.DevUI.DevTool.Map;
using ImGuiNET;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Backend-neutral entry point for optional World-data editors. DryCycle weather and
/// temperature widgets are installed by an optional integration, not by this page.
/// </summary>
internal static class WorldWorkspaceDataView
{
    private sealed class Renderer
    {
        internal readonly Action<EditorMapPresentationSnapshot> Draw;
        internal readonly Func<bool> Dirty;
        internal readonly Action<Exception> Report;
        internal Renderer(Action<EditorMapPresentationSnapshot> draw,
            Func<bool> dirty, Action<Exception> report)
        {
            Draw = draw;
            Dirty = dirty;
            Report = report;
        }
    }

    private sealed class Lease : IDisposable
    {
        private Renderer renderer;
        internal Lease(Renderer renderer) => this.renderer = renderer;

        public void Dispose()
        {
            Renderer previous = Interlocked.Exchange(ref renderer, null);
            if (previous == null) return;
            lock (Sync)
            {
                if (ReferenceEquals(registered, previous))
                {
                    Volatile.Write(ref registered, null);
                    Volatile.Write(ref active, null);
                    errorText = string.Empty;
                }
            }
        }
    }

    private static readonly object Sync = new();
    private static Renderer active;
    private static Renderer registered;
    private static string errorText = string.Empty;

    // The optional DryCycle plugin owns this lease and has a hard BepInEx dependency on
    // DryCycle. Keep a faulted registration visible so its error remains accessible.
    internal static bool Available => Volatile.Read(ref registered) != null;

    internal static IDisposable Register(Action<EditorMapPresentationSnapshot> draw,
        Func<bool> dirty, Action<Exception> report)
    {
        if (draw == null) throw new ArgumentNullException(nameof(draw));
        if (dirty == null) throw new ArgumentNullException(nameof(dirty));
        Renderer renderer = new(draw, dirty, report);
        lock (Sync)
        {
            if (registered != null)
                throw new InvalidOperationException("World data editor renderer is already registered.");
            errorText = string.Empty;
            Volatile.Write(ref active, renderer);
            Volatile.Write(ref registered, renderer);
        }
        return new Lease(renderer);
    }

    internal static bool HasDirtyData
    {
        get
        {
            // Author-data ownership is independent of the active widget renderer.
            if (EditorAuthoringParticipants.HasDirty) return true;
            Renderer renderer = Volatile.Read(ref active);
            if (renderer == null) return false;
            try { return renderer.Dirty(); }
            catch (Exception error)
            {
                DetachFaulted(renderer, error);
                return true; // A broken renderer must not mark unsaved author data clean.
            }
        }
    }

    internal static bool SaveDirty() => EditorAuthoringParticipants.SaveDirty();

    internal static void Draw(EditorMapPresentationSnapshot snapshot)
    {
        Renderer renderer = Volatile.Read(ref active);
        if (renderer == null)
        {
            if (!string.IsNullOrEmpty(errorText))
                ImGui.TextDisabled(errorText);
            else
                ImGui.TextDisabled(DevToolUiSettings.T(
                    "当前未安装可用的扩展世界数据编辑器。",
                    "No optional World-data editor is installed."));
            return;
        }

        try { renderer.Draw(snapshot); }
        catch (Exception error)
        {
            DetachFaulted(renderer, error);
            ImGui.TextDisabled(DevToolUiSettings.T(
                "扩展世界数据编辑器发生错误，已安全停用；数据保存仍由作者数据服务处理。",
                "Optional World-data editor failed and was detached; author data is still saved separately."));
        }
    }

    private static void DetachFaulted(Renderer renderer, Exception error)
    {
        lock (Sync)
        {
            if (ReferenceEquals(active, renderer))
            {
                Volatile.Write(ref active, null);
                errorText = "World-data editor failed: " + error.Message;
            }
        }
        try { renderer.Report?.Invoke(error); }
        catch { /* diagnostics are untrusted, core editor remains usable */ }
    }
}
