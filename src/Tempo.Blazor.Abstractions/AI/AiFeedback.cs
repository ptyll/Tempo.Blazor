namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The reader's rating of an assistant answer, optionally with the reason they typed after a thumbs
/// down. The reason is always optional: a rating that requires an explanation gets no ratings.
/// </summary>
public sealed class AiFeedback
{
    /// <summary>The rating itself.</summary>
    public AiFeedbackRating Rating { get; set; } = AiFeedbackRating.None;

    /// <summary>Free-text reason supplied after a negative rating, when the reader gave one.</summary>
    public string? Reason { get; set; }

    /// <summary>UTC time the rating was cast.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
