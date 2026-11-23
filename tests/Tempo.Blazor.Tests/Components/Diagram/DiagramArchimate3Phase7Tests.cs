using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class DiagramArchimate3Phase7Tests : LocalizationTestBase
{
    public DiagramArchimate3Phase7Tests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["DiagramStencilSet_Archimate3"] = "ArchiMate 3.2",
            ["DiagramStencilPalette_Archimate3Business"] = "Business",
            ["DiagramStencilPalette_Archimate3Application"] = "Application",
            ["DiagramStencilPalette_Archimate3Technology"] = "Technology",
            ["DiagramStencilPalette_Archimate3Motivation"] = "Motivation",
            ["DiagramStencilPalette_Archimate3Strategy"] = "Strategy",
            ["DiagramStencilPalette_Archimate3Relationships"] = "Relationships",
            ["DiagramStencil_Archimate3BusinessActor"] = "Business Actor",
            ["DiagramStencil_Archimate3ApplicationComponent"] = "Application Component",
            ["DiagramStencil_Archimate3TechnologyNode"] = "Node",
            ["DiagramStencil_Archimate3Goal"] = "Goal",
            ["DiagramStencil_Archimate3Capability"] = "Capability"
        });
    }

    [Fact]
    public void Provider_Exposes_Archimate3_Set_And_Core_Palettes()
    {
        var set = new Archimate3DiagramStencilProvider().GetStencilSets().Should().ContainSingle().Subject;
        var stencils = set.Stencils.ToList();

        set.Id.Should().Be("archimate3");
        set.NameResourceKey.Should().Be("DiagramStencilSet_Archimate3");
        stencils.Select(stencil => stencil.PaletteId)
            .Should().Contain([
                "archimate3.business",
                "archimate3.application",
                "archimate3.technology",
                "archimate3.motivation",
                "archimate3.strategy",
                "archimate3.relationships"
            ]);
        stencils.All(stencil => stencil.SetId == "archimate3").Should().BeTrue();
        stencils.All(stencil => stencil.SetNameResourceKey == "DiagramStencilSet_Archimate3").Should().BeTrue();
        stencils.All(stencil => !string.IsNullOrWhiteSpace(stencil.PaletteNameResourceKey)).Should().BeTrue();
        stencils.All(stencil => !string.IsNullOrWhiteSpace(stencil.NameResourceKey)).Should().BeTrue();
        stencils.All(stencil => stencil.Origin == DiagramStencilOrigin.TempoOriginal).Should().BeTrue();
        stencils.Should().NotContain(stencil => stencil.Id.StartsWith("archimate.", StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_Search_Matches_Archimate3_BusinessActor_With_Library_Terms()
    {
        var registry = CreateRegistryWithArchimate3();

        registry.Search("ArchiMate 3.2 Business Actor")
            .Should().Contain(stencil => stencil.Id == "archimate3.business.actor");
    }

    [Fact]
    public void Archimate3_Business_Palette_Contains_Core_Elements()
    {
        var business = GetArchimate3Stencils()
            .Where(stencil => stencil.PaletteId == "archimate3.business")
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        business.Keys.Should().Contain([
            "archimate3.business.actor",
            "archimate3.business.role",
            "archimate3.business.process",
            "archimate3.business.function",
            "archimate3.business.service",
            "archimate3.business.object"
        ]);
        business["archimate3.business.actor"].Layout.ShapeSvg.Should().Contain("tm-archimate3-marker-actor");
        business["archimate3.business.service"].Layout.BackgroundShape.Should().Be("rounded");
        business.Values.All(stencil => stencil.Layout.Fill == "var(--tm-archimate-business-fill)").Should().BeTrue();
        DiagramSvgPalette.Light.Tokens["--tm-archimate-business-fill"].Should().Be("#fff2b8");
    }

    [Fact]
    public void Archimate3_Application_Palette_Contains_Core_Elements()
    {
        var application = GetArchimate3Stencils()
            .Where(stencil => stencil.PaletteId == "archimate3.application")
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        application.Keys.Should().Contain([
            "archimate3.application.component",
            "archimate3.application.interface",
            "archimate3.application.function",
            "archimate3.application.process",
            "archimate3.application.service",
            "archimate3.application.data-object"
        ]);
        application["archimate3.application.component"].Layout.ShapeSvg.Should().Contain("tm-archimate3-marker-component");
        application["archimate3.application.interface"].Layout.BackgroundShape.Should().Be("ellipse");
        application.Values.All(stencil => stencil.Layout.Fill == "var(--tm-archimate-application-fill)").Should().BeTrue();
        DiagramSvgPalette.Light.Tokens["--tm-archimate-application-fill"].Should().Be("#cfe8ff");
    }

    [Fact]
    public void Archimate3_Technology_Palette_Contains_Core_Elements()
    {
        var technology = GetArchimate3Stencils()
            .Where(stencil => stencil.PaletteId == "archimate3.technology")
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        technology.Keys.Should().Contain([
            "archimate3.technology.node",
            "archimate3.technology.device",
            "archimate3.technology.system-software",
            "archimate3.technology.interface",
            "archimate3.technology.path",
            "archimate3.technology.artifact"
        ]);
        technology["archimate3.technology.node"].Layout.ShapeSvg.Should().Contain("tm-archimate3-marker-node");
        technology["archimate3.technology.artifact"].Layout.BackgroundShape.Should().Be("document");
        technology.Values.All(stencil => stencil.Layout.Fill == "var(--tm-archimate-technology-fill)").Should().BeTrue();
        DiagramSvgPalette.Light.Tokens["--tm-archimate-technology-fill"].Should().Be("#d9f2d0");
    }

    [Fact]
    public void Archimate3_Motivation_Palette_Contains_Expected_Elements()
    {
        var motivation = GetArchimate3Stencils()
            .Where(stencil => stencil.PaletteId == "archimate3.motivation")
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        motivation.Keys.Should().Contain([
            "archimate3.motivation.stakeholder",
            "archimate3.motivation.driver",
            "archimate3.motivation.assessment",
            "archimate3.motivation.goal",
            "archimate3.motivation.outcome",
            "archimate3.motivation.principle",
            "archimate3.motivation.requirement",
            "archimate3.motivation.constraint"
        ]);
        motivation["archimate3.motivation.goal"].Layout.BackgroundShape.Should().Be("rounded");
        motivation["archimate3.motivation.requirement"].Layout.ShapeSvg.Should().Contain("tm-archimate3-marker-requirement");
        motivation.Values.All(stencil => stencil.Layout.Fill == "var(--tm-archimate-motivation-fill)").Should().BeTrue();
        DiagramSvgPalette.Light.Tokens["--tm-archimate-motivation-fill"].Should().Be("#eadcff");
    }

    [Fact]
    public void Archimate3_Strategy_Palette_Contains_Expected_Elements()
    {
        var strategy = GetArchimate3Stencils()
            .Where(stencil => stencil.PaletteId == "archimate3.strategy")
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        strategy.Keys.Should().Contain([
            "archimate3.strategy.resource",
            "archimate3.strategy.capability",
            "archimate3.strategy.value-stream"
        ]);
        strategy["archimate3.strategy.resource"].Layout.ShapeSvg.Should().Contain("tm-archimate3-marker-resource");
        strategy["archimate3.strategy.value-stream"].Layout.BackgroundShape.Should().Be("hexagon");
        strategy.Values.All(stencil => stencil.Layout.Fill == "var(--tm-archimate-strategy-fill)").Should().BeTrue();
        DiagramSvgPalette.Light.Tokens["--tm-archimate-strategy-fill"].Should().Be("#ffe1c2");
    }

    [Fact]
    public void Archimate3_Relationship_Edge_Presets_Create_Valid_Edges()
    {
        var edges = GetArchimate3Stencils()
            .Where(stencil => stencil.Kind == DiagramStencilKind.Edge)
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        edges.Keys.Should().Contain([
            "archimate3.relationship.association",
            "archimate3.relationship.triggering",
            "archimate3.relationship.flow",
            "archimate3.relationship.access",
            "archimate3.relationship.serving",
            "archimate3.relationship.realization",
            "archimate3.relationship.assignment",
            "archimate3.relationship.aggregation",
            "archimate3.relationship.composition",
            "archimate3.relationship.specialization",
            "archimate3.relationship.influence"
        ]);

        var triggering = DiagramEdgeStencilFactory.CreateEdge(edges["archimate3.relationship.triggering"], "source", null, "target", null);
        triggering.IsValid().Should().BeTrue();
        triggering.ConnectorType.Should().Be("archimate-triggering");
        triggering.EndArrow.Should().Be("block");
        triggering.EndArrowFill.Should().BeTrue();

        var realization = DiagramEdgeStencilFactory.CreateEdge(edges["archimate3.relationship.realization"], "source", null, "target", null);
        realization.ConnectorType.Should().Be("archimate-realization");
        realization.Style.StrokeDashPattern.Should().Be("dashed");
        realization.EndArrow.Should().Be("open");
        realization.EndArrowFill.Should().BeFalse();

        var composition = DiagramEdgeStencilFactory.CreateEdge(edges["archimate3.relationship.composition"], "source", null, "target", null);
        composition.ConnectorType.Should().Be("archimate-composition");
        composition.StartArrow.Should().Be("diamond");
        composition.StartArrowFill.Should().BeTrue();
    }

    [Fact]
    public void AddTempoBlazorDiagramEditor_Registers_Archimate3_Provider()
    {
        var services = new ServiceCollection();

        services.AddTempoBlazorDiagramEditor();

        var provider = services.BuildServiceProvider();
        provider.GetServices<IDiagramStencilProvider>()
            .Should().Contain(provider => provider is Archimate3DiagramStencilProvider);
        provider.GetRequiredService<DiagramStencilRegistry>()
            .GetStencil("archimate3.business.actor")
            .Should().NotBeNull();
    }

    private static List<DiagramStencil> GetArchimate3Stencils()
        => new Archimate3DiagramStencilProvider()
            .GetStencilSets()
            .SelectMany(set => set.Stencils)
            .ToList();

    private static DiagramStencilRegistry CreateRegistryWithArchimate3()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());
        return registry;
    }
}
