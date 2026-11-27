using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>Redesign plan 146c7e1b, phase 6 (DE-2.2, Q2-5): the table section of the "Table" inspector.</summary>
public sealed class TmDocumentTablePropertiesPanelTests : LocalizationTestBase
{
    private const string DefaultBorderCss = "1px solid var(--tm-color-border)";
    private const string StrongBorderCss = "2px solid var(--tm-color-primary)";

    private IRenderedComponent<TmDocumentTablePropertiesPanel> RenderPanel(
        TableLayoutContent? layout = null,
        Action<TableLayoutContent>? changed = null)
        => Render<TmDocumentTablePropertiesPanel>(parameters => parameters
            .Add(p => p.Layout, layout ?? new TableLayoutContent())
            .Add(p => p.LayoutChanged, value => changed?.Invoke(value)));

    [Fact]
    public void WithoutABackground_TheColorPickerHasNoValue_NotAMadeUpWhite()
    {
        var cut = RenderPanel(new TableLayoutContent { BackgroundColor = null });

        cut.FindAll("input[type='color']").Should().BeEmpty("the native colour input always carried a value, so it faked #ffffff");
        var picker = cut.Find("[data-testid='document-table-properties-background']");
        picker.ClassList.Should().Contain("tm-color-picker");
        picker.QuerySelector(".tm-color-picker-trigger-text")!.TextContent.Trim().Should().Be("No color");
        picker.QuerySelector(".tm-color-picker-trigger-color")!.GetAttribute("style")!.Should().Contain("transparent");
        cut.Markup.Should().NotContain("#ffffff");
    }

    [Fact]
    public void WithABackground_ThePickerShowsItsHex()
    {
        var cut = RenderPanel(new TableLayoutContent { BackgroundColor = "#eef2ff" });

        cut.Find("[data-testid='document-table-properties-background'] .tm-color-picker-trigger-text")
            .TextContent.Trim().Should().Be("#eef2ff");
    }

    [Fact]
    public void Picker_IsTheCompactVariant_NoGradientNoAlpha()
    {
        var cut = RenderPanel();

        var picker = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmColorPicker>().Instance;
        picker.ShowGradient.Should().BeFalse();
        picker.ShowAlpha.Should().BeFalse();
        picker.ShowHexInput.Should().BeTrue();
        picker.ShowClearButton.Should().BeTrue("\"No color\" clears the background");
    }

    [Fact]
    public async Task ClearingThePicker_RaisesALayoutWithoutBackground()
    {
        TableLayoutContent? received = null;
        var cut = RenderPanel(new TableLayoutContent { BackgroundColor = "#eef2ff", Width = 600 }, layout => received = layout);

        var picker = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmColorPicker>();
        await cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(null));

