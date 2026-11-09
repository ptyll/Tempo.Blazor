using Microsoft.AspNetCore.Components;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbGroupBar : ComponentBase
{
    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField> Fields   { get; set; } = [];
    [Parameter]                 public NotionDatabaseGrouping?        Grouping { get; set; }
    [Parameter]                 public bool                           ReadOnly { get; set; }

    [Parameter] public EventCallback<NotionDatabaseGrouping?> OnGroupingChanged { get; set; }
    [Parameter] public EventCallback                          OnClose           { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private Guid?         _selectedFieldId;
    private bool          _hideEmptyGroups;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private bool          _initialized;

    protected override void OnParametersSet()
    {
        if (!_initialized)
        {
            _selectedFieldId = Grouping?.FieldId;
            _hideEmptyGroups = Grouping?.HideEmptyGroups ?? false;
            _sortDirection   = Grouping?.SortDirection   ?? SortDirection.Ascending;
            _initialized     = true;
        }
    }

    // ── Groupable field types ─────────────────────────────────────────────────

    internal static readonly HashSet<DatabaseFieldType> GroupableTypes =
    [
        DatabaseFieldType.Select,
        DatabaseFieldType.MultiSelect,
        DatabaseFieldType.Status,
        DatabaseFieldType.Person,
        DatabaseFieldType.Checkbox,
        DatabaseFieldType.Date,
        DatabaseFieldType.DateRange,
        DatabaseFieldType.CreatedTime,
        DatabaseFieldType.LastEditedTime,
        DatabaseFieldType.CreatedBy,
        DatabaseFieldType.LastEditedBy
    ];

    internal IEnumerable<IDatabaseField> GroupableFields =>
        Fields.Where(f => GroupableTypes.Contains(f.Type));

    // ── Mutations ─────────────────────────────────────────────────────────────

    private async Task SelectFieldAsync(Guid? fieldId)
    {
        _selectedFieldId = fieldId;
        await EmitAsync();
    }

    private async Task ToggleHideEmptyAsync(bool value)
    {
        _hideEmptyGroups = value;
        await EmitAsync();
    }

    private async Task SetSortDirectionAsync(SortDirection dir)
    {
        _sortDirection = dir;
        await EmitAsync();
    }

    // ── Emit ──────────────────────────────────────────────────────────────────

    private async Task EmitAsync()
    {
        var grouping = _selectedFieldId.HasValue
            ? new NotionDatabaseGrouping(_selectedFieldId.Value, _hideEmptyGroups, _sortDirection)
            : (NotionDatabaseGrouping?)null;
        await OnGroupingChanged.InvokeAsync(grouping);
    }

    // Field type icons come from DatabaseFieldIcons (DB-2).
}
