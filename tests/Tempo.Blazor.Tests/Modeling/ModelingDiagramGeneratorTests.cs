using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Serialization;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class ModelingDiagramGeneratorTests : LocalizationTestBase
{
    public ModelingDiagramGeneratorTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());
    }

    [Fact]
    public void Known_semantic_type_uses_mapped_stencil()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            Element("task-a", "source-task-a", "bpmn", "userTask", "Approve request"));

        var result = generator.Generate(model);

        result.Document!.Nodes.Should().ContainSingle()
            .Which.StencilId.Should().Be("bpmn2.task.user");
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Unknown_semantic_type_adds_issue_and_uses_fallback_node_stencil()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            Element("unknown-a", "source-unknown-a", "bpmn", "mysteryType", "Mystery"));

        var result = generator.Generate(model);

        result.Document!.Nodes.Should().ContainSingle()
            .Which.StencilId.Should().Be("general.rectangle");
        result.Issues.Should().ContainSingle(issue =>
            issue.Severity == ModelingIssueSeverity.Warning
            && issue.Category == "mapping"
            && issue.SourceElementId == "unknown-a");
    }

    [Fact]
    public void Source_metadata_is_preserved_in_node_data()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            Element("task-a", "jira-123", "bpmn", "userTask", "Approve request", "JiraIssue", "/projects/TEMPO/issues/123"));

        var result = generator.Generate(model);

        var node = result.Document!.Nodes.Single();
        node.Id.Should().Be("jira-123");
        node.Data["sourceId"].Should().Be("jira-123");
        node.Data["sourceType"].Should().Be("JiraIssue");
        node.Data["sourcePath"].Should().Be("/projects/TEMPO/issues/123");
        node.Data["name"].Should().Be("Approve request");
        node.Data["label"].Should().Be("Approve request");
    }

    [Fact]
    public void Relationship_with_missing_source_or_target_adds_issue_and_is_skipped()
    {
        var generator = CreateGenerator();
        var relationship = Relationship("flow-a", "task-a", "missing-task", "sequenceFlow");
        var model = CreateModel(
            [Element("task-a", "source-task-a", "bpmn", "userTask", "Approve request")],
            [relationship]);

        var result = generator.Generate(model);

        result.Document!.Edges.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue =>
            issue.Severity == ModelingIssueSeverity.Warning
            && issue.SourceRelationshipId == "flow-a"
            && issue.Message.Contains("missing source or target", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Empty_model_generates_empty_document_without_exception()
    {
        var generator = CreateGenerator();
        var model = CreateModel([]);

        var result = generator.Generate(model);

        result.Document.Should().NotBeNull();
        result.Document!.Nodes.Should().BeEmpty();
        result.Document.Edges.Should().BeEmpty();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Large_model_generates_under_two_seconds()
    {
        var generator = CreateGenerator();
        var elements = Enumerable.Range(0, 1_000)
            .Select(index => Element($"task-{index}", $"source-task-{index}", "bpmn", "userTask", $"Task {index}"))
            .ToArray();
        var relationships = Enumerable.Range(0, 500)
            .Select(index => Relationship($"flow-{index}", $"task-{index}", $"task-{index + 1}", "sequenceFlow"))
            .ToArray();
        var model = CreateModel(elements, relationships);

        var stopwatch = Stopwatch.StartNew();
        var result = generator.Generate(model);
        stopwatch.Stop();

        result.Document!.Nodes.Should().HaveCount(1_000);
        result.Document.Edges.Should().HaveCount(500);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Cyclic_relationships_generate_both_edges_without_looping()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B")
            ],
            [
                Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow"),
                Relationship("flow-b-a", "task-b", "task-a", "sequenceFlow")
            ]);

        var result = generator.Generate(model);

        result.Document!.Edges.Should().HaveCount(2);
        result.Document.Edges.Select(edge => (edge.SourceNodeId, edge.TargetNodeId)).Should().Contain([
            ("source-task-a", "source-task-b"),
            ("source-task-b", "source-task-a")
        ]);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Self_referencing_relationship_adds_issue_and_is_skipped()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [Element("task-a", "source-task-a", "bpmn", "userTask", "A")],
            [Relationship("flow-self", "task-a", "task-a", "sequenceFlow")]);

        var result = generator.Generate(model);

        result.Document!.Edges.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue =>
            issue.SourceRelationshipId == "flow-self"
            && issue.Message.Contains("self", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_element_ids_add_issue_and_skip_later_duplicate()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
            Element("task-a", "source-task-a-duplicate", "bpmn", "serviceTask", "Duplicate A"));

        var result = generator.Generate(model);

        result.Document!.Nodes.Should().ContainSingle();
        result.Document.Nodes.Single().Id.Should().Be("source-task-a");
        result.Issues.Should().ContainSingle(issue =>
            issue.SourceElementId == "task-a"
            && issue.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Known_relationship_type_uses_edge_stencil_defaults()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B")
            ],
            [Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow", "Next")]);

        var result = generator.Generate(model);

        var edge = result.Document!.Edges.Should().ContainSingle().Which;
        edge.Id.Should().Be("flow-a-b");
        edge.SourceNodeId.Should().Be("source-task-a");
        edge.TargetNodeId.Should().Be("source-task-b");
        edge.ConnectorType.Should().Be("bpmn-sequence-flow");
        edge.Label.Should().Be("Next");
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Requested_view_uses_view_page_metadata_positions_dimensions_and_waypoints()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B"),
                Element("task-c", "source-task-c", "bpmn", "serviceTask", "C")
            ],
            [Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow")]);
        model.Views.Add(new ModelingViewDto
        {
            Id = "view-main",
            Name = "Main process",
            Notation = "bpmn",
            ViewpointKey = "operations",
            Nodes =
            [
                new() { ElementId = "task-a", X = 120, Y = 140, Width = 180, Height = 90 },
                new() { ElementId = "task-b", X = 420, Y = 140, Width = 200, Height = 100 }
            ],
            Connections =
            [
                new()
                {
                    RelationshipId = "flow-a-b",
                    SourceNodeId = "task-a",
                    TargetNodeId = "task-b",
                    Waypoints =
                    [
                        new() { X = 310, Y = 160 },
                        new() { X = 350, Y = 210 }
                    ]
                }
            ]
        });

        var result = generator.Generate(model, new ModelingDiagramGenerationOptionsDto { ViewId = "view-main" });

        var page = result.Document!.ActivePage;
        page.Id.Should().Be("view-main");
        page.Name.Should().Be("Main process");
        page.Nodes.Should().HaveCount(2);
        page.Nodes.Should().NotContain(node => node.Id == "source-task-c");
        page.Nodes.Single(node => node.Id == "source-task-a").Should().Match<DiagramNode>(node =>
            node.X == 120 && node.Y == 140 && node.W == 180 && node.H == 90);
        var edge = page.Edges.Should().ContainSingle().Which;
        edge.Waypoints.Should().HaveCount(2);
        edge.IsManuallyRouted.Should().BeTrue();
        edge.Waypoints[0].X.Should().Be(310);
        edge.Waypoints[1].Y.Should().Be(210);
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Default_view_generates_all_elements_on_default_layer_with_stable_z_index()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B")
            ],
            [Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow")]);

        var result = generator.Generate(model);

        var page = result.Document!.ActivePage;
        page.Id.Should().Be("modeling-page-default");
        page.Layers.Should().ContainSingle(layer => layer.Id == "modeling-default");
        page.Nodes.Should().OnlyContain(node => node.LayerId == "modeling-default");
        page.Edges.Should().OnlyContain(edge => edge.LayerId == "modeling-default");
        page.Nodes.Select(node => node.ZIndex).Should().BeEquivalentTo([0, 1]);
        page.Edges.Single().ZIndex.Should().Be(2);
    }

    [Fact]
    public void Repeated_generation_from_same_model_produces_identical_document()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B")
            ],
            [Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow")]);

        var first = generator.Generate(model);
        var second = generator.Generate(model);

        JsonSerializer.Serialize(first.Document!, DiagramJsonOptions.Default)
            .Should().Be(JsonSerializer.Serialize(second.Document!, DiagramJsonOptions.Default));
        first.GeneratedAt.Should().Be(second.GeneratedAt);
    }

    [Fact]
    public void Generated_document_roundtrips_through_diagram_serializer()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B")
            ],
            [Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow")]);

        var result = generator.Generate(model);
        var json = DiagramSerializer.Serialize(result.Document!);
        var restored = DiagramSerializer.Deserialize(json);

        restored.Nodes.Should().HaveCount(2);
        restored.Edges.Should().HaveCount(1);
        restored.Nodes.Select(node => node.Id).Should().BeEquivalentTo(["source-task-a", "source-task-b"]);
    }

    [Fact]
    public void Missing_requested_view_generates_default_view_and_info_issue()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
            Element("task-b", "source-task-b", "bpmn", "serviceTask", "B"));

        var result = generator.Generate(model, new ModelingDiagramGenerationOptionsDto { ViewId = "missing-view" });

        result.Document!.Nodes.Should().HaveCount(2);
        result.Document.ActivePage.Id.Should().Be("modeling-page-default");
        result.Issues.Should().ContainSingle(issue =>
            issue.Severity == ModelingIssueSeverity.Info
            && issue.Category == "view"
            && issue.Message.Contains("missing-view", StringComparison.Ordinal));
    }

    [Fact]
    public void Single_element_model_generates_one_node_and_no_edges()
    {
        var generator = CreateGenerator();
        var model = CreateModel(Element("task-a", "source-task-a", "bpmn", "userTask", "A"));

        var result = generator.Generate(model);

        result.Document!.Nodes.Should().ContainSingle();
        result.Document.Edges.Should().BeEmpty();
        result.Issues.Should().BeEmpty();
    }

    [Fact]
    public void Generator_ignores_element_ParentId_for_node_nesting()
    {
        // Phase 12 decision (confirmed): element ParentId drives the MODEL TREE hierarchy only.
        // Diagram nesting stays owned by ModelingViewNodeDto.ParentNodeId — the generator must
        // never map element ParentId onto DiagramNode.ParentNodeId.
        var parent = Element("element-parent", "source-parent", "bpmn", "subProcess", "Parent");
        var child = Element("element-child", "source-child", "bpmn", "userTask", "Child");
        child.ParentId = "element-parent";

        var generator = CreateGenerator();
        var result = generator.Generate(CreateModel(parent, child));

        result.Document.Should().NotBeNull();
        result.Issues.Should().NotContain(issue => issue.Category == "hierarchy");
        var nodes = result.Document!.Nodes;
        nodes.Should().HaveCount(2);
        nodes.Should().OnlyContain(node => node.ParentNodeId == null,
            "element ParentId is never projected onto the diagram node nesting");
    }

    [Fact]
    public void Generated_document_renders_in_diagram_editor_without_exception()
    {
        var generator = CreateGenerator();
        var model = CreateModel(
            [
                Element("task-a", "source-task-a", "bpmn", "userTask", "A"),
                Element("task-b", "source-task-b", "bpmn", "serviceTask", "B")
            ],
            [Relationship("flow-a-b", "task-a", "task-b", "sequenceFlow")]);
        var document = generator.Generate(model).Document!;

        var cut = Render<TmDiagramEditor>(parameters => parameters
            .Add(editor => editor.Document, document)
            .Add(editor => editor.ReadOnly, true)
            .Add(editor => editor.ShowToolbar, false)
            .Add(editor => editor.ShowToolbox, false)
            .Add(editor => editor.ShowPropertiesPanel, false)
            .Add(editor => editor.ShowLayersPanel, false)
            .Add(editor => editor.ShowMinimap, false));

        cut.Find(".tm-diagram-editor").Should().NotBeNull();
        cut.Find("[data-node-id='source-task-a']").Should().NotBeNull();
    }

    private static ModelingDiagramGenerator CreateGenerator()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());

        return new ModelingDiagramGenerator(new TestModelingStencilMapper(), registry);
    }

    private static ModelingModelDto CreateModel(params ModelingElementDto[] elements)
        => CreateModel(elements, []);

    private static ModelingModelDto CreateModel(
        IReadOnlyCollection<ModelingElementDto> elements,
        IReadOnlyCollection<ModelingRelationshipDto> relationships)
        => new()
        {
            Id = "modeling-test-model",
            Title = "Modeling test model",
            Notation = "bpmn",
            SupportedNotations = ["bpmn", "archimate"],
            Elements = elements.ToList(),
            Relationships = relationships.ToList()
        };

    private static ModelingElementDto Element(
        string id,
        string sourceId,
        string notation,
        string semanticType,
        string name,
        string sourceType = "ExternalObject",
        string sourcePath = "")
        => new()
        {
            Id = id,
            SourceId = sourceId,
            SourceType = sourceType,
            SourcePath = sourcePath,
            Notation = notation,
            SemanticType = semanticType,
            Name = name
        };

    private static ModelingRelationshipDto Relationship(
        string id,
        string sourceElementId,
        string targetElementId,
        string relationshipType,
        string name = "")
        => new()
        {
            Id = id,
            SourceId = $"source-{id}",
            SourceType = "ExternalRelationship",
            SourceElementId = sourceElementId,
            TargetElementId = targetElementId,
            RelationshipType = relationshipType,
            Name = name
        };

    private sealed class TestModelingStencilMapper : IModelingStencilMapper
    {
        public string? GetStencilId(string notationKey, string semanticType)
            => (notationKey, semanticType) switch
            {
                ("bpmn", "userTask") => "bpmn2.task.user",
                ("bpmn", "serviceTask") => "bpmn2.task.service",
                ("bpmn", "startEvent") => "bpmn2.event.start",
                ("archimate", "applicationComponent") => "archimate3.application.component",
                _ => null
            };

        public string? GetEdgeStencilId(string notationKey, string relationshipType)
            => (notationKey, relationshipType) switch
            {
                ("bpmn", "sequenceFlow") => "bpmn2.flow.sequence",
                ("archimate", "serving") => "archimate3.relationship.serving",
                _ => null
            };
    }
}
