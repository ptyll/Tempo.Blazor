using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 4 / decision <c>MODELING-AUTHORING-CHANGESET</c> §10: the BPMN VISUAL CONVENTIONS the
/// modeling stencil mapper guarantees — every catalog type resolves to an existing stencil, a pool
/// and a lane are horizontal BANDS whose name lives in a left label band, a task shows its type
/// icon, events differ by their ring, gateways carry their marker and data objects use the document
/// / cylinder shapes. Every BPMN node shape is also painted with the canonical CSS TOKENS (never a
/// hard-coded hex), so the dark theme and a high-contrast theme recolour it.
/// </summary>
public sealed class BpmnStencilMapperConventionsTests : LocalizationTestBase
{
    private static readonly BpmnStencilMapper Mapper = new();

    public BpmnStencilMapperConventionsTests()
    {
        var registry = Services.GetRequiredService<DiagramStencilRegistry>();
        registry.RegisterProvider(new BuiltInDiagramStencilProvider());
        registry.RegisterProvider(new Bpmn2DiagramStencilProvider());
        registry.RegisterProvider(new Archimate3DiagramStencilProvider());
    }

    // ── Mapping completeness ────────────────────────────────────────────────

    [Fact]
    public void Every_catalog_element_type_maps_to_an_existing_node_stencil()
    {
        var registry = ResolveRegistry();
        BpmnModelingCatalog.ElementTypes.Should().NotBeEmpty();

        foreach (var semanticType in BpmnModelingCatalog.ElementTypes)
        {
            var stencilId = Mapper.GetStencilId("bpmn2", semanticType);
            stencilId.Should().NotBeNull($"'{semanticType}' is a shipped BPMN element type");
            var stencil = registry.GetStencil(stencilId!);
            stencil.Should().NotBeNull($"the mapped stencil '{stencilId}' must be registered");
            stencil!.Kind.Should().Be(DiagramStencilKind.Node, $"'{semanticType}' is an element");
        }
    }

    [Fact]
    public void Every_catalog_relationship_type_maps_to_an_existing_edge_stencil()
    {
        var registry = ResolveRegistry();
        BpmnModelingCatalog.RelationshipTypes.Should().NotBeEmpty();

        foreach (var relationshipType in BpmnModelingCatalog.RelationshipTypes)
        {
            var stencilId = Mapper.GetEdgeStencilId("bpmn2", relationshipType);
            stencilId.Should().NotBeNull($"'{relationshipType}' is a shipped BPMN relationship type");
            var stencil = registry.GetStencil(stencilId!);
            stencil.Should().NotBeNull($"the mapped edge stencil '{stencilId}' must be registered");
            stencil!.Kind.Should().Be(DiagramStencilKind.Edge, $"'{relationshipType}' is a relationship");
        }
    }

    [Fact]
    public void Legacy_bpmn_notation_key_and_case_insensitive_types_are_supported()
    {
        Mapper.GetStencilId("bpmn", "userTask").Should().Be("bpmn2.task.user");
        Mapper.GetStencilId("bpmn2", "USERTASK").Should().Be("bpmn2.task.user");
        Mapper.GetStencilId("archimate", "UserTask").Should().BeNull("another notation never borrows BPMN shapes");
    }

    // ── Pool / lane bands ──────────────────────────────────────────────────

    [Fact]
    public void Pool_and_lane_are_horizontal_bands_with_a_left_label_band()
    {
        var registry = ResolveRegistry();

        foreach (var (semanticType, stencilId) in new[] { ("Pool", "bpmn2.pool"), ("Lane", "bpmn2.lane") })
        {
            var stencil = registry.GetStencil(stencilId)!;
            Mapper.GetStencilId("bpmn2", semanticType).Should().Be(stencilId);
            stencil.IsSwimlane.Should().BeTrue($"{semanticType} is a swimlane container");
            stencil.Layout.BackgroundShape.Should().Be("swimlane-horizontal", "BPMN lanes run horizontally");
            stencil.Layout.Sections.Should().Contain(section => section.Type == "swimlane",
                "the label band is the stencil's swimlane section, not a component overlay");
            stencil.DefaultWidth.Should().BeGreaterThan(300, "a band spans the pool's width");
        }
    }

    // ── Task type icons ────────────────────────────────────────────────────

