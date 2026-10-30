using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// A strongly-typed, GENERIC wrapper over <see cref="TmChat"/>: hosts keep their own domain
/// sequence (<c>Items</c>) and map each item to a <see cref="ChatMessage"/> with
/// <c>MessageSelector</c> — the mapped message always carries the item as its
/// <see cref="ChatMessage.Payload"/> (unless the selector supplies its own payload), so even a
/// plain <c>TmChat</c> <c>MessageTemplate</c> can read it back with
/// <c>PayloadAs&lt;TItem&gt;()</c>. An <c>ItemTemplate</c> receives a
/// <see cref="ChatItemContext{TItem}"/> (the item plus everything TmChat knows around the
/// message) and can be restricted with a typed <c>UseTemplateFor</c> predicate with exactly the
/// precedence <c>TmChat.UseTemplateFor</c> documents.
/// <para>
/// <b>TmChat stays non-generic.</b> This component is pure composition: it owns no JS, no timers
/// and no disposables of its own — the inner <see cref="TmChat"/> (available through
/// <see cref="Chat"/>) implements <see cref="IAsyncDisposable"/> and registers the draft guard,
/// so <c>TmTypedChat</c> does NOT duplicate <c>ITmDraftGuard</c> and does not need to be
/// disposable itself.
/// </para>
/// <para>
/// <b>Forwarding contract:</b> every other <c>TmChat</c> parameter (<c>OnSend</c>,
/// <c>ComposerTemplate</c>, <c>AutoAttachComposerInput</c>, <c>RenderWindowSize</c>, …) is
/// forwarded by attribute splatting — pass it on <c>TmTypedChat</c> and it reaches the inner
/// chat. There is NO IntelliSense for these forwarded parameters and the compiler cannot type
/// check them; a typo surfaces as an unmatched attribute (silently ignored or forwarded to the
/// DOM) instead of a compile error. <c>Messages</c>, <c>MessageTemplate</c> and
/// <c>UseTemplateFor</c> are OWNED by this component: passing any of them through the splat
/// throws an <see cref="ArgumentException"/> naming the conflicting attribute.
/// </para>
/// </summary>
/// <typeparam name="TItem">The host's domain type the conversation is built from.</typeparam>
/// <example>
/// <code>
/// &lt;TmTypedChat Items="_tickets" MessageSelector="ToChatMessage"
///              OnSend="@(EventCallback.Factory.Create&lt;ChatSendRequest&gt;(this, HandleSend))"&gt;
///     &lt;ItemTemplate Context="ctx"&gt;
///         &lt;TmChatBubble Variant="ChatBubbleVariant.Pending"&gt;@ctx.Message.Message.Text&lt;/TmChatBubble&gt;
///     &lt;/ItemTemplate&gt;
/// &lt;/TmTypedChat&gt;
/// </code>
/// </example>
public partial class TmTypedChat<TItem> : ComponentBase
{
    private readonly Dictionary<object, ChatMessage> _selectorCache =
        new(System.Collections.Generic.ReferenceEqualityComparer.Instance);

    private Func<TItem, ChatMessage>? _cachedSelector;
    private IReadOnlyList<ChatMessage> _mappedMessages = [];
    private TmChat? _chat;

    /// <summary>
    /// The host's domain sequence the conversation renders. The order of <see cref="Items"/> is
    /// the order of the mapped <see cref="ChatMessage"/> sequence handed to the inner
    /// <see cref="TmChat"/>.
    /// </summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<TItem> Items { get; set; } = [];

