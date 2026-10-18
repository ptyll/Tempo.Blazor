using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>Generates an org-chart diagram from CSV data.</summary>
public static class CsvToOrgChartGenerator
{
    public static DiagramDocument Generate(CsvParseResult parseResult, IReadOnlyList<CsvColumnMapping> mappings)
    {
        var nameCol = ResolveColumn(parseResult.Headers, mappings, "Name");
        var managerCol = ResolveColumn(parseResult.Headers, mappings, "Manager");

        var doc = CreateBaseDocument("Org Chart");
        var page = doc.ActivePage;

        var nodeIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in parseResult.Rows)
        {
            var name = GetValue(row, nameCol);
            if (!string.IsNullOrWhiteSpace(name))
                allNames.Add(name);

            var manager = GetValue(row, managerCol);
            if (!string.IsNullOrWhiteSpace(manager))
                allNames.Add(manager);
        }

        double x = 40;
        double y = 40;
        foreach (var name in allNames)
        {
            var node = new DiagramNode
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                StencilId = "general.rectangle",
                X = x,
                Y = y,
                W = 180,
                H = 60,
                LayerId = null,
                Data = new Dictionary<string, object> { ["label"] = name }
            };
            nodeIdByName[name] = node.Id;
            page.Nodes.Add(node);
            x += 220;
            if (x > 1000)
            {
                x = 40;
                y += 120;
            }
        }

        var addedEdges = new HashSet<(string Source, string Target)>();
        foreach (var row in parseResult.Rows)
        {
            var name = GetValue(row, nameCol);
            var manager = GetValue(row, managerCol);
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(manager))
                continue;
            if (!nodeIdByName.TryGetValue(manager, out var sourceId))
                continue;
            if (!nodeIdByName.TryGetValue(name, out var targetId))
                continue;
            if (sourceId == targetId)
                continue;

            var key = (sourceId, targetId);
            if (addedEdges.Contains(key))
                continue;
            addedEdges.Add(key);

            page.Edges.Add(new DiagramEdge
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                SourceNodeId = sourceId,
                TargetNodeId = targetId,
                Routing = "straight",
                ConnectorType = "association",
                EndArrow = "classic"
            });
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
}
