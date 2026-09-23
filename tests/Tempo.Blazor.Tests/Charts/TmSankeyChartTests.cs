using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>bUnit tests for the TmSankeyChart SVG renderer and state handling.</summary>
public class TmSankeyChartTests : LocalizationTestBase
{
    [Fact]
    public void SankeyChart_RendersResponsiveAccessibleSvg()
    {
        var cut = RenderChart(ValidData());

        var svg = cut.Find("svg");
        svg.GetAttribute("viewBox").Should().Be("0 0 800 400");
        svg.GetAttribute("preserveAspectRatio").Should().Be("xMidYMid meet");
        svg.GetAttribute("role").Should().Be("img");
        svg.GetAttribute("aria-label").Should().Be("Sankey chart");
        svg.GetAttribute("width").Should().Be("100%");
        svg.GetAttribute("height").Should().Be("100%");
    }

    [Fact]
    public void SankeyChart_RendersOneNodeAndLinkElementPerInput()
    {
        var cut = RenderChart(ValidData());

        cut.FindAll("rect.tm-sankey__node").Should().HaveCount(3);
        cut.FindAll("path.tm-sankey__link").Should().HaveCount(2);
    }

    [Fact]
    public void SankeyChart_UsesCustomNodeColorAndPaletteFallback()
    {
        var cut = RenderChart(ValidData());
        var nodes = cut.FindAll("rect.tm-sankey__node");

        nodes[0].GetAttribute("fill").Should().Be("#123456");
        nodes[1].GetAttribute("fill").Should().Be("#ef4444");
    }

    [Fact]
    public void SankeyChart_RendersProportionalLinkWidthsAndSourceColor()
    {
        var cut = RenderChart(ValidData());
        var links = cut.FindAll("path.tm-sankey__link");
        var firstWidth = AttributeAsDouble(links[0], "stroke-width");
        var secondWidth = AttributeAsDouble(links[1], "stroke-width");

        secondWidth.Should().BeApproximately(firstWidth * 2, 0.002);
        links.Should().OnlyContain(link => link.GetAttribute("stroke") == "#123456");
    }

