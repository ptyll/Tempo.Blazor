namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// One slice of a <see cref="TmBreakdownCard"/>: a label, its value and an optional colour.
/// </summary>
/// <remarks>
/// A part carries no callback — the card is a read-only summary. A host that needs a drill-down puts
/// a link next to the card; the card itself stays one table a reader can consume in order.
/// </remarks>
public sealed class BreakdownPart
{
    /// <summary>
    /// Stable identity of the part, rendered as <c>data-part-id</c>. It is never a DOM id: a host
    /// value is not a valid id, and a slug would collide (<c>a.b</c> vs <c>a-b</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// It should be UNIQUE within one card. The card uses it as the row identity: the Blazor
    /// <c>@key</c> that lets the reorder animation move the right node, and the key of the FLIP shift
    /// lookup.
    /// </para>
    /// <para>
    /// Duplicate and empty ids are TOLERATED, not trusted: the card derives a private row key for
    /// every row that does not own a unique id, so all rows still render and the table never throws.
    /// Such a row is simply not tracked across a reorder — a reorder of rows without a unique id is
    /// not animated. Empty is the default, so a host that writes parts with only
    /// <see cref="Label"/> and <see cref="Value"/> gets a working (unanimated) card instead of an
    /// exception.
    /// </para>
    /// </remarks>
    public string Id { get; set; } = string.Empty;

    /// <summary>The part's name, rendered as the row header and never only as a colour legend.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The part's amount. It drives both the proportional strip and the percentage; a negative
    /// amount counts as zero (a breakdown of a whole cannot be negative) while the row still shows
    /// the part, so the data is never silently dropped.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Optional colour of the part, as a CSS colour value — a <c>var(--tm-*)</c> token reference, a
    /// hex literal, an <c>rgb</c>/<c>hsl</c>/<c>oklch</c>/<c>color-mix</c> function, or a named
    /// colour. It is written into the segment's and the row swatch's inline
    /// <c>background-color</c>, so the parameter wins over the fallback token tone no matter where
    /// the consumer's stylesheet sits in source order.
    /// </summary>
    /// <remarks>
    /// A value that is not a CSS colour (a CSS class name such as <c>text-green-600</c>, or any
    /// payload carrying <c>;</c>, <c>{</c>, <c>}</c> or <c>url(</c>) emits no style and the part falls
    /// back to the rotating token tone instead. The same grammar <c>TmStatCard.SubValueColor</c>
    /// validates since 2.9.0 — see <see cref="CssColorValue"/>.
    /// </remarks>
    public string? Color { get; set; }
}
