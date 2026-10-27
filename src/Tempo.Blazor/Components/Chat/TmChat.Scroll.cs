using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Scroll half of <see cref="TmChat"/> (FZE-6): keeps the message list pinned to the bottom only
/// while the user is actually reading there, preserves the visual position across a prepend of
/// older history and counts the messages that arrive while the user reads above the bottom
/// behind the jump-to-new button. The position work itself lives in the <c>chat.js</c> module
/// (<c>attachScroller</c>): the scroller keeps its own PRE-UPDATE geometry snapshot (refreshed
/// on every scroll event and at the end of every <c>afterUpdate</c>), so there is no
/// <c>beforeUpdate</c> round trip — this half only CLASSIFIES the change (synchronously, from
/// <c>OnParametersSetAsync</c>) and the render applies it through <c>afterUpdate(mode)</c> from
/// <see cref="ApplyScrollAfterRenderAsync"/> (OnAfterRender — the DOM is already updated).
/// ComponentBase renders as soon as <c>OnParametersSetAsync</c> returns an unfinished task, so
/// the classification must complete before the first <c>await</c> — an
/// <see cref="ApplyScrollAfterRenderAsync"/> racing a still-pending parameter set would
/// otherwise consume a stale mode. Split out of <c>TmChat.razor.cs</c> to keep that file under
/// the 1200-line source ceiling (N270/F13), like <c>TmChat.TypingExpiry.cs</c>.
/// </summary>
public partial class TmChat
{
    /// <summary>
    /// Distance from the bottom (px) that still counts as "at the bottom" — passed to the
    /// <c>chat.js</c> scroller as its <c>threshold</c>. A small tolerance absorbs sub-pixel
    /// rounding and fractional scroll offsets so the pinned state does not flap.
    /// </summary>
    private const int ScrollAtBottomThresholdPx = 40;

    /// <summary>
    /// Distance from the top (px) that counts as "near the top" — passed to the <c>chat.js</c>
    /// scroller as its <c>topThreshold</c>; the near-top report drives the automatic
    /// <see cref="OnLoadOlder"/> trigger at the top edge.
    /// </summary>
    private const int ScrollNearTopThresholdPx = 80;

    // ── Parameters ──

    /// <summary>
    /// When the component scrolls the message list to the bottom on its own. Default
    /// <see cref="ChatAutoScroll.WhenAtBottom"/> — the ONLY documented change of TmChat's default
    /// behavior (scrolling used to be a no-op): the first render lands at the bottom and further
    /// messages scroll only while the user is at the bottom; messages arriving above the
    /// reading position are counted behind the jump-to-new button, the user's own messages
    /// always scroll and a prepend of older history preserves the visual position.
    /// <para>
    /// Nothing scrolls before interactivity: during prerender (and any render without a live
    /// JS circuit) the scroller is not attached and every automatic scroll is a no-op — the
    /// first INTERACTIVE render lands at the bottom.
    /// </para>
    /// <para>
    /// Change classification (from the RENDERED root messages — thread replies render in the
    /// panel, not the list): first AND last root Id unchanged → no message change (the JS
    /// scroller still refreshes its snapshot and re-pins a pinned viewport whose content grew,
    /// e.g. the typing indicator); the old last Id now SITS INTERIOR to the list →
    /// an append even when old rows were trimmed from the head (a sliding-window feed keeps
    /// working); the old first Id moved down → a prepend (position preserved); only when
    /// NEITHER boundary Id survives is the change treated as a reset (land at the bottom).
    /// <see cref="ChatAutoScroll.Never"/> restores the pre-2.10 behavior — the component itself
    /// makes no scroll calls at all (the explicit <see cref="ScrollToBottomAsync"/> /
    /// <see cref="ScrollToMessageAsync"/> helpers still work, the jump-to-new button does not
    /// render); <see cref="ChatAutoScroll.Always"/> pins the list to the bottom on every message
    /// change regardless of the reading position.
    /// </para>
    /// </summary>
    [Parameter] public ChatAutoScroll AutoScroll { get; set; } = ChatAutoScroll.WhenAtBottom;

