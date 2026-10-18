using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// bUnit tests for the SQL import dialog: debounced live preview, guarded import button,
/// localized layout options, per-instance label ids and the always-present live region.
/// </summary>
public class TmDiagramSqlImportDialogTests : LocalizationTestBase
{
    private const string SingleTableSql =
        """
        CREATE TABLE Customer (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100) NOT NULL
        );
        """;

    private const string TwoTableSql =
        """
        CREATE TABLE Customer (
            Id INT PRIMARY KEY,
            Name NVARCHAR(100) NOT NULL
        );

        CREATE TABLE "Order" (
            Id INT PRIMARY KEY,
            CustomerId INT,
            FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
        );
        """;

    private const string NoTableSql = "SELECT 1;";

    private readonly FakeTimeProvider _time = new();

    public TmDiagramSqlImportDialogTests() => Services.AddSingleton<TimeProvider>(_time);

    /// <summary>Types, crosses the debounce deadline and drains the InvokeAsync hop.</summary>
    private async Task TypeAndSettleAsync(Bunit.IRenderedComponent<TmDiagramSqlImportDialog> cut, string text)
    {
        cut.Find("textarea").Input(text);
        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramSqlImportDialog.PreviewDebounceMs + 1));
        await cut.InvokeAsync(() => { });
    }

    [Fact]
    public async Task Preview_ShowsTablesAndRelations_WhenSqlTyped()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, SingleTableSql);

        var preview = cut.Find(".tm-diagram-sql-import__preview");
        preview.TextContent.Should().Contain("Tables: 1");
        preview.TextContent.Should().Contain("Relations: 0");
    }

    [Fact]
    public async Task Preview_IsDebounced_ParsesOnlyAfterTypingPauses()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(SingleTableSql);
        cut.FindAll(".tm-diagram-sql-import__preview").Should().BeEmpty(
            "the parse must not run on the keystroke itself");

        _time.Advance(TimeSpan.FromMilliseconds(TmDiagramSqlImportDialog.PreviewDebounceMs - 1));
        await cut.InvokeAsync(() => { });
        cut.FindAll(".tm-diagram-sql-import__preview").Should().BeEmpty(
            "299 ms is still inside the debounce window");

        _time.Advance(TimeSpan.FromMilliseconds(2));
        await cut.InvokeAsync(() => { });

        cut.Find(".tm-diagram-sql-import__preview").TextContent.Should().Contain("Tables: 1");
    }

    [Fact]
    public void Preview_Hides_WhenSqlEmpty()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.FindAll(".tm-diagram-sql-import__preview").Should().BeEmpty();
    }

    [Fact]
    public async Task HidingTheDialog_CancelsThePendingPreview()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(SingleTableSql);
        cut.Render(p => p.Add(x => x.Show, false));
        _time.Advance(TimeSpan.FromSeconds(1));
        await cut.InvokeAsync(() => { });

        cut.Render(p => p.Add(x => x.Show, true));
        cut.FindAll(".tm-diagram-sql-import__preview").Should().BeEmpty(
            "a pending debounced parse must not fire after the dialog was hidden");
    }

    [Fact]
    public async Task Import_Disabled_WhenPreviewHasZeroTables()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, NoTableSql);

        var importButton = cut.FindAll("button").First(b => b.TextContent.Contains("Import"));
        importButton.HasAttribute("disabled").Should().BeTrue(
            "importing must stay disabled while the parsed SQL has zero tables");
    }

    [Fact]
    public async Task Import_Enabled_WhenPreviewHasTables()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, SingleTableSql);

        var importButton = cut.FindAll("button").First(b => b.TextContent.Contains("Import"));
        importButton.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task TooComplexInput_PreviewErrorIsPolite_ImportClickShowsAlert()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        // One never-closing CREATE TABLE repeated 20k times exceeds the parser's time budget:
        // the dialog maps the typed InputTooComplex to the localized wording.
        var pathological = string.Concat(Enumerable.Repeat("CREATE TABLE t ( a INT ", 20000));
        await TypeAndSettleAsync(cut, pathological);

        // While typing the problem stays POLITE in the status region (aria-invalid on the
        // textarea); the assertive alert is reserved for the Import click.
        cut.FindAll(".tm-alert").Should().BeEmpty();
        var status = cut.Find(".tm-diagram-sql-import__status");
        status.TextContent.Should().Contain("too complex");
        status.TextContent.Should().NotContain("time budget",
            "the English developer message must never reach the user");
        cut.Find("textarea").GetAttribute("aria-invalid").Should().Be("true");

        // A valid parse enables Import; typing the pathological text and clicking Import before
        // the debounce promotes the failure to the assertive alert.
        await TypeAndSettleAsync(cut, SingleTableSql);
        cut.Find("textarea").Input(pathological);
        await cut.InvokeAsync(() => cut.FindAll("button").First(b => b.TextContent.Contains("Import")).Click());
        cut.Find(".tm-alert").TextContent.Should().Contain("too complex");
    }

    [Fact]
    public async Task Import_Disabled_AfterParseError()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));
        await TypeAndSettleAsync(cut, SingleTableSql);

        // Simulate the UpdatePreview catch branch (SqlParser.Parse threw): the
        // sentinel state must disable Import, not leave it clickable.
        typeof(TmDiagramSqlImportDialog)
            .GetField("_previewTables", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(cut.Instance, -1);
        cut.Render();

        var importButton = cut.FindAll("button").First(b => b.TextContent.Contains("Import"));
        importButton.HasAttribute("disabled").Should().BeTrue(
            "a parse error (_previewTables == -1) must keep import disabled, like zero tables");
    }

    [Fact]
    public void Status_LiveRegion_ExistsBeforeAnyPreview()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        var status = cut.Find(".tm-diagram-sql-import__status");
        status.GetAttribute("role").Should().Be("status");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.TextContent.Should().BeEmpty("the live region renders before any preview content");
    }

    [Fact]
    public void LayoutOptions_AreLocalized_Cs()
    {
        UseCzechLocalization();
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        var options = cut.FindAll("select option").Select(o => o.TextContent.Trim()).ToList();
        options.Should().Contain("Hierarchicky shora dolů");
        options.Should().Contain("Hierarchicky zleva doprava");
        options.Should().NotContain("Top-Bottom");
        options.Should().NotContain("Left-Right");
    }

    [Fact]
    public void Labels_HaveUniqueId_PerInstance()
    {
        var first = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));
        var second = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        var firstTextarea = first.Find("textarea");
        var secondTextarea = second.Find("textarea");

        firstTextarea.Id.Should().NotBeNullOrWhiteSpace();
        secondTextarea.Id.Should().NotBeNullOrWhiteSpace();
        firstTextarea.Id.Should().NotBe(secondTextarea.Id, "two dialogs rendered at once must not share field ids");

        first.Find($"label[for='{firstTextarea.Id}']").Should().NotBeNull();
        second.Find($"label[for='{secondTextarea.Id}']").Should().NotBeNull();
    }

    [Fact]
    public async Task Summary_RendersIconsAndCounts()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        await TypeAndSettleAsync(cut, TwoTableSql);

        // Mockup: the parse summary is a box of icon stat chips — one per count.
        var stats = cut.FindAll(".tm-diagram-sql-import__stat").ToList();
        stats.Should().HaveCount(2, "one chip per table and relation count");
        stats[0].TextContent.Should().Contain("Tables: 2");
        stats[1].TextContent.Should().Contain("Relations: 1");
        stats.Should().OnlyContain(s => s.QuerySelector("svg") != null,
            "every stat chip paints its icon");
    }
}
