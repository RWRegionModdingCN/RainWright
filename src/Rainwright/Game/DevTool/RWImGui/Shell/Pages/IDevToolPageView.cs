using System;
using DryCycle.DevUI.DevTool.Core;
using Num = System.Numerics;

namespace DryCycle.DevUI.DevTool.RWImGui;

/// <summary>
/// Rendering contract for one DevTool page.
///
/// This contract owns presentation only. Identity, lifecycle and retained-state ownership live on
/// IDevToolPage, keeping frontend drawing concerns separate from editor-page identity.
/// Navigation metadata, shared-surface capabilities and session status live here so shared chrome
/// does not know concrete page implementations or read page-specific PresentationHub objects.
/// </summary>
internal interface IDevToolPageView
{
    int NavigationOrder { get; }
    string NavigationLabel { get; }
    string NavigationTooltip { get; }
    bool UsesDedicatedWorkspace { get; }
    bool SupportsSceneSurface { get; }
    bool SupportsPlacementInput { get; }
    bool HasTopControls { get; }
    bool IsSceneSurfaceVisible(EditorPresentationSnapshot snapshot);

    string GetSessionStatus(EditorPresentationSnapshot snapshot);
    bool SuppressInspector(EditorPresentationSnapshot snapshot);
    void DrawBackground(EditorPresentationSnapshot snapshot, Num.Vector2 display);
    void DrawTopControls(EditorPresentationSnapshot snapshot);
    void DrawBrowser(EditorPresentationSnapshot snapshot);
    void DrawInspector(EditorPresentationSnapshot snapshot);
    void DrawWorkspace(EditorPresentationSnapshot snapshot, Num.Vector2 display);
    void DrawSceneWorkspace(EditorPresentationSnapshot snapshot);
}

/// <summary>
/// Composite contract used by the RWImGui registry. One object is the authoritative owner for both
/// page lifecycle and page rendering; the registry never has to keep parallel Page/PageView maps.
/// </summary>
internal interface IDevToolFrontendPage : IDevToolPage, IDevToolPageView
{
}

/// <summary>
/// Lifecycle-safe base class for built-in and future frontend pages.
///
/// Activate/Deactivate are idempotent. Activation rolls back its active flag when page-specific
/// startup fails, while deactivation marks the page inactive before invoking cleanup so a throwing
/// cleanup cannot leave the registry believing the old page is still active.
/// </summary>
internal abstract class DevToolFrontendPageBase : IDevToolFrontendPage
{
    private bool active;
    private bool statusProjectionValid;
    private bool statusProjectionChinese;
    private DevToolPageStatusState projectedStatus;
    private string projectedStatusText = string.Empty;

    public abstract string Id { get; }
    public abstract EditorToolMode Mode { get; }
    public virtual int NavigationOrder => int.MaxValue;
    public virtual string NavigationLabel => Id;
    public virtual string NavigationTooltip => NavigationLabel;
    public virtual bool UsesDedicatedWorkspace => false;
    public virtual bool SupportsSceneSurface => false;
    public virtual bool SupportsPlacementInput => false;
    public virtual bool HasTopControls => false;
    public virtual bool IsSceneSurfaceVisible(EditorPresentationSnapshot snapshot) =>
        SupportsSceneSurface;

    public string GetSessionStatus(EditorPresentationSnapshot snapshot)
    {
        DevToolPageStatusState state = BuildSessionStatusState(snapshot);
        bool chinese = DevToolUiSettings.IsChinese;
        if (statusProjectionValid && statusProjectionChinese == chinese && projectedStatus.Equals(state))
            return projectedStatusText;

        statusProjectionValid = true;
        statusProjectionChinese = chinese;
        projectedStatus = state;
        projectedStatusText = FormatSessionStatus(state) ?? string.Empty;
        return projectedStatusText;
    }

    public virtual bool SuppressInspector(EditorPresentationSnapshot snapshot) => false;
    public virtual void DrawBackground(EditorPresentationSnapshot snapshot, Num.Vector2 display) { }
    public virtual void DrawTopControls(EditorPresentationSnapshot snapshot) { }
    public abstract void DrawBrowser(EditorPresentationSnapshot snapshot);
    public abstract void DrawInspector(EditorPresentationSnapshot snapshot);
    public virtual void DrawWorkspace(EditorPresentationSnapshot snapshot, Num.Vector2 display) { }
    public virtual void DrawSceneWorkspace(EditorPresentationSnapshot snapshot) { }

    public void Activate()
    {
        if (active) return;
        active = true;
        try
        {
            OnActivate();
        }
        catch
        {
            active = false;
            throw;
        }
    }

    public void Deactivate()
    {
        if (!active) return;
        active = false;
        OnDeactivate();
    }

    public void Reset()
    {
        statusProjectionValid = false;
        projectedStatusText = string.Empty;
        OnReset();
    }

    protected virtual DevToolPageStatusState BuildSessionStatusState(EditorPresentationSnapshot snapshot) =>
        new(textA: snapshot?.Document ?? string.Empty);

    protected virtual string FormatSessionStatus(DevToolPageStatusState state) => state.TextA;
    protected virtual void OnActivate() { }
    protected virtual void OnDeactivate() { }
    protected virtual void OnReset() { }
}