    /// <summary>
    /// Fired when the user reaches the top edge of the message list (through the built-in
    /// "load older" affordance or the scroller's near-top report) and older history should be
    /// PREPENDED to <see cref="Messages"/>. The host loads the page and updates the parameter —
    /// the component detects the prepend (the first message Id changed, the last stayed) and
    /// preserves the visual position instead of scrolling. Only consulted when
    /// <see cref="HasMoreBefore"/> is <see langword="true"/> and while
    /// <see cref="IsLoadingOlder"/> is <see langword="false"/>. Not wired → neither the built-in
    /// affordance nor the top-edge trigger render (additive — hosts without pagination keep
    /// today's DOM).
    /// </summary>
    [Parameter] public EventCallback OnLoadOlder { get; set; }

    /// <summary>
    /// Whether older history exists above the current page (the host's "has more" cursor).
    /// Together with a wired <see cref="OnLoadOlder"/> it renders the built-in "load older"
    /// affordance at the top of the message list and arms the top-edge trigger. Default
    /// <see langword="false"/> (no pagination — today's DOM).
    /// </summary>
    [Parameter] public bool HasMoreBefore { get; set; }

    /// <summary>
    /// Whether the host is currently loading the older page (its own loading state, mirrored
    /// into the component). While <see langword="true"/> the "load older" affordance is disabled
    /// and neither it nor the top-edge trigger fires <see cref="OnLoadOlder"/> again. Default
    /// <see langword="false"/>.
    /// </summary>
    [Parameter] public bool IsLoadingOlder { get; set; }

    /// <summary>
    /// Whether the floating jump-to-new button renders when messages arrive while the user is
    /// reading above the bottom. Default <see langword="true"/>. The button shows the count of
    /// the unseen messages (<c>TmChat_NewMessages</c> plural family; replaced by
    /// <see cref="JumpToNewTemplate"/>) and scrolling back to the bottom — by the user or
    /// through the button — resets it. Never renders with <see cref="AutoScroll"/> =
    /// <see cref="ChatAutoScroll.Never"/>.
    /// </summary>
    [Parameter] public bool ShowJumpToNewButton { get; set; } = true;

    /// <summary>
    /// Replaces the CONTENT of the floating jump-to-new button (icon + localized count) with
    /// host markup. The template receives the current count of unseen messages; the
    /// <c>&lt;button&gt;</c> element itself stays built-in (its click — scroll to the bottom,
    /// count reset, composer refocus — and the <c>TmChat_JumpToNew</c> aria-label are the
    /// component's). Null (the default) renders the built-in content.
    /// </summary>
    [Parameter] public RenderFragment<int>? JumpToNewTemplate { get; set; }

    // ── Scroll state ──

    /// <summary>The <c>{ dispose, isAtBottom, isPinned, … }</c> handle of the chat.js scroller.</summary>
    private IJSObjectReference? _scroller;

    /// <summary>The last at-bottom state the scroller reported (initially true — pinned).</summary>
    private bool _atBottom = true;

    /// <summary>Messages that arrived while the user was reading above the bottom.</summary>
    private int _unreadCount;

    /// <summary>The last near-top state the scroller reported.</summary>
    private bool _nearTop;

    /// <summary>
    /// Arms the near-top load trigger: set when the user scrolls away from the top or when a
    /// load finishes, consumed when <see cref="OnLoadOlder"/> fires — one page per "visit" to
    /// the top edge, no per-scroll-event spam.
    /// </summary>
    private bool _loadOlderArmed = true;
    private bool _lastIsLoadingOlder;

    /// <summary>
    /// Component-side in-flight guard over <see cref="OnLoadOlder"/>: the host's own
    /// <see cref="IsLoadingOlder"/> parameter cannot cover the window between the trigger and
    /// the parameter update arriving, so BOTH the click and the top-edge path check this before
    /// firing and clear it in a <c>finally</c> — a click racing the near-top report fires once.
    /// </summary>
    private bool _loadOlderInFlight;

