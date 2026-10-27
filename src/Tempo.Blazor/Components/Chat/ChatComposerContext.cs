using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Live view over the <c>TmChat</c> composer, handed to the
/// <c>TmChat.ComposerTemplate</c> render fragment. A host that fully replaces the built-in
/// composer (subscribe gate, PPV field, owner note + hint, …) keeps the component's send path,
/// submit-state and draft guard working by driving its own input through this context: bind the
/// input to <see cref="Text"/>, push each keystroke with <see cref="SetTextAsync"/> and send with
/// <see cref="SendAsync"/>. Register the input element with <see cref="AttachAsync"/> to give it
/// the same Enter/Shift+Enter + IME keydown behavior as the built-in composer and to make it the
/// refocus target of <c>RefocusAfterSend</c>.
/// <para>
/// The context is a LIVE view over the owning <c>TmChat</c>, not a snapshot — every member reads
/// the component's current state, so a context instance captured from an earlier render (e.g.
/// inside an event handler) stays usable after the component re-renders.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Text"/> is the source of truth for the composer text: the template is re-rendered
/// from it, so a controlled input bound to <c>composer.Text</c> clears itself after a send — no
/// host-side field and no manual reset is needed.
/// </para>
/// <para>
/// <see cref="SendAsync"/> follows exactly the same path as the built-in send button: the text is
/// trimmed, <c>TmChat.OnSendMessage</c> and <see cref="ChatSendRequest"/> events are raised (via
/// TmChat's <c>OnSendMessage</c> / <c>OnSend</c> parameters) and the submitting flag flips for
/// the duration. The composer clears only AFTER both handlers completed — a host handler that
/// throws keeps the text (the draft is not lost) and the exception propagates; a completed send
/// reports the cleared slot through the draft guard (<c>OnDraftChanged</c>) and, with
/// <c>RefocusAfterSend</c>, focuses the input registered via <see cref="AttachAsync"/> back.
/// Nothing is sent when the composer is disabled, empty or already submitting — check
/// <see cref="CanSend"/> to mirror that on a custom send button.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// &lt;TmChat Messages="_messages" CurrentUser="_user" OnSend="@HandleSend"&gt;
///     &lt;ComposerTemplate Context="composer"&gt;
///         &lt;!-- controlled input: Text is the source of truth --&gt;
///         &lt;textarea value="@composer.Text" disabled="@composer.Disabled"
///                   @oninput="@(e =&gt; composer.SetTextAsync(e.Value?.ToString() ?? string.Empty))"&gt;&lt;/textarea&gt;
///         &lt;button disabled="@(!composer.CanSend)" @onclick="composer.SendAsync"&gt;Send&lt;/button&gt;
///     &lt;/ComposerTemplate&gt;
/// &lt;/TmChat&gt;
/// </code>
/// </example>
public sealed class ChatComposerContext
{
    private readonly TmChat _owner;

    /// <summary>
    /// Creates a context. Instances are created by <c>TmChat</c> only; hosts receive them from the
    /// <c>ComposerTemplate</c> parameter rather than building their own.
    /// </summary>
    internal ChatComposerContext(TmChat owner) => _owner = owner;

    /// <summary>
    /// The current composer text as the component sees it — the source of truth the template is
    /// rendered from: what the built-in textarea holds, or whatever the custom composer last
    /// pushed through <see cref="SetTextAsync"/>. Bind a controlled input to it; after
    /// <see cref="SendAsync"/> it is empty again (the re-render propagates the cleared text into
    /// the bound input).
    /// </summary>
    public string Text => _owner.ComposerText;

    /// <summary>
    /// Pushes the text typed in a CUSTOM composer into the component, so the draft guard
    /// (<c>HasUnsavedDraft</c> / <c>OnDraftChanged</c>'s <c>HasComposerDraft</c>) keeps covering a
    /// replaced composer exactly as it covers the built-in one. Call it from the custom input's
    /// <c>oninput</c>; the component re-renders afterwards, which re-invokes the template with a
    /// fresh context. Does not trim — trimming happens on send, like for the built-in composer.
    /// </summary>
    /// <param name="text">The current text of the custom composer input.</param>
    public Task SetTextAsync(string text) => _owner.SetComposerTextAsync(text);

