using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Dialog for mapping source spreadsheet columns to Gantt task properties.</summary>
public partial class TmGanttImportMappingDialog
{
    private readonly Dictionary<string, GanttColumnKey?> _mappings = new();
    private readonly string _titleId = $"gantt-mapping-title-{Guid.NewGuid():N}";

    private static readonly GanttColumnKey[] AvailableTargets =
    [
        GanttColumnKey.Title,
        GanttColumnKey.Start,
        GanttColumnKey.End,
        GanttColumnKey.Progress,
        GanttColumnKey.Status,
        GanttColumnKey.Priority,
        GanttColumnKey.Duration,
        GanttColumnKey.Deadline,
        GanttColumnKey.Estimation,
        GanttColumnKey.TimeLog,
    ];

    /// <summary>Source column names from the imported file.</summary>
    [Parameter] public IEnumerable<string> SourceColumns { get; set; } = [];

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when the user confirms the mapping.</summary>
    [Parameter] public EventCallback<IReadOnlyList<GanttColumnMapping>> OnImport { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        foreach (var col in SourceColumns)
        {
            if (!_mappings.ContainsKey(col))
                _mappings[col] = null;
        }
    }

    private string GetMappingValue(string col) =>
        _mappings.TryGetValue(col, out var v) && v.HasValue ? v.Value.ToString() : string.Empty;

    private void SetMapping(string col, string? value)
    {
        if (string.IsNullOrEmpty(value))
            _mappings[col] = null;
        else if (Enum.TryParse<GanttColumnKey>(value, out var key))
            _mappings[col] = key;
    }

    private async Task ConfirmAsync()
    {
        var result = _mappings
            .Where(kv => kv.Value.HasValue)
            .Select(kv => new GanttColumnMapping { SourceColumn = kv.Key, TargetProperty = kv.Value!.Value })
            .ToList();
        await OnImport.InvokeAsync(result);
        await OnClose.InvokeAsync();
    }
}
