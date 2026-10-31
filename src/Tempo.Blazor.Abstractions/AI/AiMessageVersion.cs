namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One superseded generation of an <see cref="AiMessage"/>: the content a Regenerate replaced. The live
/// answer stays in <see cref="AiMessage.Parts"/> (it is always the LAST version), so a conversation can
/// switch between "1/3", "2/3" and "3/3" without copying the current answer into the history first.
/// </summary>
/// <remarks>
/// The version is a snapshot: its <see cref="Parts"/> are copied when it is created, so later stream
/// deltas that grow the live message never mutate a history entry the user is looking at.
/// </remarks>
public sealed class AiMessageVersion
{
    /// <summary>Creates an empty version with a fresh id.</summary>
    public AiMessageVersion()
    {
    }

    /// <summary>Creates a version from an existing part list (the parts are copied, not referenced).</summary>
    /// <param name="parts">Parts of the superseded generation, in rendering order.</param>
    public AiMessageVersion(IEnumerable<AiMessagePart> parts)
        : this(parts, DateTimeOffset.UtcNow, null)
    {
    }

    /// <summary>Creates a version from an existing part list, timestamped by the caller.</summary>
    /// <param name="parts">Parts of the superseded generation, in rendering order.</param>
    /// <param name="createdAt">When the generation was produced.</param>
    /// <param name="model">Model identifier that produced it, when the host reports one.</param>
    public AiMessageVersion(IEnumerable<AiMessagePart> parts, DateTimeOffset createdAt, string? model)
    {
        ArgumentNullException.ThrowIfNull(parts);

        foreach (var part in parts)
        {
            Parts.Add(part);
        }

        CreatedAt = createdAt;
        Model = model;
    }

    /// <summary>Stable version identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Ordered parts of this generation.</summary>
    public IList<AiMessagePart> Parts { get; } = new List<AiMessagePart>();

    /// <summary>UTC creation time of the generation.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Model identifier that produced this generation, when the host reports one.</summary>
    public string? Model { get; set; }

    /// <summary>All text of the version concatenated in part order (markdown, not plain text).</summary>
    public string Text => string.Concat(Parts.OfType<AiTextPart>().Select(part => part.Markdown));
}