    /// <summary>
    /// Sends the current <see cref="Text"/> through the SAME path as the built-in send button:
    /// the text is trimmed, TmChat's <c>OnSendMessage</c> and <c>OnSend</c> fire, the submit
    /// state flips (<see cref="IsSubmitting"/>) for the duration and — only after BOTH handlers
    /// completed without throwing — the composer clears (the re-render propagates the empty
    /// <see cref="Text"/> into the bound input, so a controlled input clears itself) and, with
    /// <c>RefocusAfterSend</c>, the registered composer input is focused again. A host handler
    /// that throws KEEPS the text in place (the draft is not lost) and the exception propagates
    /// to the caller. A no-op when <see cref="CanSend"/> is <see langword="false"/>. Safe to
    /// call from any thread — the send is dispatched onto the component's renderer
    /// synchronisation context.
    /// </summary>
    public Task SendAsync() => _owner.SendComposerViaDispatcherAsync();

    /// <summary>
    /// Registers the host's own composer input element with the component, so a
    /// <c>ComposerTemplate</c> that renders its own <c>&lt;textarea&gt;</c> (or
    /// <c>&lt;input&gt;</c>) receives the SAME keyboard behavior as the built-in composer:
    /// Enter submits (with <c>preventDefault</c>), Shift+Enter and Alt/Ctrl/Meta+Enter stay
    /// native, an IME composition session never submits, and the input auto-resizes up to the
    /// component's row cap. The behavior is delivered by the same <c>chat.js</c> module the
    /// built-in composer uses, routed to the same submit path
    /// (<c>OnSendMessage</c>/<c>OnSend</c> via <see cref="SendAsync"/>'s entry point).
    /// <para>
    /// Call it once the host's input has mounted — typically from the host's
    /// <c>OnAfterRenderAsync</c> with the captured <see cref="ElementReference"/> — and again
    /// whenever the element is replaced; call it with <c>default</c> to detach. The registered
    /// element is also the refocus target of <c>RefocusAfterSend</c>: after a successful send
    /// the component focuses it back, so the user can type the next message immediately.
    /// (With <c>TmChat.AutoAttachComposerInput</c> the template input is attached by id
    /// automatically; an explicit <c>AttachAsync</c> always wins over that handle, and the
    /// refocus target is the explicit registration — or the input auto-attached through
    /// <c>AutoAttachComposerInput</c> once the registration is released with <c>default</c>.)
    /// Safe to call from any thread.
    /// </para>
    /// </summary>
    /// <param name="element">The host composer input; <c>default</c> detaches a previous registration.</param>
    public Task AttachAsync(ElementReference element) => _owner.AttachCustomComposerAsync(element);

    /// <summary>
    /// Whether sending is currently allowed — the composer is not disabled, not already
    /// submitting and holds text that is not whitespace-only. Mirror it on a custom send button's
    /// <c>disabled</c> state; <see cref="SendAsync"/> ignores the request when it is
    /// <see langword="false"/>.
    /// </summary>
    public bool CanSend => _owner.ComposerCanSend;

    /// <summary>Whether the component's <c>Disabled</c> parameter is set (a gate such as a closed subscription typically drives it).</summary>
    public bool Disabled => _owner.Disabled;

    /// <summary>
    /// Whether a send is currently in flight (the host handler has not completed yet). While it is
    /// <see langword="true"/>, <see cref="CanSend"/> is <see langword="false"/> and further
    /// <see cref="SendAsync"/> calls are ignored.
    /// </summary>
    public bool IsSubmitting => _owner.ComposerIsSubmitting;

    /// <summary>
    /// Moves focus to the composer input — the same logic as the post-send refocus: the element
    /// registered through <see cref="AttachAsync"/> if there is one, or the input
    /// auto-attached through <c>TmChat.AutoAttachComposerInput</c>, otherwise the built-in
    /// composer textarea. An element that already holds focus is left untouched (the module's
    /// <c>focusIfNeeded</c>), so a call from a host button does not close the on-screen
    /// keyboard. A best-effort no-op while nothing is focusable (no attached element with a
    /// custom composer, <c>ShowComposer</c>=<see langword="false"/>, or the circuit is gone).
    /// </summary>
    public ValueTask FocusAsync() => _owner.FocusComposerInputAsync();

    /// <summary>
    /// DOM id TmChat assigns to the built-in composer textarea, usable for a custom label
    /// (<c>aria-describedby</c>, <c>&lt;label for&gt;</c>) or <c>aria-controls</c>. The attribute
    /// is present in the DOM only while the built-in textarea actually renders — with a custom
    /// composer (or <c>ShowComposer=false</c>) there is no built-in textarea to point at.
    /// <para>
    /// With <c>TmChat.AutoAttachComposerInput</c>, put this id on the <c>ComposerTemplate</c>
    /// input (<c>id="@composer.InputId"</c>); TmChat then attaches that element by id within its
    /// own root.
    /// </para>
    /// </summary>
    public string InputId => _owner.ComposerInputId;
}
