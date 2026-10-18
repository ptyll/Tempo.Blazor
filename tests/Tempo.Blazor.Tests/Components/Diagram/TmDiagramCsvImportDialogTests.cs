using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// bUnit tests for the CSV import dialog: localized diagram types and semantic
/// mapping fields, per-instance label ids and a live preview.
/// </summary>
public class TmDiagramCsvImportDialogTests : LocalizationTestBase
{
    private const string OrgChartCsv =
        """
        name,manager
        Alice,
        Bob,Alice
        """;

    [Fact]
    public void TypeOptions_AreLocalized_Cs()
    {
        UseCzechLocalization();
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        var options = cut.FindAll("select option").Select(o => o.TextContent.Trim()).ToList();
        options.Should().Contain("Organogram");
        options.Should().Contain("Vývojový diagram");
        options.Should().Contain("Časová osa");
        options.Should().NotContain("Org Chart");
        options.Should().NotContain("Flowchart");
        options.Should().NotContain("Timeline");
    }

    [Fact]
    public void MappingLabels_AreLocalized()
    {
        UseCzechLocalization();
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        var labels = cut.FindAll(".tm-diagram-csv-import__mapping-label")
            .Select(l => l.TextContent.Trim().TrimEnd('*').Trim()).ToList();
        labels.Should().Contain("Název");
        labels.Should().Contain("Nadřízený");
        labels.Should().NotContain("Name");
        labels.Should().NotContain("Manager");
    }

    [Fact]
    public void Labels_HaveUniqueFor_PerInstance()
    {
        var first = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));
        var second = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        var firstTextarea = first.Find("textarea");
        var secondTextarea = second.Find("textarea");

        firstTextarea.Id.Should().NotBeNullOrWhiteSpace();
        secondTextarea.Id.Should().NotBeNullOrWhiteSpace();
        firstTextarea.Id.Should().NotBe(secondTextarea.Id, "two dialogs rendered at once must not share field ids");

        first.Find($"label[for='{firstTextarea.Id}']").Should().NotBeNull();
        second.Find($"label[for='{secondTextarea.Id}']").Should().NotBeNull();
    }

    [Fact]
    public void Preview_UpdatesOnInput()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        cut.FindAll(".tm-diagram-csv-import__preview-table").Should().BeEmpty();

        cut.Find("textarea").Input(OrgChartCsv);
        cut.Render();

        // Data preview table appears with the parsed headers
        var table = cut.Find(".tm-diagram-csv-import__preview-table");
        table.TextContent.Should().Contain("name");
        table.TextContent.Should().Contain("manager");

        // Node/edge preview appears once the required mappings are chosen
        var mappingSelects = cut.FindAll(".tm-diagram-csv-import__mapping-row select").ToList();
        mappingSelects.Should().HaveCount(2);
        mappingSelects[0].Change("name");
        mappingSelects[1].Change("manager");
        cut.Render();

        cut.Find(".tm-diagram-csv-import__preview").TextContent.Should().Contain("Nodes: 2");
    }

    [Fact]
    public void Mapping_IsTwoColumnForm()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(OrgChartCsv);
        cut.Render();

        // Mockup: every mapping renders as a two-column form row — a label on the left
        // and its column select on the right, never stacked controls.
        var rows = cut.FindAll(".tm-diagram-csv-import__mapping-row").ToList();
        rows.Should().HaveCount(2);

        foreach (var row in rows)
        {
            row.QuerySelectorAll("label").Should().ContainSingle(
                "a two-column mapping row carries exactly one label");
            row.QuerySelectorAll("select").Should().ContainSingle(
                "a two-column mapping row carries exactly one select");
        }
    }

    [Fact]
    public void Summary_RendersNodeAndEdgeStatChips()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(OrgChartCsv);
        cut.Render();

        var mappingSelects = cut.FindAll(".tm-diagram-csv-import__mapping-row select").ToList();
        mappingSelects[0].Change("name");
        mappingSelects[1].Change("manager");
        cut.Render();

        // Mockup: the live summary is a box of icon stat chips — one per count.
        var stats = cut.FindAll(".tm-diagram-csv-import__stat").ToList();
        stats.Should().HaveCount(2, "one chip per node and edge count");
        stats[0].TextContent.Should().Contain("Nodes: 2");
        stats[1].TextContent.Should().Contain("Edges: 1");
        stats.Should().OnlyContain(s => s.QuerySelector("svg") != null,
            "every stat chip paints its icon");
    }

    [Fact]
    public void Subtitle_IsLocalized()
    {
        UseCzechLocalization();
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        var subtitle = cut.Find(".tm-modal-subtitle");
        subtitle.TextContent.Should().Contain("Mapujte sloupce CSV",
            "the dialog explains its purpose through the TmModal subtitle");
    }
}
