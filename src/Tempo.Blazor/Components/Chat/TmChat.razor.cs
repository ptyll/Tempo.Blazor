using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using System.Threading;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// A chat conversation component with message rendering, typing indicators, attachments, and send input.
/// </summary>
public partial class TmChat : TmComponentBase, ITmDraftGuard, IAsyncDisposable
{
    /// <summary>The currently authenticated user.</summary>
    [Parameter] public ChatUser CurrentUser { get; set; } = new();

    /// <summary>List of messages to display.</summary>
    [Parameter] public IReadOnlyList<ChatMessage> Messages { get; set; } = [];

    /// <summary>Users currently typing.</summary>
    [Parameter] public IReadOnlyList<ChatUser> TypingUsers { get; set; } = [];

    // ── Typing indicator: label, template, AT visibility, expiry ──

    /// <summary>
    /// Replaces the localized typing text with a host-computed label. The built-in text comes from
    /// the <c>TmChat_TypingSingle</c>/<c>TmChat_TypingTwo</c>/<c>TmChat_TypingMany</c> resources
    /// ("{0} is typing…"); a set label receives the current <see cref="TypingUsers"/> list and its
    /// result is rendered instead — e.g. <c>users =&gt; $"{users[0].Name} píše…"</c>. Null (the
    /// default) keeps the localized text. <see cref="TypingTemplate"/> takes precedence: the
    /// template replaces the WHOLE indicator, the label only its text span.
    /// </summary>
    [Parameter] public Func<IReadOnlyList<ChatUser>, string>? TypingLabel { get; set; }

    /// <summary>
    /// Replaces the WHOLE built-in typing indicator with host markup. The template receives the
    /// current <see cref="TypingUsers"/> list; the built-in
    /// <c>.tm-chat__typing-indicator</c> element (dots + text) is not rendered at all. Null
    /// (the default) renders the built-in indicator. Markup passed into the template carries the
    /// HOST's CSS scope, not <c>TmChat.razor.css</c>.
    /// <para>
    /// The <see cref="TypingTimeout"/> expiry applies to the template TOO: an expired window
    /// renders NEITHER the built-in indicator NOR the template (the next
    /// <see cref="TypingUsers"/> change reopens the window). <see cref="TypingAriaHidden"/> by
    /// contrast applies ONLY to the built-in indicator — a template sets its own AT visibility.
    /// Do NOT put <c>aria-live</c> or <c>role="status"</c> into the template: <see cref="TmChat"/>
    /// keeps one live region of its own and typing is never announced by the component, so an
    /// extra live region inside the template would only risk double announcements.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment<IReadOnlyList<ChatUser>>? TypingTemplate { get; set; }

    /// <summary>
    /// Hides the WHOLE built-in typing indicator from assistive technology by rendering
    /// <c>aria-hidden="true"</c> on its wrapper — not just the decorative dots, which always carry
    /// their own <c>aria-hidden</c>. Default <see langword="false"/> (today's markup: the wrapper
    /// has no <c>aria-hidden</c> and its text stays reachable). For a host that conveys typing
    /// through its own live region, set this to <see langword="true"/> so the indicator is not
    /// double-read; typing itself is NEVER announced by the component.
    /// </summary>
    [Parameter] public bool TypingAriaHidden { get; set; }

    /// <summary>
    /// Auto-hides the typing indicator when the host stops sending fresh
    /// <see cref="TypingUsers"/>. The expiry applies to BOTH the built-in indicator and a
    /// <see cref="TypingTemplate"/> — an expired window renders neither. The expiry window is
    /// measured from the LAST CHANGE of
    /// <see cref="TypingUsers"/>: a new list instance OR a different content (order-sensitive,
    /// per-<see cref="ChatUser"/> value equality) opens a fresh window, an identical list
    /// reference with unchanged content does not — pass a stable reference and re-assign the list
    /// only on real typing signals, or every parent re-render extends the window forever. When
    /// the window expires the indicator disappears but the host's <see cref="TypingUsers"/> list
    /// is never mutated; the next change reopens it. <see langword="null"/> (the default) never
    /// expires. A non-positive timeout (<see cref="TimeSpan.Zero"/> or negative) expires
    /// IMMEDIATELY: the indicator is suppressed from the next render and no timer is armed.
    /// The clock is the injected <see cref="TimeProvider"/> (fallback
    /// <see cref="TimeProvider.System"/>), so tests drive expiry with a
    /// <c>FakeTimeProvider</c>. A set timeout arms a single-shot timer that is cancelled in
    /// <see cref="DisposeAsync"/> — no dangling timer after unmount.
    /// </summary>
    [Parameter] public TimeSpan? TypingTimeout { get; set; }

    /// <summary>Placeholder for the message input.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Whether the input is disabled.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Fired when the user sends a message (text only). Retained for backward compatibility.</summary>
    [Parameter] public EventCallback<string> OnSendMessage { get; set; }

    /// <summary>Fired when the user sends a message, carrying optional thread/reply context.</summary>
    [Parameter] public EventCallback<ChatSendRequest> OnSend { get; set; }

    /// <summary>Fired when an attachment is clicked.</summary>
    [Parameter] public EventCallback<ChatAttachment> OnAttachmentClick { get; set; }

    // ── K6: threads, edit/delete, reactions, receipts ──

    /// <summary>Fired when the user commits an edit to one of their messages.</summary>
    [Parameter] public EventCallback<ChatMessageEdit> OnEditMessage { get; set; }

