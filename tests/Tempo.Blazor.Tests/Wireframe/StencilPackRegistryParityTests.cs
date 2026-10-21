using FluentAssertions.Execution;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;
using Xunit;

namespace Tempo.Blazor.Tests.Wireframe;

public class StencilPackRegistryParityTests
{
    private static readonly string[] GoldenDeclarativeTypes =
    [
        "TmStepper",
        "TmTimeline",
        "TmTreeView",
        "TmTreeList",
        "TmActivityLog",
        "TmActivityTimeline",
        "TmImageGallery",
        "TmAIPrompt",
        "TmShareLinkPanel",
        "TmSankeyChart",
        "TmSparkline",
        "TmScheduler"
    ];

    public static TheoryData<string> GoldenDeclarativeTypeData
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in GoldenDeclarativeTypes)
                data.Add(type);
            return data;
        }
    }

    public static TheoryData<string> BuiltInSchemaTypeData
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var type in new BuiltInComponentSchemas().GetSchemas().Select(schema => schema.Type))
                data.Add(type);
            return data;
        }
    }

    [Fact]
    public void Registry_CoversEveryBuiltInSchema_ThroughPack()
    {
        var registry = Registry();
        var canonicalTypes = new BuiltInComponentSchemas()
            .GetSchemas()
            .Select(schema => schema.Type)
            .ToArray();

        using var _ = new AssertionScope();
        foreach (var type in canonicalTypes)
            registry.GetDef(type).Should().NotBeNull($"{type} must resolve through the Tempo stencil pack");
    }

    [Fact]
    public void BuiltInPack_EveryComponentDeclaresAtLeastOneRole()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());

        pack.Components
            .Where(component => component.Roles is null || component.Roles.Count == 0)
            .Select(component => component.Type)
            .Should()
            .BeEmpty("every built-in component must declare at least one UI role");
    }

    [Fact]
    public void BuiltInPack_AllReferencedRolesExistInBaselineVocabulary()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());
        var vocabulary = new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]);

        pack.Components
            .SelectMany(component => component.Roles ?? [])
            .Distinct(StringComparer.Ordinal)
            .Where(role => vocabulary.Find(role) is null)
            .Should()
            .BeEmpty("built-in pack roles must point to the baseline UI role vocabulary");
    }

    [Fact]
    public void Registry_PropagatesBuiltInPackRolesToDefinitions()
    {
        var registry = Registry();

        var def = registry.GetDef("TmSearchInput");

        def.Should().NotBeNull();
        def!.Roles.Should().Contain("search-input");
    }

    [Fact]
    public void Registry_PropagatesBuiltInPackContainerFlagToDefinitions()
    {
        var registry = Registry();

        var def = registry.GetDef("TmCard");

        def.Should().NotBeNull();
        def!.IsContainer.Should().BeTrue();
    }

    [Fact]
    public void BuiltInSchemas_RolesMatchBuiltInPackRoles()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());
        var schemas = new BuiltInComponentSchemas()
            .GetSchemas()
            .ToDictionary(schema => schema.Type, StringComparer.Ordinal);

        foreach (var component in pack.Components)
        {
            schemas.TryGetValue(component.Type, out var schema)
                .Should()
                .BeTrue($"built-in schema '{component.Type}' should exist");
            schema!.Roles.Should().Equal(component.Roles ?? []);
        }
    }

    [Fact]
    public void BuiltInSchemas_ContainerFlagsMatchBuiltInPack()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());
        var schemas = new BuiltInComponentSchemas()
            .GetSchemas()
            .ToDictionary(schema => schema.Type, StringComparer.Ordinal);

        foreach (var component in pack.Components)
        {
            schemas.TryGetValue(component.Type, out var schema)
                .Should()
                .BeTrue($"built-in schema '{component.Type}' should exist");
            schema!.IsContainer.Should().Be(component.IsContainer);
        }
    }

    [Fact]
    public void SankeySchema_ExposesHoverHighlightBehavior()
    {
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmSankeyChart");

        schema.Props.Should().ContainSingle(prop =>
            prop.Name == "highlightOnHover" &&
            prop.Type == PropType.Bool &&
            Equals(prop.Default, true) &&
            prop.Category == "Behavior");
    }

    [Fact]
    public void RadioGroupSchema_ExposesSegmentedAppearance()
    {
        // L12 (rule 13): the segmented appearance is a real rendering, so the wireframe schema must
        // offer it. The built-in pack takes its props from this schema, so the two cannot drift.
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmRadioGroup");

        schema.Props.Should().ContainSingle(prop =>
            prop.Name == "appearance" &&
            prop.Type == PropType.Enum &&
            Equals(prop.Default, "Radio") &&
            prop.Options!.SequenceEqual(new[] { "Radio", "Segmented" }) &&
            prop.Category == "Appearance");
    }

    [Fact]
    public void RadioGroupPack_RendersTheSegmentedAppearanceAsATray()
    {
        // The pack takes its props from the schema, so the parity that matters is the render: it must
        // branch on the same appearance prop the schema declares, drawing the tray only when segmented.
        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmRadioGroup\""));

        entry.Should().Contain("appearance == \\\"Segmented\\\"");
        entry.Should().Contain("appearance != \\\"Segmented\\\"");
    }

    [Fact]
    public void DataTableStencil_ExposesRedesignPropsAndBranchesOnThem()
    {
        // Fáze 12: the wireframe stencil must offer the redesign opt-ins and actually draw them, so a
        // designer can preview cards, a mobile layout and the filter summary before writing markup.
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmDataTable");

        schema.Props.Should().Contain(prop => prop.Name == "appearance" && prop.Type == PropType.Enum
            && Equals(prop.Default, "plain") && prop.Options!.SequenceEqual(new[] { "plain", "card" }));
        schema.Props.Should().Contain(prop => prop.Name == "mobileLayout" && prop.Type == PropType.Enum
            && Equals(prop.Default, "none") && prop.Options!.SequenceEqual(new[] { "none", "scroll", "cards" }));
        schema.Props.Should().Contain(prop => prop.Name == "showFilterSummary" && prop.Type == PropType.Bool
            && Equals(prop.Default, false));
        schema.Props.Should().Contain(prop => prop.Name == "columnMenu" && prop.Type == PropType.Bool);
        schema.Props.Should().Contain(prop => prop.Name == "exportSelected" && prop.Type == PropType.Bool);

        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmDataTable\""));

        entry.Should().Contain("appearance == \\\"card\\\"");
        entry.Should().Contain("mobileLayout == \\\"cards\\\"");
        entry.Should().Contain("mobileLayout == \\\"scroll\\\"");
        entry.Should().Contain("showFilterSummary");
    }

    [Fact]
    public void MultiViewListStencil_ExposesRedesignPropsAndBranchesOnThem()
    {
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmMultiViewList");

        schema.Props.Should().Contain(prop => prop.Name == "view" && prop.Type == PropType.Enum
            && Equals(prop.Default, "list") && prop.Options!.SequenceEqual(new[] { "table", "cards", "list" }));
        schema.Props.Should().Contain(prop => prop.Name == "responsive" && prop.Type == PropType.Bool
            && Equals(prop.Default, false));
        schema.Props.Should().Contain(prop => prop.Name == "showItemCount" && prop.Type == PropType.Bool);
        schema.Props.Should().Contain(prop => prop.Name == "viewSwitcherLabels" && prop.Type == PropType.Enum);

        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmMultiViewList\""));

        entry.Should().Contain("view == \\\"cards\\\"");
        entry.Should().Contain("view == \\\"table\\\"");
        entry.Should().Contain("responsive");
    }

    [Fact]
    public void ViewManagerStencil_ExposesTheOpenPanelVariant()
    {
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmViewManager");

        schema.Props.Should().Contain(prop => prop.Name == "open" && prop.Type == PropType.Bool
            && Equals(prop.Default, false));

        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmViewManager\""));

        entry.Should().Contain("open");
    }

    [Fact]
    public void FilterBuilderStencil_ExposesPresentationVariants()
    {
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmFilterBuilder");

        schema.Props.Should().Contain(prop => prop.Name == "presentation" && prop.Type == PropType.Enum
            && Equals(prop.Default, "inline") && prop.Options!.SequenceEqual(new[] { "inline", "popover", "auto" }));

        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmFilterBuilder\""));

        entry.Should().Contain("presentation == \\\"popover\\\"");
        entry.Should().Contain("presentation == \\\"auto\\\"");
    }

    [Fact]
    public void ToolbarButtonSchema_ExposesPressedToggle()
    {
        // F12/C2: the toolbar button has a pressed (toggle) state, so the wireframe schema must offer
        // it. The built-in pack takes its props from this schema, so the two cannot drift.
        var schema = new BuiltInComponentSchemas()
            .GetSchemas()
            .Single(candidate => candidate.Type == "TmToolbarButton");

        schema.Props.Should().ContainSingle(prop =>
            prop.Name == "pressed" &&
            prop.Type == PropType.Bool &&
            Equals(prop.Default, false) &&
            prop.Category == "Behavior");
    }

    [Fact]
    public void ToolbarButtonPack_RendersThePressedStateAsAHighlight()
    {
        // Parity in the render: a pressed toolbar button draws highlighted (the subtle primary fill
        // with the primary border), unpressed keeps the plain outline.
        var entry = BuiltInStencilPackProvider.ReadPackJson()
            .Split('\n')
            .Single(line => line.Contains("\"type\": \"TmToolbarButton\""));

        entry.Should().Contain("pressed ? token(\\\"color.primary.subtle\\\"");
        entry.Should().Contain("pressed ? token(\\\"color.primary.border\\\"");
    }

    [Fact]
    public void BuiltInPack_MarksKnownLayoutContainers()
    {
        var pack = StencilPackSerializer.Deserialize(BuiltInStencilPackProvider.ReadPackJson());

        pack.Components
            .Where(component => component.IsContainer)
            .Select(component => component.Type)
            .Should().Contain(["TmCard", "TmSection", "TmModal", "TmDialog", "TmDrawer", "TmFormSection"]);
    }


    [Fact]
    public void DashboardPackEntry_MapsToSameNamedNativeRenderer()
    {
        // 2ee91e41: like the diagram editor (U18/X11), the dashboard stencil honors layoutMode,
        // which needs the native renderer — the pack entry must delegate to it by name.
        var registry = Registry();
        var def = registry.GetDef("TmDashboard");

        def.Should().NotBeNull();
        def!.PackId.Should().Be("tempo");
        def.NativeType.Should().Be("TmDashboard");
    }

    [Theory]
    [MemberData(nameof(GoldenDeclarativeTypeData))]
    public async Task DeclarativeGoldenTypes_RenderSafeSvg(string type)
    {
        var registry = Registry();
        var def = registry.GetDef(type) ?? throw new InvalidOperationException($"Missing definition for {type}.");
        var page = PageWith(def);

        var svg = await Renderer(registry).RenderPageAsync(page);

        using var _ = new AssertionScope(type);
        def.PackId.Should().Be("tempo");
        def.NativeType.Should().BeNull();
        svg.Should().StartWith("<svg");
        svg.Should().Contain("<rect");
        svg.Should().Contain("<text");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("javascript:");
        svg.Should().NotContainEquivalentOf("onerror=");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Theory]
    [MemberData(nameof(BuiltInSchemaTypeData))]
    public async Task EveryBuiltInSchemaType_RendersSafeSvgThroughPack(string type)
    {
        var registry = Registry();
        var def = registry.GetDef(type) ?? throw new InvalidOperationException($"Missing definition for {type}.");
        var element = ElementFor(def);

        var act = () => def.RenderSvg(element, new RenderTreeBuilder());
        var svg = await Renderer(registry).RenderPageAsync(PageWith(element));

        using var _ = new AssertionScope(type);
        act.Should().NotThrow($"{type} should render through the Tempo stencil pack");
        svg.Should().StartWith("<svg");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("javascript:");
        svg.Should().NotContainEquivalentOf("onerror=");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    private static WireframePage PageWith(WireframeComponentDef def)
        => PageWith(ElementFor(def));

    private static WireframePage PageWith(WireframeElement element)
    {
        var page = new WireframePage
        {
            Id = "phase12",
            Name = "Phase 12",
            Width = Math.Max(700, element.W + 80),
            Height = Math.Max(420, element.H + 80)
        };
        page.Elements.Add(element);
        return page;
    }

    private static WireframeElement ElementFor(WireframeComponentDef def)
        => new()
        {
            Id = "sut",
            Type = def.Type,
            X = 40,
            Y = 40,
            W = def.DefaultWidth,
            H = def.DefaultHeight
        };

    private static WireframeSvgRenderer Renderer(WireframeComponentRegistry registry)
        => new(registry, Services());

    private static WireframeComponentRegistry Registry()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    private static IServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
