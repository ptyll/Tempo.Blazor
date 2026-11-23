using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Runtime.ExceptionServices;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.AI.Services;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Code-behind of <see cref="TmAiConversation"/>: the delta stream (throttled renders, stop, terminal
/// states), the render window of a long conversation, the scroll bookkeeping delegated to the package's
/// ES module and the interop plumbing. The markup stays in the component file so the file-length ceiling
/// holds and the transcript's ARIA contract reads in one place.
/// </summary>
public partial class TmAiConversation
{
    private const string InteropModulePath = "./_content/Tempo.Blazor.AI/js/ai-conversation.js";

    /// <summary>The smallest render window worth shipping: below it the transcript is a teaser, not a log.</summary>
    private const int MinRenderWindowSize = 10;

    /// <summary>The smallest "load older" step; a zero step would turn the button into a no-op.</summary>
    private const int MinRenderWindowStep = 10;

    private readonly List<AiMessage> _windowMessages = [];

    private ElementReference _logRef;
    private ElementReference _contentRef;
    private IJSObjectReference? _module;
    private DotNetObjectReference<TmAiConversation>? _self;
    private TimeProvider? _clock;

    /// <summary>The service provider, injected so the render throttle can resolve the host's clock.</summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private IReadOnlyList<AiMessage>? _lastMessages;
    private int _lastMessageCount = -1;
    private string? _lastTailId;
    private bool _generating;
    private bool _completionAnnounced;
    private bool _hostStopRaised;

    private CancellationTokenSource? _streamCts;
    private readonly List<(int Sequence, string Text)> _announcements = [];
    private int _announcementSequence;
    private int _windowStart = -1;
    private bool _windowExpanded;
    private bool _hasNewMessages;
    private bool _contentDirty;
    private bool _stickToBottom = true;
    private bool _scrollReported;
    private bool _focusLogWhenIdle;
    private bool _attached;
    private bool _focusLogAfterRender;
    private bool _hasRendered;
    private bool _disposed;
    private TaskCompletionSource<bool>? _renderCompletion;

    /// <summary>
    /// The turns of the conversation, oldest first. The host owns the list: append a turn before it starts
    /// streaming and the component renders it. Reassigning the property to a NEW list instance is treated as
    /// loading another conversation (the render window snaps to the newest turns); appending to the same
    /// instance keeps the window where the reader left it.
    /// </summary>
    [Parameter] public IReadOnlyList<AiMessage>? Messages { get; set; }

    /// <summary>
    /// Replaces the built-in <see cref="TmAiMessage"/> of every turn. The template receives the message, so a
    /// host can attach its own per-turn actions (regenerate, feedback, retry) without the conversation
    /// re-declaring them.
    /// </summary>
    [Parameter] public RenderFragment<AiMessage>? MessageTemplate { get; set; }

    /// <summary>
    /// Prompts offered by the empty state. They render only while the conversation has no turn AND
    /// <see cref="OnSuggestion"/> is bound — a chip that raises nothing is not offered.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? Suggestions { get; set; }

    /// <summary>Raised with the text of an activated suggestion prompt.</summary>
    [Parameter] public EventCallback<string> OnSuggestion { get; set; }

    /// <summary>
    /// How the empty-state suggestion chips sit in the panel. Default
    /// <see cref="AiSuggestionsLayout.Wrap"/> keeps today's wrapping, centred row.
    /// <see cref="AiSuggestionsLayout.ScrollRow"/> is a nowrap horizontal scroller that reveals a
    /// partially visible focused chip without moving the transcript; <see cref="AiSuggestionsLayout.Auto"/>
    /// is ScrollRow under 640 px via a container query.
    /// </summary>
    [Parameter] public AiSuggestionsLayout SuggestionsLayout { get; set; } = AiSuggestionsLayout.Wrap;

    /// <summary>
    /// Raised once after a running generation was stopped — by the built-in Stop control or by a host calling
    /// <see cref="StopAsync"/>. The component cancels the token it handed to the stream itself, so this is a
    /// notification, not a request: a handler must not call <see cref="StopAsync"/> again (it is a no-op, so
    /// it cannot loop). While a host-driven generation runs (<see cref="Generating"/> with no stream), the
    /// raised stop asks the host to cancel its own work instead, once per generation window.
    /// </summary>
    [Parameter] public EventCallback OnStop { get; set; }

    /// <summary>
    /// Whether the host's OWN generation — one that does not travel through <see cref="StreamAsync"/> — is
    /// running right now. The host sets it when it submits its request and clears it when the answer
    /// arrived; the component treats that window exactly like a stream: <see cref="IsGenerating"/> becomes
    /// true (the root's <c>data-generating</c>, the log's <c>aria-busy</c> and the built-in Stop pill
    /// follow), the true→false edge announces the completed answer when the last turn is
    /// <see cref="AiMessageStatus.Completed"/>, and <see cref="StopAsync"/> raises <see cref="OnStop"/> so
    /// the host can cancel its own work. Default false: a host that streams through
    /// <see cref="StreamAsync"/> binds nothing — the component drives the state itself and the edge stays
    /// silent, so a completion is never announced twice.
    /// </summary>
    [Parameter] public bool Generating { get; set; }

    /// <summary>
    /// Optional host formatter for the completion note spoken into the transcript live region. Invoked on
    /// the true→false generating edge and at the end of <see cref="StreamAsync"/>, only for the last turn
    /// whose <see cref="AiMessage.Status"/> is <see cref="AiMessageStatus.Completed"/>. A
    /// <see langword="null"/> return (or a null parameter) keeps the localized default
    /// <c>TmAiConversation_Completed</c>. Stopped and Failed turns never call the formatter.
    /// </summary>
    [Parameter] public Func<AiMessage, string?>? CompletionAnnouncement { get; set; }

    /// <summary>
    /// Text of the placeholder a turn with no content yet renders while it waits for its first part
    /// (<see cref="AiMessageStatus.Pending"/>). The value flows to the built-in <see cref="TmAiMessage"/>
    /// as its <c>PendingText</c>; a host with its own <see cref="MessageTemplate"/> passes
    /// <c>PendingText</c> itself. Default null renders no placeholder — the turn is simply empty until its
    /// first token arrives (the pre-2.12 behaviour; a localized default is planned for 3.0, where it
    /// becomes a deliberate visual change).
    /// </summary>
    [Parameter] public string? MessagePendingText { get; set; }

    /// <summary>
    /// How many of the newest turns are rendered; <see langword="null"/> (the default) renders every turn.
    /// A window keeps a very long transcript's DOM small, renders the contiguous tail and grows upwards by
    /// <see cref="RenderWindowStep"/> through the "Load older messages" control. Values below 10 are raised
    /// to 10 — a two-row window is a teaser, not a conversation.
    /// </summary>
    [Parameter] public int? RenderWindowSize { get; set; }

    /// <summary>How many turns one "Load older messages" activation adds. Default 50, raised to at least 10.</summary>
    [Parameter] public int RenderWindowStep { get; set; } = 50;

    /// <summary>
    /// Minimum time between two renders of a streamed answer. Default 50 ms. A Server circuit re-rendering per
    /// token is a render storm, so deltas arriving faster than this interval are folded into the message but
    /// not painted; the stream always renders once when it starts and once when it ends. Zero or a negative
    /// value renders every applied delta.
    /// </summary>
    [Parameter] public TimeSpan RenderThrottle { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Offer the built-in "Stop generating" control while a stream or a Generating window runs. Default
    /// true; a host that renders its own stop control (a composer, a toolbar) turns it off and calls
    /// <see cref="StopAsync"/> itself. The pill renders only while it can act — an uncancelled stream, or
    /// a host-driven window whose one stop has not been delivered yet.
    /// </summary>
    [Parameter] public bool ShowStop { get; set; } = true;

    /// <summary>
    /// Follow new content and offer the "New messages" affordance. Default true. When false the component
    /// imports no browser module at all and never scrolls — the host owns the scrolling surface.
    /// </summary>
    [Parameter] public bool AutoScroll { get; set; } = true;

    /// <summary>CSS length of the transcript (for example <c>"32rem"</c> or <c>"100%"</c>). Default null uses the stylesheet's height.</summary>
    [Parameter] public string? Height { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    private IReadOnlyList<AiMessage> MessageList => Messages ?? Array.Empty<AiMessage>();

    private IReadOnlyList<string> SuggestionList => Suggestions ?? Array.Empty<string>();

    private bool HasMessages => MessageList.Count > 0;

    private int WindowStep => Math.Max(MinRenderWindowStep, RenderWindowStep);

    private bool ShowLoadOlder => _windowStart > 0;

    /// <summary>
    /// Whether the built-in Stop affordance can DO something right now: an uncancelled stream token, or a
    /// host-driven window whose one stop has not been delivered yet. A pill that cannot act must not
    /// render — a control the reader can press but that does nothing is worse than a missing control.
    /// </summary>
    private bool CanStop => _streamCts is { IsCancellationRequested: false }
        || (_streamCts is null && Generating && OnStop.HasDelegate && !_hostStopRaised);

    /// <summary>Whether the floating bar (the "New messages" and "Stop generating" pills) renders.</summary>
    private bool ShowOverlayBar => _hasNewMessages || (CanStop && ShowStop);

    private string? HeightStyle => string.IsNullOrWhiteSpace(Height) ? null : $"block-size: {Height}";

    private string SuggestionsLayoutName => SuggestionsLayout switch
    {
        AiSuggestionsLayout.ScrollRow => "scroll-row",
        AiSuggestionsLayout.Auto => "auto",
        _ => "wrap",
    };

    private string SuggestionsClass => SuggestionsLayout switch
    {
        AiSuggestionsLayout.ScrollRow => "tm-ai-conversation__suggestions tm-ai-conversation__suggestions--scroll-row",
        AiSuggestionsLayout.Auto => "tm-ai-conversation__suggestions tm-ai-conversation__suggestions--auto",
        _ => "tm-ai-conversation__suggestions",
    };

    private string RootClass => string.Concat(
        "tm-ai-conversation",
        SuggestionsLayout == AiSuggestionsLayout.ScrollRow ? " tm-ai-conversation--suggestions-scroll-row" : string.Empty,
        SuggestionsLayout == AiSuggestionsLayout.Auto ? " tm-ai-conversation--suggestions-auto" : string.Empty,
        string.IsNullOrWhiteSpace(Class) ? string.Empty : $" {Class}");

    /// <summary>
    /// Whether a generation is running right now — a stream the component folded itself
    /// (<see cref="StreamAsync"/>) or a host-driven one (<see cref="Generating"/>). The host reads it to
    /// disable a send button or to render its own stop affordance; the value is also published on the root
    /// as <c>data-generating</c>.
    /// </summary>
    public bool IsGenerating => _streamCts is not null || Generating;

    /// <summary>
    /// The clock behind the render throttle. Resolved through the service provider so a host that never
    /// registers a <see cref="TimeProvider"/> still gets <see cref="TimeProvider.System"/>; tests register a
    /// <c>FakeTimeProvider</c>. The package never registers one itself.
    /// </summary>
    private TimeProvider Clock => _clock ??= Services.GetService<TimeProvider>() ?? TimeProvider.System;

    // ── Lifecycle ──────────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var messages = MessageList;
        var tailId = messages.Count > 0 ? messages[^1].Id : null;
        var contentChanged = !ReferenceEquals(messages, _lastMessages)
            || messages.Count != _lastMessageCount
            || !string.Equals(tailId, _lastTailId, StringComparison.Ordinal);

        SyncRenderWindow(messages, contentChanged);
        SyncGeneratingWindow(messages);

        if (contentChanged)
        {
            _contentDirty = true;
        }

        _lastMessages = messages;
        _lastMessageCount = messages.Count;
        _lastTailId = tailId;
    }

    /// <summary>
    /// The bookkeeping of a host-driven generation window. Opening one resets the two once-per-window
    /// flags; closing one announces the completed answer — the same note <see cref="StreamAsync"/> speaks,
    /// so a screen reader hears "Answer complete" no matter which half of the package drove the generation.
    /// A window whose answer already arrived through <see cref="StreamAsync"/> stays silent: the stream
    /// announced the completion, and <see cref="_completionAnnounced"/> is how the edge knows.
    /// </summary>
    private void SyncGeneratingWindow(IReadOnlyList<AiMessage> messages)
    {
        if (Generating && !_generating)
        {
            _completionAnnounced = false;
            _hostStopRaised = false;
        }
        else if (!Generating && _generating
            && !_completionAnnounced
            && messages.Count > 0
            && messages[^1].Status == AiMessageStatus.Completed)
        {
            Announce(ResolveCompletionText(messages[^1]));
        }

        _generating = Generating;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        CompletePendingRender(succeeded: !_disposed);
        _hasRendered = true;
        if (_disposed)
        {
            return;
        }

        if (_focusLogAfterRender)
        {
            _focusLogAfterRender = false;
            await FocusLogAsync();
        }
        else if (_focusLogWhenIdle && !ShowOverlayBar)
        {
            // A pill that had the focus was removed by the reader's own activation. Waiting until the whole
            // bar is gone means the move never takes the focus away from a pill that is still needed.
            _focusLogWhenIdle = false;
            await FocusLogAsync();
        }

        if (AutoScroll && !_attached)
        {
            await AttachAsync();
        }

        if (!firstRender && !_contentDirty)
        {
            return;
        }

        _contentDirty = false;

        if (!AutoScroll || !_attached)
        {
            return;
        }

        if (firstRender)
        {
            // A conversation opens at its latest turn; the reader never has to scroll down to find it.
            _stickToBottom = true;
            await ScrollToBottomCoreAsync(smooth: false);
            return;
        }

        // Once the module has reported a scroll position, that report is the state of record. Probing the
        // geometry on every render instead would be a Blazor round-trip for every parent render — exactly
        // the Server cost this component exists to avoid. The probe stays as the fallback for a transcript
        // the module has not reported on yet (the freshly attached one), so a conversation restored in a
        // scrolled-up position is not yanked to the bottom by its first appended turn.
        if (!_scrollReported)
        {
            var probed = await IsNearBottomAsync();
            if (probed is null)
            {
                return;
            }

            _stickToBottom = probed.Value;
        }

        if (_stickToBottom)
        {
            if (_hasNewMessages)
            {
                _hasNewMessages = false;
                await InvokeAsync(StateHasChanged);
            }

            await ScrollToBottomCoreAsync(smooth: true);
            return;
        }

        if (HasMessages && !_hasNewMessages)
        {
            _hasNewMessages = true;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ── Render window ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes the visible slice. The window is derived state, never a second copy of the list: the rows
    /// hold the host's own <see cref="AiMessage"/> instances, so a streamed turn keeps growing in place.
    /// </summary>
    private void SyncRenderWindow(IReadOnlyList<AiMessage> messages, bool contentChanged)
    {
        var count = messages.Count;

        if (RenderWindowSize is not { } requested)
        {
            _windowExpanded = false;
            _windowStart = 0;
            RebuildWindow();
            return;
        }

        var size = Math.Max(MinRenderWindowSize, requested);
        var replaced = _lastMessages is not null && !ReferenceEquals(messages, _lastMessages);
        if (!replaced && contentChanged && count == _lastMessageCount && count > 0 && _lastMessages is { Count: > 0 }
            && !string.Equals(messages[0].Id, _lastMessages[0].Id, StringComparison.Ordinal))
        {
            // Same instance, same size, different content: the host cleared and refilled the list.
            replaced = true;
        }

        if (replaced || _windowStart < 0 || _windowStart > count)
        {
            _windowExpanded = false;
            _windowStart = Math.Max(0, count - size);
        }
        else if (!_windowExpanded)
        {
            // The tail follows appended turns until the reader pulls the older ones in.
            _windowStart = Math.Max(0, count - size);
        }
        else
        {
            // An expanded window keeps its first row: the rows above the viewport must not unmount.
            _windowStart = Math.Clamp(_windowStart, 0, Math.Max(0, count - size));
        }

        RebuildWindow();
    }

    private void RebuildWindow()
    {
        var messages = MessageList;
        _windowMessages.Clear();
        for (var index = _windowStart; index < messages.Count; index++)
        {
            _windowMessages.Add(messages[index]);
        }
    }

    private async Task LoadOlderAsync()
    {
        if (_windowStart <= 0)
        {
            return;
        }

        _windowStart = Math.Max(0, _windowStart - WindowStep);
        _windowExpanded = true;
        RebuildWindow();

        if (_windowStart == 0)
        {
            // The control that had the focus just left the DOM: hand it to the transcript instead of
            // dropping the reader onto <body>.
            _focusLogAfterRender = true;
        }

        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Completes a waiter that asked to see the next paint (an expand that must land in the DOM before
    /// the module is asked to scroll to the newly mounted row). A disposed component answers
    /// <see langword="false"/> so the waiter cannot hang.
    /// </summary>
    private void CompletePendingRender(bool succeeded)
    {
        var pending = _renderCompletion;
        if (pending is null)
        {
            return;
        }

        _renderCompletion = null;
        pending.TrySetResult(succeeded);
    }

    /// <summary>
    /// Grows the render window so the named turn is in the DOM, without scrolling. Answers whether
    /// <paramref name="messageId"/> is in <see cref="Messages"/>. An unknown id, a host without a
    /// window, or a turn already inside the window leaves the slice unchanged.
    /// </summary>
    /// <param name="messageId">The <see cref="AiMessage.Id"/> of the turn that must be rendered.</param>
    /// <returns>Whether the named turn is in the host list.</returns>
    public Task<bool> ExpandRenderWindowToAsync(string messageId)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = InvokeAsync(async () =>
        {
            try
            {
                completion.TrySetResult(await ExpandWindowToCoreAsync(messageId));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return completion.Task;
    }

    /// <summary>Runs on the component's dispatcher — the guarded body of <see cref="ExpandRenderWindowToAsync"/>.</summary>
    private async Task<bool> ExpandWindowToCoreAsync(string messageId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        var messages = MessageList;
        var index = -1;
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(messages[i].Id, messageId, StringComparison.Ordinal))
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        if (RenderWindowSize is not { } requested)
        {
            return true;
        }

        if (_windowStart < 0)
        {
            var size = Math.Max(MinRenderWindowSize, requested);
            _windowStart = Math.Max(0, messages.Count - size);
        }

        if (index >= _windowStart)
        {
            return true;
        }

        var steps = (_windowStart - index + WindowStep - 1) / WindowStep;
        _windowStart = Math.Max(0, _windowStart - steps * WindowStep);
        _windowExpanded = true;
        RebuildWindow();

        if (_windowStart == 0)
        {
            _focusLogAfterRender = true;
        }

        if (!_hasRendered)
        {
            await InvokeAsync(StateHasChanged);
            return !_disposed;
        }

        var renderCompletion = _renderCompletion ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await InvokeAsync(StateHasChanged);
        await renderCompletion.Task;
        return !_disposed;
    }

    private async Task RaiseSuggestionAsync(string suggestion)
    {
        if (OnSuggestion.HasDelegate)
        {
            await OnSuggestion.InvokeAsync(suggestion);
        }
    }

    // ── Streaming ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Folds <paramref name="deltas"/> into <paramref name="message"/> — the turn the host already added to
    /// <see cref="Messages"/>. Each delta goes through <c>AiMessageStreamApplier</c>, renders are throttled to
    /// <see cref="RenderThrottle"/>, and the answer is finished when the stream ends: a stream that simply
    /// completes marks the message <see cref="AiMessageStatus.Completed"/> (a host needs no trailing
    /// <see cref="AiCompletedDelta"/>), a cancelled stream marks it <see cref="AiMessageStatus.Stopped"/> with
    /// the content that already arrived, and a stream that throws marks it
    /// <see cref="AiMessageStatus.Failed"/> with the exception message before the exception is rethrown for
    /// the host.
    /// </summary>
    /// <param name="message">The turn being streamed; it should already be part of <see cref="Messages"/>.</param>
    /// <param name="deltas">The host's stream; no LLM SDK is referenced by this package.</param>
    /// <param name="cancellationToken">The host's own cancellation, combined with <see cref="StopAsync"/>.</param>
    /// <returns>A task that completes when the stream finished, was stopped or failed.</returns>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="InvalidOperationException">A generation is already running.</exception>
    public async Task StreamAsync(
        AiMessage message,
        IAsyncEnumerable<AiStreamDelta> deltas,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(deltas);

        if (_streamCts is not null)
        {
            throw new InvalidOperationException(
                "A generation is already running; wait for it to finish or call StopAsync() before starting another one.");
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _streamCts = cts;
        _announcements.Clear();
        _completionAnnounced = false;
        _hostStopRaised = false;
        _contentDirty = true;

        var throttle = RenderThrottle > TimeSpan.Zero ? RenderThrottle : TimeSpan.Zero;
        var stopped = false;
        Exception? failure = null;

        try
        {
            // The start render paints the Stop affordance before the first token arrives — a stream that is
            // slow to produce its first delta must not look like a dead UI.
            await InvokeAsync(StateHasChanged);

            var lastRender = Clock.GetUtcNow();
            await foreach (var delta in deltas.WithCancellation(cts.Token))
            {
                if (cts.IsCancellationRequested)
                {
                    stopped = true;
                    break;
                }

                if (!AiMessageStreamApplier.Apply(message, delta, Clock.GetUtcNow()))
                {
                    continue;
                }

                var now = Clock.GetUtcNow();
                if (throttle > TimeSpan.Zero && now - lastRender < throttle)
                {
                    continue;
                }

                lastRender = now;
                _contentDirty = true;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            stopped = true;
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            if (ReferenceEquals(_streamCts, cts))
            {
                _streamCts = null;
            }

            cts.Dispose();
        }

        if (failure is not null)
        {
            if (!IsTerminal(message.Status))
            {
                message.Status = AiMessageStatus.Failed;
                message.Error = failure.Message;
            }
        }
        else if (stopped)
        {
            if (!IsTerminal(message.Status))
            {
                message.Status = AiMessageStatus.Stopped;
            }
        }
        else if (!IsTerminal(message.Status))
        {
            message.Status = AiMessageStatus.Completed;
        }

        if (failure is null && !stopped && message.Status == AiMessageStatus.Completed)
        {
            Announce(ResolveCompletionText(message));
            _completionAnnounced = true;
        }

        _contentDirty = true;
        await InvokeAsync(StateHasChanged);

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>
    /// Stops the running generation. With a stream running, the token handed to the host's stream is
    /// cancelled, the partial answer is kept and marked <see cref="AiMessageStatus.Stopped"/>, and
    /// <see cref="OnStop"/> is raised once. When that stream runs inside a host-driven window
    /// (<see cref="Generating"/> set), the raise consumes the window's one stop
    /// (<c>_hostStopRaised</c>), so a host that keeps its flag up after the stream ends is never asked a
    /// second time — and the pill loses its action and disappears with it. With a host-driven generation
    /// instead (<see cref="Generating"/> set, no stream), there is no token to cancel — the raised
    /// <see cref="OnStop"/> IS the stop: it asks the host to cancel its own work, once per generation
    /// window. A call while nothing is running is a no-op, so a host may call it defensively.
    /// </summary>
    /// <returns>A task that completes when the cancellation was requested (or the stop was delivered).</returns>
    public async Task StopAsync()
    {
        var cts = _streamCts;
        if (cts is null)
        {
            if (Generating && !_hostStopRaised && OnStop.HasDelegate)
            {
                _hostStopRaised = true;
                await OnStop.InvokeAsync();
            }

            return;
        }

        if (cts.IsCancellationRequested)
        {
            return;
        }

        // The raise below is the window's ONE stop when the host drives Generating as well: mark it as
        // spent BEFORE the cancellation lands, so a host still busy-flagged after the stream ends cannot
        // be asked again (and the pill's guard turns off in the same render).
        _hostStopRaised = true;
        cts.Cancel();

        if (OnStop.HasDelegate)
        {
            await OnStop.InvokeAsync();
        }
    }

    /// <summary>
    /// Handler of the built-in "Stop generating" pill. Stopping removes the pill, so the focus goes to the
    /// transcript once the bar is empty rather than to the document body.
    /// </summary>
    private async Task OnStopClickAsync()
    {
        _focusLogWhenIdle = true;
        await StopAsync();
    }

    /// <summary>
    /// Speaks <paramref name="text"/> into the transcript live region. Each call is a new keyed node
    /// (even when the text repeats), so a screen reader hears it again. Dispatched through
    /// <see cref="ComponentBase.InvokeAsync(Action)"/> so a host can call it from outside the renderer.
    /// </summary>
    /// <param name="text">The announcement. Null or whitespace is a no-op.</param>
    public Task AnnounceAsync(string text) => InvokeAsync(() =>
    {
        Announce(text);
        StateHasChanged();
    });

    private void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _announcementSequence++;
        _announcements.Add((_announcementSequence, text));
    }

    /// <summary>
    /// The text spoken on a completed last turn: the host formatter when it returns a value, otherwise
    /// the localized default. The formatter is invoked only for <see cref="AiMessageStatus.Completed"/>.
    /// </summary>
    private string ResolveCompletionText(AiMessage last)
    {
        if (last.Status != AiMessageStatus.Completed)
        {
            return Loc["TmAiConversation_Completed"];
        }

        return CompletionAnnouncement?.Invoke(last) ?? Loc["TmAiConversation_Completed"];
    }

    private static bool IsTerminal(AiMessageStatus status)
        => status is AiMessageStatus.Completed or AiMessageStatus.Stopped or AiMessageStatus.Failed;

    // ── Scroll — reported by the module ────────────────────────────────────────────────────────

    /// <summary>
    /// Called by the package's ES module whenever the reader scrolls the transcript. The reported position is
    /// what decides whether the next turn may follow the content; a reader back at the bottom clears the
    /// "New messages" affordance.
    /// </summary>
    /// <param name="isNearBottom">Whether the scroll position is within the module's bottom tolerance.</param>
    /// <returns>A task that completes when the state was applied.</returns>
    [JSInvokable]
    public Task OnScrollStateAsync(bool isNearBottom) => InvokeAsync(() =>
    {
        if (_disposed)
        {
            return;
        }

        _scrollReported = true;
        _stickToBottom = isNearBottom;
        if (isNearBottom && _hasNewMessages)
        {
            _hasNewMessages = false;
            StateHasChanged();
        }
    });

    /// <summary>
    /// Called by the package's ES module when the transcript's geometry changed size — the CONTENT (a
    /// streamed token, an image that finished loading, an expanded panel) or, since the box-own observer,
    /// the LOG BOX ITSELF (the host resized the panel, a layout split moved). While a generation runs and
    /// the reader is away from the bottom, the growth is reported as new content instead of pulling the
    /// viewport away.
    /// </summary>
    /// <remarks>
    /// The value is the position the reader had BEFORE the change, which is the module's contract: a reader
    /// sitting at the bottom is outside the bottom band the instant a token makes the content taller (or a
    /// shrink moves the bottom), so a post-change measurement would answer "detached" for the reader who is
    /// watching the answer arrive.
    /// </remarks>
    /// <param name="isNearBottom">Whether the scroll position was within the module's bottom tolerance.</param>
    /// <returns>A task that completes when the state was applied.</returns>
    [JSInvokable]
    public Task OnContentResizedAsync(bool isNearBottom) => InvokeAsync(async () =>
    {
        if (_disposed)
        {
            return;
        }

        _scrollReported = true;
        _stickToBottom = isNearBottom;
        if (isNearBottom)
        {
            if (_hasNewMessages)
            {
                _hasNewMessages = false;
                StateHasChanged();
            }

            // A box resize is a one-off correction, not an animation the reader is watching: jump to the
            // new bottom instead of flying there (a smooth scroll during a shrink reads as judder).
            await ScrollToBottomCoreAsync(smooth: false);
            return;
        }

        if (IsGenerating && HasMessages && !_hasNewMessages)
        {
            _hasNewMessages = true;
            StateHasChanged();
        }
    });

    private async Task JumpToLatestAsync() => await JumpToLatestCoreAsync();

    /// <summary>
    /// Handler of the built-in "New messages" pill. The pill stops being rendered by this very activation,
    /// so the focus is handed to the transcript instead of being dropped onto <c>&lt;body&gt;</c>.
    /// </summary>
    private async Task OnNewMessagesClickAsync()
    {
        _focusLogWhenIdle = true;
        await JumpToLatestCoreAsync();
    }

    private async Task JumpToLatestCoreAsync()
    {
        _hasNewMessages = false;
        _stickToBottom = true;
        await ScrollToBottomCoreAsync(smooth: true);
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Follows the newest turn on the host's behalf — the SAME jump the built-in "New messages" pill
    /// performs, not just another scroll probe: the pill (if it was up) is dismissed and the follow is
    /// re-armed, so the next appended turn is followed again instead of pill-ed. A host calls it when its
    /// own state says the reader should be at the bottom (its own "jump to latest" control, or after it
    /// finished a host-driven <see cref="Generating"/> window it kept off the bottom). Without the browser
    /// module (<c>AutoScroll="false"</c>, static rendering) the call is a quiet no-op: the host that owns
    /// the scrolling surface owns the scrolling.
    /// </summary>
    /// <returns>A task that completes when the jump was applied.</returns>
    public Task ScrollToBottomAsync() => InvokeAsync(async () =>
    {
        if (_disposed)
        {
            return;
        }

        await JumpToLatestCoreAsync();
    });

    /// <summary>
    /// Brings the turn with <paramref name="messageId"/> into view — the row the transcript rendered with
    /// <c>data-message-id</c>. The module scrolls ONLY the transcript (the page the component sits on never
    /// moves), marks the scroll as its own so the animation cannot be reported as the reader detaching, and
    /// honours reduced motion; the mark's release reports the honest position when it differs from the
    /// detached baseline the jump set. Unless the named turn IS the newest one, the jump also detaches the
    /// follow: the next appended turn is news for a reader who was just taken somewhere else, so it pops
    /// the "New messages" pill instead of being followed. When a render window is in effect and the named
    /// turn sits above it, the window grows first (see <see cref="ExpandRenderWindowToAsync"/>) so the row
    /// is in the DOM before the module is asked to scroll. The method answers whether the named turn
    /// actually moved: <see langword="false"/> for an empty id, without the browser module
    /// (<c>AutoScroll="false"</c>, static rendering), for an id that is in neither the host list nor the
    /// transcript, or when the interop failed.
    /// </summary>
    /// <param name="messageId">The <see cref="AiMessage.Id"/> of the turn to show.</param>
    /// <returns>Whether the transcript scrolled to the named turn.</returns>
    public Task<bool> ScrollToMessageAsync(string messageId)
    {
        // ComponentBase.InvokeAsync has no value-returning overload, so the dispatcher hop carries the
        // answer out through a completion source: the work (and the module call inside it) still runs on
        // the component's dispatcher — the same thread the scroll state lives on.
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = InvokeAsync(async () =>
        {
            try
            {
                completion.TrySetResult(await ScrollToMessageCoreAsync(messageId));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return completion.Task;
    }

    /// <summary>Runs on the component's dispatcher — the guarded body of <see cref="ScrollToMessageAsync"/>.</summary>
    private async Task<bool> ScrollToMessageCoreAsync(string messageId)
    {
        if (_disposed || string.IsNullOrWhiteSpace(messageId))
        {
            return false;
        }

        await ExpandWindowToCoreAsync(messageId);
        if (_disposed)
        {
            return false;
        }

        var module = _module;
        if (module is null || !_attached)
        {
            return false;
        }

        try
        {
            var moved = await module.InvokeAsync<bool>("scrollToMessage", _logRef, messageId, true);
            if (moved && !IsLastTurn(messageId))
            {
                // The reader was taken AWAY from the bottom: the honest baseline for the next growth is
                // detached, and the module's own release report keeps its internal state in sync.
                _stickToBottom = false;
                _scrollReported = true;
            }

            return moved;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="messageId"/> names the newest turn of the host's list.</summary>
    private bool IsLastTurn(string messageId)
        => MessageList.Count > 0 && string.Equals(MessageList[^1].Id, messageId, StringComparison.Ordinal);

    // ── Interop plumbing ──────────────────────────────────────────────────────────────────────

    private async Task AttachAsync()
    {
        var module = await EnsureModuleAsync();
        if (module is null)
        {
            return;
        }

        try
        {
            _self ??= DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("attach", _logRef, _contentRef, _self);
            _attached = true;
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // Static rendering: the transcript still renders, the scroll tracking is best effort.
        }
    }

    private async Task<bool?> IsNearBottomAsync()
    {
        var module = _module;
        if (module is null)
        {
            return null;
        }

        try
        {
            return await module.InvokeAsync<bool>("isNearBottom", _logRef);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            return null;
        }
    }

    private async Task ScrollToBottomCoreAsync(bool smooth)
    {
        var module = _module;
        if (module is null || !_attached)
        {
            return;
        }

        try
        {
            await module.InvokeVoidAsync("scrollToBottom", _logRef, smooth);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
        }
    }

    private async Task FocusLogAsync()
    {
        try
        {
            await _logRef.FocusAsync();
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // No interop (static rendering): the expansion still works, the focus move is best effort.
        }
    }

    private async Task<IJSObjectReference?> EnsureModuleAsync()
        => AutoScroll ? await EnsureInteropModuleAsync() : null;

    private async Task<IJSObjectReference?> EnsureInteropModuleAsync()
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
        CompletePendingRender(succeeded: false);
        _streamCts?.Cancel();

        var module = _module;
        _module = null;
        if (module is not null && _attached)
        {
            try
            {
                await module.InvokeVoidAsync("detach", _logRef);
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
            }
        }

        _attached = false;
        _self?.Dispose();
        _self = null;

        if (module is not null)
        {
            try
            {
                await module.DisposeAsync();
            }
            catch (Exception exception) when (IsInteropFailure(exception))
            {
            }
        }
    }
}
