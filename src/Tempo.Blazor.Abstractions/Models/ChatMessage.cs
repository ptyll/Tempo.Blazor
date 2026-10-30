namespace Tempo.Blazor.Models;

/// <summary>
/// Represents a single message in a chat conversation.
/// </summary>
public sealed record ChatMessage
{
    /// <summary>Unique identifier.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Message text content.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Author of the message.</summary>
    public ChatUser? Author { get; init; }

    /// <summary>Timestamp when the message was sent.</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Message direction / type.</summary>
    public ChatMessageType Type { get; init; } = ChatMessageType.Incoming;

    /// <summary>Optional attachments.</summary>
    public IReadOnlyList<ChatAttachment> Attachments { get; init; } = [];

    /// <summary>Whether the message has been read by the recipient.</summary>
    public bool IsRead { get; init; }

    /// <summary>Whether the message is currently being sent.</summary>
    public bool IsSending { get; init; }

    /// <summary>Whether the message failed to send.</summary>
    public bool IsError { get; init; }

    /// <summary>Optional error text when sending failed.</summary>
    public string? ErrorMessage { get; init; }

    // ── K6: threads, edit/delete, reactions, receipts (all additive) ──

    /// <summary>Id of the specific message this one replies to, when it is a reply.</summary>
    public string? ReplyToId { get; init; }

    /// <summary>Id of the root message of the thread this message belongs to; the root's own
    /// <see cref="Id"/> for a thread starter, or the root id for its replies. Null for standalone messages.</summary>
    public string? ThreadRootId { get; init; }

    /// <summary>Number of replies in this message's thread (meaningful on a thread root).</summary>
    public int ReplyCount { get; init; }

    /// <summary>Emoji reactions on this message, grouped by emoji.</summary>
    public IReadOnlyList<ChatReaction> Reactions { get; init; } = [];

    /// <summary>Per-user read receipts for this message.</summary>
    public IReadOnlyList<ChatReadReceipt> ReadBy { get; init; } = [];

    /// <summary>Timestamp of the last edit, or null when the message was never edited.</summary>
    public DateTimeOffset? EditedAt { get; init; }

    /// <summary>Whether the message has been deleted (rendered as a tombstone).</summary>
    public bool IsDeleted { get; init; }

    // ── Custom templates: host-owned message contract (all additive) ──

    /// <summary>
    /// Free-form kind of the message, e.g. <c>"ai-draft"</c>, <c>"queued"</c>, <c>"tip"</c>.
    /// <para>
    /// TmChat itself does NOT interpret this value — it only carries it into the message template
    /// (via <c>ChatMessageContext.Message</c>) and mirrors it on the message wrapper's
    /// <c>data-kind</c> attribute. Hosts use it to route a
    /// <c>RenderFragment&lt;ChatMessageContext&gt; MessageTemplate</c> (typically paired with
    /// <c>UseTemplateFor</c>) or their own grouping logic. Null for plain messages.
    /// </para>
    /// </summary>
    public string? Kind { get; init; }

    private readonly IReadOnlyDictionary<string, object?> _metadata = EmptyMetadata;

    /// <summary>
    /// Host-defined metadata entries for the message. TmChat does NOT interpret them — they are
    /// handed to the message template untouched (read them via
    /// <c>ChatMessageContext.GetMetadata&lt;T&gt;(key, fallback)</c>). Defaults to an empty
    /// read-only dictionary, never null. TmChat never serializes it.
    /// <para>
    /// Values must be in-process CLR objects. After a host-side JSON
    /// round-trip, the values become
    /// <see cref="System.Text.Json.JsonElement"/> instances instead of the original CLR types.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<string, object?> Metadata { get => _metadata; init => _metadata = value ?? EmptyMetadata; }

    /// <summary>
    /// The host's original DTO this message was mapped from. TmChat does NOT interpret it — the
    /// message template reads it back type-safely via <c>ChatMessageContext.PayloadAs&lt;T&gt;()</c>.
    /// The payload is never serialized into any component event or log output.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public object? Payload { get; init; }

    /// <summary>True when the message carries an edit timestamp.</summary>
    public bool IsEdited => EditedAt.HasValue;

    /// <summary>True when the message is a reply within a thread.</summary>
    public bool IsReply => !string.IsNullOrEmpty(ReplyToId) || !string.IsNullOrEmpty(ThreadRootId);

    /// <summary>Returns true when <paramref name="userId"/> has read this message.</summary>
    public bool IsReadByUser(string userId)
        => IsRead || ReadBy.Any(r => string.Equals(r.User?.Id, userId, StringComparison.Ordinal));

    /// <summary>Shared empty metadata instance (read-only, zero entries) used as the default.</summary>
    private static IReadOnlyDictionary<string, object?> EmptyMetadata { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());

    public ChatMessage() { }

    public ChatMessage(string id, string text, ChatUser? author = null, ChatMessageType type = ChatMessageType.Incoming, DateTimeOffset? timestamp = null)
    {
        Id = id;
        Text = text;
        Author = author;
        Type = type;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Direction / type of a chat message.
/// </summary>
public enum ChatMessageType
{
    /// <summary>Message received from another user.</summary>
    Incoming,

    /// <summary>Message sent by the current user.</summary>
    Outgoing,

    /// <summary>System notification (e.g. "User joined", "User left").</summary>
    System
}
