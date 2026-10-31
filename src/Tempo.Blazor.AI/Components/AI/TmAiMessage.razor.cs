using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;
using Tempo.Blazor.AI.Markdown;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Code-behind of <see cref="TmAiMessage"/>: the part projection (including the grouping of consecutive
/// tool-call parts and the reasoning panel's live flag), the copy / read-aloud / feedback / version
/// behaviours, the roving action toolbar and the interop plumbing. The markup stays in the component file
/// so the file-length ceiling holds.
/// </summary>
public partial class TmAiMessage
{
    private const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-message.js";

    /// <summary>The actions the roving tabindex walks, in DOM order.</summary>
    private enum ActionSlot
    {
        Copy = 0,
        Regenerate = 1,
        ReadAloud = 2,
        FeedbackUp = 3,
        FeedbackDown = 4,
    }

    private ElementReference _bodyRef;
    private ElementReference _copyRef;
    private ElementReference _regenerateRef;
    private ElementReference _readAloudRef;
    private ElementReference _upRef;
    private ElementReference _downRef;
    private ElementReference _previewCloseRef;
    private ElementReference _feedbackInputRef;

    private readonly string _feedbackInputId = $"tm-ai-message-feedback-{Guid.NewGuid():n}";
    private IJSObjectReference? _module;
    private DotNetObjectReference<TmAiMessage>? _self;
    private AiMessage? _lastMessage;
    private AiFeedback? _localFeedback;
    private AiSource? _previewSource;
    private string? _previewSourceId;
    private string _feedbackReason = string.Empty;
    private string _liveAnnouncement = string.Empty;
    private AiMessageStatus? _lastStatus;
    private bool _previewOpen;
    private bool _feedbackOpen;
    private bool _copied;
    private bool _speaking;
    private bool _speechSupported;
    private bool _speechProbed;
    private bool _disposed;
    private int _rovingSlot = (int)ActionSlot.Copy;
    private CancellationTokenSource? _copiedReset;
    private int? _localVersionIndex;
    private int? _lastModelVersionIndex;

    /// <summary>
    /// The message this block renders. Required: a null value renders an empty shell (the body is empty
    /// and no action is offered) instead of throwing, so a host can place the component before the first
    /// token arrives.
    /// </summary>
    [Parameter, EditorRequired] public AiMessage? Message { get; set; }

    /// <summary>Render the avatar slot (the role glyph, or <see cref="AvatarTemplate"/>). Default true.</summary>
    [Parameter] public bool ShowAvatar { get; set; } = true;

    /// <summary>Render the action toolbar (copy, regenerate, read aloud, feedback). Default true.</summary>
    [Parameter] public bool ShowActions { get; set; } = true;

    /// <summary>Render the cited sources as cards in part order. Default true.</summary>
    [Parameter] public bool ShowSources { get; set; } = true;

    /// <summary>Render the follow-up suggestions as chips under the answer. Default true.</summary>
    [Parameter] public bool ShowFollowUps { get; set; } = true;

    /// <summary>
    /// Replaces the default role glyph inside the avatar slot. The slot itself stays decorative
    /// (<c>aria-hidden</c>): the role is conveyed by the message's own accessible structure, so a host
    /// image here must not introduce a second name for the same turn.
    /// </summary>
    [Parameter] public RenderFragment? AvatarTemplate { get; set; }

    /// <summary>
    /// Replaces the whole built-in action toolbar with host actions. The built-in bar is a
    /// <c>role="toolbar"</c> with a roving tabindex; a host template owns its own keyboard model, so it is
    /// rendered as plain content and must provide one if it renders more than one button.
    /// </summary>
    [Parameter] public RenderFragment? ActionsTemplate { get; set; }

    /// <summary>Raised when the reader asks for another generation of this answer.</summary>
    [Parameter] public EventCallback OnRegenerate { get; set; }

    /// <summary>
    /// Raised with the rating when the reader presses a thumb. A negative rating is raised immediately
    /// (thumbs up/down must never require an explanation) and raised a second time with
    /// <see cref="AiFeedback.Reason"/> when the reader submits the optional reason.
    /// </summary>
    [Parameter] public EventCallback<AiFeedback> OnFeedback { get; set; }

    /// <summary>Raised with the suggestion text when a follow-up chip is activated.</summary>
    [Parameter] public EventCallback<string> OnFollowUp { get; set; }

    /// <summary>
    /// Raised with the source id when a citation chip is activated — after the built-in source preview
    /// opened, so a host can navigate away without losing the preview state it never sees.
    /// </summary>
    [Parameter] public EventCallback<string> OnCitationClick { get; set; }

    /// <summary>Raised when the reader asks to retry a failed generation.</summary>
    [Parameter] public EventCallback OnRetry { get; set; }

    /// <summary>
    /// Raised with the tool call the reader asked to run again, from a failed call's Retry button — inside a
    /// single call's card or inside a grouped run. The buttons render only when a handler is bound, so no
    /// host gets a button that does nothing.
    /// </summary>
    [Parameter] public EventCallback<AiToolCall> OnToolCallRetry { get; set; }

    /// <summary>
    /// Raised with the target zero-based version index when the reader steps through the regenerated
    /// versions. The component shows the chosen version immediately (a switcher that only moves its label
    /// until the host answers is a lying control) but never mutates
    /// <see cref="AiMessage.ActiveVersionIndex"/>: assign the index in the handler to persist the choice,
    /// and a host that changes the model's index itself wins over the reader's last step.
    /// </summary>
    [Parameter] public EventCallback<int> ActiveVersionIndexChanged { get; set; }

    /// <summary>
    /// Offer the "Read aloud" action, which speaks the answer's plain text through the browser's
    /// <c>speechSynthesis</c>. Default false. The action is hidden when the browser has no speech
    /// synthesis (feature-detected on first render) — never a dead button.
    /// </summary>
    [Parameter] public bool ReadAloud { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>
    /// The rendered version's parts. The switcher can show a version the host has not written back to
    /// <see cref="AiMessage.ActiveVersionIndex"/> yet, so the displayed index (not the model's) selects the
    /// content: a label that says "2/3" must never sit above version 3.
    /// </summary>
    private IReadOnlyList<AiMessagePart> Parts
    {
        get
        {
            var message = Message;
            if (message is null)
            {
                return Array.Empty<AiMessagePart>();
            }

            var index = DisplayedVersionIndex;
            return index < message.Versions.Count
                ? (IReadOnlyList<AiMessagePart>)message.Versions[index].Parts
                : (IReadOnlyList<AiMessagePart>)message.Parts;
        }
    }

    /// <summary>The markdown of the displayed version, in part order.</summary>
    private string Text => string.Concat(Parts.OfType<AiTextPart>().Select(part => part.Markdown));

    private IReadOnlyList<string> FollowUps => Message is null ? Array.Empty<string>() : (IReadOnlyList<string>)Message.FollowUps.ToList();

    private bool IsStreaming => Message?.Status == AiMessageStatus.Streaming;

    private int VersionCount => Message?.VersionCount ?? 1;

    /// <summary>
    /// The version the component currently shows: the reader's last choice, else the model's index, else
    /// the newest version.
    /// </summary>
    private int DisplayedVersionIndex
        => Math.Clamp(_localVersionIndex ?? Message?.ActiveVersionIndex ?? (VersionCount - 1), 0, VersionCount - 1);

    private bool ShowsVersionSwitcher => Message is not null && VersionCount > 1;

    private AiFeedback? EffectiveFeedback => Message?.Feedback ?? _localFeedback;

    private bool CanRegenerate => OnRegenerate.HasDelegate;

    private bool CanFeedback => OnFeedback.HasDelegate;

    private bool CanReadAloud => ReadAloud && _speechSupported;

    /// <summary>
    /// The action bar is offered only for a finished turn: there is nothing to copy, regenerate or rate
    /// while the answer is still arriving. A FAILED turn keeps its bar next to the error block — Retry
    /// lives in the error block, and the partial answer is still worth copying — so the two render
    /// together rather than one replacing the other.
    /// </summary>
    private bool ShowsActions
        => ShowActions
            && ActionsTemplate is null
            && Message is not null
            && Message.Status is AiMessageStatus.Completed or AiMessageStatus.Stopped or AiMessageStatus.Failed;

    private string RoleModifier => Message?.Role switch
    {
        AiMessageRole.User => "user",
        AiMessageRole.System => "system",
        AiMessageRole.Tool => "tool",
        _ => "assistant",
    };

    private string StatusModifier => Message?.Status switch
    {
        AiMessageStatus.Streaming => "streaming",
        AiMessageStatus.Completed => "completed",
        AiMessageStatus.Stopped => "stopped",
        AiMessageStatus.Failed => "failed",
        _ => "pending",
    };

    private string AvatarIcon => Message?.Role switch
    {
        AiMessageRole.User => IconNames.User,
        AiMessageRole.System => IconNames.Settings,
        AiMessageRole.Tool => IconNames.Terminal,
        _ => IconNames.WandSparkles,
    };

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A streaming answer that stops changes the page silently: the label under the answer is only
        // read by a reader who is already there. Announce the transition through the message's single
        // live region — once, and never for a turn that arrives already stopped.
        var status = Message?.Status;
        if (_lastStatus is AiMessageStatus.Streaming or AiMessageStatus.Pending
            && status == AiMessageStatus.Stopped)
        {
            _liveAnnouncement = Loc["TmAiMessage_Stopped"];
        }

        _lastStatus = status;

        if (ReferenceEquals(_lastMessage, Message))
        {
            // The host moved the version itself: its choice wins over the reader's local step.
            if (Message?.ActiveVersionIndex != _lastModelVersionIndex)
            {
                _lastModelVersionIndex = Message?.ActiveVersionIndex;
                _localVersionIndex = null;
            }

            return;
        }

        // A different message is a different turn: no interaction state may leak across it.
        _lastMessage = Message;
        _localVersionIndex = null;
        _lastModelVersionIndex = Message?.ActiveVersionIndex;
        _localFeedback = null;
        _previewSource = null;
        _previewSourceId = null;
        _previewOpen = false;
        _feedbackOpen = false;
        _feedbackReason = string.Empty;
        _liveAnnouncement = string.Empty;
        _copied = false;
        StopSpeakingSilently();
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || !ReadAloud || _speechProbed || _disposed)
        {
            return;
        }

        _speechProbed = true;
        var module = await EnsureModuleAsync();
        if (module is null || _disposed)
        {
            return;
        }

        bool supported;
        try
        {
            supported = await module.InvokeAsync<bool>("isSpeechSupported");
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return;
        }

        if (!supported || _disposed)
        {
            return;
        }

        _speechSupported = true;
        await InvokeAsync(StateHasChanged);
    }

    // ── Parts, sources and citations ───────────────────────────────────────────────────────────

    /// <summary>
    /// The run of consecutive tool-call parts starting at <paramref name="index"/>, or null when that part
    /// is not the first of such a run (the rest of the run is rendered by its first part).
    /// </summary>
    private IReadOnlyList<AiToolCall>? ToolCallRunAt(int index)
    {
        var parts = Parts;
        if (index < 0 || index >= parts.Count || parts[index] is not AiToolCallPart)
        {
            return null;
        }

        if (index > 0 && parts[index - 1] is AiToolCallPart)
        {
            return null;
        }

        var run = new List<AiToolCall>();
        for (var position = index; position < parts.Count && parts[position] is AiToolCallPart part; position++)
        {
            run.Add(part.ToolCall);
        }

        return run;
    }

    /// <summary>
    /// The card's parameterless retry callback for <paramref name="call"/>. It stays DEFAULT (no delegate)
    /// when the host listens to nothing, so a failed card never offers a Retry button that cannot do anything.
    /// </summary>
    private EventCallback RetryCallback(AiToolCall call) => OnToolCallRetry.HasDelegate
        ? EventCallback.Factory.Create(this, () => OnToolCallRetry.InvokeAsync(call))
        : default;

    /// <summary>
    /// The href a source may be rendered with: the package's URL allowlist decides, and a refused target
    /// is rendered as plain text rather than a dead <c>#</c> link.
    /// </summary>
    private static string? SafeLink(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var sanitized = AiMarkdownUrlPolicy.SanitizeLink(url);
        return string.Equals(sanitized, AiMarkdownUrlPolicy.RefusedUrl, StringComparison.Ordinal) ? null : sanitized;
    }

    private async Task HandleCitationClickAsync(string sourceId)
    {
        var source = string.IsNullOrEmpty(sourceId)
            ? null
            : Parts.OfType<AiSourcesPart>()
                .SelectMany(part => part.Sources)
                .LastOrDefault(item => string.Equals(item.Id, sourceId, StringComparison.Ordinal));
        if (source is not null)
        {
            _previewSource = source;
            _previewSourceId = sourceId;
            _previewOpen = true;
        }

        if (OnCitationClick.HasDelegate)
        {
            await OnCitationClick.InvokeAsync(sourceId);
        }
    }

    private Task OnPreviewOpenChanged(bool open)
    {
        _previewOpen = open;
        if (!open)
        {
            _previewSource = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Post-open hook: the preview is a dialog, so focus has to enter it — a keyboard user who pressed
    /// a citation chip must be able to reach the snippet, the link and Close. The close button is the
    /// entry point because the panel's content is read-only text.
    /// </summary>
    private Task FocusPreviewCloseAsync() => FocusAsync(_previewCloseRef);

    /// <summary>Post-open hook: the reason form opens on its textarea, the only thing it asks for.</summary>
    private Task FocusFeedbackInputAsync() => FocusAsync(_feedbackInputRef);

    private async Task ClosePreviewAsync()
    {
        _previewOpen = false;
        _previewSource = null;

        // A programmatic close has no dismissal for overlay.js to restore focus from, and the anchor
        // is the (non-focusable) message body — without this the reader lands on <body>.
        await RestoreCitationFocusAsync();
    }

    /// <summary>
    /// <see cref="TmOverlayPanel"/>'s dismissal hook. A keyboard dismissal must hand focus back to the
    /// citation chip that opened the preview: overlay.js restores the panel's anchor, and the anchor
    /// here is the message body, which takes no focus. An outside pointerdown deliberately does NOT
    /// pull focus back — the reader clicked somewhere else and undoing that click is worse than a
    /// focus that follows it.
    /// </summary>
    private Task OnPreviewDismissedAsync(string reason)
        => string.Equals(reason, "outside", StringComparison.Ordinal)
            ? Task.CompletedTask
            : RestoreCitationFocusAsync();

    /// <summary>
    /// Puts focus back on the citation chip that opened the preview. The chip lives inside the
    /// markdown module's generated HTML, so Blazor has no <see cref="ElementReference"/> for it; the
    /// module resolves it by its stable <c>data-source-id</c> inside the message body.
    /// </summary>
    private async Task RestoreCitationFocusAsync()
    {
        var sourceId = _previewSourceId;
        if (string.IsNullOrEmpty(sourceId))
        {
            return;
        }

        var module = await EnsureModuleAsync();
        if (module is null)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("focusCitation", _bodyRef, sourceId);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // Static rendering: the popover still works, the focus move is best effort.
        }
    }

    // ── Follow-ups and versions ────────────────────────────────────────────────────────────────

    private async Task FollowUpAsync(string suggestion)
    {
        if (OnFollowUp.HasDelegate)
        {
            await OnFollowUp.InvokeAsync(suggestion);
        }
    }

    private async Task ChangeVersionAsync(int delta)
    {
        var target = Math.Clamp(DisplayedVersionIndex + delta, 0, VersionCount - 1);
        if (target == DisplayedVersionIndex)
        {
            return;
        }

        // The content follows the reader immediately (a switcher that only moves its label until the host
        // answers is a lying control); the host is still told, and its own index wins on the next change.
        _localVersionIndex = target;
        if (ActiveVersionIndexChanged.HasDelegate)
        {
            await ActiveVersionIndexChanged.InvokeAsync(target);
        }
    }

    // ── Copy ───────────────────────────────────────────────────────────────────────────────────

    private async Task CopyAsync()
    {
        var text = AiMessageText.ToPlainText(Text);
        var module = await EnsureModuleAsync();
        if (module is null)
        {
            return;
        }

        bool copied;
        try
        {
            copied = await module.InvokeAsync<bool>("copyText", text);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return;
        }

        if (!copied)
        {
            // A refused clipboard write (permissions, insecure context) must not claim success.
            return;
        }

        _copied = true;
        _liveAnnouncement = Loc["TmAiMessage_Copied"];
        ScheduleCopiedReset();
    }

    /// <summary>
    /// The copied confirmation resets itself, so the button does not lie after the reader moved on.
    /// The reset is cancelled on dispose; it runs on the renderer, never on a thread-pool thread.
    /// </summary>
    private void ScheduleCopiedReset()
    {
        _copiedReset?.Cancel();
        _copiedReset = new CancellationTokenSource();
        var token = _copiedReset.Token;
        _ = ResetCopiedAsync(token);
    }

    private async Task ResetCopiedAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_disposed)
        {
            return;
        }

        try
        {
            await InvokeAsync(() =>
            {
                _copied = false;
                if (string.Equals(_liveAnnouncement, Loc["TmAiMessage_Copied"], StringComparison.Ordinal))
                {
                    // Only the copy confirmation expires; a later announcement stays until it is replaced.
                    _liveAnnouncement = string.Empty;
                }

                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // The message was removed while the confirmation was pending.
        }
    }

    // ── Regenerate, retry, feedback ───────────────────────────────────────────────────────────

    private async Task RegenerateAsync()
    {
        if (OnRegenerate.HasDelegate)
        {
            await OnRegenerate.InvokeAsync();
        }
    }

    private async Task RetryAsync()
    {
        if (OnRetry.HasDelegate)
        {
            await OnRetry.InvokeAsync();
        }
    }

    private async Task RatePositiveAsync()
    {
        _feedbackOpen = false;
        var feedback = new AiFeedback { Rating = AiFeedbackRating.Positive };
        _localFeedback = feedback;
        if (OnFeedback.HasDelegate)
        {
            await OnFeedback.InvokeAsync(feedback);
        }
    }

    private async Task RateNegativeAsync()
    {
        var feedback = new AiFeedback { Rating = AiFeedbackRating.Negative };
        _localFeedback = feedback;
        _feedbackReason = string.Empty;
        _feedbackOpen = true;

        if (OnFeedback.HasDelegate)
        {
            await OnFeedback.InvokeAsync(feedback);
        }
    }

    private Task OnFeedbackOpenChanged(bool open)
    {
        _feedbackOpen = open;
        return Task.CompletedTask;
    }

    private Task CancelFeedbackAsync() => CloseFeedbackAsync();

    private async Task SubmitFeedbackAsync()
    {
        var reason = _feedbackReason.Trim();
        await CloseFeedbackAsync();

        if (reason.Length == 0)
        {
            return;
        }

        var feedback = new AiFeedback { Rating = AiFeedbackRating.Negative, Reason = reason };
        _localFeedback = feedback;
        if (OnFeedback.HasDelegate)
        {
            await OnFeedback.InvokeAsync(feedback);
        }
    }

    /// <summary>
    /// Closes the reason dialog and hands focus back to the thumbs-down button that opened it: the
    /// panel closes programmatically, so overlay.js has no dismissal of its own to restore from, and
    /// the roving tab stop has to follow the focus the close just took.
    /// </summary>
    private async Task CloseFeedbackAsync()
    {
        _feedbackOpen = false;
        _feedbackReason = string.Empty;
        _rovingSlot = (int)ActionSlot.FeedbackDown;
        await FocusAsync(_downRef);
    }

    /// <summary>
    /// <see cref="TmOverlayPanel"/>'s dismissal hook for the reason dialog. The panel's focus trap
    /// restores the opener (the thumb the reader pressed); the roving tab stop must follow it, or the
    /// next Tab starts from a different button than the one that holds focus.
    /// </summary>
    private Task OnFeedbackDismissed(string reason)
    {
        _rovingSlot = (int)ActionSlot.FeedbackDown;
        return Task.CompletedTask;
    }

    /// <summary>Best-effort focus move: a statically rendered component has no interop at all.</summary>
    private async Task FocusAsync(ElementReference reference)
    {
        try
        {
            await reference.FocusAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No interop (static rendering): the popover still opens, the focus move is best effort.
        }
    }

    // ── Read aloud ────────────────────────────────────────────────────────────────────────────

    private async Task ToggleReadAloudAsync()
    {
        var module = await EnsureModuleAsync();
        if (module is null)
        {
            return;
        }

        try
        {
            if (_speaking)
            {
                await module.InvokeVoidAsync("stopSpeaking");
                _speaking = false;
                return;
            }

            _self ??= DotNetObjectReference.Create(this);
            _speaking = await module.InvokeAsync<bool>(
                "speak",
                AiMessageText.ToPlainText(Text),
                CultureInfo.CurrentUICulture.Name,
                _self);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            _speaking = false;
        }
    }

    /// <summary>
    /// Raised by the module when the utterance finished on its own, so the toggle returns to "Read aloud"
    /// without the reader pressing anything. Marshalled onto the renderer.
    /// </summary>
    [JSInvokable]
    public Task OnSpeechEndedAsync() => InvokeAsync(() =>
    {
        _speaking = false;
        StateHasChanged();
    });

    private void StopSpeakingSilently()
    {
        if (!_speaking)
        {
            return;
        }

        _speaking = false;
        _ = StopSpeakingAsync();

        async Task StopSpeakingAsync()
        {
            var module = _module;
            if (module is null)
            {
                return;
            }

            try
            {
                await module.InvokeVoidAsync("stopSpeaking");
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
            }
        }
    }


    // ── Interop plumbing ──────────────────────────────────────────────────────────────────────

    private async Task<IJSObjectReference?> EnsureModuleAsync()
    {
        if (_disposed)
        {
            return null;
        }

        if (_module is not null)
        {
            return _module;
        }

        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", InteropModulePath);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return null;
        }

        return _module;
    }

    private static bool IsInteropFailure(Exception exception)
        => exception is JSException or JSDisconnectedException or InvalidOperationException or NotSupportedException;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _self?.Dispose();
        _self = null;
        _copiedReset?.Cancel();
        _copiedReset?.Dispose();
        _copiedReset = null;

        var module = _module;
        _module = null;
        if (module is null)
        {
            return;
        }

        try
        {
            if (_speaking)
            {
                await module.InvokeVoidAsync("stopSpeaking");
            }

            await module.DisposeAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
        }

        _speaking = false;
    }
}
