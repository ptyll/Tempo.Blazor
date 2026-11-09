using System.Text.Json;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Mcp;
using Tempo.Blazor.Mcp.Wireframe;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Tests for the component catalog tools (list + get schema with did-you-mean).</summary>
public class WireframeCatalogToolsTests
{
    private static WireframeSchemaRegistry Registry()
        => new([new BuiltInComponentSchemas()]);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ListComponents_Compact_ReturnsTypeCategoryDisplayName_Only()
    {
        var root = Parse(WireframeComponentCatalogTools.ListComponents(Registry(), compact: true));

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        var first = root.GetProperty("items").EnumerateArray().First();
        first.TryGetProperty("type", out _).Should().BeTrue();
        first.TryGetProperty("displayName", out _).Should().BeTrue();
        first.TryGetProperty("props", out _).Should().BeFalse();
    }

    [Fact]
    public void ListComponents_Full_IncludesProps()
    {
        var root = Parse(WireframeComponentCatalogTools.ListComponents(Registry(), compact: false));

        var withProps = root.GetProperty("items").EnumerateArray()
            .Any(i => i.TryGetProperty("props", out _));
        withProps.Should().BeTrue();
    }

    [Fact]
    public void ListComponents_CategoryFilter_NarrowsResults()
    {
        var registry = Registry();
        var category = registry.GetCategories().First();

        var root = Parse(WireframeComponentCatalogTools.ListComponents(registry, compact: true, category: category));

        root.GetProperty("items").EnumerateArray()
            .Should().OnlyContain(i => i.GetProperty("category").GetString() == category);
    }

    [Fact]
    public void ListComponents_Paging_LimitsItems_ButReportsTotal()
    {
        var registry = Registry();
        var total = registry.GetAll().Count();

        var root = Parse(WireframeComponentCatalogTools.ListComponents(registry, compact: true, take: 1));

        root.GetProperty("totalCount").GetInt32().Should().Be(total);
        root.GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public void GetComponentSchema_KnownType_ReturnsFullContract()
    {
        var registry = Registry();
        var type = registry.GetAll().First().Type;

        var root = Parse(WireframeComponentCatalogTools.GetComponentSchema(registry, type));

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        root.GetProperty("component").GetProperty("type").GetString().Should().Be(type);
    }

    [Fact]
    public void GetComponentSchema_UnknownType_ReturnsNotFound_WithSuggestion()
    {
        var registry = Registry();
        var known = registry.GetAll().First().Type;
        var typo = known + "X";

        var root = Parse(WireframeComponentCatalogTools.GetComponentSchema(registry, typo));

        root.GetProperty("success").GetBoolean().Should().BeFalse();
        root.GetProperty("error").GetString().Should().Be("not_found");
        root.GetProperty("message").GetString().Should().Contain(known);
    }

    [Fact]
    public void SuggestType_UsesRoleSynonymsBeforeNameDistance()
    {
        var registry = Registry();
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        var suggestion = WireframeCatalog.SuggestType(
            registry,
            vocabulary,
            "TmOtpInput",
            scope: null,
            targetPackIds: null);

        // Finding 10: "TmOtpInput" is a registered synonym of the otp-input role and the role is
        // unambiguously TmOtpInput's since the OTP plan (TmMaskedTextBox keeps only masked-input).
        suggestion.Should().Be("TmOtpInput");
    }

    [Fact]
    public void SuggestType_RoleSynonymHonorsAppPackPriority()
    {
        var appA = Guid.NewGuid().ToString("D");
        var registry = new WireframeSchemaRegistry(
        [
            new TestSchemaSource("BuiltIn", 0, Schema(
                "TmSearchInput",
                "Inputs",
                "Search Input",
                isBuiltIn: true,
                roles: ["search-input"])),
            new ScopedSchemaSource("A", 10, appA, Schema(
                "SearchBox",
                "Custom",
                "App Search",
                roles: ["search-input"]))
        ]);
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        var suggestion = WireframeCatalog.SuggestType(
            registry,
            vocabulary,
            "TmSearchBox",
            WireframeComponentScope.ForApp(appA),
            ["tempo", $"app:{appA}"]);

        suggestion.Should().Be($"app:{appA}:SearchBox");
    }

    [Fact]
    public void ListComponents_WithScope_ReturnsBaselineAndMatchingScopedCustomsOnly()
    {
        var appA = Guid.NewGuid().ToString("D");
        var appB = Guid.NewGuid().ToString("D");
        var registry = ScopedRegistry(appA, appB);

        var root = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            scopeAppId: appA));

