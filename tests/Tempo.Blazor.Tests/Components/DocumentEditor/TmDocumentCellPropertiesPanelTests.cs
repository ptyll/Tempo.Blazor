using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign plan 146c7e1b, phase 6 (DE-2.2, Q2-5): the cell section of the "Table" inspector.</summary>
public sealed class TmDocumentCellPropertiesPanelTests : LocalizationTestBase
{
    private IRenderedComponent<TmDocumentCellPropertiesPanel> RenderPanel(
        TableCellContent? cell = null,
        Action<TableCellContent>? changed = null)
        => Render<TmDocumentCellPropertiesPanel>(parameters => parameters
            .Add(p => p.Cell, cell ?? new TableCellContent { Id = "cell-1" })
            .Add(p => p.CellChanged, value => changed?.Invoke(value)));

    [Fact]
    public void VerticalAlignment_IsATileRow_WithIconAndVisibleText_AndPressedState()
    {
        TableCellContent? received = null;
        var cut = RenderPanel(new TableCellContent { Id = "cell-1", VerticalAlignment = TableCellVerticalAlignment.Middle }, cell => received = cell);

        var middle = cut.Find("[data-testid='document-cell-properties-valign-middle']");
        middle.GetAttribute("aria-pressed").Should().Be("true");
        middle.QuerySelector(".tm-icon").Should().NotBeNull();
        middle.QuerySelector(".tm-document-cell-properties__tile-label")!.TextContent.Should().Be("Middle");
        middle.QuerySelector(".tm-document-editor__sr-only").Should().BeNull("the text is visible");

        var top = cut.Find("[data-testid='document-cell-properties-valign-top']");
        top.GetAttribute("aria-pressed").Should().Be("false");
        top.QuerySelector(".tm-document-cell-properties__tile-label")!.TextContent.Should().Be("Top");
        cut.FindAll("[data-testid^='document-cell-properties-valign-'][aria-pressed='true']").Should().ContainSingle();

        cut.Find("[data-testid='document-cell-properties-valign-bottom']").Click();
        received!.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Bottom);
    }

    [Fact]
    public void Spans_AreReadOnlyRows_NotInputs()
    {
        var cut = RenderPanel(new TableCellContent { Id = "cell-1", ColumnSpan = 2, RowSpan = 3 });

        var info = cut.Find("[data-testid='document-cell-properties-span-info']");
        info.QuerySelectorAll("input, select, button").Should().BeEmpty();
        var rows = info.QuerySelectorAll(".tm-document-inspector-form__readonly-row");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("2");
        rows[1].TextContent.Should().Contain("3");
    }

    [Fact]
    public void WithoutABackground_NoNativeColorInputAndNoFakeWhite()
    {
        var cut = RenderPanel();

        cut.FindAll("input[type='color']").Should().BeEmpty();
        cut.Find("[data-testid='document-cell-properties-background'] .tm-color-picker-trigger-text").TextContent.Trim().Should().Be("No color");
        cut.Markup.Should().NotContain("#ffffff");
    }

    [Fact]
    public void ImportedBorder_ReadsCustom_AndAPresetEmitsTheOldCss()
    {
        TableCellContent? received = null;
        var cut = RenderPanel(
            new TableCellContent { Id = "cell-1", Borders = new TableCellBorders { Top = "0.5pt solid #000000" } },
            cell => received = cell);

        cut.Find("[data-testid='document-cell-properties-border']").GetAttribute("value").Should().Be("custom");

        cut.Find("[data-testid='document-cell-properties-border']").Change("strong");
        received!.Borders.Top.Should().Be("2px solid var(--tm-color-primary)");
        received.Borders.Left.Should().Be("2px solid var(--tm-color-primary)");
        received.Id.Should().Be("cell-1");
    }

    [Fact]
    public void Padding_TiesItsPxSuffixThroughAriaDescribedBy()
    {
        var cut = RenderPanel(new TableCellContent { Id = "cell-1", Padding = 6 });

        var input = cut.Find("[data-testid='document-cell-properties-padding']");
        cut.Find("#" + input.GetAttribute("aria-describedby")).TextContent.Should().Be("px");
    }

    [Fact]
    public async Task ClearingTheBackground_RaisesACellWithoutOne()
    {
        TableCellContent? received = null;
        var cut = RenderPanel(new TableCellContent { Id = "cell-1", BackgroundColor = "#ffedd5" }, cell => received = cell);

        var picker = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmColorPicker>();
        await cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(null));

        received!.BackgroundColor.Should().BeNull();
    }
}