    [Theory]
    [InlineData("UserTask", "bpmn2.task.user", "tm-bpmn-task-marker-user")]
    [InlineData("ServiceTask", "bpmn2.task.service", "tm-bpmn-task-marker-service")]
    [InlineData("ManualTask", "bpmn2.task.manual", "tm-bpmn-task-marker-manual")]
    [InlineData("ScriptTask", "bpmn2.task.script", "tm-bpmn-task-marker-script")]
    [InlineData("BusinessRuleTask", "bpmn2.task.business-rule", "tm-bpmn-task-marker-business-rule")]
    [InlineData("SendTask", "bpmn2.task.send", "tm-bpmn-task-marker-send")]
    [InlineData("ReceiveTask", "bpmn2.task.receive", "tm-bpmn-task-marker-receive")]
    public void Task_types_carry_their_type_marker(string semanticType, string stencilId, string markerClass)
    {
        var stencil = ResolveRegistry().GetStencil(stencilId)!;
        Mapper.GetStencilId("bpmn2", semanticType).Should().Be(stencilId);
        stencil.Layout.ShapeSvg.Should().Contain(markerClass, "the type icon is the BPMN convention");
    }

    [Fact]
    public void A_plain_task_has_no_type_marker()
    {
        var stencil = ResolveRegistry().GetStencil(Mapper.GetStencilId("bpmn2", "Task")!)!;
        stencil.Layout.ShapeSvg.Should().NotContain("tm-bpmn-task-marker");
    }

    // ── Gateway markers ────────────────────────────────────────────────────

    [Theory]
    [InlineData("ExclusiveGateway", "M33 33 L67 67", "the X marker")]
    [InlineData("ParallelGateway", "M50 27 V73", "the plus marker")]
    [InlineData("InclusiveGateway", "r='22'", "the inclusive circle")]
    [InlineData("ComplexGateway", "M50 26 V74", "the complex star")]
    public void Gateways_carry_their_marker(string semanticType, string markerFragment, string because)
    {
        var stencil = ResolveRegistry().GetStencil(Mapper.GetStencilId("bpmn2", semanticType)!)!;
        stencil.Layout.BackgroundShape.Should().Be("diamond", "a BPMN gateway is a diamond");
        stencil.Layout.ShapeSvg.Should().Contain(markerFragment, because);
    }

    [Fact]
    public void Event_based_gateway_carries_its_marker()
    {
        var stencil = ResolveRegistry().GetStencil(Mapper.GetStencilId("bpmn2", "EventBasedGateway")!)!;
        stencil.Layout.ShapeSvg.Should().Contain("M50 31 L56 44", "the event-based pentagon inside the double ring");
    }

    // ── Events by type ─────────────────────────────────────────────────────

    [Fact]
    public void Events_differ_by_their_ring_and_marker()
    {
        var registry = ResolveRegistry();

        // Start: one thin ring, no inner ring.
        var start = registry.GetStencil(Mapper.GetStencilId("bpmn2", "StartEvent")!)!;
        start.Layout.BackgroundShape.Should().Be("ellipse");
        CountOccurrences(start.Layout.ShapeSvg, "<circle").Should().Be(1);
        start.Layout.ShapeSvg.Should().Contain("stroke-width='2'");

        // Intermediate: an outer plus an inner ring.
        var intermediate = registry.GetStencil(Mapper.GetStencilId("bpmn2", "IntermediateCatchEvent")!)!;
        CountOccurrences(intermediate.Layout.ShapeSvg, "<circle").Should().Be(2);

        // End: the thick ring.
        var end = registry.GetStencil(Mapper.GetStencilId("bpmn2", "EndEvent")!)!;
        end.Layout.ShapeSvg.Should().Contain("stroke-width='5'");

        // Boundary: the dashed (non-interrupting) ring.
        var boundary = registry.GetStencil(Mapper.GetStencilId("bpmn2", "BoundaryEvent")!)!;
        boundary.Layout.ShapeSvg.Should().Contain("stroke-dasharray");
    }

    [Theory]
    [InlineData("bpmn2.event.message", "tm-bpmn-event-marker-message")]
    [InlineData("bpmn2.event.timer", "tm-bpmn-event-marker-timer")]
    [InlineData("bpmn2.event.error", "tm-bpmn-event-marker-error")]
    [InlineData("bpmn2.event.signal", "tm-bpmn-event-marker-signal")]
    [InlineData("bpmn2.event.terminate", "tm-bpmn-event-marker-terminate")]
    public void Typed_event_stencils_ship_their_definition_marker(string stencilId, string markerClass)
    {
        var stencil = ResolveRegistry().GetStencil(stencilId);
        stencil.Should().NotBeNull($"{stencilId} is part of the shipped BPMN palette");
        stencil!.Layout.ShapeSvg.Should().Contain(markerClass);
    }

    // ── Data objects ───────────────────────────────────────────────────────

