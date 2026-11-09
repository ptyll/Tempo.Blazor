using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbCalendarView
{
    private record CalendarCell(DateOnly Date, bool IsCurrentMonth, List<IDatabaseRecord> Records);

    // Three chips fit the cell; the fourth event is the +N overflow popover.
    private const int MaxEventsPerCell = 3;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields      { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseRecord> Records     { get; set; } = [];
    [Parameter]                 public Guid?                          DateFieldId { get; set; }
    [Parameter]                 public bool                           ReadOnly    { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord> OnRecordUpdated { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord> OnRecordClicked { get; set; }
    [Parameter] public EventCallback<DateTime>        OnNewRecord     { get; set; }

    /// <summary>Cascaded layout. A mobile container opens the day as a sheet instead of a popover.</summary>
    [CascadingParameter] private TmLayoutContext? Layout { get; set; }

    private bool IsMobile => Layout?.IsMobile == true;

    // ── Calendar state ───────────────────────────────────────────────────────

    private DateOnly         _currentMonth;
    private List<CalendarCell> _cells = [];

    /// <summary>The day whose overflow list is open, or null when the list is closed.</summary>
    private DateOnly? _overflowDate;
    private bool               _initialized;

    protected override void OnParametersSet()
    {
        if (!_initialized)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var dated = Records.Select(FirstRecordDate).FirstOrDefault(date => date is not null);
            var seed = dated ?? today;
            _currentMonth = new DateOnly(seed.Year, seed.Month, 1);
            _initialized  = true;
        }
        ComputeCalendar();
    }

    private void ComputeCalendar()
    {
        var year     = _currentMonth.Year;
        var month    = _currentMonth.Month;
        var firstDay = new DateOnly(year, month, 1);

        // G7: the week starts on the culture's first day (en-US Sunday, cs-CZ Monday).
        var firstDow    = (int)firstDay.DayOfWeek;
        var weekStart   = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var startOffset = (firstDow - weekStart + 7) % 7;
        var startDate   = firstDay.AddDays(-startOffset);

        _cells = Enumerable.Range(0, 42)
            .Select(i =>
            {
                var date    = startDate.AddDays(i);
                var records = GetRecordsForDate(date);
                return new CalendarCell(date, date.Month == month, records);
            })
            .ToList();
    }

    // ── Date field resolution ─────────────────────────────────────────────────

    private IDatabaseField? DateField => DateFieldId is not null
        ? Fields.FirstOrDefault(f => f.Id == DateFieldId)
        : Fields.FirstOrDefault(f =>
            f.Type is DatabaseFieldType.Date or DatabaseFieldType.DateRange);

    private DateOnly? FirstRecordDate(IDatabaseRecord record)
    {
        var field = DateField;
        if (field is null || !record.Fields.TryGetValue(field.Id.ToString(), out var value) || value is null)
            return null;
        var parsed = ParseDate(value);
        return parsed is null ? null : DateOnly.FromDateTime(parsed.Value);
    }

    private List<IDatabaseRecord> GetRecordsForDate(DateOnly date)
    {
        var field = DateField;
        if (field is null) return [];
        var key = field.Id.ToString();
        return Records
            .Where(r =>
            {
                if (!r.Fields.TryGetValue(key, out var v) || v is null) return false;
                var dt = ParseDate(v);
                return dt is not null && DateOnly.FromDateTime(dt.Value) == date;
            })
            .ToList();
    }

    private static DateTime? ParseDate(object val) => val switch
    {
        DateTime dt                                        => dt,
        DateOnly d                                         => d.ToDateTime(TimeOnly.MinValue),
        string s when DateTime.TryParse(s, out var parsed) => parsed,
        long ticks                                         => new DateTime(ticks),
        _                                                  => null
    };

    // ── Navigation ───────────────────────────────────────────────────────────

    private void PrevMonth()
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        ComputeCalendar();
    }

    private void NextMonth()
    {
        _currentMonth = _currentMonth.AddMonths(1);
        ComputeCalendar();
    }

    private void GoToToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        _currentMonth = new DateOnly(today.Year, today.Month, 1);
        ComputeCalendar();
    }

    // ── Computed display ──────────────────────────────────────────────────────

    private DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private string MonthTitle =>
        new DateTime(_currentMonth.Year, _currentMonth.Month, 1)
            .ToString("MMMM yyyy", CultureInfo.CurrentCulture);

    private string[] WeekdayHeaders
    {
        get
        {
            var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
            var start = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            return Enumerable.Range(0, 7).Select(i => names[(start + i) % 7]).ToArray();
        }
    }

    private string MoreLabel(int count) => Loc.Plural("TmNotionDbCalendarView_More", count);

    private static IEnumerable<IDatabaseRecord> VisibleEvents(CalendarCell cell)
        => cell.Records.Take(MaxEventsPerCell);

    private static int OverflowCount(CalendarCell cell)
        => Math.Max(0, cell.Records.Count - MaxEventsPerCell);

    private Task SetOverflowOpen(DateOnly date, bool open)
    {
        _overflowDate = open ? date : (_overflowDate == date ? null : _overflowDate);
        return Task.CompletedTask;
    }

    private async Task SelectOverflowRecordAsync(IDatabaseRecord record)
    {
        _overflowDate = null;
        await HandleEventClickAsync(record);
    }

    private CalendarCell? OverflowCell => _overflowDate is { } date
        ? _cells.FirstOrDefault(c => c.Date == date)
        : null;

    // ── Event color ───────────────────────────────────────────────────────────

    private string GetEventColor(IDatabaseRecord record)
    {
        foreach (var field in Fields.Where(f =>
            f.Type is DatabaseFieldType.Status or DatabaseFieldType.Select && f.IsVisible))
        {
            if (!record.Fields.TryGetValue(field.Id.ToString(), out var v) || v is null) continue;
            var val = v.ToString() ?? string.Empty;
            if (val.Length == 0) continue;

            string? color = null;
            if (field.Config is IStatusFieldConfig sc)
                color = sc.Groups.SelectMany(g => g.Options)
                    .FirstOrDefault(o => string.Equals(o.Name, val, StringComparison.OrdinalIgnoreCase))?.Color;
            else if (field.Config is ISelectFieldConfig sel)
                color = sel.Options
                    .FirstOrDefault(o => string.Equals(o.Name, val, StringComparison.OrdinalIgnoreCase))?.Color;

            if (!string.IsNullOrEmpty(color)) return NotionColorTokens.OptionSolid(color);
        }
        return NotionColorTokens.OptionSolid(null);
    }

    // ── Primary value ─────────────────────────────────────────────────────────

    private IDatabaseField? PrimaryField => Fields.FirstOrDefault(f => f.IsPrimary);

    private object? PrimaryRaw(IDatabaseRecord record)
    {
        var primary = PrimaryField;
        if (primary is null) return null;
        return record.Fields.TryGetValue(primary.Id.ToString(), out var value) ? value : null;
    }

    // ── Drag & drop ──────────────────────────────────────────────────────────

    private Guid?     _dragRecordId;
    private DateOnly? _dragFromDate;
    private DateOnly? _dragOverDate;

    private void OnEventDragStart(IDatabaseRecord record, DateOnly date)
    {
        _dragRecordId = record.Id;
        _dragFromDate = date;
    }

    private void SetDragOverDate(DateOnly date)
    {
        if (_dragRecordId is null) return;
        _dragOverDate = date;
    }

    private async Task OnCellDropAsync(DateOnly date)
    {
        if (_dragRecordId is null || _dragFromDate == date) { ResetDrag(); return; }

        var record = Records.FirstOrDefault(r => r.Id == _dragRecordId);
        ResetDrag();

        if (record is DatabaseRecord mutable && DateField is not null)
        {
            var dict = new Dictionary<string, object?>(record.Fields)
            {
                [DateField.Id.ToString()] = date.ToDateTime(TimeOnly.MinValue)
            };
            mutable.Fields = dict;
            await OnRecordUpdated.InvokeAsync(mutable);
        }
    }

    private void OnDragEnd(DragEventArgs _) => ResetDrag();

    private void ResetDrag()
    {
        _dragRecordId = null;
        _dragFromDate = null;
        _dragOverDate = null;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async Task HandleCellClickAsync(DateOnly date)
    {
        if (IsMobile)
        {
            await SetOverflowOpen(date, true);
            return;
        }
        if (ReadOnly) return;
        await OnNewRecord.InvokeAsync(date.ToDateTime(TimeOnly.MinValue));
    }

    private async Task HandleEventClickAsync(IDatabaseRecord record)
        => await OnRecordClicked.InvokeAsync(record);

    private async Task HandleEventKeyAsync(KeyboardEventArgs e, IDatabaseRecord record)
    {
        if (e.Key is "Enter" or " ")
            await OnRecordClicked.InvokeAsync(record);
    }
}
