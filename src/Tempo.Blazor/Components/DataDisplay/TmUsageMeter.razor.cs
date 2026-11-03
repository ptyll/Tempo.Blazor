using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The logic half of <see cref="TmUsageMeter"/>: the arithmetic runs in
/// <see cref="OnParametersSet"/> (so the render is a pure projection), the state is derived from the
/// ratio against the two thresholds, and the ONE announcement is raised on a threshold TRANSITION —
/// a freshly rendered meter says nothing, so a page load never talks over the reader.
/// </summary>
public partial class TmUsageMeter : TmComponentBase
{
    /// <summary>The threshold band the current usage sits in.</summary>
    private enum UsageMeterState
    {
        /// <summary>Below the warning threshold.</summary>
        Normal,

        /// <summary>At or above the warning threshold, below the critical one.</summary>
        Warning,

        /// <summary>At or above the critical threshold (by default the limit itself).</summary>
        Critical
    }

    /// <summary>One rendered category row of the table — the amounts a reader gets as text.</summary>
    /// <param name="Key">The category's programmatic key, rendered as <c>data-category-key</c>.</param>
    /// <param name="Label">The resolved label (the key when the host left it blank).</param>
    /// <param name="Used">The amount as the meter counts it (a negative amount is zero).</param>
    /// <param name="Share">The whole percentage of the explained usage.</param>
    /// <param name="Color">The host's colour when it is a valid CSS colour.</param>
    /// <param name="ToneIndex">The fallback token tone of the row's position.</param>
    /// <param name="IsRemainder">Whether the row stands for usage the categories do not explain.</param>
    private readonly record struct UsageRow(
        string Key,
        string Label,
        double Used,
        int Share,
        string? Color,
        int ToneIndex,
        bool IsRemainder);

    /// <summary>One painted segment of the track — the same data as a row, plus its strip weight.</summary>
    /// <param name="Key">The category's programmatic key.</param>
    /// <param name="Weight">The segment's share of the FILLED part, between 0 and 1.</param>
    /// <param name="Color">The host's colour when it is a valid CSS colour.</param>
    /// <param name="ToneIndex">The fallback token tone of the segment's position.</param>
    /// <param name="IsRemainder">Whether the segment stands for usage the categories do not explain.</param>
    private readonly record struct UsageSegment(
        string Key,
        double Weight,
        string? Color,
        int ToneIndex,
        bool IsRemainder);

    /// <summary>The default warning ratio, used when the host supplies no usable value.</summary>
    private const double DefaultWarningThreshold = 0.8;

    /// <summary>The default critical ratio, used when the host supplies no usable value.</summary>
    private const double DefaultCriticalThreshold = 1.0;

    /// <summary>
    /// The invisible character that makes a repeated announcement a real mutation. Two localized
    /// sentences can coincide, and an identical write into a polite live region is silent in some
    /// screen readers; the marker alternates on and off, so every announcement differs from the one
    /// before it while nothing extra is spoken.
    /// </summary>
    private const string AnnouncementMarker = "\u200B";

    /// <summary>The rows of the category table, in the host's order (then the remainder row).</summary>
    private IReadOnlyList<UsageRow> _rows = [];

    /// <summary>The painted segments of the track, in the host's order (then the remainder segment).</summary>
    private IReadOnlyList<UsageSegment> _segments = [];

    /// <summary>
    /// The one polite message. It stays STICKY across re-renders inside the same state — clearing it on
    /// every parameter set could wipe a fresh announcement before assistive technology has read it, and
    /// a polling host would otherwise re-announce the same warning on every refresh.
    /// </summary>
    private string _announcement = string.Empty;

    /// <summary>The state of the previous parameter set, used to detect a transition.</summary>
    private UsageMeterState _state;

    /// <summary>Whether <see cref="_state"/> already describes a render (the first one announces nothing).</summary>
    private bool _stateKnown;

    /// <summary>The text alternative of the daily sparkline, formatted once per parameter set.</summary>
    private string _dailyAriaLabel = string.Empty;

