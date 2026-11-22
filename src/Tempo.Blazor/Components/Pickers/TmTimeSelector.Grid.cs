using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Pickers;

public partial class TmTimeSelector
{
    private const int CompactHourEightColumnMinWidthPx = 396;
    private const int RecentTimesCap = 3;
    private const string GridTabHours = "Hours";
    private const string GridTabMinutes = "Minutes";

    private static readonly int[] Hours24 = Enumerable.Range(0, 24).ToArray();
    private static readonly int[] Hours12 = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

    private double _innerWidth;
    private bool _squareGridOpen;
    private TimeSegment _squareGridSegment = TimeSegment.Hours;
    private string _gridTab = GridTabHours;
    private int _hourKeyboardIndex = -1;
    private int _minuteKeyboardIndex = -1;
    private bool _focusMinuteAfterRender;
    private bool _gridKeyHandled;
    private ElementReference[] _hourCellRefs = [];
    private ElementReference[] _minuteCellRefs = [];
    private int _pendingHourFocus = -1;
    private int _pendingMinuteFocus = -1;

    /// <summary>
    /// Whether the hour/minute grid is allowed at all. Square + seconds always collapses
    /// (304 px would overflow the 260 px sheet), even when <see cref="ShowGrid"/> is Always.
    /// </summary>
    private bool GridAllowed
        => ShowGrid != TimeSelectorGrid.None
           && !(ResolvedFit == TimeSelectorFit.Square && ShowSeconds)
           && !Disabled
           && !ReadOnly
           && !(_keyboardOpen && ResolvedFit == TimeSelectorFit.Square)
           && !_typing;

    private bool ShowOpenGrid
        => GridAllowed
           && ResolvedFit == TimeSelectorFit.Square
           && _squareGridOpen;

    private bool ShowBothGrids
        => GridAllowed
           && ResolvedFit != TimeSelectorFit.Square
           && ResolvedLayout is TmLayoutMode.Tablet or TmLayoutMode.Desktop;

    private bool ShowGridTabs
        => GridAllowed
           && ResolvedFit != TimeSelectorFit.Square
           && ResolvedLayout == TmLayoutMode.Mobile;

    private bool ShowHoursGrid
        => GridAllowed && (ShowBothGrids
            || ShowOpenGrid && _squareGridSegment == TimeSegment.Hours
            || ShowGridTabs && _gridTab == GridTabHours);

    private bool ShowMinutesGrid
        => GridAllowed && (ShowBothGrids
            || ShowOpenGrid && _squareGridSegment == TimeSegment.Minutes
            || ShowGridTabs && _gridTab == GridTabMinutes);

    private bool ShowPresetRow
        => !Disabled && !ReadOnly && !_typing
           && !(_keyboardOpen && ResolvedFit == TimeSelectorFit.Square)
           && !ShowOpenGrid
           && HasPresetContent;

    private bool HasPresetContent
        => ShowNowPreset
           || (Presets is { Count: > 0 })
           || VisibleRecents.Count > 0;

    private int ResolvedGridMinuteStep => GridMinuteStep ?? Math.Max(MinuteStep, 5);

    private IReadOnlyList<int> HourValues => Uses12HourClock ? Hours12 : Hours24;

    private IReadOnlyList<int> MinuteValues
    {
        get
        {
            var step = ResolvedGridMinuteStep;
            var count = 60 / step;
            var values = new int[count];
            for (var i = 0; i < count; i++)
            {
                values[i] = i * step;
            }

            return values;
        }
    }

    private int HourColumns
    {
        get
        {
            if (ResolvedFit != TimeSelectorFit.Square)
            {
                return 6;
            }

            if (Uses12HourClock)
            {
                return 6;
            }

            var inner = _innerWidth > 0 ? _innerWidth : _viewportWidth;
            return inner > 0 && inner < CompactHourEightColumnMinWidthPx ? 6 : 8;
        }
    }

    private int MinuteColumns => ResolvedFit == TimeSelectorFit.Square ? 6 : 4;

    private IReadOnlyList<TimeOnly> VisibleRecents
    {
        get
        {
            if (RecentTimes is not { Count: > 0 })
            {
                return Array.Empty<TimeOnly>();
            }

            var presetTimes = Presets is { Count: > 0 }
                ? Presets.Select(p => p.Time).ToHashSet()
                : null;
            var result = new List<TimeOnly>(RecentTimesCap);
            foreach (var time in RecentTimes)
            {
                if (presetTimes is not null && presetTimes.Contains(time))
                {
                    continue;
                }

                result.Add(time);
                if (result.Count == RecentTimesCap)
                {
                    break;
                }
            }

            return result;
        }
    }

    private IReadOnlyList<SegmentedOption<string>> GridTabOptions =>
    [
        new()
        {
            Label = Loc["TmTimeSelector_HoursTab"],
            Value = GridTabHours,
            PanelId = HoursGridId,
        },
        new()
        {
            Label = Loc["TmTimeSelector_MinutesTab"],
            Value = GridTabMinutes,
            PanelId = MinutesGridId,
        },
    ];

    private string HoursGridId => $"{EffectiveId}-grid-hours";

