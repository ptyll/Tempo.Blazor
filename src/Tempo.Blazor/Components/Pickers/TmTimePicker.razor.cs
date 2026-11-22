using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Pickers;

/// <summary>
/// Time picker wrapping <c>TmTimeInput</c> with label, placeholder, clear button and validation
/// styling. Additive <see cref="Presentation"/> keeps today's segments DOM by default; Popup/Auto
/// compose <c>TmTimeSelector</c> into a <c>TmOverlayPanel</c> dialog (sheet on a mobile viewport).
/// </summary>
public partial class TmTimePicker
{
    private readonly string _labelId = $"tm-time-picker-label-{Guid.NewGuid():N}";
    private readonly string _generatedInputId = $"tm-time-picker-input-{Guid.NewGuid():N}";
    private readonly string _triggerId = $"tm-time-picker-trigger-{Guid.NewGuid():N}";
    private readonly string _valueId = $"tm-time-picker-value-{Guid.NewGuid():N}";
    private readonly string _overlayId = $"tm-time-picker-overlay-{Guid.NewGuid():N}";

    private bool _isOpen;
    private bool _confirming;
    private TimeOnly? _draft;
    private ElementReference _triggerRef;

    /// <summary>The bound time of day.</summary>
    [Parameter] public TimeOnly? Value { get; set; }

    /// <summary>Fires when the committed time changes (live, or on Done in OnConfirm).</summary>
    [Parameter] public EventCallback<TimeOnly?> ValueChanged { get; set; }

    /// <summary>
    /// Expression identifying the bound model field. Blazor supplies it automatically with
    /// <c>@bind-Value</c>; it links the nested input/selector to <c>EditContext.NotifyFieldChanged</c>.
    /// </summary>
    [Parameter] public Expression<Func<TimeOnly?>>? ValueExpression { get; set; }

    /// <summary>Inclusive lower bound forwarded to the nested input/selector.</summary>
    [Parameter] public TimeOnly? MinTime { get; set; }

    /// <summary>Inclusive upper bound forwarded to the nested input/selector.</summary>
    [Parameter] public TimeOnly? MaxTime { get; set; }

    /// <summary>When true, a seconds segment / spinbutton is rendered. Defaults to false.</summary>
    [Parameter] public bool ShowSeconds { get; set; }

    /// <summary>Placeholder shown while <see cref="Value"/> is null.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>Disables the nested input and the clock trigger. Distinct from <see cref="ReadOnly"/>.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Renders the nested <c>TmTimeInput</c> read-only and blocks the overlay from opening.
    /// Never assigned inside the picker. Defaults to false.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// 12-hour versus 24-hour clock forwarded to <c>TmTimeInput</c> / <c>TmTimeSelector</c>. Default
    /// <see cref="TimeHourCycle.Auto"/> follows <see cref="Culture"/>.
    /// </summary>
    [Parameter] public TimeHourCycle HourCycle { get; set; } = TimeHourCycle.Auto;

    /// <summary>
    /// Culture used to resolve <see cref="HourCycle"/>.<see cref="TimeHourCycle.Auto"/>.
    /// Null uses <see cref="CultureInfo.CurrentCulture"/>.
    /// </summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Marks the label as required. Does not hide the clear button.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>
    /// Hides the "✕" clear button that is otherwise rendered while a <see cref="Value"/> is set and the picker is
    /// not <see cref="Disabled"/>. Parity with <c>TmDateTimePicker.HideClear</c>. The value stays editable segment by
    /// segment; only the one-click clear is removed. Defaults to <c>false</c>. <see cref="Required"/> does not hide
    /// the button implicitly (same as <c>TmDatePicker</c> / <c>TmDateTimePicker</c>) — a host with a mandatory field
    /// passes <c>HideClear="true"</c> explicitly.
    /// </summary>
    [Parameter] public bool HideClear { get; set; }