    /// <summary>Fired when the user deletes one of their messages.</summary>
    [Parameter] public EventCallback<string> OnDeleteMessage { get; set; }

    /// <summary>Fired when the user toggles an emoji reaction on a message.</summary>
    [Parameter] public EventCallback<ChatReactionToggle> OnToggleReaction { get; set; }

    /// <summary>Fired when the user opens a message's reply thread.</summary>
    [Parameter] public EventCallback<ChatMessage> OnReply { get; set; }

    /// <summary>Fired once per incoming message when it first becomes visible, for read receipts.</summary>
    [Parameter] public EventCallback<string> OnMessageRead { get; set; }

    /// <summary>Id of the thread root whose reply panel is open. Two-way bindable.</summary>
    [Parameter] public string? ActiveThreadRootId { get; set; }

    /// <summary>Fires when the open thread changes.</summary>
    [Parameter] public EventCallback<string?> ActiveThreadRootIdChanged { get; set; }

    /// <summary>When true, replies are grouped into a thread panel instead of shown inline. Default <c>true</c>.</summary>
    [Parameter] public bool EnableThreads { get; set; } = true;

    /// <summary>When true, messages show a reaction bar and emoji picker. Default <c>true</c>.</summary>
    [Parameter] public bool EnableReactions { get; set; } = true;

    /// <summary>When true, the current user can edit and delete their own messages. Default <c>true</c>.</summary>
    [Parameter] public bool AllowEditDelete { get; set; } = true;

    /// <summary>When true, own messages display per-user read receipts. Default <c>true</c>.</summary>
    [Parameter] public bool ShowReadReceipts { get; set; } = true;

    /// <summary>When true, incoming messages fire <see cref="OnMessageRead"/> as they render. Default <c>true</c>.</summary>
    [Parameter] public bool AutoReadReceipts { get; set; } = true;

    /// <summary>Emoji offered by the reaction picker.</summary>
    [Parameter] public IReadOnlyList<string> ReactionEmojis { get; set; } = ["👍", "❤️", "😂", "🎉", "😮", "😢"];

    // ── Custom templates: host-owned message rendering ──

    /// <summary>
    /// Replaces the built-in message row with host markup for the messages selected by
    /// <see cref="UseTemplateFor"/>. The template receives a <see cref="ChatMessageContext"/> per
    /// rendered message: <see cref="ChatMessageContext.Message"/>,
    /// <see cref="ChatMessageContext.IsOwn"/>, <see cref="ChatMessageContext.InThread"/>,
    /// <see cref="ChatMessageContext.Index"/>, <see cref="ChatMessageContext.IsFirstInGroup"/>,
    /// a typed payload accessor (<c>context.PayloadAs&lt;T&gt;()</c>), a metadata accessor
    /// (<c>context.GetMetadata&lt;T&gt;(key, fallback)</c>) and
    /// <see cref="ChatMessageContext.DefaultContent"/> — the built-in row, so ordinary messages
    /// can fall back to the default look.
    /// <para>
    /// Precedence: when null, every message renders the built-in row (today's behavior). When
    /// set and <see cref="UseTemplateFor"/> is null, the template renders for EVERY message
    /// (including thread panel messages). When both are set, the template renders only for
    /// messages where <see cref="UseTemplateFor"/> returns <see langword="true"/> — the host
    /// thus overrides just its "special" kinds (e.g. <c>m => m.Kind == "ai-draft"</c>).
    /// </para>
    /// <para>
    /// Regardless of the template, TmChat still owns everything around the message: each message
    /// keeps a wrapper element with <c>data-message-id</c> (and <c>data-kind</c> when the
    /// message carries a <see cref="ChatMessage.Kind"/>) used for scroll anchoring and read
    /// receipts; the thread filter, draft guard and read-receipt reporting are unaffected.
    /// Note that markup passed into the template carries the HOST's CSS scope — the
    /// <c>TmChat.razor.css</c> scoped classes do not apply to it.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment<ChatMessageContext>? MessageTemplate { get; set; }

    /// <summary>
    /// Restricts <see cref="MessageTemplate"/> to the messages for which the predicate returns
    /// <see langword="true"/>. Null (the default) means the template renders for every message.
    /// Typical host usage: <c>m => m.Kind == "ai-draft"</c> to override only special kinds while
    /// ordinary messages keep the built-in row.
    /// </summary>
    [Parameter] public Func<ChatMessage, bool>? UseTemplateFor { get; set; }

    /// <summary>
    /// Notifies the parent that the chat draft state changed. Fires ONLY when the
    /// <see cref="TmChatDraftState"/> value actually changes (value-equality dedup), never on
    /// every keystroke — the record carries flags only, and repeated identical states are
    /// suppressed so parents are not re-rendered while the user types. Members:
    /// <see cref="TmChatDraftState.HasComposerDraft"/> (main composer holds text),
    /// <see cref="TmChatDraftState.HasThreadReplyDraft"/> (the open thread panel's reply input
    /// holds text), <see cref="TmChatDraftState.EditingMessageId"/> (a message's inline edit is
    /// open) and <see cref="TmChatDraftState.HasUnsavedContent"/> (the aggregate dirty flag).
    /// <para>
    /// The composer and thread inputs are plain <c>&lt;textarea&gt;</c>s whose text the host
    /// cannot observe through any parameter — this callback is the only push channel; pair it
    /// with <see cref="HasUnsavedDraft"/> for polling and <see cref="DiscardDraftAsync"/> for the
    /// actual discard after the consumer's own confirmation.
    /// </para>
    /// <para>
    /// Opening a message's inline edit reports the editing id immediately (so the consumer knows
    /// an edit is in progress) with <see cref="TmChatDraftState.HasUnsavedContent"/> still
    /// <see langword="false"/> — the flag rises only once the edit text differs from the message
    /// text as it was when the edit started, mirroring the edit semantics of
    /// <c>TmActivityComments</c>. Closing the thread panel clears its reply draft; sending clears
    /// the sent slot.
    /// </para>
    /// </summary>
    [Parameter] public EventCallback<TmChatDraftState> OnDraftChanged { get; set; }

