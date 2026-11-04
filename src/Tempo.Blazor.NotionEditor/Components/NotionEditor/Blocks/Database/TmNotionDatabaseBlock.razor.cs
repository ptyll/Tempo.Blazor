using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDatabaseBlock : TmComponentBase
{
    // ── DI ───────────────────────────────────────────────────────────────────

    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Cascaded context ─────────────────────────────────────────────────────

    [CascadingParameter]
    private NotionEditorContext Context { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IPageBlock Block   { get; set; } = default!;
    [Parameter] public bool ReadOnly  { get; set; }
    [Parameter] public bool IsFocused { get; set; }
    [Parameter] public EventCallback             OnFocused    { get; set; }
    [Parameter] public EventCallback<IPageBlock> OnUpdated    { get; set; }
    [Parameter] public EventCallback             OnOpenAsPage { get; set; }

    // ── Derived content ──────────────────────────────────────────────────────

    private IInlineDatabaseBlockContent? _inlineContent;
    private ILinkedDatabaseBlockContent? _linkedContent;
    private Guid                         _databaseId;

    // ── State ────────────────────────────────────────────────────────────────

    private List<IDatabaseField>  _fields  = new();
    private List<IDatabaseView>   _views   = new();
    private List<IDatabaseRecord> _records = new();
    private int                   _totalRecords;
    private Guid                  _activeViewId;
    private IDatabaseView?        _activeView => _views.FirstOrDefault(v => v.Id == _activeViewId);

    private bool    _loading;
    private string? _error;

    private bool           _isEditingTitle;
    private string         _titleBuffer  = string.Empty;
    private ElementReference _titleInputRef;

    private bool                               _showFieldsPanel;
    private bool                               _showFilterPanel;
    private bool                               _showSortPanel;
    private bool                               _showGroupPanel;
    private IDatabaseField?                    _editingField;
    private IDatabaseRecord?                   _openRecord;
    private bool                               _showTemplateMenu;
    private IDatabaseRecordTemplate?           _editingTemplate;
    private bool                               _showSubItems;
    private bool                               _showImportExport;
    private INotionDatabaseFilter?             _localFilter;
    private IReadOnlyList<NotionDatabaseSort>? _localSorts;
    private NotionDatabaseGrouping?            _localGrouping;

    // ── View management ───────────────────────────────────────────────────────
    private bool           _showAddViewPicker;
    private double         _addPickerX;
    private double         _addPickerY;
    private IDatabaseView? _viewContextMenu;
    private double         _viewCtxMenuX;
    private double         _viewCtxMenuY;
    private IDatabaseView? _renamingView;
    private string         _renameBuffer = string.Empty;
    private ElementReference _renameInputRef;

    private IGalleryViewConfig?  GalleryConfig  => _activeView?.Config as IGalleryViewConfig;
    private ICalendarViewConfig? CalendarConfig  => _activeView?.Config as ICalendarViewConfig;
    private ITimelineViewConfig? TimelineConfig  => _activeView?.Config as ITimelineViewConfig;
    private IDatabaseField? BoardGroupField
    {
        get
        {
            var gid = (_localGrouping ?? _activeView?.Grouping)?.FieldId;
            if (gid.HasValue)
                return _fields.FirstOrDefault(f => f.Id == gid.Value);
            return _fields.FirstOrDefault(f => f.Type == DatabaseFieldType.Status)
                ?? _fields.FirstOrDefault(f => f.Type == DatabaseFieldType.Select);
        }
    }

    private IDatabaseField? TableGroupByField
    {
        get
        {
            var gid = (_localGrouping ?? _activeView?.Grouping)?.FieldId;
            return gid.HasValue ? _fields.FirstOrDefault(f => f.Id == gid.Value) : null;
        }
    }

    private Guid _lastLoadedDbId;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnParametersSet()
    {
        _inlineContent = Block.Content as IInlineDatabaseBlockContent;
        _linkedContent = Block.Content as ILinkedDatabaseBlockContent;
        _databaseId    = _inlineContent?.DatabaseId ?? _linkedContent?.SourceDatabaseId ?? Guid.Empty;

        if (_activeViewId == Guid.Empty)
            _activeViewId = _inlineContent?.ActiveViewId ?? _linkedContent?.ActiveViewId ?? Guid.Empty;
    }

    protected override async Task OnInitializedAsync()
        => await LoadAsync();

    protected override async Task OnParametersSetAsync()
    {
        if (_databaseId != _lastLoadedDbId && _databaseId != Guid.Empty)
            await LoadAsync();
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (_databaseId == Guid.Empty)
        {
            _error = null;
            return;
        }

        if (Context.DatabaseProvider is null)
        {
            _error = Loc["TmNotionDatabaseBlock_NoProvider"];
            return;
        }

        _loading        = true;
        _error          = null;
        _lastLoadedDbId = _databaseId;
        StateHasChanged();

        try
        {
            var dbIdStr    = _databaseId.ToString();
            var fieldsTask = Context.DatabaseProvider.GetFieldsAsync(dbIdStr);
            var viewsTask  = Context.DatabaseProvider.GetViewsAsync(dbIdStr);
            await Task.WhenAll(fieldsTask, viewsTask);

            _fields = fieldsTask.Result.ToList();
            _views  = viewsTask.Result.ToList();

            if ((_activeViewId == Guid.Empty || !_views.Any(v => v.Id == _activeViewId)) && _views.Count > 0)
                _activeViewId = _views[0].Id;

            await LoadRecordsAsync();
        }
        catch
        {
            _error = Loc["TmNotionDatabaseBlock_LoadError"];
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadRecordsAsync()
    {
        if (Context.DatabaseProvider is null || _databaseId == Guid.Empty) return;

        var view   = _activeView;
        var result = await Context.DatabaseProvider.GetRecordsAsync(
            _databaseId.ToString(),
            _localFilter   ?? view?.Filter,
            _localSorts    ?? view?.Sorts,
            _localGrouping ?? view?.Grouping,
            page: 1, pageSize: 50);

        _records      = result.Items.ToList();
        _totalRecords = result.TotalCount;
    }

    // ── View switching ───────────────────────────────────────────────────────

    private async Task HandleFilterChangedAsync(INotionDatabaseFilter? filter)
    {
        _localFilter = filter;
        await LoadRecordsAsync();
        StateHasChanged();
    }

    private async Task HandleSortsChangedAsync(IReadOnlyList<NotionDatabaseSort> sorts)
    {
        _localSorts = sorts.Count > 0 ? sorts : null;
        await LoadRecordsAsync();
        StateHasChanged();
    }

    private async Task HandleGroupingChangedAsync(NotionDatabaseGrouping? grouping)
    {
        _localGrouping = grouping;
        await LoadRecordsAsync();
        StateHasChanged();
    }

    private static int CountConditions(INotionDatabaseFilter filter) =>
        filter.Conditions.Count +
        filter.NestedFilters.Sum(CountConditions);

    private async Task SetActiveViewAsync(Guid viewId)
    {
        if (_activeViewId == viewId) return;
        _activeViewId = viewId;
        _localFilter   = null;
        _localSorts    = null;
        _localGrouping = null;
        CloseAllPanels();
        await LoadRecordsAsync();
        StateHasChanged();
    }

    private void ToggleAddViewPicker(MouseEventArgs e)
    {
        if (_showAddViewPicker)
        {
            _showAddViewPicker = false;
        }
        else
        {
            _addPickerX        = e.ClientX;
            _addPickerY        = e.ClientY;
            _showAddViewPicker = true;
            _viewContextMenu   = null;
        }
    }

    private async Task HandleAddViewAsync(DatabaseViewType type)
    {
        _showAddViewPicker = false;
        if (Context.DatabaseProvider is null) return;
        var typeName = type switch
        {
            DatabaseViewType.Board    => Loc["TmNotionDatabaseBlock_ViewBoard"],
            DatabaseViewType.Gallery  => Loc["TmNotionDatabaseBlock_ViewGallery"],
            DatabaseViewType.Calendar => Loc["TmNotionDatabaseBlock_ViewCalendar"],
            DatabaseViewType.Timeline => Loc["TmNotionDatabaseBlock_ViewTimeline"],
            DatabaseViewType.List     => Loc["TmNotionDatabaseBlock_ViewList"],
            _                         => Loc["TmNotionDatabaseBlock_ViewTable"]
        };
        var newView = new DatabaseView
        {
            Name            = typeName,
            Type            = type,
            VisibleFieldIds = _fields.Select(f => f.Id).ToList()
        };
        var created = await Context.DatabaseProvider.CreateViewAsync(_databaseId.ToString(), newView);
        _views.Add(created);
        await SetActiveViewAsync(created.Id);
    }

    private void ShowViewContextMenu(IDatabaseView view, MouseEventArgs e)
    {
        _viewContextMenu   = view;
        _viewCtxMenuX      = e.ClientX;
        _viewCtxMenuY      = e.ClientY;
        _showAddViewPicker = false;
    }

    private void CloseViewContextMenu() => _viewContextMenu = null;

    private void StartRenameView(IDatabaseView view)
    {
        _renamingView    = view;
        _renameBuffer    = view.Name;
        _viewContextMenu = null;
        StateHasChanged();
        _ = Task.Delay(10).ContinueWith(_ =>
        {
            InvokeAsync(async () =>
            {
                try { await _renameInputRef.FocusAsync(); } catch { }
            });
        });
    }

    private async Task CommitRenameViewAsync()
    {
        if (_renamingView is null) return;
        var view = _renamingView;
        _renamingView = null;
        var name = _renameBuffer.Trim();
        if (name.Length == 0 || name == view.Name) { StateHasChanged(); return; }
        if (view is DatabaseView mutable)
        {
            mutable.Name = name;
            if (Context.DatabaseProvider is not null)
            {
                try { await Context.DatabaseProvider.UpdateViewAsync(_databaseId.ToString(), mutable); }
                catch { /* provider may not support it */ }
            }
        }
        StateHasChanged();
    }

    private async Task HandleRenameViewKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Enter")  await CommitRenameViewAsync();
        if (e.Key is "Escape") { _renamingView = null; StateHasChanged(); }
    }

    private async Task DeleteViewAsync(IDatabaseView view)
    {
        _viewContextMenu = null;
        if (_views.Count <= 1) return;
        if (Context.DatabaseProvider is not null)
        {
            try { await Context.DatabaseProvider.DeleteViewAsync(_databaseId.ToString(), view.Id.ToString()); }
            catch { /* provider may not support it */ }
        }
        _views.Remove(view);
        if (_activeViewId == view.Id && _views.Count > 0)
            await SetActiveViewAsync(_views[0].Id);
        StateHasChanged();
    }

    private async Task DuplicateViewAsync(IDatabaseView view)
    {
        _viewContextMenu = null;
        if (Context.DatabaseProvider is null) return;
        try
        {
            var created = await Context.DatabaseProvider.DuplicateViewAsync(_databaseId.ToString(), view.Id.ToString());
            _views.Add(created);
            await SetActiveViewAsync(created.Id);
        }
        catch { /* ignore */ }
    }

    // ── Record actions ───────────────────────────────────────────────────────

    private async Task HandleNewRecordAsync()
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var record  = new DatabaseRecord { DatabaseId = _databaseId };
        var created = await Context.DatabaseProvider.CreateRecordAsync(_databaseId.ToString(), record);
        _records.Add(created);
        _totalRecords++;
        StateHasChanged();
    }

    private async Task HandleCalendarNewRecordAsync(DateTime date)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var record = new DatabaseRecord { DatabaseId = _databaseId };

        var dateField = CalendarConfig is not null
            ? _fields.FirstOrDefault(f => f.Id == CalendarConfig.DateFieldId)
            : _fields.FirstOrDefault(f =>
                f.Type == DatabaseFieldType.Date || f.Type == DatabaseFieldType.DateRange);

        if (dateField is not null)
        {
            record.Fields = new Dictionary<string, object?>
            {
                [dateField.Id.ToString()] = date
            };
        }

        var created = await Context.DatabaseProvider.CreateRecordAsync(_databaseId.ToString(), record);
        _records.Add(created);
        _totalRecords++;
        StateHasChanged();
    }

    private async Task HandleBoardNewRecordAsync(string? groupValue)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var record = new DatabaseRecord { DatabaseId = _databaseId };

        if (groupValue is { Length: > 0 } && BoardGroupField is not null)
        {
            record.Fields = new Dictionary<string, object?>
            {
                [BoardGroupField.Id.ToString()] = groupValue
            };
        }

        var created = await Context.DatabaseProvider.CreateRecordAsync(_databaseId.ToString(), record);
        _records.Add(created);
        _totalRecords++;
        StateHasChanged();
    }

    private Task HandleRecordClickAsync(IDatabaseRecord record)
    {
        CloseAllPanels();
        _openRecord = record;
        return Task.CompletedTask;
    }

    private async Task HandleRecordDetailUpdatedAsync(IDatabaseRecord updated)
    {
        var idx = _records.FindIndex(r => r.Id == updated.Id);
        if (idx >= 0) _records[idx] = updated;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private Task HandleRecordDetailClosedAsync()
    {
        _openRecord = null;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adapts the detail's record callback to the block's parameterless <c>OnOpenAsPage</c>.
    /// The open record is already <c>_openRecord</c>, so the host does not need the record again.
    /// </summary>
    private Task HandleRecordOpenedAsPageAsync(IDatabaseRecord _)
        => OnOpenAsPage.InvokeAsync();

    private string? _deleteError;

    private async Task HandleRecordDeletedAsync(IDatabaseRecord record)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        try
        {
            await Context.DatabaseProvider.DeleteRecordAsync(_databaseId.ToString(), record.Id.ToString());
        }
        catch (Exception)
        {
            _deleteError = Loc["TmNotionDbRecordDetail_DeleteFailed"];
            StateHasChanged();
            throw;
        }

        _deleteError = null;
        _records.RemoveAll(r => r.Id == record.Id);
        if (_totalRecords > 0) _totalRecords--;
        if (_openRecord?.Id == record.Id) _openRecord = null;
        StateHasChanged();
    }

    private async Task HandleRecordUpdatedAsync(IDatabaseRecord record)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        if (record is DatabaseRecord mutable)
        {
            var updated = await Context.DatabaseProvider.UpdateRecordAsync(_databaseId.ToString(), mutable);
            var idx = _records.FindIndex(r => r.Id == updated.Id);
            if (idx >= 0) _records[idx] = updated;
            StateHasChanged();
        }
    }

    private async Task HandleFieldResizedAsync((Guid FieldId, int Width) args)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var field = _fields.FirstOrDefault(f => f.Id == args.FieldId);
        if (field is null) return;
        var updated = new DatabaseField
        {
            Id        = field.Id,
            Name      = field.Name,
            Type      = field.Type,
            IsPrimary = field.IsPrimary,
            Config    = field.Config,
            IsVisible = field.IsVisible,
            Width     = args.Width
        };
        var result = await Context.DatabaseProvider.UpdateFieldAsync(_databaseId.ToString(), updated);
        var idx = _fields.FindIndex(f => f.Id == args.FieldId);
        if (idx >= 0) _fields[idx] = result;
    }

    private async Task HandleFieldMovedAsync((int FromIndex, int ToIndex) args)
    {
        if (ReadOnly || args.FromIndex == args.ToIndex) return;
        var visFields = VisibleFields.ToList();
        if (args.FromIndex < 0 || args.FromIndex >= visFields.Count) return;
        if (args.ToIndex   < 0 || args.ToIndex   >= visFields.Count) return;
        var moved = visFields[args.FromIndex];
        visFields.RemoveAt(args.FromIndex);
        visFields.Insert(args.ToIndex, moved);
        _fields = _fields
            .Where(f => !f.IsVisible && !f.IsPrimary)
            .Concat(visFields)
            .ToList();
        StateHasChanged();
        await Task.CompletedTask;
    }

    private async Task HandleAddFieldAsync()
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var field = new DatabaseField
        {
            Name      = $"Field {_fields.Count + 1}",
            Type      = DatabaseFieldType.Text,
            IsVisible = true,
            Width     = 140
        };
        var created = await Context.DatabaseProvider.CreateFieldAsync(_databaseId.ToString(), field);
        _fields.Add(created);
        StateHasChanged();
    }

    private async Task HandleOpenAsPageAsync()
    {
        if (OnOpenAsPage.HasDelegate)
            await OnOpenAsPage.InvokeAsync();
    }

    // ── Title editing ─────────────────────────────────────────────────────────

    private async Task StartEditTitleAsync()
    {
        if (ReadOnly || _inlineContent is null) return;
        _titleBuffer    = _inlineContent.Title;
        _isEditingTitle = true;
        StateHasChanged();
        await Task.Delay(10);
        try { await _titleInputRef.FocusAsync(); } catch { }
    }

    private async Task CommitTitleAsync()
    {
        if (!_isEditingTitle) return;
        _isEditingTitle = false;
        if (_inlineContent is InlineDatabaseBlockContent editable)
        {
            editable.Title = _titleBuffer.Trim();
            await OnUpdated.InvokeAsync(Block);
        }
    }

    private async Task HandleTitleKeyAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or "Escape")
            await CommitTitleAsync();
    }

    // ── Fields panel ─────────────────────────────────────────────────────────

    private void ToggleFieldsPanel()
    {
        _showFieldsPanel = !_showFieldsPanel;
        if (_showFieldsPanel) { _showFilterPanel = false; _showSortPanel = false; _showGroupPanel = false; _editingField = null; }
    }

    private void ToggleFilterPanel()
    {
        _showFilterPanel = !_showFilterPanel;
        if (_showFilterPanel) { _showFieldsPanel = false; _showSortPanel = false; _showGroupPanel = false; _editingField = null; }
    }

    private void ToggleSortPanel()
    {
        _showSortPanel = !_showSortPanel;
        if (_showSortPanel) { _showFieldsPanel = false; _showFilterPanel = false; _showGroupPanel = false; _editingField = null; }
    }

    private void ToggleGroupPanel()
    {
        _showGroupPanel = !_showGroupPanel;
        if (_showGroupPanel) { _showFieldsPanel = false; _showFilterPanel = false; _showSortPanel = false; _editingField = null; }
    }

    private void CloseAllPanels()
    {
        _showFieldsPanel   = false;
        _showFilterPanel   = false;
        _showSortPanel     = false;
        _showGroupPanel    = false;
        _editingField      = null;
        _openRecord        = null;
        _showTemplateMenu  = false;
        _editingTemplate   = null;
        _showImportExport  = false;
        _showAddViewPicker = false;
        _viewContextMenu   = null;
    }

    private void ToggleImportExport()
    {
        _showImportExport = !_showImportExport;
        if (_showImportExport)
        {
            _showFieldsPanel  = false; _showFilterPanel = false;
            _showSortPanel    = false; _showGroupPanel  = false;
            _editingField     = null;  _openRecord      = null;
            _showTemplateMenu = false; _editingTemplate = null;
        }
    }

    private async Task HandleImportedAsync()
    {
        _showImportExport = false;
        await LoadRecordsAsync();
        StateHasChanged();
    }

    private ElementReference _templateTrigger;

    private async Task CloseTemplateMenuAsync()
    {
        _showTemplateMenu = false;
        try { await _templateTrigger.FocusAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"template menu focus return failed: {ex.Message}"); }
    }

    private void ToggleTemplateMenu()
    {
        _showTemplateMenu = !_showTemplateMenu;
        if (_showTemplateMenu)
        {
            _showFieldsPanel = false; _showFilterPanel = false;
            _showSortPanel   = false; _showGroupPanel  = false;
            _editingField    = null;  _openRecord      = null;
            _editingTemplate = null;
        }
    }

    private async Task HandleTemplateRecordCreatedAsync(IDatabaseRecord record)
    {
        _records.Add(record);
        _totalRecords++;
        _showTemplateMenu = false;
        StateHasChanged();
        await Task.CompletedTask;
    }

    private Task HandleEditTemplateAsync(IDatabaseRecordTemplate template)
    {
        _editingTemplate  = template;
        _showTemplateMenu = false;
        return Task.CompletedTask;
    }

    private async Task HandleTemplateSavedAsync(IDatabaseRecordTemplate template)
    {
        await Task.CompletedTask;
    }

    // ── Sub-items ─────────────────────────────────────────────────────────────

    private async Task HandleExpandRecordAsync(IDatabaseRecord record)
    {
        if (Context.DatabaseProvider is null) return;
        try
        {
            var children = await Context.DatabaseProvider.GetSubItemsAsync(record.Id.ToString());
            var added = false;
            foreach (var child in children)
            {
                if (!_records.Any(r => r.Id == child.Id))
                {
                    _records.Add(child);
                    added = true;
                }
            }
            if (added) StateHasChanged();
        }
        catch { /* ignore */ }
    }

    private void OpenFieldEditor(IDatabaseField field)
    {
        _editingField    = field;
        _showFieldsPanel = false;
        _showFilterPanel = false;
        _showSortPanel   = false;
        _showGroupPanel  = false;
    }

    private async Task HandleFieldEditorChangedAsync(IDatabaseField field)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var result = await Context.DatabaseProvider.UpdateFieldAsync(_databaseId.ToString(), field);
        var idx    = _fields.FindIndex(f => f.Id == result.Id);
        if (idx >= 0) _fields[idx] = result;
        _editingField = null;
        StateHasChanged();
    }

    private async Task HandleFieldEditorDuplicatedAsync(IDatabaseField field)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var dup = new DatabaseField
        {
            Name      = field.Name + " (2)",
            Type      = field.Type,
            IsPrimary = false,
            Config    = field.Config,
            IsVisible = field.IsVisible,
            Width     = field.Width
        };
        var created = await Context.DatabaseProvider.CreateFieldAsync(_databaseId.ToString(), dup);
        var idx     = _fields.FindIndex(f => f.Id == field.Id);
        _fields.Insert(idx >= 0 ? idx + 1 : _fields.Count, created);
        _editingField = null;
        StateHasChanged();
    }

    private async Task HandleFieldEditorDeletedAsync(Guid fieldId)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        await Context.DatabaseProvider.DeleteFieldAsync(_databaseId.ToString(), fieldId.ToString());
        _fields.RemoveAll(f => f.Id == fieldId);
        _editingField = null;
        StateHasChanged();
    }

    private async Task ToggleFieldVisibilityAsync(IDatabaseField field, bool visible)
    {
        if (Context.DatabaseProvider is null || ReadOnly) return;
        var updated = new DatabaseField
        {
            Id        = field.Id,
            Name      = field.Name,
            Type      = field.Type,
            IsPrimary = field.IsPrimary,
            Config    = field.Config,
            IsVisible = visible,
            Width     = field.Width
        };
        var result = await Context.DatabaseProvider.UpdateFieldAsync(_databaseId.ToString(), updated);
        var idx    = _fields.FindIndex(f => f.Id == field.Id);
        if (idx >= 0) _fields[idx] = result;
        StateHasChanged();
    }

    private async Task ShowAllFieldsAsync()
    {
        foreach (var f in _fields.Where(fld => !fld.IsVisible).ToList())
            await ToggleFieldVisibilityAsync(f, true);
    }

    private async Task HideAllFieldsAsync()
    {
        foreach (var f in _fields.Where(fld => fld.IsVisible && !fld.IsPrimary).ToList())
            await ToggleFieldVisibilityAsync(f, false);
    }

    // ── Block context menu ───────────────────────────────────────────────────

    private bool   _blockCtxOpen;
    private double _blockCtxX;
    private double _blockCtxY;
    private bool   _copyIdDone;

    private Task HandleContextMenuAsync(MouseEventArgs e)
    {
        CloseAllPanels();
        _blockCtxX    = e.ClientX;
        _blockCtxY    = e.ClientY;
        _blockCtxOpen = true;
        return Task.CompletedTask;
    }

    private Task HandleRootKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key is "Escape" && _blockCtxOpen)
        {
            _blockCtxOpen = false;
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    private void CloseBlockContextMenu()
    {
        _blockCtxOpen = false;
        _copyIdDone   = false;
    }

    private async Task CtxRenameAsync()
    {
        _blockCtxOpen = false;
        await StartEditTitleAsync();
    }

    private async Task CtxOpenAsPageAsync()
    {
        _blockCtxOpen = false;
        await HandleOpenAsPageAsync();
    }

    private Task CtxExportAsync()
    {
        _blockCtxOpen = false;
        _showImportExport = true;
        return Task.CompletedTask;
    }

    private async Task CtxCopyIdAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", _databaseId.ToString());
        }
        catch { /* clipboard may be blocked in some browsers */ }
        _copyIdDone = true;
        StateHasChanged();
        await Task.Delay(1500);
        _copyIdDone   = false;
        _blockCtxOpen = false;
        StateHasChanged();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IEnumerable<IDatabaseField> VisibleFields
    {
        get
        {
            var visIds = _activeView?.VisibleFieldIds;
            return visIds is { Count: > 0 }
                ? _fields.Where(f => visIds.Contains(f.Id))
                : _fields.Where(f => f.IsVisible);
        }
    }

    private static string FormatCellValue(IDatabaseRecord record, IDatabaseField field)
    {
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var val) || val is null)
            return string.Empty;

        return NotionDatabaseValueFormatter.Format(val);
    }

    private static MarkupString ViewTypeIcon(DatabaseViewType type) => (MarkupString)(type switch
    {
        DatabaseViewType.Table    => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='3' y='3' width='18' height='18' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M3 9h18M3 15h18M9 3v18M15 3v18' stroke='currentColor' stroke-width='1.5'/></svg>",
        DatabaseViewType.Board    => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='2' y='4' width='5' height='16' rx='1' stroke='currentColor' stroke-width='1.5'/><rect x='9' y='4' width='5' height='11' rx='1' stroke='currentColor' stroke-width='1.5'/><rect x='16' y='4' width='6' height='14' rx='1' stroke='currentColor' stroke-width='1.5'/></svg>",
        DatabaseViewType.Gallery  => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='2' y='2' width='9' height='9' rx='1' stroke='currentColor' stroke-width='1.5'/><rect x='13' y='2' width='9' height='9' rx='1' stroke='currentColor' stroke-width='1.5'/><rect x='2' y='13' width='9' height='9' rx='1' stroke='currentColor' stroke-width='1.5'/><rect x='13' y='13' width='9' height='9' rx='1' stroke='currentColor' stroke-width='1.5'/></svg>",
        DatabaseViewType.Calendar => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='3' y='4' width='18' height='17' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M16 2v4M8 2v4M3 10h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseViewType.Timeline => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M3 6h18M3 12h14M3 18h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/><rect x='5' y='4' width='8' height='4' rx='1' fill='currentColor'/><rect x='5' y='10' width='5' height='4' rx='1' fill='currentColor'/></svg>",
        DatabaseViewType.List     => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M9 6h12M9 12h12M9 18h12M5 6h.01M5 12h.01M5 18h.01' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        _                         => string.Empty
    });

    private static MarkupString FieldTypeIcon(DatabaseFieldType type) => (MarkupString)(type switch
    {
        DatabaseFieldType.Text            => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M4 7h16M4 12h16M4 17h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Number          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M7 3L5 21M19 3l-2 18M3 9h18M3 15h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Select          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><circle cx='12' cy='12' r='4' fill='currentColor'/></svg>",
        DatabaseFieldType.MultiSelect     => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='2' y='5' width='9' height='6' rx='1' fill='currentColor'/><rect x='13' y='5' width='9' height='6' rx='1' fill='currentColor'/><rect x='2' y='14' width='9' height='6' rx='1' fill='currentColor'/></svg>",
        DatabaseFieldType.Status          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><path d='M8 12l3 3 5-5' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
        DatabaseFieldType.Date or DatabaseFieldType.DateRange
                                          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='3' y='4' width='18' height='17' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M16 2v4M8 2v4M3 10h18' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Checkbox        => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='3' y='3' width='18' height='18' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M7 12l4 4 6-6' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
        DatabaseFieldType.Person          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='8' r='4' stroke='currentColor' stroke-width='1.5'/><path d='M4 20c0-4 3.6-7 8-7s8 3 8 7' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Url             => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/><path d='M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.Email           => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><rect x='2' y='4' width='20' height='16' rx='2' stroke='currentColor' stroke-width='1.5'/><path d='M2 8l10 6 10-6' stroke='currentColor' stroke-width='1.5'/></svg>",
        DatabaseFieldType.Phone           => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M5 4h4l2 5-2.5 1.5a11 11 0 0 0 4 4L14 12l5 2v4a2 2 0 0 1-2 2A16 16 0 0 1 3 6a2 2 0 0 1 2-2z' stroke='currentColor' stroke-width='1.5' stroke-linejoin='round'/></svg>",
        DatabaseFieldType.Files           => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z' stroke='currentColor' stroke-width='1.5'/><polyline points='14 2 14 8 20 8' stroke='currentColor' stroke-width='1.5'/></svg>",
        DatabaseFieldType.CreatedTime or DatabaseFieldType.LastEditedTime
                                          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='12' cy='12' r='9' stroke='currentColor' stroke-width='1.5'/><path d='M12 7v5l3 3' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>",
        DatabaseFieldType.CreatedBy or DatabaseFieldType.LastEditedBy
                                          => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><circle cx='9' cy='7' r='4' stroke='currentColor' stroke-width='1.5'/><path d='M3 21v-2a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v2' stroke='currentColor' stroke-width='1.5'/><path d='M16 11l2 2 4-4' stroke='currentColor' stroke-width='1.5' stroke-linecap='round' stroke-linejoin='round'/></svg>",
        _                                 => "<svg width='12' height='12' viewBox='0 0 24 24' fill='none' aria-hidden='true'><path d='M4 7h16M4 12h16M4 17h10' stroke='currentColor' stroke-width='1.5' stroke-linecap='round'/></svg>"
    });
}
