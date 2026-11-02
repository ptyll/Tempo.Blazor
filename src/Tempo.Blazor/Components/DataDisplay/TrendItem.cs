namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// One metric row of a <see cref="TmTrendList"/>: a label, its value, an optional change over the
/// comparison period, an optional sparkline and an optional link target.
/// </summary>
/// <remarks>
/// The row is data, not behaviour: it carries no callbacks. A host that needs a row action passes
/// <see cref="Href"/> (a refused value means no target at all — see
/// <see cref="TmTrendList.Items"/>), which keeps the row a single native focus stop.
/// </remarks>
public sealed class TrendItem
{
    /// <summary>
    /// Stable identity of the row. It is rendered as <c>data-item-id</c> — never as a DOM id,
    /// because a host value is not a valid id (<c>a.b</c> and <c>a-b</c> would collide in a CSS
    /// selector, and a long value has no id counterpart).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The metric's name, rendered as the row's label (and as the link text when <see cref="Href"/> renders).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// The host-formatted value, rendered verbatim (e.g. <c>"2.1 %"</c> or <c>"42"</c>). Ignored when
    /// <see cref="NumericValue"/> is set, because a row cannot show two values.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// The value as a NUMBER. When set it wins over <see cref="Value"/> and is formatted with the
    /// list's <see cref="TmTrendList.ValueFormat"/> and <see cref="TmTrendList.Culture"/>.
    /// </summary>
    public double? NumericValue { get; set; }

    /// <summary>
    /// The change over the comparison period in PERCENT POINTS: <c>12.5</c> renders as
    /// <c>+12,5&nbsp;%</c> in Czech and <c>+12.5%</c> in English (the symbol and its spacing come
    /// from the culture). Null renders no change marker at all.
    /// </summary>
    public double? Delta { get; set; }

    /// <summary>
    /// Whether a rise is the good outcome for THIS metric. Default <see langword="true"/>. It decides
    /// the tone (the <c>--positive</c>/<c>--negative</c> class) only — never the direction word or the
    /// arrow — so a metric where less is better (churn, latency, error rate) passes
    /// <see langword="false"/> and still reads "Decrease".
    /// </summary>
    public bool PositiveIsGood { get; set; } = true;

    /// <summary>
    /// Optional mini chart of the metric's history, rendered through <c>TmSparkline</c>. Null (the
    /// default) renders nothing; the chart keeps its own accessible name, so it is a text alternative
    /// rather than a decorative squiggle.
    /// </summary>
    public double[]? Sparkline { get; set; }

    /// <summary>
    /// Optional navigation target, rendered as one native <c>&lt;a&gt;</c> around the label. Only a
    /// relative URL and the <c>http</c>, <c>https</c> and <c>mailto</c> schemes are accepted; a refused
    /// value (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>, a protocol-relative
    /// <c>//host</c>, or a value carrying a control character a browser would strip before reading
    /// the scheme) means NO target at all rather than a rewritten link.
    /// </summary>
    public string? Href { get; set; }
}