    /// <summary>
    /// The amount consumed, in the host's unit. The meter measures THIS number — a supplied
    /// <see cref="Categories"/> list only explains it, so the headline can never disagree with the
    /// number the host calls the usage. A negative value counts as zero.
    /// </summary>
    [Parameter]
    public double Used { get; set; }

    /// <summary>
    /// The quota the usage is measured against. Default <c>100</c> (a percentage meter). A value of
    /// <c>0</c> or less means the limit is UNKNOWN: nothing is measurable, so the meter renders 0 of 0
    /// and stays in the normal state instead of dividing by zero or claiming an infinite limit.
    /// </summary>
    [Parameter]
    public double Limit { get; set; } = 100;

    /// <summary>
    /// The unit every amount is shown in ("credits", "API calls", "GB"). Null or blank renders bare
    /// numbers and drops the unit from the meter's accessible value text, so no double space is left
    /// behind. The wording is the host's data, not library chrome — it is never localised by the
    /// component.
    /// </summary>
    [Parameter]
    public string? Unit { get; set; }

    /// <summary>
    /// The .NET numeric format string of every amount (used, limit, over-limit and the category
    /// values). Default <c>"N0"</c> (whole, group-separated numbers). The percentage is always a whole
    /// culture-formatted percentage.
    /// </summary>
    [Parameter]
    public string ValueFormat { get; set; } = "N0";

    /// <summary>
    /// Formats one amount instead of <see cref="ValueFormat"/> — for a unit whose own convention is not
    /// a plain number (for example a byte size or an abbreviated "7.5k"). It is used everywhere an
    /// amount appears, including the meter's accessible value text and the category table, so the
    /// reader and the screen reader can never meet two different numbers.
    /// </summary>
    [Parameter]
    public Func<double, string>? ValueFormatter { get; set; }

    /// <summary>
    /// The culture that formats the amounts, the percentage and the reset date. Null (the default)
    /// means the current culture at render time, so the meter follows the request culture without
    /// being told.
    /// </summary>
    [Parameter]
    public CultureInfo? Culture { get; set; }

    /// <summary>
    /// The ratio of the limit (not a percentage) at which the meter turns to the warning state and the
    /// <see cref="NearLimitContent"/> slot appears. Default <c>0.8</c> (80 %). A value of <c>0</c> makes
    /// the meter warn immediately, a negative value counts as <c>0</c> and <see cref="double.NaN"/>
    /// falls back to the default; the state is always checked critical-first, so a
    /// <see cref="CriticalThreshold"/> below this value still wins. An unknown limit (<c>0</c> or less)
    /// keeps the meter normal whatever the thresholds say, because nothing is measurable.
    /// </summary>
    [Parameter]
    public double WarningThreshold { get; set; } = DefaultWarningThreshold;

    /// <summary>
    /// The ratio of the limit at which the meter turns to the critical state. Default <c>1.0</c> — the
    /// limit itself. A negative value counts as <c>0</c> and <see cref="double.NaN"/> falls back to the
    /// default. Any usage ABOVE it is additionally reported as over the limit, with the exact excess in
    /// the host's unit.
    /// </summary>
    [Parameter]
    public double CriticalThreshold { get; set; } = DefaultCriticalThreshold;

    /// <summary>
    /// When the quota resets, rendered as a localized "Resets on {date}" line in the culture's own
    /// short date format. Null (the default) renders no line. The date is formatted with the value's
    /// own offset — it is never shifted into another time zone, so the day the host names is the day
    /// the reader sees.
    /// </summary>
    [Parameter]
    public DateTimeOffset? ResetsAt { get; set; }

    /// <summary>
    /// The usage of the last days, in the host's unit, rendered as a small bar sparkline under the
    /// meter. Null or empty renders nothing. The graphic carries a text alternative with the real
    /// numbers (minimum, maximum, last value), so it is not a decorative squiggle.
    /// </summary>
    [Parameter]
    public double[]? DailyUsage { get; set; }

