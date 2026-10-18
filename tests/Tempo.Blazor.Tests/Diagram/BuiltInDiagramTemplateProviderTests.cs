using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Templates;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class BuiltInDiagramTemplateProviderTests
{
    [Fact]
    public async Task LoadsTheEmbeddedCatalog_WithThumbnails()
    {
        // Regression (D15): the resource lookup used the assembly name, but the RootNamespace
        // prefixes the embedded resources — the catalog silently loaded empty. The ids below are
        // the built-in catalog entries; every template WITH a document gets a generated SVG
        // thumbnail (label-less, transparent, 16:10).
        var categories = await new BuiltInDiagramTemplateProvider().GetTemplateCategoriesAsync();
        var templates = categories.SelectMany(c => c.Templates).ToList();

        var ids = templates.Select(t => t.Id).ToList();
        ids.Should().Contain("blank");
        ids.Should().Contain("flowchart-basic");
        ids.Should().Contain("uml-class");
        ids.Should().Contain("er-diagram");
        ids.Should().Contain("network-topology");

        templates.Where(t => !string.IsNullOrEmpty(t.DocumentJson)).Should().OnlyContain(t =>
            !string.IsNullOrEmpty(t.ThumbnailUrl) && t.ThumbnailUrl.StartsWith("data:image/svg+xml;base64,", StringComparison.Ordinal),
            "every document-backed template gets a generated thumbnail");
    }

    [Fact]
    public async Task BlankTemplate_HasNoGeneratedThumbnail_TheCardRendersTheCategoryIconPlaceholder()
    {
        var categories = await new BuiltInDiagramTemplateProvider().GetTemplateCategoriesAsync();
        var blank = categories.SelectMany(c => c.Templates).First(t => t.Id == "blank");

        blank.DocumentJson.Should().BeNullOrEmpty();
        blank.ThumbnailUrl.Should().BeNullOrEmpty(
            "a document without nodes has nothing to preview — the card falls back to the category icon placeholder");
    }

    [Fact]
    public async Task EveryDocumentJsonPath_InTheCatalog_ResolvesToANonEmptyEmbeddedResource()
    {
        // The csproj pins LogicalName to the exact resource name — this proves every catalog
        // path still resolves (a renamed file or a drifted convention fails here, not in the
        // browser with a blank card).
        var indexJson = await BuiltInDiagramTemplateProvider.ReadResourceAsync("diagram-templates/index.json");
        indexJson.Should().NotBeNullOrEmpty("the embedded catalog index must exist");

        using var doc = System.Text.Json.JsonDocument.Parse(indexJson);
        foreach (var el in doc.RootElement.GetProperty("templates").EnumerateArray())
        {
            if (!el.TryGetProperty("documentJson", out var pathEl) || pathEl.ValueKind != System.Text.Json.JsonValueKind.String)
                continue;
            var path = pathEl.GetString();
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var content = await BuiltInDiagramTemplateProvider.ReadResourceAsync(path);
            content.Should().NotBeNullOrEmpty($"the documentJson path '{path}' must resolve to a non-empty embedded resource");
        }
    }

    [Fact]
    public async Task ExtendedTemplates_HaveThumbnails()
    {
        var categories = await new ExtendedDiagramTemplateProvider().GetTemplateCategoriesAsync();
        var templates = categories.SelectMany(c => c.Templates).ToList();

        templates.Should().NotBeEmpty();
        templates.Should().OnlyContain(t =>
            !string.IsNullOrEmpty(t.ThumbnailUrl) && t.ThumbnailUrl.StartsWith("data:image/svg+xml;base64,", StringComparison.Ordinal),
            "every extended template gets a generated thumbnail");
    }

    [Fact]
    public void Thumbnails_OmitLabels_AndUseATransparent16By10Viewport()
    {
        var doc = new Tempo.Blazor.Components.Diagram.Models.DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new Tempo.Blazor.Components.Diagram.Models.DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100, Y = 100, W = 120, H = 60,
            Data = new Dictionary<string, object> { ["label"] = "Illegible at card size" }
        });

        var svg = Tempo.Blazor.Components.Diagram.Services.DiagramThumbnailSvgGenerator.Generate(doc,
            new Tempo.Blazor.Components.Diagram.Services.DiagramThumbnailOptions
            {
                IncludeLabels = false,
                OpaqueBackground = false,
                Width = 320,
                Height = 200
            });

        svg.Should().NotContain("Illegible at card size", "template previews draw no labels");
        svg.Should().NotContain("fill=\"#ffffff\"", "template previews stay transparent — the card provides the surface");
        svg.Should().Contain("width=\"320\"");
        svg.Should().Contain("height=\"200\"");
        svg.Should().Contain("preserveAspectRatio=\"xMidYMid meet\"");
        svg.Should().Contain("<rect", "the node itself still renders");
    }
}
