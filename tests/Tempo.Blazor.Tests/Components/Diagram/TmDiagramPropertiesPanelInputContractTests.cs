using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// The properties panel renders raw input/textarea elements carrying the .tm-text-input class.
/// Nothing else in the library styles that class (core ships .tm-input, the demo only sets
/// font-family), so the browser's UA default leaked through: in dark theme the page W/H/Scale
/// and the node X/Y/W/H fields painted as WHITE boxes on the dark panel with barely legible
/// text (final UX review, DiagramEditor phase 8, dark editor capture). Freeze the contract:
/// the package stylesheet itself must give the fields the tokenized control look that follows
/// the theme — surface background, primary text, a real border — instead of UA chrome.
/// Also freezes the input binding contract (F3): the replace-shape search opens empty, never
/// pre-filled with the C# field name.
/// </summary>
public class TmDiagramPropertiesPanelInputContractTests : LocalizationTestBase
{
    public TmDiagramPropertiesPanelInputContractTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void ReplaceShapeSearch_IsEmptyOnFirstRender()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Nodes.Add(new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 10, W = 100, H = 50 });

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, new[] { doc.Nodes[0].Id })
            .Add(c => c.ReadOnly, false));

        // Expand the "Replace shape" section (click the header of its TmSection).
        var header = cut.FindAll(".tm-section__header, .tm-section-header")
            .FirstOrDefault(h => h.TextContent.Contains("Replace shape", StringComparison.OrdinalIgnoreCase))
            ?? cut.FindAll("button").FirstOrDefault(b => b.TextContent.Contains("Replace shape", StringComparison.OrdinalIgnoreCase));
        header.Should().NotBeNull("the Replace shape section renders a collapsible header");
        header!.Click();
        cut.Render();

        var input = cut.Find(".tm-diagram-properties input[type='search']");
        input.GetAttribute("value").Should().BeEmpty(
            "the F3 literal binding used to pre-fill the field with '_replaceShapeSearch'");
    }

    [Fact]
    public void ReadOnly_SelectedNode_ShowsReadOnlySummary()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        var node = new DiagramNode { StencilId = "general.rectangle", X = 10, Y = 20, W = 120, H = 60 };
        node.Data["label"] = "Alpha";
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramPropertiesPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIds, new[] { node.Id })
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-diagram-properties__readonly-summary").Should().HaveCount(1,
            "the R1 inspection selection stays visible as a plain fact list");
        var summary = cut.Find(".tm-diagram-properties__readonly-summary").TextContent;
        summary.Should().Contain("Rectangle", "the shape display name is part of the summary");
        summary.Should().Contain("Alpha", "text fields render as plain text");
        summary.Should().Contain("10", "X is part of the summary");
        summary.Should().Contain("120", "W is part of the summary");
        cut.FindAll(".tm-diagram-properties__readonly-summary input, .tm-diagram-properties__readonly-summary textarea")
            .Should().BeEmpty("the summary shows facts, never editing widgets");

        // The read-only note stays as the header hint above the summary.
        cut.Find(".tm-diagram-properties__empty").TextContent.Should().Contain("Read-only mode");
    }

    private static string PackageCss()
    {        var root = ThemeCss.RepositoryRoot().FullName;
        return Path.Combine(root, "src", "Tempo.Blazor.DiagramEditor", "wwwroot", "css",
            "components", "_diagram-editor.css");
    }

    private static string? Property(string selector, string property)
    {
        var css = ThemeCss.StripComments(File.ReadAllText(PackageCss()));
        foreach (Match rule in Regex.Matches(css, @"(?<selector>[^{}]+)\{(?<body>[^{}]*)\}"))
        {
            var parts = ThemeCss.Normalise(rule.Groups["selector"].Value)
                .Split(',')
                .Select(part => part.Trim())
                .ToList();
            if (!parts.Contains(selector, StringComparer.Ordinal))
            {
                continue;
            }

            foreach (var declaration in rule.Groups["body"].Value.Split(';'))
            {
                var separator = declaration.IndexOf(':', StringComparison.Ordinal);
                if (separator > 0 &&
                    string.Equals(declaration[..separator].Trim(), property, StringComparison.Ordinal))
                {
                    return ThemeCss.Normalise(declaration[(separator + 1)..]);
                }
            }
        }

        return null;
    }

    [Fact]
    public void TextInput_Fields_AreStyledWithThemeTokens()
    {
        const string selector = ".tm-diagram-properties__field .tm-text-input";

        Property(selector, "background-color")
            .Should().Be("var(--tm-bg-surface)",
                "the UA white default must not leak onto the dark panel");

        Property(selector, "color")
            .Should().Be("var(--tm-text-primary)",
                "the value text must follow the theme, not the UA default");

        Property(selector, "border")
            .Should().Contain("var(--tm-border-color)",
                "a visible border keeps the field readable on both themes");

        Property(selector, "border-radius")
            .Should().NotBeNull("the fields should match the control radius convention");
    }

    [Fact]
    public void TextInput_Fields_StayFullWidth_AndTextareaKeepsRoomToBreathe()
    {
        const string selector = ".tm-diagram-properties__field .tm-text-input";

        Property(selector, "width").Should().Be("100%",
            "the regression guard from the original rule must survive");
    }
}
