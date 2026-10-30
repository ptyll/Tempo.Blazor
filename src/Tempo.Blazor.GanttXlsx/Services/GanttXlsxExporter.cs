using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.WorkItems;

namespace Tempo.Blazor.Services;

/// <summary>Exports Gantt tasks to an XLSX spreadsheet.</summary>
public static class GanttXlsxExporter
{
    private static readonly GanttColumnKey[] DefaultColumns =
    [
        GanttColumnKey.Title, GanttColumnKey.Start, GanttColumnKey.End,
        GanttColumnKey.Progress, GanttColumnKey.Status, GanttColumnKey.Priority,
        GanttColumnKey.Duration, GanttColumnKey.Assignees
    ];

    /// <summary>Exports Gantt work items and dependencies to an XLSX workbook.</summary>
    /// <param name="tasks">Work items to export.</param>
    /// <param name="dependencies">Dependencies between exported work items.</param>
    /// <param name="options">Export options including selected columns.</param>
    /// <returns>XLSX workbook bytes.</returns>
    public static byte[] Export(
        IEnumerable<TmWorkItem> tasks,
        IEnumerable<GanttDependency> dependencies,
        GanttExportOptions options)
    {
        var cols = options.Columns?.ToArray() ?? DefaultColumns;
        var taskList = tasks.ToList();

        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbp = doc.AddWorkbookPart();
            wbp.Workbook = new Workbook();

            var wsp = wbp.AddNewPart<WorksheetPart>();
            var sd  = new SheetData();
            wsp.Worksheet = new Worksheet(sd);

            wbp.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = wbp.GetIdOfPart(wsp),
                SheetId = 1,
                Name = "Tasks"
            });

            // Header row
            sd.Append(BuildRow(cols.Select(c => c.ToString())));

            // Data rows
            foreach (var task in taskList)
                sd.Append(BuildRow(cols.Select(c => GetCellValue(task, c))));

            doc.Save();
        }

        return ms.ToArray();
    }

    private static Row BuildRow(IEnumerable<string> values)
    {
        var row = new Row();
        foreach (var v in values)
            row.Append(new Cell { CellValue = new CellValue(v), DataType = CellValues.InlineString });
        return row;
    }

    private static string GetCellValue(TmWorkItem task, GanttColumnKey col) => GanttColumnValues.Format(task, col);
}
