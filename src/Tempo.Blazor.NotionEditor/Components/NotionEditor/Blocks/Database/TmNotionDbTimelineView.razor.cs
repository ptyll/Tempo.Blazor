using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbTimelineView : TmComponentBase
{
    private enum TimelineZoom { Day, Week, Month }
    private enum DragMode    { None, MoveBar, ResizeBar }

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields           { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseRecord> Records          { get; set; } = [];
    [Parameter]                 public Guid?                          StartDateFieldId { get; set; }
    [Parameter]                 public Guid?                          EndDateFieldId   { get; set; }
    [Parameter]                 public bool                           ShowTableArea    { get; set; } = true;
    [Parameter]                 public bool                           ReadOnly         { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord> OnRecordUpdated { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord> OnRecordClicked { get; set; }
    [Parameter] public EventCallback                  OnNewRecord     { get; set; }

    // ── State ────────────────────────────────────────────────────────────────

    private TimelineZoom _zoom          = TimelineZoom.Week;
    private DateTime     _viewStart;
    private bool         _showTableArea = true;
    private bool         _initialized;

    private DragMode _dragMode      = DragMode.None;
    private Guid?    _dragId;
    private double   _dragStartX;
    private DateTime _dragOrigStart;
    private DateTime _dragOrigEnd;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        if (!_initialized)
        {
            _showTableArea = ShowTableArea;
            _viewStart     = StartOfUnit(DateTime.Today.AddDays(-14), TimelineZoom.Week);
            _initialized   = true;
        }
    }

    // ── Field resolution ──────────────────────────────────────────────────────

    private IDatabaseField? StartField => StartDateFieldId is not null
        ? Fields.FirstOrDefault(f => f.Id == StartDateFieldId)
        : Fields.FirstOrDefault(f => f.Type is DatabaseFieldType.Date or DatabaseFieldType.DateRange);

    private IDatabaseField? EndField => EndDateFieldId is not null
        ? Fields.FirstOrDefault(f => f.Id == EndDateFieldId)
        : Fields.Skip(1).FirstOrDefault(f => f.Type is DatabaseFieldType.Date or DatabaseFieldType.DateRange);

    private IDatabaseField? PrimaryField => Fields.FirstOrDefault(f => f.IsPrimary);

    private IEnumerable<IDatabaseField> SidebarFields =>
        Fields.Where(f => !f.IsPrimary && f.IsVisible).Take(2);

    // ── Zoom geometry ─────────────────────────────────────────────────────────

    private double ColWidthPx => _zoom switch
    {
        TimelineZoom.Day   => 44.0,
        TimelineZoom.Week  => 100.0,
        TimelineZoom.Month => 110.0,
        _                  => 44.0
    };

    private double PxPerDay => _zoom switch
    {
        TimelineZoom.Day   => 44.0,
        TimelineZoom.Week  => 100.0 / 7.0,
        TimelineZoom.Month => 110.0 / 30.0,
        _                  => 44.0
    };

    private int ColumnCount => _zoom switch
    {
        TimelineZoom.Day   => 60,
        TimelineZoom.Week  => 26,
        TimelineZoom.Month => 18,
        _                  => 60
    };

    private double TotalGanttWidth => ColumnCount * ColWidthPx;

    private double DateToPixel(DateTime date) => (date - _viewStart).TotalDays * PxPerDay;

    private static DateTime StartOfWeek(DateTime date)
    {
        var dow = (int)date.DayOfWeek;
        return date.Date.AddDays(dow == 0 ? -6 : 1 - dow);
    }

    private static DateTime StartOfUnit(DateTime date, TimelineZoom zoom) => zoom switch
    {
        TimelineZoom.Week  => StartOfWeek(date),
        TimelineZoom.Month => new DateTime(date.Year, date.Month, 1),
        _                  => date.Date
    };

    private List<(DateTime Start, string Label)> GetColumnHeaders()
    {
        var result  = new List<(DateTime, string)>(ColumnCount);
        var culture = CultureInfo.CurrentCulture;

        for (var i = 0; i < ColumnCount; i++)
        {
            var (start, label) = _zoom switch
            {
                TimelineZoom.Day => (
                    _viewStart.AddDays(i),
                    _viewStart.AddDays(i).ToString("ddd d", culture)
                ),
                TimelineZoom.Week => (
                    _viewStart.AddDays(i * 7),
                    _viewStart.AddDays(i * 7).ToString("MMM d", culture)
                ),
                TimelineZoom.Month => (
                    _viewStart.AddMonths(i),
                    _viewStart.AddMonths(i).ToString("MMM yy", culture)
                ),
                _ => (
                    _viewStart.AddDays(i),
                    _viewStart.AddDays(i).ToString("d", culture)
                )
            };
            result.Add((start, label));
        }

        return result;
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void PrevPeriod()
    {
        _viewStart = _zoom switch
        {
            TimelineZoom.Day   => _viewStart.AddDays(-30),
            TimelineZoom.Week  => _viewStart.AddDays(-13 * 7),
            TimelineZoom.Month => _viewStart.AddMonths(-9),
            _                  => _viewStart.AddDays(-30)
        };
    }

    private void NextPeriod()
    {
        _viewStart = _zoom switch
        {
            TimelineZoom.Day   => _viewStart.AddDays(30),
            TimelineZoom.Week  => _viewStart.AddDays(13 * 7),
            TimelineZoom.Month => _viewStart.AddMonths(9),
            _                  => _viewStart.AddDays(30)
        };
    }

    private void GoToToday()
    {
        _viewStart = StartOfUnit(DateTime.Today.AddDays(-14), _zoom);
    }

    private void SetZoom(TimelineZoom zoom)
    {
        _zoom      = zoom;
        _viewStart = StartOfUnit(DateTime.Today.AddDays(-14), zoom);
    }

    // ── Bar geometry ──────────────────────────────────────────────────────────

    private const double MinBarWidthPx = 28.0;

    private (double Left, double Width, bool Visible) GetBarGeometry(IDatabaseRecord record)
    {
        var start = GetDate(record, StartField);
        if (start is null) return (0, 0, false);

        var end    = GetDate(record, EndField) ?? start.Value.AddDays(1);
        var left   = DateToPixel(start.Value);
        var width  = Math.Max(MinBarWidthPx, DateToPixel(end) - left);
        return (left, width, true);
    }

    private DateTime? GetDate(IDatabaseRecord record, IDatabaseField? field)
    {
        if (field is null) return null;
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var v) || v is null) return null;
        return ParseDate(v);
    }

    private static DateTime? ParseDate(object val) => val switch
    {
        DateTime dt                                        => dt,
        DateOnly d                                         => d.ToDateTime(TimeOnly.MinValue),
        string s when DateTime.TryParse(s, out var parsed) => parsed,
        long ticks                                         => new DateTime(ticks),
        _                                                  => null
    };

    // ── Bar color ─────────────────────────────────────────────────────────────

    private string GetBarColor(IDatabaseRecord record)
    {
        foreach (var field in Fields.Where(f =>
            f.Type is DatabaseFieldType.Status or DatabaseFieldType.Select))
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

    // ── Today marker ─────────────────────────────────────────────────────────

    private double TodayPixel   => DateToPixel(DateTime.Today);
    private bool   TodayVisible => TodayPixel is >= 0 and <= 9000;

    // ── Formatted values ──────────────────────────────────────────────────────



    private static object? RawValue(IDatabaseRecord record, IDatabaseField field)
        => record.Fields.TryGetValue(field.Id.ToString(), out var value) ? value : null;

    // ── Drag & drop ───────────────────────────────────────────────────────────

    private void StartMoveBar(MouseEventArgs e, IDatabaseRecord record)
    {
        if (ReadOnly) return;
        _dragMode      = DragMode.MoveBar;
        _dragId        = record.Id;
        _dragStartX    = e.ClientX;
        _dragOrigStart = GetDate(record, StartField) ?? DateTime.Today;
        _dragOrigEnd   = GetDate(record, EndField)   ?? _dragOrigStart.AddDays(1);
    }

    private void StartResizeBar(MouseEventArgs e, IDatabaseRecord record)
    {
        if (ReadOnly) return;
        _dragMode      = DragMode.ResizeBar;
        _dragId        = record.Id;
        _dragStartX    = e.ClientX;
        _dragOrigStart = GetDate(record, StartField) ?? DateTime.Today;
        _dragOrigEnd   = GetDate(record, EndField)   ?? _dragOrigStart.AddDays(1);
    }

    private void HandleMouseMove(MouseEventArgs e)
    {
        if (_dragMode == DragMode.None || _dragId is null) return;

        var deltaDays = (e.ClientX - _dragStartX) / PxPerDay;
        var record    = Records.FirstOrDefault(r => r.Id == _dragId);
        if (record is not DatabaseRecord mutable) return;

        if (_dragMode == DragMode.MoveBar)
        {
            var newStart = _dragOrigStart.AddDays(deltaDays);
            var duration = (_dragOrigEnd - _dragOrigStart).TotalDays;
            UpdateDraggedDates(mutable, newStart, newStart.AddDays(duration));
        }
        else
        {
            var newEnd = _dragOrigEnd.AddDays(deltaDays);
            if (newEnd > _dragOrigStart.AddHours(1))
                UpdateDraggedDates(mutable, _dragOrigStart, newEnd);
        }
    }

    private void UpdateDraggedDates(DatabaseRecord record, DateTime start, DateTime end)
    {
        var dict = new Dictionary<string, object?>(record.Fields);
        if (StartField is not null) dict[StartField.Id.ToString()] = start;
        if (EndField   is not null) dict[EndField.Id.ToString()]   = end;
        record.Fields = dict;
        StateHasChanged();
    }

    private async Task HandleMouseUpAsync(MouseEventArgs _)
    {
        if (_dragMode == DragMode.None || _dragId is null) return;

        var record = Records.FirstOrDefault(r => r.Id == _dragId);
        _dragMode  = DragMode.None;
        _dragId    = null;

        if (record is not null)
            await OnRecordUpdated.InvokeAsync(record);
    }

    private async Task HandleItemKeyAsync(KeyboardEventArgs e, IDatabaseRecord record)
    {
        if (e.Key is "Enter" or " ")
            await OnRecordClicked.InvokeAsync(record);
    }

    private void CancelDrag()
    {
        if (_dragMode == DragMode.None) return;
        var record = Records.FirstOrDefault(r => r.Id == _dragId);
        if (record is DatabaseRecord mutable)
            UpdateDraggedDates(mutable, _dragOrigStart, _dragOrigEnd);
        _dragMode = DragMode.None;
        _dragId   = null;
    }
}