    /// <summary>
    /// Maps one <typeparamref name="TItem"/> to the <see cref="ChatMessage"/> the inner
    /// <see cref="TmChat"/> renders. Called from <see cref="OnParametersSet"/> — once per NEW
    /// item reference (unchanged references reuse the previously mapped
    /// <see cref="ChatMessage"/> instance, so Blazor's keyed row matching keeps the row's
    /// component instances alive); value-type items are re-mapped on every parameter pass.
    /// <para>
    /// The cache saves MAPPING WORK only — it is not a memo of the conversation. The selector
    /// must depend on nothing but the item handed to it; REPLACING an item (a different
    /// reference for the same row) or REPLACING the selector forces the affected rows to be
    /// re-mapped, so state captured by the selector cannot be smuggled past a re-map.
    /// </para>
    /// <para>
    /// When the returned message has a null <see cref="ChatMessage.Payload"/>, the item is
    /// stored as the payload — so even a plain (non-generic) <c>TmChat</c> template can read the
    /// item back with <c>PayloadAs&lt;TItem&gt;()</c>. A payload the selector sets itself wins
    /// and is left untouched.
    /// </para>
    /// </summary>
    [Parameter, EditorRequired]
    public Func<TItem, ChatMessage> MessageSelector { get; set; } = null!;

    /// <summary>
    /// Replaces the built-in message row with host markup that receives a
    /// <see cref="ChatItemContext{TItem}"/> — the item plus the mapped message's context. Null
    /// renders the built-in rows for every item. The wrapper element carrying the
    /// <c>data-message-id</c> anchor is rendered by <see cref="TmChat"/> around the template
    /// output either way.
    /// </summary>
    [Parameter]
    public RenderFragment<ChatItemContext<TItem>>? ItemTemplate { get; set; }

    /// <summary>
    /// Restricts <see cref="ItemTemplate"/> to the items the typed predicate accepts — the same
    /// precedence <c>TmChat.UseTemplateFor</c> documents: null means the template renders for
    /// EVERY item, otherwise only for the items where it returns <see langword="true"/>. The
    /// predicate receives the item recovered from the mapped message's payload (exact type).
    /// A message whose mapped payload is NOT a <typeparamref name="TItem"/> — the selector
    /// supplied its own payload — never renders the typed template: it renders the built-in row,
    /// with or without a predicate.
    /// </summary>
    [Parameter]
    public Func<TItem, bool>? UseTemplateFor { get; set; }

    /// <summary>
    /// Forwarded verbatim (splatted) onto the inner <see cref="TmChat"/> together with every
    /// other unmatched attribute — this is how <c>OnSend</c>, <c>ComposerTemplate</c>,
    /// <c>AutoAttachComposerInput</c>, <c>RenderWindowSize</c> and the rest of the 60+
    /// <c>TmChat</c> parameters reach the chat without this component duplicating them. Because
    /// they are splatted, there is no IntelliSense and no compile-time checking for them; and
    /// because <c>Messages</c>, <c>MessageTemplate</c> and <c>UseTemplateFor</c> are owned by
    /// <c>TmTypedChat</c>, passing any of them here throws an <see cref="ArgumentException"/>
    /// with a message naming the conflicting attribute.
    /// <para>
    /// <b>EventCallback pitfall:</b> for an attribute that matches no parameter the Razor
    /// compiler has no target type, so a bare method group (<c>OnSend="HandleSend"</c>)
    /// compiles to the handler's NAME as a plain string. <c>TmTypedChat</c> checks every
    /// forwarded value against the inner <see cref="TmChat"/> parameter it names and throws a
    /// descriptive <see cref="ArgumentException"/> from <see cref="OnParametersSet"/> when the
    /// type cannot reach it — for a string the message shows the factory form to write instead:
    /// <c>OnSend="@(EventCallback.Factory.Create&lt;ChatSendRequest&gt;(this,
    /// HandleSend))"</c>. Values whose type already matches (bools, strings, ints, enums,
    /// render fragments) pass through unchanged.
    /// </para>
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The inner <see cref="TmChat"/> instance this component renders. Null before the first render.</summary>
    public TmChat? Chat => _chat;

    // ── Mapping (runs in OnParametersSet, before any render) ─────────────────────

    /// <summary>
    /// Validates the splat conflicts and maps <see cref="Items"/> through
    /// <see cref="MessageSelector"/> into the message sequence the inner <see cref="TmChat"/> renders.
    /// </summary>
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        ValidateForwardedAttributes();

        if (MessageSelector is null)
        {
            // EditorRequired keeps hosts honest, but an incomplete component must still fail soft.
            _mappedMessages = [];
            return;
        }