    /// <summary>Visible label. Also names the overlay dialog when <see cref="SheetTitle"/> is unset.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// How the picker presents. Default <see cref="TimePickerPresentation.Segments"/> keeps today's
    /// <c>TmTimeInput</c> DOM with no overlay. Popup adds a clock trigger that opens
    /// <c>TmTimeSelector</c> in a dialog. Auto is a trigger+sheet on a mobile viewport and Popup otherwise.
    /// </summary>
    [Parameter] public TimePickerPresentation Presentation { get; set; } = TimePickerPresentation.Segments;

    /// <summary>
    /// When the overlay writes <see cref="ValueChanged"/>. Default <see cref="TimeCommitMode.Auto"/>:
    /// a sheet holds a draft until Done; a popover commits live. <see cref="TimeCommitMode.OnConfirm"/>
    /// always drafts (popover then renders its own Done). Close without Done discards the draft.
    /// </summary>
    [Parameter] public TimeCommitMode CommitMode { get; set; } = TimeCommitMode.Auto;

    /// <summary>
    /// Layout used to resolve Auto presentation and forwarded to the overlay. Auto follows the
    /// viewport cascade; a forced value never measures, so a test can pin mobile/desktop.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>
    /// Visible title of the mobile sheet header. Falls back to <see cref="Label"/>, then the
    /// localized <c>TmTimePicker_SheetTitle</c>.
    /// </summary>
    [Parameter] public string? SheetTitle { get; set; }

    /// <summary>
    /// Stable id of the picker root. When set and <see cref="InputId"/> is unset, the hours
    /// segment uses <c>{Id}-input</c>.
    /// </summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>
    /// Optional id applied to the hours segment so a parent <c>&lt;label for&gt;</c> can target it.
    /// Wins over the <c>{Id}-input</c> default. When both this and <see cref="Id"/> are unset the
    /// generated <c>tm-time-picker-input-{{guid}}</c> id is kept (today's DOM).
    /// </summary>
    [Parameter] public string? InputId { get; set; }

    /// <summary>
    /// Extra ids referenced from every segment / spinbutton <c>aria-describedby</c>. Forwarded to
    /// the nested input and selector.
    /// </summary>
    [Parameter] public string? AriaDescribedBy { get; set; }

    /// <summary>Minute step forwarded to the overlay selector. Defaults to 1.</summary>
    [Parameter] public int MinuteStep { get; set; } = 1;

    /// <summary>
    /// When true, an off-step bound value is marked invalid and never rewritten (forwarded to the
    /// overlay selector). Defaults to false.
    /// </summary>
    [Parameter] public bool EnforceStep { get; set; }

    /// <summary>Date used with <see cref="TimeZone"/> for DST gap/ambiguity. Both must be set.</summary>
    [Parameter] public DateOnly? ReferenceDate { get; set; }

    /// <summary>Time zone used with <see cref="ReferenceDate"/> for DST gap/ambiguity. Both must be set.</summary>
    [Parameter] public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Which occurrence of an ambiguous civil time to prefer. Defaults to earlier.</summary>
    [Parameter] public AmbiguousTimeResolution AmbiguousTimeResolution { get; set; } = AmbiguousTimeResolution.Earlier;

    /// <summary>Raised with the chosen offset when the user resolves an ambiguous time in the overlay.</summary>
    [Parameter] public EventCallback<TimeSpan> AmbiguousOffsetChanged { get; set; }

    /// <summary>Additional HTML attributes. <c>aria-*</c> land on the group; everything else on the root.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The viewport's layout. Auto presentation (sheet vs popover) resolves from this cascade.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Viewport { get; set; }

    private TmLayoutMode ResolvedLayout
        => TmLayout.Resolve(LayoutMode, Viewport, measured: null, TmLayoutMode.Desktop);

    private bool UsesOverlay => Presentation is TimePickerPresentation.Popup or TimePickerPresentation.Auto;

    private bool IsMobileAuto => Presentation == TimePickerPresentation.Auto && ResolvedLayout == TmLayoutMode.Mobile;

