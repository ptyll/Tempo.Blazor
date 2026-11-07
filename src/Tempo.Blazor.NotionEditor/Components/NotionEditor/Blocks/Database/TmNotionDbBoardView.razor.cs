using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbBoardView : TmComponentBase, IAsyncDisposable
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

    /// <summary>Cascaded layout. A mobile container snaps its lanes and keeps the Move-to menu.</summary>
    [CascadingParameter] private TmLayoutContext? Layout { get; set; }

    private bool IsMobile => Layout?.IsMobile == true;

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

    // The Move-to menu is the non-drag alternative (WCAG 2.5.7). It writes the same group
    // field a drop would, so the host sees one OnRecordUpdated either way.
    private string CurrentGroup(IDatabaseRecord record)
        => GroupByField is not null
           && record.Fields.TryGetValue(GroupByField.Id.ToString(), out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

    private async Task MoveToAsync(IDatabaseRecord record, string groupValue)
    {
        if (record is not DatabaseRecord mutable || GroupByField is null) return;

        var current = record.Fields.TryGetValue(GroupByField.Id.ToString(), out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;
        if (string.Equals(current, groupValue, StringComparison.OrdinalIgnoreCase)) return;

        mutable.Fields = new Dictionary<string, object?>(record.Fields)
        {
            [GroupByField.Id.ToString()] = groupValue.Length > 0 ? groupValue : null
        };
        await OnRecordUpdated.InvokeAsync(mutable);
    }

    // ── Card helpers ─────────────────────────────────────────────────────────

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

    private static object? PropertyValue(IDatabaseRecord record, IDatabaseField field)
    {
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var value) || value is null)
            return null;
        return NotionDatabaseValueFormatter.Format(value).Length == 0 ? null : value;
    }

    private int GetSubItemCount(IDatabaseRecord record)
        => Records.Count(r => r.ParentRecordId == record.Id);

    // Field type icons come from DatabaseFieldIcons (DB-2).
}