    /// <summary>
    /// The categories that explain the consumption, drawn as segments inside the filled part of the
    /// track and listed in a real table next to it (caption, column headers, a row header per
    /// category). A category with an empty key is skipped; usage the categories do not account for
    /// becomes an explicit localized "Other" row, so the table never silently loses a number.
    /// </summary>
    [Parameter]
    public IReadOnlyList<UsageCategory> Categories { get; set; } = [];

    /// <summary>
    /// Renders the compact presentation: the track, the amount and the percentage with the state badge,
    /// and nothing else. The reset line, the daily sparkline and the category table are dropped — a
    /// breakdown without its numbers would be colour-only, so a compact meter paints a single-state bar
    /// instead. <see cref="Title"/> stays VISIBLE (as a plain label rather than a heading), because a
    /// compact bar without its label is unreadable the moment several of them sit side by side; the
    /// meter's accessible name falls back from that title to <see cref="AriaLabel"/> and then to the
    /// localized default.
    /// </summary>
    [Parameter]
    public bool Compact { get; set; }

    /// <summary>
    /// An optional heading above the meter. It also NAMES the meter (through <c>aria-labelledby</c>),
    /// so a reader hears what the numbers belong to. The compact presentation renders the same text as a
    /// plain visible label instead of a heading. When the title is set it is the meter's only name —
    /// <see cref="AriaLabel"/> is not used on top of it.
    /// </summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>
    /// The heading level of <see cref="Title"/> (2–6, clamped), so the meter sits under the right page
    /// heading instead of hard-coding an <c>&lt;h3&gt;</c> into the outline. Default <c>3</c>; the
    /// compact presentation renders a plain label and ignores this value.
    /// </summary>
    [Parameter]
    public int HeadingLevel { get; set; } = 3;

