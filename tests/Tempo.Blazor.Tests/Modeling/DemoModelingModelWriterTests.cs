using System.Text.Json;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 2: <see cref="DemoModelingModelWriter"/> is the reference writer for the v2 change set —
/// it advertises <see cref="ModelingModelWriterCapabilities.All"/> and applies structure + layout
/// through the shared <see cref="ModelingOperationEngine"/> in the documented order, so the demo
/// exercises exactly the semantics a host writer must implement.
/// </summary>
public sealed class DemoModelingModelWriterTests
{
    [Fact]
    public void Demo_writer_advertises_structure_and_layout()
    {
        var writer = new DemoModelingModelWriter();

        writer.Capabilities.Should().HaveFlag(ModelingModelWriterCapabilities.Content);
        writer.Capabilities.Should().HaveFlag(ModelingModelWriterCapabilities.Structure);
        writer.Capabilities.Should().HaveFlag(ModelingModelWriterCapabilities.Layout);
    }

    [Fact]
    public async Task Content_changes_keep_working()
    {
        var writer = new DemoModelingModelWriter();

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            Elements =
            [
                new ModelingElementChange
                {
                    ElementId = "bpmn-validate-order",
                    Name = "Validate the order",
                    Description = "Renamed through the v2 writer",
                    SetProperties = new Dictionary<string, JsonElement>
                    {
                        ["owner"] = JsonSerializer.SerializeToElement("Finance")
                    }
                }
            ],
            Relationships =
            [
                new ModelingRelationshipChange { RelationshipId = "rel-validate-to-ship", Name = "ready to ship" }
            ]
        });

        result.Succeeded.Should().BeTrue();
        var element = writer.PersistedModel.Elements.Single(item => item.Id == "bpmn-validate-order");
        element.Name.Should().Be("Validate the order");
        element.Description.Should().Be("Renamed through the v2 writer");
        element.Properties["owner"].GetString().Should().Be("Finance");
        writer.PersistedModel.Relationships.Single(item => item.Id == "rel-validate-to-ship")
            .Name.Should().Be("ready to ship");
    }

    [Fact]
    public async Task Structural_changes_are_persisted_in_the_documented_order()
    {
        var writer = new DemoModelingModelWriter();

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            AddedElements =
            [
                // Parents precede children in list order (the editor emits this order).
                new ModelingElementAdd
                {
                    Id = "bpmn-pool-fulfillment",
                    SemanticType = "pool",
                    Name = "Order fulfillment",
                    Notation = "bpmn",
                    ParentId = string.Empty
                },
                new ModelingElementAdd
                {
                    Id = "bpmn-lane-warehouse",
                    SemanticType = "lane",
                    Name = "Warehouse",
                    Notation = "bpmn",
                    ParentId = "bpmn-pool-fulfillment"
                },
                new ModelingElementAdd
                {
                    Id = "bpmn-task-pick",
                    SemanticType = "userTask",
                    Name = "Pick items",
                    Notation = "bpmn",
                    ParentId = "bpmn-lane-warehouse",
                    Properties = new Dictionary<string, JsonElement>
                    {
                        ["stencilId"] = JsonSerializer.SerializeToElement("bpmn2.task.user")
                    }
                }
            ],
            Elements =
            [
                new ModelingElementChange { ElementId = "bpmn-ship-order", ParentId = "bpmn-lane-warehouse" }
            ],
            AddedRelationships =
            [
                new ModelingRelationshipAdd
                {
                    Id = "rel-validate-to-pick",
                    RelationshipType = "sequenceFlow",
                    SourceElementId = "bpmn-validate-order",
                    TargetElementId = "bpmn-task-pick",
                    Name = "picked"
                }
            ],
            RemovedRelationshipIds = ["rel-start-to-validate"],
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = "demo-fulfillment-overview",
                    Nodes =
                    [
                        new ModelingViewNodeChange { ElementId = "bpmn-task-pick", X = 260, Y = 360, Width = 180, Height = 90 },
                        new ModelingViewNodeChange { ElementId = "bpmn-ship-order", ParentNodeId = "bpmn-pool-fulfillment" }
                    ],
                    RemovedNodeElementIds = [],
                    RemovedConnectionRelationshipIds = ["rel-start-to-validate"],
                    Connections =
                    [
                        new ModelingViewConnectionChange { RelationshipId = "rel-validate-to-pick" }
                    ]
                }
            ]
        });

        result.Succeeded.Should().BeTrue(string.Join(" | ", result.Issues.Select(issue => issue.Message)));

        var model = writer.PersistedModel;
        model.Elements.Should().Contain(element => element.Id == "bpmn-lane-warehouse" && element.ParentId == "bpmn-pool-fulfillment");
        var task = model.Elements.Single(element => element.Id == "bpmn-task-pick");
        task.ParentId.Should().Be("bpmn-lane-warehouse");
        task.Properties["stencilId"].GetString().Should().Be("bpmn2.task.user");
        model.Elements.Single(element => element.Id == "bpmn-ship-order").ParentId.Should().Be("bpmn-lane-warehouse");
        model.Relationships.Should().NotContain(relationship => relationship.Id == "rel-start-to-validate");
        model.Relationships.Should().Contain(relationship => relationship.Id == "rel-validate-to-pick"
            && relationship.SourceElementId == "bpmn-validate-order");
        var node = model.Views.Single(view => view.Id == "demo-fulfillment-overview").Nodes.Single(item => item.ElementId == "bpmn-task-pick");
        node.X.Should().Be(260);
        node.Y.Should().Be(360);
        model.Views.Single(view => view.Id == "demo-fulfillment-overview").Nodes
            .Single(item => item.ElementId == "bpmn-ship-order").ParentNodeId.Should().Be("bpmn-pool-fulfillment");
        model.Views.Single(view => view.Id == "demo-fulfillment-overview").Connections
            .Should().Contain(connection => connection.RelationshipId == "rel-validate-to-pick");
    }

    [Fact]
    public async Task Layout_changes_persist_node_geometry_and_waypoints()
    {
        var writer = new DemoModelingModelWriter();

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = "demo-fulfillment-overview",
                    Nodes = [new ModelingViewNodeChange { ElementId = "bpmn-validate-order", X = 640, Y = 220 }],
                    Connections =
                    [
                        new ModelingViewConnectionChange
                        {
                            RelationshipId = "rel-validate-to-ship",
                            Waypoints = [new ModelingViewWaypointDto { X = 500, Y = 260 }, new ModelingViewWaypointDto { X = 560, Y = 300 }]
                        }
                    ]
                }
            ]
        });

        result.Succeeded.Should().BeTrue();
        var view = writer.PersistedModel.Views.Single(item => item.Id == "demo-fulfillment-overview");
        var node = view.Nodes.Single(item => item.ElementId == "bpmn-validate-order");
        node.X.Should().Be(640);
        node.Y.Should().Be(220);
        node.Width.Should().Be(160, "omitted geometry keeps the stored value");
        var connection = view.Connections.Single(item => item.RelationshipId == "rel-validate-to-ship");
        connection.Waypoints.Should().HaveCount(2);
        connection.Waypoints[0].X.Should().Be(500);

        // An empty waypoint list clears the manual routing.
        var cleared = await SaveAsync(writer, new ModelingModelChangeSet
        {
            ViewChanges =
            [
                new ModelingViewChange
                {
                    ViewId = "demo-fulfillment-overview",
                    Connections = [new ModelingViewConnectionChange { RelationshipId = "rel-validate-to-ship", Waypoints = [] }]
                }
            ]
        });

        cleared.Succeeded.Should().BeTrue();
        writer.PersistedModel.Views.Single(item => item.Id == "demo-fulfillment-overview").Connections
            .Single(item => item.RelationshipId == "rel-validate-to-ship").Waypoints.Should().BeEmpty();
    }

    [Fact]
    public async Task View_changes_work_when_the_model_has_no_view_yet()
    {
        var writer = new DemoModelingModelWriter();
        // Simulate a host model without a view: the writer/engine create ONE default view.
        writer.PersistedModel.Views.Clear();

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            ViewChanges =
            [
                new ModelingViewChange
                {
                    Nodes = [new ModelingViewNodeChange { ElementId = "bpmn-validate-order", X = 32, Y = 48 }]
                }
            ]
        });

        result.Succeeded.Should().BeTrue();
        writer.PersistedModel.Views.Should().ContainSingle();
        writer.PersistedModel.Views[0].Nodes.Should().ContainSingle(node => node.ElementId == "bpmn-validate-order");
    }

    [Fact]
    public async Task An_invalid_structural_batch_fails_without_persisting()
    {
        var writer = new DemoModelingModelWriter();

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            // Still referenced by rel-validate-to-ship and rel-* incident relationships: the editor
            // expands the cascade, a writer must not — the engine reports it and nothing is saved.
            RemovedElementIds = ["bpmn-validate-order"]
        });

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("still referenced");
        result.Issues.Should().Contain(issue => issue.Message.Contains("still referenced", StringComparison.Ordinal));
        writer.SaveCount.Should().Be(0, "a rejected batch never advances the source version");
        writer.PersistedModel.Elements.Should().Contain(element => element.Id == "bpmn-validate-order");
    }

    [Fact]
    public async Task Structural_changes_bump_the_source_version_so_the_next_save_does_not_conflict()
    {
        var writer = new DemoModelingModelWriter();

        var first = await SaveAsync(writer, new ModelingModelChangeSet
        {
            AddedElements = [new ModelingElementAdd { Id = "bpmn-task-audit", SemanticType = "userTask", Name = "Audit", Notation = "bpmn" }]
        });
        first.Succeeded.Should().BeTrue();
        var versionAfterFirst = writer.SourceVersion;
        versionAfterFirst.Should().NotBe("2026.06");

        var second = await SaveAsync(writer, new ModelingModelChangeSet
        {
            Elements = [new ModelingElementChange { ElementId = "bpmn-task-audit", Name = "Audit order" }]
        });
        second.Succeeded.Should().BeTrue();
        writer.PersistedModel.Elements.Single(element => element.Id == "bpmn-task-audit").Name.Should().Be("Audit order");
    }

    [Fact]
    public async Task Injected_relationship_rules_reject_an_invalid_flow_and_persist_nothing()
    {
        var writer = new DemoModelingModelWriter(new PoolRules());

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            AddedElements =
            [
                new ModelingElementAdd { Id = "bpmn-pool-warehouse", SemanticType = "pool", Name = "Warehouse", Notation = "bpmn" },
                new ModelingElementAdd
                {
                    Id = "bpmn-task-pack",
                    SemanticType = "userTask",
                    Name = "Pack",
                    Notation = "bpmn",
                    ParentId = "bpmn-pool-warehouse"
                }
            ],
            AddedRelationships =
            [
                // A SequenceFlow into a Pool is invalid BPMN: the engine's relationship rule rejects
                // it because the writer passed the injected rules into its operation context.
                new ModelingRelationshipAdd
                {
                    Id = "rel-validate-to-pool",
                    RelationshipType = "sequenceFlow",
                    SourceElementId = "bpmn-validate-order",
                    TargetElementId = "bpmn-pool-warehouse"
                }
            ]
        });

        result.Succeeded.Should().BeFalse();
        // The engine's invalid_relationship wording: only that code prefixes "'<type>' from … to … is
        // not valid for notation" — a not_found endpoint or a duplicate id would read differently.
        result.Message.Should().StartWith(
            "operations[2] add_relationship: 'sequenceFlow' from 'userTask' to 'pool' is not valid for notation 'mixed'.");
        result.Issues.Should().Contain(issue => issue.Message.Contains("not valid for notation", StringComparison.Ordinal));
        writer.SaveCount.Should().Be(0, "a rejected batch never advances the source version");
        writer.PersistedModel.Elements.Should().NotContain(element => element.Id == "bpmn-pool-warehouse");
        writer.PersistedModel.Relationships.Should().NotContain(relationship => relationship.Id == "rel-validate-to-pool");
    }

    [Fact]
    public async Task A_parent_move_that_invalidates_existing_flows_is_rejected_and_nothing_is_persisted()
    {
        var writer = new DemoModelingModelWriter(new PoolRules());

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            AddedElements = [new ModelingElementAdd { Id = "bpmn-pool-warehouse", SemanticType = "pool", Name = "Warehouse", Notation = "bpmn" }],
            // Moving the task into the new pool invalidates BOTH untouched SequenceFlows incident to
            // it (the Authoring policy re-validates after the last operation).
            Elements = [new ModelingElementChange { ElementId = "bpmn-validate-order", ParentId = "bpmn-pool-warehouse" }]
        });

        result.Succeeded.Should().BeFalse();
        result.Message.Should().StartWith(
            "operations[1] update_element: 'sequenceFlow' from 'startEvent' to 'userTask' is not valid for notation 'mixed'.");
        result.Issues.Should().HaveCount(2, "both incident flows are invalidated by the move");
        result.Issues.Should().OnlyContain(issue => issue.Message.Contains("not valid for notation", StringComparison.Ordinal));
        result.Issues.Select(issue => issue.Id).Should().OnlyHaveUniqueItems(
            "the post-batch re-validation reports SEVERAL errors for ONE operation index");
        result.Issues.Should().OnlyContain(issue => issue.Id.StartsWith("demo-modeling-save-", StringComparison.Ordinal));
        writer.SaveCount.Should().Be(0);
        var persisted = writer.PersistedModel;
        persisted.Elements.Should().NotContain(element => element.Id == "bpmn-pool-warehouse");
        persisted.Elements.Single(element => element.Id == "bpmn-validate-order").ParentId.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_set_that_names_one_id_in_two_sections_is_rejected_without_persisting()
    {
        var writer = new DemoModelingModelWriter();

        var result = await SaveAsync(writer, new ModelingModelChangeSet
        {
            Elements = [new ModelingElementChange { ElementId = "bpmn-validate-order", Name = "Renamed" }],
            RemovedElementIds = ["bpmn-validate-order"]
        });

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be("changes: id 'bpmn-validate-order' appears in more than one of added/updated/removed.");
        var issue = result.Issues.Should().ContainSingle().Subject;
        issue.Id.Should().Be("demo-modeling-save-0--1-duplicate_id");
        writer.SaveCount.Should().Be(0);
        writer.PersistedModel.Elements.Single(element => element.Id == "bpmn-validate-order")
            .Name.Should().Be("Validate order", "the contradictory change set applies nothing");
    }

    /// <summary>
    /// The BPMN rule the demo writer tests need: a <c>sequenceFlow</c> connects only flow nodes and
    /// never crosses a pool boundary. Deliberately smaller than the shipped
    /// <c>BpmnRelationshipRulesProvider</c> — the writer must not depend on a concrete rule set, only
    /// on <see cref="IModelingRelationshipRulesProvider"/>.
    /// </summary>
    private sealed class PoolRules : IModelingRelationshipRulesProvider
    {
        public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
            => Validate(sourceType, targetType, relationshipType).IsValid;

        public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
        {
            var basic = Validate(context.SourceElement.SemanticType, context.TargetElement.SemanticType, context.Relationship.RelationshipType);
            if (!basic.IsValid)
            {
                return basic;
            }

            // The pool boundary applies to SequenceFlow only — a cross-model traceability
            // relationship (realization/assignment) may legitimately span them.
            if (!string.Equals(context.Relationship.RelationshipType, "sequenceFlow", StringComparison.OrdinalIgnoreCase))
            {
                return ModelingRelationshipRuleResult.Valid;
            }

            var sourcePool = FindPool(context.SourceElement, context.ElementsById);
            var targetPool = FindPool(context.TargetElement, context.ElementsById);
            if (!string.Equals(sourcePool, targetPool, StringComparison.Ordinal))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    $"BPMN SequenceFlow cannot cross a pool boundary ('{sourcePool}' → '{targetPool}').",
                    "Keep both flow nodes inside one pool, or model the cross-pool interaction as a MessageFlow.");
            }

            return ModelingRelationshipRuleResult.Valid;
        }

        private static string FindPool(ModelingElementDto element, IReadOnlyDictionary<string, ModelingElementDto> elementsById)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = element;
            while (current is not null && seen.Add(current.Id))
            {
                if (string.Equals(current.SemanticType, "pool", StringComparison.OrdinalIgnoreCase))
                {
                    return current.Id;
                }

                if (string.IsNullOrEmpty(current.ParentId) || !elementsById.TryGetValue(current.ParentId, out var parent))
                {
                    return string.Empty;
                }

                current = parent;
            }

            return string.Empty;
        }

        private static ModelingRelationshipRuleResult Validate(string sourceType, string targetType, string relationshipType)
        {
            if (!string.Equals(relationshipType, "sequenceFlow", StringComparison.OrdinalIgnoreCase))
            {
                return ModelingRelationshipRuleResult.Valid;
            }

            if (!IsFlowNode(sourceType) || !IsFlowNode(targetType))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "BPMN SequenceFlow can connect only BPMN flow nodes.",
                    "Connect tasks, events, gateways, subprocesses, or call activities with SequenceFlow.");
            }

            return ModelingRelationshipRuleResult.Valid;
        }

        private static bool IsFlowNode(string semanticType)
            => semanticType is "userTask" or "serviceTask" or "task" or "startEvent" or "endEvent" or "subprocess"
                or "Task" or "StartEvent" or "EndEvent" or "SubProcess";
    }

    private static Task<ModelingModelSaveResult> SaveAsync(DemoModelingModelWriter writer, ModelingModelChangeSet changeSet)
    {
        changeSet.ModelId = DemoModelingModelProvider.ModelIdValue;
        changeSet.ProviderKey = DemoModelingModelProvider.ProviderKeyValue;
        changeSet.BaseVersion = writer.SourceVersion;
        return writer.SaveChangesAsync(changeSet, CancellationToken.None);
    }
}
