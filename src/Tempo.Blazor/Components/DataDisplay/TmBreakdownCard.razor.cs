using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.DataDisplay;

/// <summary>
/// The logic half of <see cref="TmBreakdownCard"/>: the shares are computed in
/// <see cref="OnParametersSet"/> (so the render is a pure projection), the period switcher is mapped
/// from the host's <see cref="SelectOption{TValue}"/> list, and the FLIP reorder is driven by two
/// consecutive renders — the shifted state and then the transitioned state — which is why this
/// component needs no JavaScript at all.
/// </summary>
public partial class TmBreakdownCard : TmComponentBase
{
    /// <summary>The computed shares of the current render, biggest first.</summary>
    private IReadOnlyList<BreakdownShare> _shares = [];

    /// <summary>The total the percentages were measured against.</summary>
    private double _total;

    /// <summary>The amount the declared total leaves unaccounted for (zero when there is none).</summary>
    private double _remainderValue;

    /// <summary>The percentage of the remainder (zero when there is none).</summary>
    private int _remainderPercent;

    /// <summary>The period options mapped once per parameter set.</summary>
    private List<SegmentedOption<string>> _periodSegments = [];

    /// <summary>
    /// The unique row key of every share of the current render, keyed by share REFERENCE — a
    /// <see cref="BreakdownShare"/> is a record, so two parts that are equal by value would otherwise
    /// share one dictionary slot.
    /// </summary>
    private Dictionary<BreakdownShare, string> _rowKeys = new(ReferenceEqualityComparer.Instance);

    /// <summary>The row keys rendered last time, used to decide whether a FLIP is needed.</summary>
    private List<string>? _previousOrder;

    /// <summary>
    /// The shift of every row that moved in the last reorder, in row units, keyed by row key. Cleared
    /// (or replaced) on the next reorder; a row that did not move is absent and gets no animation class.
    /// </summary>
    private Dictionary<string, int> _shifts = [];

    /// <summary>
    /// Bumped on every reorder. The row key includes it, so the keyframe animation restarts on a
    /// second reorder even though the class is already present on a row that moved before.
    /// </summary>
    private int _flipVersion;

    /// <summary>
    /// The parts of the breakdown. The rows render biggest first; a negative value counts as zero for
    /// the percentage but the row itself stays visible, so the host's data is never silently dropped.
    /// </summary>
    [Parameter]
    public IReadOnlyList<BreakdownPart> Parts { get; set; } = [];

    /// <summary>
    /// An optional heading above the card. It also NAMES the table (as its caption), so a reader
    /// hears what the numbers are a breakdown of.
    /// </summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>
    /// The accessible name used when no <see cref="Title"/> is rendered. Null means the localized
    /// default ("Breakdown").
    /// </summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>
    /// The declared whole the parts are a share of. Default <see langword="null"/> = the parts' sum,
    /// which makes the rounded shares add up to exactly 100 %. A declared total BIGGER than the sum
    /// adds an explicit, localized "Other" row and segment for the rest (so the visible shares still
    /// add up to 100 %); a total BELOW the sum cannot be the whole, so it is ignored and the sum is
    /// used — a breakdown never renders a share above 100 %.
    /// </summary>
    [Parameter]
    public double? Total { get; set; }

    /// <summary>
    /// The .NET numeric format string of every value (parts and the total). Default <c>"N0"</c>
    /// (whole, group-separated numbers). The share column is always a culture-formatted whole
    /// percentage.
    /// </summary>
    [Parameter]
    public string ValueFormat { get; set; } = "N0";

    /// <summary>
    /// The culture that formats the values and the shares. Default <see langword="null"/> = the
    /// current culture at render time, so the card follows the request culture without being told.
    /// </summary>
    [Parameter]
    public CultureInfo? Culture { get; set; }

    /// <summary>Renders the total row above the strip. Default <see langword="true"/>.</summary>
    [Parameter]
    public bool ShowTotal { get; set; } = true;

    /// <summary>Renders the proportional strip. Default <see langword="true"/>.</summary>
    [Parameter]
    public bool ShowBar { get; set; } = true;

    /// <summary>
    /// Animates the rows into their new places when the sorted order changes (a FLIP transition).
    /// Default <see langword="true"/>; the motion is suppressed by
    /// <c>prefers-reduced-motion: reduce</c> in CSS regardless of this switch, and the table's data
    /// order (which is what a reader gets) never depends on the animation.
    /// </summary>
    [Parameter]
    public bool AnimateReorder { get; set; } = true;

    /// <summary>
    /// Renders the loading placeholder: skeletons plus a visually hidden localized "loading" text,
    /// with <c>aria-busy="true"</c> on the root. No stale data stays on screen.
    /// </summary>
    [Parameter]
    public bool IsLoading { get; set; }

    /// <summary>
    /// The periods a host can switch between, rendered as a single-choice segmented control when at
    /// least one option is supplied. The card does not interpret the values — it raises
    /// <see cref="PeriodChanged"/> and the host loads the matching data.
    /// </summary>
    [Parameter]
    public IReadOnlyList<SelectOption<string>> PeriodOptions { get; set; } = [];

