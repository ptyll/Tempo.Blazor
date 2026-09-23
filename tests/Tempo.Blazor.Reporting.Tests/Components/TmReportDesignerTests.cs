using Tempo.Blazor.Reporting.Components;
using Tempo.Blazor.Reporting.Models;
using Tempo.Blazor.Reporting.Tests.Fixtures;
using Tempo.Reporting.Abstractions.Definitions;

namespace Tempo.Blazor.Reporting.Tests.Components;

public sealed class TmReportDesignerTests : ReportingComponentTestBase
{
    [Fact]
    public void Designer_CanvasIsNamedRegion_NotMainLandmark()
    {
        // N190 — the host layout owns the single <main> landmark; the designer canvas shell is a
        // labelled <section> region instead (036ce5fd/a59cac9a rule, generalised).
        var cut = Render<TmReportDesigner>(parameters => parameters
            .Add(component => component.Definition, Definition()));

        cut.FindAll("main").Should().BeEmpty("the host layout owns the single <main> landmark");
        cut.Find("section.tm-report-designer__canvas-shell")
            .GetAttribute("aria-label").Should().Be("Report canvas");
    }

    [Fact]
    public void Designer_RendersBandsAndUpdatesBandHeightAndZoom()
    {
        var cut = Render<TmReportDesigner>(parameters => parameters
            .Add(component => component.Definition, Definition()));

        cut.Find("[data-testid='tm-report-designer']").Should().NotBeNull();
        cut.Find("[data-testid='tm-designer-band-Detail']").TextContent.Should().Contain("Detail");

        cut.Find("[data-testid='tm-designer-zoom']").Change("125");
        cut.Find("[data-testid='tm-designer-band-height-Detail']").Change("180");

        cut.Find("[data-testid='tm-designer-zoom']").GetAttribute("value").Should().Be("125");
        cut.Find("[data-testid='tm-designer-band-Detail']").GetAttribute("style").Should().Contain("180");
    }

    [Fact]
    public void Designer_AddsSelectsCopiesAndUndoesTextElement()
    {
        var cut = Render<TmReportDesigner>(parameters => parameters
            .Add(component => component.Definition, Definition()));

        cut.Find("[data-testid='tm-designer-add-textbox']").Click();
        cut.Find("[data-testid='tm-designer-element-textbox-1']").Click();
        cut.Find("[data-testid='tm-designer-property-text']").Change("Customer total");

        cut.Find("[data-testid='tm-designer-properties']").TextContent.Should().Contain("textbox-1");
        cut.Find("[data-testid='tm-designer-element-textbox-1']").TextContent.Should().Contain("Customer total");

        cut.Find("[data-testid='tm-designer-copy']").Click();
        cut.FindAll("[data-testid^='tm-designer-element-']").Should().HaveCount(2);

        cut.Find("[data-testid='tm-designer-undo']").Click();
        cut.FindAll("[data-testid^='tm-designer-element-']").Should().HaveCount(1);
    }

    [Fact]
    public void Designer_DataTabInsertsFieldExpressionAndValidatesInvalidExpression()
    {
        var cut = Render<TmReportDesigner>(parameters => parameters
            .Add(component => component.Definition, Definition()));

        cut.Find("[data-testid='tm-designer-add-textbox']").Click();
        cut.Find("[data-testid='tm-designer-tab-data']").Click();
        cut.Find("[data-testid='tm-designer-insert-field-Orders-Customer']").Click();
        cut.Find("[data-testid='tm-designer-tab-design']").Click();

        cut.Find("[data-testid='tm-designer-element-textbox-1']").TextContent.Should().Contain("Fields.Customer");

        cut.Find("[data-testid='tm-designer-tab-data']").Click();
        cut.Find("[data-testid='tm-designer-expression-input']").Change("=Fields.");
        cut.Find("[data-testid='tm-designer-expression-error']").TextContent.Should().Contain("Select a field");
    }

