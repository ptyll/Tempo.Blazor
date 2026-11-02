using System.Globalization;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The delta/trend grammar shared by the data-display components that show a change over a period
/// (<see cref="TmStatCard"/> and <see cref="TmTrendList"/>). One implementation keeps the culture's
/// percent convention, the direction icon and the hidden direction word identical in both — a host
/// that switches a metric from a KPI card to a trend row must not get a differently formatted number.
/// </summary>
internal static class TrendDelta
{
    /// <summary>
    /// The format a delta falls back to: a signed number with one optional decimal (<c>+12.5</c>).
    /// The percent symbol and its spacing come from the culture afterwards.
    /// </summary>
    internal const string SignedNumberFormat = "+0.#;-0.#;0.#";

    /// <summary>The no-break space .NET's percent patterns put between the number and the symbol.</summary>
    internal const string NoBreakSpace = "\u00A0";

    /// <summary>
    /// Formats a percentage delta for <paramref name="culture"/>. With no explicit
    /// <paramref name="format"/> the percent symbol and its spacing follow the culture's own percent
    /// pattern (English <c>+12.5%</c>, Czech <c>+12,5&nbsp;%</c>); a malformed explicit format falls
    /// back to that same convention, because a bad format is a styling mistake, not a reason to fail
    /// a render.
    /// </summary>
    /// <param name="delta">The percentage delta.</param>
    /// <param name="format">An optional .NET numeric format string supplied by the host.</param>
    /// <param name="culture">The culture that formats the number and places the symbol.</param>
    internal static string Format(double delta, string? format, CultureInfo culture)
    {
        if (!string.IsNullOrWhiteSpace(format))
        {
            try
            {
                return delta.ToString(format, culture);
            }
            catch (FormatException)
            {
                // Deliberate fall-through: the host's format is broken, the metric is not.
            }
        }

        return PlacePercent(delta.ToString(SignedNumberFormat, culture), culture);
    }

    /// <summary>
    /// Formats one whole percentage (a share of a whole, never a signed delta) with the culture's
    /// own symbol placement: English <c>34%</c>, Czech <c>34&nbsp;%</c>. The no-break space is
    /// deliberate — .NET's own <c>P</c> format inserts a regular space for Czech, which lets the
    /// number and its symbol wrap apart mid-cell.
    /// </summary>
    /// <param name="percent">The whole percentage points.</param>
    /// <param name="culture">The culture that formats the number and places the symbol.</param>
    internal static string FormatPercent(int percent, CultureInfo culture)
        => PlacePercent(percent.ToString("0", culture), culture);

    /// <summary>
    /// Places the percent symbol next to an already formatted number, mirroring
    /// <see cref="NumberFormatInfo.PercentPositivePattern"/>: 0 = <c>n %</c>, 1 = <c>n%</c>,
    /// 2 = <c>%n</c>, 3 = <c>% n</c> (the gap is a no-break space in every form).
    /// </summary>
    /// <param name="number">The formatted number.</param>
    /// <param name="culture">The culture whose placement is mirrored.</param>
    private static string PlacePercent(string number, CultureInfo culture)
        => culture.NumberFormat.PercentPositivePattern switch
        {
            1 => number + "%",
            2 => "%" + number,
            3 => "%" + NoBreakSpace + number,
            _ => number + NoBreakSpace + "%"
        };

    /// <summary>
    /// The direction a row renders: the host's explicit value when it is not
    /// <see cref="TrendDirection.Auto"/>, otherwise the sign of <paramref name="delta"/>. Null means
    /// no marker at all (no delta and no explicit direction).
    /// </summary>
    /// <param name="delta">The row's percentage delta, when it has one.</param>
    /// <param name="requested">The host-supplied direction.</param>
    internal static TrendDirection? ResolveDirection(double? delta, TrendDirection requested)
    {
        if (requested != TrendDirection.Auto)
        {
            return requested;
        }

        return delta switch
        {
            > 0 => TrendDirection.Up,
            < 0 => TrendDirection.Down,
            0 => TrendDirection.Flat,
            _ => null
        };
    }

    /// <summary>
    /// The tone a direction paints: the OUTCOME, not the direction, so a metric where less is better
    /// is green while it falls. A flat trend is always neutral.
    /// </summary>
    /// <param name="direction">The resolved direction.</param>
    /// <param name="positiveIsGood">Whether a rise is the good news for this metric.</param>
    internal static string OutcomeClass(TrendDirection direction, bool positiveIsGood)
    {
        if (direction == TrendDirection.Flat)
        {
            return "neutral";
        }

        return (direction == TrendDirection.Up) == positiveIsGood ? "positive" : "negative";
    }

