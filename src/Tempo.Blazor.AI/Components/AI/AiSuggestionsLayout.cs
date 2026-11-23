namespace Tempo.Blazor.Components.AI;

/// <summary>
/// How the empty-state suggestion chips of <see cref="TmAiConversation"/> sit in the panel.
/// Default <see cref="Wrap"/> keeps today's wrapping, centred row.
/// </summary>
public enum AiSuggestionsLayout
{
    /// <summary>
    /// The chips wrap and stay centred (the default). The empty-state DOM is unchanged.
    /// </summary>
    Wrap = 0,

    /// <summary>
    /// A single nowrap row that scrolls horizontally. A partially visible focused chip is revealed
    /// inside the row (never by scrolling the transcript or the page), and the chip's focus ring
    /// sits inside the scroller so overflow does not clip it.
    /// </summary>
    ScrollRow = 1,

    /// <summary>
    /// <see cref="ScrollRow"/> when the conversation is narrower than 640 px, <see cref="Wrap"/>
    /// otherwise. The switch is a CSS container query; the default Wrap paint is unchanged at
    /// desktop widths.
    /// </summary>
    Auto = 2,
}
