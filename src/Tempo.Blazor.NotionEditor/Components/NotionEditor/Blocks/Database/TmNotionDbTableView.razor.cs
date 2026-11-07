using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public enum TableRowHeight { Short, Medium, Tall }

public partial class TmNotionDbTableView : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    private ElementReference _tableRef;
    private IJSObjectReference? _resizeSession;
    private DotNetObjectReference<TmNotionDbTableView>? _dotNetRef;
    private readonly string _resizeHintId = $"tm-dbt-resize-hint-{Guid.NewGuid():N}";
    private string ResizeHintId => _resizeHintId;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields  { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseRecord> Records { get; set; } = [];
    [Parameter] public bool           ReadOnly  { get; set; }
    [Parameter] public TableRowHeight RowHeight { get; set; } = TableRowHeight.Medium;
    [Parameter] public bool           WrapCells { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord>                  OnRecordUpdated { get; set; }
    [Parameter] public EventCallback                                   OnNewRecord     { get; set; }
    [Parameter] public EventCallback                                   OnNewField      { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord>                  OnRecordClicked { get; set; }
    [Parameter] public EventCallback<(Guid FieldId, int Width)>        OnFieldResized  { get; set; }

    /// <summary>Raised after the user confirms deleting a row. The block owns the provider call.</summary>
    [Parameter] public EventCallback<IDatabaseRecord>                  OnRecordDeleted { get; set; }
    [Parameter] public EventCallback<(int FromIndex, int ToIndex)>     OnFieldMoved    { get; set; }
    [Parameter] public EventCallback<IDatabaseField>                   OnFieldEdit     { get; set; }
    [Parameter] public IDatabaseField?                                 GroupByField    { get; set; }
    [Parameter] public bool                                            HideEmptyGroups { get; set; }
    [Parameter] public bool                                            ShowSubItems    { get; set; }
    [Parameter] public EventCallback<IDatabaseRecord>                  OnExpandRecord  { get; set; }

    /// <summary>
    /// A coarse pointer (touch). Hides the header drag and the column resize handle; column order
    /// then changes from the Properties panel (the non-drag alternative, WCAG 2.5.7).
    /// </summary>
    [Parameter] public bool CoarsePointer { get; set; }

    private string HeaderDraggable => (!ReadOnly && !CoarsePointer).ToString().ToLowerInvariant();

    // ── Inline edit state ────────────────────────────────────────────────────

    private Guid? _editRecordId;
    private Guid? _editFieldId;

    private bool IsEditing(Guid recordId, Guid fieldId)
        => _editRecordId == recordId && _editFieldId == fieldId;

    private void StartEdit(IDatabaseRecord record, IDatabaseField field)
    {
        if (ReadOnly) return;
        _editRecordId = record.Id;
        _editFieldId  = field.Id;
    }

    private void CancelEdit()
    {
        _editRecordId = null;
        _editFieldId  = null;
    }

    private async Task HandleCellCommitAsync(IDatabaseRecord record, IDatabaseField field, object? newValue)
    {
        _editRecordId = null;
        _editFieldId  = null;

        if (record is DatabaseRecord mutable)
        {
            var dict = new Dictionary<string, object?>(record.Fields)
            {
                [field.Id.ToString()] = newValue
            };
            mutable.Fields = dict;
            await OnRecordUpdated.InvokeAsync(mutable);
        }
    }

    // ── Row selection ─────────────────────────────────────────────────────────

    private readonly HashSet<Guid> _selectedIds = [];

    private bool AllSelected
        => Records.Count > 0 && _selectedIds.Count == Records.Count;

    private void ToggleSelectAll()
    {
        if (AllSelected) _selectedIds.Clear();
        else foreach (var r in Records) _selectedIds.Add(r.Id);
    }

    private void ToggleSelectRow(Guid id)
    {
        if (!_selectedIds.Remove(id)) _selectedIds.Add(id);
    }

    private bool IsSelected(Guid id) => _selectedIds.Contains(id);

    // ── Computed ─────────────────────────────────────────────────────────────

    private IEnumerable<IDatabaseField> VisibleFields
        => Fields.Where(f => f.IsVisible || f.IsPrimary);

    private string RowHeightClass => RowHeight switch
    {
        TableRowHeight.Short => "tm-dbt--short",
        TableRowHeight.Tall  => "tm-dbt--tall",
        _                    => "tm-dbt--medium"
    };

    private int GetColumnWidth(IDatabaseField field)
    {
        if (_columnWidths.TryGetValue(field.Id, out var w)) return w;
        return field.Width ?? (field.IsPrimary ? 200 : 140);
    }

    // ── Column resize ─────────────────────────────────────────────────────────
    // The drag writes the width to the header cell in JS (notion-db-column-resize.js, which
    // imports the shared pointer-drag helper). Blazor state changes only when the gesture ends,
    // so a pointermove never re-renders the table. Alt+Arrow on the handle is the keyboard path.

    private readonly Dictionary<Guid, int> _columnWidths = new();

    private IDatabaseRecord? _pendingDelete;

    private bool AskDeleteRow(IDatabaseRecord record)
    {
        _pendingDelete = record;
        return true;
    }

    private Task OnRowMenuAsync(IDatabaseRecord record, string value) => value switch
    {
        "delete" => Task.FromResult(AskDeleteRow(record)),
        "open"   => OnRecordClicked.InvokeAsync(record),
        _        => Task.CompletedTask
    };

    private Task ConfirmDeleteRowAsync(bool? confirmed)
    {
        var record = _pendingDelete;
        _pendingDelete = null;
        return confirmed == true && record is not null
            ? OnRecordDeleted.InvokeAsync(record)
            : Task.CompletedTask;
    }

    private bool _resizeWanted;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Delegated attach listens on the table root, so later fields and a ReadOnly flip that
        // mounts handles after the first render still resize. Re-attach only when the want-state
        // changes (ReadOnly), not on every render.
        var want = !ReadOnly;
        if (want == _resizeWanted && _resizeSession is not null) return;
        if (!want)
        {
            await DetachResizeAsync();
            _resizeWanted = false;
            return;
        }

        _dotNetRef ??= DotNetObjectReference.Create(this);
        try
        {
            _resizeSession = await JS.InvokeAsync<IJSObjectReference>(
                "tmDb.attachColumnResize", _tableRef, _dotNetRef);
            _resizeWanted = true;
        }
        catch (Exception ex)
        {
            _resizeWanted = false;
            Console.Error.WriteLine($"tmDb.attachColumnResize failed: {ex.Message}");
        }
    }

    private async Task DetachResizeAsync()
    {
        if (_resizeSession is null) return;
        var session = _resizeSession;
        _resizeSession = null;
        try { await session.InvokeVoidAsync("detach"); }
        catch (JSDisconnectedException) { }
        catch (Exception ex) { Console.Error.WriteLine($"column resize detach failed: {ex.Message}"); }
        try { await session.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }

    /// <summary>JS callback. Commits a finished resize — never called per pointermove.</summary>
    [JSInvokable]
    public Task OnColumnResized(string fieldId, int width)
    {
        if (!Guid.TryParse(fieldId, out var id)) return Task.CompletedTask;
        _columnWidths[id] = Math.Max(60, width);
        return OnFieldResized.InvokeAsync((id, _columnWidths[id]));
    }

    public async ValueTask DisposeAsync()
    {
        try { await DetachResizeAsync(); }
        catch (JSDisconnectedException) { }
        catch (Exception ex) { Console.Error.WriteLine($"column resize dispose failed: {ex.Message}"); }
        _dotNetRef?.Dispose();
    }

    // ── Column drag reorder ───────────────────────────────────────────────────

    private int _dragFromFieldIndex = -1;
    private int _dragOverFieldIndex = -1;

    private void OnHeaderDragStart(DragEventArgs e, int fieldIndex)
    {
        // The handle is draggable="false" and preventDefaults its own dragstart. A bubbled
        // dragstart whose type we cannot see still starts a reorder — the handle's own
        // handler stops that. This path is the column-header reorder.
        OnDragStart(fieldIndex);
    }

    private void OnDragStart(int fieldIndex) => _dragFromFieldIndex = fieldIndex;

    private void OnDragOver(int fieldIndex) => _dragOverFieldIndex = fieldIndex;

    private async Task OnDropAsync(int fieldIndex)
    {
        if (_dragFromFieldIndex < 0 || _dragFromFieldIndex == fieldIndex)
        {
            _dragFromFieldIndex = -1;
            _dragOverFieldIndex = -1;
            return;
        }
        var from = _dragFromFieldIndex;
        _dragFromFieldIndex = -1;
        _dragOverFieldIndex = -1;
        await OnFieldMoved.InvokeAsync((from, fieldIndex));
    }

    // ── Sub-items ─────────────────────────────────────────────────────────────

    private readonly HashSet<Guid> _expandedRecordIds = [];

    private void ToggleExpandRecord(IDatabaseRecord record)
    {
        if (!_expandedRecordIds.Remove(record.Id))
        {
            _expandedRecordIds.Add(record.Id);
            if (OnExpandRecord.HasDelegate)
                _ = OnExpandRecord.InvokeAsync(record);
        }
    }

    private bool IsRecordExpanded(Guid id) => _expandedRecordIds.Contains(id);

    private IReadOnlyList<IDatabaseRecord> GetDirectChildren(Guid parentId)
        => Records.Where(r => r.ParentRecordId == parentId).ToList();

    private bool HasChildren(Guid parentId)
        => Records.Any(r => r.ParentRecordId == parentId);

    private bool IsTopLevel(IDatabaseRecord record)
        => record.ParentRecordId is null;

    private IReadOnlyList<IDatabaseRecord> FilterRecordsForGroup(IReadOnlyList<IDatabaseRecord> groupRecords)
        => ShowSubItems
            ? groupRecords.Where(IsTopLevel).ToList()
            : groupRecords;

    // ── Grouping ──────────────────────────────────────────────────────────────

    private readonly HashSet<string> _collapsedGroups = [];

    private void ToggleGroup(string key)
    {
        if (!_collapsedGroups.Remove(key)) _collapsedGroups.Add(key);
    }

    private bool IsGroupCollapsed(string key) => _collapsedGroups.Contains(key);

    private IReadOnlyList<RecordGroup> GetGroups()
    {
        if (GroupByField is null)
            return [new RecordGroup("__all__", null, Records)];

        return Records
            .GroupBy(r => GetGroupKey(r))
            .Where(g => !(HideEmptyGroups && g.Key == "__empty__"))
            .OrderBy(g => g.Key == "__empty__" ? int.MaxValue : 0)
            .Select(g => new RecordGroup(g.Key, GetGroupLabel(g.Key), g.ToList()))
            .ToList();
    }

    private string GetGroupKey(IDatabaseRecord record)
    {
        if (GroupByField is null) return "__all__";
        if (!record.Fields.TryGetValue(GroupByField.Id.ToString(), out var val) || val is null)
            return "__empty__";
        if (val is bool b) return b ? "true" : "false";
        return val.ToString() ?? "__empty__";
    }

    private string GetGroupLabel(string key)
    {
        if (key == "__all__")    return string.Empty;
        if (key == "__empty__")  return Loc["TmNotionDbTableView_GroupNoValue"];
        if (GroupByField?.Type == DatabaseFieldType.Checkbox)
            return key == "true" ? Loc["TmNotionDbTableView_GroupChecked"] : Loc["TmNotionDbTableView_GroupUnchecked"];
        return key;
    }

    private static bool IsOptionLikeField(DatabaseFieldType t)
        => t is DatabaseFieldType.Select or DatabaseFieldType.MultiSelect or DatabaseFieldType.Status;

    internal sealed class RecordGroup(string key, string? label, IReadOnlyList<IDatabaseRecord> records)
    {
        public string                        Key     { get; } = key;
        public string?                       Label   { get; } = label;
        public IReadOnlyList<IDatabaseRecord> Records { get; } = records;
    }

    // Column header icons come from DatabaseFieldIcons (DB-2).
}