    private bool ShowSegments => !IsMobileAuto;

    private bool IsSheetPresentation => UsesOverlay && ResolvedLayout == TmLayoutMode.Mobile;

    private bool UsesOnConfirm => CommitMode == TimeCommitMode.OnConfirm
        || (CommitMode == TimeCommitMode.Auto && IsSheetPresentation);

    private TimeOnly? SelectorValue => UsesOnConfirm ? _draft : Value;

    private string EffectiveInputId => !string.IsNullOrEmpty(InputId)
        ? InputId!
        : !string.IsNullOrEmpty(Id)
            ? $"{Id}-input"
            : _generatedInputId;

    private string LabelFor => ShowSegments ? EffectiveInputId : _triggerId;

    private string EffectiveSheetTitle
        => FirstNonEmpty(SheetTitle, Label, Loc["TmTimePicker_SheetTitle"])!;

    private string? TriggerAriaLabel => IsMobileAuto ? null : Loc["TmTimePicker_OpenSelector"];

    private string? TriggerLabelledBy => IsMobileAuto
        ? (string.IsNullOrEmpty(Label) ? _valueId : $"{_labelId} {_valueId}")
        : null;

    private string DisplayedValue
    {
        get
        {
            if (Value is { } value)
            {
                return TimeOfDayFormat.Format(value, HourCycle, Culture);
            }

            return Placeholder
                ?? (ShowSeconds ? Loc["TmTimePicker_PlaceholderSeconds"] : Loc["TmTimePicker_Placeholder"]);
        }
    }

    private bool IsOutOfRange =>
        Value.HasValue &&
        ((MinTime.HasValue && Value.Value < MinTime.Value) ||
         (MaxTime.HasValue && Value.Value > MaxTime.Value));

    /// <summary>Consumer <c>aria-*</c> attributes target the input group; everything else the root.</summary>
    private IReadOnlyDictionary<string, object>? AriaAttributes =>
        AdditionalAttributes is null
            ? null
            : AdditionalAttributes
                .Where(kv => kv.Key.StartsWith("aria-", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

    private IReadOnlyDictionary<string, object>? RootAttributes =>
        AdditionalAttributes is null
            ? null
            : AdditionalAttributes
                .Where(kv => !kv.Key.StartsWith("aria-", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kv => kv.Key, kv => kv.Value);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private Task OnTimeChanged(TimeOnly? t) => ValueChanged.InvokeAsync(t);

    private Task ClearValue() => ValueChanged.InvokeAsync(null);

    private Task ToggleOverlayAsync()
    {
        if (Disabled || ReadOnly)
        {
            return Task.CompletedTask;
        }

        if (_isOpen)
        {
            return CloseWithoutCommitAsync();
        }

        _draft = Value;
        _confirming = false;
        _isOpen = true;
        return Task.CompletedTask;
    }

    private Task OnSelectorChanged(TimeOnly? t)
    {
        if (UsesOnConfirm)
        {
            _draft = t;
            StateHasChanged();
            return Task.CompletedTask;
        }

        return ValueChanged.InvokeAsync(t);
    }

    private async Task OnSheetDoneAsync()
    {
        _confirming = true;
        await CommitDraftAsync();
    }

    private async Task ConfirmPopoverAsync()
    {
        _confirming = true;
        await CommitDraftAsync();
        _isOpen = false;
        _confirming = false;
    }

    private Task OnOverlayOpenChanged(bool open)
    {
        if (open)
        {
            _isOpen = true;
            return Task.CompletedTask;
        }

        if (!_confirming)
        {
            _draft = Value;
        }

        _confirming = false;
        _isOpen = false;
        return Task.CompletedTask;
    }

    private Task CloseWithoutCommitAsync()
    {
        _confirming = false;
        _draft = Value;
        _isOpen = false;
        return Task.CompletedTask;
    }

    private Task CommitDraftAsync() => ValueChanged.InvokeAsync(_draft);
}
