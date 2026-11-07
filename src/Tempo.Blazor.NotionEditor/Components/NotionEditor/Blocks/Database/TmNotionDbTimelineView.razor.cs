using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbTimelineView : TmComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _rootRef;
    private IJSObjectReference? _module;
    private IJSObjectReference? _timeline;
    private DotNetObjectReference<TmNotionDbTimelineView>? _self;

    private enum TimelineZoom { Day, Week, Month }

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

    /// <summary>Cascaded layout. A mobile container hides the sidebar and opens a date sheet on tap.</summary>
    [CascadingParameter] private TmLayoutContext? Layout { get; set; }

    private bool IsMobile => Layout?.IsMobile == true;

    private bool IsCoarseSheet => Layout?.IsMobile == true || Layout?.IsTablet == true;

    // ── State ────────────────────────────────────────────────────────────────

    private TimelineZoom _zoom          = TimelineZoom.Week;
    private DateTime     _viewStart;
    private bool         _showTableArea = true;
    private bool         _initialized;
    private Guid?        _sheetRecordId;

    private TimelineZoomLevel ZoomLevel => _zoom switch
    {
        TimelineZoom.Day => TimelineZoomLevel.Day,
        TimelineZoom.Month => TimelineZoomLevel.Month,
        _ => TimelineZoomLevel.Week
    };

    private bool ShowSidebar => _showTableArea && !IsMobile;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || ReadOnly) return;
        _self = DotNetObjectReference.Create(this);
        _module = await JS.InvokeAsync<IJSObjectReference>("import",
            "./_content/Tempo.Blazor.NotionEditor/js/notion-db-timeline.js");
        _timeline = await _module.InvokeAsync<IJSObjectReference>("attachTimeline", _rootRef, _self);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_timeline is not null)
            {
                await _timeline.InvokeVoidAsync("detach");
                await _timeline.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }

        try
        {
            if (_module is not null) await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is already gone.
        }
        _self?.Dispose();
    }

    /// <summary>Commits a bar move or resize. The pointer helper calls this once, on pointerup.</summary>
    [JSInvokable]
    public Task OnBarCommitted(string recordId, int days, bool resize)
        => Guid.TryParse(recordId, out var id)
            ? ApplyKeyboardStepAsync(id, days, resize)
            : Task.CompletedTask;

    /// <summary>Moves (or resizes) one bar by a whole number of days and raises <see cref="OnRecordUpdated"/>.</summary>
    public async Task ApplyKeyboardStepAsync(Guid recordId, int days, bool resize)
    {
        if (ReadOnly || days == 0) return;
        if (Records.FirstOrDefault(r => r.Id == recordId) is not DatabaseRecord mutable) return;

        var start = GetDate(mutable, StartField) ?? DateTime.Today;
        var end = GetDate(mutable, EndField) ?? start.AddDays(1);
        if (resize)
        {
            var next = end.AddDays(days);
            if (next <= start) return;
            end = next;
        }
        else
        {
            start = start.AddDays(days);
            end = end.AddDays(days);
        }

        WriteDates(mutable, start, end);
        await OnRecordUpdated.InvokeAsync(mutable);
    }

    private void WriteDates(DatabaseRecord record, DateTime start, DateTime end)
    {
        var dict = new Dictionary<string, object?>(record.Fields);
        if (StartField is not null) dict[StartField.Id.ToString()] = start;
        if (EndField is not null) dict[EndField.Id.ToString()] = end;
        record.Fields = dict;
    }

    private void OpenDateSheet(IDatabaseRecord record) => _sheetRecordId = record.Id;

    private void CloseDateSheet() => _sheetRecordId = null;

    private IDatabaseRecord? SheetRecord => _sheetRecordId is { } id
        ? Records.FirstOrDefault(r => r.Id == id)
        : null;

    private string RecordCountLabel => Loc.Plural("TmNotionDbTimelineView_RecordCount", Records.Count);

    private IEnumerable<(string Label, int Span)> MonthHeaderSpans()
    {
        var headers = GetColumnHeaders();
        if (headers.Count == 0) yield break;
        var label = headers[0].Start.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
        var span = 0;
        foreach (var header in headers)
        {
            var next = header.Start.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            if (next != label)
            {
                yield return (label, span);
                label = next;
                span = 0;
            }
            span++;
        }
        yield return (label, span);
    }

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

    private double ColWidthPx => TimelineGeometry.ColumnWidth(ZoomLevel);

    private double PxPerDay => TimelineGeometry.PixelsPerDay(ZoomLevel);

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

    private Task OnBarClickAsync(IDatabaseRecord record)
        => IsMobile ? OpenSheetAndReturn(record) : OnRecordClicked.InvokeAsync(record);

    private Task OpenSheetAndReturn(IDatabaseRecord record)
    {
        OpenDateSheet(record);
        return Task.CompletedTask;
    }

    private async Task OpenSheetRecordAsync(IDatabaseRecord record)
    {
        CloseDateSheet();
        await OnRecordClicked.InvokeAsync(record);
    }

    private string SheetDate(IDatabaseRecord record, IDatabaseField? field)
        => GetDate(record, field)?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    private async Task HandleItemKeyAsync(KeyboardEventArgs e, IDatabaseRecord record)
    {
        // Alt+Arrow is owned by notion-db-timeline.js. A Blazor handler would raise
        // OnRecordUpdated a second time for the same chord.
        if (e.Key is "Enter" or " ")
            await OnRecordClicked.InvokeAsync(record);
    }

    private async Task CommitSheetDateAsync(IDatabaseRecord record, bool end, ChangeEventArgs e)
    {
        if (ReadOnly || record is not DatabaseRecord mutable) return;
        if (!DateTime.TryParse(e.Value?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && !DateTime.TryParse(e.Value?.ToString(), out parsed))
            return;

        var start = GetDate(mutable, StartField) ?? parsed.Date;
        var finish = GetDate(mutable, EndField) ?? start.AddDays(1);
        if (end) finish = parsed.Date;
        else start = parsed.Date;
        if (finish <= start) finish = start.AddDays(1);
        WriteDates(mutable, start, finish);
        await OnRecordUpdated.InvokeAsync(mutable);
    }
}
