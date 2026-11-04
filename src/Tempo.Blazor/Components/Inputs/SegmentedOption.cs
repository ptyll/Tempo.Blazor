namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// One segment of a <see cref="TmSegmentedControl{TValue}"/>: the label a reader sees and the value the
/// control reports.
/// <para>
/// A segment may also carry an <see cref="Icon"/>, a numeric <see cref="Count"/> and a short
/// <see cref="Badge"/> string. Both the count and the badge are part of the segment's accessible name
/// (they are ordinary text inside the option), so a screen reader reads "Inbox 12" instead of an
/// unlabelled 12 — no separate live region, no duplicate announcement.
/// </para>
/// </summary>
/// <typeparam name="TValue">The type of the value the segment reports.</typeparam>
public sealed record SegmentedOption<TValue>
{
    /// <summary>The visible label of the segment. Rendered as text, never as markup.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The value this segment reports through <see cref="TmSegmentedControl{TValue}.Value"/>. Compared
    /// with <see cref="System.Collections.Generic.EqualityComparer{T}.Default"/>, so a record or an enum
    /// works as naturally as a string.
    /// </summary>
    public TValue Value { get; init; } = default!;

    /// <summary>
    /// Optional icon name (see <see cref="Tempo.Blazor.Components.Icons.IconNames"/>) shown before the
    /// label. The icon is decorative — it never carries the only copy of the label.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Optional count shown as a small numeric suffix ("Inbox 12"). A count is data, not decoration: it
    /// is rendered as text and stays part of the segment's accessible name. Ignored (not rendered) while
    /// no value is set for this segment.
    /// </summary>
    public int? Count { get; init; }

    /// <summary>
    /// Optional short badge shown after the label, for a state word the host owns ("New", "Beta"). It is
    /// text, not a colour-only cue, and stays part of the segment's accessible name.
    /// </summary>
    public string? Badge { get; init; }

    /// <summary>
    /// When <c>true</c> the segment cannot be selected and is skipped by the arrow keys, Home and End —
    /// a disabled segment still renders, because removing it would renumber every neighbour.
    /// </summary>
    public bool IsDisabled { get; init; }

    /// <summary>
    /// Id of the panel this segment owns, used only when the control renders as a tab list
    /// (<see cref="TmSegmentedControl{TValue}.Role"/> is <see cref="SegmentedControlRole.Tabs"/>): it
    /// becomes the tab's <c>aria-controls</c>. Null falls back to
    /// <see cref="TmSegmentedControl{TValue}.PanelId"/>.
    /// </summary>
    public string? PanelId { get; init; }
}
