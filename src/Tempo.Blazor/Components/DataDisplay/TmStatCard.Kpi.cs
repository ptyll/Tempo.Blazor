using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The additive KPI half of <see cref="TmStatCard"/> (plan ff043b2e, phase 1): a delta with its
/// trend, a comparison label, an icon, a sparkline, a loading skeleton, an animated numeric value
/// and a whole-card link/button target.
/// </summary>
/// <remarks>
/// <para>
/// Every member here is OPT-IN and the defaults reproduce the 2.9.x card byte for byte — no extra
/// element, no extra attribute, no extra class. <c>TmStatCardTests</c> pins that with an exact
/// markup comparison, because a host that styles <c>.tm-stat-card</c> or asserts on its DOM must not
/// notice this release.
/// </para>
/// <para>
/// Two rules hold every new surface together. Colour is never the only carrier of the trend: the
/// direction also travels as an arrow icon and as a visually hidden word
/// (<c>TmStatCard_TrendUp</c>/<c>Down</c>/<c>Flat</c>). And the tone is the OUTCOME, not the
/// direction: <see cref="PositiveIsGood"/> inverts the colour class of a rise while the direction
/// word still says "Increase", so a falling churn rate can be green and still be a decrease.
/// </para>
/// </remarks>
public partial class TmStatCard
{
    /// <summary>
    /// The format the delta falls back to: a signed number with one optional decimal (<c>+12.5</c>).
    /// The percent symbol and its spacing come from the culture afterwards, so English renders
    /// <c>+12.5%</c> while Czech renders <c>+12,5&nbsp;%</c> — the same placement the culture's own
    /// percent format uses.
    /// </summary>
    private const string SignedNumberFormat = "+0.#;-0.#;0.#";

    /// <summary>The no-break space .NET's percent patterns put between the number and the symbol.</summary>
    private const string NoBreakSpace = "\u00A0";

    /// <summary>
    /// The numeric change of the metric over the comparison period, in PERCENT POINTS: 12.5 renders
    /// as <c>+12,5 %</c>. Null (the default) renders no delta; the trend marker can still appear
    /// through <see cref="TrendDirection"/>.
    /// </summary>
    /// <remarks>
    /// The parameter is a percentage because that is what a KPI card compares across periods of
    /// different sizes; a host that works in absolute units formats the number itself and passes it
    /// through <see cref="SubValue"/>. The value is formatted with <see cref="DeltaFormat"/> and
    /// <see cref="Culture"/>.
    /// </remarks>
    [Parameter]
    public double? Delta { get; set; }

    /// <summary>
    /// An explicit .NET numeric format string for <see cref="Delta"/>. Default <see langword="null"/>
    /// = the culture's own percent convention: a signed, culture-formatted number with the percent
    /// symbol placed by <see cref="NumberFormatInfo.PercentPositivePattern"/> (English
    /// <c>+12.5%</c>, Czech <c>+12,5&nbsp;%</c>). An invalid format falls back to that same
    /// convention instead of throwing out of a render.
    /// </summary>
    [Parameter]
    public string? DeltaFormat { get; set; }

    /// <summary>
    /// The direction of the trend. Default <see cref="TrendDirection.Auto"/> derives it from the sign
    /// of <see cref="Delta"/>; the explicit members exist for a metric whose delta is unknown but
    /// whose direction is not. Without a delta and with <see cref="TrendDirection.Auto"/> no trend
    /// marker renders at all.
    /// </summary>
    [Parameter]
    public TrendDirection TrendDirection { get; set; } = TrendDirection.Auto;

    /// <summary>
    /// Whether a rise is the good outcome. Default <see langword="true"/>. It decides the TONE (the
    /// <c>--positive</c>/<c>--negative</c> class) only — never the direction word or the arrow — so a
    /// metric where less is better (churn, latency, error rate) passes
    /// <see langword="false"/> and still reads "Decrease".
    /// </summary>
    [Parameter]
    public bool PositiveIsGood { get; set; } = true;

