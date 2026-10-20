using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// bUnit tests for the CSV import dialog: localized diagram types and semantic
/// mapping fields, per-instance label ids, a debounced parse-once live preview and
/// stale-mapping cleanup when the header changes.
/// </summary>
public class TmDiagramCsvImportDialogTests : LocalizationTestBase
{
    private const string OrgChartCsv =
        """
        name,manager
        Alice,
        Bob,Alice
        """;

    private readonly FakeTimeProvider _time = new();

    public TmDiagramCsvImportDialogTests() => Services.AddSingleton<TimeProvider>(_time);

    /// <summary>Types, crosses the debounce deadline and drains the InvokeAsync hop.</summary>
    private async Task TypeAndSettleAsync(Bunit.IRenderedComponent<TmDiagramCsvImportDialog> cut, string text)
    {
        cut.Find("textarea").Input(text);
        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramCsvImportDialog.PreviewDebounceMs + 1));
        await cut.InvokeAsync(() => { });
    }

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
    public async Task Preview_UpdatesOnInput()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        cut.FindAll(".tm-diagram-csv-import__preview-table").Should().BeEmpty();

        await TypeAndSettleAsync(cut, OrgChartCsv);

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
    public async Task EditingCsv_ClearsMappingsWhoseColumnVanished()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, OrgChartCsv);

        var mappingSelects = cut.FindAll(".tm-diagram-csv-import__mapping-row select").ToList();
        mappingSelects[0].Change("name");
        mappingSelects[1].Change("manager");
        cut.Render();

        cut.Find(".tm-diagram-csv-import__preview").TextContent.Should().Contain("Nodes: 2");

        // Edit the header: both mapped columns disappear from the CSV.
        await TypeAndSettleAsync(cut, "fullname,boss\nAlice,\nBob,Alice");

        var cleared = cut.FindAll(".tm-diagram-csv-import__mapping-row select").ToList();
        cleared.Should().HaveCount(2);
        ((AngleSharp.Html.Dom.IHtmlSelectElement)cleared[0]).Value.Should().BeEmpty(
            "a mapping whose column vanished from the header is cleared, never kept stale");
        cut.FindAll(".tm-diagram-csv-import__preview").Should().BeEmpty(
            "without valid required mappings the node/edge preview must not render");
    }

    [Fact]
    public async Task ParseError_IsLocalized_AndNeverShowsTheRawMessage()
    {
        UseCzechLocalization();
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, "onlyonecolumn\nvalue");

        // While typing the problem stays POLITE in the status region — with the localized
        // wording, never the raw exception message (a resource key).
        cut.FindAll(".tm-alert").Should().BeEmpty("typing-time problems must never use role=alert");
        var status = cut.Find(".tm-diagram-csv-import__status");
        status.TextContent.Should().Contain("CSV musí obsahovat alespoň 2 sloupce");
        status.TextContent.Should().NotContain("CsvImportDialog_",
            "the raw exception message (a resource key) must never reach the user");

        // A valid parse enables Import; typing invalid text and clicking Import before the
        // debounce promotes the failure to the assertive alert.
        await TypeAndSettleAsync(cut, "name,manager\nAlice,");
        cut.Find("textarea").Input("onlyonecolumn\nvalue");
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Contains("Import")).Click());
        cut.Find(".tm-diagram-csv-import__error").TextContent.Should().Contain("CSV musí obsahovat alespoň 2 sloupce");
    }

    [Fact]
    public async Task TooLargeInput_PreviewErrorIsPolite_ImportClickShowsAlert()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        // Over the 512 KB cap CsvParser throws the typed InputTooComplex: the dialog maps it
        // to the localized "too large" wording — never the English developer sentence.
        var oversized = "ColA,ColB\n" + new string('x', 512 * 1024);
        await TypeAndSettleAsync(cut, oversized);

        cut.FindAll(".tm-alert").Should().BeEmpty("typing-time problems must never use role=alert");
        var status = cut.Find(".tm-diagram-csv-import__status");
        status.TextContent.Should().Contain("too large");
        status.TextContent.Should().NotContain("maximum length",
            "the English developer message must never reach the user");
        cut.Find("textarea").GetAttribute("aria-invalid").Should().Be("true");

        // A valid parse enables Import; typing the oversized text and clicking Import before
        // the debounce promotes the failure to the assertive alert.
        await TypeAndSettleAsync(cut, "name,manager\nAlice,");
        cut.Find("textarea").Input(oversized);
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Contains("Import")).Click());
        cut.Find(".tm-diagram-csv-import__error").TextContent.Should().Contain("too large");
    }

    [Fact]
    public async Task ParseOnce_PerDebouncedEdit()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        // Two quick edits inside one debounce window: only the LAST text parses.
        cut.Find("textarea").Input("name,manager\nAlice,");
        cut.Find("textarea").Input("name,manager\nAlice,\nBob,Alice");
        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramCsvImportDialog.PreviewDebounceMs + 1));
        await cut.InvokeAsync(() => { });

        var mappingSelects = cut.FindAll(".tm-diagram-csv-import__mapping-row select").ToList();
        mappingSelects[0].Change("name");
        mappingSelects[1].Change("manager");
        cut.Render();

        // Two data rows of the second text → 2 nodes (the first, superseded text had one).
        cut.Find(".tm-diagram-csv-import__preview").TextContent.Should().Contain("Nodes: 2");
    }

    [Fact]
    public async Task HidingTheDialog_CancelsThePendingPreview()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(OrgChartCsv);
        cut.Render(p => p.Add(x => x.Show, false));
        _time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });

        cut.Render(p => p.Add(x => x.Show, true));
        cut.FindAll(".tm-diagram-csv-import__preview-table").Should().BeEmpty(
            "a pending debounced parse must not fire after the dialog was hidden");
    }

    [Fact]
    public void Status_LiveRegion_ExistsBeforeAnyPreview()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        var status = cut.Find(".tm-diagram-csv-import__status");
        status.GetAttribute("role").Should().Be("status");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.TextContent.Should().BeEmpty("the live region renders before any preview content");
    }

    [Fact]
    public async Task Mapping_IsTwoColumnForm()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, OrgChartCsv);

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
    public async Task Summary_RendersNodeAndEdgeStatChips()
    {
        var cut = Render<TmDiagramCsvImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, OrgChartCsv);

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
