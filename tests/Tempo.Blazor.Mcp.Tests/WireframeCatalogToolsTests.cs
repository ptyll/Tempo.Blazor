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
    public void ListComponentsScoped_WithScope_ListsAppScopedFirst()
    {
        var appId = Guid.NewGuid().ToString("D");
        var registry = LargeScopedRegistry(appId);

        var root = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            scopeAppId: appId));

        var types = root.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .ToList();

        types.Should().HaveCount(200);
        types.Should().Contain($"app:{appId}:AppWidget000");
        types.Should().Contain($"app:{appId}:AppWidget001");
        types[0].Should().Be($"app:{appId}:AppWidget000");
        types[1].Should().Be($"app:{appId}:AppWidget001");
    }

    [Fact]
    public void ListComponentsScoped_WithTargetPacks_ListsAppScopedFirst()
    {
        // Subtask 3 orders app-scoped types first when scopeAppId OR targetPackIds is given.
        // WireframeSchemaRegistry.GetAll hides app-scoped schemas when scope is null, so a
        // targetPackIds-only call (scopeAppId: null) never surfaces AppWidget* types. The
        // targetPackIds half of the catalog condition therefore only filters together with
        // scopeAppId; this test covers that combined path (tempo + app:{id} packs).
        var appId = Guid.NewGuid().ToString("D");
        var registry = LargeScopedRegistry(appId);

        var root = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            scopeAppId: appId,
            targetPackIds: ["tempo", $"app:{appId}"]));

        var types = root.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .ToList();

        types[0].Should().Be($"app:{appId}:AppWidget000");
        types[1].Should().Be($"app:{appId}:AppWidget001");
        root.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        root.GetProperty("nextSkip").GetInt32().Should().Be(200);
    }

    [Fact]
    public void ListComponents_WithoutScope_KeepsRegistryOrder()
    {
        var registry = Registry();
        var expected = registry.GetAll().Take(10).Select(s => s.Type).ToList();

        var root = Parse(WireframeComponentCatalogTools.ListComponents(registry, compact: true, take: 10));
        var types = root.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("type").GetString())
            .ToList();

        types.Should().Equal(expected);
    }

    [Fact]
    public void ListComponents_Paging_ReportsHasMoreAndNextSkip()
    {
        var appId = Guid.NewGuid().ToString("D");
        var registry = LargeScopedRegistry(appId);

        var first = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            scopeAppId: appId));
        first.GetProperty("totalCount").GetInt32().Should().Be(252);
        first.GetProperty("returnedCount").GetInt32().Should().Be(200);
        first.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        first.GetProperty("nextSkip").GetInt32().Should().Be(200);

        var last = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            skip: 200,
            scopeAppId: appId));
        last.GetProperty("returnedCount").GetInt32().Should().Be(52);
        last.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        AssertNextSkipAbsentOrNull(last);
    }

    [Fact]
    public void ListComponents_SkipPastEnd_ReturnsEmptyAndNoMore()
    {
        var appId = Guid.NewGuid().ToString("D");
        var registry = LargeScopedRegistry(appId);

        var root = Parse(WireframeComponentCatalogTools.ListComponentsScoped(
            registry,
            compact: true,
            skip: 500,
            scopeAppId: appId));

        root.GetProperty("totalCount").GetInt32().Should().Be(252);
        root.GetProperty("returnedCount").GetInt32().Should().Be(0);
        root.GetProperty("items").GetArrayLength().Should().Be(0);
        root.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        AssertNextSkipAbsentOrNull(root);
    }

    private static void AssertNextSkipAbsentOrNull(JsonElement root)
    {
        if (root.TryGetProperty("nextSkip", out var nextSkip))
            nextSkip.ValueKind.Should().Be(JsonValueKind.Null, "nextSkip should be null or omitted on the last page");
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
        // "verification code" is an `otp-input` synonym, so the ROLE path answers with the role's home
        // component instead of falling through to nearest-name matching (which would have to guess across
        // every Tm* name). Since phase 16 of plan 7ba95ed6 that home is the dedicated TmOtpInput — a mask
        // is a different control, so `masked-input` no longer answers for OTP.
        var registry = Registry();
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        var suggestion = WireframeCatalog.SuggestType(
            registry,
            vocabulary,
            "verification code",
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

    private static WireframeSchemaRegistry LargeScopedRegistry(string appId, int builtInCount = 250, int appCount = 2)
    {
        var builtIn = Enumerable.Range(0, builtInCount)
            .Select(i => Schema($"TmBuiltIn{i:D3}", "ABuiltIn", $"Built In {i:D3}", isBuiltIn: true))
            .ToArray();
        var app = Enumerable.Range(0, appCount)
            .Select(i => Schema($"AppWidget{i:D3}", "ZApp", $"App Widget {i:D3}"))
            .ToArray();
        return new WireframeSchemaRegistry(
        [
            new TestSchemaSource("BuiltIn", 0, builtIn),
            new ScopedSchemaSource("App", 10, appId, app)
        ]);
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