        // A changed selector delegate invalidates the cache — the previously mapped messages
        // would otherwise keep stale content even though the host asked for a different
        // mapping. The comparison is Equals (not ReferenceEquals): the Razor compiler builds a
        // FRESH delegate for a method-group selector on every render, and Delegate.Equals
        // treats a fresh binding of the same instance/static method as equal, so a host that
        // writes MessageSelector="ToChatMessage" does not pay a full re-map every render. Two
        // distinct lambdas (different generated methods) are never equal — correctness first:
        // a re-created lambda simply pays the re-map.
        if (!Equals(MessageSelector, _cachedSelector))
        {
            _selectorCache.Clear();
            _cachedSelector = MessageSelector;
        }

        if (typeof(TItem).IsValueType)
        {
            // Value types have no identity to cache by — map (and box) them every pass.
            _mappedMessages = Items.Select(MapItem).ToArray();
            return;
        }

        var mapped = new ChatMessage[Items.Count];
        var seen = new HashSet<object>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i]!;
            if (item is null)
            {
                // A null item has no identity to cache by — map it fresh on every pass and
                // never put it into the cache (a null key would also fake a "seen" item below).
                mapped[i] = MapItem(default!);
                continue;
            }

            if (!_selectorCache.TryGetValue(item, out var message))
            {
                message = MapItem((TItem)item);
                _selectorCache[item] = message;
            }

            mapped[i] = message;
            seen.Add(item);
        }

        // Evict entries whose items disappeared so a long-lived chat does not accumulate them.
        if (_selectorCache.Count > seen.Count)
        {
            foreach (var stale in _selectorCache.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                _selectorCache.Remove(stale);
            }
        }

        _mappedMessages = mapped;
    }

    /// <summary>
    /// Maps one item: the selector's message, with the item stored as its
    /// <see cref="ChatMessage.Payload"/> when the selector left the payload null.
    /// </summary>
    private ChatMessage MapItem(TItem item)
    {
        var message = MessageSelector(item);
        return message.Payload is null ? message with { Payload = item } : message;
    }

    /// <summary>
    /// The <see cref="ParameterAttribute"/> types of the inner <see cref="TmChat"/>, keyed
    /// case-insensitively by property name — the reference the splat validation checks forwarded
    /// values against. Built once per closed generic type.
    /// </summary>
    private static readonly Dictionary<string, Type> ForwardedTmChatParameterTypes =
        typeof(TmChat).GetProperties()
            .Where(p => p.IsDefined(typeof(ParameterAttribute), inherit: false))
            .ToDictionary(p => p.Name, p => p.PropertyType, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the type expects a delegate — an <see cref="EventCallback"/>, an
    /// <see cref="EventCallback{TResult}"/> or any other <see cref="MulticastDelegate"/>.</summary>
    private static bool IsEventCallbackType(Type type)
        => type == typeof(EventCallback)
            || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EventCallback<>))
            || typeof(MulticastDelegate).IsAssignableFrom(type);

    /// <summary>Renders a parameter type the way a host reads it (e.g. <c>EventCallback&lt;ChatSendRequest&gt;</c>).</summary>
    private static string DescribeType(Type type)
        => type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(DescribeType))}>"
            : type.Name;

    /// <summary>
    /// Rejects the attributes <c>TmTypedChat</c> owns — the host must not route them through the
    /// splat, where they would silently fight the values this component derives from
    /// <see cref="Items"/>/<see cref="MessageSelector"/> — and a forwarded value whose type
    /// cannot reach the inner <see cref="TmChat"/> parameter it names (the classic case: a bare
    /// method group compiled to the handler's NAME as a string, which would surface as an
    /// <see cref="InvalidCastException"/> deep inside TmChat's render).
    /// </summary>
    private void ValidateForwardedAttributes()
    {
        if (AdditionalAttributes is null) return;

        foreach (var key in AdditionalAttributes.Keys)
        {
            if (key.Equals("Messages", StringComparison.OrdinalIgnoreCase)
                || key.Equals("MessageTemplate", StringComparison.OrdinalIgnoreCase)
                || key.Equals("UseTemplateFor", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"The '{key}' attribute is owned by TmTypedChat and cannot be forwarded to the inner TmChat: "
                    + "TmTypedChat derives the message sequence from Items + MessageSelector and translates "
                    + "ItemTemplate/UseTemplateFor itself. Remove it from the splat — set Items, "
                    + "MessageSelector, ItemTemplate or UseTemplateFor directly instead.",
                    nameof(AdditionalAttributes));
            }
        }

        foreach (var (key, value) in AdditionalAttributes)
        {
            // Not a TmChat parameter (a DOM attribute or the wrapper's own slot) — not ours to
            // judge; it is forwarded verbatim.
            if (!ForwardedTmChatParameterTypes.TryGetValue(key, out var expected)) continue;
            if (expected.IsInstanceOfType(value)) continue;

            if (value is string && IsEventCallbackType(expected))
            {
                // The EventCallback pitfall: for an attribute that matches no parameter of
                // TmTypedChat the Razor compiler has no target type, so a bare method group
                // (OnSend="HandleSend") compiles to the handler's NAME as a plain string. That
                // string can never become the delegate TmChat expects — without this guard the
                // chat would throw an InvalidCastException while rendering.
                var handlerName = $"Handle{(key.StartsWith("On", StringComparison.Ordinal) ? key[2..] : key)}";
                var typeArgument = expected.IsGenericType
                    && expected.GetGenericTypeDefinition() == typeof(EventCallback<>)
                        ? DescribeType(expected.GetGenericArguments()[0])
                        : null;
                var factoryForm = typeArgument is null
                    ? $"{key}=\"@(EventCallback.Factory.Create(this, {handlerName}))\""
                    : $"{key}=\"@(EventCallback.Factory.Create<{typeArgument}>(this, {handlerName}))\"";
                throw new ArgumentException(
                    $"The '{key}' attribute was forwarded to the inner TmChat as a STRING — the signature of a bare "
                    + $"method group ({key}=\"{handlerName}\") passed through the splat: TmTypedChat has no parameter "
                    + "of that name, so the Razor compiler has no target type and compiles the handler's NAME as a "
                    + $"plain string instead of the delegate, while TmChat's '{key}' parameter expects "
                    + $"'{DescribeType(expected)}' and would throw an InvalidCastException while rendering. Forward "
                    + $"the handler with an explicitly-typed factory instead: {factoryForm}",
                    nameof(AdditionalAttributes));
            }

            var actual = value?.GetType() is { } actualType ? DescribeType(actualType) : "null";
            throw new ArgumentException(
                $"The '{key}' attribute forwarded to the inner TmChat is a '{actual}' but "
                + $"TmChat's '{key}' parameter expects '{DescribeType(expected)}'. The splat is not type-checked by "
                + "the compiler — pass the value in TmChat's parameter type.",
                nameof(AdditionalAttributes));
        }
    }

    // ── Template translation ─────────────────────────────────────────────────────

    /// <summary>
    /// The inner <see cref="TmChat"/>'s MessageTemplate: null without an <see cref="ItemTemplate"/>
    /// (built-in rows for everything), otherwise a fragment that recovers the item from the mapped
    /// message's payload (exact type — no second lookup dictionary) and hands it to the typed
    /// template together with TmChat's own message context.
    /// </summary>
    private RenderFragment<ChatMessageContext>? InnerMessageTemplate
        => ItemTemplate is null ? null : RenderTypedItem;

    private RenderFragment RenderTypedItem(ChatMessageContext context)
        => ItemTemplate!(new ChatItemContext<TItem>(context.PayloadAs<TItem>()!, context));

    /// <summary>
    /// The inner <see cref="TmChat"/>'s UseTemplateFor: whenever an <see cref="ItemTemplate"/> is
    /// set, the inner predicate is ALWAYS present — it accepts exactly the messages whose mapped
    /// payload is a <typeparamref name="TItem"/> (the wrapper stored the item) and additionally
    /// narrows them through the typed <see cref="UseTemplateFor"/> when one exists. A message the
    /// selector gave its OWN payload to (something that is not a <typeparamref name="TItem"/>)
    /// does not render the typed template — it renders the BUILT-IN row, exactly as it would
    /// without an <see cref="ItemTemplate"/>. (With the predicate omitted, TmChat would render
    /// the template for EVERY message and a selector-owned payload would reach the typed template
    /// as <c>default(TItem)</c>.)
    /// </summary>
    private Func<ChatMessage, bool>? InnerUseTemplateFor
        => ItemTemplate is not null ? MatchesTypedPredicate : null;

    private bool MatchesTypedPredicate(ChatMessage message)
        => message.Payload is TItem item && (UseTemplateFor?.Invoke(item) ?? true);

    // ── Delegation to the inner TmChat ───────────────────────────────────────────
    // Every member here is a no-op (or false) before the first render — the inner chat does not
    // exist yet and nothing must throw. TmTypedChat owns no JS and no timers, so it is NOT
    // IAsyncDisposable: the inner TmChat disposes itself.

    /// <summary>Scrolls the conversation to its end. No-op before the first render.</summary>
    public ValueTask ScrollToBottomAsync()
        => Chat?.ScrollToBottomAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Scrolls to the row of <paramref name="item"/> — resolved to the mapped message id through
    /// the <see cref="_selectorCache"/> first (the id the rows were rendered with; the host's
    /// selector does not re-run) and through <see cref="MessageSelector"/> only for an item that
    /// is not currently mapped. Returns whether the row was found and scrolled to;
    /// <see langword="false"/> before the first render.
    /// </summary>
    public ValueTask<bool> ScrollToItemAsync(TItem item)
    {
        if (Chat is null || MessageSelector is null) return ValueTask.FromResult(false);

        var id = _selectorCache.TryGetValue(item!, out var mapped)
            ? mapped.Id
            : MessageSelector(item).Id;
        return Chat.ScrollToMessageAsync(id);
    }

    /// <summary>
    /// Scrolls to the row of the message with the given id. Returns whether the row was found and
    /// scrolled to; <see langword="false"/> before the first render.
    /// </summary>
    public ValueTask<bool> ScrollToMessageAsync(string id)
        => Chat is null ? ValueTask.FromResult(false) : Chat.ScrollToMessageAsync(id);

    /// <summary>
    /// Moves keyboard focus to the row of <paramref name="item"/> — resolved to the mapped
    /// message id through the <see cref="_selectorCache"/> first (the id the rows were rendered
    /// with; the host's selector does not re-run) and through <see cref="MessageSelector"/> only
    /// for an item that is not currently mapped. Returns whether the row was found and focused;
    /// <see langword="false"/> before the first render.
    /// </summary>
    public ValueTask<bool> FocusItemAsync(TItem item)
    {
        if (Chat is null || MessageSelector is null) return ValueTask.FromResult(false);

        var id = _selectorCache.TryGetValue(item!, out var mapped)
            ? mapped.Id
            : MessageSelector(item).Id;
        return Chat.FocusMessageAsync(id);
    }

    /// <summary>
    /// Announces host text through the chat's single polite live region (with TmChat's trailing
    /// throttle). No-op before the first render.
    /// </summary>
    public Task AnnounceAsync(string text)
        => Chat?.AnnounceAsync(text) ?? Task.CompletedTask;

    /// <summary>Moves keyboard focus to the composer. No-op before the first render.</summary>
    public ValueTask FocusComposerAsync()
        => Chat?.FocusComposerAsync() ?? ValueTask.CompletedTask;

    /// <summary>
    /// Whether the composer (built-in or custom, driven through the composer context) holds an
    /// unsaved draft. <see langword="false"/> before the first render.
    /// </summary>
    public bool HasUnsavedDraft => Chat?.HasUnsavedDraft ?? false;

    /// <summary>Discards the composer draft. No-op before the first render.</summary>
    public Task DiscardDraftAsync()
        => Chat?.DiscardDraftAsync() ?? Task.CompletedTask;
}
