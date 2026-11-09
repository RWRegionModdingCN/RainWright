using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DevInterface;
using DryCycle.DevUI.DevTool.Core;

namespace DryCycle.DevUI.DevTool.Dialog;

internal sealed class DialogEditorState
{
    internal string SelectedPath = string.Empty;
    internal bool WasNewUi;
}

internal static class DialogEditorStateHub
{
    private static ConditionalWeakTable<EditorSession, DialogEditorState> states = new();

    internal static DialogEditorState Get(EditorSession session) =>
        session == null ? null : states.GetValue(session, _ => new DialogEditorState());

    internal static void Reset() => states = new ConditionalWeakTable<EditorSession, DialogEditorState>();
}

public static class DialogEditorPresentationHub
{
    private static EditorDialogPresentationSnapshot current
    {
        get => EditorDialogPresentationState.Current;
        set => EditorDialogPresentationState.Publish(value);
    }
    private static EditorSession observedSession;
    private static DialogPage observedPage;
    private static object observedConversationLoader;
    private static string[] observedDialogPaths;
    private static long observedRevision;
    private static string observedSelectedPath = string.Empty;
    private static string observedLanguage = string.Empty;
    private static bool observedNewUi;

    public static EditorDialogPresentationSnapshot Current => current;

    internal static void Publish(EditorSession session)
    {
        if (session?.ToolMode != EditorToolMode.Dialog || session.Owner?.activePage is not DialogPage page)
        {
            Clear();
            return;
        }

        DialogEditorState state = DialogEditorStateHub.Get(session);
        bool newUi = !EditorUiModeState.UseVanilla;
        if (newUi && !state.WasNewUi)
        {
            // Vanilla DialogPage renders preview text directly through HUD DialogBox/chatlog.
            // Clear it once when the rebuilt UI takes ownership so there are never two
            // previews stacked on top of each other. ConversationLoader data itself remains.
            try { page.ClearDialogs(); }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool dialog legacy preview clear failed: " + error.Message);
            }
        }
        state.WasNewUi = newUi;

        if (page.leftBoundary != null) page.leftBoundary.isVisible = !newUi;
        if (page.rightBoundary != null) page.rightBoundary.isVisible = !newUi;

        if (EditorRevisionHub.RequiresLiveWorkspaceRefresh(session))
            EditorRevisionHub.Mark(session, EditorRevisionKind.Dialog);

        long revision = EditorRevisionHub.Get(session, EditorRevisionKind.Dialog);
        string[] dialogPaths = page.dialogPanel?.dialogPaths ?? Array.Empty<string>();
        string language = session.Owner.game?.rainWorld?.inGameTranslator?.currentLanguage?.value ?? string.Empty;
        object conversationLoader = page.convoLoader;

        if (ReferenceEquals(observedSession, session) &&
            ReferenceEquals(observedPage, page) &&
            ReferenceEquals(observedConversationLoader, conversationLoader) &&
            ReferenceEquals(observedDialogPaths, dialogPaths) &&
            observedRevision == revision &&
            string.Equals(observedSelectedPath, state.SelectedPath, StringComparison.Ordinal) &&
            string.Equals(observedLanguage, language, StringComparison.Ordinal) &&
            observedNewUi == newUi &&
            current.Available)
            return;

        string[] paths = dialogPaths.Length == 0
            ? Array.Empty<string>()
            : (string[])dialogPaths.Clone();
        Array.Sort(paths, StringComparer.OrdinalIgnoreCase);

        List<EditorDialogEventSnapshot> events = new();
        if (!string.IsNullOrEmpty(state.SelectedPath))
        {
            if (page.convoLoader?.chatlogMessages != null)
            {
                string[] messages = page.convoLoader.chatlogMessages;
                for (int i = 0; i < messages.Length; i++)
                {
                    events.Add(new EditorDialogEventSnapshot
                    {
                        Kind = EditorDialogEventKind.Chatlog,
                        Text = messages[i] ?? string.Empty
                    });
                }
            }
            else if (page.convoLoader?.events != null)
            {
                for (int i = 0; i < page.convoLoader.events.Count; i++)
                {
                    Conversation.DialogueEvent value = page.convoLoader.events[i];
                    if (value is Conversation.TextEvent text)
                    {
                        events.Add(new EditorDialogEventSnapshot
                        {
                            Kind = EditorDialogEventKind.Text,
                            InitialWait = text.initialWait,
                            Linger = text.textLinger,
                            Text = page.ReplaceParts(text.text ?? string.Empty)
                        });
                    }
                    else if (value is Conversation.WaitEvent wait)
                    {
                        events.Add(new EditorDialogEventSnapshot
                        {
                            Kind = EditorDialogEventKind.Wait,
                            InitialWait = wait.initialWait,
                            Text = "Wait"
                        });
                    }
                    else if (value is Conversation.SpecialEvent special)
                    {
                        events.Add(new EditorDialogEventSnapshot
                        {
                            Kind = EditorDialogEventKind.Special,
                            InitialWait = special.initialWait,
                            Text = special.eventName ?? string.Empty
                        });
                    }
                    else if (value != null)
                    {
                        events.Add(new EditorDialogEventSnapshot
                        {
                            Kind = EditorDialogEventKind.Unknown,
                            InitialWait = value.initialWait,
                            Text = value.GetType().Name
                        });
                    }
                }
            }
        }

        current = new EditorDialogPresentationSnapshot
        {
            Available = true,
            Language = language,
            SelectedPath = state.SelectedPath,
            SelectedFileName = string.IsNullOrEmpty(state.SelectedPath) ? string.Empty : Path.GetFileName(state.SelectedPath),
            DialogPaths = paths,
            Events = events.ToArray()
        };

        observedSession = session;
        observedPage = page;
        observedConversationLoader = conversationLoader;
        observedDialogPaths = dialogPaths;
        observedRevision = revision;
        observedSelectedPath = state.SelectedPath ?? string.Empty;
        observedLanguage = language;
        observedNewUi = newUi;
    }

    internal static void Clear()
    {
        current = EditorDialogPresentationSnapshot.Empty;
        observedSession = null;
        observedPage = null;
        observedConversationLoader = null;
        observedDialogPaths = null;
        observedRevision = 0L;
        observedSelectedPath = string.Empty;
        observedLanguage = string.Empty;
        observedNewUi = false;
    }
}

internal static class DialogEditorCommandProcessor
{
    internal static void Process(EditorSession session)
    {
        bool dirty = false;
        for (int processed = 0;
             processed < DialogEditorCommandQueue.MaxCommandsPerFrame &&
             DialogEditorCommandQueue.TryDequeue(out DialogEditorCommand command);
             processed++)
        {
            try
            {
                if (command.Kind == DialogEditorCommandKind.SelectDialog &&
                    DialogEditorActions.SelectDialog(session, command.Path))
                    dirty = true;
            }
            catch (Exception error)
            {
                global::DryCycle.DevUI.DevTool.Core.EditorDiagnostics.LogSource?.LogWarning("DevTool dialog command failed: " + error.Message);
            }
        }

        int rejected = DialogEditorCommandQueue.ConsumeRejectedCount();
        if (rejected > 0)
            EditorDiagnostics.Warning("Dialog editor command inbox rejected " + rejected +
                " edit(s); pending=" + DialogEditorCommandQueue.PendingCount);

        if (dirty)
            EditorRevisionHub.Mark(session, EditorRevisionKind.Dialog);
    }

    internal static void Clear() => DialogEditorCommandQueue.Clear();
}