    [Fact]
    public void Data_objects_use_the_document_and_cylinder_shapes()
    {
        var registry = ResolveRegistry();

        var dataObject = registry.GetStencil(Mapper.GetStencilId("bpmn2", "DataObject")!)!;
        dataObject.Layout.BackgroundShape.Should().Be("document");
        dataObject.Layout.ShapeSvg.Should().Contain("L100 22 V100", "the folded corner is the document convention");

        var dataStore = registry.GetStencil(Mapper.GetStencilId("bpmn2", "DataStore")!)!;
        dataStore.Layout.BackgroundShape.Should().Be("cylinder");
        dataStore.Layout.ShapeSvg.Should().Contain("<ellipse");
    }

    // ── Canvas paint tokens (review A7) ────────────────────────────────────

    [Fact]
    public void Every_bpmn_node_stencil_is_painted_with_the_canvas_tokens()
    {
        var nodeStencils = new Bpmn2DiagramStencilProvider()
            .GetStencilSets()
            .SelectMany(set => set.Stencils)
            .Where(stencil => stencil.Kind == DiagramStencilKind.Node)
            .ToList();

        nodeStencils.Should().NotBeEmpty();

        foreach (var stencil in nodeStencils)
        {
            stencil.Layout.Fill.Should().StartWith("var(--tm-diagram-paper",
                $"{stencil.Id} must resolve its fill from the canvas PAPER token");
            stencil.Layout.Stroke.Should().StartWith("var(--tm-diagram-node-stroke",
                $"{stencil.Id} must resolve its stroke from the canvas STROKE token");
            stencil.Layout.Fill.Should().Contain("#ffffff",
                "the literal fallback keeps an SVG viewer without the stylesheet white");
            stencil.Layout.StrokeWidth.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void The_canvas_declares_both_paint_tokens()
    {
        var tokens = ReadEditorAsset(Path.Combine("css", "components", "_diagram-editor.css"));

        // The tokens are declared on .tm-diagram-canvas, next to the paper/ink pair the node bodies
        // and edge labels already use, so a host or a high-contrast theme recolours BPMN shapes by
        // overriding them.
        tokens.Should().Contain("--tm-diagram-paper:", "--tm-diagram-paper is the fill token");
        tokens.Should().Contain("--tm-diagram-node-stroke:", "--tm-diagram-node-stroke is the stroke token");
        tokens.Should().Contain("--tm-diagram-node-stroke: var(--tm-paper-ink);",
            "the stroke token resolves the paper ink");
    }

    [Fact]
    public void The_svg_export_carries_both_custom_properties_onto_the_clone_root()
    {
        // A standalone serialized SVG has no stylesheet: the exporter copies the computed values of
        // the canvas-scoped custom properties onto the clone root, so the BPMN fills/strokes survive.
        var script = ReadEditorAsset(Path.Combine("js", "diagram-editor.js"));

        script.Should().Contain("'--tm-diagram-paper'");
        script.Should().Contain("'--tm-diagram-node-stroke'");
        script.Should().Contain("'--tm-diagram-node-ink'");
    }

    [Fact]
    public void The_pool_and_lane_stencils_paint_one_outer_rect_only()
    {
        var registry = ResolveRegistry();

        foreach (var stencilId in new[] { "bpmn2.pool", "bpmn2.lane" })
        {
            var shapeSvg = registry.GetStencil(stencilId)!.Layout.ShapeSvg;
            CountOccurrences(shapeSvg, "<rect").Should().Be(1,
                $"{stencilId} paints only its outer band — the swimlane header and separators are the component's");
            shapeSvg.Should().NotContain("<path", "a separator path here would paint the same line twice");
        }
    }

    [Fact]
    public void The_bpmn_marker_surfaces_stay_reachable_behind_the_native_primitive()
    {
        // U1: the task/event/gateway markers live in the stencil's ShapeSvg, which the native SVG
        // primitive would hide — the stylesheet re-shows it for bpmn2 stencils (and the canvas paints
        // the native primitive with fill/stroke none so nothing is painted twice).
        var css = ReadEditorAsset(Path.Combine("css", "components", "_diagram-editor.css"));
        css.Should().Contain(
            "[data-stencil-id^=\"bpmn2.\"] .tm-diagram-node__svg-bg",
            "the bpmn2 SVG background (task icon, event ring, gateway marker) must be re-shown");
    }
    // ── Helpers ─────────────────────────────────────────────────────────────

    private DiagramStencilRegistry ResolveRegistry() => Services.GetRequiredService<DiagramStencilRegistry>();

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }

        return count;
    }

    /// <summary>Reads a SHIPPED DiagramEditor asset (the contract is verified against the files, not
    /// a copy).</summary>
    private static string ReadEditorAsset(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "src", "Tempo.Blazor.DiagramEditor", "wwwroot", relativePath);
        File.Exists(path).Should().BeTrue($"{path} is a shipped DiagramEditor asset");
        return File.ReadAllText(path);
    }
}
