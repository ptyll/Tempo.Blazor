using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;

namespace Tempo.Blazor.Components.NotionEditor.Blocks.Database;

public partial class TmNotionDbListView : TmComponentBase
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

    // ── Field chip rendering ──────────────────────────────────────────────────

    /// <summary>The chip the row shows for one field: the raw value plus the option colour, if any.</summary>
    private (object? Value, string? Color)? ChipOf(IDatabaseRecord record, IDatabaseField field)
    {
        if (!record.Fields.TryGetValue(field.Id.ToString(), out var value) || value is null)
            return null;

        string? color = field.Config switch
        {
            ISelectFieldConfig select => select.Options
                .FirstOrDefault(o => string.Equals(o.Name, value.ToString(), StringComparison.OrdinalIgnoreCase))?.Color,
            IStatusFieldConfig status => status.Groups.SelectMany(g => g.Options)
                .FirstOrDefault(o => string.Equals(o.Name, value.ToString(), StringComparison.OrdinalIgnoreCase))?.Color,
            _ => null
        };

        return NotionDatabaseValueFormatter.Format(value).Length == 0 ? null : (value, color);
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