    /// <summary>
    /// The previously-first row to focus after a CLICK-initiated prepend (the load-older button
    /// had focus and is about to be replaced/aria-disabled): captured at click time, consumed
    /// by <see cref="ApplyScrollAfterRenderAsync"/> through the scroller's <c>focusMessage</c>
    /// (with <c>tabindex="-1"</c> and <c>preventScroll</c>) so focus never falls to the body.
    /// </summary>
    private string? _loadOlderFocusAnchorId;

    /// <summary>
    /// Whether the built-in load-older button currently holds focus (tracked through
    /// <c>@onfocusin</c>/<c>@onfocusout</c>) — a click-initiated load only moves focus to the
    /// previously-first row when the click left it ON the button.
    /// </summary>
    private bool _loadOlderFocused;

    /// <summary>What kind of message-list change the NEXT render applies (computed in OnParametersSet).</summary>
    private ScrollChangeMode _pendingMode;

    /// <summary>First ROOT message Id of the rendered list as of the last parameter set.</summary>
    private string? _lastFirstId;

    /// <summary>Last ROOT message Id of the rendered list as of the last parameter set.</summary>
    private string? _lastLastId;

    /// <summary>
    /// Count of the RENDERED root messages as of the last parameter set; −1 = nothing seen yet.
    /// Thread replies are excluded — they render in the panel, so they are neither an append
    /// nor a prepend of the list.
    /// </summary>
    private int _lastRootCount = -1;

    /// <summary>
    /// How many ROOT messages the pending append added — captured in
    /// <see cref="DetectScrollChange"/> BEFORE the trackers move (the after-render half reads
    /// it, because <see cref="_lastRootCount"/> is already the NEW count by then).
    /// </summary>
    private int _pendingRootDelta;

    /// <summary>The message-list change kinds the scroll logic distinguishes.</summary>
    private enum ScrollChangeMode
    {
        /// <summary>No message change (a typing-only or parameter-only re-render, or a middle-row edit): the scroller only refreshes its snapshot and re-pins a grown pinned viewport.</summary>
        None,

        /// <summary>New messages arrived at the END of the root list — the old last Id sits interior to the new list (which also covers a sliding-window feed trimming its head) or the head survived while the tail was replaced.</summary>
        Append,

        /// <summary>Older history arrived at the START of the list — the old first Id moved down (the last Id may or may not have survived).</summary>
        Prepend,

        /// <summary>The whole list was replaced (neither boundary Id survives) — land at the bottom.</summary>
        Reset,
    }

    /// <summary>Whether the floating jump-to-new button renders right now.</summary>
    private bool ShowJumpButton
        => ShowJumpToNewButton
            && AutoScroll != ChatAutoScroll.Never
            && _unreadCount > 0
            && !_atBottom;

    /// <summary>Whether the built-in "load older" affordance renders (host wired the callback).</summary>
    private bool ShowLoadOlder => HasMoreBefore && OnLoadOlder.HasDelegate;

    /// <summary>
    /// Whether the message at the END of the RENDERED root sequence was sent by the current
    /// user — the last THREAD REPLY does not count (it renders in the panel, not the list), so
    /// the own-message check reads the last root message.
    /// </summary>
    private bool IsLastRootMessageOwn
        => RootMessages().LastOrDefault() is { } last && IsCurrentUser(last.Author);

