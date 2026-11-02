using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbBoardView : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _boardRef;
    private DotNetObjectReference<TmNotionDbBoardView>? _dotNetRef;
    private record GroupDef(string Value, string Label, string? Color);
    private record BoardGroup(string Value, string Label, string? Color, List<IDatabaseRecord> Records);

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields          { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseRecord> Records         { get; set; } = [];
    [Parameter]                 public IDatabaseField?                GroupByField    { get; set; }
    [Parameter]                 public bool                           ReadOnly        { get; set; }
    [Parameter]                 public bool                           HideEmptyGroups { get; set; }
    [Parameter]                 public IReadOnlyList<Guid>?           PreviewFieldIds { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord> OnRecordUpdated { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord> OnRecordClicked { get; set; }
    [Parameter] public EventCallback<string?>         OnNewRecord     { get; set; }

    // ── Computed state ───────────────────────────────────────────────────────

    private List<BoardGroup>     _groups        = [];
    private List<IDatabaseField> _previewFields = [];

    protected override void OnParametersSet()
    {
        ComputeGroups();
        ComputePreviewFields();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await JS.InvokeVoidAsync("tmDb.initBoardDrag", _boardRef, _dotNetRef);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        await Task.CompletedTask;
    }

    private void ComputeGroups()
    {
        var defs     = GetGroupDefinitions();
        var fieldKey = GroupByField?.Id.ToString();

        var buckets = new Dictionary<string, List<IDatabaseRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in defs)
            buckets[d.Value] = [];
        buckets[string.Empty] = [];

        foreach (var record in Records)
        {
            string matched = string.Empty;
            if (fieldKey is not null && record.Fields.TryGetValue(fieldKey, out var rv))
                matched = MatchGroupValue(rv?.ToString() ?? string.Empty, defs);

            if (!buckets.ContainsKey(matched))
                buckets[matched] = [];
            buckets[matched].Add(record);
        }

        var result = defs
            .Select(d => new BoardGroup(
                d.Value,
                d.Label,
                d.Color,
                buckets.GetValueOrDefault(d.Value) ?? []))
            .ToList();

        var uncatRecords = buckets.GetValueOrDefault(string.Empty) ?? [];
        result.Add(new BoardGroup(string.Empty, string.Empty, null, uncatRecords));

        _groups = HideEmptyGroups
            ? result.Where(g => g.Records.Count > 0).ToList()
            : result;
    }

    private IReadOnlyList<GroupDef> GetGroupDefinitions()
    {
        if (GroupByField is null) return [];

        if (GroupByField.Config is IStatusFieldConfig statusCfg)
            return statusCfg.Groups
                .SelectMany(g => g.Options.Select(o => new GroupDef(o.Name, o.Name, o.Color)))
                .ToList();

        if (GroupByField.Config is ISelectFieldConfig selectCfg)
            return selectCfg.Options
                .Select(o => new GroupDef(o.Name, o.Name, o.Color))
                .ToList();

        var fieldKey = GroupByField.Id.ToString();
        return Records
            .Select(r => r.Fields.TryGetValue(fieldKey, out var v) ? v?.ToString() ?? "" : "")
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(v => new GroupDef(v, v, null))
            .ToList();
    }

    private static string MatchGroupValue(string recordValue, IReadOnlyList<GroupDef> defs)
    {
        if (string.IsNullOrEmpty(recordValue)) return string.Empty;
        foreach (var d in defs)
        {
            if (string.Equals(d.Value, recordValue, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(d.Label, recordValue, StringComparison.OrdinalIgnoreCase))
                return d.Value;
        }
        return string.Empty;
    }

    private void ComputePreviewFields()
    {
        if (PreviewFieldIds is { Count: > 0 })
        {
            _previewFields = PreviewFieldIds
                .Select(id => Fields.FirstOrDefault(f => f.Id == id))
                .OfType<IDatabaseField>()
                .ToList();
        }
        else
        {
            _previewFields = Fields
                .Where(f => !f.IsPrimary && f.IsVisible && f.Id != GroupByField?.Id)
                .Take(3)
                .ToList();
        }
    }

    // ── Drag & drop (JS-driven via [JSInvokable]) ────────────────────────────

    private Guid?   _dragRecordId;
    private string? _dragFromGroup;
    private string? _dragOverGroup;

    [JSInvokable]
    public Task JsDragStart(string recordId, string fromGroup)
    {
        _dragRecordId  = Guid.TryParse(recordId, out var g) ? g : (Guid?)null;
        _dragFromGroup = fromGroup;
        _dragOverGroup = null;
        // Do NOT call StateHasChanged here — Blazor DOM re-render during dragstart
        // causes the browser to lose the dragged element reference and cancel the drag.
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task JsDragEnter(string groupValue)
    {
        if (_dragOverGroup == groupValue) return Task.CompletedTask;
        _dragOverGroup = groupValue;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task JsDrop(string groupValue, string? recordId = null, string? fromGroup = null)
    {
        var dragRecordId = _dragRecordId;
        if (dragRecordId is null && !string.IsNullOrWhiteSpace(recordId) && Guid.TryParse(recordId, out var parsedRecordId))
            dragRecordId = parsedRecordId;

        var dragFromGroup = _dragFromGroup ?? fromGroup;

        if (dragRecordId is null || dragFromGroup == groupValue)
        {
            await JsDragEnd();
            return;
        }

        var record = Records.FirstOrDefault(r => r.Id == dragRecordId);
        _dragRecordId  = null;
        _dragFromGroup = null;
        _dragOverGroup = null;

        if (record is DatabaseRecord mutable && GroupByField is not null)
        {
            var dict = new Dictionary<string, object?>(record.Fields)
            {
                [GroupByField.Id.ToString()] = groupValue.Length > 0 ? (object?)groupValue : null
            };
            mutable.Fields = dict;
            await InvokeAsync(() => OnRecordUpdated.InvokeAsync(mutable));
        }

        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task JsDragEnd()
    {
        _dragRecordId  = null;
        _dragFromGroup = null;
        _dragOverGroup = null;
        return InvokeAsync(StateHasChanged);
    }

    // ── Card actions ─────────────────────────────────────────────────────────

    private async Task HandleCardClickAsync(IDatabaseRecord record)
        => await OnRecordClicked.InvokeAsync(record);

    private async Task HandleCardKeyAsync(KeyboardEventArgs e, IDatabaseRecord record)
    {
        if (e.Key is "Enter" or " ")
            await OnRecordClicked.InvokeAsync(record);
    }

    private async Task HandleAddCardAsync(string groupValue)
        => await OnNewRecord.InvokeAsync(groupValue.Length > 0 ? groupValue : null);

    // ── Card helpers ─────────────────────────────────────────────────────────

    private IDatabaseField? PrimaryField => Fields.FirstOrDefault(f => f.IsPrimary);

    private string GetPrimaryValue(IDatabaseRecord record)
    {
        var primary = PrimaryField;
        if (primary is null) return string.Empty;
        return record.Fields.TryGetValue(primary.Id.ToString(), out var v)
            ? NotionDatabaseValueFormatter.Format(v)
            : string.Empty;
    }

    private string? GetCoverUrl(IDatabaseRecord record)
    {
        var filesField = Fields.FirstOrDefault(f => f.Type == DatabaseFieldType.Files && f.IsVisible);
        if (filesField is not null && record.Fields.TryGetValue(filesField.Id.ToString(), out var fv))
        {
            var url = fv switch
            {
                string s                          => s,
                string[] arr when arr.Length > 0  => arr[0],
                IEnumerable<string> list          => list.FirstOrDefault(),
                _                                 => null
            };
            if (!string.IsNullOrEmpty(url)) return url;
        }

        var urlField = Fields.FirstOrDefault(f => f.Type == DatabaseFieldType.Url && f.IsVisible);
        if (urlField is not null && record.Fields.TryGetValue(urlField.Id.ToString(), out var uv))
        {
            var url = uv?.ToString() ?? string.Empty;
            if (url.StartsWith("http://", StringComparison.Ordinal) ||
                url.StartsWith("https://", StringComparison.Ordinal))
                return url;
        }

        return null;
    }

    private static string FormatFieldValue(IDatabaseRecord record, IDatabaseField field)
    {
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var val) || val is null)
            return string.Empty;

        return NotionDatabaseValueFormatter.Format(val);
    }

    private int GetSubItemCount(IDatabaseRecord record)
        => Records.Count(r => r.ParentRecordId == record.Id);

    private static MarkupString FieldTypeIcon(DatabaseFieldType type) => (MarkupString)(type switch
    {
        DatabaseFieldType.Text        => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M4 7h16M4 12h16M4 17h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Number      => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M7 3L5 21M19 3l-2 18M3 9h18M3 15h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Select      => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><circle cx='12' cy='12' r='4' fill='currentColor'/></svg>",
        DatabaseFieldType.MultiSelect => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='2' y='5' width='9' height='6' rx='1' fill='currentColor'/><rect x='13' y='5' width='9' height='6' rx='1' fill='currentColor'/><rect x='2' y='14' width='9' height='6' rx='1' fill='currentColor'/></svg>",
        DatabaseFieldType.Status      => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><path d='M8 12l3 3 5-5' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
        DatabaseFieldType.Date        => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='3' y='4' width='18' height='17' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M16 2v4M8 2v4M3 10h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Checkbox    => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='3' y='3' width='18' height='18' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M7 12l4 4 6-6' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
        DatabaseFieldType.Person      => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='8' r='4' stroke='currentColor' stroke-width='1.5'/><path d='M4 20c0-4 3.6-7 8-7s8 3 8 7' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        _                             => "<svg width='11' height='11' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M4 7h16M4 12h16M4 17h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>"
    });
}
