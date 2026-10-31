namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One turn of an AI conversation: the user prompt or the streamed assistant answer, built from ordered
/// <see cref="AiMessagePart"/>s. The record is mutable because it is grown by the delta stream — feed it
/// through <c>AiMessageStreamApplier</c> instead of mutating the parts by hand, so the state transitions
/// and the idempotence rules stay in one place.
/// </summary>
public sealed class AiMessage
{
    /// <summary>Creates an empty assistant message with a fresh id.</summary>
    public AiMessage()
    {
    }

    /// <summary>Creates a message of the given role carrying one text part.</summary>
    /// <param name="role">Who owns the turn.</param>
    /// <param name="markdown">Initial markdown text.</param>
    public AiMessage(AiMessageRole role, string? markdown)
    {
        Role = role;
        if (!string.IsNullOrEmpty(markdown))
        {
            Parts.Add(new AiTextPart(markdown));
        }
    }

    /// <summary>Stable message identifier; the streaming API addresses messages by it.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Who produced the turn.</summary>
    public AiMessageRole Role { get; set; } = AiMessageRole.Assistant;

    /// <summary>Ordered parts of the message.</summary>
    public IList<AiMessagePart> Parts { get; } = new List<AiMessagePart>();

    /// <summary>UTC creation time.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Current lifecycle state.</summary>
    public AiMessageStatus Status { get; set; } = AiMessageStatus.Pending;

    /// <summary>Failure reason when <see cref="Status"/> is <see cref="AiMessageStatus.Failed"/>.</summary>
    public string? Error { get; set; }

    /// <summary>Model identifier that produced the answer, when the host reports one.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// Sources cited by the answer — the contents of the trailing <see cref="AiSourcesPart"/>, or an empty
    /// list while nothing has been cited.
    /// </summary>
    public IReadOnlyList<AiSource> Sources =>
        Parts.OfType<AiSourcesPart>().LastOrDefault()?.Sources ?? (IReadOnlyList<AiSource>)Array.Empty<AiSource>();

    /// <summary>Finds a cited source by id, so a <c>cite:</c> chip can resolve its preview.</summary>
    /// <param name="sourceId">The id carried by the citation link.</param>
    /// <returns>The source, or <see langword="null"/> when the answer does not cite it.</returns>
    public AiSource? FindSource(string? sourceId)
        => string.IsNullOrEmpty(sourceId)
            ? null
            : Parts.OfType<AiSourcesPart>()
                .SelectMany(part => part.Sources)
                .LastOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal));

    /// <summary>All text of the message concatenated in part order (markdown, not plain text).</summary>
    public string Text => string.Concat(Parts.OfType<AiTextPart>().Select(part => part.Markdown));
}