    /// <summary>
    /// The accessible name used when no <see cref="Title"/> is rendered. Null means the localized
    /// default ("Usage").
    /// </summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>
    /// The call to action rendered while the usage is at or above the warning threshold — typically a
    /// link to upgrade or buy more quota. Rendered at BOTH the warning and the critical state (the
    /// reader needs it most once the limit is reached) and never while the usage is normal. The host
    /// supplies the control, so focus and keyboard behaviour are its own.
    /// </summary>
    [Parameter]
    public RenderFragment? NearLimitContent { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>The culture the numbers are formatted with — the parameter when set, the current culture otherwise.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>The limit as the meter measures against it — a non-positive limit is an unknown one.</summary>
    private double EffectiveLimit => Limit > 0 ? Limit : 0;

    /// <summary>The used amount as the meter measures it.</summary>
    private double EffectiveUsed => UsageMeterMath.EffectiveUsed(Used, Limit);

    /// <summary>
    /// The used share of the limit as a raw ratio — deliberately NOT clamped, because the percentage
    /// and the state must report a usage over the limit as it is (120 %, not 100 %).
    /// </summary>
    private double UsageRatio => EffectiveLimit > 0 ? EffectiveUsed / EffectiveLimit : 0;

    /// <summary>The used share of the limit, clamped to the 0..1 band — what the track can paint.</summary>
    private double FillRatio => UsageMeterMath.Ratio(EffectiveUsed, EffectiveLimit);

    /// <summary>The whole percentage of the limit, rounded away from zero.</summary>
    private int PercentPoints => (int)Math.Round(UsageRatio * 100, MidpointRounding.AwayFromZero);

    /// <summary>Whether the usage is above the limit (not merely at it).</summary>
    private bool OverLimit => EffectiveLimit > 0 && EffectiveUsed > EffectiveLimit;

    /// <summary>Whether the host supplied a daily history to draw.</summary>
    private bool HasDailyUsage => DailyUsage is { Length: > 0 };

    /// <summary>Whether the visible title renders — in either presentation, when the host set one.</summary>
    private bool HasTitle => !string.IsNullOrWhiteSpace(Title);

    /// <summary>The title's id; it labels the meter in both presentations.</summary>
    private string? LabelledBy => HasTitle ? TitleId : null;

    /// <summary>The heading tag of the full presentation, clamped to the 2–6 band of the page outline.</summary>
    private string TitleHeadingTag
        => "h" + Math.Clamp(HeadingLevel, 2, 6).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The visible heading of the full presentation, built as a render fragment so the tag comes from
    /// <see cref="HeadingLevel"/> instead of a hard-coded <c>h3</c>. The text is added with
    /// <c>AddContent</c>, so a host value is encoded exactly like markup content.
    /// </summary>
    private RenderFragment TitleHeading => builder =>
    {
        builder.OpenElement(0, TitleHeadingTag);
        builder.AddAttribute(1, "class", "tm-usage-meter__title");
        builder.AddAttribute(2, "id", TitleId);
        builder.AddContent(3, Title);
        builder.CloseElement();
    };

    /// <summary>
    /// The meter's <c>aria-label</c>: only when no visible title names it, so a meter never carries
    /// two names. Falls back from the host's <see cref="AriaLabel"/> to the localized default.
    /// </summary>
    private string? AriaLabelAttribute
        => HasTitle
            ? null
            : !string.IsNullOrWhiteSpace(AriaLabel) ? AriaLabel : Loc["TmUsageMeter_AriaLabel"];

    /// <summary>The ids of the lines that add information the meter value cannot carry.</summary>
    private string? DescribedBy
    {
        get
        {
            var ids = new List<string>(2);
            if (OverLimit)
            {
                ids.Add(OverId);
            }

            if (!Compact && ResetsAt is not null)
            {
                ids.Add(ResetId);
            }

            return ids.Count == 0 ? null : string.Join(' ', ids);
        }
    }

    /// <summary>The state band of the current render.</summary>
    private UsageMeterState State => _state;

    /// <summary>The machine-readable state slug, rendered as <c>data-state</c>.</summary>
    private string StateSlug => _state switch
    {
        UsageMeterState.Warning => "warning",
        UsageMeterState.Critical => "critical",
        _ => "normal"
    };

    /// <summary><c>true</c> while the usage is over the limit, otherwise an omitted attribute.</summary>
    private string? OverLimitAttribute => OverLimit ? "true" : null;

    /// <summary>The localized state text, or empty in the normal state.</summary>
    private string StateText => _state switch
    {
        UsageMeterState.Warning => Loc["TmUsageMeter_StateWarning"],
        UsageMeterState.Critical => Loc["TmUsageMeter_StateCritical"],
        _ => string.Empty
    };

    /// <summary>
    /// The state's icon — the carrier that does not depend on colour. Warning and critical deliberately
    /// use different glyphs (triangle vs. circle), so the two states stay distinguishable in a
    /// monochrome or forced-colours rendering.
    /// </summary>
    private string StateIcon => _state switch
    {
        UsageMeterState.Critical => IconNames.AlertCircle,
        _ => IconNames.AlertTriangle
    };

    /// <summary>The unit suffix, or empty when the host supplied no unit.</summary>
    private string UnitSuffix => string.IsNullOrWhiteSpace(Unit) ? string.Empty : " " + Unit!.Trim();

    /// <summary>The visible "used / limit" readout, with the unit when there is one.</summary>
    private string AmountText => FormatAmount(EffectiveUsed) + " / " + FormatAmount(EffectiveLimit) + UnitSuffix;

    /// <summary>The visible percentage of the limit.</summary>
    private string PercentText => FormatPercent(PercentPoints);

    /// <summary>
    /// The meter's accessible value text: "7,500 of 10,000 credits, 75%" — the amount, the unit when
    /// there is one and the whole percentage, all in the supplied culture.
    /// </summary>
    private string ValueText
    {
        get
        {
            var amount = string.Format(
                EffectiveCulture,
                Loc["TmUsageMeter_ValueOf"],
                FormatAmount(EffectiveUsed),
                FormatAmount(EffectiveLimit));
            var percent = FormatPercent(PercentPoints);

            return string.IsNullOrWhiteSpace(Unit)
                ? string.Format(EffectiveCulture, Loc["TmUsageMeter_AriaValueTextNoUnit"], amount, percent)
                : string.Format(EffectiveCulture, Loc["TmUsageMeter_AriaValueText"], amount, Unit!.Trim(), percent);
        }
    }

    /// <summary>The over-limit line: the exact excess in the host's unit.</summary>
    private string OverLimitText
        => string.Format(
            EffectiveCulture,
            Loc["TmUsageMeter_OverLimit"],
            FormatAmount(EffectiveUsed - EffectiveLimit) + UnitSuffix);

    /// <summary>The daily sparkline's text alternative, computed once per parameter set.</summary>
    private string DailyAriaLabel => _dailyAriaLabel;

    /// <summary>The filled part of the track, as an invariant CSS percentage (never above 100).</summary>
    private string FillPercent => (FillRatio * 100).ToString("0.##", CultureInfo.InvariantCulture);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var ratio = UsageRatio;
        var warning = UsableThreshold(WarningThreshold, DefaultWarningThreshold);
        var critical = UsableThreshold(CriticalThreshold, DefaultCriticalThreshold);

        // An unknown limit makes nothing measurable, so no threshold can claim a verdict — a negative
        // WarningThreshold must not put a 0-of-0 meter into the warning state.
        var state = EffectiveLimit <= 0
            ? UsageMeterState.Normal
            : ratio >= critical
                ? UsageMeterState.Critical
                : ratio >= warning
                    ? UsageMeterState.Warning
                    : UsageMeterState.Normal;

        if (_stateKnown && state != _state)
        {
            Announce(state);
        }

        _state = state;
        _stateKnown = true;

        BuildCategories();
        _dailyAriaLabel = BuildDailyAriaLabel();
    }

