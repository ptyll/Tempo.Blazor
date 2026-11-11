using FluentAssertions;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 3 view-import tests for the headless BPMN 2.0 XML importer (plan 6fca9139):
/// BPMNShape bounds become view nodes, BPMNEdge waypoints become connections, one view per
/// BPMNPlane (per covered model), DI flags and label bounds are preserved as <c>bpmndi:*</c>
/// properties, and broken diagram data is reported instead of silently dropped.
/// Full normalized output of every corpus fixture is pinned by <c>BpmnImportGoldenTests</c>.
/// </summary>
public sealed class BpmnViewImportTests
{
    private static BpmnImportResult Import(string fixture, Action<BpmnImportOptions>? configure = null)
    {
        var options = new BpmnImportOptions();
        configure?.Invoke(options);
        return new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture), options);
    }

    private static ModelingModelDto SingleModel(BpmnImportResult result)
    {
        result.HasErrors.Should().BeFalse(result.ErrorMessage ?? "import failed");
        return result.Models.Should().ContainSingle().Which;
    }

    private static ModelingViewDto SingleView(ModelingModelDto model)
    {
        model.Views.Should().ContainSingle();
        var view = model.Views[0];
        view.Notation.Should().Be("bpmn2");
        return view;
    }

    private static ModelingViewNodeDto Node(ModelingViewDto view, string elementId) =>
        view.Nodes.Should().ContainSingle(node => node.ElementId == elementId, $"view must contain a node for {elementId}").Subject;

    // -------------------------------------------------------------- shapes -> nodes

    [Fact]
    public void Shape_bounds_become_view_node_geometry_in_xml_order()
    {
        var view = SingleView(SingleModel(Import("simple-process.bpmn")));

        view.Id.Should().Be("BPMNPlane_1");
        view.Name.Should().Be("Order handling");
        view.Nodes.Select(node => node.ElementId).Should().Equal(
            "StartEvent_1", "Task_EnterOrder", "Gateway_Approved", "Task_Ship",
            "EndEvent_Shipped", "EndEvent_Rejected");

        var start = Node(view, "StartEvent_1");
        (start.X, start.Y, start.Width, start.Height).Should().Be((172, 152, 36, 36));
        Node(view, "Task_EnterOrder").X.Should().Be(260);
        Node(view, "Gateway_Approved").Width.Should().Be(50);
        Node(view, "EndEvent_Rejected").Y.Should().Be(262);
    }

    [Fact]
    public void Nodes_nested_in_lane_shapes_carry_the_lane_node_as_parent()
    {
        var view = SingleView(SingleModel(Import("nested-lanes.bpmn")));

        Node(view, "Lane_Phone").ParentNodeId.Should().Be("Lane_FrontOffice", "nested lanes band inside their parent lane");
        Node(view, "Task_PhoneCall").ParentNodeId.Should().Be("Lane_Phone");
        Node(view, "Task_Assess").ParentNodeId.Should().Be("Lane_BackOffice");
        Node(view, "Lane_FrontOffice").ParentNodeId.Should().BeNull("top-level lanes have no shape parent");
        Node(view, "StartEvent_Claim").ParentNodeId.Should().Be("Lane_FrontOffice");
    }

    [Fact]
    public void Pool_nodes_own_their_flow_nodes()
    {
        var model = SingleModel(Import("black-box-pool.bpmn"));
        var view = SingleView(model);

        Node(view, "Task_Send").ParentNodeId.Should().Be("Participant_Internal");
        Node(view, "Participant_External").ParentNodeId.Should().BeNull(
            "a black-box pool has no parent shape even though it carries no children");
        Node(view, "StartEvent_BlackBox").ParentNodeId.Should().Be("Participant_Internal");
    }

    // -------------------------------------------------------------- edges -> connections

    [Fact]
    public void Edge_waypoints_become_connection_geometry()
    {
        var view = SingleView(SingleModel(Import("simple-process.bpmn")));

        view.Connections.Should().HaveCount(5);
        var flow = view.Connections.Should().ContainSingle(connection => connection.RelationshipId == "Flow_1").Subject;
        flow.SourceNodeId.Should().Be("StartEvent_1");
        flow.TargetNodeId.Should().Be("Task_EnterOrder");
        flow.Waypoints.Should().HaveCount(2);
        (flow.Waypoints[0].X, flow.Waypoints[0].Y).Should().Be((208, 170));
        (flow.Waypoints[1].X, flow.Waypoints[1].Y).Should().Be((260, 170));

        var rejected = view.Connections.Single(connection => connection.RelationshipId == "Flow_Rejected");
        rejected.Waypoints.Should().HaveCount(3, "multi-bend DI polylines are preserved verbatim");
    }

    [Fact]
    public void Multiple_planes_become_multiple_views_per_model()
    {
        var result = Import("collapsed-subprocess.bpmn");
        var model = SingleModel(result);

        model.Views.Select(view => view.Id).Should().Equal("BPMNPlane_Collapsed", "BPMNPlane_Inner");
        var overview = model.Views[0];
        overview.Nodes.Select(node => node.ElementId).Should().Equal(
            "StartEvent_Collapsed", "SubProcess_Collapsed", "EndEvent_Collapsed");
        var inner = model.Views[1];
        inner.Nodes.Select(node => node.ElementId).Should().Equal("StartEvent_Inner", "Task_Inner", "EndEvent_Inner");
        inner.Connections.Should().HaveCount(2);
    }

    [Fact]
    public void Collapsed_subprocess_internals_have_no_parent_in_their_own_plane()
    {
        var model = SingleModel(Import("collapsed-subprocess.bpmn"));
        var inner = model.Views[1];

        Node(inner, "StartEvent_Inner").ParentNodeId.Should().BeNull(
            "the collapsed subprocess shape lives on the overview plane only, so the internals render un-nested on their own plane");
    }

    [Fact]
    public void Expanded_subprocess_children_nest_under_the_subprocess_node()
    {
        var view = SingleView(SingleModel(Import("event-subprocess.bpmn")));

        Node(view, "SubProcess_OnSignal").ParentNodeId.Should().BeNull();
        Node(view, "Task_Notify").ParentNodeId.Should().Be("SubProcess_OnSignal");
    }

    [Fact]
    public void Collaboration_planes_partition_into_one_view_per_covered_model()
    {
        var result = Import("collaboration-3-pools.bpmn");
        result.Models.Should().HaveCount(3);

        var customer = result.Models.Single(model => model.Id == "Process_Customer");
        var shop = result.Models.Single(model => model.Id == "Process_Shop");
        var warehouse = result.Models.Single(model => model.Id == "Process_Warehouse");

        foreach (var model in new[] { customer, shop, warehouse })
        {
            model.Views.Should().ContainSingle($"the collaboration plane must reach the {model.Id} model").Which.Id
                .Should().Be("BPMNPlane_1");
        }

        var customerView = customer.Views[0];
        customerView.Nodes.Should().OnlyContain(node =>
            customer.Elements.Any(element => element.Id == node.ElementId),
            "a model's view contains only its own elements");
        Node(customerView, "Task_PlaceOrder").ParentNodeId.Should().Be("Participant_Customer");
        customerView.Connections.Should().OnlyContain(connection =>
            connection.RelationshipId.StartsWith("Flow_", StringComparison.Ordinal),
            "a connection is imported only when BOTH ends are nodes of that view — cross-model " +
            "message flows stay out of the per-model views (their geometry is preserved on the relationship)");

        var order = customer.Relationships.Single(relationship => relationship.Id == "MessageFlow_Order");
        order.Properties.Should().ContainKey("bpmndi:waypoints");
        order.Properties.Should().ContainKey("bpmndi:planeId").WhoseValue.GetString().Should().Be("BPMNPlane_1");
    }

    [Fact]
    public void Collaboration_views_render_through_the_generator_without_missing_end_issues()
    {
        var result = Import("collaboration-3-pools.bpmn");
        var registry = CreateRegistry();
        var generator = new ModelingDiagramGenerator(new BpmnViewImportStencilMapper(), registry);

        foreach (var model in result.Models)
        {
            var generated = generator.Generate(
                model, new ModelingDiagramGenerationOptionsDto { ViewId = "BPMNPlane_1" });

            generated.Issues.Should().NotContain(issue =>
                issue.Message.Contains("missing source or target", StringComparison.Ordinal),
                $"no view of model {model.Id} may reference connections with ends outside the view");
        }
    }

    [Fact]
    public void Single_with_pools_imports_one_view_with_every_element_and_pool_root_parents()
    {
        var result = Import("collaboration-3-pools.bpmn", options => options.ModelSplit = BpmnModelSplit.SingleWithPools);
        var model = SingleModel(result);
        var view = SingleView(model);

        view.Id.Should().Be("BPMNPlane_1");
        view.Nodes.Select(node => node.ElementId).Should().BeEquivalentTo(
            model.Elements.Select(element => element.Id), "the single plane covers every element of the pooled model");
        view.Connections.Should().HaveCount(model.Relationships.Count, "every flow and message flow connects in-model nodes");

        Node(view, "Participant_Customer").ParentNodeId.Should().BeNull("pools are root parents");
        Node(view, "Participant_Warehouse").ParentNodeId.Should().BeNull();
        Node(view, "Task_PlaceOrder").ParentNodeId.Should().Be("Participant_Customer");
        Node(view, "Task_Confirm").ParentNodeId.Should().Be("Lane_Sales");
    }

    [Fact]
    public void Per_process_planes_attach_to_their_own_process_model()
    {
        var result = Import("call-activity.bpmn");
        result.Models.Should().HaveCount(2);

        var order = result.Models.Single(model => model.Id == "Process_Order");
        order.Views.Should().ContainSingle().Which.Id.Should().Be("BPMNPlane_1");
        order.Views[0].Nodes.Should().HaveCount(4);
        result.Models.Single(model => model.Id == "Process_Fulfilment").Views.Should().ContainSingle()
            .Which.Id.Should().Be("BPMNPlane_2");
    }

    // -------------------------------------------------------------- DI flags and labels

    [Fact]
    public void Di_flags_are_preserved_as_bpmndi_properties_on_the_element()
    {
        var model = SingleModel(Import("simple-process.bpmn"));
        var gateway = model.Elements.Single(element => element.Id == "Gateway_Approved");
        gateway.Properties.Should().ContainKey("bpmndi:isMarkerVisible").WhoseValue.GetString().Should().Be("true");

        var collapsed = SingleModel(Import("collapsed-subprocess.bpmn"))
            .Elements.Single(element => element.Id == "SubProcess_Collapsed");
        collapsed.Properties.Should().ContainKey("bpmndi:isExpanded").WhoseValue.GetString().Should().Be("false");

        var lane = SingleModel(Import("nested-lanes.bpmn")).Elements.Single(element => element.Id == "Lane_Phone");
        lane.Properties.Should().ContainKey("bpmndi:isHorizontal").WhoseValue.GetString().Should().Be("true");
    }

    [Fact]
    public void Label_bounds_are_preserved_for_the_export_round_trip()
    {
        var model = SingleModel(Import("simple-process.bpmn"));
        var start = model.Elements.Single(element => element.Id == "StartEvent_1");
        var label = start.Properties.Should().ContainKey("bpmndi:labelBounds").WhoseValue;
        label.GetProperty("x").GetDouble().Should().Be(158);
        label.GetProperty("y").GetDouble().Should().Be(195);
        label.GetProperty("width").GetDouble().Should().Be(65);
        label.GetProperty("height").GetDouble().Should().Be(27);

        var flow = model.Relationships.Single(relationship => relationship.Id == "Flow_Approved");
        var flowLabel = flow.Properties.Should().ContainKey("bpmndi:labelBounds").WhoseValue;
        flowLabel.GetProperty("x").GetDouble().Should().Be(475);
    }

    // -------------------------------------------------------------- partial DI

    [Fact]
    public void Missing_edges_are_generated_and_reported()
    {
        var result = Import("boundary-events.bpmn");
        var model = SingleModel(result);
        var view = SingleView(model);

        view.Nodes.Should().HaveCount(18, "every shape is present");
        view.Connections.Should().HaveCount(13, "the fixture carries no BPMNEdge elements; all connections are generated");
        var flow = view.Connections.Single(connection => connection.RelationshipId == "Flow_B1");
        flow.Waypoints.Should().HaveCount(2, "generated connections carry routed endpoint waypoints");
        result.Warnings.Should().Contain(warning => warning.Message.Contains("have no BPMNDI edge"));
    }

    [Fact]
    public void Partial_di_fill_targets_the_ancestors_view_offsets_below_the_di_bounds_and_never_duplicates()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                         xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                         id="Definitions_Partial" targetNamespace="urn:x">
              <process id="Process_Partial" isExecutable="false">
                <startEvent id="Start_A"/>
                <subProcess id="Sub_A">
                  <task id="Task_A1"/>
                </subProcess>
                <subProcess id="Sub_B">
                  <task id="Task_B1"/>
                </subProcess>
                <endEvent id="End_A"/>
                <sequenceFlow id="Flow_1" sourceRef="Start_A" targetRef="Sub_A"/>
                <sequenceFlow id="Flow_2" sourceRef="Sub_A" targetRef="Sub_B"/>
                <sequenceFlow id="Flow_3" sourceRef="Sub_B" targetRef="End_A"/>
                <sequenceFlow id="Flow_A1" sourceRef="Task_A1" targetRef="Task_A1"/>
                <sequenceFlow id="Flow_B1" sourceRef="Task_B1" targetRef="Task_B1"/>
              </process>
              <bpmndi:BPMNDiagram id="Diagram_Partial">
                <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="Process_Partial">
                  <bpmndi:BPMNShape id="Start_A_di" bpmnElement="Start_A">
                    <dc:Bounds x="100" y="100" width="36" height="36"/>
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="Sub_A_di" bpmnElement="Sub_A">
                    <dc:Bounds x="200" y="80" width="200" height="120"/>
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="Sub_B_di" bpmnElement="Sub_B">
                    <dc:Bounds x="200" y="260" width="200" height="120"/>
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="End_A_di" bpmnElement="End_A">
                    <dc:Bounds x="460" y="100" width="36" height="36"/>
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
                <bpmndi:BPMNPlane id="BPMNPlane_2" bpmnElement="Sub_A">
                  <bpmndi:BPMNShape id="Task_A1_di" bpmnElement="Task_A1">
                    <dc:Bounds x="240" y="120" width="100" height="80"/>
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;

        var result = new BpmnXmlImporter().Import(xml);
        var model = SingleModel(result);
        model.Views.Should().HaveCount(2);

        var overview = model.Views[0];
        var inner = model.Views[1];
        overview.Nodes.Should().Contain(node => node.ElementId == "Task_B1",
            "Task_B1 has no shape anywhere; its nearest viewed semantic ancestor (Sub_B) lives on the overview");
        inner.Nodes.Should().NotContain(node => node.ElementId == "Task_B1");
        inner.Nodes.Should().ContainSingle(node => node.ElementId == "Task_A1");

        var diBottom = overview.Nodes.Where(node => node.ElementId != "Task_B1").Max(node => node.Y + node.Height);
        var filled = Node(overview, "Task_B1");
        filled.Y.Should().BeGreaterThanOrEqualTo(diBottom,
            "the generated block is offset below the view's DI bounding box, never overlapping it");
        Node(overview, "Task_B1").ParentNodeId.Should().Be("Sub_B");

        var allNodes = model.Views.SelectMany(view => view.Nodes).ToList();
        allNodes.Select(node => node.ElementId).Should().OnlyHaveUniqueItems("no duplicate ElementId across views");
        foreach (var viewNodes in model.Views.Select(view => view.Nodes))
        {
            for (var first = 0; first < viewNodes.Count; first++)
            {
                for (var second = first + 1; second < viewNodes.Count; second++)
                {
                    var a = viewNodes[first];
                    var b = viewNodes[second];
                    var nested = a.ParentNodeId == b.ElementId || b.ParentNodeId == a.ElementId;
                    var overlap = a.X < b.X + b.Width && b.X < a.X + a.Width
                        && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                    overlap.Should().BeFalse($"nodes {a.ElementId} and {b.ElementId} must not overlap within one view");
                }
            }
        }
    }

    [Fact]
    public void Edge_attributes_label_styles_and_unsupported_di_constructs_are_preserved_or_reported()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                         xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                         xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                         id="Definitions_EdgeFlags" targetNamespace="urn:x">
              <process id="Process_EdgeFlags" isExecutable="false">
                <startEvent id="Start_F"/>
                <task id="Task_F"/>
                <sequenceFlow id="Flow_F" sourceRef="Start_F" targetRef="Task_F"/>
              </process>
              <bpmndi:BPMNDiagram id="Diagram_EdgeFlags">
                <bpmndi:BPMNLabelStyle id="LabelStyle_1" fontFamily="Arial" fontSize="11"/>
                <bpmndi:BPMNPlane id="BPMNPlane_F" bpmnElement="Process_EdgeFlags">
                  <bpmndi:BPMNShape id="Start_F_di" bpmnElement="Start_F">
                    <dc:Bounds x="100" y="100" width="36" height="36"/>
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="Task_F_di" bpmnElement="Task_F">
                    <dc:Bounds x="200" y="100" width="100" height="80"/>
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNEdge id="Flow_F_di" bpmnElement="Flow_F" messageVisibleKind="initiating"
                                   sourceElement="Start_F_di" targetElement="Task_F_di">
                    <di:extension>
                      <marker kind="custom"/>
                    </di:extension>
                    <di:waypoint x="136" y="118"/>
                    <di:waypoint x="200" y="118"/>
                    <bpmndi:BPMNLabel id="Flow_F_label" labelStyle="LabelStyle_1">
                      <dc:Bounds x="150" y="90" width="40" height="20"/>
                    </bpmndi:BPMNLabel>
                  </bpmndi:BPMNEdge>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;

        var result = new BpmnXmlImporter().Import(xml);
        var model = SingleModel(result);
        var flow = model.Relationships.Single(relationship => relationship.Id == "Flow_F");

        flow.Properties.Should().ContainKey("bpmndi:messageVisibleKind").WhoseValue.GetString().Should().Be("initiating");
        flow.Properties.Should().ContainKey("bpmndi:sourceElement").WhoseValue.GetString().Should().Be("Start_F_di");
        flow.Properties.Should().ContainKey("bpmndi:targetElement").WhoseValue.GetString().Should().Be("Task_F_di");
        flow.Properties.Should().ContainKey("bpmndi:labelStyle").WhoseValue.GetString().Should().Be("LabelStyle_1");
        flow.Properties.Should().ContainKey("bpmndi:labelBounds");
        var waypoints = flow.Properties.Should().ContainKey("bpmndi:waypoints").WhoseValue;
        waypoints.GetArrayLength().Should().Be(2);
        flow.Properties.Should().ContainKey("bpmndi:planeId").WhoseValue.GetString().Should().Be("BPMNPlane_F");

        result.Losses.Should().Contain(loss => loss.Construct == "BPMNLabelStyle");
        result.Losses.Should().Contain(loss => loss.Construct == "di:extension");
    }

    // -------------------------------------------------------------- losses and warnings

    [Fact]
    public void Di_referencing_unknown_elements_is_reported_not_dropped()
    {
        var result = Import("unsupported-constructs.bpmn");

        result.Losses.Should().Contain(loss => loss.Construct == "BPMNShape" && loss.Reason.Contains("ChoreoTask_1"));
        result.Losses.Should().Contain(loss => loss.Construct == "BPMNShape" && loss.Reason.Contains("Conversation_1"));
        result.Losses.Should().Contain(loss => loss.Construct == "BPMNEdge" && loss.Reason.Contains("ConversationLink_1"));
        result.Warnings.Should().Contain(warning => warning.Message.Contains("ChoreoTask_1"));

        var us = result.Models.Single(model => model.Id == "Process_Us");
        us.Views.Should().ContainSingle().Which.Nodes.Should().Contain(node => node.ElementId == "Participant_Us");
        result.Models.Single(model => model.Id == "Process_Them").Views.Should().ContainSingle();
    }

    [Fact]
    public void Shape_without_bounds_is_reported_and_the_position_is_generated()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                         xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                         xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                         id="Definitions_NoBounds" targetNamespace="urn:x">
              <process id="Process_NoBounds" isExecutable="false">
                <startEvent id="StartEvent_A"/>
                <task id="Task_A"/>
                <sequenceFlow id="Flow_A" sourceRef="StartEvent_A" targetRef="Task_A"/>
              </process>
              <bpmndi:BPMNDiagram id="Diagram_A">
                <bpmndi:BPMNPlane id="Plane_A" bpmnElement="Process_NoBounds">
                  <bpmndi:BPMNShape id="StartEvent_A_di" bpmnElement="StartEvent_A">
                    <dc:Bounds x="100" y="100" width="36" height="36"/>
                  </bpmndi:BPMNShape>
                  <bpmndi:BPMNShape id="Task_A_di" bpmnElement="Task_A"/>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;

        var result = new BpmnXmlImporter().Import(xml);
        var model = SingleModel(result);
        result.Losses.Should().Contain(loss => loss.Construct == "dc:Bounds" && loss.Reason.Contains("Task_A"));
        result.Warnings.Should().Contain(warning => warning.Message.Contains("Task_A"));

        var view = SingleView(model);
        Node(view, "StartEvent_A").X.Should().Be(100);
        var generated = Node(view, "Task_A");
        generated.Width.Should().Be(100);
        generated.X.Should().NotBe(0, "the position is generated, not dropped");
    }

    [Fact]
    public void Di_is_ignored_when_import_is_disabled_and_auto_layout_takes_over()
    {
        var result = Import("simple-process.bpmn", options => options.ImportBpmndi = false);
        var model = SingleModel(result);
        var view = SingleView(model);

        view.Id.Should().Be("auto");
        result.Warnings.Should().Contain(warning => warning.Message.Contains("ImportBpmndi=false"));
        Node(view, "StartEvent_1").X.Should().NotBe(172, "stored DI positions are ignored");

        var withDi = SingleView(SingleModel(Import("simple-process.bpmn")));
        view.Nodes.Should().HaveCount(withDi.Nodes.Count);
    }

    [Fact]
    public void No_di_document_gets_a_full_auto_layout_with_a_warning()
    {
        var result = Import("no-bpmndi.bpmn");
        var model = SingleModel(result);
        var view = SingleView(model);

        view.Id.Should().Be("auto");
        view.Nodes.Should().HaveCount(model.Elements.Count);
        view.Connections.Should().HaveCount(model.Relationships.Count);
        result.Warnings.Should().Contain(warning => warning.Message.Contains("no BPMNDI diagram data"));
    }

    [Fact]
    public void Imported_views_render_through_the_modeling_generator_with_the_stored_geometry()
    {
        var model = SingleModel(Import("simple-process.bpmn"));
        var generator = new ModelingDiagramGenerator(
            new BpmnViewImportStencilMapper(), CreateRegistry());

        var generated = generator.Generate(
            model, new ModelingDiagramGenerationOptionsDto { ViewId = model.Views[0].Id });

        var start = generated.Document.Pages[0].Nodes.Single(
            node => node.Data.GetValueOrDefault("modelElementId") as string == "StartEvent_1");
        start.X.Should().Be(172);
        start.Y.Should().Be(152);
    }

    private static DiagramStencilRegistry CreateRegistry()
    {
        var registry = new DiagramStencilRegistry();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        return registry;
    }

    /// <summary>Minimal bpmn2 stencil mapping for the headless generator check (the full catalog lives in the Modeling package).</summary>
    private sealed class BpmnViewImportStencilMapper : IModelingStencilMapper
    {
        public string? GetStencilId(string notationKey, string semanticType) =>
            notationKey == "bpmn2"
                ? semanticType switch
                {
                    "StartEvent" => "bpmn2.event.start",
                    "EndEvent" => "bpmn2.event.end",
                    "Task" => "bpmn2.task",
                    "UserTask" => "bpmn2.task.user",
                    "ExclusiveGateway" => "bpmn2.gateway.exclusive",
                    _ => null,
                }
                : null;

        public string? GetEdgeStencilId(string notationKey, string relationshipType) =>
            notationKey == "bpmn2" && relationshipType is "SequenceFlow" or "ConditionalFlow" or "DefaultFlow"
                ? "bpmn2.flow.sequence"
                : null;
    }
}
