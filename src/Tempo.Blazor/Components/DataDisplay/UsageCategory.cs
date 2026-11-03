namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// One category of a <see cref="TmUsageMeter"/>: what a slice of the consumption was spent on.
/// </summary>
/// <remarks>
/// <para>
/// A category explains the meter — it never replaces the headline number. The component's
/// <c>Used</c> parameter stays the measured total, the categories are drawn as segments inside the
/// filled part of the track and listed in a real table next to it, and the usage a category does not
/// account for is shown as an explicit localized "Other" row instead of being silently dropped.
/// </para>
/// <para>
/// The model is data only: it carries no callback. A host that needs a drill-down puts a link in the
/// meter's own content instead.
/// </para>
/// </remarks>
public sealed class UsageCategory
{
    /// <summary>
    /// The programmatic key of the category — matched ORDINALLY and rendered as
    /// <c>data-category-key</c>, never as a DOM id (a host value is not a valid id, and a slug would
    /// collide). A category with an empty key is skipped: it could not be told apart from another one.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// The human-readable label, rendered as the row header of the category table. A blank label falls
    /// back to <see cref="Key"/>, so a category is never nameless for a reader.
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// How much of the quota this category consumed, in the meter's own unit. A negative amount counts
    /// as zero for the segment, the share and the row's cell (it reads <c>0</c>), while the row itself
    /// stays visible, so a host's data is never silently dropped from the table.
    /// </summary>
    public double Used { get; set; }

    /// <summary>
    /// Optional colour of the category as a CSS colour value — a <c>var(--tm-*)</c> token reference, a
    /// hex literal, an <c>rgb</c>/<c>hsl</c>/<c>oklch</c>/<c>color-mix</c> function, or a named colour.
    /// It is written into the segment's and the row swatch's inline <c>background-color</c>, so it wins
    /// over the rotating fallback token tone.
    /// </summary>
    /// <remarks>
    /// A value that is not a CSS colour (a CSS class name such as <c>text-green-600</c>, or any payload
    /// carrying <c>;</c>, <c>{</c>, <c>}</c> or <c>url(</c>) emits no style and the category falls back to
    /// the rotating token tone instead. The grammar is the same one <see cref="CssColorValue"/> applies
    /// to <see cref="TmBreakdownCard"/> and <c>TmStatCard.SubValueColor</c>.
    /// </remarks>
    public string? Color { get; set; }
}
