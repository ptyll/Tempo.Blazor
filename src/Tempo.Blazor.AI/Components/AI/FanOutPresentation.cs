using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The one place a fan-out surface turns a <see cref="FanOutFilter"/> into a state, a slug and a localized word.
/// <para>
/// Deliberately internal: a filter is presentation, the states are the contract. It exists so the filter bar, the tile
/// tones and the tests cannot drift — <see cref="All"/> matches everything and every other member maps onto exactly one
/// <see cref="AgentActivityStatus"/>, whose icon and tone come from <see cref="AgentStatusPresentation"/>.
/// </para>
/// </summary>
internal static class FanOutPresentation
{
    /// <summary>The filters the bar offers, in reading order (all, then the work's own progress).</summary>
    public static readonly IReadOnlyList<FanOutFilter> Filters =
    [
        FanOutFilter.All,
        FanOutFilter.Queued,
        FanOutFilter.Running,
        FanOutFilter.WaitingForApproval,
        FanOutFilter.Succeeded,
        FanOutFilter.Failed,
        FanOutFilter.Cancelled,
    ];

    /// <summary>The status a filter selects, or <c>null</c> for <see cref="FanOutFilter.All"/>.</summary>
    /// <param name="filter">The filter to map.</param>
    /// <returns>The one status the filter shows, or <c>null</c> when it shows everything.</returns>
    public static AgentActivityStatus? Status(FanOutFilter filter) => filter switch
    {
        FanOutFilter.Queued => AgentActivityStatus.Queued,
        FanOutFilter.Running => AgentActivityStatus.Running,
        FanOutFilter.WaitingForApproval => AgentActivityStatus.WaitingForApproval,
        FanOutFilter.Succeeded => AgentActivityStatus.Succeeded,
        FanOutFilter.Failed => AgentActivityStatus.Failed,
        FanOutFilter.Cancelled => AgentActivityStatus.Cancelled,
        _ => null,
    };

    /// <summary>The stable slug of a filter, used as the <c>data-filter</c> hook and by the tests.</summary>
    /// <param name="filter">The filter to name.</param>
    /// <returns><c>all</c> for <see cref="FanOutFilter.All"/>, otherwise the tone slug of its status.</returns>
    public static string Slug(FanOutFilter filter)
        => Status(filter) is { } status ? AgentStatusPresentation.Tone(status) : "all";

    /// <summary>The localized filter word.</summary>
    /// <param name="localizer">The localizer to read the shared keys from.</param>
    /// <param name="filter">The filter to name.</param>
    /// <returns>The translated word; <c>null</c> for a value the enum does not declare.</returns>
    public static string? Text(ITmLocalizer localizer, FanOutFilter filter) => filter switch
    {
        FanOutFilter.All => localizer["TmFanOutGrid_FilterAll"],
        FanOutFilter.Queued => localizer["TmAgentStatus_Queued"],
        FanOutFilter.Running => localizer["TmAgentStatus_Running"],
        FanOutFilter.WaitingForApproval => localizer["TmAgentStatus_WaitingForApproval"],
        FanOutFilter.Succeeded => localizer["TmAgentStatus_Succeeded"],
        FanOutFilter.Failed => localizer["TmAgentStatus_Failed"],
        FanOutFilter.Cancelled => localizer["TmAgentStatus_Cancelled"],
        _ => null,
    };

    /// <summary>Whether <paramref name="filter"/> is one of the declared members.</summary>
    /// <param name="filter">The value to check.</param>
    /// <returns><c>true</c> for a declared member; an unknown value is read as <see cref="FanOutFilter.All"/>.</returns>
    public static bool IsKnown(FanOutFilter filter) => filter is >= FanOutFilter.All and <= FanOutFilter.Cancelled;
}