    /// <summary>
    /// Compares the first/last ROOT message Ids and the root count against the previous
    /// parameter set to classify the change — see the rules on <see cref="ScrollChangeMode"/>.
    /// Synchronous and side-effect-free on purpose: it runs from <c>OnParametersSetAsync</c>
    /// BEFORE the first <c>await</c>, because ComponentBase renders as soon as that method
    /// yields an unfinished task — a still-pending parameter set must already carry the mode
    /// its <see cref="ApplyScrollAfterRenderAsync"/> will consume. The pre-update GEOMETRY is
    /// not captured here: the <c>chat.js</c> scroller keeps its own snapshot (refreshed on
    /// every scroll event and at the end of every <c>afterUpdate</c>), so no interop round
    /// trip is needed on the render path.
    /// </summary>
    private void DetectScrollChange()
    {
        // The rendered root sequence, materialized once: thread replies render in the panel, so
        // they are neither an append nor a prepend of the list.
        var roots = RootMessages().ToList();
        var count = roots.Count;
        string? firstId = count > 0 ? roots[0].Id : null;
        string? lastId = count > 0 ? roots[count - 1].Id : null;

        ScrollChangeMode mode;
        int appended;
        if (_lastRootCount < 0 || count == 0 || _lastRootCount == 0)
        {
            // The very first parameter set is the first render's job (it scrolls to the bottom);
            // an emptied list has nothing to preserve, a fill from empty is an append.
            if (_lastRootCount == 0 && count > 0)
            {
                mode = ScrollChangeMode.Append;
                appended = count;
            }
            else
            {
                mode = ScrollChangeMode.None;
                appended = 0;
            }
        }
        else
        {
            var firstIndex = IndexOfId(roots, _lastFirstId);
            var lastIndex = IndexOfId(roots, _lastLastId);

            if (firstIndex == 0 && lastIndex == count - 1)
            {
                // Both boundary Ids unchanged — a typing-only or parameter-only re-render (or a
                // middle-row edit) is NEITHER an append nor a prepend.
                mode = ScrollChangeMode.None;
                appended = 0;
            }
            else if (lastIndex >= 0 && lastIndex < count - 1)
            {
                // The old last message now sits INTERIOR to the list: new messages arrived at
                // the end — including when a sliding-window feed trimmed rows from the head.
                // Only the rows AFTER the old last are the appended ones: a plain count delta
                // would subtract the trimmed head and undercount the unread.
                mode = ScrollChangeMode.Append;
                appended = count - 1 - lastIndex;
            }
            else if (firstIndex > 0)
            {
                // The old first message moved DOWN: older history arrived above (a prepend —
                // possibly one that also trimmed the tail; the scroller's anchor restores the
                // reading position either way).
                mode = ScrollChangeMode.Prepend;
                appended = 0;
            }
            else if (firstIndex == 0)
            {
                // The head is unchanged but the tail was replaced or extended without the old
                // last surviving (e.g. an optimistic send replaced by its confirmed row) —
                // everything after the surviving head is new.
                mode = ScrollChangeMode.Append;
                appended = Math.Max(count - 1, 0);
            }
            else if (lastIndex < 0)
            {
                // Reset — and ONLY reset — when neither boundary Id survives: the whole list
                // was replaced, so the only sane landing is the bottom.
                mode = ScrollChangeMode.Reset;
                appended = 0;
            }
            else
            {
                // The old last message is still last but the old first is gone: a middle
                // removal — no boundary moved, nothing to compensate.
                mode = ScrollChangeMode.None;
                appended = 0;
            }
        }

        // The appended ROOT rows are the announcement candidates (FZE-7): an initial fill from an
        // empty list and a PREPEND of older history are deliberately not announced, so the batch
        // only fills for a real append to a NON-EMPTY list — and only for ids the component has
        // NEVER seen: a host reloading the conversation re-delivers the same ids (a
        // tail-replaced reload would otherwise re-announce the whole history). The announcements
        // half consumes (and clears) the batch in the same parameter set — ScheduleAnnouncements
        // runs right after this method — so it never accumulates across sets.
        if (appended > 0 && _lastRootCount > 0)
        {
            foreach (var message in roots.GetRange(count - appended, appended))
            {
                if (_seenRootIds.Add(message.Id)) _appendedRoots.Add(message);
            }
        }

        // Seed / refresh the seen set AFTER the append filter: the first parameter set, a prepend
        // and a reset mark the whole current root sequence as seen (a loaded history is not
        // news), and the just-queued arrivals are marked seen too so a later reload skips them.
        _seenRootIds.UnionWith(roots.Select(m => m.Id));

        // Accumulate the appended count: several classifications can land before ONE
        // after-render consumes them (Append then None, a racing parameter set) — overwriting
        // would undercount the unread behind the jump button.
        _pendingRootDelta += appended;
        _lastFirstId = firstId;
        _lastLastId = lastId;
        _lastRootCount = count;

        // A finished load re-arms the top-edge trigger: one page per visit to the top edge —
        // the next page loads when the user scrolls away and reaches the top again (the
        // afterUpdate position shift of a real prepend reports near-top=false on its own).
        if (_lastIsLoadingOlder && !IsLoadingOlder) _loadOlderArmed = true;
        _lastIsLoadingOlder = IsLoadingOlder;

        // MERGE the pending mode instead of overwriting it: two classifications can arrive
        // before a single after-render (a fast parameter set racing the dispatcher). Reset
        // wins over anything, a None keeps what was already pending, any real change replaces
        // a None — so Append-then-None still scrolls (and counts) on the single render.
        _pendingMode = (_pendingMode, mode) switch
        {
            (_, ScrollChangeMode.Reset) or (ScrollChangeMode.Reset, _) => ScrollChangeMode.Reset,
            (var pending, ScrollChangeMode.None) => pending,
            _ => mode,
        };

        // A stale focus anchor (a click-initiated load whose prepend never arrived, e.g. the
        // host ignored the request) must not steal focus on a LATER prepend: clear it unless
        // a load is still in flight or this very change is the prepend it belongs to.
        if (mode != ScrollChangeMode.Prepend && !IsLoadingOlder && !_loadOlderInFlight)
        {
            _loadOlderFocusAnchorId = null;
        }
    }

