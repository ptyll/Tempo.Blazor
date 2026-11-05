using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The one place a lane block's <see cref="AgentLaneBlockKind"/> and <see cref="AgentLaneBlockMarker"/> become an icon,
/// a stable CSS tone slug and a localized WORD.
/// <para>
/// Deliberately internal: this is presentation, not a contract (the contracts are the enums' meanings). It exists so
/// the block glyphs, the marker chips and their stable hooks cannot drift between the timeline and the table view —
/// the surface that owns the run paints one vocabulary everywhere.
/// </para>
/// <para>
/// An unknown value (an enum member from a newer backend, or a cast integer) falls back to the neutral glyph and word,
/// so nothing throws; <see cref="IsKnownKind"/> / <see cref="IsKnownMarker"/> let the component refuse to paint a value
/// it cannot name instead of guessing.
/// </para>
/// </summary>
internal static class AgentLanePresentation
{
    /// <summary>The built-in icon id of a block kind. Always paired with the word, never the only carrier.</summary>
    /// <param name="kind">The kind to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant; <see cref="IconNames.Circle"/> for an unknown value.</returns>
    public static string KindIcon(AgentLaneBlockKind kind) => kind switch
    {
        AgentLaneBlockKind.Task => IconNames.CheckSquare,
        AgentLaneBlockKind.Tool => IconNames.Terminal,
        AgentLaneBlockKind.Message => IconNames.Message,
        AgentLaneBlockKind.Review => IconNames.ShieldCheck,
        AgentLaneBlockKind.Handoff => IconNames.Send,
        _ => IconNames.Circle,
    };

    /// <summary>The stable CSS modifier slug of a block kind.</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>A lowercase slug, spelled out rather than derived from <c>ToString().ToLower()</c>.</returns>
    public static string KindSlug(AgentLaneBlockKind kind) => kind switch
    {
        AgentLaneBlockKind.Task => "task",
        AgentLaneBlockKind.Tool => "tool",
        AgentLaneBlockKind.Message => "message",
        AgentLaneBlockKind.Review => "review",
        AgentLaneBlockKind.Handoff => "handoff",
        _ => "other",
    };

    /// <summary>The localized word of a block kind, in the supplied localizer's language.</summary>
    /// <param name="localizer">The localizer to read the shared <c>TmAgentLanes_BlockKind_*</c> keys from.</param>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The translated word; the neutral "Task" word for an unknown value.</returns>
    public static string KindText(ITmLocalizer localizer, AgentLaneBlockKind kind) => kind switch
    {
        AgentLaneBlockKind.Task => localizer["TmAgentLanes_BlockKind_Task"],
        AgentLaneBlockKind.Tool => localizer["TmAgentLanes_BlockKind_Tool"],
        AgentLaneBlockKind.Message => localizer["TmAgentLanes_BlockKind_Message"],
        AgentLaneBlockKind.Review => localizer["TmAgentLanes_BlockKind_Review"],
        AgentLaneBlockKind.Handoff => localizer["TmAgentLanes_BlockKind_Handoff"],
        _ => localizer["TmAgentLanes_BlockKind_Task"],
    };

    /// <summary>Whether <paramref name="kind"/> is one of the declared kinds.</summary>
    /// <param name="kind">The value to check.</param>
    /// <returns><c>true</c> for a declared member; the component paints the neutral glyph for <c>false</c>.</returns>
    public static bool IsKnownKind(AgentLaneBlockKind kind)
        => kind is >= AgentLaneBlockKind.Task and <= AgentLaneBlockKind.Handoff;

    /// <summary>The built-in icon id of a review marker. Always paired with the word.</summary>
    /// <param name="marker">The marker to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant; <see cref="IconNames.Circle"/> for an unknown value.</returns>
    public static string MarkerIcon(AgentLaneBlockMarker marker) => marker switch
    {
        AgentLaneBlockMarker.Approval => IconNames.CheckCircle,
        AgentLaneBlockMarker.ChangesRequested => IconNames.AlertTriangle,
        AgentLaneBlockMarker.Review => IconNames.Eye,
        _ => IconNames.Circle,
    };

    /// <summary>The stable CSS modifier slug of a review marker.</summary>
    /// <param name="marker">The marker to name.</param>
    /// <returns>A lowercase slug, spelled out rather than derived from <c>ToString().ToLower()</c>.</returns>
    public static string MarkerSlug(AgentLaneBlockMarker marker) => marker switch
    {
        AgentLaneBlockMarker.Approval => "approval",
        AgentLaneBlockMarker.ChangesRequested => "changes-requested",
        AgentLaneBlockMarker.Review => "review",
        _ => "other",
    };

    /// <summary>The localized word of a review marker, in the supplied localizer's language.</summary>
    /// <param name="localizer">The localizer to read the shared <c>TmAgentLanes_Marker_*</c> keys from.</param>
    /// <param name="marker">The marker to name.</param>
    /// <returns>The translated word; a neutral word for an unknown value.</returns>
    public static string MarkerText(ITmLocalizer localizer, AgentLaneBlockMarker marker) => marker switch
    {
        AgentLaneBlockMarker.Approval => localizer["TmAgentLanes_Marker_Approval"],
        AgentLaneBlockMarker.ChangesRequested => localizer["TmAgentLanes_Marker_ChangesRequested"],
        AgentLaneBlockMarker.Review => localizer["TmAgentLanes_Marker_Review"],
        _ => localizer["TmAgentLanes_NoMarker"],
    };

    /// <summary>Whether <paramref name="marker"/> is one of the declared markers.</summary>
    /// <param name="marker">The value to check.</param>
    /// <returns><c>true</c> for a declared member; the component paints no marker for <c>false</c>.</returns>
    public static bool IsKnownMarker(AgentLaneBlockMarker marker)
        => marker is >= AgentLaneBlockMarker.Approval and <= AgentLaneBlockMarker.Review;
}
