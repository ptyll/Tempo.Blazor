using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class CsvImportDialogTests : DiagramTestBase
{
    [Fact]
    public void CsvImportDialog_RendersTitle()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        var title = cut.Find(".tm-modal-title");
        title.TextContent.Should().Be("Import CSV to Diagram");
    }

    [Fact]
    public void CsvImportDialog_ShowsPreviewRowsAfterInput()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        var textarea = cut.Find("textarea");
        textarea.Input("Name,Manager\nAlice,Bob\nCharlie,Bob");

        cut.WaitForAssertion(() =>
        {
            var table = cut.Find(".tm-diagram-csv-import__preview-table");
            table.Should().NotBeNull();
            var rows = cut.FindAll(".tm-diagram-csv-import__preview-table tbody tr");
            rows.Count.Should().Be(2);
        });
    }

    [Fact]
    public void CsvImportDialog_RendersErrorForInvalidCsvOnImport()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        var textarea = cut.Find("textarea");
        textarea.Input("Name\nAlice");

        cut.WaitForAssertion(() =>
        {
            var alerts = cut.FindAll(".tm-alert");
            alerts.Should().ContainSingle();
            alerts[0].TextContent.Should().Contain("at least 2 columns");
        });
    }

    [Fact]
    public void CsvImportDialog_RendersColumnMappingsForOrgChart()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        var textarea = cut.Find("textarea");
        textarea.Change("Name,Manager\nAlice,Bob");

        cut.WaitForAssertion(() =>
        {
            var labels = cut.FindAll(".tm-diagram-csv-import__mapping-label");
            labels.Should().Contain(l => l.TextContent.Contains("Name"));
            labels.Should().Contain(l => l.TextContent.Contains("Manager"));
        });
    }

    [Fact]
    public void CsvImportDialog_InvokesOnImport_WhenValid()
    {
        CsvImportResult? captured = null;
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => captured = r));

        var textarea = cut.Find("textarea");
        textarea.Input("Name,Manager\nAlice,Bob");

        // Wait for selects to appear (type + 2 mappings)
        cut.WaitForAssertion(() => cut.FindAll("select").Count.Should().BeGreaterThanOrEqualTo(3));

        // Select mapping for Name
        var nameSelect = cut.FindAll("select").Skip(1).First();
        nameSelect.Change("Name");

        // Select mapping for Manager
        var managerSelect = cut.FindAll("select").Skip(2).First();
        managerSelect.Change("Manager");

        var importBtn = cut.FindAll("button").First(b => b.TextContent == "Import");
        cut.InvokeAsync(() => importBtn.Click());

        cut.WaitForAssertion(() =>
        {
            captured.Should().NotBeNull();
            captured!.Document.ActivePage.Nodes.Count.Should().Be(2);
        });
    }
}
