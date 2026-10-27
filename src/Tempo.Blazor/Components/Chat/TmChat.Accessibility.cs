using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Threading;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Accessibility half of <see cref="TmChat"/> (FZE-7): the component's SINGLE live region, the
/// trailing-throttled announcement pipeline whose text the HOST supplies, the focus hooks and the
/// thread-panel focus restoration.
/// <para>
/// The rules that shape this half: TmChat renders exactly ONE live region (the announcements
/// region) — the banner, the typing indicator and every template stay silent, and the messages
/// container is a labelled <c>role="region"</c> rather than a <c>role="log"</c> (which would
/// imply a second live region). The library never invents sentences about messages:
/// <see cref="MessageAnnouncement"/> returns the text per message (a <see langword="null"/>
/// result means "do not announce this one"), <see cref="BatchAnnouncement"/> summarizes a
/// window-held burst of messages and <see cref="AnnounceAsync"/> announces host actions ("Draft
/// approved"). Typing is NEVER announced. Split out of <c>TmChat.razor.cs</c> to keep that file
/// under the 1200-line source ceiling (N270/F13), like <c>TmChat.TypingExpiry.cs</c>.
/// </para>
/// </summary>
public partial class TmChat
{
    // ── Parameters ──

    /// <summary>
    /// The text announced for ONE new message, supplied by the host (localized, with its domain
    /// context — e.g. <c>m =&gt; $"Nová zpráva od {m.Author?.Name}"</c>). The component never
    /// invents sentences. A <see langword="null"/> func or a <see langword="null"/>/whitespace
    /// result means "do not announce this message" — the default state is a silent chat.
    /// <para>
    /// Only messages APPENDED to the end of the rendered root list are candidates: the initial
    /// load, a fill from an empty list and a PREPEND of older history are not announcements (a
    /// loaded history is not news), a tail-replaced reload re-announces only ids the component
    /// has never seen, thread replies render in the thread panel (announce them through
    /// <see cref="AnnounceAsync"/> if the host needs to) and the user's OWN messages are skipped
    /// unless <see cref="AnnounceOwnMessages"/> is <see langword="true"/>. Announcements share
    /// the <see cref="AnnouncementThrottle"/> trailing window with <see cref="AnnounceAsync"/> —
    /// a burst within the window is summarized by <see cref="BatchAnnouncement"/> or collapsed
    /// to the LATEST text.
    /// </para>
    /// </summary>
    [Parameter] public Func<ChatMessage, string?>? MessageAnnouncement { get; set; }

    /// <summary>
    /// Optional summary for a burst announced TOGETHER: when at least TWO messages wait in one
    /// <see cref="AnnouncementThrottle"/> window, the trailing flush announces this instead of
    /// the last individual text — e.g. <c>batch =&gt; $"{batch.Count} new messages"</c>. The
    /// delegate receives the messages held by the window (the ones whose
    /// <see cref="MessageAnnouncement"/> produced text). A <see langword="null"/> result falls
    /// back to the last individual message text. A host action announced through
    /// <see cref="AnnounceAsync"/> in the same window is composed AROUND the summary (or the
    /// last message text) — the summary is consulted only for message-only bursts of two or
    /// more, never for a single message.
    /// </summary>
    [Parameter] public Func<IReadOnlyList<ChatMessage>, string?>? BatchAnnouncement { get; set; }