    // ── Slots: composer, banner, empty state, header ──

    /// <summary>
    /// Replaces the built-in composer (the input textarea + send button) with host markup — a
    /// subscribe gate, a PPV field, an owner note + hint, anything the consumer domain needs. The
    /// template receives a live <see cref="ChatComposerContext"/>: drive the typed text through
    /// <see cref="ChatComposerContext.SetTextAsync"/> and send with
    /// <see cref="ChatComposerContext.SendAsync"/> so the draft guard (<c>HasUnsavedDraft</c>,
    /// <c>OnDraftChanged</c>) and the send path (<c>OnSendMessage</c> / <c>OnSend</c>, submit
    /// state) keep working exactly as for the built-in composer; mirror
    /// <see cref="ChatComposerContext.CanSend"/> on a custom send button.
    /// <para>
    /// When set, the built-in composer does NOT render (<c>.tm-chat__input</c> and
    /// <c>.tm-chat__send-btn</c> disappear from the DOM) and neither <see cref="ComposerAddon"/>
    /// nor <see cref="ComposerFooter"/> render — the template owns the whole composer area, and
    /// markup passed into the template carries the HOST's CSS scope, not
    /// <c>TmChat.razor.css</c>. The <c>.tm-chat__input-area</c> wrapper itself STAYS (its padding
    /// and border-top), carrying <c>tm-chat__input-area--custom</c> so the template's markup
    /// stacks vertically instead of the textarea + send flex row. Ignored when
    /// <see cref="ShowComposer"/> is <see langword="false"/>.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment<ChatComposerContext>? ComposerTemplate { get; set; }

    /// <summary>
    /// Content rendered ABOVE the built-in composer input, inside the composer area — an owner
    /// note, a hint or a short status line. Only rendered with the built-in composer (a
    /// <see cref="ComposerTemplate"/> replaces the whole area and renders its own chrome).
    /// </summary>
    [Parameter] public RenderFragment? ComposerAddon { get; set; }

    /// <summary>
    /// Content rendered BELOW the built-in composer input row — a footnote, a remaining-character
    /// count or a secondary action. Only rendered with the built-in composer (a
    /// <see cref="ComposerTemplate"/> replaces the whole area and renders its own chrome).
    /// </summary>
    [Parameter] public RenderFragment? ComposerFooter { get; set; }

    /// <summary>
    /// Ids of host elements, e.g. inside <see cref="ComposerAddon"/>/<see cref="ComposerFooter"/>,
    /// describing the composer input. Rendered as <c>aria-describedby</c> on the built-in
    /// textarea; omitted from the DOM while <see langword="null"/> (the default). With a
    /// <see cref="ComposerTemplate"/> the built-in textarea does not render — describe the
    /// host-owned input there instead.
    /// </summary>
    [Parameter] public string? ComposerDescribedBy { get; set; }

    /// <summary>
    /// Whether the composer renders at all. <see langword="false"/> drops the composer area from
    /// the DOM — the read-only / viewing variant of a chat (e.g. a subscribe gate rendered through
    /// other markup, or a conversation the user may only read). Default <see langword="true"/>
    /// (today's behavior). When <see langword="false"/>, <see cref="ComposerTemplate"/> is ignored
    /// too.
    /// <para>
    /// Note that this covers only the MAIN composer: the thread panel's reply input is separate.
    /// A truly read-only chat also needs <see cref="EnableThreads"/>=<see langword="false"/> and
    /// <see cref="AllowEditDelete"/>=<see langword="false"/> so no reply input, edit or delete
    /// affordance renders.
    /// </para>
    /// </summary>
    [Parameter] public bool ShowComposer { get; set; } = true;

    /// <summary>
    /// Content rendered ABOVE the message list — a connection banner (offline / reconnecting) or
    /// any conversation-level callout. Rendered raw: TmChat adds NO wrapper element and
    /// deliberately does NOT turn the banner into an aria-live region.
    /// <para>
    /// IMPORTANT: TmChat may own exactly one live region (rule enforced by the a11y phase). If the
    /// banner content should be announced, put the <c>role="status"</c> (or an explicit
    /// <c>aria-live</c>) on an element INSIDE this slot and keep the text short and
    /// host-controlled — the slot content itself must carry the announcing role, never a wrapper
    /// TmChat would add around it.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment? BannerContent { get; set; }

    /// <summary>
    /// Content rendered at the VERY TOP of the component, above <see cref="BannerContent"/> and the
    /// message list — a conversation title bar or header controls. Rendered raw (no wrapper), so
    /// the host markup owns its layout.
    /// <para>
    /// Note that an OPEN THREAD PANEL OVERLAYS it: the thread panel is absolutely positioned over
    /// the component (top/right/bottom), so while a thread is open the panel covers the header
    /// area on the panel's side.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment? HeaderContent { get; set; }