    [Fact]
    public void Designer_EditsChartPropertiesAndRendersChartPreview()
    {
        ReportDefinition? changed = null;
        var cut = Render<TmReportDesigner>(parameters => parameters
            .Add(component => component.Definition, Definition())
            .Add(component => component.DefinitionChanged, definition => changed = definition));

        cut.Find("[data-testid='tm-designer-add-chart']").Click();
        cut.Find("[data-testid='tm-designer-chart-properties']").Should().NotBeNull();
        cut.Find("[data-testid='tm-designer-chart-preview']").QuerySelector("svg").Should().NotBeNull();

        cut.Find("[data-testid='tm-designer-chart-type']").Change("Donut");
        cut.Find("[data-testid='tm-designer-chart-title']").Change("Revenue mix");
        cut.Find("[data-testid='tm-designer-chart-series-name']").Change("Net sales");
        cut.Find("[data-testid='tm-designer-chart-category-expression']").Change("=Fields.Customer");
        cut.Find("[data-testid='tm-designer-chart-value-expression']").Change("=Fields.Total");
        cut.Find("[data-testid='tm-designer-chart-color']").Change("#14b8a6");

        cut.Find("[data-testid='tm-designer-element-chart-1']").TextContent.Should().Contain("Revenue mix");
        var chart = changed!.Bands.Detail!.Elements.OfType<ReportChartElement>().Single();
        chart.ChartType.Should().Be(ReportChartType.Donut);
        chart.Title.Should().Be("Revenue mix");
        chart.Series.Should().ContainSingle();
        chart.Series[0].Name.Should().Be("Net sales");
        chart.Series[0].CategoryExpression.Should().Be("=Fields.Customer");
        chart.Series[0].ValueExpression.Should().Be("=Fields.Total");
        chart.Series[0].Color.Should().Be("#14b8a6");
    }

    [Fact]
    public void Designer_PreviewValidatesAndRaisesPublishSave()
    {
        ReportDesignerSaveEventArgs? saved = null;
        var cut = Render<TmReportDesigner>(parameters => parameters
            .Add(component => component.Definition, Definition())
            .Add(component => component.Saved, args => saved = args));

        cut.Find("[data-testid='tm-designer-tab-preview']").Click();
        cut.Find("[data-testid='tm-designer-preview']").TextContent.Should().Contain("Sales Register");
        cut.Find("[data-testid='tm-designer-publish']").Click();

        saved.Should().NotBeNull();
        saved!.Kind.Should().Be(ReportDesignerSaveKind.Publish);
        saved.Definition.Name.Should().Be("Sales Register");
    }

    private static ReportDefinition Definition()
        => new()
        {
            Id = "sales-register",
            Name = "Sales Register",
            PageSetup = new ReportPageSetup
            {
                PageSize = ReportPageSize.A4,
                Margins = new ReportThickness(36),
            },
            Parameters =
            [
                new ReportParameterDefinition
                {
                    Name = "Region",
                    Label = "Region",
                    DataType = ReportParameterType.List,
                    DefaultExpression = "=\"EU\"",
                },
            ],
            DataSets =
            [
                new ReportDataSetDefinition
                {
                    Name = "Orders",
                    Source = new ReportDataSourceReference { Name = "ERP SQL" },
                    Query = "select Customer, Total from Orders",
                    Fields =
                    [
                        new ReportDataSetField("Customer", ReportDataFieldType.String),
                        new ReportDataSetField("Total", ReportDataFieldType.Number),
                    ],
                },
            ],
            Bands = new ReportBandCollection
            {
                PageHeader = new ReportBand { Kind = ReportBandKind.PageHeader, Height = 72 },
                Detail = new ReportBand { Kind = ReportBandKind.Detail, Height = 120 },
                PageFooter = new ReportBand { Kind = ReportBandKind.PageFooter, Height = 48 },
            },
        };
}