    /// <summary>
    /// The subject is that the component USES the supplied formatter — whatever string that formatter
    /// returns has to reach the label. The decimal separator is no part of that subject, so the
    /// formatter fixes its own culture.
    /// <para>
    /// It did not, and the test measured the RUNNER instead of the component: <c>$"EUR {value:0.00}"</c>
    /// interpolates under <see cref="CultureInfo.CurrentCulture"/>, so on any cs-CZ machine it produced
    /// "EUR 30,00" against an assert spelling "EUR 30.00" — red on a correct component. Pinning the
    /// thread's culture would have cured the symptom and kept the coupling; making the test's OWN
    /// formatter deterministic removes it.
    /// </para>
    /// <para>
    /// NOT a statement about the default: <c>FormatValue</c> falls back to
    /// <c>CultureInfo.CurrentCulture</c> on purpose, because a chart should render numbers in the
    /// viewer's locale. That default is exercised by the tests that supply no formatter.
    /// </para>
    /// </summary>
    [Fact]
    public void SankeyChart_LabelsIncludeValuesUsingCustomFormatter()
    {
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, ValidData())
            .Add(component => component.ValueFormatter,
                value => $"EUR {value.ToString("0.00", CultureInfo.InvariantCulture)}"));

        var labels = cut.FindAll("text.tm-sankey__label");
        labels.Should().HaveCount(3);
        // The expected values are passed as ONE collection on purpose. FluentAssertions has no params
        // overload of Contain for a collection subject, so Contain("a", "b", "c") binds to
        // Contain(expected, because, becauseArgs): only "a" is asserted and "b"/"c" silently become the
        // failure-message arguments. The test claimed three labels and measured one.
        labels.Select(label => label.TextContent).Should().Contain(new[]
        {
            "Income — EUR 30.00",
            "Housing — EUR 10.00",
            "Savings — EUR 20.00",
        });
    }

    [Fact]
    public void SankeyChart_ShowValuesFalseRendersLabelsWithoutValues()
    {
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, ValidData())
            .Add(component => component.ShowValues, false));

        cut.FindAll("text.tm-sankey__label")
            .Select(label => label.TextContent)
            .Should().Equal("Income", "Housing", "Savings");
    }

    [Fact]
    public void SankeyChart_ShowLabelsFalseHidesLabels()
    {
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, ValidData())
            .Add(component => component.ShowLabels, false));

        cut.FindAll("text.tm-sankey__label").Should().BeEmpty();
    }

    [Fact]
    public void SankeyChart_ReservesHorizontalSpaceForEndpointLabels()
    {
        var cut = RenderChart(ValidData());
        var labels = cut.FindAll("text.tm-sankey__label");

        AttributeAsDouble(labels[0], "x").Should().BeGreaterThanOrEqualTo(120);
        AttributeAsDouble(labels[1], "x").Should().BeLessThanOrEqualTo(680);
        AttributeAsDouble(labels[2], "x").Should().BeLessThanOrEqualTo(680);
    }

    [Fact]
    public void SankeyChart_RendersNodeAndLinkTitles()
    {
        var cut = RenderChart(ValidData());

        // One collection, not loose arguments — see the note in
        // SankeyChart_LabelsIncludeValuesUsingCustomFormatter: loose arguments assert only the first.
        cut.FindAll("rect.tm-sankey__node title")
            .Select(title => title.TextContent)
            .Should().Contain(new[] { "Income — 30", "Housing — 10", "Savings — 20" });
        cut.FindAll("path.tm-sankey__link title")
            .Select(title => title.TextContent)
            .Should().Contain(new[] { "Income → Housing: 10", "Income → Savings: 20" });
    }

    [Fact]
    public void SankeyChart_AppliesContainerOptionsAndLinkOpacity()
    {
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, ValidData())
            .Add(component => component.Width, "640px")
            .Add(component => component.Height, "320px")
            .Add(component => component.LinkOpacity, 0.25)
            .Add(component => component.Class, "cash-flow"));

        var root = cut.Find(".tm-sankey");
        root.ClassList.Should().Contain("cash-flow");
        root.GetAttribute("style").Should().Contain("width:640px").And.Contain("height:320px");
        cut.FindAll("path.tm-sankey__link")
            .Should().OnlyContain(link => link.GetAttribute("stroke-opacity") == "0.25");
    }

    [Fact]
    public void SankeyChart_NonFiniteOpacityAndBlankColorUseSafeDefaults()
    {
        var data = Data(
            [Node("A", color: " "), Node("B")],
            [Link("A", "B", 1)]);
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, data)
            .Add(component => component.LinkOpacity, double.NaN));

        cut.Find("rect.tm-sankey__node").GetAttribute("fill").Should().Be("#3b82f6");
        cut.Find("path.tm-sankey__link").GetAttribute("stroke-opacity").Should().Be("0.4");
    }

    [Fact]
    public void SankeyChart_NodeClickReturnsOriginalNode()
    {
        var data = InteractionData();
        SankeyNode? clicked = null;
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, data)
            .Add(component => component.OnNodeClick, node => clicked = node));

        FindNode(cut, "B").Click();

        clicked.Should().BeSameAs(data.Nodes[1]);
    }

    [Fact]
    public void SankeyChart_LinkClickReturnsOriginalLink()
    {
        var data = InteractionData();
        SankeyLink? clicked = null;
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, data)
            .Add(component => component.OnLinkClick, link => clicked = link));

        FindLink(cut, 1).Click();

        clicked.Should().BeSameAs(data.Links[1]);
    }

    [Fact]
    public void SankeyChart_ClickableElementsAreKeyboardAccessible()
    {
        var data = InteractionData();
        SankeyNode? clickedNode = null;
        SankeyLink? clickedLink = null;
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, data)
            .Add(component => component.OnNodeClick, node => clickedNode = node)
            .Add(component => component.OnLinkClick, link => clickedLink = link));
        var node = FindNode(cut, "B");
        var link = FindLink(cut, 1);

        node.GetAttribute("role").Should().Be("button");
        node.GetAttribute("tabindex").Should().Be("0");
        node.GetAttribute("aria-label").Should().Be("B — 10");
        link.GetAttribute("role").Should().Be("button");
        link.GetAttribute("tabindex").Should().Be("0");
        link.GetAttribute("aria-label").Should().Be("B → C: 10");

        node.TriggerEvent("onfocus", new FocusEventArgs());
        FindNode(cut, "A").ClassList.Should().Contain("tm-sankey__node--highlight");
        FindNode(cut, "D").ClassList.Should().Contain("tm-sankey__node--dimmed");
        node.TriggerEvent("onblur", new FocusEventArgs());
        cut.FindAll(".tm-sankey__node--highlight, .tm-sankey__node--dimmed")
            .Should().BeEmpty();

        node.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = "Enter" });
        // Space activates on KEYUP (keyboard-activation-convention) — the keydown
        // alone must not fire the click.
        link.TriggerEvent("onkeydown", new KeyboardEventArgs { Key = " " });
        clickedLink.Should().BeNull("Space keydown must not activate — the release does");
        link.TriggerEvent("onkeyup", new KeyboardEventArgs { Key = " " });

        clickedNode.Should().BeSameAs(data.Nodes[1]);
        clickedLink.Should().BeSameAs(data.Links[1]);
    }

    [Fact]
    public void SankeyChart_NodeHoverHighlightsConnectedElementsAndDimsOthers()
    {
        var cut = RenderChart(InteractionData());

        FindNode(cut, "B").TriggerEvent("onmouseover", new MouseEventArgs());

        FindNode(cut, "A").ClassList.Should().Contain("tm-sankey__node--highlight");
        FindNode(cut, "B").ClassList.Should().Contain("tm-sankey__node--highlight");
        FindNode(cut, "C").ClassList.Should().Contain("tm-sankey__node--highlight");
        FindNode(cut, "D").ClassList.Should().Contain("tm-sankey__node--dimmed");
        FindLabel(cut, "A").ClassList.Should().Contain("tm-sankey__label--highlight");
        FindLabel(cut, "B").ClassList.Should().Contain("tm-sankey__label--highlight");
        FindLabel(cut, "C").ClassList.Should().Contain("tm-sankey__label--highlight");
        FindLabel(cut, "D").ClassList.Should().Contain("tm-sankey__label--dimmed");
        FindLink(cut, 0).ClassList.Should().Contain("tm-sankey__link--highlight");
        FindLink(cut, 1).ClassList.Should().Contain("tm-sankey__link--highlight");
        FindLink(cut, 2).ClassList.Should().Contain("tm-sankey__link--dimmed");
    }

    [Fact]
    public void SankeyChart_LinkHoverHighlightsLinkAndEndpointsThenMouseoutRestoresClasses()
    {
        var cut = RenderChart(InteractionData());
        var hoveredLink = FindLink(cut, 0);

        hoveredLink.TriggerEvent("onmouseover", new MouseEventArgs());

        FindLink(cut, 0).ClassList.Should().Contain("tm-sankey__link--highlight");
        FindLink(cut, 1).ClassList.Should().Contain("tm-sankey__link--dimmed");
        FindLink(cut, 2).ClassList.Should().Contain("tm-sankey__link--dimmed");
        FindNode(cut, "A").ClassList.Should().Contain("tm-sankey__node--highlight");
        FindNode(cut, "B").ClassList.Should().Contain("tm-sankey__node--highlight");
        FindNode(cut, "C").ClassList.Should().Contain("tm-sankey__node--dimmed");
        FindNode(cut, "D").ClassList.Should().Contain("tm-sankey__node--dimmed");
        FindLabel(cut, "A").ClassList.Should().Contain("tm-sankey__label--highlight");
        FindLabel(cut, "B").ClassList.Should().Contain("tm-sankey__label--highlight");
        FindLabel(cut, "C").ClassList.Should().Contain("tm-sankey__label--dimmed");
        FindLabel(cut, "D").ClassList.Should().Contain("tm-sankey__label--dimmed");

        FindLink(cut, 0).TriggerEvent("onmouseout", new MouseEventArgs());

        cut.FindAll(".tm-sankey__link--highlight, .tm-sankey__link--dimmed")
            .Should().BeEmpty();
        cut.FindAll(".tm-sankey__node--highlight, .tm-sankey__node--dimmed")
            .Should().BeEmpty();
    }

    [Fact]
    public void SankeyChart_HighlightOnHoverFalseIgnoresHover()
    {
        var cut = Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, InteractionData())
            .Add(component => component.HighlightOnHover, false));

        FindNode(cut, "B").TriggerEvent("onmouseover", new MouseEventArgs());

        cut.FindAll(".tm-sankey__link--highlight, .tm-sankey__link--dimmed")
            .Should().BeEmpty();
        cut.FindAll(".tm-sankey__node--highlight, .tm-sankey__node--dimmed")
            .Should().BeEmpty();
    }

    [Fact]
    public void SankeyChart_ReplacingDataClearsHoverState()
    {
        var cut = RenderChart(InteractionData());
        FindNode(cut, "B").TriggerEvent("onmouseover", new MouseEventArgs());

        cut.Render(parameters => parameters
            .Add(component => component.Data, Data(
                [Node("X"), Node("Y")],
                [Link("X", "Y", 2)])));

        cut.FindAll(".tm-sankey__link--highlight, .tm-sankey__link--dimmed")
            .Should().BeEmpty();
        cut.FindAll(".tm-sankey__node--highlight, .tm-sankey__node--dimmed")
            .Should().BeEmpty();
    }

    [Fact]
    public void SankeyChart_EmptyDataRendersLocalizedNoDataState()
    {
        var cut = RenderChart(Data([], []));

        cut.Find(".tm-sankey__error").TextContent.Should().Be("No data available");
        cut.FindAll("svg").Should().BeEmpty();
    }

    [Fact]
    public void SankeyChart_CycleRendersLocalizedErrorWithoutThrowing()
    {
        var cut = RenderChart(Data(
            [Node("A"), Node("B")],
            [Link("A", "B", 1), Link("B", "A", 1)]));

        cut.Find(".tm-sankey__error").TextContent.Should().Be("Flow data contains a cycle");
        cut.FindAll("svg").Should().BeEmpty();
    }

    [Fact]
    public void SankeyChart_InvalidDataRendersLocalizedErrorWithoutThrowing()
    {
        var cut = RenderChart(Data(
            [Node("A")],
            [Link("A", "missing", 1)]));

        cut.Find(".tm-sankey__error").TextContent.Should().Be("Invalid flow data");
        cut.FindAll("svg").Should().BeEmpty();
    }

    private IRenderedComponent<TmSankeyChart> RenderChart(SankeyData data) =>
        Render<TmSankeyChart>(parameters => parameters
            .Add(component => component.Data, data));

    private static SankeyData ValidData() =>
        Data(
            [
                Node("income", "Income", "#123456"),
                Node("housing", "Housing"),
                Node("savings", "Savings"),
            ],
            [
                Link("income", "housing", 10),
                Link("income", "savings", 20),
            ]);

    private static SankeyData InteractionData() =>
        Data(
            [Node("A"), Node("B"), Node("C"), Node("D")],
            [
                Link("A", "B", 10),
                Link("B", "C", 10),
                Link("D", "C", 5),
            ]);

    private static SankeyData Data(
        IReadOnlyList<SankeyNode> nodes,
        IReadOnlyList<SankeyLink> links) =>
        new()
        {
            Nodes = nodes,
            Links = links,
        };

    private static SankeyNode Node(string id, string? label = null, string? color = null) =>
        new()
        {
            Id = id,
            Label = label ?? id,
            Color = color,
        };

    private static SankeyLink Link(string sourceId, string targetId, double value) =>
        new()
        {
            SourceId = sourceId,
            TargetId = targetId,
            Value = value,
        };

    private static double AttributeAsDouble(AngleSharp.Dom.IElement element, string name) =>
        double.Parse(element.GetAttribute(name)!, CultureInfo.InvariantCulture);

    private static AngleSharp.Dom.IElement FindNode(
        IRenderedComponent<TmSankeyChart> cut,
        string nodeId) =>
        cut.Find($"rect.tm-sankey__node[data-node-id='{nodeId}']");

    private static AngleSharp.Dom.IElement FindLink(
        IRenderedComponent<TmSankeyChart> cut,
        int linkIndex) =>
        cut.Find($"path.tm-sankey__link[data-link-index='{linkIndex}']");

    private static AngleSharp.Dom.IElement FindLabel(
        IRenderedComponent<TmSankeyChart> cut,
        string nodeId) =>
        cut.Find($"text.tm-sankey__label[data-node-id='{nodeId}']");
}
