using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>Dialog for configuring and triggering Gantt export.</summary>
public partial class TmGanttExportDialog
{
    private GanttExportFormat _format = GanttExportFormat.Pdf;
    private string? _paperSize = "A4";
    private bool _landscape;
    private readonly string _titleId = $"gantt-export-title-{Guid.NewGuid():N}";

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when the user confirms export with the chosen options.</summary>
    [Parameter] public EventCallback<GanttExportOptions> OnExport { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    private async Task ExportAsync()
    {
        var opts = new GanttExportOptions(
            Format: _format,
            PaperSize: _paperSize,
            Landscape: _landscape);
        await OnExport.InvokeAsync(opts);
        await OnClose.InvokeAsync();
    }
}