    /// <summary>Index of the first message carrying <paramref name="id"/>; −1 when absent.</summary>
    private static int IndexOfId(IReadOnlyList<ChatMessage> messages, string? id)
    {
        if (id is null) return -1;
        for (var i = 0; i < messages.Count; i++)
        {
            if (string.Equals(messages[i].Id, id, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    /// <summary>
    /// Applies the pending scroll mode AFTER the render (the DOM now carries the new rows):
    /// a reset lands at the bottom, an append defers to the JS scroller — it scrolls when the
    /// viewport WAS at the bottom (the scroller's own <c>pinned</c> belief or a live
    /// measurement) and reports <c>unread</c> otherwise (then the user's own message still
    /// scrolls, anything else counts behind the jump button) — a prepend restores the position
    /// on the scroller's anchor, and a NO-CHANGE render only refreshes the scroller's snapshot
    /// (which re-pins a pinned viewport whose content grew, e.g. the typing indicator).
    /// Called from <c>OnAfterRenderAsync</c>; a no-op under <see cref="ChatAutoScroll.Never"/>.
    /// </summary>
    private async Task ApplyScrollAfterRenderAsync(bool firstRender)
    {
        if (AutoScroll == ChatAutoScroll.Never) return;

        var mode = _pendingMode;
        _pendingMode = ScrollChangeMode.None;
        // The appended count travels WITH the mode: captured and cleared as the mode is
        // consumed, so a merged (Append + None) classification counts exactly once and a
        // later change starts from zero.
        var pendingRootDelta = _pendingRootDelta;
        _pendingRootDelta = 0;

        // Instant for the first render and a replaced list: an animated initial scroll fires
        // events along the whole track (a spurious near-top report would auto-load history)
        // and a conversation opening should land at the bottom at once.
        if (firstRender || mode == ScrollChangeMode.Reset)
        {
            await ScrollToBottomCoreAsync(instant: true);
            return;
        }

        var scroller = _scroller;
        if (scroller is null) return;

        if (mode == ScrollChangeMode.None)
        {
            await InvokeAfterUpdateAsync(scroller, "none");
            return;
        }

        // Always pins the list to the bottom on EVERY message change — no position bookkeeping
        // (the scroller's snapshot is refreshed by its own scrollToBottom), the scroll decides,
        // nothing counts as unread.
        if (AutoScroll == ChatAutoScroll.Always)
        {
            await ScrollToBottomCoreAsync();
        }
        else
        {
            var status = await InvokeAfterUpdateAsync(
                scroller, mode == ScrollChangeMode.Append ? "append" : "prepend");

            if (mode == ScrollChangeMode.Append
                && !string.Equals(status, "scrolled", StringComparison.Ordinal))
            {
                // The scroller did not scroll (the user was reading above the bottom, or it
                // could not answer): the user's own message still lands at the bottom, every
                // other append is counted behind the jump button.
                if (IsLastRootMessageOwn)
                {
                    await ScrollToBottomCoreAsync();
                }
                else
                {
                    _unreadCount += pendingRootDelta > 0 ? pendingRootDelta : 1;
                    StateHasChanged();
                }
            }
        }

        // A click-initiated prepend moves focus off the (relocated/aria-disabled) load-older
        // button onto the previously-first row — never leave it on the body.
        if (mode == ScrollChangeMode.Prepend && _loadOlderFocusAnchorId is not null)
        {
            var anchorId = _loadOlderFocusAnchorId;
            _loadOlderFocusAnchorId = null;
            try
            {
                await scroller.InvokeVoidAsync("focusMessage", anchorId);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
            {
                // The circuit dropped mid-load — focus is best-effort, the messages are safe.
            }
        }
    }

    /// <summary>
    /// The <c>afterUpdate</c> half of the scroller bookkeeping: returns the scroller's status
    /// (<c>scrolled</c>/<c>unread</c>/<c>preserved</c>/<c>none</c>) or <see langword="null"/>
    /// when the scroller is unreachable (the position compensation of THIS change is lost, not
    /// the messages).
    /// </summary>
    private async ValueTask<string?> InvokeAfterUpdateAsync(IJSObjectReference scroller, string mode)
    {
        try
        {
            return await scroller.InvokeAsync<string?>("afterUpdate", mode);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// The [JSInvokable] at-bottom report of the <c>chat.js</c> scroller — fired on CHANGE only
    /// (never per scroll event). Coming back to the bottom clears the unseen count, so a manual
    /// scroll down hides the jump button exactly like the button's own click does. Public for
    /// the JS callback contract; runs through <see cref="InvokeAsync"/> so it is safe from the
    /// JS dispatcher path and re-renders only when the button visibility could change.
    /// </summary>
    [JSInvokable]
    public Task OnScrollStateChanged(bool atBottom) => InvokeAsync(() =>
    {
        if (_disposed) return;
        var stateChanged = _atBottom != atBottom;
        _atBottom = atBottom;
        var hadUnread = _unreadCount;
        if (atBottom) _unreadCount = 0;
        if (stateChanged || hadUnread != _unreadCount) StateHasChanged();
    });

    /// <summary>
    /// The [JSInvokable] near-top report of the <c>chat.js</c> scroller — fired on CHANGE only.
    /// Entering the top band fires <see cref="OnLoadOlder"/> once (guarded by
    /// <see cref="HasMoreBefore"/> / <see cref="IsLoadingOlder"/> and re-armed when the user
    /// scrolls away or the load finishes); leaving it re-arms. Public for the JS callback
    /// contract; near-top state itself is not rendered, so no re-render is queued.
    /// </summary>
    [JSInvokable]
    public Task OnScrollNearTop(bool nearTop) => InvokeAsync(async () =>
    {
        if (_disposed) return;
        var changed = _nearTop != nearTop;
        _nearTop = nearTop;
        if (!nearTop)
        {
            _loadOlderArmed = true;
            return;
        }

        if (changed) await TryLoadOlderAsync();
    });

    /// <summary>
    /// Fires <see cref="OnLoadOlder"/> once per armed near-top visit. The
    /// <see cref="_loadOlderInFlight"/> guard covers the window before the host's
    /// <see cref="IsLoadingOlder"/> parameter arrives — a click racing the top-edge report
    /// fires the load once.
    /// </summary>
    private async Task TryLoadOlderAsync()
    {
        if (_loadOlderInFlight || !_loadOlderArmed || !HasMoreBefore || IsLoadingOlder || !OnLoadOlder.HasDelegate) return;
        _loadOlderArmed = false;
        _loadOlderInFlight = true;
        try
        {
            await OnLoadOlder.InvokeAsync();
        }
        finally
        {
            _loadOlderInFlight = false;
        }
    }

    /// <summary>
    /// Click on the built-in "load older" affordance (the in-flight guard and the
    /// <c>aria-disabled</c> state guard double fires). When the click left focus ON the button,
    /// the previously-first row is recorded as the focus anchor: after the prepend the scroller
    /// moves focus there (<c>focusMessage</c>, <c>preventScroll</c>) instead of leaving it on
    /// the button or the body.
    /// </summary>
    private async Task HandleLoadOlderClickAsync()
    {
        if (_loadOlderInFlight || IsLoadingOlder || !HasMoreBefore) return;
        _loadOlderArmed = false;
        _loadOlderFocusAnchorId = _loadOlderFocused ? RootMessages().FirstOrDefault()?.Id : null;
        _loadOlderInFlight = true;
        try
        {
            await OnLoadOlder.InvokeAsync();
        }
        finally
        {
            _loadOlderInFlight = false;
        }
    }

    /// <summary>
    /// Click on the floating jump-to-new button: resets the count (which hides the button on
    /// the next render), scrolls to the bottom and — so focus never falls to the body when the
    /// button unmounts — moves focus to the composer for a KEYBOARD activation
    /// (<c>e.Detail == 0</c>), falling back to the last message row and the messages container
    /// when no composer renders (<see cref="ShowComposer"/>=<see langword="false"/>). A POINTER
    /// activation (<c>e.Detail &gt; 0</c>) deliberately skips the composer — the user is
    /// pointing, not typing — and focuses the last message row (<c>preventScroll</c>, the
    /// smooth scroll of the click itself is not fought).
    /// </summary>
    private async Task HandleJumpToNewClickAsync(MouseEventArgs e)
    {
        _unreadCount = 0;
        _atBottom = true;
        StateHasChanged();
        await ScrollToBottomCoreAsync();

        if (e.Detail == 0)
        {
            if (ShowComposer)
            {
                await FocusComposerInputAsync();
            }
            else
            {
                await FocusLastMessageRowAsync();
            }
        }
        else
        {
            await FocusLastMessageRowAsync();
        }
    }

    /// <summary>
    /// Focuses the LAST rendered message row through the scroller's <c>focusMessage</c>
    /// (<c>tabindex="-1"</c>, <c>preventScroll</c>), falling back to the messages container
    /// (itself <c>tabindex="-1"</c>) when the scroller is not attached or there are no rows —
    /// the shared "focus must not fall to the body" contract of the jump-button click.
    /// </summary>
    private async ValueTask FocusLastMessageRowAsync()
    {
        var lastId = RootMessages().LastOrDefault()?.Id;
        var scroller = _scroller;
        if (!string.IsNullOrEmpty(lastId) && scroller is not null)
        {
            try
            {
                await scroller.InvokeVoidAsync("focusMessage", lastId);
                return;
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
            {
                // Fall through to the container: focus is best-effort, body is not an option.
            }
        }

        try
        {
            await _messagesContainer.FocusAsync(preventScroll: true);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // The element is gone or the circuit dropped — nothing to focus, nothing to report.
        }
    }

    /// <summary>
    /// Scrolls the message list to the bottom — the public helper for hosts (e.g. after a
    /// "new messages" toast or an external navigation). A no-op while the JS scroller is not
    /// attached (prerender, a failed import, <see cref="ChatAutoScroll.Never"/> keeps working —
    /// an explicit host call is never suppressed). Dispatched through
    /// <see cref="InvokeAsync"/> so it is safe from any thread; arriving at the bottom clears
    /// the unseen count and <see cref="ScrollToBottomCoreAsync"/> re-renders when the jump
    /// button was visible, so the button hides with THIS call.
    /// </summary>
    public ValueTask ScrollToBottomAsync() => new(InvokeAsync(async () =>
    {
        if (_disposed) return;
        await ScrollToBottomCoreAsync();
    }));

    /// <summary>
    /// Scrolls the message list so the message with the given id
    /// (the <c>data-message-id</c> anchor) is in view — the public helper for
    /// "jump to message" links and notification deep links. Returns whether the anchor was
    /// FOUND (and scrolled to): <see langword="false"/> for an unknown id, an empty id, or
    /// while the JS scroller is not attached. Dispatched through
    /// <see cref="InvokeAsync"/> so it is safe from any thread.
    /// </summary>
    public async ValueTask<bool> ScrollToMessageAsync(string? id)
    {
        var found = false;
        await InvokeAsync(async () => found = await ScrollToMessageCoreAsync(id));
        return found;
    }

    /// <summary>
    /// The core anchor scroll — returns whether the JS scroller found the row.
    /// </summary>
    private async Task<bool> ScrollToMessageCoreAsync(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || _disposed) return false;
        var scroller = _scroller;
        if (scroller is null) return false;
        try
        {
            return await scroller.InvokeAsync<bool>("scrollToMessage", id);
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Best-effort navigation: an unreachable circuit loses the scroll, not the data.
            return false;
        }
    }

    /// <summary>
    /// Whether the message list is currently scrolled to (or within the threshold of) the
    /// bottom. The value is the last state the <c>chat.js</c> scroller reported — a change of
    /// the real scroll position is picked up on the scroller's next report, a
    /// <see cref="ScrollToBottomAsync"/> call sets it optimistically. Read on the component's
    /// dispatcher when called off the render path.
    /// </summary>
    public bool IsAtBottom => _atBottom;

    /// <summary>
    /// The core scroll (no dispatch — call through the public wrappers or the render path).
    /// Marks the state at-bottom and clears the count — the scroller's own report of the
    /// programmatic scroll agrees with it. When the jump button was visible BEFORE the scroll
    /// it is re-rendered away immediately (<see cref="StateHasChanged"/>): the caller's render
    /// has already happened, so without this the button would linger until the next render.
    /// </summary>
    private async ValueTask ScrollToBottomCoreAsync(bool instant = false)
    {
        var scroller = _scroller;
        if (scroller is null) return;
        var wasVisible = ShowJumpButton;
        try
        {
            await scroller.InvokeVoidAsync("scrollToBottom", instant);
            _atBottom = true;
            _unreadCount = 0;
            if (wasVisible) StateHasChanged();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // A dropped circuit loses the scroll, not the messages.
        }
    }

    /// <summary>
    /// Attaches the <c>chat.js</c> scroller to the messages container once (the container
    /// element exists for the component's whole lifetime, so — unlike the composer slots —
    /// there is nothing to re-attach). A failure leaves the handle unset and the next render
    /// retries. With the scroller attached the component receives the at-bottom and near-top
    /// reports even under <see cref="ChatAutoScroll.Never"/> (they drive the top-edge
    /// <see cref="OnLoadOlder"/> trigger); only the scroll ACTIONS are suppressed there.
    /// <para>
    /// The FZE-13 viewport tracker rides this per-render sync (its partial defines the
    /// <see cref="TrackVirtualKeyboard"/> parameter): it hangs off the same <c>chat.js</c>
    /// module and its pinned-scroller rescue option takes THIS handle, so the tail of this
    /// method is its hook — <c>TmChat.razor.cs</c> stays untouched under its source ceiling.
    /// </para>
    /// </summary>
    private async Task SyncScrollerAsync()
    {
        if (_scroller is null && !_disposed && _module is not null && _selfRef is not null
            && !string.IsNullOrEmpty(_messagesContainer.Id))
        {
            try
            {
                var handle = await _module.InvokeAsync<IJSObjectReference>(
                    "attachScroller",
                    _messagesContainer,
                    _selfRef,
                    new { threshold = ScrollAtBottomThresholdPx, topThreshold = ScrollNearTopThresholdPx });
                if (_disposed)
                {
                    // The component went away while the attach was in flight — the handle would leak.
                    await DisposeUnreachableAsync(handle);
                }
                else
                {
                    _scroller = handle;
                }
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
            {
                // Attach failed (dropped circuit / prerender) — retry on the next render.
                _scroller = null;
            }
        }

        // FZE-13 hook: after the scroller state is settled, sync the opt-in visualViewport
        // tracker (it re-attaches when the parameter or the scroller handle changed).
        await SyncViewportTrackerAsync();
    }

    /// <summary>
    /// Detaches and disposes the scroller handle (part of <see cref="DisposeAsyncCore"/>).
    /// The FZE-13 viewport tracker dies here too: it hangs off the same <c>chat.js</c> module
    /// and its only extra dependency is the scroller handle — one cleanup path for both.
    /// </summary>
    private async ValueTask DisposeScrollerAsync()
    {
        var handle = _scroller;
        _scroller = null;
        await DisposeJsHandleAsync(handle);
        await DisposeViewportTrackerAsync();
    }
}