    /// <summary>
    /// Replaces the built-in empty state (the icon + "no messages" texts) when the conversation has
    /// no messages. The built-in empty state is fully dropped, so the host owns the empty-scenario
    /// copy (e.g. a "start the conversation" call to action). Only applies while
    /// <c>Messages</c> is empty; with messages present the slot is not rendered.
    /// <para>
    /// The content renders RAW at the top of the message list (no wrapper element). The built-in
    /// empty state fills the message list and centers its content — for the built-in look the
    /// host's slot root should use <c>flex:1</c> plus vertical/horizontal centering, since it sits
    /// in the message list's flex column.
    /// </para>
    /// </summary>
    [Parameter] public RenderFragment? EmptyContent { get; set; }

    /// <summary>
    /// Whether the built-in message rows show the author avatar. Default <see langword="true"/>
    /// (today's behavior); set <see langword="false"/> for consumers whose bubbles carry no
    /// avatars. The switch applies to the BUILT-IN rows only — a <see cref="MessageTemplate"/>
    /// renders its own markup and decides about avatars itself.
    /// </summary>
    [Parameter] public bool ShowAvatars { get; set; } = true;

    private string _inputText = string.Empty;
    private ElementReference _messagesContainer;
    private ElementReference _composerInput;
    private bool _isSubmitting;

    // ── Composer slot state ──
    private static int _composerInputCounter;
    private readonly string _composerInputId = $"tm-chat-composer-{Interlocked.Increment(ref _composerInputCounter)}";
    private ChatComposerContext? _composerContext;

    // ── K6 interaction state ──
    private string? _activeThread;                       // internal fallback when not bound
    private string? _editingId;
    private string _editText = string.Empty;
    private string? _pickerForId;
    private string _threadInput = string.Empty;
    private readonly HashSet<string> _reportedRead = [];

    /// <summary>
    /// Text of the message as it was when its edit started — the baseline for the
    /// differs-from-original semantics of <see cref="TmChatDraftState.HasUnsavedContent"/> on the
    /// edit path (opening an edit without changes is not an unsaved draft).
    /// </summary>
    private string _editOriginalText = string.Empty;

    /// <summary>Last draft state handed to the parent — used to dedup <see cref="OnDraftChanged"/> notifications.</summary>
    private TmChatDraftState _lastDraftState = new(false, false, null, false);

    // ── Typing expiry state ──

    /// <summary>
    /// Clock behind the typing-expiry timer. Resolved through <see cref="IServiceProvider"/> so a
    /// consumer that never registers a <see cref="TimeProvider"/> still gets
    /// <see cref="TimeProvider.System"/> — tests register a FakeTimeProvider (the same pattern as
    /// <c>TmSearchInput</c>'s debounce clock).
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private TimeProvider Clock
        => _clock ??= Services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
    private TimeProvider? _clock;

    private ITimer? _typingTimer;
    private IReadOnlyList<ChatUser>? _lastTypingUsers;
    private ChatUser[] _lastTypingSnapshot = [];
    private TimeSpan? _lastTypingTimeout;
    private bool _typingExpired;
    private bool _disposed;

    /// <summary>Test-visible state: is a typing-expiry timer still armed?</summary>
    internal bool TypingTimerIsPending => _typingTimer is not null;

    /// <summary>Test-visible state: has the typing-expiry window already elapsed?</summary>
    internal bool TypingIndicatorExpired => _typingExpired;

    /// <summary>
    /// Whether the typing indicator renders (the built-in markup OR a <see cref="TypingTemplate"/>):
    /// there are typing users AND the expiry window has not elapsed (an elapsed window only
    /// suppresses while a timeout is actually configured — a host clearing
    /// <see cref="TypingTimeout"/> to null goes back to "never expires", so the indicator follows
    /// <see cref="TypingUsers"/> directly again).
    /// </summary>
    private bool ShowTypingIndicator
        => TypingUsers.Count > 0 && (!_typingExpired || TypingTimeout is null);

    private string? EffectiveActiveThread => ActiveThreadRootId ?? _activeThread;

    private string PlaceholderText => Placeholder ?? Loc["TmChat_Placeholder"];

    private bool IsSendDisabled => Disabled || string.IsNullOrWhiteSpace(_inputText) || _isSubmitting;

    // ── Composer slot surface: a live view over this component, handed to ComposerTemplate ──

    /// <summary>Current composer text — what the built-in textarea holds, or the last <see cref="SetComposerTextAsync"/> push.</summary>
    internal string ComposerText => _inputText;

    /// <summary>Whether a composer send is currently in flight.</summary>
    internal bool ComposerIsSubmitting => _isSubmitting;

    /// <summary>Whether sending is currently allowed (not disabled, not submitting, non-empty text).</summary>
    internal bool ComposerCanSend => !Disabled && !ComposerIsSubmitting && !string.IsNullOrWhiteSpace(_inputText);

    /// <summary>DOM id assigned to the built-in composer textarea; the attribute is present in the DOM only while that textarea renders.</summary>
    internal string ComposerInputId => _composerInputId;

    /// <summary>The per-component <see cref="ChatComposerContext"/>; the context is a live view, so one instance is reused.</summary>
    internal ChatComposerContext ComposerContext => _composerContext ??= new(this);

    /// <summary>
    /// Pushes custom-composer text into the component so the draft guard keeps covering a replaced
    /// composer. Dispatched like <see cref="DiscardDraftAsync"/> so it is safe from any thread.
    /// </summary>
    internal Task SetComposerTextAsync(string text)
        => InvokeAsync(async () =>
        {
            _inputText = text ?? string.Empty;
            StateHasChanged();
            await NotifyDraftChangedAsync();
        });

