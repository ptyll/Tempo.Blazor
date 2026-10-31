using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Context handed to the <c>TmChat.MessageTemplate</c> render fragment for every message it
/// renders. The template receives everything TmChat knows "around" the message — the message
/// itself, whether the current user sent it, whether it is rendered inside the thread panel, its
/// position and group position — plus a <see cref="DefaultContent"/> fragment with the built-in
/// message row, so a host can take over only special message kinds and fall back to the default
/// look for ordinary messages.
/// <para>
/// TmChat never interprets <see cref="ChatMessage.Kind"/>, <see cref="ChatMessage.Metadata"/> or
/// <see cref="ChatMessage.Payload"/>; they exist purely for the template.
/// </para>
/// </summary>
/// <example>
/// <code>
/// &lt;TmChat Messages="_messages" CurrentUser="_user"&gt;
///     &lt;MessageTemplate Context="context"&gt;
///         @if (context.Message.Kind == "ai-draft")
///         {
///             &lt;MyDraftBubble Draft="@(context.PayloadAs&lt;MyDraftDto&gt;()!)" /&gt;
///         }
///         else
///         {
///             @context.DefaultContent
///         }
///     &lt;/MessageTemplate&gt;
/// &lt;/TmChat&gt;
/// </code>
/// </example>
public sealed class ChatMessageContext
{
    /// <summary>
    /// Creates a context. Instances are created by <c>TmChat</c> only; hosts normally receive
    /// them from the <c>MessageTemplate</c> parameter rather than building their own.
    /// </summary>
    /// <param name="message">The message being rendered.</param>
    /// <param name="isOwn">
    /// Whether the message was authored by <c>TmChat.CurrentUser</c>.
    /// </param>
    /// <param name="inThread">Whether the message is rendered inside the thread panel.</param>
    /// <param name="index">
    /// Zero-based position of the message within the sequence it is rendered in — the root
    /// conversation list, or the thread sequence (root first, then replies) when
    /// <see cref="InThread"/> is <see langword="true"/>.
    /// </param>
    /// <param name="isFirstInGroup">
    /// Whether the previous rendered message had a different author or a different
    /// <see cref="ChatMessage.Kind"/> — the first message of a group shows its full metadata.
    /// </param>
    /// <param name="defaultContent">
    /// Render fragment with the built-in message row, available for fallback rendering.
    /// </param>
    internal ChatMessageContext(
        ChatMessage message,
        bool isOwn,
        bool inThread,
        int index,
        bool isFirstInGroup,
        RenderFragment defaultContent)
    {
        Message = message;
        IsOwn = isOwn;
        InThread = inThread;
        Index = index;
        IsFirstInGroup = isFirstInGroup;
        DefaultContent = defaultContent;
    }

    /// <summary>The message being rendered, including its host-owned Kind/Metadata/Payload.</summary>
    public ChatMessage Message { get; }

    /// <summary>
    /// Whether the message was authored by <c>TmChat.CurrentUser</c> (an "own" message).
    /// </summary>
    public bool IsOwn { get; }

    /// <summary>
    /// Whether the message is rendered inside the thread panel (the thread root or one of its
    /// replies) rather than in the root conversation list.
    /// </summary>
    public bool InThread { get; }

    /// <summary>
    /// Zero-based position of the message within the sequence it is rendered in — the root
    /// conversation list, or the thread sequence when <see cref="InThread"/> is
    /// <see langword="true"/>.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Whether the previous rendered message had a different author or a different
    /// <see cref="ChatMessage.Kind"/>. The first message of a group can show its full metadata;
    /// continuation messages typically hide repeated author names and timestamps.
    /// </summary>
    public bool IsFirstInGroup { get; }

    /// <summary>
    /// Render fragment with the built-in message row exactly as it renders without a template —
    /// returning it from the template keeps the default look for messages the host does not take
    /// over. The fragment does not include the message wrapper (the element carrying the
    /// <c>data-message-id</c> anchor); TmChat renders that around template output either way.
    /// </summary>
    public RenderFragment DefaultContent { get; }

    /// <summary>
    /// Returns the message's <see cref="ChatMessage.Payload"/> typed as <typeparamref name="T"/>,
    /// or <c>default</c> when the payload is null or of another type. No conversion is attempted —
    /// cast to the exact type the host stored on the message.
    /// </summary>
    /// <example>
    /// <code>
    /// var draft = context.PayloadAs&lt;InboxMessageDto&gt;();
    /// if (draft is not null) { /* render the draft bubble */ }
    /// </code>
    /// </example>
    public T? PayloadAs<T>()
        => Message.Payload is T typed ? typed : default;

    /// <summary>
    /// Reads an entry from the message's <see cref="ChatMessage.Metadata"/> cast to
    /// <typeparamref name="T"/>, or <paramref name="fallback"/> when the key is missing, the value
    /// is null or of another type. No conversion is attempted — store and read the same type.
    /// </summary>
    public T? GetMetadata<T>(string key, T? fallback = default)
    {
        if (!Message.Metadata.TryGetValue(key, out var value) || value is null)
        {
            return fallback;
        }

        return value is T typed ? typed : fallback;
    }
}
