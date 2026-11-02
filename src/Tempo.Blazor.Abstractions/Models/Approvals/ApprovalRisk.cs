namespace Tempo.Blazor.Abstractions.Models.Approvals;

/// <summary>
/// Risk level of an approval request, shown as a localized badge on
/// <c>TmApprovalCard</c>.
/// <para>
/// The level drives the badge text and the card's accent, so a reader always gets the risk in
/// words — the colour is a second channel, never the only one (WCAG 1.4.1).
/// </para>
/// </summary>
public enum ApprovalRisk
{
    /// <summary>Routine request; nothing unusual about it.</summary>
    Low,

    /// <summary>The request needs attention but is within the usual range.</summary>
    Medium,

    /// <summary>Consequential request — money, access or an irreversible action.</summary>
    High,

    /// <summary>Request that must not be approved without an explicit second look.</summary>
    Critical,
}