    /// <summary>
    /// The trailing throttle window shared by the message pipeline and <see cref="AnnounceAsync"/>.
    /// Default 1 second. Within the window the latest announcement wins and is flushed when the
    /// window ends; a free window flushes immediately (the first message of a quiet conversation
    /// is never delayed). A non-positive value disables throttling — every announcement flushes
    /// immediately. The clock is the injected <see cref="TimeProvider"/> (fallback
    /// <see cref="TimeProvider.System"/>), so tests drive the window with a
    /// <c>FakeTimeProvider</c>; the pending timer is cancelled in <see cref="DisposeAsync"/>.
    /// </summary>
    [Parameter] public TimeSpan AnnouncementThrottle { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Whether the current user's OWN messages are announced. Default <see langword="false"/> —
    /// the user knows what they typed. "Own" matches the built-in bubble logic: a
    /// <see cref="ChatMessageType.Outgoing"/> message or one authored by
    /// <see cref="CurrentUser"/>. System messages are NOT own and are announced like any other
    /// message when <see cref="MessageAnnouncement"/> produces text for them.
    /// </summary>
    [Parameter] public bool AnnounceOwnMessages { get; set; }

    /// <summary>
    /// The accessible name of the messages container (a scrollable <c>role="region"</c>). Default:
    /// the localized <c>TmChat_MessagesLabel</c>. The container is deliberately NOT a
    /// <c>role="log"</c> — <c>log</c> implies <c>aria-live</c> and would make a second live region
    /// out of it; new messages are announced through the component's own region instead.
    /// <para>
    /// A page with SEVERAL chats should pass a DISTINCT label per chat — the container is a
    /// named landmark and a Tab stop, so two chats labelled "Messages" are indistinguishable in
    /// the landmark list / when cycling regions (e.g.
    /// <c>MessagesAriaLabel="Support channel"</c> next to <c>MessagesAriaLabel="Team chat"</c>).
    /// </para>
    /// </summary>
    [Parameter] public string? MessagesAriaLabel { get; set; }

    /// <summary>
    /// The accessible name of the built-in composer textarea. Default: the effective placeholder
    /// (the host's <see cref="Placeholder"/> or the localized <c>TmChat_Placeholder</c>) — until
    /// this parameter existed the textarea had only a placeholder, which is no accessible name.
    /// Ignored with a <see cref="ComposerTemplate"/> (the template owns its own input).
    /// </summary>
    [Parameter] public string? ComposerAriaLabel { get; set; }

    // ── Announcement state ──

    /// <summary>Messages held by the current throttle window, with their already-computed texts.
    /// A <see langword="null"/> <see cref="ChatMessage"/> entry is a HOST action text announced
    /// through <see cref="AnnounceAsync"/>.</summary>
    private readonly List<(ChatMessage? Message, string Text)> _pendingAnnouncements = [];

    /// <summary>The current content of the live region; <see langword="null"/> renders it empty.</summary>
    private string? _announcement;

    /// <summary>
    /// Bumped on every announcement — the <c>@key</c> of the live region's content span. A NEW
    /// key makes Blazor destroy the old span and insert a fresh one, so an announcement of the
    /// SAME sentence re-announces (a live region ignores an unchanged DOM text node) without a
    /// two-phase clear-then-set render.
    /// </summary>
    private int _announcementSeq;

    /// <summary>Test-visible state: how many announcements were rendered so far.</summary>
    internal int AnnouncementSequence => _announcementSeq;

    /// <summary>The pending trailing-flush timer (at most one — the window has one end).</summary>
    private ITimer? _announcementTimer;

    /// <summary>
    /// Monotonic generation of the announcement timer: bumped when a timer is armed and when one
    /// is cancelled, so a fired callback can tell whether it still owns the CURRENT window.
    /// </summary>
    private int _announcementTimerGen;

    /// <summary>When the last flush STARTED — the origin of the current throttle window.</summary>
    private DateTimeOffset? _lastAnnouncedAt;

    /// <summary>A leading flush is due in the awaited half of <see cref="OnParametersSetAsync"/>.</summary>
    private bool _announceNow;

    /// <summary>Test-visible state: is a trailing-flush timer still armed?</summary>
    internal bool AnnouncementTimerIsPending => _announcementTimer is not null;

    /// <summary>Test-visible state: the current live-region content.</summary>
    internal string? CurrentAnnouncement => _announcement;

    /// <summary>The effective throttle: a non-positive parameter disables the window entirely.</summary>
    private TimeSpan EffectiveThrottle
        => AnnouncementThrottle <= TimeSpan.Zero ? TimeSpan.Zero : AnnouncementThrottle;

    /// <summary>
    /// Whether the throttle window is free at <paramref name="now"/>: no flush has happened yet,
    /// the parameter disabled throttling, or the last flush is older than the window.
    /// </summary>
    private bool AnnouncementWindowIsFree(DateTimeOffset now)
        => EffectiveThrottle <= TimeSpan.Zero
            || _lastAnnouncedAt is null
            || now - _lastAnnouncedAt.Value >= EffectiveThrottle;

    // ── The appended-batch feeder (TmChat.Scroll.cs classifies, this half announces) ──

    /// <summary>
    /// The root messages appended at the END of the rendered list, captured by
    /// <see cref="DetectScrollChange"/> (an initial fill and a prepend never land here). Consumed
    /// — and cleared — by <see cref="ScheduleAnnouncements"/> on the same parameter set.
    /// </summary>
    private readonly List<ChatMessage> _appendedRoots = [];

    /// <summary>
    /// Every root message id the component has seen rendered — the announcement dedup of a
    /// tail-replaced reload: a host reloading the conversation re-delivers ids the component
    /// already announced (or saw on the first set), and only NEVER-SEEN ids in an appended
    /// batch are news. Seeded with the whole root list on the first parameter set, on a prepend
    /// and after every other change kind (see <see cref="DetectScrollChange"/>).
    /// </summary>
    private readonly HashSet<string> _seenRootIds = [];

    /// <summary>
    /// Collects the announcement texts of the appended messages and schedules the flush. Runs
    /// SYNCHRONOUSLY from <see cref="OnParametersSetAsync"/> before the first await — the actual
    /// flush happens in the awaited half (<see cref="FlushAnnouncementsIfDueAsync"/>), while a
    /// busy window arms the trailing timer that flushes at its end.
    /// </summary>
    private void ScheduleAnnouncements()
    {
        var arrivals = _appendedRoots.ToArray();
        _appendedRoots.Clear();
        if (MessageAnnouncement is not null)
        {
            foreach (var message in arrivals)
            {
                if (!ShouldAnnounce(message)) continue;
                var text = MessageAnnouncement(message);
                if (string.IsNullOrWhiteSpace(text)) continue;
                _pendingAnnouncements.Add((message, text));
            }
        }

        if (_pendingAnnouncements.Count == 0) return;
        var now = Clock.GetUtcNow();
        if (AnnouncementWindowIsFree(now))
        {
            _announceNow = true;
            return;
        }

        ArmAnnouncementTimer(now);
    }

    /// <summary>Whether the message would be announced at all (the own-message filter).</summary>
    private bool ShouldAnnounce(ChatMessage message)
        => MessageAnnouncement is not null && (AnnounceOwnMessages || !IsOwnMessage(message));

    /// <summary>
    /// "Own" exactly as the built-in bubbles draw it: an outgoing type or the current user as the
    /// author. The user knows what they typed — own messages are not announced by default.
    /// </summary>
    private bool IsOwnMessage(ChatMessage message)
        => message.Type == ChatMessageType.Outgoing || IsCurrentUser(message.Author);

    /// <summary>
    /// Arms the single trailing-flush timer for the REMAINDER of the current window. A timer that
    /// is already armed is kept (it fires at the window's end and flushes whatever accumulated by
    /// then — the latest wins). A window that is already over flushes through the awaited half.
    /// </summary>
    private void ArmAnnouncementTimer(DateTimeOffset now)
    {
        if (_announcementTimer is not null) return;
        var remaining = _lastAnnouncedAt is { } last
            ? EffectiveThrottle - (now - last)
            : EffectiveThrottle;
        if (remaining <= TimeSpan.Zero)
        {
            _announceNow = true;
            return;
        }

        // Single-shot timer on the injected clock. The callback closes over `timer` AND the
        // generation: the ReferenceEquals check can tell the currently armed timer from a
        // replaced one, and the generation check invalidates a callback whose window was
        // cancelled (DisposeAsync) or superseded between firing and dispatching.
        var gen = ++_announcementTimerGen;
        ITimer? timer = null;
        timer = Clock.CreateTimer(_ => _ = OnAnnouncementTimerFiredAsync(timer, gen), null, remaining, Timeout.InfiniteTimeSpan);
        _announcementTimer = timer;
    }

    private void CancelAnnouncementTimer()
    {
        // Bump the generation FIRST: a callback already in flight must observe a cancelled
        // window even if it races this call on another thread.
        _announcementTimerGen++;
        _announcementTimer?.Dispose();
        _announcementTimer = null;
    }

    private async Task OnAnnouncementTimerFiredAsync(ITimer? timer, int generation)
    {
        if (timer is null || _disposed) return;
        // The callback runs on a thread-pool thread (TimeProvider.System) or the test thread
        // advancing a FakeTimeProvider; hop to the renderer before touching announcement state.
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed
                    || generation != _announcementTimerGen
                    || !ReferenceEquals(_announcementTimer, timer))
                {
                    return Task.CompletedTask;
                }

                _announcementTimer = null;
                timer.Dispose();
                return FlushPendingAnnouncementsAsync();
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer (or the whole component) was disposed between the timer firing and the
            // dispatch — the announcement no longer has anywhere to render, not an error.
        }
    }

    /// <summary>
    /// The awaited half of the schedule: performs the LEADING flush when the window was free at
    /// schedule time. A no-op when the pending batch is empty (the trailing timer owns those).
    /// </summary>
    private async Task FlushAnnouncementsIfDueAsync()
    {
        if (!_announceNow || _disposed) return;
        _announceNow = false;
        await FlushPendingAnnouncementsAsync();
    }

    /// <summary>
    /// Flushes everything held by the current window: the composed text becomes the live region's
    /// content and a fresh window starts. Idempotent — an empty batch is a no-op, so a flush that
    /// races an already-consumed schedule never re-announces.
    /// </summary>
    private async Task FlushPendingAnnouncementsAsync()
    {
        if (_pendingAnnouncements.Count == 0 || _disposed) return;
        var batch = _pendingAnnouncements.ToArray();
        _pendingAnnouncements.Clear();
        _lastAnnouncedAt = Clock.GetUtcNow();

        var text = ComposeAnnouncementText(batch, BatchAnnouncement);
        if (string.IsNullOrWhiteSpace(text)) return;
        ShowAnnouncement(text);
    }

    /// <summary>
    /// Composes the flush text out of the held batch:
    /// <list type="bullet">
    /// <item>one entry → its own text (a single message NEVER consults the summary);</item>
    /// <item>two or more MESSAGES → <see cref="BatchAnnouncement"/>'s summary when it produces
    /// text, else the latest message's text;</item>
    /// <item>a HOST action mixed with messages → the host text COMPOSED WITH the message part
    /// (<c>"Draft approved 2 new messages"</c> / <c>"Draft approved Hello"</c>), so both
    /// survive one window; the summary is still only consulted for 2+ messages.</item>
    /// </list>
    /// </summary>
    private static string? ComposeAnnouncementText(
        IReadOnlyList<(ChatMessage? Message, string Text)> batch,
        Func<IReadOnlyList<ChatMessage>, string?>? batchAnnouncement)
    {
        if (batch.Count == 0) return null;

        var hostTexts = batch.Where(entry => entry.Message is null).ToArray();
        var hostText = hostTexts.Length > 0 ? hostTexts[^1].Text : null; // the latest action wins

        var messages = batch.Where(entry => entry.Message is not null).ToArray();
        string? messagePart;
        if (messages.Length == 0)
        {
            messagePart = null;
        }
        else if (messages.Length == 1)
        {
            messagePart = messages[0].Text;
        }
        else if (batchAnnouncement is not null)
        {
            var summary = batchAnnouncement(messages.Select(entry => entry.Message!).ToArray());
            messagePart = string.IsNullOrWhiteSpace(summary) ? messages[^1].Text : summary;
        }
        else
        {
            messagePart = messages[^1].Text;
        }

        if (string.IsNullOrWhiteSpace(hostText)) return messagePart;
        if (string.IsNullOrWhiteSpace(messagePart)) return hostText;
        return $"{hostText} {messagePart}";
    }

    /// <summary>
    /// Writes the text into the live region: the content is a span keyed by the announcement
    /// sequence, so every announcement REPLACES the previous span node. A live region ignores an
    /// unchanged DOM text node — the fresh node (even for the SAME sentence, a repeated status)
    /// re-announces without a two-phase clear-then-set render.
    /// </summary>
    private void ShowAnnouncement(string text)
    {
        if (_disposed) return;
        _announcementSeq++;
        _announcement = text;
        StateHasChanged();
    }

    /// <summary>
    /// Announces a HOST action through the same single live region and the same throttle — e.g.
    /// "Draft approved" after the host's own flow completes. A free window announces immediately
    /// and DROPS any pending messages (they arrived in the same window that is now spent — the
    /// action is the news). A busy window KEEPS the pending messages and records the action; the
    /// trailing flush composes the action text around the summary or the last message text (see
    /// <see cref="ComposeAnnouncementText"/>), so a host action and the messages of one window
    /// both survive. Dispatched through <see cref="InvokeAsync"/> so it is safe from any thread;
    /// whitespace is a no-op that never clears the region.
    /// </summary>
    public Task AnnounceAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        return InvokeAsync(() =>
        {
            if (_disposed) return;
            var now = Clock.GetUtcNow();
            if (AnnouncementWindowIsFree(now))
            {
                // The window is free: announce the action NOW. Pending messages that arrived in
                // the just-spent window would flush after it and read as a second, late news —
                // drop them instead of queueing the host text behind (or over) them.
                _pendingAnnouncements.Clear();
                _lastAnnouncedAt = now;
                ShowAnnouncement(text);
                return;
            }

            // Within the window: the pending messages STAY, the action joins them (replacing any
            // earlier action — the latest request wins) and the armed trailing timer flushes the
            // composition at the window's end.
            _pendingAnnouncements.RemoveAll(entry => entry.Message is null);
            _pendingAnnouncements.Add((null, text));
            ArmAnnouncementTimer(now);
        });
    }

    // ── Focus hooks ──

    /// <summary>
    /// Moves keyboard focus to the message with the given id — the wrapper element carrying
    /// <c>data-message-id</c> (the <c>chat.js</c> scroller gives it <c>tabindex="-1"</c>,
    /// scrolls it into view with <c>block: 'nearest'</c> honouring reduced motion and focuses it
    /// with <c>preventScroll</c>). Returns whether the message was found and focused: the query
    /// is SCOPED to the messages container (the thread panel renders the root message a second
    /// time, so a page-wide query would be ambiguous), <see langword="false"/> for an unknown or
    /// empty id, before interactivity, or with a dropped circuit. Dispatched through
    /// <see cref="InvokeAsync"/> so it is safe from any thread.
    /// </summary>
    public async ValueTask<bool> FocusMessageAsync(string? id)
    {
        var found = false;
        await InvokeAsync(async () => found = await FocusMessageCoreAsync(id));
        return found;
    }

    /// <summary>The core focus move — returns whether the JS scroller found (and focused) the row.</summary>
    private async Task<bool> FocusMessageCoreAsync(string? id)
    {
        if (_disposed || string.IsNullOrWhiteSpace(id)) return false;
        // FZE-14: an anchor ABOVE the render window is mounted first (a render prepend with the
        // anchor restore, awaited through the after-render bookkeeping); a thread reply or an
        // unknown id changes nothing — the JS call below keeps today's semantics (not found).
        await EnsureMessageInRenderWindowAsync(id);
        var scroller = _scroller;
        if (scroller is null) return false;
        try
        {
            return await scroller.InvokeAsync<bool>("focusMessageVisible", id);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Best-effort focus: an unreachable circuit loses the focus, not the messages.
            return false;
        }
    }

    // ── Thread-opener focus restoration ──

    /// <summary>
    /// Which built-in affordance opened the thread (its button gets the focus back).
    /// </summary>
    private enum ThreadOpenerKind
    {
        /// <summary>The thread badge under the bubble ("N replies").</summary>
        Badge,

        /// <summary>The "Reply" action button of the message's action bar.</summary>
        ReplyAction,
    }

    private readonly Dictionary<string, ElementReference> _threadBadgeRefs = [];
    private readonly Dictionary<string, ElementReference> _threadReplyRefs = [];

    /// <summary>The message id and the kind of the button that opened the CURRENT thread session.</summary>
    private string? _threadOpenerId;
    private ThreadOpenerKind? _threadOpenerKind;

    /// <summary>The thread id the captured opener belongs to (a mismatch means a host-driven open).</summary>
    private string? _threadOpenerThreadId;

    /// <summary>
    /// Set by <see cref="OpenThreadAsync"/> and consumed by the next render — the render that
    /// first shows the panel is the one that legalizes the capture.
    /// </summary>
    private bool _threadOpenerCapturedThisRender;

    /// <summary>Set by <see cref="OpenThreadAsync"/>: the panel's reply input gets the focus after the render that mounts it (best effort).</summary>
    private bool _focusThreadInputAfterRender;

    /// <summary>The thread-panel open state as of the last render (for the close transition).</summary>
    private bool _threadOpenAtLastRender;

    /// <summary>Records the opener of the thread being opened (the button regains focus on close).</summary>
    private void CaptureThreadOpener(string messageId, ThreadOpenerKind kind)
    {
        _threadOpenerId = messageId;
        _threadOpenerKind = kind;
        _threadOpenerThreadId = messageId;
        _threadOpenerCapturedThisRender = true;
    }

    private void ClearThreadOpener()
    {
        _threadOpenerId = null;
        _threadOpenerKind = null;
        _threadOpenerThreadId = null;
    }

    /// <summary>
    /// Drops the affordance references of messages that no longer render — Blazor leaves the
    /// dictionary keys behind when a row (or only its badge / reply button) unmounts, and a stale
    /// entry would hand a detached element to <c>restoreFocus</c>. The current OPENER is kept:
    /// its entry is still needed by the close path of the render in progress.
    /// </summary>
    private void PruneThreadRefs()
    {
        if (_threadBadgeRefs.Count == 0 && _threadReplyRefs.Count == 0) return;
        var rendered = new HashSet<string>(RootMessages().Select(m => m.Id));
        PruneRefs(_threadBadgeRefs, rendered);
        PruneRefs(_threadReplyRefs, rendered);

        static void PruneRefs(Dictionary<string, ElementReference> refs, HashSet<string> renderedIds)
        {
            List<string>? dead = null;
            foreach (var id in refs.Keys)
            {
                if (renderedIds.Contains(id)) continue;
                dead ??= [];
                dead.Add(id);
            }

            if (dead is null) return;
            foreach (var id in dead) refs.Remove(id);
        }
    }

    /// <summary>
    /// The element reference of the captured opener button — but ONLY when the opener is still a
    /// root message whose affordance would render. A message that left <see cref="Messages"/>,
    /// lost its replies or renders through a host template has no live button to focus; a stale
    /// reference must never reach <c>restoreFocus</c> (the JS side would then have to fall back
    /// to an element that no longer exists).
    /// </summary>
    private ElementReference? ThreadOpenerRestoreTarget
    {
        get
        {
            if (_threadOpenerId is null || _threadOpenerKind is not { } kind) return null;
            var root = RootMessages().FirstOrDefault(m => m.Id == _threadOpenerId);
            if (root is null) return null;
            var renders = kind switch
            {
                ThreadOpenerKind.Badge => !UsesTemplate(root) && ReplyCountOf(root) > 0,
                ThreadOpenerKind.ReplyAction => !UsesTemplate(root) && CanReplyInThread && !root.IsDeleted,
                _ => false,
            };
            if (!renders) return null;

            var refs = kind == ThreadOpenerKind.Badge ? _threadBadgeRefs : _threadReplyRefs;
            return refs.TryGetValue(_threadOpenerId, out var reference)
                && !string.IsNullOrEmpty(reference.Id)
                    ? reference
                    : null;
        }
    }

    /// <summary>
    /// The fallback <c>restoreFocus</c> receives when the opener button is gone: the composer
    /// input while one renders, else the (keyboard-focusable) messages container.
    /// </summary>
    private ElementReference ThreadFocusFallback
        => ShowComposer && !string.IsNullOrEmpty(_composerInput.Id) ? _composerInput : _messagesContainer;

    /// <summary>
    /// Tracks the thread panel's open state across RENDERS (an event-handler render does not run
    /// <see cref="OnParametersSetAsync"/>, so the close of the built-in button would be missed
    /// there). An open→closed transition — the built-in close button OR a parameter-driven
    /// unmount (the root message leaving <see cref="Messages"/>) — restores the focus; an open
    /// that TmChat's own affordances did not click invalidates a stale capture so it can never
    /// restore onto an unrelated button. Called from <see cref="OnAfterRenderAsync"/>.
    /// </summary>
    private async ValueTask SyncThreadOpenerFocusAfterRenderAsync()
    {
        var threadOpen = EnableThreads && ActiveThreadRoot is not null;
        var wasOpen = _threadOpenAtLastRender;
        _threadOpenAtLastRender = threadOpen; // consumed synchronously — no double restore

        var capturedThisRender = _threadOpenerCapturedThisRender;
        _threadOpenerCapturedThisRender = false;
        if (threadOpen
            && !capturedThisRender
            && !string.Equals(EffectiveActiveThread, _threadOpenerThreadId, StringComparison.Ordinal))
        {
            ClearThreadOpener();
        }

        // Opening a thread through the built-in UI moves the focus to the panel's reply input
        // once the render mounted it (best effort) — a keyboard user can type the reply at once.
        if (_focusThreadInputAfterRender)
        {
            _focusThreadInputAfterRender = false;
            if (threadOpen) await FocusThreadInputBestEffortAsync();
        }

        if (wasOpen && !threadOpen)
        {
            await FocusThreadOpenerAsync();
        }
    }

    /// <summary>
    /// Returns focus to the button that opened the closed thread — the close button itself
    /// unmounts with the panel, so without this the focus would fall to the body. Best-effort:
    /// the module's <c>restoreFocus</c> when <c>chat.js</c> is attached (it refuses to STEAL
    /// focus the composer or any other element still holds, targets the opener only while it is
    /// actually rendered and falls back to the composer / the messages container otherwise), the
    /// raw <see cref="ElementReference.FocusAsync"/> fallback otherwise; every failure is
    /// swallowed. Clears the capture so a later close cannot re-focus an unrelated button.
    /// </summary>
    private async ValueTask FocusThreadOpenerAsync()
    {
        var opener = ThreadOpenerRestoreTarget;
        var fallback = ThreadFocusFallback;
        ClearThreadOpener();
        try
        {
            if (_module is not null)
            {
                await _module.InvokeAsync<bool>("restoreFocus", opener, fallback);
            }
            else if (opener is { } target && !string.IsNullOrEmpty(target.Id))
            {
                await target.FocusAsync();
            }
            else if (!string.IsNullOrEmpty(fallback.Id))
            {
                await fallback.FocusAsync();
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Focus is best-effort: a disconnected circuit or an unmounted button is not an error.
        }
    }

    /// <summary>
    /// Container-level dismiss key of the thread panel (the Escape exception of the keyboard
    /// activation convention): Escape closes the thread from WHEREVER inside the panel the focus
    /// sits — the reply input included. Every other key keeps its native meaning: the handler
    /// ignores it (the input's Enter/IME behavior belongs to the input, not the panel).
    /// </summary>
    private Task HandleThreadAsideKeyDownAsync(KeyboardEventArgs e)
        => e.Key == "Escape" ? CloseThreadAsync() : Task.CompletedTask;

    /// <summary>Localized accessible name of the messages container.</summary>
    private string MessagesAriaLabelText => MessagesAriaLabel ?? Loc["TmChat_MessagesLabel"];

    /// <summary>
    /// Localized accessible name of the built-in composer textarea — the host's override, else
    /// the effective placeholder (which itself defaults to <c>TmChat_Placeholder</c>).
    /// </summary>
    private string ComposerAriaLabelText => ComposerAriaLabel ?? PlaceholderText;
}
