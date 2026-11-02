namespace Tempo.Blazor.Abstractions.Shared;

/// <summary>
/// A consumer-supplied label rendered next to a comment entry.
/// <see cref="Text"/> is already localized by the consumer; the component does not translate it.
/// </summary>
public sealed class TmCommentBadge
{
    /// <summary>Badge text, already localized by the consumer. Required for the badge to render.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Visual tone. Defaults to <see cref="TmCommentBadgeTone.Neutral"/>.</summary>
    public TmCommentBadgeTone Tone { get; set; } = TmCommentBadgeTone.Neutral;

    /// <summary>Optional Tempo icon name rendered with the badge.</summary>
    public string? Icon { get; set; }

    /// <summary>Optional tooltip and accessible description.</summary>
    public string? Title { get; set; }

    /// <summary>Returns true when <see cref="Text"/> contains non-whitespace characters.</summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Text);
}
