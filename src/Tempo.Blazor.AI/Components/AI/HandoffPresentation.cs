using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The one place a handoff surface turns a <see cref="HandoffStatus"/> or a <see cref="HandoffItemKind"/> into an icon,
/// a stable CSS tone slug and a localized word.
/// <para>
/// Deliberately internal: the statuses and the kinds are the contract, this mapping is presentation. It exists so the
/// chain view, the table view and the tests cannot drift — every state is always an ICON plus a WORD plus a tone, never
/// a colour alone (WCAG 1.4.1) — and so an unknown value from a newer backend falls back to the neutral state instead
/// of throwing.
/// </para>
/// </summary>
internal static class HandoffPresentation
{
    /// <summary>The status an unknown value is read as (a handoff nobody classified has not been accepted).</summary>
    /// <param name="status">The value to normalise.</param>
    /// <returns><paramref name="status"/> when it is declared, <see cref="HandoffStatus.Pending"/> otherwise.</returns>
    public static HandoffStatus Normalize(HandoffStatus status)
        => IsKnownStatus(status) ? status : HandoffStatus.Pending;

    /// <summary>Whether <paramref name="status"/> is one of the declared states.</summary>
    /// <param name="status">The value to check.</param>
    /// <returns><c>true</c> for a declared member.</returns>
    public static bool IsKnownStatus(HandoffStatus status)
        => status is >= HandoffStatus.Pending and <= HandoffStatus.Completed;

    /// <summary>The stable CSS modifier slug of a status, never derived from <c>ToString().ToLower()</c>.</summary>
    /// <param name="status">The status to name.</param>
    /// <returns><c>pending</c> / <c>accepted</c> / <c>rejected</c> / <c>completed</c>.</returns>
    public static string StatusTone(HandoffStatus status) => Normalize(status) switch
    {
        HandoffStatus.Accepted => "accepted",
        HandoffStatus.Rejected => "rejected",
        HandoffStatus.Completed => "completed",
        _ => "pending",
    };

    /// <summary>The built-in icon id of a status. Always paired with the word, never the only carrier.</summary>
    /// <param name="status">The status to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant.</returns>
    public static string StatusIcon(HandoffStatus status) => Normalize(status) switch
    {
        HandoffStatus.Accepted => IconNames.UserCheck,
        HandoffStatus.Rejected => IconNames.Ban,
        HandoffStatus.Completed => IconNames.CheckCircle,
        _ => IconNames.Clock,
    };

    /// <summary>The localized word of a status.</summary>
    /// <param name="localizer">The localizer to read the <c>TmHandoffStatus_*</c> keys from.</param>
    /// <param name="status">The status to name.</param>
    /// <returns>The translated word; the pending word for an unknown value.</returns>
    public static string StatusText(ITmLocalizer localizer, HandoffStatus status) => Normalize(status) switch
    {
        HandoffStatus.Accepted => localizer["TmHandoffStatus_Accepted"],
        HandoffStatus.Rejected => localizer["TmHandoffStatus_Rejected"],
        HandoffStatus.Completed => localizer["TmHandoffStatus_Completed"],
        _ => localizer["TmHandoffStatus_Pending"],
    };

    /// <summary>The stable CSS slug of an item kind (<c>memory</c>, <c>artifact</c>, …).</summary>
    /// <param name="kind">The kind to name.</param>
    /// <returns>A lowercase slug; <c>context</c> for an unknown value.</returns>
    public static string KindSlug(HandoffItemKind kind) => NormalizeKind(kind) switch
    {
        HandoffItemKind.Instruction => "instruction",
        HandoffItemKind.Tool => "tool",
        HandoffItemKind.Artifact => "artifact",
        HandoffItemKind.Memory => "memory",
        _ => "context",
    };

    /// <summary>The built-in icon id of an item kind.</summary>
    /// <param name="kind">The kind to draw.</param>
    /// <returns>An <see cref="IconNames"/> constant.</returns>
    public static string KindIcon(HandoffItemKind kind) => NormalizeKind(kind) switch
    {
        HandoffItemKind.Instruction => IconNames.ClipboardList,
        HandoffItemKind.Tool => IconNames.Terminal,
        HandoffItemKind.Artifact => IconNames.Package,
        HandoffItemKind.Memory => IconNames.Database,
        _ => IconNames.MessageSquare,
    };

    /// <summary>The localized word of an item kind.</summary>
    /// <param name="localizer">The localizer to read the <c>TmHandoffItemKind_*</c> keys from.</param>
    /// <param name="kind">The kind to name.</param>
    /// <returns>The translated word; the context word for an unknown value.</returns>
    public static string KindText(ITmLocalizer localizer, HandoffItemKind kind) => NormalizeKind(kind) switch
    {
        HandoffItemKind.Instruction => localizer["TmHandoffItemKind_Instruction"],
        HandoffItemKind.Tool => localizer["TmHandoffItemKind_Tool"],
        HandoffItemKind.Artifact => localizer["TmHandoffItemKind_Artifact"],
        HandoffItemKind.Memory => localizer["TmHandoffItemKind_Memory"],
        _ => localizer["TmHandoffItemKind_Context"],
    };

    /// <summary>The kind an unknown value is read as.</summary>
    /// <param name="kind">The value to normalise.</param>
    /// <returns><paramref name="kind"/> when it is declared, <see cref="HandoffItemKind.Context"/> otherwise.</returns>
    public static HandoffItemKind NormalizeKind(HandoffItemKind kind)
        => kind is >= HandoffItemKind.Context and <= HandoffItemKind.Memory ? kind : HandoffItemKind.Context;
}
