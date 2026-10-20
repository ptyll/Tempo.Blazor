using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Services;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class DiagramUml25Phase5Tests : LocalizationTestBase
{
    public DiagramUml25Phase5Tests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDiagramProperties_Toggle"] = "Toggle properties",
            ["TmDiagramProperties_Title"] = "Properties",
            ["TmDiagramProperties_Link"] = "Link",
            ["TmDiagramProperties_LinkPlaceholder"] = "https://...",
            ["TmDiagramProperties_Style"] = "Style",
            ["TmDiagramProperties_ReplaceShape"] = "Replace Shape",
            ["TmDiagramProperties_ReplaceShapeSearch"] = "Search shapes",
            ["DiagramStencilSet_Uml25"] = "UML 2.5",
            ["DiagramStencilPalette_Uml25Class"] = "Class",
            ["DiagramStencilPalette_Uml25UseCase"] = "Use Case",
            ["DiagramStencilPalette_Uml25Activity"] = "Activity",
            ["DiagramStencilPalette_Uml25Sequence"] = "Sequence",
            ["DiagramStencilPalette_Uml25Deployment"] = "Deployment",
            ["DiagramStencilPalette_Uml25Relationships"] = "Relationships",
            ["DiagramStencil_Uml25Class"] = "Class",
            ["DiagramStencil_Uml25Dependency"] = "Dependency"
        });
    }

    [Fact]
    public void Provider_Exposes_Uml25_Set_And_Class_Palette()
    {
        var stencils = GetUml25Stencils();

        stencils.Should().Contain(stencil => stencil.Id == "uml25.class");
        stencils.Where(stencil => stencil.PaletteId == "uml25.class")
            .Should().Contain(stencil => stencil.Id == "uml25.abstract-class")
            .And.Contain(stencil => stencil.Id == "uml25.interface")
            .And.Contain(stencil => stencil.Id == "uml25.package");
        stencils.All(stencil => stencil.SetId == "uml25").Should().BeTrue();
        stencils.All(stencil => stencil.SetNameResourceKey == "DiagramStencilSet_Uml25").Should().BeTrue();
        stencils.All(stencil => !string.IsNullOrWhiteSpace(stencil.PaletteNameResourceKey)).Should().BeTrue();
        stencils.All(stencil => !string.IsNullOrWhiteSpace(stencil.NameResourceKey)).Should().BeTrue();
        stencils.All(stencil => stencil.Origin == DiagramStencilOrigin.TempoOriginal).Should().BeTrue();
    }

    [Fact]
    public void Registry_Search_Matches_Uml25_Class_With_Library_Terms()
    {
        var registry = CreateRegistryWithUml25();

        registry.Search("UML 2.5 Class")
            .Should().Contain(stencil => stencil.Id == "uml25.class");
    }

    [Fact]
    public void Uml25_Class_Has_Name_Attributes_And_Operations_Compartments()
    {
        var stencil = GetUml25Stencils().Single(stencil => stencil.Id == "uml25.class");

        stencil.Kind.Should().Be(DiagramStencilKind.Node);
        stencil.Layout.Sections.Where(section => section.Type != "divider")
            .Select(section => section.DataKey)
            .Should().Equal("name", "attributes", "operations");
        stencil.Layout.Sections.Count(section => section.Type == "divider").Should().Be(2);
        stencil.DefaultData["name"].Should().Be("ClassName");
        stencil.DefaultData["attributes"].Should().BeAssignableTo<IEnumerable<string>>();
        stencil.DefaultData["operations"].Should().BeAssignableTo<IEnumerable<string>>();
        stencil.Ports.Should().HaveCountGreaterThanOrEqualTo(4);
        stencil.ConnectionPoints.Should().NotBeEmpty();
    }

    [Fact]
    public void Uml25_Toolbox_Palettes_Expose_UseCase_Activity_Sequence_And_Deployment()
    {
        var stencils = GetUml25Stencils();

        stencils.Where(stencil => stencil.PaletteId == "uml25.usecase")
            .Select(stencil => stencil.Id)
            .Should().Contain(["uml25.actor", "uml25.use-case", "uml25.system-boundary", "uml25.note"]);
        stencils.Where(stencil => stencil.PaletteId == "uml25.activity")
            .Select(stencil => stencil.Id)
            .Should().Contain(["uml25.activity-initial", "uml25.activity-final", "uml25.activity-action", "uml25.activity-decision", "uml25.activity-fork-join", "uml25.activity-object-node"]);
        stencils.Where(stencil => stencil.PaletteId == "uml25.sequence")
            .Select(stencil => stencil.Id)
            .Should().Contain(["uml25.sequence-lifeline", "uml25.sequence-activation", "uml25.sequence-combined-fragment", "uml25.sequence-message"]);
        stencils.Where(stencil => stencil.PaletteId == "uml25.deployment")
            .Select(stencil => stencil.Id)
            .Should().Contain(["uml25.component", "uml25.deployment-node", "uml25.artifact", "uml25.deployment-spec"]);
    }

    [Fact]
    public void Uml25_Relationship_Edge_Presets_Create_Valid_Edges()
    {
        var stencils = GetUml25Stencils()
            .Where(stencil => stencil.Kind == DiagramStencilKind.Edge)
            .ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);

        stencils.Keys.Should().Contain([
            "uml25.association",
            "uml25.directed-association",
            "uml25.dependency",
            "uml25.generalization",
            "uml25.realization",
            "uml25.aggregation",
            "uml25.composition"
        ]);

        var dependency = DiagramEdgeStencilFactory.CreateEdge(stencils["uml25.dependency"], "source", null, "target", null);
        dependency.IsValid().Should().BeTrue();
        dependency.ConnectorType.Should().Be("dependency");
        dependency.Routing.Should().Be("orthogonal");
        dependency.EndArrow.Should().Be("open");
        dependency.EndArrowFill.Should().BeFalse();
        dependency.Style.StrokeDashPattern.Should().Be("dashed");

        var generalization = DiagramEdgeStencilFactory.CreateEdge(stencils["uml25.generalization"], "source", null, "target", null);
        generalization.EndArrow.Should().Be("block");
        generalization.EndArrowFill.Should().BeFalse();

        var composition = DiagramEdgeStencilFactory.CreateEdge(stencils["uml25.composition"], "source", null, "target", null);
        composition.StartArrow.Should().Be("diamond");
        composition.StartArrowFill.Should().BeTrue();
    }

    [Fact]
    public void Uml25_Class_Inline_Name_Edit_Emits_Node_Data_Update()
    {
        var registry = CreateRegistryWithUml25();
        Services.AddSingleton(registry);
        var node = CreateClassNode(registry);
        (string DataKey, object Value)? edit = null;

        var cut = Render<TmDiagramStencilShape>(parameters => parameters
            .Add(p => p.Node, node)
            .Add(p => p.OnSectionEdit, EventCallback.Factory.Create<(string DataKey, object Value)>(this, value => edit = value)));

        cut.FindAll(".tm-diagram-node__section")[0].DoubleClick();
        var input = cut.Find(".tm-diagram-node__inline-input");
        input.Input("Customer");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        edit.Should().NotBeNull();
        edit!.Value.DataKey.Should().Be("name");
        edit.Value.Value.Should().Be("Customer");
    }

    [Fact]
    public void Uml25_Class_Properties_Edit_Attributes_List_Uses_CommandStack()
    {
        var registry = CreateRegistryWithUml25();
        Services.AddSingleton(registry);
        var doc = new DiagramDocument();
        var node = CreateClassNode(registry);
        doc.Nodes.Add(node);
        var stack = new DiagramCommandStack();

        var cut = Render<CascadingValue<DiagramCommandStack>>(parameters => parameters
            .Add(p => p.Value, stack)
            .AddChildContent<TmDiagramPropertiesPanel>(child => child
                .Add(p => p.Document, doc)
                .Add(p => p.SelectedIds, [node.Id])
                .Add(p => p.ReadOnly, false)));

        var attributesEditor = cut.FindAll(".tm-diagram-properties__field")
            .Single(field => field.QuerySelector("label")?.TextContent.Trim() == "Attributes")
            .QuerySelector("textarea")!;
        attributesEditor.Change("- id: Guid\n- email: string");

        node.Data["attributes"].Should().BeAssignableTo<IEnumerable<string>>()
            .Which.Should().Contain(["- id: Guid", "- email: string"]);
        stack.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Uml25_Class_Properties_Edit_Operations_List_Uses_CommandStack()
    {
        var registry = CreateRegistryWithUml25();
        Services.AddSingleton(registry);
        var doc = new DiagramDocument();
        var node = CreateClassNode(registry);
        doc.Nodes.Add(node);
        var stack = new DiagramCommandStack();

        var cut = Render<CascadingValue<DiagramCommandStack>>(parameters => parameters
            .Add(p => p.Value, stack)
            .AddChildContent<TmDiagramPropertiesPanel>(child => child
                .Add(p => p.Document, doc)
                .Add(p => p.SelectedIds, [node.Id])
                .Add(p => p.ReadOnly, false)));

        var operationsEditor = cut.FindAll(".tm-diagram-properties__field")
            .Single(field => field.QuerySelector("label")?.TextContent.Trim() == "Operations")
            .QuerySelector("textarea")!;
        operationsEditor.Change("+ Save(): Task\n+ Archive(): void");

        node.Data["operations"].Should().BeAssignableTo<IEnumerable<string>>()
            .Which.Should().Contain(["+ Save(): Task", "+ Archive(): void"]);
        stack.CanUndo.Should().BeTrue();
    }

    [Fact]
    public void Class_ContentStyle_UsesSafeCenter()
    {
        // U7: content taller than the node must clip at the BOTTOM only — `safe center`
        // falls back to start on overflow so the class-name header stays visible.
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        Services.AddSingleton(registry);

        var node = new DiagramNode { StencilId = "uml.class", W = 180, H = 100 };
        node.Data["name"] = "Customer";
        node.Data["attributes"] = new[] { "- id: Guid", "- name: string", "- email: string" };
        node.Data["methods"] = new[] { "+ Save(): void", "+ Load(): void" };

        var cut = Render<TmDiagramStencilShape>(parameters => parameters
            .Add(p => p.Node, node));

        cut.Find(".tm-diagram-node__content").GetAttribute("style")
            .Should().Contain("justify-content:safe center");
    }

    [Fact]
    public void AddTempoBlazorDiagramEditor_Registers_Uml25_Provider()
    {
        var services = new ServiceCollection();

        services.AddTempoBlazorDiagramEditor();

        var provider = services.BuildServiceProvider();
        provider.GetServices<IDiagramStencilProvider>()
            .Should().Contain(provider => provider is Uml25DiagramStencilProvider);
        provider.GetRequiredService<DiagramStencilRegistry>()
            .GetStencil("uml25.class")
            .Should().NotBeNull();
    }

    private static List<DiagramStencil> GetUml25Stencils()
        => new Uml25DiagramStencilProvider()
            .GetStencilSets()
            .SelectMany(set => set.Stencils)
            .ToList();

    private static DiagramStencilRegistry CreateRegistryWithUml25()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new Uml25DiagramStencilProvider());
        return registry;
    }

    private static DiagramNode CreateClassNode(DiagramStencilRegistry registry)
    {
        var stencil = registry.GetStencil("uml25.class")!;
        var node = new DiagramNode { StencilId = stencil.Id, W = stencil.DefaultWidth, H = stencil.DefaultHeight };
        foreach (var item in stencil.DefaultData)
            node.Data[item.Key] = item.Value;
        return node;
    }
}