    /// <summary>
    /// Focuses the built-in composer textarea; a no-op while it is not rendered (custom composer
    /// or <see cref="ShowComposer"/>=<see langword="false"/>). Focus is best-effort: a failing or
    /// disconnected JS call (the input unmounted between renders, the circuit dropped) is
    /// swallowed rather than surfaced as an error.
    /// </summary>
    internal async ValueTask FocusComposerInputAsync()
    {
        if (!ShowComposer || ComposerTemplate is not null || string.IsNullOrEmpty(_composerInput.Id))
        {
            return;
        }

        try
        {
            await _composerInput.FocusAsync();
        }
        catch (JSException)
        {
            // The element is gone (or the focus call failed) — nothing to focus, nothing to report.
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone (Server) — focus is meaningless, not an error.
        }
    }

    /// <summary>
    /// Whether the component currently holds an unsaved draft in ANY slot — main composer, open
    /// thread panel's reply input, or a message edit whose text differs from the message as it
    /// was when the edit started. The SAME value carried by the
    /// <see cref="TmChatDraftState.HasUnsavedContent"/> flag that <see cref="OnDraftChanged"/>
    /// reports. Read live from the current input state (not from the last notification), so
    /// consumers can poll it at any time, for example before a conversation reload or a page
    /// navigation that would silently discard the draft.
    /// <para>
    /// Read it on the component's dispatcher (e.g. from an event handler or via
    /// <c>InvokeAsync</c>) when called from a background thread.
    /// </para>
    /// </summary>
    public bool HasUnsavedDraft => BuildDraftState().HasUnsavedContent;

    /// <summary>
    /// Clears every draft slot: the main composer, the open thread panel's reply input and any
    /// open message edit (the edit is cancelled, its text reset). The thread panel itself stays
    /// open — its visibility is the host's business (<see cref="ActiveThreadRootId"/>), only the
    /// unsaved text belongs to the draft. Intended for the consumer AFTER the user confirmed the
    /// discard in the consumer's OWN confirmation dialog — the component never shows a
    /// confirmation itself; that is deliberately the consumer's responsibility. Typical flow:
    /// <see cref="HasUnsavedDraft"/> reports a draft, the consumer asks, and on confirmation
    /// calls this method so the next reload does not ask again.
    /// <para>
    /// When no draft exists the call is a no-op — the state does not change and the
    /// value-equality dedup suppresses any <see cref="OnDraftChanged"/> notification.
    /// Safe to call from outside the render cycle; the reset, re-render and notification are
    /// dispatched through <c>InvokeAsync</c>.
    /// </para>
    /// </summary>
    public Task DiscardDraftAsync()
        => InvokeAsync(async () =>
        {
            _inputText = string.Empty;
            _threadInput = string.Empty;
            _editingId = null;
            _editText = string.Empty;
            _editOriginalText = string.Empty;
            StateHasChanged();
            await NotifyDraftChangedAsync();
        });

    /// <summary>
    /// Builds the current draft snapshot. <see cref="TmChatDraftState.HasComposerDraft"/> and
    /// <see cref="TmChatDraftState.HasThreadReplyDraft"/> mean not-whitespace-only text — the
    /// thread-reply slot only while the thread panel is actually mounted (its root message
    /// exists), so a parameter-driven unmount clears the slot immediately;
    /// <see cref="TmChatDraftState.EditingMessageId"/> identifies the message under edit;
    /// <see cref="TmChatDraftState.HasUnsavedContent"/> aggregates the three — the edit counts
    /// only once its text differs from the original message text (see the remarks on
    /// <see cref="OnDraftChanged"/>).
    /// </summary>
    private TmChatDraftState BuildDraftState()
    {
        var hasComposerDraft = !string.IsNullOrWhiteSpace(_inputText);
        var hasThreadReplyDraft = ActiveThreadRoot is not null && !string.IsNullOrWhiteSpace(_threadInput);
        var hasEditDraft = _editingId is not null
            && !string.Equals(_editText, _editOriginalText, StringComparison.Ordinal);

        return new TmChatDraftState(
            hasComposerDraft,
            hasThreadReplyDraft,
            _editingId,
            hasComposerDraft || hasThreadReplyDraft || hasEditDraft);
    }

    /// <summary>
    /// Notifies <see cref="OnDraftChanged"/> only when the draft state actually changed. The dedup
    /// is mandatory: all three inputs report content changes into this method (via the editors'
    /// bind after-hook, i.e. per keystroke in real usage), and without the value-equality check
    /// each keystroke would invoke the callback and re-render the parent; the record's value
    /// equality makes the comparison cheap and exact.
    /// </summary>
    private async Task NotifyDraftChangedAsync()
    {
        var state = BuildDraftState();
        if (state == _lastDraftState)
        {
            return;
        }

        _lastDraftState = state;
        await OnDraftChanged.InvokeAsync(state);
    }

    protected override async Task OnParametersSetAsync()
    {
        await base.OnParametersSetAsync();
        ScheduleTypingExpiry();
        // Parameter-driven transitions (e.g. the thread panel unmounting because its root message
        // left Messages) must be reported; the dedup keeps repeated parameter sets silent.
        await NotifyDraftChangedAsync();
    }

    // ── Typing expiry ──

