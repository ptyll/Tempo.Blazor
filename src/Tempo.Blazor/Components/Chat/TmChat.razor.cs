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

    // ── Layout (FZE-8) ──

    /// <summary>
    /// Lets the chat be a flex CHILD of the host layout. <see langword="false"/> (the default)
    /// keeps today's behavior: the chat fills its host box (<c>height: 100%</c>) and never shrinks
    /// below its <c>--tm-chat-min-height</c> floor (default 300px). <see langword="true"/> adds the
    /// <c>tm-chat--fill</c> modifier — <c>flex: 1 1 auto; height: auto;
    /// min-height: var(--tm-chat-min-height, 0)</c> — so the chat grows and shrinks with the
    /// parent's free space and the internal message scroller (itself <c>min-height: 0</c>)
    /// absorbs the difference. The modifier reads the knob with a ZERO fallback (rather than a
    /// bare 0) so a floor declared on a wrapper or <c>:root</c> keeps applying in fill mode too;
    /// a host that wants the parent to own the height entirely sets the knob to 0. Use it when
    /// the chat sits in a flex row/column of an app shell (a sidebar pane, a split view); inside
    /// a non-flex block keep the default.
    /// </summary>
    [Parameter] public bool FillParent { get; set; }

    /// <summary>
    /// The root class list. The FZE-8 modifier is emitted from here as a LITERAL (both variants
    /// appear in this file verbatim) so MarkupClassCoverageTests measures it: an embedded ternary
    /// inside the markup's class attribute would truncate the scan at the expression's quote and
    /// demote <c>tm-chat</c> itself to an unmeasurable interpolated stem.
    /// </summary>
    private string RootCssClass => FillParent ? "tm-chat tm-chat--fill" : "tm-chat";

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

    /// <summary>
    /// Whether pressing Enter in a composer input submits. Default <see langword="true"/>
    /// (today's behavior: the built-in composer, the thread panel's reply input and an open
    /// message edit all submit on Enter). <see langword="false"/> makes Enter a NATIVE new line
    /// in all three inputs — nothing is sent from Enter itself, only from the send button,
    /// <see cref="ChatComposerContext.SendAsync"/> or Ctrl/⌘+Enter (the Ctrl/⌘+Enter submit is
    /// handled by the <c>chat.js</c> module and requires no Shift/Alt and no IME composition;
    /// Shift+Enter and Alt+Enter stay native). Shift+Enter always inserts a new line and
    /// Alt/Ctrl/Meta+Enter always stay native when <see cref="EnterToSend"/> is
    /// <see langword="true"/> (browser and host shortcuts).
    /// <para>
    /// The keydown handling lives in the <c>chat.js</c> module once it is attached (it also owns
    /// the IME guard, which a net8 <see cref="Web.KeyboardEventArgs"/> cannot express); this
    /// parameter is passed to the module as its <c>enterToSend</c> option and re-attaches the
    /// module when it changes. The built-in inputs advertise the mode through
    /// <c>enterkeyhint="send"|"enter"</c> and the composer hint
    /// (<c>TmChat_ComposerHint_EnterToSend</c>/<c>TmChat_ComposerHint_EnterNewLine</c>).
    /// </para>
    /// </summary>
    [Parameter] public bool EnterToSend { get; set; } = true;

    /// <summary>
    /// Whether the composer input is focused again after a SUCCESSFUL send. Default
    /// <see langword="true"/> — after the send handlers complete and the composer clears, focus
    /// returns to the input (the built-in textarea, or the element a custom composer registered
    /// through <see cref="ChatComposerContext.AttachAsync"/>) so the user can type the next
    /// message without reaching for the mouse. Set <see langword="false"/> for hosts that move
    /// focus themselves (e.g. into a confirmation UI). A failed send (a handler that throws)
    /// does not refocus — the text is preserved and the user keeps their context.
    /// </summary>
    [Parameter] public bool RefocusAfterSend { get; set; } = true;

    private string _inputText = string.Empty;
    private ElementReference _messagesContainer;
    private ElementReference _composerInput;
    private ElementReference _threadInputElement;
    private ElementReference _editInputElement;
    private bool _isSubmitting;
    private bool _isThreadSubmitting;

    // ── Composer slot state ──
    private static int _composerInputCounter;
    private readonly string _composerInputId = $"tm-chat-composer-{Interlocked.Increment(ref _composerInputCounter)}";
    private readonly string _threadInputId = $"tm-chat-thread-{Interlocked.Increment(ref _composerInputCounter)}";
    // The thread panel's aria-labelledby target (the title span) — same per-instance counter, so
    // several chats on one page never collide (MarkupClassCoverageTests scans the id prefixes).
    private readonly string _threadTitleId = $"tm-chat-thread-title-{Interlocked.Increment(ref _composerInputCounter)}";
    private ChatComposerContext? _composerContext;

    // ── K6 interaction state ──
    private string? _activeThread;                       // internal fallback when not bound
    private string? _editingId;
    private bool _isEditSubmitting;                      // a duplicate dispatch commits once
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

    /// <summary>JS runtime behind the composer module (<c>chat.js</c>); null only before initialization.</summary>
    [Inject] private IJSRuntime JS { get; set; } = default!;

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

    private bool IsThreadSendDisabled => Disabled || string.IsNullOrWhiteSpace(_threadInput) || _isThreadSubmitting;

    /// <summary>Localized submit hint for the built-in composer, following <see cref="EnterToSend"/>.</summary>
    private string ComposerHintText
        => EnterToSend ? Loc["TmChat_ComposerHint_EnterToSend"] : Loc["TmChat_ComposerHint_EnterNewLine"];

    /// <summary>
    /// The <c>aria-describedby</c> of the built-in textarea: the host's
    /// <see cref="ComposerDescribedBy"/> ids merged (space-separated) with the component's own
    /// hidden submit hint; the hint id alone when no host ids are given.
    /// </summary>
    private string ComposerDescribedByAttribute
        => string.IsNullOrWhiteSpace(ComposerDescribedBy)
            ? $"{_composerInputId}-hint"
            : $"{ComposerDescribedBy} {_composerInputId}-hint";

    private string ComposerEnterKeyHint => EnterToSend ? "send" : "enter";

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
    /// Focuses the composer input — the shared logic behind <see cref="FocusComposerAsync"/>,
    /// <see cref="ChatComposerContext.FocusAsync"/> and the post-send refocus
    /// (<see cref="RefocusAfterSend"/>): the element a custom composer registered through
    /// <see cref="ChatComposerContext.AttachAsync"/> if there is one, otherwise the built-in
    /// textarea. Focus is best-effort: a failing or disconnected JS call (the input unmounted
    /// between renders, the circuit dropped) is swallowed rather than surfaced as an error, and
    /// an element that already holds focus is left untouched (the module's
    /// <c>focusIfNeeded</c>) so the on-screen keyboard is not closed on touch devices.
    /// </summary>
    internal async ValueTask FocusComposerInputAsync()
    {
        if (ComposerTemplate is not null)
        {
            // A custom composer replaced the built-in textarea — the attached element is the
            // focus target. Nothing to focus while nothing is attached yet (or the module is
            // still importing): the host owns the input there.
            if (_module is null) return;
            var element = _customComposerElement;
            if (element is not { } target || string.IsNullOrEmpty(target.Id)) return;
            try
            {
                await _module.InvokeVoidAsync("focusIfNeeded", target);
            }
            catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
            {
                // Focus is best-effort: a disconnected circuit or an unmounted input is not an error.
            }

            return;
        }

        if (!ShowComposer || string.IsNullOrEmpty(_composerInput.Id))
        {
            return;
        }

        try
        {
            if (_module is not null)
            {
                // The module is loaded — go through focusIfNeeded so an element that already
                // holds focus is left untouched (the on-screen keyboard stays open on touch).
                await _module.InvokeVoidAsync("focusIfNeeded", _composerInput);
            }
            else
            {
                await _composerInput.FocusAsync();
            }
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // The element is gone, the circuit dropped or the renderer was disposed — nothing to
            // focus, nothing to report (focus is best-effort; the same contract as in chat.js).
        }
    }

    /// <summary>
    /// Moves focus to the composer input — the public API for hosts that need to refocus the
    /// chat from their own UI (e.g. after a "new messages" toast or a navigation back to the
    /// conversation). With a custom composer the attached element is focused (register the
    /// host's own input through <see cref="ChatComposerContext.AttachAsync"/>); otherwise the
    /// built-in textarea is. An element that already holds focus is left untouched. A no-op
    /// while no composer input is rendered (or the circuit is gone).
    /// </summary>
    public ValueTask FocusComposerAsync() => FocusComposerInputAsync();

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
        // Classify the message-list change FIRST and BEFORE any await: ComponentBase renders as
        // soon as OnParametersSetAsync yields an unfinished task, so OnAfterRenderAsync can run
        // while this method is still pending (e.g. behind a slow OnDraftChanged handler) and
        // must already see the mode of THIS parameter set, not a stale one.
        DetectScrollChange();
        // FZE-7: the appended rows feed the announcement pipeline — decided SYNCHRONOUSLY,
        // before the first await, exactly like the scroll classification. The leading flush runs
        // in the awaited half below.
        ScheduleAnnouncements();
        // Drop affordance references of rows that no longer render — the thread close path must
        // never hand a stale element to restoreFocus.
        PruneThreadRefs();
        await FlushAnnouncementsIfDueAsync();

        // Parameter-driven transitions (e.g. the thread panel unmounting because its root message
        // left Messages) must be reported; the dedup keeps repeated parameter sets silent.
        await NotifyDraftChangedAsync();
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
    /// after the host did. Disposes the typing timer and the <c>chat.js</c> composer module with
    /// its per-input handles (a dropped circuit is swallowed — the JS side is unreachable anyway).
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        _disposed = true;
        CancelTypingTimer();
        CancelAnnouncementTimer();
        await DisposeScrollerAsync();
        await DisposeComposerInteropAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            // The module and the DotNetObjectReference are created unconditionally on the first
            // render — the composer and the scroller hang their work off the same module.
            _selfRef ??= DotNetObjectReference.Create(this);
            await EnsureModuleAsync();
        }

        await SyncScrollerAsync();
        await ApplyScrollAfterRenderAsync(firstRender);
        await ReportReadReceiptsAsync();
        await SyncJsComposersAsync();
        // LAST: the thread panel may have just closed (its own button OR a parameter-driven
        // unmount) — the opener button regains focus once the DOM is settled.
        await SyncThreadOpenerFocusAfterRenderAsync();
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
    /// The single send path of the composer, shared by the built-in send button,
    /// <see cref="ChatComposerContext.SendAsync"/> and the <c>chat.js</c> Enter submit
    /// (<see cref="OnComposerSubmit"/>): the text is trimmed, both send events fire, the submit
    /// state flips for the duration of the host handlers and — only after BOTH handlers
    /// completed without throwing — the composer is cleared, reported through the draft guard
    /// and refocused (<see cref="RefocusAfterSend"/>). A host handler that throws keeps the
    /// text in place (the draft is not lost) and the exception propagates to the caller.
    /// <para>
    /// Text typed while the send is in flight is KEPT: the clear compares against a snapshot
    /// (<c>raw</c>) taken before the handlers ran — text equal to the snapshot is cleared, a
    /// superset of it loses only the sent prefix (trimmed), and anything else (the user rewrote
    /// the whole draft mid-send) is left untouched. The submit flag is reset in a
    /// <c>finally</c> with a re-render, so a failed Enter-path send never leaves the send
    /// button disabled.
    /// </para>
    /// A no-op while the composer cannot send (<see cref="IsSendDisabled"/>), which also makes
    /// a duplicate dispatch (Enter plus a late fallback) a no-op rather than a second send.
    /// Call on the component's dispatcher (an event handler or via <c>InvokeAsync</c>) — like
    /// the built-in button, this mutates state directly.
    /// </summary>
    internal async Task SubmitComposerAsync()
    {
        if (IsSendDisabled) return;
        var text = _inputText.Trim();
        var raw = _inputText;
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
            StateHasChanged();
        }

        // The send SUCCEEDED (both handlers completed): clear the composer and report it
        // through the shared notify. The clear deliberately happens AFTER the handlers — a
        // failing handler must not swallow the user's text (FZE-5 decision) — and keeps text
        // typed while the send was in flight (only the sent prefix is removed).
        if (_inputText == raw)
        {
            _inputText = string.Empty;
        }
        else if (_inputText.StartsWith(raw, StringComparison.Ordinal))
        {
            _inputText = _inputText[raw.Length..].TrimStart();
        }

        StateHasChanged();
        await NotifyDraftChangedAsync();
        await ResizeComposerAfterClearAsync(_mainSlot);

        if (RefocusAfterSend)
        {
            await RefocusComposerAfterSendAsync();
        }
    }

    /// <summary>
    /// Dispatches <see cref="SubmitComposerAsync"/> through <see cref="InvokeAsync"/> so a host
    /// may call <see cref="ChatComposerContext.SendAsync"/> from any thread (a custom composer's
    /// handler is not guaranteed to run on the renderer's synchronisation context, and the direct
    /// path mutates state). The built-in button stays on the direct path — it always runs on the
    /// dispatcher already.
    /// </summary>
    internal Task SendComposerViaDispatcherAsync() => InvokeAsync(SubmitComposerAsync);

    /// <summary>
    /// Whether this keydown is a submit-Enter: Enter without Shift (the documented new line),
    /// Alt, Ctrl or Meta (browser and host shortcuts). The IME guard is applied by the
    /// <c>chat.js</c> module; the C# fallback adds its own net9+ guard.
    /// </summary>
    private static bool IsSubmitEnter(KeyboardEventArgs e)
        => e.Key == "Enter" && !e.ShiftKey && !e.AltKey && !e.CtrlKey && !e.MetaKey;

    /// <summary>
    /// Fallback Enter handler for the built-in composer — active only while the
    /// <c>chat.js</c> module is not attached yet (prerender / before interactivity / a failed
    /// import); once attached, the JS listener owns the submit (with <c>preventDefault</c>) and
    /// this handler stays quiet so Enter activates exactly once.
    /// </summary>
    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        if (_mainSlot.JsOwnsEnter) return;
        if (!EnterToSend || !IsSubmitEnter(e)) return;
