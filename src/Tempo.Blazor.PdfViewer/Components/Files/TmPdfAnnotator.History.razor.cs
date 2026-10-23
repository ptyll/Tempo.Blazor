using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// FZE-15: the undo/redo surface of <see cref="TmPdfAnnotator"/> — the per-session command
/// stack (<see cref="PdfAnnotationHistory"/>), the public <c>UndoAsync</c>/<c>RedoAsync</c>
/// API with <c>CanUndo</c>/<c>CanRedo</c>, the <c>AllowUndo</c> switch with
/// <c>OnHistoryChanged</c>, the JS-driven Ctrl/⌘+Z / Ctrl+Shift+Z / Ctrl+Y shortcuts and the
/// undo/redo announcements (the visually-hidden live region plus a dismissible visible status
/// chip). Split from TmPdfAnnotator.razor.cs to keep both under the 1200-line ceiling.
/// </summary>
public partial class TmPdfAnnotator
{
    // ── FZE-15: history state ────────────────────────────────────────────────

    /// <summary>The per-annotator command stack (one instance, cleared on a document change).</summary>
    private readonly PdfAnnotationHistory _history = new();

    /// <summary>The latest aria-live announcement (undo/redo result or the conflict hint);
    /// rendered through the visually-hidden live region at the annotator root.</summary>
    private string? _announcement;

    /// <summary>Whether undo/redo is offered. When false the history group and the keyboard
    /// shortcuts are hidden and mutations bypass the stack entirely.</summary>
    [Parameter] public bool AllowUndo { get; set; } = true;

    /// <summary>Callback invoked whenever the history state changes (a step was executed,
    /// undone, redone, skipped or the stack was cleared).</summary>
    [Parameter] public EventCallback OnHistoryChanged { get; set; }

    /// <summary>Whether there is a recorded step to undo. False when <see cref="AllowUndo"/> is
    /// off or a history operation is currently running (<see cref="PdfAnnotationHistory.IsBusy"/>):
    /// the buttons and the mobile "More" entries disable while a step is in flight.</summary>
    public bool CanUndo => AllowUndo && _history.CanUndo && !_history.IsBusy;

    /// <summary>Whether there is a recorded step to redo. False when <see cref="AllowUndo"/> is
    /// off or a history operation is currently running.</summary>
    public bool CanRedo => AllowUndo && _history.CanRedo && !_history.IsBusy;

    /// <summary>The localized name of the next undo step's action, or the "nothing to undo" text.</summary>
    private string UndoTooltip => AllowUndo && _history.NextUndoAction is { } action
        ? Loc["TmPdfAnnotator_UndoAction", ActionName(action)]
        : Loc["TmPdfAnnotator_NothingToUndo"];

    /// <summary>The localized name of the next redo step's action, or the "nothing to redo" text.</summary>
    private string RedoTooltip => AllowUndo && _history.NextRedoAction is { } action
        ? Loc["TmPdfAnnotator_RedoAction", ActionName(action)]
        : Loc["TmPdfAnnotator_NothingToRedo"];

    /// <summary>Localized name of a history action (the "Undo: {action}" / "Redone: {action}" labels).</summary>
    private string ActionName(PdfAnnotatorHistoryAction action) => action switch
    {
        PdfAnnotatorHistoryAction.Create => Loc["TmPdfAnnotator_ActionCreate"],
        PdfAnnotatorHistoryAction.Reply => Loc["TmPdfAnnotator_ActionReply"],
        PdfAnnotatorHistoryAction.Edit => Loc["TmPdfAnnotator_ActionEdit"],
        PdfAnnotatorHistoryAction.Resolve => Loc["TmPdfAnnotator_ActionResolve"],
        PdfAnnotatorHistoryAction.Reopen => Loc["TmPdfAnnotator_ActionReopen"],
        PdfAnnotatorHistoryAction.Delete => Loc["TmPdfAnnotator_ActionDelete"],
        PdfAnnotatorHistoryAction.Stamp => Loc["TmPdfAnnotator_ActionStamp"],
        PdfAnnotatorHistoryAction.Ink => Loc["TmPdfAnnotator_ActionInk"],
        PdfAnnotatorHistoryAction.Highlight => Loc["TmPdfAnnotator_ActionHighlight"],
        _ => string.Empty
    };