    /// <summary>
    /// (Re)arms the typing-expiry window. A CHANGE of <see cref="TypingUsers"/> — a new list
    /// instance, a different content OR an in-place mutation of the same list instance (see
    /// <see cref="TypingTimeout"/>) — resets the expiry and opens a fresh window; a change of
    /// <see cref="TypingTimeout"/> alone rearms the pending timer (e.g. null → a duration starts
    /// measuring now) without reopening an already elapsed window. An unchanged list and timeout
    /// keep the running deadline — re-arming on every parent re-render would extend the window
    /// forever.
    /// </summary>
    private void ScheduleTypingExpiry()
    {
        // The list is stored BY REFERENCE, so an in-place mutation of the same instance is never
        // visible through it — the content comparison runs against a value SNAPSHOT of the last
        // seen list instead.
        var usersChanged = !ReferenceEquals(TypingUsers, _lastTypingUsers)
            || !TypingUsersContentEquals(TypingUsers, _lastTypingSnapshot);
        var timeoutChanged = !Nullable.Equals(TypingTimeout, _lastTypingTimeout);
        _lastTypingUsers = TypingUsers;
        _lastTypingTimeout = TypingTimeout;
        _lastTypingSnapshot = TypingUsers.ToArray();

        if (usersChanged) _typingExpired = false;
        if (!usersChanged && !timeoutChanged) return;
        RearmTypingTimer();
    }

    /// <summary>Order-sensitive per-item value comparison of two typing-user lists.</summary>
    private static bool TypingUsersContentEquals(IReadOnlyList<ChatUser> current, IReadOnlyList<ChatUser>? previous)
    {
        if (previous is null || current.Count != previous.Count) return false;
        for (var i = 0; i < current.Count; i++)
        {
            if (!current[i].Equals(previous[i])) return false;
        }
        return true;
    }

    /// <summary>Disarms any pending timer and arms a fresh single-shot one when an expiry applies.</summary>
    private void RearmTypingTimer()
    {
        CancelTypingTimer();
        if (TypingTimeout is not { } timeout || TypingUsers.Count == 0 || _typingExpired) return;
        if (timeout <= TimeSpan.Zero)
        {
            // A non-positive timeout expires IMMEDIATELY: no timer, the next render suppresses.
            _typingExpired = true;
            return;
        }

        // Single-shot timer on the injected clock. The callback closes over `timer` itself so the
        // ReferenceEquals check inside can tell "the currently armed timer" apart from one that
        // was already replaced or cancelled.
        ITimer? timer = null;
        timer = Clock.CreateTimer(_ => _ = OnTypingExpiredAsync(timer), null, timeout, Timeout.InfiniteTimeSpan);
        _typingTimer = timer;
    }

    private void CancelTypingTimer()
    {
        _typingTimer?.Dispose();
        _typingTimer = null;
    }