    /// <summary>
    /// The comparison the delta is measured against, e.g. "vs. last month". Host data (the component
    /// does not localize it) rendered next to the delta; a null value renders nothing.
    /// </summary>
    [Parameter]
    public string? ComparisonLabel { get; set; }

    /// <summary>
    /// The name of an icon (<see cref="IconNames"/> constants or a registry entry) rendered in the
    /// card's top corner. Decorative by design: the label and the value already name the card, so
    /// the icon is <c>aria-hidden</c>. Null renders nothing.
    /// </summary>
    [Parameter]
    public string? Icon { get; set; }

    /// <summary>
    /// Optional sparkline data rendered as a mini chart under the metric (through
    /// <see cref="TmSparkline"/>). Null or empty renders nothing; the chart keeps its own accessible
    /// name from <see cref="TmSparkline"/>.
    /// </summary>
    [Parameter]
    public double[]? Sparkline { get; set; }

    /// <summary>
    /// Renders the card as a loading placeholder: three skeletons in place of the value plus a
    /// visually hidden localized "Loading" text, with <c>aria-busy="true"</c> on the root. The label
    /// stays visible (it is static information), while the data-bearing parts are not rendered.
    /// Default <see langword="false"/>.
    /// </summary>
    [Parameter]
    public bool IsLoading { get; set; }

    /// <summary>
    /// Animates <see cref="NumericValue"/> through <see cref="TmAnimatedNumber"/>. Default
    /// <see langword="false"/> — a still number is a pure-Blazor path with no JavaScript at all.
    /// Reduced motion is honoured by <see cref="TmAnimatedNumber"/> independently of this switch.
    /// </summary>
    [Parameter]
    public bool AnimateValue { get; set; }

    /// <summary>
    /// The metric as a NUMBER, rendered through <see cref="TmAnimatedNumber"/> with
    /// <see cref="ValueFormat"/> and <see cref="Culture"/>. When set it wins over the string
    /// <see cref="Value"/> (a card cannot show two values); when null the string value renders as
    /// before.
    /// </summary>
    [Parameter]
    public double? NumericValue { get; set; }

    /// <summary>
    /// The .NET numeric format string of <see cref="NumericValue"/>. Default <c>"N0"</c> (whole,
    /// group-separated numbers), matching <see cref="TmAnimatedNumber.Format"/>.
    /// </summary>
    [Parameter]
    public string ValueFormat { get; set; } = "N0";

    /// <summary>
    /// Text placed before <see cref="NumericValue"/> (a currency symbol, a sign), forwarded to
    /// <see cref="TmAnimatedNumber.Prefix"/>. Host data — the component does not localize it. Ignored
    /// when the string <see cref="Value"/> supplies the value instead.
    /// </summary>
    [Parameter]
    public string? ValuePrefix { get; set; }

    /// <summary>
    /// Text placed after <see cref="NumericValue"/> (for example <c>" %"</c>), forwarded to
    /// <see cref="TmAnimatedNumber.Suffix"/>. Host data — the component does not localize it. Ignored
    /// when the string <see cref="Value"/> supplies the value instead.
    /// </summary>
    [Parameter]
    public string? ValueSuffix { get; set; }

    /// <summary>
    /// The culture that formats <see cref="Delta"/> and <see cref="NumericValue"/>. Default
    /// <see langword="null"/> = the current culture at render time.
    /// </summary>
    [Parameter]
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// Turns the whole card into a link. The root element becomes <c>&lt;a class="tm-stat-card
    /// tm-stat-card--interactive"&gt;</c>, so the entire surface is one native target with the
    /// platform's focus and activation behaviour — no JavaScript, no nested control.
    /// </summary>
    /// <remarks>
    /// Only a relative URL or the <c>http</c>, <c>https</c> and <c>mailto</c> schemes render; a
    /// refused value (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>, a protocol-relative
    /// <c>//elsewhere</c>, or a value carrying a control character that a browser would strip before
    /// reading the scheme) means NO target at all — the card falls back to the plain
    /// <c>&lt;div&gt;</c> rather than rendering an <c>&lt;a&gt;</c> that lies about being a link.
    /// <see cref="OnClick"/> wins over this parameter when both are set.
    /// </remarks>
    [Parameter]
    public string? Href { get; set; }

