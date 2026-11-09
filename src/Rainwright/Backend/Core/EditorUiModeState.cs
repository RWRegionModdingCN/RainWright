using System.Threading;

namespace DryCycle.DevUI.DevTool.Core;

/// <summary>
/// Global DevTool presentation preference. This state is intentionally independent from
/// room/document history: changing UI presentation must never alter editor data, selection,
/// save state or Undo/Redo stacks.
/// </summary>
public static class EditorUiModeState
{
    private static volatile bool useVanilla;
    private static volatile bool overlayHidden;
    private static volatile bool topStatusHidden;
    private static int presentationRevision;

    public static int PresentationRevision => Volatile.Read(ref presentationRevision);

    /// <summary>
    /// True when the original Rain World DevInterface is the primary UI.
    /// The rebuilt RWImGui frontend draws no editor windows in this mode.
    /// </summary>
    public static bool UseVanilla => useVanilla;

    /// <summary>
    /// Temporary visibility gate used by Escape. This is presentation-only state so opening
    /// Warp Menu does not alter the active editor document, selection or history.
    /// </summary>
    public static bool OverlayHidden => overlayHidden;

    /// <summary>Presentation-only visibility of the rebuilt global status bar.</summary>
    public static bool TopStatusHidden => topStatusHidden;

    public static void ToggleTopStatus()
    {
        Interlocked.Increment(ref presentationRevision);
        topStatusHidden = !topStatusHidden;
    }

    public static void SetVanilla(bool value)
    {
        // This setter is called from both Rain World's main thread (shortcuts) and RWImGui's render
        // callback. Keep it strictly presentation-state-only. Main-thread revision invalidation for
        // Vanilla -> New UI hand-off is observed later by EditorRevisionHub from DevUI.Update.
        if (useVanilla != value)
        {
            // Invalidate old input claims before publishing the new owner. Returning to New UI
            // must never revive a text/drag capture from before the Vanilla interval.
            Interlocked.Increment(ref presentationRevision);
            useVanilla = value;
        }

        // Preserve the original contract: requesting New UI also clears a temporary Escape/pause
        // hide even when New UI was already selected.
        if (!value)
            SetOverlayHidden(false);
    }

    public static void SetOverlayHidden(bool value)
    {
        if (overlayHidden == value) return;
        Interlocked.Increment(ref presentationRevision);
        overlayHidden = value;
    }

    public static void ToggleOverlayHidden()
    {
        SetOverlayHidden(!overlayHidden);
    }
}