    /// <summary>
    /// Turns a host threshold into the ratio the comparison uses: <see cref="double.NaN"/> is not a
    /// ratio, so it falls back to the documented default, and a negative value counts as <c>0</c>
    /// (which already warns from the first measurable amount).
    /// </summary>
    /// <param name="threshold">The host's ratio.</param>
    /// <param name="fallback">The documented default.</param>
    private static double UsableThreshold(double threshold, double fallback)
        => double.IsNaN(threshold) ? fallback : Math.Max(0, threshold);

    /// <summary>
    /// Writes the one message for a transition. Two localized sentences may coincide (or a host may
    /// override both), and an identical write into a polite live region is silent in some screen
    /// readers, so a repeated sentence gets an alternating invisible marker. Nothing else is ever
    /// cleared or re-set, so a re-render inside the same state announces nothing.
    /// </summary>
    /// <param name="state">The state the meter has just entered.</param>
    private void Announce(UsageMeterState state)
    {
        var text = Announcement(state);
        _announcement = string.Equals(text, _announcement, StringComparison.Ordinal)
            ? text + AnnouncementMarker
            : text;
    }

    /// <summary>
    /// Builds the category rows and the painted segments in one pass, so the table and the strip can
    /// never disagree: both read the same clamped amounts, the same fallback tones and the same
    /// remainder decision.
    /// </summary>
    private void BuildCategories()
    {
        _rows = [];
        _segments = [];

        if (Compact || Categories is null || Categories.Count == 0)
        {
            return;
        }

        var kept = Categories
            .Where(category => category is not null && !string.IsNullOrEmpty(category.Key))
            .ToList();
        if (kept.Count == 0)
        {
            return;
        }

        var amounts = kept.Select(category => category.Used > 0 ? category.Used : 0).ToList();
        var explained = amounts.Sum();
        var remainder = EffectiveUsed > explained ? EffectiveUsed - explained : 0;

        // The strip is measured against whichever is bigger, so an inconsistent host (categories that
        // claim more than the headline) can never paint past the fill's own width.
        var stripTotal = Math.Max(EffectiveUsed, explained);
        var shareAmounts = new List<double>(amounts);
        if (remainder > 0)
        {
            shareAmounts.Add(remainder);
        }

        var shares = UsageMeterMath.Shares(shareAmounts, stripTotal);

        var rows = new List<UsageRow>(kept.Count + 1);
        var segments = new List<UsageSegment>(kept.Count + 1);
        for (var index = 0; index < kept.Count; index++)
        {
            var category = kept[index];
            var label = string.IsNullOrWhiteSpace(category.Label) ? category.Key : category.Label;
            var color = ColorOf(category);
            var tone = index % 6;
            var weight = stripTotal > 0 ? amounts[index] / stripTotal : 0;

            rows.Add(new UsageRow(category.Key, label, amounts[index], shares[index], color, tone, false));
            segments.Add(new UsageSegment(category.Key, weight, color, tone, false));
        }

        if (remainder > 0)
        {
            var label = Loc["TmUsageMeter_OtherCategory"];
            var weight = stripTotal > 0 ? remainder / stripTotal : 0;
            rows.Add(new UsageRow(string.Empty, label, remainder, shares[^1], null, kept.Count % 6, true));
            segments.Add(new UsageSegment(string.Empty, weight, null, kept.Count % 6, true));
        }

        _rows = rows;
        _segments = segments;
    }

