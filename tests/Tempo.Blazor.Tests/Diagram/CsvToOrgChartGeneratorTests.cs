using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class CsvToOrgChartGeneratorTests
{
    [Fact]
    public void Generate_CreatesNodesAndEdges_ForSimpleHierarchy()
    {
        var parseResult = new CsvParseResult
        {
            Headers = ["Name", "Manager"],
            Rows =
            [
                ["Alice", "Bob"],
                ["Bob", ""],
                ["Charlie", "Bob"]
            ],
            DetectedDelimiter = ','
        };

        var mappings = new List<CsvColumnMapping>
        {
            new() { SemanticField = "Name", SelectedColumn = "Name", IsRequired = true },
            new() { SemanticField = "Manager", SelectedColumn = "Manager", IsRequired = true }
        };

        var doc = CsvToOrgChartGenerator.Generate(parseResult, mappings);

        doc.ActivePage.Nodes.Count.Should().Be(3);
        doc.ActivePage.Edges.Count.Should().Be(2);

        var bobNode = doc.ActivePage.Nodes.First(n => n.Data.TryGetValue("label", out var v) && v?.ToString() == "Bob");
        doc.ActivePage.Edges.Should().Contain(e => e.SourceNodeId == bobNode.Id);
    }

    [Fact]
    public void Generate_SkipsDuplicateEdges()
    {
        var parseResult = new CsvParseResult
        {
            Headers = ["Name", "Manager"],
            Rows =
            [
                ["Alice", "Bob"],
                ["Alice", "Bob"]
            ],
            DetectedDelimiter = ','
        };

        var mappings = new List<CsvColumnMapping>
        {
            new() { SemanticField = "Name", SelectedColumn = "Name", IsRequired = true },
            new() { SemanticField = "Manager", SelectedColumn = "Manager", IsRequired = true }
        };

        var doc = CsvToOrgChartGenerator.Generate(parseResult, mappings);
        doc.ActivePage.Edges.Count.Should().Be(1);
    }

    [Fact]
    public void Generate_ThrowsWhenMappingMissing()
    {
        var parseResult = new CsvParseResult
        {
            Headers = ["Name", "Manager"],
            Rows = [["Alice", "Bob"]],
            DetectedDelimiter = ','
        };

        var mappings = new List<CsvColumnMapping>();
        var act = () => CsvToOrgChartGenerator.Generate(parseResult, mappings);
        act.Should().Throw<InvalidOperationException>().WithMessage("*Name*");
    }

    [Fact]
    public void MissingColumn_ThrowsTypedImportError()
    {
        var parseResult = new CsvParseResult
        {
            Headers = ["Name", "Manager"],
            Rows = [["Alice", "Bob"]],
            DetectedDelimiter = ','
        };

        var mappings = new List<CsvColumnMapping>
        {
            new() { SemanticField = "Name", SelectedColumn = "ghost", IsRequired = true },
            new() { SemanticField = "Manager", SelectedColumn = "Manager", IsRequired = true }
        };

        var act = () => CsvToOrgChartGenerator.Generate(parseResult, mappings);
        act.Should().Throw<DiagramImportException>()
            .Which.Error.Should().Be(DiagramImportError.ColumnNotFound);
    }
}
