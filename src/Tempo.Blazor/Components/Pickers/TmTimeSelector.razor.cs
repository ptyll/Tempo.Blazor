using System.Globalization;
using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Pickers;

/// <summary>
/// Inline APG spinbutton time-of-day selector: hours/minutes/(seconds) with +/−, optional AM/PM,
/// and a JS module for keys, typeahead, auto-repeat and fit. The hour/minute grid, presets and
/// picker popup are later phases; this surface still evaluates <see cref="ShowGrid"/> collapse
/// in markup (square + seconds → <see cref="TimeSelectorGrid.None"/>).
/// </summary>
public partial class TmTimeSelector
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";
    private const int SquareWidthPx = 640;
    private const int SquareHeightPx = 480;

    private readonly string _generatedId = $"tm-time-sel-{Guid.NewGuid():N}"[..20];

    private ElementReference _root;
    private ElementReference _hoursRef;
    private ElementReference _minutesRef;
    private ElementReference _secondsRef;
    private DotNetObjectReference<TmTimeSelector>? _self;
    private Task<IJSObjectReference>? _moduleTask;
    private bool _attached;
    private bool _keyboardOpen;
    private TimeSelectorFit? _measuredFit;
    private string _liveText = string.Empty;
    private string? _liveTextOverride;
    private TimeOnly? _draft;
    private bool _hasDraft;
    private TimeOnly? _lastSeenValue;
    private TimeProvider? _clock;
    private EditContext? _subscribedEditContext;
    private FieldIdentifier? _fieldIdentifier;
    private Expression<Func<TimeOnly?>>? _seenValueExpression;

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>
    /// Clock behind the empty-value "start from now" path. Resolved through
    /// <see cref="IServiceProvider"/> so a consumer that never registers a
    /// <see cref="TimeProvider"/> still gets <see cref="TimeProvider.System"/>.
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    /// <summary>The bound time of day. Null renders <c>--</c> in the readout; +/− starts from now rounded to the step (or <see cref="MinTime"/>).</summary>
    [Parameter] public TimeOnly? Value { get; set; }

    /// <summary>Fires on every accepted step, typeahead or period change, including a value outside <see cref="MinTime"/>/<see cref="MaxTime"/>.</summary>
    [Parameter] public EventCallback<TimeOnly?> ValueChanged { get; set; }

    /// <summary>
    /// Expression identifying the bound model field. Blazor supplies it automatically with
    /// <c>@bind-Value</c>; it is what links the selector to <c>EditContext.NotifyFieldChanged</c>.
    /// </summary>
    [Parameter] public Expression<Func<TimeOnly?>>? ValueExpression { get; set; }

    /// <summary>Inclusive lower bound. A value below this bound stays on screen and is announced as invalid.</summary>
    [Parameter] public TimeOnly? MinTime { get; set; }

    /// <summary>Inclusive upper bound. A value above this bound stays on screen and is announced as invalid.</summary>
    [Parameter] public TimeOnly? MaxTime { get; set; }

    /// <summary>When true, <see cref="MinTime"/> after <see cref="MaxTime"/> is read as a range that crosses midnight.</summary>
    [Parameter] public bool AllowOvernightRange { get; set; }

    /// <summary>Minute step. Must be a positive divisor of 60 other than 60. Defaults to 1.</summary>
    [Parameter] public int MinuteStep { get; set; } = 1;

    /// <summary>Grid minute step. Null uses <c>max(MinuteStep, 5)</c>. Unused until the grid phase.</summary>
    [Parameter] public int? GridMinuteStep { get; set; }

    /// <summary>When true, typed values snap onto <see cref="MinuteStep"/>. Defaults to false.</summary>
    [Parameter] public bool EnforceStep { get; set; }

    /// <summary>When true, a seconds spinbutton is rendered after minutes. Defaults to false.</summary>
    [Parameter] public bool ShowSeconds { get; set; }

    /// <summary>
    /// 12-hour versus 24-hour clock. Default <see cref="TimeHourCycle.Auto"/> follows
    /// <see cref="Culture"/> (or <see cref="CultureInfo.CurrentCulture"/>).
    /// </summary>
    [Parameter] public TimeHourCycle HourCycle { get; set; } = TimeHourCycle.Auto;

    /// <summary>Culture used to resolve <see cref="HourCycle"/>.<see cref="TimeHourCycle.Auto"/> and to format bounds. Null uses <see cref="CultureInfo.CurrentCulture"/>.</summary>
    [Parameter] public CultureInfo? Culture { get; set; }

    /// <summary>Optional preset chips. Unused until the presets phase.</summary>
    [Parameter] public IReadOnlyList<TimePreset>? Presets { get; set; }

    /// <summary>When true, a "Now" chip is offered. Unused until the presets phase.</summary>
    [Parameter] public bool ShowNowPreset { get; set; }

    /// <summary>Host-owned recent times. Unused until the presets phase.</summary>
    [Parameter] public IReadOnlyList<TimeOnly>? RecentTimes { get; set; }

    /// <summary>
    /// Whether the hour/minute grid would show. This phase does not render the grid, but square +
    /// seconds still collapses the effective value to <see cref="TimeSelectorGrid.None"/>.
    /// </summary>
    [Parameter] public TimeSelectorGrid ShowGrid { get; set; } = TimeSelectorGrid.Auto;

    /// <summary>
    /// Typeahead in the spinbuttons and the "Type the time" switch on a coarse pointer. The switch
    /// is never offered in a square fit (decision 15). Defaults to true.
    /// </summary>
    [Parameter] public bool AllowTyping { get; set; } = true;

    /// <summary>Vertical swipe on a spinbutton. Unused until the swipe phase. Defaults to false.</summary>
    [Parameter] public bool EnableSwipe { get; set; }

    /// <summary>Date used with <see cref="TimeZone"/> for DST gap/ambiguity. Both must be set.</summary>
    [Parameter] public DateOnly? ReferenceDate { get; set; }

    /// <summary>Time zone used with <see cref="ReferenceDate"/> for DST gap/ambiguity. Both must be set.</summary>
    [Parameter] public TimeZoneInfo? TimeZone { get; set; }

    /// <summary>Which occurrence of an ambiguous civil time to prefer. Defaults to earlier.</summary>
    [Parameter] public AmbiguousTimeResolution AmbiguousTimeResolution { get; set; } = AmbiguousTimeResolution.Earlier;

    /// <summary>Raised with the chosen offset when the user resolves an ambiguous time.</summary>
    [Parameter] public EventCallback<TimeSpan> AmbiguousOffsetChanged { get; set; }

    /// <summary>Disables every spinbutton and stepper and applies <c>tm-time-selector--disabled</c>. Distinct from <see cref="ReadOnly"/>.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Renders the time visible and focusable but not editable: +/− are hidden (not merely
    /// disabled). Never assigned inside the component. Defaults to false.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>External invalid state (host validation). Combined with the selector's own min/max check.</summary>
    [Parameter] public bool Invalid { get; set; }

    /// <summary>
    /// Error message rendered below the readout while invalid. Host text wins over the localized
    /// min/max message. Null or empty with <see cref="Invalid"/> true is the supported way to
    /// surface a message owned by a surrounding <c>TmFormField</c>.
    /// </summary>
    [Parameter] public string? ErrorText { get; set; }

    /// <summary><c>aria-label</c> of the group. Defaults to the localized "Time picker" label; ignored when <see cref="AriaLabelledBy"/> is set.</summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary><c>aria-labelledby</c> of the group. When set, <see cref="AriaLabel"/> is not rendered.</summary>
    [Parameter] public string? AriaLabelledBy { get; set; }

    /// <summary>Extra ids referenced from every spinbutton's <c>aria-describedby</c>. Joined with the selector's own error id and the surrounding <c>TmFormField</c> cascade.</summary>
    [Parameter] public string? AriaDescribedBy { get; set; }

    /// <summary>
    /// Layout of the wrapping observer. <see cref="TmLayoutMode.Auto"/> measures; any other value
    /// pins the mode and never imports the observer script. Never pair this with
    /// <see cref="ResolvedLayoutChanged"/> as a two-way bind.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Mode rendered before the first measurement. Defaults to desktop.</summary>
    [Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;

    /// <summary>Raised when the resolved layout changes. Never pair with a two-way bind of <see cref="LayoutMode"/>.</summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <summary>
    /// Fine layout. <see cref="TimeSelectorFit.Auto"/> is measured (square when width &lt; 640 and
    /// height &lt; 480); <see cref="TimeSelectorFit.Square"/> / <see cref="TimeSelectorFit.Regular"/>
    /// pin it and win over measurement. Prerender / no JS is regular.
    /// </summary>
    [Parameter] public TimeSelectorFit Fit { get; set; } = TimeSelectorFit.Auto;

    /// <summary>Moves focus to the hours spinbutton after the first render.</summary>
    [Parameter] public bool AutoFocus { get; set; }

    /// <summary>Fires once on a completed change (tap, typeahead with advance, end of auto-repeat), not on every arrow key.</summary>
    [Parameter] public EventCallback<TimeOnly?> OnCommitted { get; set; }

    /// <summary>Optional id used as the prefix of the hours spinbutton (<c>{Id}-hours</c>) and the error element (<c>{Id}-error</c>). When omitted a per-instance id is generated.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>Additional CSS class(es) applied to the group.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Unmatched attributes land on the group.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The <c>EditForm</c>'s edit context, when the selector is used inside one.</summary>
    [CascadingParameter] private EditContext? CascadedEditContext { get; set; }

    /// <summary>Id of the error message a surrounding <c>TmFormField</c> currently renders.</summary>
    [CascadingParameter(Name = TmFormField.ErrorIdCascadingName)]
    private string? FormFieldErrorId { get; set; }

    /// <summary>Ids a surrounding <c>TmFormField</c> publishes for its own help text.</summary>
    [CascadingParameter(Name = TmFormField.DescribedByIdsCascadingName)]
    private string? FormFieldDescribedByIds { get; set; }

    /// <summary>Id of the hours spinbutton (<c>{Id}-hours</c>) so a parent <c>&lt;label for&gt;</c> can target it.</summary>
    public string InputId => SegmentId(TimeSegment.Hours);

    private TimeProvider Clock
        => _clock ??= Services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;

    private CultureInfo EffectiveCulture => Culture ?? CultureInfo.CurrentCulture;

    private TimeHourCycle ResolvedHourCycle => TimeOfDayFormat.ResolveHourCycle(HourCycle, EffectiveCulture);

    private bool Uses12HourClock => ResolvedHourCycle == TimeHourCycle.H12;

    private string EffectiveId => string.IsNullOrEmpty(Id) ? _generatedId : Id!;

    private string ErrorId => $"{EffectiveId}-error";

    private TimeSelectorFit ResolvedFit
        => Fit != TimeSelectorFit.Auto ? Fit : _measuredFit ?? TimeSelectorFit.Regular;

    private string FitAttr => ResolvedFit == TimeSelectorFit.Square ? "square" : "regular";

    private TimeSelectorGrid EffectiveGrid
    {
        get
        {
            if (ResolvedFit == TimeSelectorFit.Square && ShowSeconds)
            {
                return TimeSelectorGrid.None;
            }

            if (ShowGrid == TimeSelectorGrid.Always)
            {
                return TimeSelectorGrid.Always;
            }

            if (ShowGrid == TimeSelectorGrid.None || ResolvedFit == TimeSelectorFit.Square)
            {
                return TimeSelectorGrid.None;
            }

            return TimeSelectorGrid.Auto;
        }
    }

    private string GridAttr => EffectiveGrid switch
    {
        TimeSelectorGrid.None => "none",
        TimeSelectorGrid.Always => "always",
        _ => "auto",
    };

    private bool ShowSteppers => !ReadOnly && !(_keyboardOpen && ResolvedFit == TimeSelectorFit.Square);

    private TimeOnly? ShownValue => _hasDraft ? _draft : Value;

    private bool IsPm => (ShownValue ?? TimeOnly.MinValue).Hour >= 12;

    private IReadOnlyList<SegmentedOption<bool>> PeriodOptions =>
    [
        new() { Label = Loc["TmTimeSelector_Am"], Value = false },
        new() { Label = Loc["TmTimeSelector_Pm"], Value = true },
    ];

    private bool IsOutOfRange
        => ShownValue is { } shown
           && !TimeOfDayFormat.IsInRange(shown, MinTime, MaxTime, AllowOvernightRange);

    private string LiveText => _liveTextOverride ?? _liveText;

    private bool IsInvalid => Invalid || IsOutOfRange || FormFieldErrorId is { Length: > 0 };

    private string? ValidationMessage =>
        CascadedEditContext?.GetValidationMessages(FieldIdentifier).FirstOrDefault();

    private string? ResolvedErrorText
    {
        get
        {
            if (!string.IsNullOrEmpty(ErrorText))
            {
                return ErrorText;
            }

            if (IsOutOfRange)
            {
                var min = FormatValue(MinTime ?? TimeOnly.MinValue);
                var max = FormatValue(MaxTime ?? TimeOnly.MaxValue);
                return Loc["TmTimeSelector_OutOfRange", min, max];
            }

            return ValidationMessage;
        }
    }

    private string? GroupAriaLabel => AriaLabelledBy is null
        ? AriaLabel ?? Loc["TmTimeSelector_AriaLabel"]
        : null;

    private string? SegmentDescribedBy => TmDescribedBy.Join(
        string.IsNullOrEmpty(ResolvedErrorText) ? null : ErrorId,
        FormFieldDescribedByIds,
        FormFieldErrorId,
        AriaDescribedBy);

    private string RootClass
    {
        get
        {
            var classes = "tm-time-selector";
            if (Disabled)
            {
                classes += " tm-time-selector--disabled";
            }

            if (ReadOnly)
            {
                classes += " tm-time-selector--readonly";
            }

            if (IsInvalid)
            {
                classes += " tm-time-selector--invalid";
            }

            if (ShowSeconds)
            {
                classes += " tm-time-selector--with-seconds";
            }

            if (!string.IsNullOrWhiteSpace(Class))
            {
                classes += " " + Class;
            }

            return classes;
        }
    }

    private FieldIdentifier FieldIdentifier
    {
        get
        {
            if (_fieldIdentifier is null && ValueExpression is not null)
            {
                _fieldIdentifier = FieldIdentifier.Create(ValueExpression);
            }

            return _fieldIdentifier ?? new FieldIdentifier(this, nameof(Value));
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        TimeMinuteStep.Validate(MinuteStep);

        if (!ReferenceEquals(ValueExpression, _seenValueExpression))
        {
            _seenValueExpression = ValueExpression;
            _fieldIdentifier = null;
        }

        if (_hasDraft)
        {
            if (Value == _draft)
            {
                _hasDraft = false;
            }
            else if (!Nullable.Equals(Value, _lastSeenValue))
            {
                _hasDraft = false;
            }
        }

        _lastSeenValue = Value;

        if (!ReferenceEquals(CascadedEditContext, _subscribedEditContext))
        {
            DetachValidationStateChanged();
            _fieldIdentifier = null;
            _subscribedEditContext = CascadedEditContext;
            if (_subscribedEditContext is not null)
            {
                _subscribedEditContext.OnValidationStateChanged += HandleValidationStateChanged;
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await AttachAsync();
            if (AutoFocus && !Disabled)
            {
                await FocusSegmentAsync(TimeSegment.Hours);
            }
        }
        else if (_attached)
        {
            await UpdateAsync();
        }
    }

    /// <summary>Moves focus to the hours spinbutton. No-op while <see cref="Disabled"/>.</summary>
    public Task FocusAsync() => FocusSegmentAsync(TimeSegment.Hours);

    /// <summary>Moves focus to <paramref name="segment"/>. No-op while <see cref="Disabled"/>.</summary>
    public async Task FocusSegmentAsync(TimeSegment segment)
    {
        if (Disabled)
        {
            return;
        }

        try
        {
            await SegmentRef(segment).FocusAsync();
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
    }

    /// <summary>
    /// Re-announces the current live / error text. The polite live region is cleared for one
    /// render and then restored, because it only announces changes.
    /// </summary>
    public async Task ShowErrorAsync()
    {
        _liveTextOverride = string.Empty;
        StateHasChanged();
        await Task.Yield();
        _liveTextOverride = null;
        StateHasChanged();
    }

    /// <summary>Called by <c>tm-time-selector.js</c> for arrow / PageUp / PageDown / auto-repeat.</summary>
    [JSInvokable]
    public Task OnKeyStepAsync(string segment, int delta, bool announce)
        => InvokeAsync(() => StepAsync(ParseSegment(segment), delta, announce));

    /// <summary>Called by the module when the software-keyboard heuristic flips.</summary>
    [JSInvokable]
    public Task OnKeyboardChanged(bool open)
        => InvokeAsync(() =>
        {
            if (_keyboardOpen == open)
            {
                return;
            }

            _keyboardOpen = open;
            StateHasChanged();
        });

    /// <summary>
    /// Called by the module with the measured inline-size and the layout height (never the root
    /// block-size). Ignored while the software keyboard is open so fit stays locked.
    /// </summary>
    [JSInvokable]
    public Task OnViewportChanged(double width, double height)
        => InvokeAsync(() =>
        {
            if (_keyboardOpen || Fit != TimeSelectorFit.Auto || width <= 0 || height <= 0)
            {
                return;
            }

            var next = width < SquareWidthPx && height < SquareHeightPx
                ? TimeSelectorFit.Square
                : TimeSelectorFit.Regular;
            if (_measuredFit == next)
            {
                return;
            }

            _measuredFit = next;
            StateHasChanged();
        });

    /// <summary>
    /// Called by the module when the typeahead buffer commits a segment value.
    /// <paramref name="advance"/> is owned by JS (<c>focusSegment</c> + <c>scrollIntoView</c>);
    /// Blazor only writes the model.
    /// </summary>
    [JSInvokable]
    public Task OnTypeaheadAsync(string segment, int value, bool advance)
    {
        // JS already moved focus when advance is true; keep the parameter for the module contract.
        _ = advance;
        return InvokeAsync(() => ApplyTypeaheadAsync(ParseSegment(segment), value));
    }

    /// <summary>Called by the module for Home / End on a spinbutton.</summary>
    [JSInvokable]
    public Task OnHomeEndAsync(string segment, bool home)
        => InvokeAsync(() => ApplyHomeEndAsync(ParseSegment(segment), home));

    /// <inheritdoc />
    public void Dispose() => DetachValidationStateChanged();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        DetachValidationStateChanged();
        try
        {
            if (_moduleTask is { } task)
            {
                var module = await task;
                if (_attached)
                {
                    await module.InvokeVoidAsync("detach", _root);
                }

                await module.DisposeAsync();
            }
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
        finally
        {
            _self?.Dispose();
        }
    }

    private Task OnStepperAsync(TimeSegment segment, int delta)
        => StepAsync(segment, delta, announce: true);

    private async Task StepAsync(TimeSegment segment, int delta, bool announce)
    {
        if (Disabled || ReadOnly)
        {
            return;
        }

        if (delta == 0)
        {
            if (announce && ShownValue is { } shown)
            {
                _liveText = Loc["TmTimeSelector_Set", FormatValue(shown)];
                await OnCommitted.InvokeAsync(shown);
                StateHasChanged();
            }

            return;
        }

        var current = ShownValue ?? StartingTime();
        var next = TimeOfDayFormat.Step(current, segment, delta, MinuteStep, MinTime, MaxTime, AllowOvernightRange);
        await CommitAsync(next, announce);
    }

    private async Task ApplyTypeaheadAsync(TimeSegment segment, int value)
    {
        if (Disabled || ReadOnly)
        {
            return;
        }

        var current = ShownValue ?? StartingTime();
        TimeOnly next;
        switch (segment)
        {
            case TimeSegment.Hours:
                var hour = Uses12HourClock ? ToHour24(NormalizeHour12(value), current.Hour >= 12) : Math.Clamp(value, 0, 23);
                next = new TimeOnly(hour, current.Minute, current.Second);
                break;
            case TimeSegment.Minutes:
                next = new TimeOnly(current.Hour, Math.Clamp(value, 0, 59), current.Second);
                break;
            case TimeSegment.Seconds:
                next = new TimeOnly(current.Hour, current.Minute, Math.Clamp(value, 0, 59));
                break;
            case TimeSegment.DayPeriod:
                next = new TimeOnly(ToHour24(Hour12(current.Hour), value != 0), current.Minute, current.Second);
                break;
            default:
                return;
        }

        await CommitAsync(next, announce: true);
    }

    private async Task ApplyHomeEndAsync(TimeSegment segment, bool home)
    {
        if (Disabled || ReadOnly)
        {
            return;
        }

        var current = ShownValue ?? StartingTime();
        TimeOnly next = segment switch
        {
            TimeSegment.Hours when home && MinTime is { } min =>
                new TimeOnly(min.Hour, current.Minute, current.Second),
            TimeSegment.Hours when !home && MaxTime is { } max =>
                new TimeOnly(max.Hour, current.Minute, current.Second),
            TimeSegment.Hours => new TimeOnly(home ? 0 : 23, current.Minute, current.Second),
            TimeSegment.Minutes => new TimeOnly(current.Hour, home ? 0 : 59, current.Second),
            TimeSegment.Seconds => new TimeOnly(current.Hour, current.Minute, home ? 0 : 59),
            TimeSegment.DayPeriod => TimeOfDayFormat.Step(current, TimeSegment.DayPeriod, home ? -1 : 1, MinuteStep, MinTime, MaxTime, AllowOvernightRange),
            _ => current,
        };

        if (!TimeOfDayFormat.IsInRange(next, MinTime, MaxTime, AllowOvernightRange))
        {
            if (home && MinTime is { } minBound)
            {
                next = minBound;
            }
            else if (!home && MaxTime is { } maxBound)
            {
                next = maxBound;
            }
            else
            {
                return;
            }
        }

        await CommitAsync(next, announce: false);
    }

    private Task OnPeriodChanged(bool isPm)
    {
        if (Disabled || ReadOnly)
        {
            return Task.CompletedTask;
        }

        var current = ShownValue ?? StartingTime();
        var hour12 = Hour12(current.Hour);
        return CommitAsync(new TimeOnly(ToHour24(hour12, isPm), current.Minute, current.Second), announce: true);
    }

    private async Task CommitAsync(TimeOnly value, bool announce)
    {
        if (Disabled || ReadOnly)
        {
            return;
        }

        _draft = value;
        _hasDraft = true;
        await ValueChanged.InvokeAsync(value);
        CascadedEditContext?.NotifyFieldChanged(FieldIdentifier);
        if (announce)
        {
            _liveText = Loc["TmTimeSelector_Set", FormatValue(value)];
            await OnCommitted.InvokeAsync(value);
        }

        StateHasChanged();
    }

    private TimeOnly StartingTime()
    {
        var now = TimeOnly.FromTimeSpan(Clock.GetLocalNow().TimeOfDay);
        var rounded = TimeOfDayFormat.RoundUpToStep(now, MinuteStep);
        if (MinTime is { } min && !AllowOvernightRange && rounded < min)
        {
            return min;
        }

        return rounded;
    }

    private bool CanStep(TimeSegment segment, int delta)
    {
        if (Disabled)
        {
            return false;
        }

        var current = ShownValue ?? StartingTime();
        var next = TimeOfDayFormat.Step(current, segment, delta, MinuteStep, MinTime, MaxTime, AllowOvernightRange);
        return next != current;
    }

    private string SegmentId(TimeSegment segment) => $"{EffectiveId}-{SegmentSlug(segment)}";

    private static string SegmentSlug(TimeSegment segment) => segment switch
    {
        TimeSegment.Hours => "hours",
        TimeSegment.Minutes => "minutes",
        TimeSegment.Seconds => "seconds",
        _ => "period",
    };

    private static TimeSegment ParseSegment(string? segment) => segment?.ToLowerInvariant() switch
    {
        "minutes" => TimeSegment.Minutes,
        "seconds" => TimeSegment.Seconds,
        "dayperiod" or "period" => TimeSegment.DayPeriod,
        _ => TimeSegment.Hours,
    };

    private ElementReference SegmentRef(TimeSegment segment) => segment switch
    {
        TimeSegment.Minutes => _minutesRef,
        TimeSegment.Seconds => _secondsRef,
        _ => _hoursRef,
    };

    private string SegmentAriaLabel(TimeSegment segment) => segment switch
    {
        TimeSegment.Minutes => Loc["TmTimePicker_Minutes"],
        TimeSegment.Seconds => Loc["TmTimePicker_Seconds"],
        _ => Loc["TmTimePicker_Hours"],
    };

    private string IncreaseLabel(TimeSegment segment) => segment switch
    {
        TimeSegment.Minutes => Loc["TmTimeSelector_IncreaseMinute", MinuteStep],
        TimeSegment.Seconds => Loc["TmTimeSelector_IncreaseSecond"],
        _ => Loc["TmTimeSelector_IncreaseHour"],
    };

    private string DecreaseLabel(TimeSegment segment) => segment switch
    {
        TimeSegment.Minutes => Loc["TmTimeSelector_DecreaseMinute", MinuteStep],
        TimeSegment.Seconds => Loc["TmTimeSelector_DecreaseSecond"],
        _ => Loc["TmTimeSelector_DecreaseHour"],
    };

    private string SegmentDisplay(TimeSegment segment)
    {
        if (ShownValue is null)
        {
            return "--";
        }

        var t = ShownValue.Value;
        var n = segment switch
        {
            TimeSegment.Hours => Uses12HourClock ? Hour12(t.Hour) : t.Hour,
            TimeSegment.Minutes => t.Minute,
            TimeSegment.Seconds => t.Second,
            _ => 0,
        };
        return n.ToString("D2", CultureInfo.InvariantCulture);
    }

    private string? SegmentValueNow(TimeSegment segment)
    {
        if (ShownValue is null)
        {
            return null;
        }

        var t = ShownValue.Value;
        var n = segment switch
        {
            TimeSegment.Hours => Uses12HourClock ? Hour12(t.Hour) : t.Hour,
            TimeSegment.Minutes => t.Minute,
            TimeSegment.Seconds => t.Second,
            _ => 0,
        };
        return n.ToString(CultureInfo.InvariantCulture);
    }

    private string SegmentValueMin(TimeSegment segment) => segment switch
    {
        TimeSegment.Hours => Uses12HourClock ? "1" : "0",
        _ => "0",
    };

    private string SegmentValueMax(TimeSegment segment) => segment switch
    {
        TimeSegment.Hours => Uses12HourClock ? "12" : "23",
        _ => "59",
    };

    private string? SegmentValueText(TimeSegment segment)
    {
        if (ShownValue is null)
        {
            return null;
        }

        var t = ShownValue.Value;
        return segment switch
        {
            TimeSegment.Hours when Uses12HourClock =>
                $"{Hour12(t.Hour).ToString(CultureInfo.InvariantCulture)} {Loc[t.Hour >= 12 ? "TmTimeSelector_Pm" : "TmTimeSelector_Am"]}",
            TimeSegment.Hours => Loc.Plural("TmTimeSelector_HourValue", t.Hour),
            TimeSegment.Minutes => Loc.Plural("TmTimeSelector_MinuteValue", t.Minute),
            TimeSegment.Seconds => Loc.Plural("TmTimeSelector_SecondValue", t.Second),
            _ => null,
        };
    }

    private string FormatValue(TimeOnly value)
        => Uses12HourClock
            ? TimeOfDayFormat.Format(value, HourCycle, EffectiveCulture)
            : value.ToString(ShowSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);

    private static int Hour12(int hour24)
    {
        var hour12 = hour24 % 12;
        return hour12 == 0 ? 12 : hour12;
    }

    private static int NormalizeHour12(int value)
        => value == 0 ? 12 : Math.Clamp(value, 1, 12);

    private static int ToHour24(int hour12, bool isPm)
    {
        if (hour12 == 12)
        {
            return isPm ? 12 : 0;
        }

        return isPm ? hour12 + 12 : hour12;
    }

    private Task<IJSObjectReference> Module => _moduleTask ??=
        JS.InvokeAsync<IJSObjectReference>("import", ModulePath).AsTask();

    private async Task AttachAsync()
    {
        try
        {
            _self ??= DotNetObjectReference.Create(this);
            var module = await Module;
            await module.InvokeVoidAsync("attach", _root, _self);
            _attached = true;
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
    }

    private async Task UpdateAsync()
    {
        try
        {
            var module = await Module;
            await module.InvokeVoidAsync("update", _root);
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
    }

    private void HandleValidationStateChanged(object? sender, ValidationStateChangedEventArgs e) =>
        InvokeAsync(StateHasChanged);

    private void DetachValidationStateChanged()
    {
        if (_subscribedEditContext is not null)
        {
            _subscribedEditContext.OnValidationStateChanged -= HandleValidationStateChanged;
            _subscribedEditContext = null;
        }
    }

    private static bool IsInteropTeardown(Exception ex)
        => ex is JSException or InvalidOperationException or TaskCanceledException or ObjectDisposedException;
}