    /// <summary>
    /// Turns the whole card into a button: the root element becomes
    /// <c>&lt;button type="button" class="tm-stat-card tm-stat-card--interactive"&gt;</c> and this
    /// callback fires on click. Native Enter/Space activation applies — the component adds no key
    /// handling of its own — and a nested interactive control is not allowed inside it.
    /// </summary>
    [Parameter]
    public EventCallback OnClick { get; set; }

    /// <summary>The culture the numeric parts are formatted with — the parameter when set, the current culture otherwise.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>Whether <see cref="NumericValue"/> supplies the value instead of the string <see cref="Value"/>.</summary>
    private bool HasNumericValue => NumericValue.HasValue;

    /// <summary>
    /// Whether the card is ONE native target (<see cref="OnClick"/> or a safe <see cref="Href"/>).
    /// Such a card builds its accessible name from its content, which is why the decorative parts
    /// step out of the accessibility tree when this is true.
    /// </summary>
    private bool IsInteractive => OnClick.HasDelegate || EffectiveHref is not null;

    /// <summary>
    /// The navigation target that may actually render, or null when <see cref="Href"/> is not a safe
    /// link. Trimmed first, because a browser strips leading space characters before resolving the
    /// scheme (<c>" javascript:…"</c> would otherwise still run).
    /// </summary>
    private string? EffectiveHref => SafeHref(Href);

    /// <summary>The value written into the root's <c>aria-busy</c> — <c>null</c> omits the attribute.</summary>
    private string? BusyAttribute => IsLoading ? "true" : null;

    /// <summary>
    /// The root classes: the base class plus the opt-in modifiers. The DEFAULT card yields exactly
    /// <c>tm-stat-card</c>, which is what keeps the legacy markup byte-identical.
    /// </summary>
    private string CardClass
    {
        get
        {
            var classes = "tm-stat-card";
            if (IsInteractive)
            {
                classes += " tm-stat-card--interactive";
            }

            // The icon is absolutely positioned in the top-end corner, so the gutter that keeps the
            // text out from under it exists only when the icon is actually painted.
            if (!IsLoading && !string.IsNullOrEmpty(Icon))
            {
                classes += " tm-stat-card--has-icon";
            }

            if (IsLoading)
            {
                classes += " tm-stat-card--loading";
            }

            return classes;
        }
    }

    /// <summary>The trend marker to render, or null when the card has neither a delta nor a direction.</summary>
    private TrendView? ResolvedTrend
    {
        get
        {
            TrendDirection? direction = TrendDirection != TrendDirection.Auto
                ? TrendDirection
                : Delta switch
                {
                    > 0 => TrendDirection.Up,
                    < 0 => TrendDirection.Down,
                    0 => TrendDirection.Flat,
                    _ => null
                };

            if (direction is null)
            {
                return null;
            }

            var resolved = direction.Value;
            var isGood = (resolved == TrendDirection.Up) == PositiveIsGood;
            var outcome = resolved == TrendDirection.Flat ? "neutral" : isGood ? "positive" : "negative";

            return new TrendView(
                resolved,
                resolved.ToString().ToLowerInvariant(),
                resolved switch
                {
                    TrendDirection.Up => IconNames.ArrowUp,
                    TrendDirection.Down => IconNames.ArrowDown,
                    _ => IconNames.Minus
                },
                resolved switch
                {
                    TrendDirection.Up => "TmStatCard_TrendUp",
                    TrendDirection.Down => "TmStatCard_TrendDown",
                    _ => "TmStatCard_TrendFlat"
                },
                outcome);
        }
    }

