using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// Dialog for configuring and triggering a Gantt export: format tiles (a radio group), the options that apply to
/// the chosen format (paper, orientation, critical path, today line, workload, zoom for PDF / PNG; the columns
/// for Excel) and a preview area.
/// </summary>
public partial class TmGanttExportDialog
{
    private sealed record FormatTile(GanttExportFormat Format, string Slug, string Icon, string LabelKey);

    private static readonly FormatTile[] Tiles =
    [
        new(GanttExportFormat.Pdf, "pdf", "file-text", "TmGantt_ExportPdf"),
        new(GanttExportFormat.Png, "png", "image", "TmGantt_ExportPng"),
        new(GanttExportFormat.Xlsx, "xlsx", "table", "TmGantt_ExportXlsx"),
        new(GanttExportFormat.Xml, "xml", "code", "TmGantt_ExportXml"),
    ];

    /// <summary>The Excel exporter's own default set - the checkboxes start on it, and an untouched set exports as <c>null</c>.</summary>
    private static readonly GanttColumnKey[] DefaultColumns =
    [
        GanttColumnKey.Title, GanttColumnKey.Start, GanttColumnKey.End, GanttColumnKey.Progress,
        GanttColumnKey.Status, GanttColumnKey.Priority, GanttColumnKey.Duration, GanttColumnKey.Assignees,
    ];

    /// <summary>The columns the workbook can carry (the exporter writes nothing for the other keys).</summary>
    private static readonly GanttColumnKey[] ExportableColumns =
    [
        GanttColumnKey.Title, GanttColumnKey.Start, GanttColumnKey.End, GanttColumnKey.Progress,
        GanttColumnKey.Status, GanttColumnKey.Priority, GanttColumnKey.Duration, GanttColumnKey.Deadline,
        GanttColumnKey.Assignees, GanttColumnKey.Estimation, GanttColumnKey.TimeLog, GanttColumnKey.Comments,
    ];

    private static readonly int[] ZoomSteps = [50, 75, 100, 150, 200];

    private static readonly IReadOnlyList<SelectOption<string>> PaperSizes =
    [
        new("A4", "A4"), new("A3", "A3"), new("Letter", "Letter"),
    ];

    private GanttExportFormat _format = GanttExportFormat.Pdf;
    private string? _paperSize = "A4";
    private bool _landscape;
    private bool _includeCriticalPath;
    private bool _includeToday = true;
    private bool _includeWorkload;
    private int? _zoomLevel;
    private HashSet<GanttColumnKey> _columns = [.. DefaultColumns];

    private readonly string _formatName = $"gantt-export-format-{Guid.NewGuid():N}";
    private readonly string _formatLabelId = $"gantt-export-format-label-{Guid.NewGuid():N}";
    private readonly string _columnsLabelId = $"gantt-export-columns-label-{Guid.NewGuid():N}";

    /// <summary>Whether the dialog is visible.</summary>
    [Parameter] public bool IsOpen { get; set; }

    /// <summary>Fires when the user confirms export with the chosen options.</summary>
    [Parameter] public EventCallback<GanttExportOptions> OnExport { get; set; }

    /// <summary>Fires when the dialog should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>
    /// Content of the preview area. When <c>null</c> (default) the area shows the neutral "Preview" placeholder;
    /// a host that renders its own preview (an image of the chart, a table) supplies it here.
    /// </summary>
    [Parameter] public RenderFragment? PreviewContent { get; set; }

    private bool IsImage => _format is GanttExportFormat.Pdf or GanttExportFormat.Png;

    private string FormatClass(GanttExportFormat format) => _format == format
        ? "tm-gantt__export-format-option tm-gantt__export-format-option--selected"
        : "tm-gantt__export-format-option";

    private IReadOnlyList<SelectOption<int?>> ZoomLevels =>
    [
        new(null, Loc["TmGantt_ExportZoomCurrent"]),
        .. ZoomSteps.Select(step => new SelectOption<int?>(step, $"{step} %")),
    ];

    private void SelectFormat(GanttExportFormat format) => _format = format;

    private void SetPaperSize(string? value) => _paperSize = value;

    private void SetLandscape(bool value) => _landscape = value;

    private void SetIncludeCriticalPath(bool value) => _includeCriticalPath = value;

    private void SetIncludeToday(bool value) => _includeToday = value;

    private void SetIncludeWorkload(bool value) => _includeWorkload = value;

    private void SetZoomLevel(int? value) => _zoomLevel = value;

    private bool IsColumnChecked(GanttColumnKey key) => _columns.Contains(key);

    private void SetColumn(GanttColumnKey key, bool on)
    {
        if (on) _columns.Add(key);
        else _columns.Remove(key);
    }

    /// <summary>
    /// The columns to export, in workbook order; <c>null</c> while the set equals the exporter's default so the
    /// exporter keeps owning it (and a host's own exporter sees "no preference"). An empty set is also sent as
    /// <c>null</c>: a workbook with no columns is never what the user wanted.
    /// </summary>
    private IReadOnlyList<GanttColumnKey>? SelectedColumns()
    {
        var chosen = ExportableColumns.Where(_columns.Contains).ToList();
        return chosen.Count == 0 || _columns.SetEquals(DefaultColumns) ? null : chosen;
    }

    private async Task ExportAsync()
    {
        // Only the options that apply to the chosen format travel: a PDF has no columns, a workbook has no paper.
        var opts = IsImage
            ? new GanttExportOptions(
                Format: _format,
                PaperSize: _paperSize,
                Landscape: _landscape,
                ZoomLevel: _zoomLevel,
                IncludeCriticalPath: _includeCriticalPath,
                IncludeToday: _includeToday,
                IncludeWorkload: _includeWorkload)
            : new GanttExportOptions(
                Format: _format,
                Columns: _format == GanttExportFormat.Xlsx ? SelectedColumns() : null);
        await OnExport.InvokeAsync(opts);
        await OnClose.InvokeAsync();
    }
}
