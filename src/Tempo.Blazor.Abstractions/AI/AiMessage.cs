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
    /// Superseded generations of the answer, oldest first. <see cref="Parts"/> always holds the LATEST
    /// generation, so the number of selectable versions is <see cref="Versions"/>'s count plus one; a
    /// Regenerate handler appends the previous parts as an <see cref="AiMessageVersion"/> before it starts
    /// the new stream.
    /// </summary>
    /// <remarks>
    /// The list is get-only but mutable, and the live parts must be CLEARED after they are archived —
    /// otherwise the new generation is appended to the old one and the last version renders both:
    /// <code>
    /// message.Versions.Add(new AiMessageVersion(message.Parts));
    /// message.Parts.Clear();                       // the host starts the new stream here
    /// message.ActiveVersionIndex = message.VersionCount - 1;
    /// </code>
    /// </remarks>
    public IList<AiMessageVersion> Versions { get; } = new List<AiMessageVersion>();

    /// <summary>
    /// Zero-based index of the version the conversation displays, or <see langword="null"/> for the newest
    /// one. The last index (<see cref="VersionCount"/> - 1) selects the live <see cref="Parts"/>; an index
    /// outside the valid range is clamped through <see cref="EffectiveVersionIndex"/>, so a host that swaps
    /// the version list and the index in two renders never throws.
    /// </summary>
    /// <remarks>
    /// <see langword="null"/> is the default on purpose: a message that just gained a new generation must
    /// keep showing THAT generation, not silently fall back to the oldest history entry.
    /// </remarks>
    public int? ActiveVersionIndex { get; set; }

    /// <summary>How many versions the message has, including the live <see cref="Parts"/> (always at least one).</summary>
    public int VersionCount => Versions.Count + 1;

    /// <summary>
    /// <see cref="ActiveVersionIndex"/> clamped into the valid range, with <see langword="null"/> resolving
    /// to the newest version. This is the index a UI renders and labels.
    /// </summary>
    public int EffectiveVersionIndex
        => ActiveVersionIndex is { } requested
            ? Math.Clamp(requested, 0, VersionCount - 1)
            : VersionCount - 1;

    /// <summary>The reader's rating of the answer, or <see langword="null"/> while it is unrated.</summary>
    public AiFeedback? Feedback { get; set; }

    /// <summary>Suggested next questions rendered as follow-up chips under the answer.</summary>
    public IList<string> FollowUps { get; } = new List<string>();

    /// <summary>
    /// Parts of the version selected by <see cref="ActiveVersionIndex"/> — the one the UI renders. The
    /// live <see cref="Parts"/> are returned for the newest version.
    /// </summary>
    public IReadOnlyList<AiMessagePart> ActiveParts
    {
        get
        {
            var index = EffectiveVersionIndex;
            return index < Versions.Count ? (IReadOnlyList<AiMessagePart>)Versions[index].Parts : (IReadOnlyList<AiMessagePart>)Parts;
        }
    }

    /// <summary>
    /// Sources cited by the ACTIVE version — the contents of its trailing <see cref="AiSourcesPart"/>, or an
    /// empty list while nothing has been cited.
    /// </summary>
    public IReadOnlyList<AiSource> Sources =>
        ActiveParts.OfType<AiSourcesPart>().LastOrDefault()?.Sources ?? (IReadOnlyList<AiSource>)Array.Empty<AiSource>();

    /// <summary>Finds a cited source by id in the active version, so a <c>cite:</c> chip can resolve its preview.</summary>
    /// <param name="sourceId">The id carried by the citation link.</param>
    /// <returns>The source, or <see langword="null"/> when the active version does not cite it.</returns>
    public AiSource? FindSource(string? sourceId)
        => string.IsNullOrEmpty(sourceId)
            ? null
            : ActiveParts.OfType<AiSourcesPart>()
                .SelectMany(part => part.Sources)
                .LastOrDefault(source => string.Equals(source.Id, sourceId, StringComparison.Ordinal));

    /// <summary>All text of the active version concatenated in part order (markdown, not plain text).</summary>
    public string ActiveText => string.Concat(ActiveParts.OfType<AiTextPart>().Select(part => part.Markdown));

    /// <summary>All text of the live parts concatenated in part order (markdown, not plain text).</summary>
    /// <remarks>
    /// These are the LIVE parts — the newest generation, not necessarily the one on screen. Anything the
    /// reader sees or hears for the displayed turn (Copy, read aloud) must project
    /// <see cref="ActiveText"/> instead; <see cref="ActiveParts"/> carries the same distinction for the
    /// sources and every other part type.
    /// </remarks>
    public string Text => string.Concat(Parts.OfType<AiTextPart>().Select(part => part.Markdown));
}