    /// <summary>
    /// The daily sparkline's text alternative: the localized series name followed by the real minimum,
    /// maximum and last value, formatted with the meter's own culture and value format.
    /// </summary>
    private string BuildDailyAriaLabel()
    {
        if (!HasDailyUsage)
        {
            return string.Empty;
        }

        var values = DailyUsage!;
        return string.Format(
            EffectiveCulture,
            Loc["TmUsageMeter_DailyUsageSummary"],
            FormatAmount(values.Min()),
            FormatAmount(values.Max()),
            FormatAmount(values[^1]));
    }

    /// <summary>The one message a transition raises — the same verdict the visible badge shows.</summary>
    /// <param name="state">The state the meter has just entered.</param>
    private string Announcement(UsageMeterState state) => state switch
    {
        UsageMeterState.Warning => Loc["TmUsageMeter_AnnounceWarning"],
        UsageMeterState.Critical => Loc["TmUsageMeter_AnnounceCritical"],
        _ => Loc["TmUsageMeter_AnnounceNormal"]
    };

    /// <summary>Formats one amount with <see cref="ValueFormatter"/> when supplied, else <see cref="ValueFormat"/>.</summary>
    /// <param name="value">The amount.</param>
    private string FormatAmount(double value)
    {
        if (ValueFormatter is not null)
        {
            return ValueFormatter(value);
        }

        var format = string.IsNullOrWhiteSpace(ValueFormat) ? "N0" : ValueFormat;
        try
        {
            return value.ToString(format, EffectiveCulture);
        }
        catch (FormatException)
        {
            // A broken host format is a styling mistake, not a reason to fail a render.
            return value.ToString("N0", EffectiveCulture);
        }
    }

    /// <summary>Formats a whole percentage with the culture's own symbol placement.</summary>
    /// <param name="percent">The whole percentage points.</param>
    private string FormatPercent(int percent) => TrendDelta.FormatPercent(percent, EffectiveCulture);

