using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbListView : ComponentBase
{
    // ── Parameters ───────────────────────────────────────────────────────────

    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseField>  Fields          { get; set; } = [];
    [Parameter, EditorRequired] public IReadOnlyList<IDatabaseRecord> Records         { get; set; } = [];
    [Parameter]                 public IReadOnlyList<Guid>?           PreviewFieldIds { get; set; }
    [Parameter]                 public bool                           ReadOnly        { get; set; }

    [Parameter] public EventCallback<IDatabaseRecord> OnRecordClicked { get; set; }
    [Parameter] public EventCallback                  OnNewRecord     { get; set; }

    // ── Field helpers ─────────────────────────────────────────────────────────

    private IDatabaseField? PrimaryField => Fields.FirstOrDefault(f => f.IsPrimary);

    private IEnumerable<IDatabaseField> PreviewFields
    {
        get
        {
            if (PreviewFieldIds is { Count: > 0 })
                return PreviewFieldIds
                    .Select(id => Fields.FirstOrDefault(f => f.Id == id))
                    .OfType<IDatabaseField>()
                    .Where(f => !f.IsPrimary)
                    .Take(5);

            return Fields.Where(f => !f.IsPrimary && f.IsVisible).Take(5);
        }
    }

    private string GetPrimaryValue(IDatabaseRecord record)
    {
        var pf = PrimaryField;
        if (pf is null) return string.Empty;
        return record.Fields.TryGetValue(pf.Id.ToString(), out var v)
            ? NotionDatabaseValueFormatter.Format(v, "MMM d")
            : string.Empty;
    }

    // ── Field chip rendering ──────────────────────────────────────────────────

    // Returns (displayValue, cssColor?) — one tuple per visible chip
    internal IEnumerable<(string Value, string? Color)> GetFieldChips(IDatabaseRecord record, IDatabaseField field)
    {
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var v) || v is null)
            return [];

        switch (field.Type)
        {
            case DatabaseFieldType.Status when field.Config is IStatusFieldConfig sc:
            {
                var val = v.ToString() ?? string.Empty;
                if (val.Length == 0) return [];
                var opt = sc.Groups.SelectMany(g => g.Options)
                    .FirstOrDefault(o => string.Equals(o.Name, val, StringComparison.OrdinalIgnoreCase));
                return [(val, NotionColorTokens.OptionSolid(opt?.Color))];
            }

            case DatabaseFieldType.Select when field.Config is ISelectFieldConfig sel:
            {
                var val = v.ToString() ?? string.Empty;
                if (val.Length == 0) return [];
                var opt = sel.Options
                    .FirstOrDefault(o => string.Equals(o.Name, val, StringComparison.OrdinalIgnoreCase));
                return [(val, NotionColorTokens.OptionSolid(opt?.Color))];
            }

            case DatabaseFieldType.MultiSelect when field.Config is ISelectFieldConfig msel:
            {
                var vals = v switch
                {
                    string[] arr            => arr,
                    IEnumerable<string> lst => lst.ToArray(),
                    string s                => s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    _                       => Array.Empty<string>()
                };
                return vals
                    .Where(val => val.Length > 0)
                    .Select(val =>
                    {
                        var opt = msel.Options.FirstOrDefault(o =>
                            string.Equals(o.Name, val, StringComparison.OrdinalIgnoreCase));
                        return (val, (string?)NotionColorTokens.OptionSolid(opt?.Color));
                    });
            }

            case DatabaseFieldType.Checkbox:
            {
                if (v is bool b && !b) return [];
                return [("✓", null)];
            }

            default:
            {
                var formatted = NotionDatabaseValueFormatter.Format(v, "MMM d");
                return formatted.Length > 0 ? [(formatted, null)] : [];
            }
        }
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private async Task HandleRowClickAsync(IDatabaseRecord record)
        => await OnRecordClicked.InvokeAsync(record);

    private async Task HandleRowKeyAsync(KeyboardEventArgs e, IDatabaseRecord record)
    {
        if (e.Key is "Enter" or " ")
            await OnRecordClicked.InvokeAsync(record);
    }
}
