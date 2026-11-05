using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The one place an <c>AgentActivityStatus</c> becomes an icon, a CSS tone slug and a localized WORD.
/// <para>
/// Deliberately internal: this is presentation, not a contract (the contract is the enum's meaning). It exists so
/// the eleven agent visualizations cannot drift — every surface that paints an agent's state reads the same glyph,
/// the same stable <c>.tm-agent-badge__status--{tone}</c> hook and the same resource keys.
/// </para>
/// <para>
/// An unknown value (an enum member from a newer backend, or a cast integer) falls back to the neutral
/// <c>Idle</c> tone so nothing throws; <see cref="IsKnown"/> lets a component refuse to paint a state it cannot
/// name instead of guessing.
/// </para>
/// </summary>
internal static class AgentStatusPresentation
{
    /// <summary>The built-in icon id of the state. Always paired with the word, never the only carrier.</summary>
    /// <param name="status">The state to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant; <see cref="IconNames.Circle"/> for an unknown value.</returns>
    public static string Icon(AgentActivityStatus status) => status switch
    {
        AgentActivityStatus.Queued => IconNames.Clock,
        AgentActivityStatus.Running => IconNames.Activity,
        AgentActivityStatus.WaitingForApproval => IconNames.Lock,
        AgentActivityStatus.Succeeded => IconNames.CheckCircle,
        AgentActivityStatus.Failed => IconNames.AlertCircle,
        AgentActivityStatus.Cancelled => IconNames.Ban,
        _ => IconNames.Circle,
    };

    /// <summary>The stable CSS modifier slug of the state (<c>approval</c> for <c>WaitingForApproval</c>).</summary>
    /// <param name="status">The state to name.</param>
    /// <returns>A lowercase slug, never derived from <c>ToString().ToLower()</c>, so the hook cannot drift.</returns>
    public static string Tone(AgentActivityStatus status) => status switch
    {
        AgentActivityStatus.Queued => "queued",
        AgentActivityStatus.Running => "running",
        AgentActivityStatus.WaitingForApproval => "approval",
        AgentActivityStatus.Succeeded => "succeeded",
        AgentActivityStatus.Failed => "failed",
        AgentActivityStatus.Cancelled => "cancelled",
        _ => "idle",
    };

    /// <summary>The localized word of the state, in the supplied localizer's language.</summary>
    /// <param name="localizer">The localizer to read the shared <c>TmAgentStatus_*</c> keys from.</param>
    /// <param name="status">The state to name.</param>
    /// <returns>The translated word; the neutral word for an unknown value.</returns>
    public static string Text(ITmLocalizer localizer, AgentActivityStatus status) => status switch
    {
        AgentActivityStatus.Queued => localizer["TmAgentStatus_Queued"],
        AgentActivityStatus.Running => localizer["TmAgentStatus_Running"],
        AgentActivityStatus.WaitingForApproval => localizer["TmAgentStatus_WaitingForApproval"],
        AgentActivityStatus.Succeeded => localizer["TmAgentStatus_Succeeded"],
        AgentActivityStatus.Failed => localizer["TmAgentStatus_Failed"],
        AgentActivityStatus.Cancelled => localizer["TmAgentStatus_Cancelled"],
        _ => localizer["TmAgentStatus_Idle"],
    };

    /// <summary>Whether <paramref name="status"/> is one of the declared states.</summary>
    /// <param name="status">The value to check.</param>
    /// <returns><c>true</c> for a declared member; a component renders no chip for <c>false</c>.</returns>
    public static bool IsKnown(AgentActivityStatus status)
        => status is >= AgentActivityStatus.Idle and <= AgentActivityStatus.Cancelled;
}
