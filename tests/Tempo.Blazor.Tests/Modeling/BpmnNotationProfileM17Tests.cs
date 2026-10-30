using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class BpmnNotationProfileM17Tests : LocalizationTestBase
{
    [Fact]
    public void Bpmn_profile_exposes_m17_notation_types_relationships_and_viewpoints()
    {
        var profile = new BpmnNotationProfile();

        profile.NotationKey.Should().Be("bpmn2");
        profile.SupportedElementTypes.Should().Contain([
            "Task",
            "UserTask",
            "ServiceTask",
            "SendTask",
            "ReceiveTask",
            "ScriptTask",
            "BusinessRuleTask",
            "ManualTask",
            "SubProcess",
            "EventSubProcess",
            "CallActivity",
            "StartEvent",
            "EndEvent",
            "IntermediateCatchEvent",
            "IntermediateThrowEvent",
            "BoundaryEvent",
            "ExclusiveGateway",
            "ParallelGateway",
            "InclusiveGateway",
            "ComplexGateway",
            "EventBasedGateway",
            "Pool",
            "Lane",
            "DataObject",
            "DataStore",
            "Group",
            "TextAnnotation"
        ]);
        profile.SupportedRelationshipTypes.Should().Equal([
            "SequenceFlow",
            "ConditionalFlow",
            "DefaultFlow",
            "MessageFlow",
            "Association",
            "DataInputAssociation",
            "DataOutputAssociation"
        ]);
        profile.SupportedViewpointKeys.Should().Equal(["Collaboration", "Process", "Choreography", "Conversation"]);
    }

    [Fact]
    public void Legacy_bpmn_profile_has_distinct_display_name()
    {
        var legacy = new BpmnLegacyModelingNotationProfile();

        legacy.DisplayName.Should().Be("BPMN Legacy");
        legacy.DisplayName.Should().NotBe(new BpmnNotationProfile().DisplayName);
    }

    [Fact]
    public void All_bpmn_element_types_have_existing_node_stencil_mapping()
    {
        var mapper = new BpmnStencilMapper();
        var registry = CreateStencilRegistry();
        var profile = new BpmnNotationProfile();

        foreach (var semanticType in profile.SupportedElementTypes)
        {
            var stencilId = mapper.GetStencilId(profile.NotationKey, semanticType);

            stencilId.Should().NotBeNull("BPMN element type {0} must map to a diagram stencil", semanticType);
            registry.GetStencil(stencilId!).Should().NotBeNull("mapped BPMN stencil {0} must exist", stencilId);
        }
    }

    [Fact]
    public void Unknown_bpmn_element_type_returns_null_stencil_mapping()
    {
        var mapper = new BpmnStencilMapper();

        mapper.GetStencilId("bpmn2", "AiTask").Should().BeNull();
    }

    [Fact]
    public void Bpmn_relationship_rules_apply_pool_boundary_constraints()
    {
        var rules = CreateRules();

        rules.IsValidRelationship("bpmn2", "Pool", "Pool", "SequenceFlow").Should().BeFalse();
        rules.IsValidRelationship("bpmn2", "Pool", "Pool", "MessageFlow").Should().BeTrue();

        var samePoolMessage = rules.ValidateRelationship(CreateRelationshipContext(
            Relationship("message-same-pool", "task-a", "task-b", "MessageFlow"),
            Element("pool-a", "Pool", "Sales pool"),
            Element("task-a", "UserTask", "A", poolId: "pool-a"),
            Element("task-b", "ServiceTask", "B", poolId: "pool-a")));

        samePoolMessage.IsValid.Should().BeFalse();
        samePoolMessage.Message.Should().Contain("MessageFlow");
    }

    [Fact]
    public void Bpmn_relationship_rules_cover_known_valid_and_invalid_pairs()
    {
        var rules = CreateRules();
        var validPairs = new[]
        {
            ("StartEvent", "UserTask", "SequenceFlow"),
            ("UserTask", "ServiceTask", "SequenceFlow"),
            ("ServiceTask", "ExclusiveGateway", "SequenceFlow"),
            ("ExclusiveGateway", "ManualTask", "SequenceFlow"),
            ("ParallelGateway", "EndEvent", "SequenceFlow"),
            ("SubProcess", "CallActivity", "SequenceFlow"),
            ("IntermediateCatchEvent", "IntermediateThrowEvent", "SequenceFlow"),
            ("Task", "DataObject", "Association"),
            ("DataObject", "Task", "DataInputAssociation"),
            ("Task", "DataStore", "DataOutputAssociation")
        };
        var invalidPairs = new[]
        {
            ("Pool", "Pool", "SequenceFlow"),
            ("Lane", "Lane", "MessageFlow"),
            ("DataObject", "DataStore", "SequenceFlow"),
            ("TextAnnotation", "Task", "MessageFlow"),
            ("Task", "Task", "UnsupportedFlow")
        };

        validPairs.Should().OnlyContain(pair => rules.IsValidRelationship("bpmn2", pair.Item1, pair.Item2, pair.Item3));
        invalidPairs.Should().OnlyContain(pair => !rules.IsValidRelationship("bpmn2", pair.Item1, pair.Item2, pair.Item3));
    }

    [Fact]
    public void Generator_skips_cross_pool_sequence_flow_and_reports_warning()
    {
        var generator = CreateGenerator();
        var model = CreateBpmnModel(
            [
                Element("pool-a", "Pool", "Sales pool"),
                Element("pool-b", "Pool", "Warehouse pool"),
                Element("task-a", "UserTask", "Validate order", poolId: "pool-a"),
                Element("task-b", "ServiceTask", "Ship order", poolId: "pool-b")
            ],
            [Relationship("cross-pool", "task-a", "task-b", "SequenceFlow")]);

        var result = generator.Generate(model);

        result.Document!.Edges.Should().BeEmpty();
        result.Issues.Should().ContainSingle(issue =>
            issue.Severity == ModelingIssueSeverity.Warning
            && issue.Category == "validation"
            && issue.SourceRelationshipId == "cross-pool"
            && issue.Message.Contains("Pool", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generator_uses_bpmn_stencils_and_falls_back_for_unknown_ai_task()
    {
        var generator = CreateGenerator();
        var model = CreateBpmnModel(
            [
                Element("start", "StartEvent", "Start"),
                Element("ai-task", "AiTask", "AI classify order"),
                Element("end", "EndEvent", "End")
            ],
            [Relationship("start-ai", "start", "ai-task", "SequenceFlow")]);

        var result = generator.Generate(model);

        result.Document!.Nodes.Single(node => node.Data["modelElementId"].ToString() == "start")
            .StencilId.Should().Be("bpmn2.event.start");
        result.Document.Nodes.Single(node => node.Data["modelElementId"].ToString() == "ai-task")
            .StencilId.Should().Be("general.rectangle");
        result.Issues.Should().Contain(issue =>
            issue.Severity == ModelingIssueSeverity.Warning
            && issue.SourceElementId == "ai-task"
            && issue.Message.Contains("No node stencil mapping", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Generator_allows_unknown_bpmn_task_in_supported_viewpoint_and_uses_fallback()
    {
        var generator = CreateGeneratorWithViewpointRules();
        var model = CreateBpmnModel(
            [
                Element("pool", "Pool", "Sales pool"),
                Element("ai-task", "AiTask", "AI classify order", poolId: "pool")
            ],
            []);
        model.Views.Add(new ModelingViewDto
        {
            Id = "process-view",
            Name = "Process",
            Notation = "bpmn2",
            ViewpointKey = "Process",
            Nodes =
            [
                new() { ElementId = "pool", X = 40, Y = 40, Width = 480, Height = 180 },
                new() { ElementId = "ai-task", X = 120, Y = 100, Width = 160, Height = 92, ParentNodeId = "pool" }
            ]
        });

        var result = generator.Generate(model, new ModelingDiagramGenerationOptionsDto
        {
            ViewId = "process-view",
            ViewpointKey = "Process",
            IncludeIssues = true
        });

        result.Document!.Nodes.Single(node => node.Data["modelElementId"].ToString() == "ai-task")
            .StencilId.Should().Be("general.rectangle");
        result.Issues.Should().Contain(issue =>
            issue.Severity == ModelingIssueSeverity.Warning
            && issue.SourceElementId == "ai-task"
            && issue.Message.Contains("AiTask", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bpmn_relationship_rules_resolve_containment_via_semantic_ParentId()
    {
        // Phase 12: a model that reports the hierarchy ONLY through ModelingElementDto.ParentId
        // (no poolId/subProcessId property at all) must resolve the pool/subprocess containers
        // the same way the Properties-based models do.
        var rules = CreateRules();

        var samePoolSequence = rules.ValidateRelationship(CreateRelationshipContext(
            Relationship("seq-parent-same-pool", "task-a", "task-b", "SequenceFlow"),
            Element("pool-a", "Pool", "Sales pool"),
            Element("task-a", "UserTask", "A", parentId: "pool-a"),
            Element("task-b", "ServiceTask", "B", parentId: "pool-a")));
        samePoolSequence.IsValid.Should().BeTrue("SequenceFlow inside one ParentId pool stays valid");

        var samePoolMessage = rules.ValidateRelationship(CreateRelationshipContext(
            Relationship("message-parent-same-pool", "task-a", "task-b", "MessageFlow"),
            Element("pool-a", "Pool", "Sales pool"),
            Element("task-a", "UserTask", "A", parentId: "pool-a"),
            Element("task-b", "ServiceTask", "B", parentId: "pool-a")));
        samePoolMessage.IsValid.Should().BeFalse("MessageFlow within one pool is refused");
        samePoolMessage.Message.Should().Contain("MessageFlow");

        var crossPoolSequence = rules.ValidateRelationship(CreateRelationshipContext(
            Relationship("seq-parent-cross-pool", "task-a", "task-b", "SequenceFlow"),
            Element("pool-a", "Pool", "Sales pool"),
            Element("pool-b", "Pool", "Warehouse pool"),
            Element("task-a", "UserTask", "A", parentId: "pool-a"),
            Element("task-b", "ServiceTask", "B", parentId: "pool-b")));
        crossPoolSequence.IsValid.Should().BeFalse("SequenceFlow cannot cross pools reported via ParentId");

        var crossPoolMessage = rules.ValidateRelationship(CreateRelationshipContext(
            Relationship("message-parent-cross-pool", "task-a", "task-b", "MessageFlow"),
            Element("pool-a", "Pool", "Sales pool"),
            Element("pool-b", "Pool", "Warehouse pool"),
            Element("task-a", "UserTask", "A", parentId: "pool-a"),
            Element("task-b", "ServiceTask", "B", parentId: "pool-b")));
        crossPoolMessage.IsValid.Should().BeTrue();

        // A SubProcess container resolved through ParentId only: a boundary crossing between a
        // subprocess child and a pool child is refused (the old property-only walk returned an
        // empty container on both sides and let the flow through).
        var acrossContainers = rules.ValidateRelationship(CreateRelationshipContext(
            Relationship("seq-parent-subprocess-vs-pool", "task-a", "task-b", "SequenceFlow"),
            Element("sub-1", "SubProcess", "Inner subprocess"),
            Element("pool-a", "Pool", "Sales pool"),
            Element("task-a", "UserTask", "A", parentId: "sub-1"),
            Element("task-b", "ServiceTask", "B", parentId: "pool-a")));
        acrossContainers.IsValid.Should().BeFalse(
            "SequenceFlow cannot cross the SubProcess/Pool boundary reported by ParentId");
    }

    private static BpmnRelationshipRulesProvider CreateRules()
    {
        var registry = CreateProfileRegistry();
        return new BpmnRelationshipRulesProvider(registry);
    }

    private static ModelingNotationProfileRegistry CreateProfileRegistry()
        => new([
            new BpmnNotationProfile(),
            new BpmnLegacyModelingNotationProfile(),
            new ArchimateModelingNotationProfile()
        ]);

    private static ModelingDiagramGenerator CreateGenerator()
        => new(new BuiltInModelingStencilMapper(), CreateStencilRegistry(), relationshipRules: CreateRules());

    private static ModelingDiagramGenerator CreateGeneratorWithViewpointRules()
    {
        var profiles = CreateProfileRegistry();
        return new ModelingDiagramGenerator(
            new BuiltInModelingStencilMapper(),
            CreateStencilRegistry(),
            relationshipRules: new BpmnRelationshipRulesProvider(profiles),
            viewpointRules: new BuiltInModelingViewpointRulesProvider(profiles));
    }

    private static DiagramStencilRegistry CreateStencilRegistry()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        return registry;
    }

    private static ModelingModelDto CreateBpmnModel(
        IReadOnlyCollection<ModelingElementDto> elements,
        IReadOnlyCollection<ModelingRelationshipDto> relationships)
        => new()
        {
            Id = "bpmn-m17-test",
            Title = "BPMN M17 test",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Elements = elements.ToList(),
            Relationships = relationships.ToList()
        };

    private static ModelingRelationshipRuleContext CreateRelationshipContext(
        ModelingRelationshipDto relationship,
        params ModelingElementDto[] elements)
    {
        var byId = elements.ToDictionary(element => element.Id, StringComparer.Ordinal);
        return new ModelingRelationshipRuleContext
        {
            NotationKey = "bpmn2",
            Relationship = relationship,
            SourceElement = byId[relationship.SourceElementId],
            TargetElement = byId[relationship.TargetElementId],
            ElementsById = byId
        };
    }

    private static ModelingElementDto Element(string id, string semanticType, string name, string? poolId = null, string? parentId = null)
    {
        var element = new ModelingElementDto
        {
            Id = id,
            SourceId = $"m17/{id}",
            SourceType = semanticType,
            SourcePath = $"/M17/{id}",
            Notation = "bpmn2",
            SemanticType = semanticType,
            Name = name,
            ParentId = parentId ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(poolId))
            element.Properties["poolId"] = System.Text.Json.JsonSerializer.SerializeToElement(poolId);

        return element;
    }

    private static ModelingRelationshipDto Relationship(string id, string sourceId, string targetId, string relationshipType)
        => new()
        {
            Id = id,
            SourceId = $"m17/{id}",
            SourceType = relationshipType,
            SourceElementId = sourceId,
            TargetElementId = targetId,
            RelationshipType = relationshipType
        };
}
