using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// axe "critical" regression guard for the Properties panel (<c>.tm-wd-props</c>): every input / select / textarea
/// (checkboxes, colour swatches and read-only fields included) must expose an accessible name in EVERY panel state -
/// empty selection (Canvas / Page / Grid), a single element with every prop type, a multi-selection, a connector -
/// in the built-in cultures. The name is resolved the way the accessible-name algorithm does (aria-labelledby, aria-label,
/// <c>label[for]</c>, a wrapping label), and a label that resolves to empty text does not count.
/// </summary>
public class WireframePropertiesPanelA11yTests : LocalizationTestBase
{
    private static WireframeComponentDef RichDef() => new()
    {
        Type = "TmButton", DisplayName = "TmButton", Category = "Test", DefaultWidth = 120, DefaultHeight = 36,
        IsBuiltIn = true, RenderSvg = (_, _) => { },
        Props =
        [
            new PropDef { Name = "text", DisplayName = "Text", Type = PropType.String, Category = "Content", IsRequired = true },
            new PropDef { Name = "variant", DisplayName = "Variant", Type = PropType.Enum, Options = ["primary", "secondary"], Category = "Appearance" },
            new PropDef { Name = "fill", DisplayName = "Fill", Type = PropType.Color, Category = "Appearance" },
            new PropDef { Name = "radius", DisplayName = "Radius", Type = PropType.Int, Category = "Appearance" },
            new PropDef { Name = "opacity", DisplayName = "Opacity", Type = PropType.Double, Category = "Appearance" },
            new PropDef { Name = "items", DisplayName = "Items", Type = PropType.StringList, Category = "Content" },
            new PropDef { Name = "disabled", DisplayName = "Disabled", Type = PropType.Bool, Category = "Behavior" },
        ],
    };

    private bool _registryAdded;

    private WireframeDocument NewDoc(out WireframeElement a, out WireframeElement b, out WireframeElement c)
    {
        var doc = new WireframeDocument { Title = "T" };
        doc.EnsureActivePage();
        a = WireframeDocumentExtensions.NewElement("TmButton", 10, 10, 100, 30);
        b = WireframeDocumentExtensions.NewElement("TmButton", 150, 10, 100, 30);
        c = WireframeDocumentExtensions.NewElement("TmButton", 300, 10, 100, 30);
        b.GroupId = "g1"; // surfaces the read-only Group field
        doc.Elements.Add(a); doc.Elements.Add(b); doc.Elements.Add(c);
        doc.Connectors.Add(new WireframeConnector { FromId = a.Id, ToId = b.Id });
        return doc;
    }