    private async Task OnTypingExpiredAsync(ITimer? timer)
    {
        if (timer is null || _disposed) return;
        // The callback runs on a thread-pool thread (TimeProvider.System) or the test thread
        // advancing a FakeTimeProvider; hop to the renderer before touching expiry state.
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed || !ReferenceEquals(_typingTimer, timer)) return Task.CompletedTask;
                // Single-shot: once fired it can never expire again — disarm so
                // TypingTimerIsPending reports the truth instead of a spent timer.
                _typingTimer = null;
                timer.Dispose();
                _typingExpired = true;
                StateHasChanged();
                return Task.CompletedTask;
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer (or the whole component) was disposed between the timer firing and the
            // dispatch — the expiry no longer has anything to render into, not an error.
        }
    }

    /// <summary>
    /// Cancels the pending typing-expiry timer by delegating to <see cref="DisposeAsyncCore"/>.
    /// TmChat previously held no unmanaged lifecycle, so it did not implement disposal; the timer
    /// made an <see cref="IAsyncDisposable"/> implementation necessary.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The actual cleanup behind <see cref="DisposeAsync"/> — SUBCLASSES OVERRIDE THIS method (not
    /// <see cref="DisposeAsync"/> itself) to add their own teardown, ideally awaiting/calling
    /// <c>base.DisposeAsyncCore()</c>. Idempotent — the renderer may dispose the component again
    /// after the host did.
    /// </summary>
    protected virtual ValueTask DisposeAsyncCore()
    {
        _disposed = true;
        CancelTypingTimer();
        return ValueTask.CompletedTask;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender || Messages.Count > 0)
        {
            await ScrollToBottomAsync();
        }

        await ReportReadReceiptsAsync();
    }

    private async Task ReportReadReceiptsAsync()
    {
        if (!AutoReadReceipts || !OnMessageRead.HasDelegate) return;

        // Enumerate a snapshot: the handler may mutate the host's message list
        // (e.g. a shared conversation store) while we iterate, and the list is not ours.
        foreach (var message in Messages.ToArray())
        {
            if (message.Type == ChatMessageType.System || message.IsDeleted) continue;
            if (IsCurrentUser(message.Author)) continue;
            if (string.IsNullOrEmpty(message.Id) || _reportedRead.Contains(message.Id)) continue;
            if (message.IsReadByUser(CurrentUser.Id)) continue;

            _reportedRead.Add(message.Id);
            await OnMessageRead.InvokeAsync(message.Id);
        }
    }

    private async Task ScrollToBottomAsync()
    {
        await Task.CompletedTask;
    }

    private string GetMessageCssClass(ChatMessage message)
    {
        var baseClass = "tm-chat__message";
        var suffix = message.Type switch
        {
            ChatMessageType.System => "system",
            ChatMessageType.Outgoing => "outgoing",
            _ => IsCurrentUser(message.Author) ? "outgoing" : "incoming"
        };
        return $"{baseClass} {baseClass}--{suffix}";
    }

    // ── Custom templates ──

    /// <summary>
    /// Whether <paramref name="message"/> renders through <see cref="MessageTemplate"/>. The
    /// template must be set, and — when <see cref="UseTemplateFor"/> is provided — the message
    /// must match its predicate (see the precedence documented on <see cref="MessageTemplate"/>).
    /// </summary>
    private bool UsesTemplate(ChatMessage message)
        => MessageTemplate is not null && (UseTemplateFor?.Invoke(message) ?? true);

    /// <summary>Builds the <see cref="ChatMessageContext"/> for one rendered message row.</summary>
    private ChatMessageContext CreateTemplateContext(
        ChatMessage message, bool inThread, int index, bool isFirstInGroup)
        => new(message,
            isOwn: IsCurrentUser(message.Author),
            inThread: inThread,
            index: index,
            isFirstInGroup: isFirstInGroup,
            defaultContent: RenderBuiltInMessage(message, inThread));

    /// <summary>
    /// The root message sequence with the per-row template inputs: the zero-based
    /// <c>Index</c> within the sequence and <c>IsFirstInGroup</c> (the previous rendered message
    /// has a different author or a different <see cref="ChatMessage.Kind"/>).
    /// </summary>
    private IEnumerable<(ChatMessage Message, int Index, bool IsFirstInGroup)> RootEntries()
        => EntriesWithTemplateInputs(RootMessages());

    /// <summary>
    /// The thread panel sequence (root first, then replies ordered by timestamp) with the same
    /// per-row template inputs as <see cref="RootEntries"/>.
    /// </summary>
    private IEnumerable<(ChatMessage Message, int Index, bool IsFirstInGroup)> ThreadEntries(ChatMessage root)
        => EntriesWithTemplateInputs(new[] { root }.Concat(RepliesOf(root.Id)));

    private static IEnumerable<(ChatMessage Message, int Index, bool IsFirstInGroup)> EntriesWithTemplateInputs(
        IEnumerable<ChatMessage> sequence)
    {
        ChatMessage? previous = null;
        var index = 0;
        foreach (var message in sequence)
        {
            yield return (message, index, IsFirstInGroup(message, previous));
            previous = message;
            index++;
        }
    }

    private static bool IsFirstInGroup(ChatMessage message, ChatMessage? previous)
    {
        if (previous is null) return true;
        return !string.Equals(previous.Author?.Id, message.Author?.Id, StringComparison.Ordinal)
            || !string.Equals(previous.Kind, message.Kind, StringComparison.Ordinal);
    }

    private bool IsCurrentUser(ChatUser? user)
    {
        if (user is null) return false;
        return user.Id == CurrentUser.Id;
    }

    private string FormatTimestamp(DateTimeOffset timestamp)
    {
        var local = timestamp.ToLocalTime();
        return local.ToString("HH:mm");
    }

    private static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue) return "";
        var b = bytes.Value;
        return b switch
        {
            < 1024 => $"{b} B",
            < 1024 * 1024 => $"{b / 1024.0:0.0} KB",
            < 1024 * 1024 * 1024 => $"{b / (1024.0 * 1024.0):0.0} MB",
            _ => $"{b / (1024.0 * 1024.0 * 1024.0):0.0} GB"
        };
    }

    private async Task HandleSendAsync()
    {
        await SubmitComposerAsync();
    }

    /// <summary>
    /// The single send path of the composer, shared by the built-in send button and
    /// <see cref="ChatComposerContext.SendAsync"/>: the text is trimmed and cleared, both send
    /// events fire, the submit state flips for the duration of the host handlers and the cleared
    /// composer is reported through the draft guard. A no-op while the composer cannot send
    /// (<see cref="IsSendDisabled"/>). Call on the component's dispatcher (an event handler or via
    /// <c>InvokeAsync</c>) — like the built-in button, this mutates state directly.
    /// </summary>
    internal async Task SubmitComposerAsync()
    {
        if (IsSendDisabled) return;
        var text = _inputText.Trim();
        _inputText = string.Empty;
        _isSubmitting = true;
        StateHasChanged();
        try
        {
            await OnSendMessage.InvokeAsync(text);
            await OnSend.InvokeAsync(new ChatSendRequest(text));
        }
        finally
        {
            _isSubmitting = false;
        }
        StateHasChanged();
        // The composer slot just cleared — report it exactly once through the shared notify.
        await NotifyDraftChangedAsync();
    }

    /// <summary>
    /// Dispatches <see cref="SubmitComposerAsync"/> through <see cref="InvokeAsync"/> so a host
    /// may call <see cref="ChatComposerContext.SendAsync"/> from any thread (a custom composer's
    /// handler is not guaranteed to run on the renderer's synchronisation context, and the direct
    /// path mutates state). The built-in button stays on the direct path — it always runs on the
    /// dispatcher already.
    /// </summary>
    internal Task SendComposerViaDispatcherAsync() => InvokeAsync(SubmitComposerAsync);

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await HandleSendAsync();
        }
    }

    // ── K6: threads ──

    private bool IsThreadReply(ChatMessage m)
        => EnableThreads && !string.IsNullOrEmpty(m.ThreadRootId) && m.ThreadRootId != m.Id;

    private IEnumerable<ChatMessage> RootMessages()
        => EnableThreads ? Messages.Where(m => !IsThreadReply(m)) : Messages;

    private IReadOnlyList<ChatMessage> RepliesOf(string rootId)
        => Messages.Where(m => m.ThreadRootId == rootId && m.Id != rootId)
                   .OrderBy(m => m.Timestamp)
                   .ToList();

    private int ReplyCountOf(ChatMessage root)
    {
        var counted = RepliesOf(root.Id).Count;
        return counted > 0 ? counted : root.ReplyCount;
    }

    private ChatMessage? ActiveThreadRoot
        => EffectiveActiveThread is null ? null : Messages.FirstOrDefault(m => m.Id == EffectiveActiveThread);

    private async Task OpenThreadAsync(ChatMessage root)
    {
        _activeThread = root.Id;
        _pickerForId = null;
        await ActiveThreadRootIdChanged.InvokeAsync(root.Id);
        await OnReply.InvokeAsync(root);
    }

    private async Task CloseThreadAsync()
    {
        _activeThread = null;
        _threadInput = string.Empty;
        await ActiveThreadRootIdChanged.InvokeAsync(null);
        // Closing the panel clears its reply draft — report the cleared slot.
        await NotifyDraftChangedAsync();
    }

    private async Task SendThreadReplyAsync()
    {
        var root = ActiveThreadRoot;
        if (root is null) return;
        var text = _threadInput.Trim();
        if (string.IsNullOrEmpty(text)) return;
        _threadInput = string.Empty;
        await OnSend.InvokeAsync(new ChatSendRequest(text, replyToId: root.Id, threadRootId: root.Id));
        await NotifyDraftChangedAsync();
    }

    private async Task HandleThreadKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await SendThreadReplyAsync();
        }
    }

    // ── K6: edit / delete ──

    private bool CanEditDelete(ChatMessage m)
        => AllowEditDelete && !m.IsDeleted && m.Type != ChatMessageType.System && IsCurrentUser(m.Author);

    // Actions are only offered when their handler is actually wired, so a read-only
    // host (e.g. the basic demo) shows no dead buttons.
    private bool CanReact => EnableReactions && OnToggleReaction.HasDelegate;
    private bool CanReplyInThread => EnableThreads && OnSend.HasDelegate;
    private bool CanEditMessage(ChatMessage m) => CanEditDelete(m) && OnEditMessage.HasDelegate;
    private bool CanDeleteMessage(ChatMessage m) => CanEditDelete(m) && OnDeleteMessage.HasDelegate;
    private bool HasMessageActions(ChatMessage m, bool inThread)
        => CanReact || (!inThread && CanReplyInThread) || CanEditMessage(m) || CanDeleteMessage(m);

    private async Task StartEdit(ChatMessage m)
    {
        _editingId = m.Id;
        _editText = m.Text;
        _editOriginalText = m.Text;
        _pickerForId = null;
        // The edit state itself is a transition the consumer must see (editing id set), even
        // though HasUnsavedContent is still false until the text changes.
        await NotifyDraftChangedAsync();
    }

    private async Task CancelEdit()
    {
        _editingId = null;
        _editText = string.Empty;
        _editOriginalText = string.Empty;
        await NotifyDraftChangedAsync();
    }

    private async Task CommitEditAsync()
    {
        if (_editingId is null) return;
        var id = _editingId;
        var text = _editText.Trim();
        _editingId = null;
        if (!string.IsNullOrEmpty(text))
        {
            await OnEditMessage.InvokeAsync(new ChatMessageEdit(id, text));
        }
        // The edit closed (committed or discarded as empty) — report the cleared editing state.
        await NotifyDraftChangedAsync();
    }

    private async Task HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !e.ShiftKey)
        {
            await CommitEditAsync();
        }
        else if (e.Key == "Escape")
        {
            await CancelEdit();
        }
    }

    private async Task DeleteAsync(ChatMessage m)
    {
        await OnDeleteMessage.InvokeAsync(m.Id);
    }

    // ── K6: reactions ──

    private void TogglePicker(string messageId)
        => _pickerForId = _pickerForId == messageId ? null : messageId;

    private async Task ToggleReactionAsync(string messageId, string emoji)
    {
        _pickerForId = null;
        await OnToggleReaction.InvokeAsync(new ChatReactionToggle(messageId, emoji));
    }

    private string GetReactionChipClass(ChatReaction reaction)
    {
        var cls = "tm-chat__reaction";
        if (reaction.ReactedBy(CurrentUser.Id)) cls += " tm-chat__reaction--mine";
        return cls;
    }

    // ── K6: read receipts ──

    private IReadOnlyList<ChatReadReceipt> OtherReaders(ChatMessage m)
        => m.ReadBy.Where(r => r.User is not null && r.User.Id != CurrentUser.Id).ToList();

    private string GetReceiptsText(ChatMessage m)
    {
        var readers = OtherReaders(m);
        if (readers.Count == 0) return string.Empty;
        var names = string.Join(", ", readers.Select(r => r.User!.Name));
        return string.Format(Loc["TmChat_ReadBy"], names);
    }

    private async Task HandleAttachmentClick(ChatAttachment attachment)
    {
        await OnAttachmentClick.InvokeAsync(attachment);
    }

    private string GetTypingText()
    {
        if (TypingUsers.Count == 0) return string.Empty;
        if (TypingLabel is not null) return TypingLabel(TypingUsers);
        if (TypingUsers.Count == 1)
            return string.Format(Loc["TmChat_TypingSingle"], TypingUsers[0].Name);
        if (TypingUsers.Count == 2)
            return string.Format(Loc["TmChat_TypingTwo"], TypingUsers[0].Name, TypingUsers[1].Name);
        return string.Format(Loc["TmChat_TypingMany"], TypingUsers[0].Name, TypingUsers.Count - 1);
    }
}
