namespace Tempo.Blazor.AI.Models;

/// <summary>How the reader rated an assistant answer.</summary>
public enum AiFeedbackRating
{
    /// <summary>No rating yet.</summary>
    None = 0,

    /// <summary>The answer helped (thumbs up).</summary>
    Positive = 1,

    /// <summary>The answer was not helpful (thumbs down).</summary>
    Negative = 2,
}
