using System.Globalization;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>Generates a timeline diagram from CSV data.</summary>
public static class CsvToTimelineGenerator
{
    public static DiagramDocument Generate(CsvParseResult parseResult, IReadOnlyList<CsvColumnMapping> mappings)
    {
        var dateCol = ResolveColumn(parseResult.Headers, mappings, "Date");
        var eventCol = ResolveColumn(parseResult.Headers, mappings, "Event");

        var doc = CreateBaseDocument("Timeline");
        var page = doc.ActivePage;

        var rowsWithDates = parseResult.Rows
            .Select(r => new
            {
                Row = r,
                DateText = GetValue(r, dateCol),
                EventText = GetValue(r, eventCol),
                ParsedDate = ParseDate(GetValue(r, dateCol))
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.EventText))
            .OrderBy(x => x.ParsedDate)
            .ThenBy(x => x.DateText)
            .ToList();

        const double startX = 40;
        const double startY = 100;
        const double gapX = 40;
        const double nodeW = 200;
        const double nodeH = 40;

        for (int i = 0; i < rowsWithDates.Count; i++)
        {
            var item = rowsWithDates[i];
            var displayName = string.IsNullOrWhiteSpace(item.DateText)
                ? item.EventText
                : $"{item.DateText}: {item.EventText}";

            var node = new DiagramNode
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                StencilId = "project.timeline-bar",
                X = startX + i * (nodeW + gapX),
                Y = startY,
                W = nodeW,
                H = nodeH,
                LayerId = null,
                Data = new Dictionary<string, object> { ["name"] = displayName }
            };
            page.Nodes.Add(node);
        }

        doc.SnapToGrid(8);
        return doc;
    }

    private static DiagramDocument CreateBaseDocument(string title)
    {
        var doc = new DiagramDocument
        {
            Title = title,
            Pages =
            [
                new DiagramPage
                {
                    Name = title,
                    PageSize = DiagramPageSize.A4,
                    PageOrientation = DiagramPageOrientation.Landscape,
                    Width = 1123,
                    Height = 794,
                    Layers = []
                }
            ],
            ActivePageIndex = 0
        };
        return doc;
    }

    private static int ResolveColumn(IReadOnlyList<string> headers, IReadOnlyList<CsvColumnMapping> mappings, string semanticField)
    {
        var mapping = mappings.FirstOrDefault(m =>
            m.SemanticField.Equals(semanticField, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.SelectedColumn));

        if (mapping is null)
            throw new DiagramImportException(DiagramImportError.MappingMissing, $"Column mapping for '{semanticField}' is missing.", field: semanticField);

        for (int i = 0; i < headers.Count; i++)
        {
            if (headers[i].Equals(mapping.SelectedColumn, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        throw new DiagramImportException(DiagramImportError.ColumnNotFound, $"Selected column '{mapping.SelectedColumn}' for '{semanticField}' was not found in CSV headers.", column: mapping.SelectedColumn, field: semanticField);
    }

    private static string GetValue(IReadOnlyList<string> row, int index)
    {
        if (index < 0 || index >= row.Count)
            return string.Empty;
        return row[index];
    }

    private static DateTime ParseDate(string text)
    {
        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
            return dt;
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;

        // Fallback: try common ISO-ish formats
        string[] formats =
        [
            "yyyy-MM-dd",
            "dd.MM.yyyy",
            "MM/dd/yyyy",
            "dd/MM/yyyy",
            "yyyy-MM-dd HH:mm",
            "dd.MM.yyyy HH:mm",
            "MM/dd/yyyy HH:mm"
        ];

        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
            return dt;

        return DateTime.MinValue;
    }
}
