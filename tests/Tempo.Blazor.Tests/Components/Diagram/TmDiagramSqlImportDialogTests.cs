using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// bUnit tests for the SQL import dialog: live preview, guarded import button,
/// localized layout options and per-instance label ids.
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

        CREATE TABLE Order (
            Id INT PRIMARY KEY,
            CustomerId INT,
            FOREIGN KEY (CustomerId) REFERENCES Customer(Id)
        );
        """;

    private const string NoTableSql = "SELECT 1;";

    [Fact]
    public void Preview_ShowsTablesAndRelations_WhenSqlTyped()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(SingleTableSql);
        cut.Render();

        var preview = cut.Find(".tm-diagram-sql-import__preview");
        preview.TextContent.Should().Contain("Tables: 1");
        preview.TextContent.Should().Contain("Relations: 0");
    }

    [Fact]
    public void Preview_Hides_WhenSqlEmpty()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.FindAll(".tm-diagram-sql-import__preview").Should().BeEmpty();
    }

    [Fact]
    public void Import_Disabled_WhenPreviewHasZeroTables()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(NoTableSql);
        cut.Render();

        var importButton = cut.FindAll("button").First(b => b.TextContent.Contains("Import"));
        importButton.HasAttribute("disabled").Should().BeTrue(
            "importing must stay disabled while the parsed SQL has zero tables");
    }

    [Fact]
    public void Import_Enabled_WhenPreviewHasTables()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(SingleTableSql);
        cut.Render();

        var importButton = cut.FindAll("button").First(b => b.TextContent.Contains("Import"));
        importButton.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Import_Disabled_AfterParseError()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));
        cut.Find("textarea").Input(SingleTableSql);
        cut.Render();

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
    public void Summary_RendersIconsAndCounts()
    {
        var cut = Render<TmDiagramSqlImportDialog>(p => p.Add(x => x.Show, true));

        cut.Find("textarea").Input(TwoTableSql);
        cut.Render();

        // Mockup: the parse summary is a box of icon stat chips — one per count.
        var stats = cut.FindAll(".tm-diagram-sql-import__stat").ToList();
        stats.Should().HaveCount(2, "one chip per table and relation count");
        stats[0].TextContent.Should().Contain("Tables: 2");
        stats[1].TextContent.Should().Contain("Relations: 1");
        stats.Should().OnlyContain(s => s.QuerySelector("svg") != null,
            "every stat chip paints its icon");
    }
}
