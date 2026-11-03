namespace Tempo.Blazor.Models;

/// <summary>
/// One selectable value of a <see cref="FacetGroup"/> — the option row of a faceted filter
/// (<c>TmFacetFilter</c>).
/// <para>
/// The model is data only: it carries the programmatic <see cref="Value"/> the host filters on, the
/// human <see cref="Label"/> a reader sees, an optional result <see cref="Count"/> (the number of
/// records the option would leave — that is what makes a facet a facet rather than a plain
/// checkbox list), an optional decorative <see cref="Icon"/> and <see cref="IsDisabled"/> for an
/// option the current data cannot offer. The component never interprets the value; it hands the
/// selected values back as a set keyed by the group's <see cref="FacetGroup.Key"/>.
/// </para>
/// </summary>
public sealed record FacetOption
{
    /// <summary>
    /// The programmatic value of the option, returned in the selection set of its group. Values are
    /// matched ORDINALLY (case-sensitive) within a group; an empty value is ignored by the component.
    /// </summary>
    public string Value { get; init; } = string.Empty;

    /// <summary>
    /// The human-readable label — the option's accessible name (together with the count, when one is
    /// supplied). A blank label falls back to <see cref="Value"/>, so an option is never nameless.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// The number of results this option would leave, shown next to the label and included in the
    /// checkbox's accessible name. <see langword="null"/> (the default) renders no count at all —
    /// a host that does not know the counts must not be forced to invent them.
    /// </summary>
    public int? Count { get; init; }

    /// <summary>
    /// Optional icon name resolved through the icon registry (<c>IconRegistry</c>/<c>IconNames</c>).
    /// The icon is DECORATIVE — it is rendered <c>aria-hidden</c>, so the label stays the only
    /// carrier of meaning. An unknown name falls back to the generic icon.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Disables this option: it renders disabled (and keeps its count visible) and cannot be toggled.
    /// A disabled option that is ALREADY selected stays selected and keeps its removable chip — the
    /// component never silently drops a filter the user applied earlier.
    /// </summary>
    public bool IsDisabled { get; init; }
}

/// <summary>
/// One group of a faceted filter: a labelled, collapsible set of <see cref="FacetOption"/>s whose
/// selected values come back under <see cref="Key"/> in the component's selection dictionary.
/// </summary>
/// <remarks>
/// A record's generated <c>Equals</c> compares <see cref="Options"/> by REFERENCE, so two groups
/// built separately with equal options are not equal by value. The component never compares groups —
/// it identifies one by <see cref="Key"/> — and equality is deliberately not overridden: a deep
/// comparison of every option on the render path would be the wrong cost for a value the host owns.
/// </remarks>
public sealed record FacetGroup
{
    /// <summary>
    /// The programmatic key of the group — the dictionary key of the selection this group owns.
    /// Keys are matched ORDINALLY against the host's selection dictionary; a group with an empty key
    /// is skipped (its options could not be reported back).
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// The human-readable group label. It is the disclosure button's name, and the prefix of every
    /// active-filter chip this group produces. A blank label falls back to <see cref="Key"/>.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>The options of the group, rendered in the order supplied (the component never re-sorts).</summary>
    public IReadOnlyList<FacetOption> Options { get; init; } = [];

    /// <summary>
    /// The INITIAL collapsed state of the group. The user owns the state afterwards: a parameter set
    /// that flips this value later does not re-collapse or re-expand a group the user already
    /// touched. Default <see langword="false"/> (expanded).
    /// </summary>
    public bool IsCollapsed { get; init; }

    /// <summary>
    /// How many options the group shows before its "Show {n} more" button appears. <see langword="null"/>
    /// (the default) uses the component's <c>MaxVisible</c>; a value of <c>0</c> or less shows every
    /// option of this group.
    /// </summary>
    public int? MaxVisible { get; init; }

    /// <summary>
    /// How many options turn the group's own search box on. <see langword="null"/> (the default) uses
    /// the component's <c>SearchThreshold</c>; a value of <c>0</c> or less disables the search box for
    /// this group (a short list does not need one).
    /// </summary>
    public int? SearchThreshold { get; init; }
}
