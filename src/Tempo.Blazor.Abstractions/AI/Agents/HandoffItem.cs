namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One thing that travelled across a handoff — or did not: what it was, what kind of thing it is, and (for an item that
/// was lost) why it never reached the next agent.
/// <para>
/// The record is a VALUE: the same type describes what a step passed on and what it lost, so a host builds one list of
/// <see cref="HandoffStep.PassedItems"/> and one of <see cref="HandoffStep.LostItems"/> and the surface renders both
/// from the same shape. <see cref="Reason"/> is only read on a LOST item; on a passed one it is ignored (a passed item
/// does not need an excuse).
/// </para>
/// <para>
/// Nothing here carries colour: the kind is an enum the surface turns into an icon and a localized word, and the
/// lost/passed distinction is carried by the LIST an item lives in plus an icon and a word, never by a tone alone
/// (WCAG 1.4.1).
/// </para>
/// </summary>
public sealed record HandoffItem
{
    /// <summary>Creates an item of a handoff.</summary>
    /// <param name="label">
    /// What was (or was not) handed over, in the host's own words ("invoice schema", "the buyer's deadline"). Empty
    /// renders the surface's own localized fallback instead of a blank row.
    /// </param>
    /// <param name="kind">Which kind of thing it is. Default <see cref="HandoffItemKind.Context"/>.</param>
    public HandoffItem(string label, HandoffItemKind kind = HandoffItemKind.Context)
    {
        Label = label ?? string.Empty;
        Kind = kind;
    }

    /// <summary>What was handed over, as the host wants it read. Empty renders the surface's own fallback.</summary>
    public string Label { get; init; }

    /// <summary>Which kind of thing it is; painted as an icon AND a localized word.</summary>
    public HandoffItemKind Kind { get; init; }

    /// <summary>
    /// Why this item never reached the next agent ("the context window was full", "the tool was not granted"). Only
    /// read on a LOST item; empty renders no reason. A passed item keeps whatever the host stored and the surface
    /// ignores it.
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>Whether the host supplied a label worth printing.</summary>
    public bool HasLabel => !string.IsNullOrWhiteSpace(Label);

    /// <summary>Whether the item carries a reason worth printing (only a lost item renders it).</summary>
    public bool HasReason => !string.IsNullOrWhiteSpace(Reason);

    /// <summary>Whether <see cref="Kind"/> is one of the declared kinds (a cast integer may not be).</summary>
    public bool HasKnownKind => Kind is >= HandoffItemKind.Context and <= HandoffItemKind.Memory;

    /// <summary>
    /// Copies the item as a LOST one: the same label and kind plus the reason it never arrived. A host builds a
    /// <see cref="HandoffStep.LostItems"/> list from the items it wanted to pass with this helper.
    /// </summary>
    /// <param name="reason">Why the item was lost. Empty keeps the item without a reason.</param>
    /// <returns>A new item carrying <paramref name="reason"/>.</returns>
    public HandoffItem Lost(string? reason) => this with { Reason = reason };
}
