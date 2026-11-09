using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.NotionEditor.UI;

/// <summary>Result layout of <see cref="TmNotionAuditLogPanel"/>.</summary>
public enum TmNotionAuditLogLayout
{
    /// <summary>The container query decides: a table on a wide container, cards on a narrow one.</summary>
    Auto,

    /// <summary>Always the table (with column headers), regardless of the container width.</summary>
    Table,

    /// <summary>Always the cards, regardless of the container width.</summary>
    Cards,
}

/// <summary>Filterable audit log panel for Notion workspace actions.</summary>
public partial class TmNotionAuditLogPanel : TmComponentBase
{
    private static readonly IReadOnlyList<string> AvailableActions = ["create", "edit", "delete", "move", "restrict"];
    private const int DefaultPageSize = 10;
    private const int ExportLimit = 1000;

    /// <summary>Provider used to query activity entries.</summary>
    [Parameter, EditorRequired] public ITmActivityProvider ActivityProvider { get; set; } = default!;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when the close button is clicked.</summary>
    [Parameter] public EventCallback OnClosed { get; set; }

    /// <summary>Number of audit entries shown per page.</summary>
    [Parameter] public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// Result layout: <see cref="TmNotionAuditLogLayout.Auto"/> (default) renders both the table
    /// and the cards and lets the panel's own container query pick; <see cref="TmNotionAuditLogLayout.Table"/>
    /// / <see cref="TmNotionAuditLogLayout.Cards"/> pin one of them.
    /// </summary>
    [Parameter] public TmNotionAuditLogLayout Layout { get; set; } = TmNotionAuditLogLayout.Auto;

    private string LayoutModifier => Layout switch
    {
        TmNotionAuditLogLayout.Table => "tm-notion-audit--table",
        TmNotionAuditLogLayout.Cards => "tm-notion-audit--cards",
        _ => string.Empty,
    };

    private readonly HashSet<string> _expandedEntryIds = new(StringComparer.Ordinal);

    private bool _filtersSheetOpen;

    private int ActiveFilterCount =>
        (string.IsNullOrWhiteSpace(_userFilter) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(_actionFilter) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(_fromFilter) ? 0 : 1)
        + (string.IsNullOrWhiteSpace(_toFilter) ? 0 : 1);

    private bool IsExpanded(TmActivityEntry entry) => _expandedEntryIds.Contains(entry.Id);

    private void ToggleExpanded(TmActivityEntry entry)
    {
        if (!_expandedEntryIds.Remove(entry.Id))
            _expandedEntryIds.Add(entry.Id);
        StateHasChanged();
    }

