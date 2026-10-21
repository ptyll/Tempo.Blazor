using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Mcp.Wireframe;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Tests for compact and filtered wireframe authoring-guide output.</summary>
public class WireframeAuthoringGuideToolsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void GetAuthoringGuide_DefaultFull_IncludesPropVocabularyAndRoleVocabulary()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(SmallRegistry()));

        root.GetProperty("success").GetBoolean().Should().BeTrue(root.GetRawText());
        root.TryGetProperty("propVocabulary", out var propVocabulary).Should().BeTrue();
        root.TryGetProperty("components", out _).Should().BeFalse("full guide keeps the existing propVocabulary contract");
        propVocabulary.GetArrayLength().Should().Be(4);
        propVocabulary.EnumerateArray().Any(component =>
            component.GetProperty("type").GetString() == "TmButton"
            && component.TryGetProperty("props", out _)).Should().BeTrue();

        root.GetProperty("roleVocabulary").EnumerateArray()
            .Select(role => role.GetProperty("slug").GetString())
            .Should().Contain("search-input");
    }

    [Fact]
    public void GetAuthoringGuide_DocumentsOptionalElementName()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(SmallRegistry(), compact: true));

        root.GetProperty("layoutOps").GetProperty("commonParams").EnumerateArray()
            .Select(p => p.GetString()).Should().Contain("name");
        root.GetProperty("elementFields").GetProperty("name").GetString()
            .Should().Contain("human-readable");
    }

    [Fact]
    public void GetAuthoringGuide_DefaultFull_DoesNotPageWithoutExplicitTake()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(LargeRegistry(205)));

        root.GetProperty("totalCount").GetInt32().Should().Be(205);
        root.GetProperty("propVocabulary").GetArrayLength().Should().Be(205);
    }

    [Fact]
    public void GetAuthoringGuide_ExplicitTake_PagesFullGuide()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            LargeRegistry(205),
            take: 3));

        root.GetProperty("totalCount").GetInt32().Should().Be(205);
        root.GetProperty("propVocabulary").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void GetAuthoringGuide_Compact_OmitsPropVocabularyAndReturnsCompactComponents()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            SmallRegistry(),
            compact: true));

        root.TryGetProperty("propVocabulary", out _).Should().BeFalse();
        var first = root.GetProperty("components").EnumerateArray().First();
        first.TryGetProperty("type", out _).Should().BeTrue();
        first.TryGetProperty("displayName", out _).Should().BeTrue();
        first.TryGetProperty("props", out _).Should().BeFalse();
    }

    [Fact]
    public void GetAuthoringGuide_CategoryFilter_NarrowsComponents()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            SmallRegistry(),
            compact: true,
            category: "Inputs"));

        root.GetProperty("totalCount").GetInt32().Should().Be(2);
        root.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("type").GetString())
            .Should().BeEquivalentTo(["TmFilterableDropdown", "TmSearchInput"]);
    }

    [Fact]
    public void GetAuthoringGuide_TypesFilter_ReturnsOnlyRequestedTypes()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            SmallRegistry(),
            compact: true,
            types: ["TmButton", "TmCard"]));

        root.GetProperty("totalCount").GetInt32().Should().Be(2);
        root.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("type").GetString())
            .Should().BeEquivalentTo(["TmButton", "TmCard"]);
    }

    [Fact]
    public void GetAuthoringGuide_RolesFilter_ReturnsComponentsMappedToRoles()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            SmallRegistry(),
            compact: true,
            roles: ["TmSearchBox"]));

        root.GetProperty("totalCount").GetInt32().Should().Be(2);
        root.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("type").GetString())
            .Should().BeEquivalentTo(["TmFilterableDropdown", "TmSearchInput"]);
    }

    [Fact]
    public void GetAuthoringGuide_SkipTake_PagesFilteredComponents()
    {
        var root = Parse(WireframeAuthoringGuideTools.GetAuthoringGuideScoped(
            SmallRegistry(),
            compact: true,
            skip: 1,
            take: 2));

        root.GetProperty("totalCount").GetInt32().Should().Be(4);
        root.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("type").GetString())
            .Should().Equal("TmCard", "TmFilterableDropdown");
    }

    private static WireframeSchemaRegistry SmallRegistry()
        => new(
        [
            new TestSchemaSource(
                "BuiltIn",
                0,
                Schema("TmButton", "Buttons", "Button", ["button"], withProp: true),
                Schema("TmCard", "DataDisplay", "Card", ["card"]),
                Schema("TmFilterableDropdown", "Inputs", "Filterable Dropdown", ["dropdown", "search-input"]),
                Schema("TmSearchInput", "Inputs", "Search Input", ["search-input"]))
        ]);

    private static WireframeSchemaRegistry LargeRegistry(int count)
        => new(
        [
            new TestSchemaSource(
                "BuiltIn",
                0,
                Enumerable.Range(0, count)
                    .Select(index => Schema(
                        $"TmGenerated{index:D3}",
                        "Generated",
                        $"Generated {index:D3}",
                        ["card"]))
                    .ToArray())
        ]);

    private static WireframeComponentSchema Schema(
        string type,
        string category,
        string displayName,
        IReadOnlyList<string> roles,
        bool withProp = false)
        => new()
        {
            Type = type,
            Category = category,
            DisplayName = displayName,
            Roles = roles,
            Props = withProp
                ? [new PropDef { Name = "label", DisplayName = "Label" }]
                : []
        };

    private sealed class TestSchemaSource(string id, int priority, params WireframeComponentSchema[] schemas)
        : IWireframeSchemaSource
    {
        public string SourceId => id;
        public int Priority => priority;
        public IEnumerable<WireframeComponentSchema> GetSchemas() => schemas;
    }
}
