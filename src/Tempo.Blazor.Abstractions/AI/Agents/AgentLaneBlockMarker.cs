namespace Tempo.Blazor.AI.Models;

/// <summary>
/// The outcome badge a block may carry — the verdict a review left on the work, not the block's own state.
/// <para>
/// A marker is always painted as an icon AND a localized word, never as a colour alone (WCAG 1.4.1). The value is
/// nullable on <see cref="AgentLaneBlock.Marker"/>: <c>null</c> means "no verdict yet", which is not the same as
/// any member of this enum, so a component renders no marker at all rather than a neutral one.
/// </para>
/// </summary>
public enum AgentLaneBlockMarker
{
    /// <summary>The work was approved.</summary>
    Approval = 0,

    /// <summary>A reviewer asked for changes; the work is not done yet.</summary>
    ChangesRequested = 1,

    /// <summary>The block is under review and no verdict exists yet.</summary>
    Review = 2,
}