    private Task OpenFiltersSheetAsync()
    {
        _filtersSheetOpen = true;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task CloseFiltersSheet()
    {
        _filtersSheetOpen = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private async Task ApplyFiltersFromSheetAsync()
    {
        _filtersSheetOpen = false;
        await ApplyFiltersAsync();
    }

    private async Task ClearFiltersFromSheetAsync()
    {
        _filtersSheetOpen = false;
        await ClearFiltersAsync();
    }

    private async Task HandleMoreActionAsync(string value)
    {
        if (string.Equals(value, "export", StringComparison.OrdinalIgnoreCase))
            await PrepareCsvExportAsync();
    }

    /// <summary>
    /// Translates a known audit metadata key (<c>title</c>, <c>parent</c>, <c>changes</c>,
    /// <c>reason</c>, <c>user</c>, <c>permission</c>) through <c>Notion_Audit_Meta_*</c>; unknown
    /// keys render verbatim.
    /// </summary>
    private string TranslateMetaKey(string key)
    {
        var normalized = ToPascalKey(key);
        return Loc.TryGetString($"Notion_Audit_Meta_{normalized}", out var label)
            ? label
            : key;
    }

    /// <summary>Translates a known entity type (<c>page</c>, <c>database</c>); unknown types render verbatim.</summary>
    private string GetEntityTypeLabel(string entityType)
    {
        var normalized = ToPascalKey(entityType);
        return Loc.TryGetString($"Notion_Audit_Entity_{normalized}", out var label)
            ? label
            : entityType;
    }

    /// <summary>"title" → "Title"; keeps the resource-key family PascalCase while unknown shapes stay safe.</summary>
    private static string ToPascalKey(string value)
    {
        var normalized = NormalizeCssToken(value);
        return normalized.Length == 0
            ? normalized
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }

    private PagedResult<TmActivityEntry> _result = new() { Page = 1, PageSize = DefaultPageSize };
    private string _userFilter = string.Empty;
    private string _actionFilter = string.Empty;
    private string _fromFilter = string.Empty;
    private string _toFilter = string.Empty;
    private int _skip;
    private bool _isLoading;
    private string? _loadError;
    private string? _csvHref;

    private string CsvFileName => $"notion-audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";

    protected override async Task OnInitializedAsync()
        => await LoadAsync();

    private async Task LoadAsync()
    {
        _isLoading = true;
        _loadError = null;
        _csvHref = null;
        StateHasChanged();

        try
        {
            _result = await ActivityProvider.QueryAsync(BuildQuery());
        }
        catch
        {
            _loadError = Loc["Notion_Audit_LoadError"];
            _result = new PagedResult<TmActivityEntry> { Page = 1, PageSize = EffectivePageSize };
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task ApplyFiltersAsync()
    {
        _skip = 0;
        await LoadAsync();
    }

    private async Task ClearFiltersAsync()
    {
        _userFilter = string.Empty;
        _actionFilter = string.Empty;
        _fromFilter = string.Empty;
        _toFilter = string.Empty;
        _skip = 0;
        await LoadAsync();
    }

    private async Task PreviousPageAsync()
    {
        if (!_result.HasPreviousPage)
            return;

        _skip = Math.Max(0, _skip - EffectivePageSize);
        await LoadAsync();
    }

    private async Task NextPageAsync()
    {
        if (!_result.HasNextPage)
            return;

        _skip += EffectivePageSize;
        await LoadAsync();
    }

    private async Task PrepareCsvExportAsync()
    {
        var exportQuery = BuildQuery();
        exportQuery.Skip = 0;
        exportQuery.Take = ExportLimit;
        var exportResult = await ActivityProvider.QueryAsync(exportQuery);

        var csv = BuildCsv(exportResult.Items);
        _csvHref = "data:text/csv;charset=utf-8," + Uri.EscapeDataString(csv);
    }

    private void OnUserFilterInput(ChangeEventArgs args)
        => _userFilter = args.Value?.ToString() ?? string.Empty;

    private void OnActionFilterChanged(ChangeEventArgs args)
        => _actionFilter = args.Value?.ToString() ?? string.Empty;

    private void OnFromFilterChanged(ChangeEventArgs args)
        => _fromFilter = args.Value?.ToString() ?? string.Empty;

    private void OnToFilterChanged(ChangeEventArgs args)
        => _toFilter = args.Value?.ToString() ?? string.Empty;

    private TmActivityQuery BuildQuery()
        => new()
        {
            EntityType = "page",
            SearchText = string.IsNullOrWhiteSpace(_userFilter) ? null : _userFilter.Trim(),
            Action = string.IsNullOrWhiteSpace(_actionFilter) ? null : _actionFilter.Trim(),
            From = ParseDate(_fromFilter, endOfDay: false),
            To = ParseDate(_toFilter, endOfDay: true),
            Skip = _skip,
            Take = EffectivePageSize
        };

    private int EffectivePageSize => Math.Clamp(PageSize, 1, 50);

    private static DateTimeOffset? ParseDate(string value, bool endOfDay)
    {
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        var time = endOfDay ? TimeOnly.MaxValue : TimeOnly.MinValue;
        return new DateTimeOffset(DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Local)).ToUniversalTime();
    }

    private string BuildCsv(IReadOnlyList<TmActivityEntry> entries)
    {
        var builder = new StringBuilder();
        builder.Append(CsvCell(Loc["Notion_Audit_Timestamp"]));
        builder.Append(',');
        builder.Append(CsvCell(Loc["Notion_Audit_User"]));
        builder.Append(',');
        builder.Append(CsvCell(Loc["Notion_Audit_Action"]));
        builder.Append(',');
        builder.Append(CsvCell(Loc["Notion_Audit_Target"]));
        builder.Append(',');
        builder.Append(CsvCell(Loc["Notion_Audit_Details"]));
        builder.AppendLine();

        foreach (var entry in entries)
        {
            builder.Append(CsvCell(entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            builder.Append(',');
            builder.Append(CsvCell(GetUserDisplay(entry)));
            builder.Append(',');
            builder.Append(CsvCell(GetActionLabel(entry.Action)));
            builder.Append(',');
            builder.Append(CsvCell($"{entry.EntityRef.EntityType}:{entry.EntityRef.EntityId}"));
            builder.Append(',');
            builder.Append(CsvCell(FormatDetails(entry)));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private string GetActionLabel(string action)
        => action.ToLowerInvariant() switch
        {
            "create" => Loc["Notion_Audit_Action_Create"],
            "edit" => Loc["Notion_Audit_Action_Edit"],
            "delete" => Loc["Notion_Audit_Action_Delete"],
            "move" => Loc["Notion_Audit_Action_Move"],
            "restrict" => Loc["Notion_Audit_Action_Restrict"],
            _ => action
        };

    private static string GetUserDisplay(TmActivityEntry entry)
        => string.IsNullOrWhiteSpace(entry.Actor?.DisplayName) ? entry.Actor?.Id ?? string.Empty : entry.Actor.DisplayName;

    private static string ShortenTargetId(string targetId)
        => Guid.TryParse(targetId, out var id) ? id.ToString("D")[..8] : targetId;

    private static string FormatTimestamp(DateTimeOffset timestamp)
        => timestamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private static string FormatDetails(TmActivityEntry entry)
        => string.Join("; ", (entry.Metadata ?? new Dictionary<string, object>())
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => $"{pair.Key}={pair.Value}"));

    private static string NormalizeCssToken(string value)
        => string.Concat(value.ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '-'));

    private static string CsvCell(string value)
        => '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
}