    private string MinutesGridId => $"{EffectiveId}-grid-minutes";

    private Task OnHoursSpinClickAsync()
        => OnSegmentActivateAsync(TimeSegment.Hours);

    private Task OnMinutesSpinClickAsync()
        => OnSegmentActivateAsync(TimeSegment.Minutes);

    private Task OnSegmentActivateAsync(TimeSegment segment)
    {
        if (!GridAllowed || ResolvedFit != TimeSelectorFit.Square)
        {
            return Task.CompletedTask;
        }

        _squareGridOpen = true;
        _squareGridSegment = segment;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnGridTabChanged(string? tab)
    {
        if (!string.IsNullOrEmpty(tab))
        {
            _gridTab = tab;
        }

        return Task.CompletedTask;
    }

    private async Task OnHourCellAsync(int hour)
    {
        if (Disabled || ReadOnly || HourCellDisabled(hour))
        {
            return;
        }

        var current = ShownValue ?? StartingTime();
        var hour24 = Uses12HourClock ? ToHour24(hour, current.Hour >= 12) : hour;
        var next = new TimeOnly(hour24, current.Minute, current.Second);
        await CommitAsync(next, announce: true);

        if (ShowGridTabs)
        {
            _gridTab = GridTabMinutes;
            _focusMinuteAfterRender = true;
        }
        else if (ShowOpenGrid)
        {
            _squareGridSegment = TimeSegment.Minutes;
            _focusMinuteAfterRender = true;
        }
    }

    private async Task OnMinuteCellAsync(int minute)
    {
        if (Disabled || ReadOnly || MinuteCellDisabled(minute))
        {
            return;
        }

        var current = ShownValue ?? StartingTime();
        var next = new TimeOnly(current.Hour, minute, current.Second);
        await CommitAsync(next, announce: true);
    }

    private async Task OnPresetAsync(TimeOnly time)
    {
        if (Disabled || ReadOnly || TimeDisabled(time))
        {
            return;
        }

        await CommitAsync(time, announce: true);
    }

    private Task OnNowAsync()
        => OnPresetAsync(StartingTime());

    private void OnTypeTime()
    {
        if (!ShowTypeTime)
        {
            return;
        }

        _typing = true;
        _typedText = ShownValue is { } shown ? FormatValue(shown) : string.Empty;
        _typedInvalid = false;
    }

    private void OnUseControls()
    {
        _typing = false;
        _typedInvalid = false;
        _typedText = string.Empty;
    }

    private void OnTypedInput(ChangeEventArgs args)
    {
        _typedText = args.Value as string ?? string.Empty;
        _typedInvalid = false;
    }

    private async Task OnTypedKeyDownAsync(KeyboardEventArgs args)
    {
        if (args.Key is "Enter")
        {
            await CommitTypedAsync();
        }
        else if (args.Key is "Escape")
        {
            OnUseControls();
        }
    }

    private async Task OnTypedBlurAsync()
        => await CommitTypedAsync();

    private async Task CommitTypedAsync()
    {
        if (!_typing || Disabled || ReadOnly)
        {
            return;
        }

        if (!TimeOfDayFormat.TryParse(_typedText, EffectiveCulture, ShowSeconds, out var parsed))
        {
            _typedInvalid = true;
            return;
        }

        await CommitAsync(parsed, announce: true);
        OnUseControls();
    }

    private void HandleHourGridKeyDown(KeyboardEventArgs args, int index)
    {
        _gridKeyHandled = IsGridFocusKey(args.Key);
        if (!_gridKeyHandled)
        {
            return;
        }

        var before = _hourKeyboardIndex;
        HandleGridKeyDown(args, index, HourValues.Count, HourColumns, ref _hourKeyboardIndex);
        if (_hourKeyboardIndex != before)
        {
            _pendingHourFocus = _hourKeyboardIndex;
            StateHasChanged();
        }
    }

    private void HandleMinuteGridKeyDown(KeyboardEventArgs args, int index)
    {
        _gridKeyHandled = IsGridFocusKey(args.Key);
        if (!_gridKeyHandled)
        {
            return;
        }

        var before = _minuteKeyboardIndex;
        HandleGridKeyDown(args, index, MinuteValues.Count, MinuteColumns, ref _minuteKeyboardIndex);
        if (_minuteKeyboardIndex != before)
        {
            _pendingMinuteFocus = _minuteKeyboardIndex;
            StateHasChanged();
        }
    }

    private static bool IsGridFocusKey(string? key)
        => key is "ArrowRight" or "ArrowLeft" or "ArrowUp" or "ArrowDown" or "Home" or "End";

    private static void HandleGridKeyDown(
        KeyboardEventArgs args,
        int index,
        int count,
        int columns,
        ref int keyboardIndex)
    {
        if (count == 0)
        {
            return;
        }

        keyboardIndex = Math.Clamp(index, 0, count - 1);
        var nextIndex = args.Key switch
        {
            "ArrowRight" => MoveGridIndex(keyboardIndex, 1, count),
            "ArrowLeft" => MoveGridIndex(keyboardIndex, -1, count),
            "ArrowDown" => MoveGridIndex(keyboardIndex, Math.Max(1, columns), count),
            "ArrowUp" => MoveGridIndex(keyboardIndex, -Math.Max(1, columns), count),
            "Home" => 0,
            "End" => count - 1,
            _ => keyboardIndex,
        };

        if (nextIndex != keyboardIndex)
        {
            keyboardIndex = nextIndex;
        }
    }

    private static int MoveGridIndex(int index, int delta, int count)
    {
        var next = index + delta;
        if (next < 0)
        {
            return 0;
        }

        if (next >= count)
        {
            return count - 1;
        }

        return next;
    }

    private int HourTabIndex(int index)
    {
        var selected = SelectedHourIndex();
        var focus = _hourKeyboardIndex >= 0 ? _hourKeyboardIndex : selected;
        return index == focus ? 0 : -1;
    }

    private int MinuteTabIndex(int index)
    {
        var selected = SelectedMinuteIndex();
        var focus = _minuteKeyboardIndex >= 0 ? _minuteKeyboardIndex : selected;
        return index == focus ? 0 : -1;
    }

    private int SelectedHourIndex()
    {
        var values = HourValues;
        if (values.Count == 0)
        {
            return 0;
        }

        var hour = ShownValue is { } shown
            ? Uses12HourClock ? Hour12(shown.Hour) : shown.Hour
            : values[0];
        var index = IndexOf(values, hour);
        return index >= 0 ? index : 0;
    }

    private int SelectedMinuteIndex()
    {
        var values = MinuteValues;
        if (values.Count == 0)
        {
            return 0;
        }

        var minute = ShownValue?.Minute ?? 0;
        var index = IndexOf(values, minute);
        if (index >= 0)
        {
            return index;
        }

        var nearest = 0;
        var best = int.MaxValue;
        for (var i = 0; i < values.Count; i++)
        {
            var distance = Math.Abs(values[i] - minute);
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        return nearest;
    }

    private static int IndexOf(IReadOnlyList<int> values, int value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private bool HourCellSelected(int hour)
    {
        if (ShownValue is not { } shown)
        {
            return false;
        }

        return Uses12HourClock ? Hour12(shown.Hour) == hour : shown.Hour == hour;
    }

    private bool MinuteCellSelected(int minute)
        => ShownValue is { } shown && shown.Minute == minute;

    private bool HourCellDisabled(int hour)
    {
        var current = ShownValue ?? StartingTime();
        var hour24 = Uses12HourClock ? ToHour24(hour, current.Hour >= 12) : hour;
        return TimeDisabled(new TimeOnly(hour24, current.Minute, current.Second));
    }

    private bool MinuteCellDisabled(int minute)
    {
        var current = ShownValue ?? StartingTime();
        return TimeDisabled(new TimeOnly(current.Hour, minute, current.Second));
    }

    private bool TimeDisabled(TimeOnly time)
        => !TimeOfDayFormat.IsInRange(time, MinTime, MaxTime, AllowOvernightRange);

    private bool PresetPressed(TimeOnly time)
        => ShownValue is { } shown && shown == time;

    private string PresetLabel(TimePreset preset)
        => string.IsNullOrWhiteSpace(preset.Label) ? FormatValue(preset.Time) : preset.Label!;

    private static string PresetAttr(TimeOnly time)
        => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    private string RecentChipLabel(TimeOnly time)
        => string.Format(EffectiveCulture, Loc["TmTimeSelector_RecentValue"], FormatValue(time));

    private string HourCellLabel(int hour)
        => Loc.Plural("TmTimeSelector_HourValue", hour);

    private string MinuteCellLabel(int minute)
        => Loc.Plural("TmTimeSelector_MinuteValue", minute);

    private void EnsureHourCellRefs(int count)
    {
        if (_hourCellRefs.Length < count)
        {
            Array.Resize(ref _hourCellRefs, count);
        }
    }

    private void EnsureMinuteCellRefs(int count)
    {
        if (_minuteCellRefs.Length < count)
        {
            Array.Resize(ref _minuteCellRefs, count);
        }
    }

    private async Task FocusPendingGridCellsAsync()
    {
        try
        {
            if (_pendingHourFocus >= 0 && _pendingHourFocus < _hourCellRefs.Length)
            {
                await _hourCellRefs[_pendingHourFocus].FocusAsync();
            }

            if (_pendingMinuteFocus >= 0 && _pendingMinuteFocus < _minuteCellRefs.Length)
            {
                await _minuteCellRefs[_pendingMinuteFocus].FocusAsync();
            }

            if (_focusMinuteAfterRender)
            {
                var minuteIndex = SelectedMinuteIndex();
                if (minuteIndex >= 0 && minuteIndex < _minuteCellRefs.Length)
                {
                    await _minuteCellRefs[minuteIndex].FocusAsync();
                }
            }
        }
        catch (Exception ex) when (IsInteropTeardown(ex))
        {
        }
        finally
        {
            _pendingHourFocus = -1;
            _pendingMinuteFocus = -1;
            _focusMinuteAfterRender = false;
        }
    }
}