    /// <summary>The <see cref="IconNames"/> entry that carries the direction without colour.</summary>
    /// <param name="direction">The resolved direction.</param>
    internal static string Icon(TrendDirection direction) => direction switch
    {
        TrendDirection.Up => IconNames.ArrowUp,
        TrendDirection.Down => IconNames.ArrowDown,
        _ => IconNames.Minus
    };

    /// <summary>
    /// The resource key of the visually hidden direction word, namespaced by the owning component
    /// (<paramref name="prefix"/> is <c>TmStatCard</c> or <c>TmTrendList</c>) so each component owns
    /// its own wording.
    /// </summary>
    /// <param name="direction">The resolved direction.</param>
    /// <param name="prefix">The owning component's resource-key prefix.</param>
    internal static string LabelKey(TrendDirection direction, string prefix) => direction switch
    {
        TrendDirection.Up => prefix + "_TrendUp",
        TrendDirection.Down => prefix + "_TrendDown",
        _ => prefix + "_TrendFlat"
    };

    /// <summary>The lower-case direction written into the <c>data-trend</c> attribute.</summary>
    /// <param name="direction">The resolved direction.</param>
    internal static string Slug(TrendDirection direction) => direction switch
    {
        TrendDirection.Up => "up",
        TrendDirection.Down => "down",
        _ => "flat"
    };


    /// <summary>
    /// The complete marker a row renders, or null when it has neither a delta nor an explicit
    /// direction. One record keeps the arrow, the direction word and the tone in lockstep — a
    /// component cannot render the tone without the word that carries the same fact for a reader.
    /// </summary>
    /// <param name="Direction">The resolved direction.</param>
    /// <param name="Slug">The lower-case direction written into <c>data-trend</c>.</param>
    /// <param name="Icon">The <see cref="IconNames"/> entry of the arrow.</param>
    /// <param name="LabelKey">The resource key of the visually hidden direction word.</param>
    /// <param name="OutcomeClass">The BEM modifier suffix for the trend's tone.</param>
    internal sealed record TrendMarker(
        TrendDirection Direction,
        string Slug,
        string Icon,
        string LabelKey,
        string OutcomeClass);

    /// <summary>
    /// Resolves the marker to render for one metric, or null when there is nothing to show.
    /// </summary>
    /// <param name="delta">The metric's percentage delta, when it has one.</param>
    /// <param name="requested">The host-supplied direction.</param>
    /// <param name="positiveIsGood">Whether a rise is the good news for this metric.</param>
    /// <param name="keyPrefix">The owning component's resource-key prefix.</param>
    internal static TrendMarker? Marker(
        double? delta,
        TrendDirection requested,
        bool positiveIsGood,
        string keyPrefix)
    {
        var direction = ResolveDirection(delta, requested);
        if (direction is null)
        {
            return null;
        }

        var resolved = direction.Value;
        return new TrendMarker(
            resolved,
            Slug(resolved),
            Icon(resolved),
            LabelKey(resolved, keyPrefix),
            OutcomeClass(resolved, positiveIsGood));
    }

    /// <summary>
    /// The navigation target a host-supplied href may render, or null when it is not a safe link.
    /// A relative URL and the <c>http</c>, <c>https</c> and <c>mailto</c> schemes are accepted;
    /// everything else — <c>javascript:</c>, <c>data:</c>, <c>vbscript:</c> — is refused, and so is
    /// a protocol-relative <c>//elsewhere.example</c> (technically relative, off-origin in practice).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scheme is parsed HERE, not by <see cref="Uri"/>: the WHATWG URL parser REMOVES ASCII tab,
    /// LF and CR from a URL before it looks at the scheme, so <c>"java\tscript:alert(1)"</c>
    /// navigates exactly like <c>"javascript:alert(1)"</c> while <see cref="Uri.TryCreate(string?,
    /// UriKind, out Uri?)"/> reports the first one as relative (<c>IsAbsoluteUri == false</c>) and an
    /// allow-list that trusts it hands the browser a script URL. Any control character is therefore
    /// refused OUTRIGHT rather than stripped — the stripped text can still spell a script URL, so
    /// rejecting is the only safe answer.
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

        if (value.StartsWith("//", StringComparison.Ordinal)
            || value.StartsWith("\\\\", StringComparison.Ordinal))
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

    /// <summary>Whether <paramref name="scheme"/> is one of the three schemes a trend row may navigate to.</summary>
    /// <param name="scheme">The text before the first colon, already free of control characters.</param>
    private static bool IsAcceptedScheme(string scheme)
        => string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
           || string.Equals(scheme, "mailto", StringComparison.OrdinalIgnoreCase);
}