#if NET9_0_OR_GREATER
        if (e.IsComposing) return; // net9+ fallback IME guard; the primary guard lives in chat.js
#endif
        await HandleSendAsync();
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

    private async Task OpenThreadAsync(ChatMessage root, ThreadOpenerKind opener)
    {
        // The button that opens the thread gets the focus back when it closes (FZE-7) — the
        // close button itself unmounts with the panel and would drop the focus to the body.
        CaptureThreadOpener(root.Id, opener);
        // The panel's reply input receives the focus after the render that mounts it — a
        // keyboard user can type the reply at once (best effort, see the render sync).
        _focusThreadInputAfterRender = true;
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
        if (_isThreadSubmitting) return; // a duplicate dispatch (Enter + late fallback) sends once
        var text = _threadInput.Trim();
        if (string.IsNullOrEmpty(text)) return;
        var raw = _threadInput;
        _isThreadSubmitting = true;
        StateHasChanged();
        try
        {
            await OnSend.InvokeAsync(new ChatSendRequest(text, replyToId: root.Id, threadRootId: root.Id));
        }
        finally
        {
            _isThreadSubmitting = false;
            StateHasChanged();
        }

        // Cleared only after the host handler completed — a failed send keeps the reply text
        // (the same FZE-5 decision as the main composer), and text typed while the send was in
        // flight is kept (only the sent prefix is removed).
        if (_threadInput == raw)
        {
            _threadInput = string.Empty;
        }
        else if (_threadInput.StartsWith(raw, StringComparison.Ordinal))
        {
            _threadInput = _threadInput[raw.Length..].TrimStart();
        }

        StateHasChanged();
        await NotifyDraftChangedAsync();
        await ResizeComposerAfterClearAsync(_threadSlot);

        if (RefocusAfterSend)
        {
            await FocusThreadInputBestEffortAsync();
        }
    }

    /// <summary>
    /// Best-effort focus of the thread panel's reply input — shared by the open path (the panel
    /// just mounted) and the post-send refocus (the same contract as the composer refocus): a
    /// failing or disconnected JS call (the input unmounted between renders, the circuit
    /// dropped) is swallowed rather than surfaced.
    /// </summary>
    private async Task FocusThreadInputBestEffortAsync()
    {
        if (string.IsNullOrEmpty(_threadInputElement.Id)) return;
        try
        {
            await _threadInputElement.FocusAsync();
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Focus is best-effort: a disconnected circuit or an unmounted input is not an error.
        }
    }

    /// <summary>
    /// Fallback Enter handler for the thread panel's reply input — same contract as
    /// <see cref="HandleKeyDownAsync"/>: quiet once the <c>chat.js</c> module owns the input.
    /// </summary>
    private async Task HandleThreadKeyDownAsync(KeyboardEventArgs e)
    {
        if (_threadSlot.JsOwnsEnter) return;
        if (!EnterToSend || !IsSubmitEnter(e)) return;
#if NET9_0_OR_GREATER
        if (e.IsComposing) return; // net9+ fallback IME guard; the primary guard lives in chat.js
#endif
        await SendThreadReplyAsync();
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
        if (_editingId is null || _isEditSubmitting) return; // a duplicate dispatch commits once
        var id = _editingId;
        var text = _editText.Trim();
        _isEditSubmitting = true;
        try
        {
            if (!string.IsNullOrEmpty(text))
            {
                await OnEditMessage.InvokeAsync(new ChatMessageEdit(id, text));
            }
            // Cleared only after the awaited handler: a throwing handler keeps the edit open
            // with its text (the same FZE-5 decision as the failed sends), and the id stays a
            // valid dedup key for the window where Enter reaches both the JS listener and the
            // C# fallback.
            _editingId = null;
        }
        finally
        {
            _isEditSubmitting = false;
        }
        // The edit closed (committed or discarded as empty) — report the cleared editing state.
        await NotifyDraftChangedAsync();
    }

    /// <summary>
    /// Fallback keyboard handler for the message edit: Escape cancels, Enter commits. Enter is
    /// quiet once the <c>chat.js</c> module owns the input (and honours <see cref="EnterToSend"/>
    /// — <see langword="false"/> leaves Enter as a new line in the edit too, so multi-line edits
    /// are possible; the Save button and <see cref="OnEditComposerSubmit"/> still commit).
    /// </summary>
    private async Task HandleEditKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            await CancelEdit();
            return;
        }

        if (_editSlot.JsOwnsEnter) return;
        if (!EnterToSend || !IsSubmitEnter(e)) return;
#if NET9_0_OR_GREATER
        if (e.IsComposing) return; // net9+ fallback IME guard; the primary guard lives in chat.js
#endif
        await CommitEditAsync();
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
