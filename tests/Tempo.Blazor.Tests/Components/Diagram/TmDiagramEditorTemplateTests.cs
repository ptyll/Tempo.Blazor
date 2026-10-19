using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramEditorTemplateTests : LocalizationTestBase
{
    public TmDiagramEditorTemplateTests()
    {
        var stencilRegistry = Services.GetRequiredService<DiagramStencilRegistry>();
        stencilRegistry.RegisterProvider(new BuiltInDiagramStencilProvider());

        var templateRegistry = Services.GetRequiredService<DiagramTemplateRegistry>();
        templateRegistry.RegisterTemplate(new DiagramTemplate
        {
            Id = "tpl-test",
            Name = "Test Template",
            Category = "Test",
            DocumentJson = BuildTemplateJson()
        });
    }

    [Fact]
    public void Templates_Button_Opens_Gallery()
    {
        var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        var templatesButton = cut.FindAll("button").First(b => b.TextContent.Contains("Templates"));
        templatesButton.Click();
        cut.Render();

        cut.Find(".tm-diagram-template-gallery").Should().NotBeNull();
    }

    [Fact]
    public async Task Selecting_Template_Replaces_Document_With_New_Ids()
    {
        var originalDoc = new DiagramDocument();
        originalDoc.EnsurePages();
        originalDoc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 50,
            Y = 50,
            W = 100,
            H = 50
        });

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, originalDoc)
            .Add(e => e.ReadOnly, false));

        // Open gallery
        var templatesButton = cut.FindAll("button").First(b => b.TextContent.Contains("Templates"));
        templatesButton.Click();
        cut.Render();

        // Select the template card
        var card = cut.Find(".tm-diagram-template-card");
        card.Click();
        cut.Render();

        // Click Create button
        var createButton = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => createButton.Click());
        cut.Render();

        // The editor should now contain the templated nodes
        var currentDoc = cut.Instance.Document;
        currentDoc.Should().NotBeNull();
        currentDoc!.Title.Should().Be("Test Template");
        currentDoc.Pages[0].Nodes.Count.Should().Be(2);
        currentDoc.Pages[0].Edges.Count.Should().Be(1);

        // All IDs must be different from the template source
        currentDoc.Id.Should().NotBe(originalDoc.Id);
        currentDoc.Pages[0].Nodes[0].Id.Should().NotBe(originalDoc.Pages[0].Nodes[0].Id);
    }

    [Fact]
    public async Task Selected_Template_Edge_References_Are_Remapped()
    {
        var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        // Open gallery and select template
        var templatesButton = cut.FindAll("button").First(b => b.TextContent.Contains("Templates"));
        templatesButton.Click();
        cut.Render();

        cut.Find(".tm-diagram-template-card").Click();
        cut.Render();

        var createButton = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => createButton.Click());
        cut.Render();

        var doc = cut.Instance.Document!;
        var edge = doc.Pages[0].Edges[0];
        var nodeIds = doc.Pages[0].Nodes.Select(n => n.Id).ToHashSet();
        var portIds = doc.Pages[0].Nodes.SelectMany(n => n.Ports).Select(p => p.Id).ToHashSet();

        nodeIds.Should().Contain(edge.SourceNodeId);
        nodeIds.Should().Contain(edge.TargetNodeId);
        portIds.Should().Contain(edge.SourcePortId);
        portIds.Should().Contain(edge.TargetPortId);
    }

    [Fact]
    public async Task UmlTemplate_NodesAreFittedToContent()
    {
        // U7/X9: a template whose UML node is shorter than its content must arrive fitted
        // (rounded up to the 10-unit grid) — auto-height runs at template creation, not only
        // during inline editing.
        var templateRegistry = Services.GetRequiredService<DiagramTemplateRegistry>();
        templateRegistry.RegisterTemplate(new DiagramTemplate
        {
            Id = "tpl-uml",
            Name = "UML Fit Template",
            Category = "Test",
            DocumentJson = BuildUmlTemplateJson()
        });

        var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        var templatesButton = cut.FindAll("button").First(b => b.TextContent.Contains("Templates"));
        templatesButton.Click();
        cut.Render();

        var card = cut.FindAll(".tm-diagram-template-card")
            .Single(c => c.TextContent.Contains("UML Fit Template"));
        card.Click();
        cut.Render();

        var createButton = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => createButton.Click());
        cut.Render();

        var umlNode = cut.Instance.Document!.Pages[0].Nodes.Single(n => n.StencilId == "uml.class");
        umlNode.H.Should().BeGreaterThanOrEqualTo(160);
    }

    private static string BuildUmlTemplateJson()
    {
        var doc = new DiagramDocument { Title = "UML Fit Template" };
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode
        {
            StencilId = "uml.class",
            X = 100,
            Y = 100,
            W = 180,
            H = 100
        });
        doc.Pages[0].Nodes[0].Data["name"] = "Customer";
        doc.Pages[0].Nodes[0].Data["attributes"] = new[] { "- id: Guid", "- name: string", "- email: string" };
        doc.Pages[0].Nodes[0].Data["methods"] = new[] { "+ Save(): void", "+ Load(): void" };

        return Tempo.Blazor.Components.Diagram.Serialization.DiagramSerializer.Serialize(doc);
    }

    private static string BuildTemplateJson()
    {
        var doc = new DiagramDocument { Title = "Test Template" };
        doc.EnsurePages();
        var page = doc.Pages[0];

        var node1 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 100,
            Y = 100,
            W = 120,
            H = 60
        };
        node1.Ports.Add(new DiagramPort { Name = "right", Side = PortSide.Right, Offset = 0.5 });

        var node2 = new DiagramNode
        {
            StencilId = "general.rectangle",
            X = 300,
            Y = 100,
            W = 120,
            H = 60
        };
        node2.Ports.Add(new DiagramPort { Name = "left", Side = PortSide.Left, Offset = 0.5 });

        page.Nodes.Add(node1);
        page.Nodes.Add(node2);

        page.Edges.Add(new DiagramEdge
        {
            SourceNodeId = node1.Id,
            TargetNodeId = node2.Id,
            SourcePortId = node1.Ports[0].Id,
            TargetPortId = node2.Ports[0].Id
        });

        return Tempo.Blazor.Components.Diagram.Serialization.DiagramSerializer.Serialize(doc);
    }
}
