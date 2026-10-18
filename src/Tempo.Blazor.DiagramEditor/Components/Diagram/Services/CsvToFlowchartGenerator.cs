using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram.Services;

/// <summary>Generates a flowchart diagram from CSV data.</summary>
public static class CsvToFlowchartGenerator
{
    public static DiagramDocument Generate(CsvParseResult parseResult, IReadOnlyList<CsvColumnMapping> mappings)
    {
        var fromCol = ResolveColumn(parseResult.Headers, mappings, "From");
        var toCol = ResolveColumn(parseResult.Headers, mappings, "To");
        var labelCol = mappings.FirstOrDefault(m =>
            m.SemanticField.Equals("Label", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(m.SelectedColumn));

        int? labelIndex = null;
        if (labelCol is not null)
        {
            for (int i = 0; i < parseResult.Headers.Count; i++)
            {
                if (parseResult.Headers[i].Equals(labelCol.SelectedColumn, StringComparison.OrdinalIgnoreCase))
                {
                    labelIndex = i;
                    break;
                }
            }
        }

        var doc = CreateBaseDocument("Flowchart");
        var page = doc.ActivePage;

        var nodeIdByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in parseResult.Rows)
        {
            var fromName = GetValue(row, fromCol);
            var toName = GetValue(row, toCol);
            if (!string.IsNullOrWhiteSpace(fromName))
                allNames.Add(fromName);
            if (!string.IsNullOrWhiteSpace(toName))
                allNames.Add(toName);
        }

        double x = 40;
        double y = 40;
        foreach (var name in allNames)
        {
            var node = new DiagramNode
            {
                Id = Guid.NewGuid().ToString("N")[..8],
                StencilId = "flowchart.process",
                X = x,
                Y = y,
                W = 160,
                H = 80,
                LayerId = null,
                Data = new Dictionary<string, object> { ["label"] = name }
            };
            nodeIdByName[name] = node.Id;
            page.Nodes.Add(node);
            x += 200;
            if (x > 1000)
            {
                x = 40;
                y += 140;
            }
        }

        var addedEdges = new HashSet<(string Source, string Target)>();
        foreach (var row in parseResult.Rows)
        {
            var fromName = GetValue(row, fromCol);
            var toName = GetValue(row, toCol);
            if (string.IsNullOrWhiteSpace(fromName) || string.IsNullOrWhiteSpace(toName))
                continue;
            if (!nodeIdByName.TryGetValue(fromName, out var sourceId))
                continue;
            if (!nodeIdByName.TryGetValue(toName, out var targetId))
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
                EndArrow = "classic",
                Label = labelIndex.HasValue ? GetValue(row, labelIndex.Value) : null
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
