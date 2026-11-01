namespace Tempo.Blazor.Abstractions.Models.Reviews;

/// <summary>
/// One filter tab of a review queue: a stable id the host filters <c>Items</c> by, the visible title
/// and an optional count shown as a badge.
/// <para>
/// The queue renders the tab strip and raises <c>ActiveTabIdChanged</c>; it never filters the items
/// itself, because only the host knows what "Open" or "Needs my review" means for its data. The
/// count is the host's number for the SAME query, so it always agrees with the list the host passes
/// back.
/// </para>
/// </summary>
public sealed class ReviewQueueTab
{
    /// <summary>Stable identity of the tab, used as the tab panel id. Required.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The visible tab label. Required.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional icon name (an <c>IconNames</c> value or a registered icon).</summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Optional badge count for this tab — normally the number of items the tab's query returns.
    /// Null renders no badge; zero renders "0" (an empty tab still reports its emptiness).
    /// </summary>
    public int? Count { get; init; }
}