    /// <summary>Builds the command context for the current provider and document.</summary>
    private PdfAnnotationHistoryContext HistoryContext()
        => new(EffectiveProvider, EffectiveDocumentId, _history.ResolveThreadId, _history.RemapThreadId);

    /// <summary>Subscribes the stack events: <see cref="PdfAnnotationHistory.Changed"/> raises
    /// the public <see cref="OnHistoryChanged"/> callback; the conflict event announces the hint.</summary>
    private void SubscribeHistory()
    {
        if (_historySubscribed)
        {
            return;
        }

        _historySubscribed = true;
        _history.Changed += HandleHistoryChanged;
        _history.ConflictDetected += HandleHistoryConflict;
    }

    private bool _historySubscribed;

    private void HandleHistoryChanged()
        => _ = InvokeAsync(async () =>
        {
            if (OnHistoryChanged.HasDelegate)
            {
                await OnHistoryChanged.InvokeAsync();
            }
        });

    private void HandleHistoryConflict()
        => _ = InvokeAsync(async () =>
            await AnnounceAsync(Loc["TmPdfAnnotator_UndoConflict"], showStatus: true));

    // ── Announcements (live region + visible status chip) ────────────────────

    /// <summary>
    /// Records an undo/redo announcement. Repeated IDENTICAL announcements are the classic
    /// live-region trap: setting the same text raises no change event, so the region is
    /// cleared and rendered empty before the new value lands (a conflict on two consecutive
    /// steps must be announced twice). Problems (a conflict, a failure) additionally surface
    /// as a short VISIBLE status chip near the toolbar — a live region alone is invisible to
    /// sighted users. The chip auto-dismisses after ~5 s and can be closed by the user.
    /// </summary>
    private async Task AnnounceAsync(string message, bool showStatus = false)
    {
        if (string.Equals(_announcement, message, StringComparison.Ordinal))
        {
            // Clear + render + set: the identical text becomes a DOM change again.
            _announcement = null;
            _statusMessage = null;
            await InvokeAsync(StateHasChanged);
        }

        _announcement = message;
        if (showStatus)
        {
            ShowStatus(message);
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>Text of the visible status chip near the toolbar (conflict / failure hints);
    /// null renders nothing.</summary>
    private string? _statusMessage;

    /// <summary>How long the visible status chip stays up before it auto-dismisses.</summary>
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Cancellation of the chip's auto-dismiss; a newer message replaces the timer.</summary>
    private CancellationTokenSource? _statusCts;

    /// <summary>Shows the visible status chip and (re)arms its ~5 s auto-dismiss.</summary>
    private void ShowStatus(string message)
    {
        CancelStatusTimeout();
        _statusMessage = message;
        var cts = new CancellationTokenSource(StatusTimeout);
        _statusCts = cts;
        _ = ClearStatusAfterTimeoutAsync(cts);
    }

    private async Task ClearStatusAfterTimeoutAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(StatusTimeout, cts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return; // dismissed by the user or replaced by a newer message
        }

        if (_statusCts != cts || cts.IsCancellationRequested)
        {
            return;
        }

        _statusCts = null;
        _statusMessage = null;
        try
        {
            await InvokeAsync(StateHasChanged);
        }
        catch (ObjectDisposedException)
        {
            // The annotator died while the auto-dismiss was pending.
        }
    }

    /// <summary>The chip's ✕: clears the message and the pending auto-dismiss timer.</summary>
    private async Task DismissStatusAsync()
    {
        CancelStatusTimeout();
        _statusMessage = null;
        await InvokeAsync(StateHasChanged);
    }

    private void CancelStatusTimeout()
    {
        if (_statusCts is null)
        {
            return;
        }

        _statusCts.Cancel();
        _statusCts.Dispose();
        _statusCts = null;
    }

    // ── Public undo/redo API ─────────────────────────────────────────────────

    /// <summary>Undoes the newest annotation mutation of this session. A step whose thread was
    /// changed or deleted by another user is skipped with the localized conflict hint; a
    /// provider failure announces "Undo failed" and leaves the stack unchanged.</summary>
    public Task UndoAsync() => RunHistoryStepAsync(undone: true);

    /// <summary>Redoes the most recently undone annotation mutation. A provider failure
    /// announces "Redo failed" and leaves the stack unchanged.</summary>
    public Task RedoAsync() => RunHistoryStepAsync(undone: false);

    private async Task RunHistoryStepAsync(bool undone)
    {
        if (!AllowUndo)
        {
            return;
        }

        PdfAnnotationHistoryResult result;
        try
        {
            result = undone ? await _history.UndoAsync() : await _history.RedoAsync();
        }
        catch (Exception)
        {
            // The history keeps the step on a provider failure — the stack is unchanged.
            // Reload the threads (the failed call may have left the provider state half
            // applied) and tell the user, instead of failing silently.
            await AnnounceAsync(Loc[undone ? "TmPdfAnnotator_UndoFailed" : "TmPdfAnnotator_RedoFailed"], showStatus: true);
            await LoadThreadsAsync();
            await InvokeAsync(StateHasChanged);
            return;
        }

        if (result.Outcome == PdfAnnotationHistoryOutcome.Applied)
        {
            // The affected thread is selected and, in Continuous, scrolled into view.
            _selectedThreadId = result.ThreadId;
            await AnnounceAsync(
                result.Action is { } action
                    ? Loc[undone ? "TmPdfAnnotator_UndoneAnnouncement" : "TmPdfAnnotator_RedoneAnnouncement", ActionName(action)]
                    : Loc[undone ? "TmPdfAnnotator_UndoneAnnouncement" : "TmPdfAnnotator_RedoneAnnouncement"]);
        }
        else if (result.Outcome == PdfAnnotationHistoryOutcome.Skipped)
        {
            await AnnounceAsync(Loc["TmPdfAnnotator_UndoConflict"], showStatus: true);
        }

        await LoadThreadsAsync();

        if (_selectedThreadId is not null)
        {
            var thread = _threads.FirstOrDefault(t => string.Equals(t.Id, _selectedThreadId, StringComparison.Ordinal));
            await NavigateToThreadAnchorAsync(thread);
        }

        await InvokeAsync(StateHasChanged);
    }

    // ── Keyboard shortcuts (JS-driven) ───────────────────────────────────────

    /// <summary>
    /// The JS → Blazor bridge for the annotator root's ONE keydown listener
    /// (<c>tmPdfAnnotator.attachHistoryShortcuts</c>): the filtering (exactly one of Ctrl / ⌘,
    /// z/y, <c>!e.repeat</c>, no editable focus) ran in JavaScript on every keystroke and the
    /// event was already <c>preventDefault</c>-ed — only an actual shortcut press lands here.
    /// A no-op when <see cref="AllowUndo"/> is off. An in-progress Draw draft is NOT history:
    /// Ctrl+Z removes the draft's last stroke (drafts never enter the stack).
    /// </summary>
    [JSInvokable]
    public Task OnHistoryShortcut(bool redo)
    {
        if (!AllowUndo)
        {
            return Task.CompletedTask;
        }

        if (!redo && _mode == PdfAnnotatorMode.Draw && _draft is { Strokes.Count: > 0 })
        {
            _draft.Strokes.RemoveAt(_draft.Strokes.Count - 1);
            return InvokeAsync(StateHasChanged);
        }

        return redo ? RedoAsync() : UndoAsync();
    }

    /// <summary>The .NET reference the JS keydown listener invokes back through.</summary>
    private DotNetObjectReference<TmPdfAnnotator>? _shortcutRef;

    /// <summary>Wires the root keydown listener after the first render (the root element must
    /// exist). Per instance: the JS side keys the AbortController off the root element.</summary>
    private async Task AttachHistoryShortcutsAsync()
    {
        if (_shortcutRef is not null)
        {
            return;
        }

        _shortcutRef = DotNetObjectReference.Create(this);
        try
        {
            await JS.InvokeVoidAsync("tmPdfAnnotator.attachHistoryShortcuts", _annotatorRef, _shortcutRef);
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>Unregisters the JS keydown listener (the per-instance AbortController) and
    /// drops the .NET reference. Safe when the listener was never attached.</summary>
    private async Task DetachHistoryShortcutsAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmPdfAnnotator.detachHistoryShortcuts", _annotatorRef);
        }
        catch (JSException)
        {
        }
        catch (JSDisconnectedException)
        {
        }

        _shortcutRef?.Dispose();
        _shortcutRef = null;
    }
}
