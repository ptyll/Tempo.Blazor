using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Live view over the <c>TmChat</c> composer, handed to the
/// <c>TmChat.ComposerTemplate</c> render fragment. A host that fully replaces the built-in
/// composer (subscribe gate, PPV field, owner note + hint, …) keeps the component's send path,
/// submit-state and draft guard working by driving its own input through this context: bind the
/// input to <see cref="Text"/>, push each keystroke with <see cref="SetTextAsync"/> and send with
/// <see cref="SendAsync"/>.
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
/// TmChat's <c>OnSendMessage</c> / <c>OnSend</c> parameters), the submitting flag
/// flips for the duration and the cleared composer is reported through the draft guard
/// (<c>OnDraftChanged</c>). Nothing is sent when the composer is disabled, empty or already
/// submitting — check <see cref="CanSend"/> to mirror that on a custom send button.
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
    /// the text is trimmed and cleared, TmChat's <c>OnSendMessage</c> and <c>OnSend</c> fire, the
    /// submit state flips (<see cref="IsSubmitting"/>) for the duration and the cleared composer is
    /// reported to the draft guard. A no-op when <see cref="CanSend"/> is <see langword="false"/>.
    /// The template re-renders with the cleared <see cref="Text"/>, so a controlled input clears
    /// itself after an awaited send. Safe to call from any thread — the send is dispatched onto
    /// the component's renderer synchronisation context.
    /// </summary>
    public Task SendAsync() => _owner.SendComposerViaDispatcherAsync();

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
    /// Moves focus to the built-in composer textarea. With a custom composer the built-in textarea
    /// is not rendered — focus the host's own input there instead (the template markup owns it).
    /// </summary>
    public ValueTask FocusAsync() => _owner.FocusComposerInputAsync();

    /// <summary>
    /// DOM id TmChat assigns to the built-in composer textarea, usable for a custom label
    /// (<c>aria-describedby</c>, <c>&lt;label for&gt;</c>) or <c>aria-controls</c>. The attribute
    /// is present in the DOM only while the built-in textarea actually renders — with a custom
    /// composer (or <c>ShowComposer=false</c>) there is no built-in textarea to point at.
    /// </summary>
    public string InputId => _owner.ComposerInputId;
}