    private IRenderedComponent<TmWireframePropertiesPanel> RenderPanel(WireframeDocument doc, string[] selected, string[]? connectors = null)
    {
        if (!_registryAdded)
        {
            var registry = new WireframeComponentRegistry();
            registry.RegisterDefinition(RichDef());
            Services.AddSingleton(registry);
            _registryAdded = true;
        }

        return Render<TmWireframePropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, selected)
            .Add(c => c.SelectedConnectorIds, connectors ?? []));
    }

    /// <summary>The accessible name of <paramref name="control"/> per the (subset of the) accname algorithm axe checks.</summary>
    private static string AccessibleName(IRenderedComponent<TmWireframePropertiesPanel> cut, IElement control)
    {
        var labelledBy = control.GetAttribute("aria-labelledby");
        if (!string.IsNullOrWhiteSpace(labelledBy))
        {
            var text = string.Join(' ', labelledBy.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => cut.FindAll($"[id='{id}']").FirstOrDefault()?.TextContent.Trim() ?? ""));
            if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
        }

        var aria = control.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(aria)) return aria.Trim();

        var id2 = control.GetAttribute("id");
        if (!string.IsNullOrEmpty(id2))
        {
            var labels = cut.FindAll($"label[for='{id2}']")
                .Select(l => l.TextContent.Replace("*", "").Trim()).Where(t => t.Length > 0).ToList();
            if (labels.Count > 0) return string.Join(' ', labels);
        }

        var wrapping = control.Closest("label");
        if (wrapping is not null)
        {
            var text = wrapping.TextContent.Trim();
            if (text.Length > 0) return text;
        }

        return string.Empty;
    }

    private static void AssertEveryControlIsNamed(IRenderedComponent<TmWireframePropertiesPanel> cut, int minimumControls)
    {
        var controls = cut.FindAll("input, select, textarea")
            .Where(e => e.GetAttribute("type") != "hidden").ToList();
        controls.Count.Should().BeGreaterThanOrEqualTo(minimumControls, "the state under test must actually render its controls");
        var unnamed = controls.Where(e => AccessibleName(cut, e).Length == 0)
            .Select(e => e.OuterHtml.Length > 140 ? e.OuterHtml[..140] : e.OuterHtml).ToList();
        unnamed.Should().BeEmpty("every control of the Properties panel needs an accessible name (axe: label, critical)");

        // ids referenced by a label must be unique, otherwise the association silently binds the wrong control
        var ids = cut.FindAll("[id]").Select(e => e.Id!).ToList();
        ids.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    public void EmptySelection_CanvasPageAndGridControls_AreNamed(string culture)
    {
        UseCulture(culture);
        var doc = NewDoc(out _, out _, out _);
        var cut = RenderPanel(doc, []);

        AssertEveryControlIsNamed(cut, minimumControls: 6);
        cut.Find("[data-testid='wd-grid-show']").Closest("label")!.TextContent.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    public void SingleElement_EveryPropTypeControl_IsNamed(string culture)
    {
        UseCulture(culture);
        var doc = NewDoc(out var a, out _, out _);
        var cut = RenderPanel(doc, [a.Id]);

        AssertEveryControlIsNamed(cut, minimumControls: 14);
        // every declared prop type is present: bool, enum, colour (swatch + text), number, list, string
        cut.FindAll("input[type=checkbox]").Count.Should().BeGreaterThanOrEqualTo(2, "Locked + the Bool prop");
        cut.FindAll("input[type=color]").Should().NotBeEmpty();
        cut.FindAll("select").Count.Should().BeGreaterThanOrEqualTo(2, "Layer + the enum prop");
        // the bool prop's checkbox is named by the visible row label ("Disabled"), not by an empty wrapper
        AccessibleName(cut, cut.Find("input[type=checkbox][aria-labelledby]")).Should().Be("Disabled");
    }

    [Fact]
    public void ReadOnlyFields_AreNamedToo()
    {
        var doc = NewDoc(out _, out var b, out _);
        var cut = RenderPanel(doc, [b.Id]);

        var readOnly = cut.FindAll("input[readonly]");
        readOnly.Should().NotBeEmpty("Type and Group are read-only fields");
        readOnly.Should().OnlyContain(e => AccessibleName(cut, e).Length > 0);
    }

    [Fact]
    public void MultiSelection_ControlsAndArrangeGroups_AreNamed()
    {
        var doc = NewDoc(out var a, out var b, out var c);
        var cut = RenderPanel(doc, [a.Id, b.Id, c.Id]);

        AssertEveryControlIsNamed(cut, minimumControls: 8);
        // the Align / Distribute button rows are labelled groups, not orphan <label>s
        var groups = cut.FindAll("[role=group]");
        groups.Count.Should().Be(2);
        groups.Should().OnlyContain(g => g.GetAttribute("aria-labelledby") != null
            && cut.Find($"[id='{g.GetAttribute("aria-labelledby")}']").TextContent.Trim().Length > 0);
        cut.FindAll("label:not([for])").Where(l => l.QuerySelector("input") is null)
            .Should().BeEmpty("a <label> without for/control is an orphan (axe: label)");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("cs")]
    public void Connector_AllControlsAndBothColourInputs_AreNamed(string culture)
    {
        UseCulture(culture);
        var doc = NewDoc(out _, out _, out _);
        var cut = RenderPanel(doc, [], [doc.Connectors[0].Id]);

        AssertEveryControlIsNamed(cut, minimumControls: 7);
        var swatch = cut.Find("input[type=color]");
        AccessibleName(cut, swatch).Should().Contain(culture == "cs" ? "Barva čáry" : "Stroke");
    }

    [Fact]
    public void NoLabelPointsAtAMissingControl()
    {
        var doc = NewDoc(out var a, out _, out _);
        foreach (var cut in new[] { RenderPanel(doc, []), RenderPanel(doc, [a.Id]), RenderPanel(doc, [], [doc.Connectors[0].Id]) })
        {
            foreach (var label in cut.FindAll("label[for]"))
            {
                cut.FindAll($"[id='{label.GetAttribute("for")}']").Should().HaveCount(1, $"label '{label.TextContent.Trim()}' must target exactly one control");
            }
        }
    }

    private void UseCulture(string culture)
    {
        if (culture == "cs") UseCzechLocalization();
    }
}