    /// <summary>
    /// The currently selected period (one of the <see cref="PeriodOptions"/> values), two-way bound
    /// with <see cref="PeriodChanged"/>. Null renders no selection.
    /// </summary>
    [Parameter]
    public string? Period { get; set; }

    /// <summary>Fires with the newly selected period value.</summary>
    [Parameter]
    public EventCallback<string?> PeriodChanged { get; set; }

    /// <summary>
    /// The label above the period switcher. Null means the localized default ("Period") — the
    /// switcher is always named for a reader, never only by its position.
    /// </summary>
    [Parameter]
    public string? PeriodLabel { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>The culture the numeric parts are formatted with — the parameter when set, the current culture otherwise.</summary>
    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    /// <summary>
    /// Computes the shares for the current parameters and decides whether the new order needs a FLIP.
    /// A parameter set that keeps the order (a value change inside one row, a period that does not
    /// reorder anything, a loading toggle) starts no animation at all.
    /// </summary>
    protected override void OnParametersSet()
    {
        var result = BreakdownMath.Compute(Parts, Total);
        _shares = result.Shares;
        _total = result.Total;
        _remainderValue = result.RemainderValue;
        _remainderPercent = result.RemainderPercent;

        _periodSegments = (PeriodOptions ?? [])
            .Where(option => option is not null)
            .Select(option => new SegmentedOption<string>
            {
                Label = option.Label,
                Value = option.Value,
                IsDisabled = option.IsDisabled,
                Icon = option.Icon
            })
            .ToList();

        var order = BuildRowKeys(_shares);
        if (_previousOrder is { Count: > 0 }
            && order.Count > 0
            && AnimateReorder
            && !IsLoading
            && !order.SequenceEqual(_previousOrder, StringComparer.Ordinal))
        {
            var shifts = BreakdownFlip.Shifts(_previousOrder, order);
            if (shifts.Values.Any(shift => shift != 0))
            {
                _shifts = shifts;
                _flipVersion++;
            }
        }

        _previousOrder = order;
    }

    /// <summary>
    /// Computes one unique row key per share (filling <see cref="_rowKeys"/>) and returns them in the
    /// current order, so the diff, the FLIP shifts and the lookups all speak the same key.
    /// <para>
    /// A host id is used only when it is non-empty AND still unused. <see cref="BreakdownPart.Id"/>
    /// defaults to <see cref="string.Empty"/>, so a host that writes parts with just
    /// <c>Label</c>/<c>Value</c> gives every row the same <c>@key</c>; Blazor then throws "More than
    /// one sibling of element 'tbody' has the same key value" and the whole card fails to render. A
    /// duplicate id is a host data defect this card TOLERATES instead of propagating: the first
    /// occurrence keeps the id and every other one gets a key in a private U+0001 namespace, so the
    /// rows render and the reorder maths stay defined. A row that has no unique id is not tracked
    /// across a reorder (its positional key never moves), which is why <c>Id</c> should be unique.
    /// </para>
    /// </summary>
    /// <param name="shares">The shares of the current render, already in display order.</param>
    private List<string> BuildRowKeys(IReadOnlyList<BreakdownShare> shares)
    {
        var keys = new List<string>(shares.Count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        _rowKeys = new Dictionary<BreakdownShare, string>(shares.Count, ReferenceEqualityComparer.Instance);

        for (var index = 0; index < shares.Count; index++)
        {
            var share = shares[index];
            var candidate = share.Part.Id;
            string key;

            if (!string.IsNullOrEmpty(candidate) && used.Add(candidate))
            {
                key = candidate;
            }
            else
            {
                // A derived key lives in its own namespace, so it can never collide with a host id —
                // not even with an id of a later part that happens to look like a position.
                key = $"\u0001{index}";
                var attempt = 0;
                while (!used.Add(key))
                {
                    key = $"\u0001{index}\u0001{++attempt}";
                }
            }

            keys.Add(key);
            _rowKeys[share] = key;
        }

        return keys;
    }

    /// <summary>Raises <see cref="PeriodChanged"/> and keeps the local selection in sync.</summary>
    /// <param name="value">The newly selected period.</param>
    private async Task OnPeriodChangedAsync(string? value)
    {
        Period = value;
        await PeriodChanged.InvokeAsync(value);
    }

    /// <summary>Formats one value (a part or the total) with <see cref="ValueFormat"/> and <see cref="EffectiveCulture"/>.</summary>
    /// <param name="value">The amount.</param>
    private string FormatValue(double value)
        => value.ToString(string.IsNullOrWhiteSpace(ValueFormat) ? "N0" : ValueFormat, EffectiveCulture);

    /// <summary>
    /// Formats one share as a whole percentage using the culture's own percent pattern (so Czech
    /// renders <c>34&nbsp;%</c> and English <c>34%</c>). The value passed in is a percentage point
    /// count, which is why it is divided by 100 for the format.
    /// </summary>
    /// <param name="percent">The rounded percentage points.</param>
    private string FormatShare(int percent) => TrendDelta.FormatPercent(percent, EffectiveCulture);

    /// <summary>The <c>tr</c> classes: the base row plus the FLIP animation for a row that moved.</summary>
    /// <param name="share">The row's share.</param>
    private string RowClass(BreakdownShare share)
    {
        var classes = "tm-breakdown-card__row";
        if (AnimateReorder && ShiftOf(share) != 0)
        {
            classes += " tm-breakdown-card__row--flip";
        }

        return classes;
    }

    /// <summary>
    /// The Blazor key of a row: its computed unique row key (see <see cref="BuildRowKeys"/>), versioned
    /// after a reorder so the animation restarts. The version rides in its own U+0002 namespace, so a
    /// versioned key can never collide with another row's key.
    /// </summary>
    /// <param name="share">The row's share.</param>
    private string KeyOf(BreakdownShare share)
    {
        var key = _rowKeys.TryGetValue(share, out var value) ? value : string.Empty;
        return _flipVersion == 0 ? key : $"\u0002{_flipVersion}\u0002{key}";
    }

    /// <summary>
    /// The inline <c>--tm-breakdown-card-row-shift</c> of a moved row: the row starts as many of its
    /// own heights away as it moved, and the keyframe animation slides it home.
    /// </summary>
    /// <param name="share">The row's share.</param>
    private string? RowShiftStyle(BreakdownShare share)
    {
        var shift = ShiftOf(share);
        return shift == 0 ? null : $"--tm-breakdown-card-row-shift:{shift * 100}%;";
    }

    /// <summary>The shift of one row in row units, or zero when it did not move.</summary>
    /// <param name="share">The row's share.</param>
    private int ShiftOf(BreakdownShare share)
        => AnimateReorder
           && _rowKeys.TryGetValue(share, out var key)
           && _shifts.TryGetValue(key, out var shift)
            ? shift
            : 0;

    /// <summary>The strip segment classes: the base class plus the fallback token tone of its position.</summary>
    /// <param name="share">The share the segment paints.</param>
    private static string SegmentClass(BreakdownShare share) => share.ToneIndex switch
    {
        1 => "tm-breakdown-card__segment tm-breakdown-card__segment--tone-1",
        2 => "tm-breakdown-card__segment tm-breakdown-card__segment--tone-2",
        3 => "tm-breakdown-card__segment tm-breakdown-card__segment--tone-3",
        4 => "tm-breakdown-card__segment tm-breakdown-card__segment--tone-4",
        5 => "tm-breakdown-card__segment tm-breakdown-card__segment--tone-5",
        _ => "tm-breakdown-card__segment tm-breakdown-card__segment--tone-6"
    };

    /// <summary>The row swatch classes: the base class plus the same fallback tone the segment uses.</summary>
    /// <param name="share">The share the swatch belongs to.</param>
    private static string SwatchClass(BreakdownShare share) => share.ToneIndex switch
    {
        1 => "tm-breakdown-card__swatch tm-breakdown-card__swatch--tone-1",
        2 => "tm-breakdown-card__swatch tm-breakdown-card__swatch--tone-2",
        3 => "tm-breakdown-card__swatch tm-breakdown-card__swatch--tone-3",
        4 => "tm-breakdown-card__swatch tm-breakdown-card__swatch--tone-4",
        5 => "tm-breakdown-card__swatch tm-breakdown-card__swatch--tone-5",
        _ => "tm-breakdown-card__swatch tm-breakdown-card__swatch--tone-6"
    };

    /// <summary>
    /// The segment's inline style: its proportion, plus the host's own colour when that colour is a
    /// valid CSS colour. The validated value wins over the fallback tone because it is inline.
    /// </summary>
    /// <param name="share">The share the segment paints.</param>
    private static string SegmentStyle(BreakdownShare share)
    {
        var style = $"flex-grow:{share.Percent.ToString(CultureInfo.InvariantCulture)}";
        return ColorOf(share.Part) is { } color ? style + $";background-color:{color}" : style;
    }

    /// <summary>The remainder segment's inline style: whatever the parts do not account for.</summary>
    private string RemainderStyle
        => $"flex-grow:{_remainderPercent.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The swatch's inline style, or null when the part uses the fallback tone.</summary>
    /// <param name="share">The share the swatch belongs to.</param>
    private static string? SwatchStyle(BreakdownShare share)
        => ColorOf(share.Part) is { } color ? $"background-color:{color}" : null;

    /// <summary>
    /// The part's colour when it is a valid CSS colour, or null. A CSS class name (the pre-2.9.0
    /// mistake) and any payload carrying a declaration separator are refused, so nothing arbitrary
    /// reaches the inline style attribute.
    /// </summary>
    /// <param name="part">The part.</param>
    private static string? ColorOf(BreakdownPart part)
        => !string.IsNullOrWhiteSpace(part.Color) && CssColorValue.IsValid(part.Color) ? part.Color.Trim() : null;
}
