using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The presentation rules of <see cref="TmMemoryPanel"/>: the word and the glyph of every status, the slug the markup
/// carries, and how a category with no name is titled.
/// <para>
/// One mapping means the row's chip, the status filter and the announcement always name a state the same way. The tone is
/// the THIRD cue: the word is the state, the icon is the second cue, and the colour only agrees with both.
/// The presentation rules live INSIDE the package: every other <c>*Presentation</c> helper here is <c>internal</c>, and a
/// public static class would turn shapes like <see cref="FilterSlug"/> into a 2.10.0 API promise no host asked for.
/// </para>
/// </summary>
internal static class MemoryPresentation
{
    /// <summary>The localized word for a state ("Active", "Paused", "Forgotten", "To review").</summary>
    /// <param name="localizer">The localizer that owns the words.</param>
    /// <param name="status">The state to name.</param>
    /// <returns>A non-empty word for every declared state, and a neutral fallback for a cast integer.</returns>
    public static string StatusText(ITmLocalizer localizer, MemoryItemStatus status) => status switch
    {
        MemoryItemStatus.Active => localizer["TmMemoryPanel_StatusActive"],
        MemoryItemStatus.Paused => localizer["TmMemoryPanel_StatusPaused"],
        MemoryItemStatus.Forgotten => localizer["TmMemoryPanel_StatusForgotten"],
        MemoryItemStatus.PendingReview => localizer["TmMemoryPanel_StatusPendingReview"],
        _ => localizer["TmMemoryPanel_StatusUnknown"],
    };

    /// <summary>The glyph that repeats a state: a check for stored, a pause for kept-but-unused, a cross for forgotten,
    /// a question mark for a proposal.</summary>
    /// <param name="status">The state to draw.</param>
    /// <returns>A name from <see cref="IconNames"/>.</returns>
    public static string StatusIcon(MemoryItemStatus status) => status switch
    {
        MemoryItemStatus.Active => IconNames.CheckCircle,
        MemoryItemStatus.Paused => IconNames.Pause,
        MemoryItemStatus.Forgotten => IconNames.XCircle,
        MemoryItemStatus.PendingReview => IconNames.HelpCircle,
        _ => IconNames.Circle,
    };

    /// <summary>The tone a row and its chip carry, as a slug the stylesheet keys off.</summary>
    /// <param name="status">The state to tone.</param>
    /// <returns><c>active</c>, <c>paused</c>, <c>forgotten</c>, <c>pending-review</c> or <c>unknown</c>.</returns>
    public static string StatusSlug(MemoryItemStatus status) => status switch
    {
        MemoryItemStatus.Active => "active",
        MemoryItemStatus.Paused => "paused",
        MemoryItemStatus.Forgotten => "forgotten",
        MemoryItemStatus.PendingReview => "pending-review",
        _ => "unknown",
    };

    /// <summary>The group heading for a category: the host's own name, or the localized name for "no category".</summary>
    /// <param name="localizer">The localizer that owns the fallback.</param>
    /// <param name="category">The host's category, possibly blank.</param>
    /// <returns>A non-empty group name.</returns>
    public static string CategoryName(ITmLocalizer localizer, string? category)
        => string.IsNullOrWhiteSpace(category) ? localizer["TmMemoryPanel_Uncategorized"] : category!;

    /// <summary>Whether a memory belongs to the current status filter.</summary>
    /// <param name="statusFilter">The filter the reader picked.</param>
    /// <param name="status">The memory's state.</param>
    /// <returns><c>true</c> for <see cref="MemoryStatusFilter.All"/> and for the matching state.</returns>
    public static bool MatchesFilter(MemoryStatusFilter statusFilter, MemoryItemStatus status) => statusFilter switch
    {
        MemoryStatusFilter.All => true,
        MemoryStatusFilter.Active => status == MemoryItemStatus.Active,
        MemoryStatusFilter.Paused => status == MemoryItemStatus.Paused,
        MemoryStatusFilter.PendingReview => status == MemoryItemStatus.PendingReview,
        MemoryStatusFilter.Forgotten => status == MemoryItemStatus.Forgotten,
        _ => true,
    };

    /// <summary>The localizer key of a filter's segment label.</summary>
    /// <param name="statusFilter">The filter whose label is needed.</param>
    /// <returns>The key the segment label resolves through.</returns>
    public static string FilterLabelKey(MemoryStatusFilter statusFilter) => statusFilter switch
    {
        MemoryStatusFilter.Active => "TmMemoryPanel_FilterActive",
        MemoryStatusFilter.Paused => "TmMemoryPanel_FilterPaused",
        MemoryStatusFilter.PendingReview => "TmMemoryPanel_FilterPendingReview",
        MemoryStatusFilter.Forgotten => "TmMemoryPanel_FilterForgotten",
        _ => "TmMemoryPanel_FilterAll",
    };

    /// <summary>The stable <c>data-value</c> of a filter segment.</summary>
    /// <param name="statusFilter">The filter.</param>
    /// <returns><c>all</c>, <c>active</c>, <c>paused</c>, <c>pending-review</c> or <c>forgotten</c>.</returns>
    public static string FilterSlug(MemoryStatusFilter statusFilter) => statusFilter switch
    {
        MemoryStatusFilter.Active => "active",
        MemoryStatusFilter.Paused => "paused",
        MemoryStatusFilter.PendingReview => "pending-review",
        MemoryStatusFilter.Forgotten => "forgotten",
        _ => "all",
    };

    /// <summary>Reads the stable slug of a filter back (the segmented control reports strings, never cast enums).</summary>
    /// <param name="slug">The slug a segment carries (<c>all</c>, <c>active</c>, <c>paused</c>, <c>pending-review</c>, <c>forgotten</c>).</param>
    /// <param name="statusFilter">The filter the slug names, or <see cref="MemoryStatusFilter.All"/> when it names none.</param>
    /// <returns><c>true</c> when the slug named a declared filter.</returns>
    public static bool TryParseFilterSlug(string? slug, out MemoryStatusFilter statusFilter)
    {
        switch (slug)
        {
            case "active":
                statusFilter = MemoryStatusFilter.Active;
                return true;
            case "paused":
                statusFilter = MemoryStatusFilter.Paused;
                return true;
            case "pending-review":
                statusFilter = MemoryStatusFilter.PendingReview;
                return true;
            case "forgotten":
                statusFilter = MemoryStatusFilter.Forgotten;
                return true;
            case "all":
                statusFilter = MemoryStatusFilter.All;
                return true;
            default:
                statusFilter = MemoryStatusFilter.All;
                return false;
        }
    }

    /// <summary>Whether a cast value is one of the declared filters.</summary>
    /// <param name="statusFilter">The value to check.</param>
    /// <returns><c>true</c> for a declared filter.</returns>
    public static bool IsKnownFilter(MemoryStatusFilter statusFilter)
        => statusFilter is >= MemoryStatusFilter.All and <= MemoryStatusFilter.Forgotten;

    /// <summary>Whether a cast value is one of the declared forget modes.</summary>
    /// <param name="mode">The value to check.</param>
    /// <returns><c>true</c> for a declared mode.</returns>
    public static bool IsKnownForgetMode(MemoryForgetMode mode)
        => mode is MemoryForgetMode.UndoWindow or MemoryForgetMode.Immediate;
}
