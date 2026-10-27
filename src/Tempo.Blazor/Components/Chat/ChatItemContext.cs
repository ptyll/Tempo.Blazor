using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Typed context handed to <c>TmTypedChat&lt;TItem&gt;.ItemTemplate</c> for every item it renders.
/// It composes the host's <typeparamref name="TItem"/> with the <see cref="ChatMessageContext"/>
/// TmChat already builds around the mapped message — the item itself plus everything "around" it
/// (the <see cref="ChatMessage"/>, own/thread/group position and the built-in row via
/// <see cref="DefaultContent"/>).
/// <para>
/// The item is recovered from the mapped message's <see cref="ChatMessage.Payload"/> (exact type),
/// so it is the same instance the host passed in <c>Items</c>. The typed template renders ONLY for
/// messages whose payload IS a <typeparamref name="TItem"/> — a message whose
/// <c>MessageSelector</c> supplied its own payload renders the built-in row instead and never
/// reaches this context, so <see cref="Item"/> is always a real item.
/// </para>
/// </summary>
/// <typeparam name="TItem">The host's domain type the conversation is built from.</typeparam>
/// <example>
/// <code>
/// &lt;TmTypedChat Items="_events" MessageSelector="@(e =&gt; Map(e))"&gt;
///     &lt;ItemTemplate Context="ctx"&gt;
///         &lt;TmChatBubble Variant="ChatBubbleVariant.Pending" ActorLabel="@ctx.Item!.Actor"&gt;
///             &lt;Badge&gt;&lt;span class="ticket-badge"&gt;#@ctx.Item.Id&lt;/span&gt;&lt;/Badge&gt;
///             &lt;ChildContent&gt;&lt;span&gt;@ctx.Message.Message.Text&lt;/span&gt;&lt;/ChildContent&gt;
///         &lt;/TmChatBubble&gt;
///     &lt;/ItemTemplate&gt;
/// &lt;/TmTypedChat&gt;
/// </code>
/// </example>
public sealed class ChatItemContext<TItem>
{
    /// <summary>
    /// Creates a context. Instances are created by <c>TmTypedChat&lt;TItem&gt;</c> only; hosts
    /// normally receive them from the <c>ItemTemplate</c> parameter rather than building their own.
    /// </summary>
    /// <param name="item">The host's domain item being rendered.</param>
    /// <param name="message">The message context TmChat built around the mapped message.</param>
    internal ChatItemContext(TItem item, ChatMessageContext message)
    {
        Item = item;
        Message = message;
    }

    /// <summary>
    /// The host's domain item this row renders — recovered from the mapped message's
    /// <see cref="ChatMessage.Payload"/> (exact type), the same instance the host passed in
    /// <c>Items</c>. Never null: the typed template renders only for messages whose payload IS a
    /// <typeparamref name="TItem"/> (a selector-owned payload renders the built-in row instead).
    /// </summary>
    public TItem Item { get; }

    /// <summary>
    /// The message context of the mapped <see cref="ChatMessage"/>: the message itself
    /// (<see cref="ChatMessageContext.Message"/>), <see cref="ChatMessageContext.IsOwn"/>,
    /// <see cref="ChatMessageContext.InThread"/>, <see cref="ChatMessageContext.Index"/>,
    /// <see cref="ChatMessageContext.IsFirstInGroup"/> and
    /// <see cref="ChatMessageContext.DefaultContent"/>.
    /// </summary>
    public ChatMessageContext Message { get; }

    /// <summary>Whether the mapped message was authored by <c>TmChat.CurrentUser</c>.</summary>
    public bool IsOwn => Message.IsOwn;

    /// <summary>Whether the mapped message renders inside the thread panel.</summary>
    public bool InThread => Message.InThread;

    /// <summary>
    /// Zero-based position of the mapped message within the sequence it renders in — the root
    /// conversation list, or the thread sequence when <see cref="InThread"/> is
    /// <see langword="true"/>.
    /// </summary>
    public int Index => Message.Index;

    /// <summary>
    /// Whether the previous rendered message had a different author or a different
    /// <see cref="ChatMessage.Kind"/>. The first message of a group can show its full metadata.
    /// </summary>
    public bool IsFirstInGroup => Message.IsFirstInGroup;

    /// <summary>
    /// Render fragment with the built-in message row exactly as it renders without a template —
    /// returning it from the template keeps the default look. A shortcut for
    /// <c>Message.DefaultContent</c>.
    /// </summary>
    public RenderFragment DefaultContent => Message.DefaultContent;
}