        received.Should().NotBeNull();
        received!.BackgroundColor.Should().BeNull();
        received.Width.Should().Be(600, "the rest of the layout is carried over");
    }

    [Fact]
    public void PickingAColor_RaisesItAsTheBackground()
    {
        TableLayoutContent? received = null;
        var cut = RenderPanel(changed: layout => received = layout);

        var picker = cut.FindComponent<Tempo.Blazor.Components.Inputs.TmColorPicker>();
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync("#ffedd5"));

        received!.BackgroundColor.Should().Be("#ffedd5");
    }

    [Theory]
    [InlineData("0.5pt solid #000000")]
    [InlineData("1px dashed red")]
    [InlineData("3px double rgb(1, 2, 3)")]
    public void ABorderThatIsNoPreset_ReadsCustom(string css)
    {
        var cut = RenderPanel(new TableLayoutContent
        {
            Borders = new TableCellBorders { Top = css, Right = css, Bottom = css, Left = css }
        });

        var select = cut.Find("[data-testid='document-table-properties-border']");
        select.GetAttribute("value").Should().Be("custom");
        select.QuerySelector("option[value='custom']")!.TextContent.Should().Be("Custom");
        select.QuerySelector("option[value='custom']")!.HasAttribute("disabled").Should().BeFalse("it is the current state");
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("", "none")]
    [InlineData(DefaultBorderCss, "default")]
    [InlineData(StrongBorderCss, "strong")]
    public void KnownBorders_ReadAsTheirPreset(string? css, string preset)
    {
        var cut = RenderPanel(new TableLayoutContent { Borders = new TableCellBorders { Top = css } });

        cut.Find("[data-testid='document-table-properties-border']").GetAttribute("value").Should().Be(preset);
        cut.Find("option[value='custom']").HasAttribute("disabled").Should().BeTrue("Custom is only a display state of an imported value");
    }

    [Theory]
    [InlineData("default", DefaultBorderCss)]
    [InlineData("strong", StrongBorderCss)]
    [InlineData("none", "")]
    public void PickingAPreset_EmitsTheSameCssStringAsBefore(string preset, string expected)
    {
        TableLayoutContent? received = null;
        var cut = RenderPanel(changed: layout => received = layout);

        cut.Find("[data-testid='document-table-properties-border']").Change(preset);

        received.Should().NotBeNull();
        received!.Borders.Top.Should().Be(expected);
        received.Borders.Right.Should().Be(expected);
        received.Borders.Bottom.Should().Be(expected);
        received.Borders.Left.Should().Be(expected);
    }

    [Fact]
    public void PickingCustom_WritesNothing()
    {
        TableLayoutContent? received = null;
        var cut = RenderPanel(
            new TableLayoutContent { Borders = new TableCellBorders { Top = "0.5pt solid #000000" } },
            layout => received = layout);

        cut.Find("[data-testid='document-table-properties-border']").Change("custom");

        received.Should().BeNull("an imported border is left alone until a preset is chosen");
    }

    [Fact]
    public void NumberFields_TieTheirPxSuffixThroughAriaDescribedBy()
    {
        var cut = RenderPanel(new TableLayoutContent { Width = 600, CellPadding = 8 });

        foreach (var testId in new[] { "document-table-properties-width", "document-table-properties-padding" })
        {
            var input = cut.Find($"[data-testid='{testId}']");
            var describedBy = input.GetAttribute("aria-describedby");
            describedBy.Should().NotBeNullOrWhiteSpace();
            var suffix = cut.Find("#" + describedBy);
            suffix.TextContent.Should().Be("px");
            suffix.ClassList.Should().Contain("tm-document-inspector-form__suffix");
        }
    }

    [Fact]
    public void Alignment_IsAnIconSegmentedGroup_WithPressedState()
    {
        TableLayoutContent? received = null;
        var cut = RenderPanel(new TableLayoutContent { Alignment = TableHorizontalAlignment.Center }, layout => received = layout);

        cut.Find("[data-testid='document-table-properties-align-center']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='document-table-properties-align-left']").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-testid='document-table-properties-align-left']").GetAttribute("aria-label").Should().Be("Left");
        cut.Find("[data-testid='document-table-properties-align-left']").QuerySelector(".tm-icon").Should().NotBeNull();

        cut.Find("[data-testid='document-table-properties-align-right']").Click();
        received!.Alignment.Should().Be(TableHorizontalAlignment.Right);
    }

    [Fact]
    public void Root_IsTheEmbeddedFormPattern_WithAFoldableSection()
    {
        var cut = RenderPanel();

        var root = cut.Find("[data-testid='document-table-properties-panel']");
        root.ClassList.Should().Contain("tm-document-inspector-form--embedded");
        root.GetAttribute("role").Should().Be("region");
        root.QuerySelector(".tm-document-inspector-form__section-toggle")!.GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void WidthAndPadding_RaiseTheLayout()
    {
        TableLayoutContent? received = null;
        var cut = RenderPanel(changed: layout => received = layout);

        cut.Find("[data-testid='document-table-properties-width']").Change("640");
        received!.Width.Should().Be(640);
        cut.Find("[data-testid='document-table-properties-padding']").Change("12");
        received!.CellPadding.Should().Be(12);
    }
}