        var types = root.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .ToList();

        types.Should().BeEquivalentTo(["TmButton", $"app:{appA}:InvoiceCard"]);
        types.Should().NotContain("LegacyCustom");
        types.Should().NotContain($"app:{appB}:InvoiceCard");
    }

    [Fact]
    public void GetComponentSchema_WithScope_ResolvesLocalCustomType()
    {
        var appA = Guid.NewGuid().ToString("D");
        var appB = Guid.NewGuid().ToString("D");
        var registry = ScopedRegistry(appA, appB);

        var root = Parse(WireframeComponentCatalogTools.GetComponentSchemaScoped(
            registry,
            "InvoiceCard",
            scopeAppId: appA));

        root.GetProperty("success").GetBoolean().Should().BeTrue();
        var component = root.GetProperty("component");
        component.GetProperty("type").GetString().Should().Be($"app:{appA}:InvoiceCard");
        component.GetProperty("localType").GetString().Should().Be("InvoiceCard");
        component.GetProperty("scopeAppId").GetString().Should().Be(appA);
    }

    [Fact]
    public void ListComponentsScoped_WithTargetPacks_HidesUndeclaredScopedComponents()
    {
        var appA = Guid.NewGuid().ToString("D");
        var appB = Guid.NewGuid().ToString("D");
        var registry = ScopedRegistry(appA, appB);

        var tempoOnly = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            scopeAppId: appA,
            targetPackIds: ["tempo"]));
        var withApp = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            scopeAppId: appA,
            targetPackIds: ["tempo", $"app:{appA}"]));

        tempoOnly.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .Should().BeEquivalentTo(["TmButton"]);
        withApp.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .Should().BeEquivalentTo(["TmButton", $"app:{appA}:InvoiceCard"]);
    }

    private static WireframeSchemaRegistry ScopedRegistry(string appA, string appB)
        => new(
        [
            new TestSchemaSource("BuiltIn", 0, Schema("TmButton", "Buttons", "Button", isBuiltIn: true)),
            new TestSchemaSource("Legacy", 10, Schema("LegacyCustom", "Custom", "Legacy Custom")),
            new ScopedSchemaSource("A", 10, appA, Schema("InvoiceCard", "Custom", "A Invoice")),
            new ScopedSchemaSource("B", 10, appB, Schema("InvoiceCard", "Custom", "B Invoice"))
        ]);

    private static WireframeComponentSchema Schema(
        string type,
        string category,
        string displayName,
        bool isBuiltIn = false,
        IReadOnlyList<string>? roles = null)
        => new()
        {
            Type = type,
            Category = category,
            DisplayName = displayName,
            IsBuiltIn = isBuiltIn,
            Roles = roles,
            Props = []
        };

    private sealed class TestSchemaSource(string id, int priority, params WireframeComponentSchema[] schemas)
        : IWireframeSchemaSource
    {
        public string SourceId => id;
        public int Priority => priority;
        public IEnumerable<WireframeComponentSchema> GetSchemas() => schemas;
    }

    private sealed class ScopedSchemaSource(
        string id,
        int priority,
        string scopeAppId,
        params WireframeComponentSchema[] schemas)
        : IWireframeScopedSchemaSource
    {
        public string SourceId => id;
        public int Priority => priority;
        public string ScopeAppId => scopeAppId;
        public IEnumerable<WireframeComponentSchema> GetSchemas() => schemas;
    }
}
