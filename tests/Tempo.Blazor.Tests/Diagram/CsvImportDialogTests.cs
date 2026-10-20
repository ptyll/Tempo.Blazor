using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Services;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class CsvImportDialogTests : DiagramTestBase
{
    private readonly FakeTimeProvider _time = new();

    public CsvImportDialogTests() => Services.AddSingleton<TimeProvider>(_time);

    /// <summary>Types, crosses the debounce deadline and drains the InvokeAsync hop.</summary>
    private async Task TypeAndSettleAsync(IRenderedComponent<TmDiagramCsvImportDialog> cut, string text)
    {
        cut.Find("textarea").Input(text);
        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramCsvImportDialog.PreviewDebounceMs + 1));
        await cut.InvokeAsync(() => { });
    }

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
    public async Task CsvImportDialog_ShowsPreviewRowsAfterInput()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        await TypeAndSettleAsync(cut, "Name,Manager\nAlice,Bob\nCharlie,Bob");

        var table = cut.Find(".tm-diagram-csv-import__preview-table");
        table.Should().NotBeNull();
        var rows = cut.FindAll(".tm-diagram-csv-import__preview-table tbody tr");
        rows.Count.Should().Be(2);
    }

    [Fact]
    public async Task CsvImportDialog_PreviewErrorIsPolite_WhileTyping()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        // A two-line single-column CSV fails parsing: the wording renders in the POLITE status
        // region while typing — the assertive alert stays reserved for the Import click.
        await TypeAndSettleAsync(cut, "Name\nAlice");

        cut.FindAll(".tm-alert").Should().BeEmpty("typing-time problems must never use role=alert");
        var status = cut.Find(".tm-diagram-csv-import__status");
        status.GetAttribute("role").Should().Be("status");
        status.TextContent.Should().Contain("at least 2 columns");

        // The textarea is wired to the polite error (aria-invalid + aria-describedby).
        var errorId = cut.Find(".tm-diagram-csv-import__status-error").GetAttribute("id");
        var textarea = cut.Find("textarea");
        textarea.GetAttribute("aria-invalid").Should().Be("true");
        textarea.GetAttribute("aria-describedby").Should().Be(errorId);
    }

    [Fact]
    public async Task CsvImportDialog_ImportClick_PromotesThePreviewErrorToTheAlert()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        // A valid CSV parses, auto-maps and enables Import; then the text turns invalid and the
        // user clicks Import before the debounce re-runs the preview — the failure surfaces
        // through the assertive TmAlert, and the polite region hands the wording over.
        await TypeAndSettleAsync(cut, "Name,Manager\nAlice,Bob");
        cut.FindAll("button").First(b => b.TextContent == "Import").HasAttribute("disabled").Should().BeFalse();

        cut.Find("textarea").Input("Name\nAlice");
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent == "Import").Click());

        cut.Find(".tm-alert").TextContent.Should().Contain("at least 2 columns");
        cut.FindAll(".tm-diagram-csv-import__status-error").Should().BeEmpty(
            "the preview error moves into the alert, it does not duplicate");
    }

    [Fact]
    public async Task CsvImportDialog_SingleLine_ShowsNoError_WhileTyping()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        // A header-only snippet (fewer than two lines) means the user is still typing — the
        // no-data / min-columns wording must not fire yet.
        await TypeAndSettleAsync(cut, "Name");

        cut.Find(".tm-diagram-csv-import__status").TextContent.Should().BeEmpty();
        cut.FindAll(".tm-alert").Should().BeEmpty();
    }

    [Fact]
    public async Task CsvImportDialog_AutoMapsHeaderMatchingFieldNameOrLabel()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDiagramCsvImportDialog_Field_Name"] = "Název",
            ["TmDiagramCsvImportDialog_Field_Manager"] = "Nadřízený"
        });
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        // "name" matches the semantic field name (fold-compare), "Nadrizeny" matches the localized
        // label without diacritics — both pre-select without a manual mapping step.
        await TypeAndSettleAsync(cut, "name,Nadrizeny\nAlice,Bob");

        var selects = cut.FindAll(".tm-diagram-csv-import__mappings-grid select");
        selects[0].GetAttribute("value").Should().Be("name");
        selects[1].GetAttribute("value").Should().Be("Nadrizeny");
    }

    [Fact]
    public async Task CsvImportDialog_VanishedColumn_ClearsMapping_Politely()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        await TypeAndSettleAsync(cut, "Name,Manager\nAlice,Bob");
        var selects = cut.FindAll(".tm-diagram-csv-import__mappings-grid select");
        selects[0].GetAttribute("value").Should().Be("Name");
        selects[1].GetAttribute("value").Should().Be("Manager");

        // Edit the CSV so the mapped Manager column disappears: the mapping is cleared (never
        // kept stale) and the user is told through the POLITE preview error — no alert.
        await TypeAndSettleAsync(cut, "Name,Other\nAlice,Bob");

        cut.FindAll(".tm-diagram-csv-import__mappings-grid select")[1].GetAttribute("value")
            .Should().BeNullOrEmpty();
        cut.FindAll(".tm-alert").Should().BeEmpty();
        cut.Find(".tm-diagram-csv-import__status").TextContent
            .Should().Contain("Manager").And.Contain("no longer in the CSV header");
    }

    [Fact]
    public async Task CsvImportDialog_FailedParse_DoesNotWipeMappings()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        await TypeAndSettleAsync(cut, "Name,Manager\nAlice,Bob");

        // A transient failed parse (single line = still typing) must not wipe the mappings
        // the user already has — they survive until a successful parse re-validates them.
        await TypeAndSettleAsync(cut, "Name");

        cut.FindAll(".tm-diagram-csv-import__mappings-grid select")[0].GetAttribute("value").Should().Be("Name");
        cut.FindAll(".tm-diagram-csv-import__mappings-grid select")[1].GetAttribute("value").Should().Be("Manager");
    }

    [Fact]
    public async Task CsvImportDialog_RequiredMappingSelects_CarryAriaRequired()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        // Flowchart: From/To required, Label optional.
        var typeSelect = cut.FindAll("select")[0];
        await cut.InvokeAsync(() => typeSelect.Change("Flowchart"));

        await TypeAndSettleAsync(cut, "From,To,Label\nA,B,step");

        var selects = cut.FindAll(".tm-diagram-csv-import__mappings-grid select");
        selects.Should().HaveCount(3);
        selects[0].GetAttribute("aria-required").Should().Be("true");
        selects[1].GetAttribute("aria-required").Should().Be("true");
        selects[2].GetAttribute("aria-required").Should().BeNull("the optional Label mapping is not required");
    }

    [Fact]
    public async Task CsvImportDialog_RendersColumnMappingsForOrgChart()
    {
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => { }));

        await TypeAndSettleAsync(cut, "Name,Manager\nAlice,Bob");

        var labels = cut.FindAll(".tm-diagram-csv-import__mapping-label");
        labels.Should().Contain(l => l.TextContent.Contains("Name"));
        labels.Should().Contain(l => l.TextContent.Contains("Manager"));
    }

    [Fact]
    public async Task CsvImportDialog_InvokesOnImport_WhenValid()
    {
        CsvImportResult? captured = null;
        var cut = Render<TmDiagramCsvImportDialog>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnClose, () => { })
            .Add(p => p.OnImport, (CsvImportResult r) => captured = r));

        await TypeAndSettleAsync(cut, "Name,Manager\nAlice,Bob");

        // Wait for selects to appear (type + 2 mappings)
        cut.WaitForAssertion(() => cut.FindAll("select").Count.Should().BeGreaterThanOrEqualTo(3));

        // Select mapping for Name
        var nameSelect = cut.FindAll("select").Skip(1).First();
        nameSelect.Change("Name");

        // Select mapping for Manager
        var managerSelect = cut.FindAll("select").Skip(2).First();
        managerSelect.Change("Manager");

        var importBtn = cut.FindAll("button").First(b => b.TextContent == "Import");
        await cut.InvokeAsync(() => importBtn.Click());

        captured.Should().NotBeNull();
        captured!.Document.ActivePage.Nodes.Count.Should().Be(2);
    }
}