    /// <summary>
    /// Formats a number for a machine-readable attribute (<c>aria-valuenow</c>/<c>aria-valuemax</c>):
    /// attribute values are read by assistive technology, not by a human, so they are always
    /// invariant — a Czech group separator would turn the number into a different value.
    /// </summary>
    /// <param name="value">The number.</param>
    private static string Invariant(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The reset line's text: the localized template around the culture's short date.</summary>
    /// <param name="resetsAt">The supplied reset moment.</param>
    private string ResetText(DateTimeOffset resetsAt)
        => string.Format(
            EffectiveCulture,
            Loc["TmUsageMeter_ResetsAt"],
            resetsAt.ToString("d", EffectiveCulture));

    /// <summary>The root classes: the base class plus the state, the compact modifier and the host's own.</summary>
    private string RootClass
    {
        get
        {
            var classes = "tm-usage-meter";
            if (Compact)
            {
                classes += " tm-usage-meter--compact";
            }

            if (_state == UsageMeterState.Warning)
            {
                classes += " tm-usage-meter--warning";
            }
            else if (_state == UsageMeterState.Critical)
            {
                classes += " tm-usage-meter--critical";
            }

            return string.IsNullOrEmpty(Class) ? classes : classes + " " + Class;
        }
    }

    /// <summary>The segment classes: the base class, the fallback tone or the remainder marker.</summary>
    /// <param name="segment">The segment to paint.</param>
    private static string SegmentClass(UsageSegment segment)
        => segment.IsRemainder
            ? "tm-usage-meter__segment tm-usage-meter__segment--remainder"
            : "tm-usage-meter__segment " + SegmentToneClass(segment.ToneIndex);

    /// <summary>The segment's inline style: its weight of the filled part, plus its colour when valid.</summary>
    /// <param name="segment">The segment to paint.</param>
    private static string SegmentStyle(UsageSegment segment)
    {
        var style = "flex-grow:" + segment.Weight.ToString("0.####", CultureInfo.InvariantCulture);
        return segment.Color is null ? style : style + "; background-color: " + segment.Color;
    }

    /// <summary>The swatch classes of one table row.</summary>
    /// <param name="row">The row to paint.</param>
    private static string SwatchClass(UsageRow row)
        => row.IsRemainder
            ? "tm-usage-meter__swatch tm-usage-meter__swatch--remainder"
            : "tm-usage-meter__swatch " + SwatchToneClass(row.ToneIndex);

    /// <summary>The row's inline swatch style, or null when the row uses a fallback tone.</summary>
    /// <param name="row">The row to paint.</param>
    private static string? SwatchStyle(UsageRow row)
        => row.Color is null ? null : "background-color: " + row.Color;

    /// <summary>The row classes of the category table.</summary>
    /// <param name="row">The row to classify.</param>
    private static string RowClass(UsageRow row)
        => row.IsRemainder
            ? "tm-usage-meter__row tm-usage-meter__row--other"
            : "tm-usage-meter__row";

    /// <summary>The fallback token tone of a segment — the rotating categorical palette.</summary>
    /// <param name="tone">The zero-based tone index.</param>
    private static string SegmentToneClass(int tone) => tone switch
    {
        1 => "tm-usage-meter__segment--tone-2",
        2 => "tm-usage-meter__segment--tone-3",
        3 => "tm-usage-meter__segment--tone-4",
        4 => "tm-usage-meter__segment--tone-5",
        5 => "tm-usage-meter__segment--tone-6",
        _ => "tm-usage-meter__segment--tone-1"
    };

    /// <summary>The fallback token tone of a swatch — the same tone its segment paints.</summary>
    /// <param name="tone">The zero-based tone index.</param>
    private static string SwatchToneClass(int tone) => tone switch
    {
        1 => "tm-usage-meter__swatch--tone-2",
        2 => "tm-usage-meter__swatch--tone-3",
        3 => "tm-usage-meter__swatch--tone-4",
        4 => "tm-usage-meter__swatch--tone-5",
        5 => "tm-usage-meter__swatch--tone-6",
        _ => "tm-usage-meter__swatch--tone-1"
    };

    /// <summary>
    /// The category's colour when it is a valid CSS colour, otherwise null (the row then falls back to
    /// its token tone). A class name and any payload carrying a declaration separator is refused, so
    /// nothing arbitrary reaches the inline style attribute.
    /// </summary>
    /// <param name="category">The category.</param>
    private static string? ColorOf(UsageCategory category)
        => !string.IsNullOrWhiteSpace(category.Color) && CssColorValue.IsValid(category.Color)
            ? category.Color.Trim()
            : null;
}