    /// <summary>
    /// Formats <see cref="Delta"/> for the current culture. With no <see cref="DeltaFormat"/> the
    /// percent symbol and its spacing follow the culture's own percent pattern; a malformed explicit
    /// format falls back to that same convention, because a bad format is a styling mistake, not a
    /// reason to fail a render.
    /// </summary>
    /// <param name="delta">The percentage delta.</param>
    private string FormatDelta(double delta)
    {
        if (!string.IsNullOrWhiteSpace(DeltaFormat))
        {
            try
            {
                return delta.ToString(DeltaFormat, EffectiveCulture);
            }
            catch (FormatException)
            {
                // Deliberate fall-through: the host's format is broken, the metric is not.
            }
        }

        var number = delta.ToString(SignedNumberFormat, EffectiveCulture);

        // .NET's percent patterns: 0 = "n %", 1 = "n%", 2 = "%n", 3 = "% n" (the space is a
        // no-break space). Mirroring the culture instead of hardcoding " %" keeps a Czech delta
        // typographically correct and an English one free of a stray gap.
        return EffectiveCulture.NumberFormat.PercentPositivePattern switch
        {
            1 => number + "%",
            2 => "%" + number,
            3 => "%" + NoBreakSpace + number,
            _ => number + NoBreakSpace + "%"
        };
    }

    /// <summary>Raises <see cref="OnClick"/> — the root button's only handler.</summary>
    private Task HandleClickAsync() => OnClick.InvokeAsync();

    /// <summary>
    /// The navigation target a host-supplied href may render, or null when it is not a safe link.
    /// A relative URL and the <c>http</c>, <c>https</c> and <c>mailto</c> schemes are accepted;
    /// everything else — <c>javascript:</c>, <c>data:</c>, <c>vbscript:</c> — is refused, and so is
    /// a protocol-relative <c>//elsewhere.example</c> (technically relative, off-origin in practice).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheme is parsed HERE, not by <see cref="Uri"/>: the WHATWG URL parser REMOVES ASCII tab, LF
    /// and CR from a URL before it looks at the scheme, so <c>"java\tscript:alert(1)"</c> navigates
    /// exactly like <c>"javascript:alert(1)"</c> while <c>Uri.TryCreate</c> reports the first one as
    /// relative (<c>IsAbsoluteUri == false</c>) and an allow-list that trusts it hands the browser a
    /// script URL. Any control character is therefore refused OUTRIGHT rather than stripped — the
    /// stripped text can still spell a script URL, so rejecting is the only safe answer.
    /// </para>
    /// <para>
    /// With no control characters left, a scheme is exactly "a colon before the first <c>/</c>,
    /// <c>?</c> or <c>#</c>" (<c>docs/page:1</c> and <c>docs?next=b:c</c> are relative paths, not
    /// schemes) and it has to be one of the three accepted names, compared case-insensitively because
    /// schemes are.
    /// </para>
    /// </remarks>
    /// <param name="href">The candidate value.</param>
    internal static string? SafeHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return null;
        }

        var value = href.Trim();

        // Refuse, never strip: see the remarks. A browser drops these characters and then reads the
        // scheme, so their presence is the attack, not a formatting quirk.
        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return null;
            }
        }

        if (value.StartsWith("//", StringComparison.Ordinal) || value.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return null;
        }

        var colon = value.IndexOf(':');
        var delimiter = value.IndexOfAny(['/', '?', '#']);
        if (colon >= 0 && (delimiter < 0 || colon < delimiter))
        {
            return IsAcceptedScheme(value[..colon]) ? value : null;
        }

        return value;
    }

    /// <summary>Whether <paramref name="scheme"/> is one of the three schemes a card may navigate to.</summary>
    /// <param name="scheme">The text before the first colon, already free of control characters.</param>
    private static bool IsAcceptedScheme(string scheme)
        => string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, "mailto", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One rendered trend marker: the resolved direction, its icon, its visually hidden word key and
    /// the outcome class (<c>positive</c>/<c>negative</c>/<c>neutral</c>).
    /// </summary>
    /// <param name="Direction">The resolved direction.</param>
    /// <param name="Slug">The lower-case direction written into <c>data-trend</c>.</param>
    /// <param name="Icon">The <see cref="IconNames"/> entry of the arrow.</param>
    /// <param name="LabelKey">The resource key of the visually hidden direction word.</param>
    /// <param name="OutcomeClass">The BEM modifier suffix for the trend's tone.</param>
    internal sealed record TrendView(
        TrendDirection Direction,
        string Slug,
        string Icon,
        string LabelKey,
        string OutcomeClass);
}
