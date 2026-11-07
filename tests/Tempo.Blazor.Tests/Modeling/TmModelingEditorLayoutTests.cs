using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 4 / decision <c>MODELING-AUTHORING-CHANGESET</c> §9.3/§9.4: the LAYOUT + HIERARCHY authoring
/// surface of <see cref="TmModelingEditor"/> — a node move/resize and a waypoint drag reach
/// <see cref="ModelingViewDto"/> and the pending <c>ViewChanges</c>, a task moved into another
/// lane/pool changes the semantic <c>ParentId</c> and the layout parent in ONE transaction, a lane
/// grows around its content, and the "Arrange" action (LR/TB) is a single undoable step. Without the
/// <see cref="ModelingModelWriterCapabilities.Layout"/> capability nothing is written (§3.2 rule 7).
/// </summary>
public sealed class TmModelingEditorLayoutTests : LocalizationTestBase
{
    private readonly ModelingEditorTestClock _clock = new();

    public TmModelingEditorLayoutTests()
    {
        Services.AddTempoBlazorModeling();

        // The shared test base pins a BARE DiagramStencilRegistry, so the BPMN stencils the real
        // application resolves are registered here (the last registration wins) — the pool/lane
        // BANDS (swimlane data) and the stencil default sizes the arrangement reads come from them.
        Services.AddSingleton(_ =>
        {
            var registry = new Tempo.Blazor.Components.Diagram.Stencils.DiagramStencilRegistry();
            registry.RegisterProvider(new Tempo.Blazor.Components.Diagram.Stencils.BuiltInDiagramStencilProvider());
            registry.RegisterProvider(new Tempo.Blazor.Components.Diagram.Stencils.Bpmn2DiagramStencilProvider());
            return registry;
        });
        Services.AddSingleton<TimeProvider>(_clock);
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new AuthoringModelingModelProvider()));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new LanesModelingModelProvider()));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new LargeModelingModelProvider()));
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new FractionalModelingModelProvider()));
    }

    // ── Layout persistence (§9.4) ───────────────────────────────────────────

    [Fact]
    public async Task Moving_a_node_writes_the_view_geometry_and_the_pending_change_set()
    {
        var writer = AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        // Inside the lane's band, so this test isolates the MOVE (the growth case is covered below).
        await MoveNodeAsync(cut, "task-a", x: 240, y: 180);

        var viewNode = ViewNode(cut, "task-a");
        viewNode.X.Should().Be(240);
        viewNode.Y.Should().Be(180);

        var pending = cut.Instance.TestPendingChangeSet!;
        pending.ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().ContainSingle(change =>
                change.ElementId == "task-a" && change.X == 240 && change.Y == 180);
        pending.HasViewChanges.Should().BeTrue();
        cut.Instance.HasPendingModelChanges.Should().BeTrue("the layout is an unsaved change");
        cut.Instance.TestLayoutCommitCount.Should().Be(1, "one gesture is one transaction");
        cut.Instance.TestStructureAnnouncement.Should().NotBeNull();

        // The pending set reaches the writer's SaveChangesAsync.
        await cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync());
        writer.Calls.Should().ContainSingle();
        writer.Calls[0].ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().ContainSingle(change => change.ElementId == "task-a" && change.X == 240);
    }

    [Fact]
    public async Task Resizing_a_node_updates_its_width_and_height()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await MoveNodeAsync(cut, "task-a", x: 110, y: 120, width: 240, height: 140);

        var viewNode = ViewNode(cut, "task-a");
        viewNode.Width.Should().Be(240);
        viewNode.Height.Should().Be(140);
        cut.Instance.TestPendingChangeSet!.ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().ContainSingle(change => change.Width == 240 && change.Height == 140);
    }

    [Fact]
    public async Task Dragging_a_waypoint_writes_the_view_connection_and_undo_restores_it()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            var edge = document.Edges.Single(candidate => candidate.Id == "flow-approve-end");
            edge.Waypoints = [new DiagramPoint(260, 160), new DiagramPoint(300, 170)];
            edge.IsManuallyRouted = true;
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });

        var connection = cut.Instance.TestModel!.Views[0].Connections.Single(item => item.RelationshipId == "flow-approve-end");
        connection.Waypoints.Should().HaveCount(2);
        connection.Waypoints[0].X.Should().Be(260);
        cut.Instance.TestLayoutCommitCount.Should().Be(1);

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Views[0].Connections.Single(item => item.RelationshipId == "flow-approve-end")
            .Waypoints.Should().BeEmpty("one undo removes the manual routing");
        cut.Instance.TestDocument!.Edges.Single(candidate => candidate.Id == "flow-approve-end")
            .Waypoints.Should().BeEmpty("the regenerated document follows the restored view");
    }

    [Fact]
    public async Task A_layout_gesture_is_one_undo_step_and_undo_restores_the_geometry()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await MoveNodeAsync(cut, "task-a", x: 240, y: 180);
        CutNode(cut, "task-a").X.Should().Be(240);

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());

        ViewNode(cut, "task-a").X.Should().Be(110);
        CutNode(cut, "task-a").X.Should().Be(110, "the regenerated document shows the restored layout");
        cut.Instance.HasPendingModelChanges.Should().BeFalse();
    }

    [Fact]
    public async Task Geometry_is_not_persisted_without_the_layout_capability()
    {
        AddWriter(ModelingModelWriterCapabilities.Content | ModelingModelWriterCapabilities.Structure);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await MoveNodeAsync(cut, "task-a", x: 240, y: 180);

        ViewNode(cut, "task-a").X.Should().Be(110, "a Structure-only writer keeps geometry session-only");
        cut.Instance.TestLayoutCommitCount.Should().Be(0);
        cut.Instance.TestPendingChangeSet!.ViewChanges.Should().BeEmpty();
        cut.FindAll("[data-testid='modeling-arrange']").Should().BeEmpty("no Layout capability, no arrange action");
        cut.Find("[data-testid='modeling-editor']").GetAttribute("data-can-edit-layout").Should().Be("false");
    }

    [Fact]
    public async Task A_second_document_change_without_a_gesture_does_not_commit_twice()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await MoveNodeAsync(cut, "task-a", x: 240, y: 180);
        await cut.InvokeAsync(async () => await cut.Instance.SyncStructureFromDocumentAsync(cut.Instance.TestDocument!));

        cut.Instance.TestLayoutCommitCount.Should().Be(1, "the baseline advanced with the gesture");
    }

    // ── Hierarchy (§9.3) ────────────────────────────────────────────────────

    [Fact]
    public async Task Moving_a_task_into_another_lane_changes_the_semantic_and_layout_parent_in_one_step()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-b");

        // task-b lives directly in "Audit pool"; the drop lands inside "Sales" (lane of the other pool).
        var laneNode = CutNode(cut, "lane-sales");
        await MoveNodeAsync(cut, "task-b", x: laneNode.X + 40, y: laneNode.Y + 30);

        var element = cut.Instance.TestModel!.Elements.Single(item => item.Id == "task-b");
        element.ParentId.Should().Be("lane-sales", "the semantic hierarchy follows the drop");
        ViewNode(cut, "task-b").ParentNodeId.Should().Be("lane-sales", "the layout parent follows in the same transaction");

        var pending = cut.Instance.TestPendingChangeSet!;
        pending.Elements.Should().ContainSingle(change => change.ElementId == "task-b" && change.ParentId == "lane-sales");
        pending.ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().ContainSingle(change => change.ElementId == "task-b" && change.ParentNodeId == "lane-sales");

        // ONE undo restores the semantic parent AND the layout position.
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Elements.Single(item => item.Id == "task-b").ParentId.Should().Be("pool-audit");
        ViewNode(cut, "task-b").ParentNodeId.Should().Be("pool-audit");
    }

    [Fact]
    public async Task Moving_a_flow_node_across_pools_is_refused_with_the_engine_explanation()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        // task-a is the source of a SequenceFlow; landing it in the OTHER pool would make that flow
        // cross a pool boundary — the engine's Authoring post-validation refuses the batch.
        var auditNode = CutNode(cut, "pool-audit");
        await MoveNodeAsync(cut, "task-a", x: auditNode.X + 120, y: auditNode.Y + 60);

        cut.Instance.TestModel!.Elements.Single(item => item.Id == "task-a").ParentId.Should().Be("lane-sales",
            "a rejected re-parent keeps the semantic hierarchy");
        ViewNode(cut, "task-a").Y.Should().Be(120, "the layout is not persisted either");
        cut.Instance.TestStructureErrorText.Should().NotBeNull();
        cut.Instance.TestStructureErrorText.Should().Contain("cross Pool", "the rules' own explanation is shown");
        CutNode(cut, "task-a").Y.Should().Be(120, "the rejected move is visually reverted");
    }

    [Fact]
    public async Task A_lane_grows_around_a_child_that_no_longer_fits()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        var lane = CutNode(cut, "lane-sales");
        var originalHeight = ViewNode(cut, "lane-sales").Height;

        // The task's CENTRE stays inside the lane (so it is still the lane's child) while its bottom
        // edge leaves the band — the lane must stretch, not clip it.
        await MoveNodeAsync(cut, "task-a", x: 110, y: lane.Y + lane.H - 50);

        var grown = ViewNode(cut, "lane-sales");
        grown.Height.Should().BeGreaterThan(originalHeight, "the lane stretches around its content");
        cut.Instance.TestPendingChangeSet!.ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().Contain(change => change.ElementId == "lane-sales");
    }

    // ── Arrange (auto-layout LR/TB, §9.4) ──────────────────────────────────

    [Fact]
    public async Task Arrange_lays_lanes_out_as_bands_and_flow_nodes_in_a_row()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        cut.Find("[data-testid='modeling-arrange']").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Instance.OpenArrangeDialog());
        cut.Instance.ArrangeDialogOpen.Should().BeTrue();
        cut.Find("[data-testid='modeling-arrange-dialog']").Should().NotBeNull();

        await cut.InvokeAsync(() => cut.Instance.TestSetArrangeDirectionAsync("LR"));
        await cut.InvokeAsync(() => cut.Instance.ConfirmArrangeAsync());

        cut.Instance.ArrangeDialogOpen.Should().BeFalse();
        cut.Instance.TestLayoutCommitCount.Should().Be(1);

        var pool = ViewNode(cut, "pool-pool");
        var lane = ViewNode(cut, "lane-sales");
        var audit = ViewNode(cut, "pool-audit");
        var task = ViewNode(cut, "task-a");
        var end = ViewNode(cut, "end-a");

        lane.Y.Should().BeGreaterThanOrEqualTo(pool.Y, "lanes stack inside their pool (flush at the band top)");
        audit.Y.Should().BeGreaterThan(lane.Y + lane.Height, "the second pool sits below the first");
        task.X.Should().BeLessThan(end.X, "flow nodes lie left to right");
        task.Y.Should().Be(end.Y);
        task.X.Should().BeGreaterThan(lane.X, "the flow row starts inside the lane's content area");
        lane.Height.Should().BeGreaterThanOrEqualTo(task.Height);

        cut.Instance.TestPendingChangeSet!.ViewChanges.Should().NotBeEmpty();
        cut.Instance.TestStructureAnnouncement.Should().Contain("left to right", "the announcement lower-cases the direction label");

        // ONE undo restores every position.
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        ViewNode(cut, "task-a").X.Should().Be(110);
        ViewNode(cut, "pool-pool").Y.Should().Be(40);
    }

    [Fact]
    public async Task Arrange_top_to_bottom_stacks_the_flow_nodes_of_a_lane()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await cut.InvokeAsync(() => cut.Instance.OpenArrangeDialog());
        await cut.InvokeAsync(() => cut.Instance.TestSetArrangeDirectionAsync("TB"));
        await cut.InvokeAsync(() => cut.Instance.ConfirmArrangeAsync());

        var task = ViewNode(cut, "task-a");
        var end = ViewNode(cut, "end-a");
        task.X.Should().Be(end.X);
        end.Y.Should().BeGreaterThan(task.Y, "top to bottom stacks the flow nodes");
        cut.Instance.TestStructureAnnouncement.Should().Contain("top to bottom", "the announcement lower-cases the direction label");
    }

    [Fact]
    public async Task Arrange_is_refused_without_the_layout_capability()
    {
        AddWriter(ModelingModelWriterCapabilities.Content);
        using var cut = RenderEditor();

        cut.FindAll("[data-testid='modeling-arrange']").Should().BeEmpty();

        // Even a direct call cannot bypass the capability gate.
        await cut.InvokeAsync(() => cut.Instance.TestConfirmArrangeAsync());
        cut.Instance.TestLayoutCommitCount.Should().Be(0);
    }

    // ── Review A1: a Modeling load keeps the persisted (unsnapped) geometry ─

    [Fact]
    public async Task A_modeling_load_never_re_snaps_the_persisted_geometry()
    {
        // The embedded editors pass SnapDocumentOnLoad="false": the canvas keeps an X of 123 that a
        // grid snap would have moved to 120, so opening the editor is never a layout edit.
        AddWriter(ModelingModelWriterCapabilities.All, FractionalModelingModelProvider.Key);
        using var cut = Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, FractionalModelingModelProvider.Key);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
        });

        var documentNode = cut.Instance.TestDocument!.Nodes.Single(node => NodeElementId(node) == "task-fractional");
        documentNode.X.Should().Be(123, "the persisted layout must not be re-snapped on load");
        documentNode.Y.Should().Be(47);
        cut.Instance.TestModel!.Views[0].Nodes.Single(node => node.ElementId == "task-fractional")
            .X.Should().Be(123, "nor may the load write a normalised position back to the model");
        cut.Instance.HasPendingModelChanges.Should().BeFalse("opening the editor is not an edit");
        cut.Instance.TestLayoutCommitCount.Should().Be(0);
    }

    // ── Review A5: the drag set is TRANSITIVE ──────────────────────────────

    [Fact]
    public async Task Moving_a_pool_lane_and_task_by_the_same_delta_re_parents_nothing()
    {
        // The JS drag set now collects the whole SUBTREE (pool > lane > task). The C# diff sees all
        // three moved by the same delta: every node keeps its container, so no update_element is
        // produced — a pool drag is a pure layout move.
        AddWriter(ModelingModelWriterCapabilities.All, LanesModelingModelProvider.Key);
        using var cut = RenderLanesEditor();
        await SelectElementAsync(cut, "task-validate");

        const double dx = 60;
        const double dy = 20;
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            foreach (var elementId in new[] { "pool-lane", "lane-sales", "lane-warehouse", "task-validate", "task-ship" })
            {
                var node = document.Nodes.Single(candidate => NodeElementId(candidate) == elementId);
                node.X += dx;
                node.Y += dy;
            }

            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.TestPendingChangeSet!.Elements.Should().BeEmpty(
            "moving a container and its content together is not a hierarchy change");
        ViewNode(cut, "task-validate").ParentNodeId.Should().Be("lane-sales");
        ViewNode(cut, "lane-sales").ParentNodeId.Should().Be("pool-lane");
        ViewNode(cut, "pool-lane").X.Should().Be(100, "every node of the dragged subtree moved");
        ViewNode(cut, "task-validate").X.Should().Be(150 + dx);
    }

    // ── Review A3: a Layout-only writer cannot re-parent ───────────────────

    [Fact]
    public async Task A_layout_only_writer_persists_geometry_without_changing_the_parent()
    {
        // Content | Layout (no Structure): the drag crosses into another lane, but the semantic
        // hierarchy is NOT the writer's to change — only X/Y/W/H land in the change set.
        AddWriter(ModelingModelWriterCapabilities.Content | ModelingModelWriterCapabilities.Layout);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-b");

        var laneNode = CutNode(cut, "lane-sales");
        // A genuinely NEW position on both axes (task-b starts at 110,410 — a move that kept X
        // would diff as a Y-only change and the assertion would read `X = null`).
        await MoveNodeAsync(cut, "task-b", x: laneNode.X + 60, y: laneNode.Y + 30);
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.TestModel!.Elements.Single(item => item.Id == "task-b").ParentId
            .Should().Be("pool-audit", "a Layout-only writer never edits the hierarchy");
        var pending = cut.Instance.TestPendingChangeSet!;
        pending.Elements.Should().BeEmpty("no update_element is produced");
        pending.ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().ContainSingle(change =>
                change.ElementId == "task-b" && change.X == laneNode.X + 60 && change.Y == laneNode.Y + 30);
    }

    // ── Review A4: only a MOVED node re-resolves its container ─────────────

    [Fact]
    public async Task Shrinking_a_lane_does_not_re_parent_its_children()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "lane-sales");

        // A container RESIZE (no position change) must never re-resolve the children's parents —
        // shrinking the band under "end-a" would otherwise re-parent it to the pool.
        await MoveNodeAsync(cut, "lane-sales", x: 70, y: 70, width: 320, height: 120);
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.TestPendingChangeSet!.Elements.Should().BeEmpty(
            "a lane resize emits no update_element for its children");
        ViewNode(cut, "end-a").ParentNodeId.Should().Be("lane-sales");
        ViewNode(cut, "task-a").ParentNodeId.Should().Be("lane-sales");
    }

    [Fact]
    public async Task A_node_that_only_resized_keeps_its_baseline_layout_parent()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await MoveNodeAsync(cut, "task-a", x: 110, y: 120, width: 240, height: 120);

        ViewNode(cut, "task-a").ParentNodeId.Should().Be("lane-sales",
            "a resize keeps the layout parent the baseline resolved");
        cut.Instance.TestPendingChangeSet!.Elements.Should().BeEmpty();
    }

    // ── Review A6: Arrange clears the old routing ──────────────────────────

    [Fact]
    public async Task Arrange_clears_the_waypoints_of_a_routed_connection_and_undo_restores_them()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        // A manually routed flow (the pre-arrange geometry).
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            var edge = document.Edges.Single(candidate => candidate.Id == "flow-approve-end");
            edge.Waypoints = [new DiagramPoint(260, 160), new DiagramPoint(300, 170)];
            edge.IsManuallyRouted = true;
            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });
        cut.Instance.TestModel!.Views[0].Connections.Single(item => item.RelationshipId == "flow-approve-end")
            .Waypoints.Should().HaveCount(2);

        await cut.InvokeAsync(() => cut.Instance.OpenArrangeDialog());
        await cut.InvokeAsync(() => cut.Instance.TestConfirmArrangeAsync());
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.TestModel!.Views[0].Connections.Single(item => item.RelationshipId == "flow-approve-end")
            .Waypoints.Should().BeEmpty("the arranged scene drops the bends of the old geometry");
        cut.Instance.TestDocument!.Edges.Single(candidate => candidate.Id == "flow-approve-end")
            .Waypoints.Should().BeEmpty("the regenerated document follows the cleared routing");

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Views[0].Connections.Single(item => item.RelationshipId == "flow-approve-end")
            .Waypoints.Should().HaveCount(2, "one undo restores the routing with the geometry");
    }

    // ── Review U4: flush lanes grow their pool and push the next lane ──────

    [Fact]
    public async Task Growing_a_lane_pushes_the_next_lane_and_grows_the_pool_in_one_transaction()
    {
        AddWriter(ModelingModelWriterCapabilities.All, LanesModelingModelProvider.Key);
        using var cut = RenderLanesEditor();
        await SelectElementAsync(cut, "task-validate");

        var lane = CutNode(cut, "lane-sales");
        // The view DTOs are LIVE references (the command mutates them), so the "before" values must be
        // captured as numbers, not as DTOs.
        var poolHeightBefore = ViewNode(cut, "pool-lane").Height;
        var warehouseYBefore = ViewNode(cut, "lane-warehouse").Y;
        var commitsBefore = cut.Instance.TestLayoutCommitCount;

        // The task's CENTRE stays inside the Sales lane, while its bottom leaves the band.
        await MoveNodeAsync(cut, "task-validate", x: 150, y: 130);
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        var sales = ViewNode(cut, "lane-sales");
        var warehouse = ViewNode(cut, "lane-warehouse");
        sales.Height.Should().BeGreaterThan(lane.H, "the lane stretches around its content");
        warehouse.Y.Should().BeGreaterThan(warehouseYBefore,
            "the following lane is pushed down by the growth delta, so the bands stay flush");
        warehouse.Y.Should().Be(sales.Y + sales.Height, "flush: no gap between the lane bands");
        warehouse.Width.Should().Be(sales.Width, "sibling lanes are equalized");
        ViewNode(cut, "pool-lane").Height.Should().BeGreaterThan(poolHeightBefore,
            "the ancestor pool grows around the grown lanes");
        cut.Instance.TestLayoutCommitCount.Should().Be(commitsBefore + 1, "one gesture is one transaction");
    }

    // ── Review U5: the non-drag "Move to container" ────────────────────────

    [Fact]
    public async Task Move_to_container_is_one_layout_transaction_and_announces_the_container()
    {
        AddWriter(ModelingModelWriterCapabilities.All, LanesModelingModelProvider.Key);
        using var cut = RenderLanesEditor();
        await SelectElementAsync(cut, "task-validate");

        cut.Find("[data-testid='modeling-move-to-container']").Should().NotBeNull();
        await cut.InvokeAsync(() => cut.Instance.TestOpenMoveToContainerAsync());
        cut.Instance.MoveToContainerDialogOpen.Should().BeTrue();
        cut.Find("[data-testid='modeling-move-to-container-dialog']").Should().NotBeNull();

        // The candidate list excludes the element itself (and its subtree) and the root is the
        // first option of the select.
        cut.Instance.TestMoveToContainerChoices.Select(choice => choice.Value)
            .Should().Contain("lane-warehouse").And.NotContain("task-validate");

        await cut.InvokeAsync(() => cut.Instance.TestSetMoveToContainerAsync("lane-warehouse"));
        var commitsBefore = cut.Instance.TestLayoutCommitCount;
        await cut.InvokeAsync(() => cut.Instance.TestConfirmMoveToContainerAsync());
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.MoveToContainerDialogOpen.Should().BeFalse();
        cut.Instance.TestLayoutCommitCount.Should().Be(commitsBefore + 1, "one command, one undo step");
        cut.Instance.TestModel!.Elements.Single(element => element.Id == "task-validate").ParentId
            .Should().Be("lane-warehouse");
        ViewNode(cut, "task-validate").ParentNodeId.Should().Be("lane-warehouse");
        var moved = CutNode(cut, "task-validate");
        moved.Y.Should().BeGreaterThanOrEqualTo(ViewNode(cut, "lane-warehouse").Y,
            "the task lands inside the target lane's content area");
        cut.Instance.TestStructureAnnouncement.Should().Contain("Warehouse",
            "the hierarchy change names the container it moved into");

        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        cut.Instance.TestModel!.Elements.Single(element => element.Id == "task-validate").ParentId
            .Should().Be("lane-sales", "one undo restores the semantic parent");
    }

    [Fact]
    public async Task Move_to_container_can_move_an_element_back_to_the_root()
    {
        AddWriter(ModelingModelWriterCapabilities.All, LanesModelingModelProvider.Key);
        using var cut = RenderLanesEditor();
        await SelectElementAsync(cut, "task-validate");

        await cut.InvokeAsync(() => cut.Instance.TestOpenMoveToContainerAsync());
        await cut.InvokeAsync(() => cut.Instance.TestSetMoveToContainerAsync(string.Empty));
        await cut.InvokeAsync(() => cut.Instance.TestConfirmMoveToContainerAsync());
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.TestModel!.Elements.Single(element => element.Id == "task-validate").ParentId
            .Should().BeEmpty();
        ViewNode(cut, "task-validate").ParentNodeId.Should().BeEmpty();
        cut.Instance.TestStructureAnnouncement.Should().Contain("out of its container",
            "the root announcement explains what happened in words");
    }

    [Fact]
    public async Task Move_to_container_is_hidden_without_the_layout_capability()
    {
        AddWriter(
            ModelingModelWriterCapabilities.Content | ModelingModelWriterCapabilities.Structure,
            LanesModelingModelProvider.Key);
        using var cut = RenderLanesEditor();

        cut.FindAll("[data-testid='modeling-move-to-container']").Should().BeEmpty();
        await cut.InvokeAsync(() => cut.Instance.TestOpenMoveToContainerAsync());
        cut.Instance.MoveToContainerDialogOpen.Should().BeFalse();
    }

    // ── Review U6: keyboard-nudge coalescing ───────────────────────────────

    [Fact]
    public async Task Two_nudges_of_the_same_element_are_one_undo_step()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-a");

        await MoveNodeAsync(cut, "task-a", x: 240, y: 180);
        await MoveNodeAsync(cut, "task-a", x: 248, y: 188);
        await cut.InvokeAsync(() => cut.Instance.TestFlushLayoutAnnouncementAsync());

        cut.Instance.TestLayoutCommitCount.Should().Be(1, "the burst is one transaction (U6)");
        ViewNode(cut, "task-a").X.Should().Be(248, "the merged command carries the LAST geometry");
        cut.Instance.TestPendingChangeSet!.ViewChanges.Should().ContainSingle()
            .Which.Nodes.Should().ContainSingle(change => change.ElementId == "task-a" && change.X == 248);

        // The BEFORE snapshot of the burst is kept: one undo returns to the pre-burst geometry.
        await cut.InvokeAsync(() => cut.Instance.UndoModelEditAsync());
        ViewNode(cut, "task-a").X.Should().Be(110);
    }

    [Fact]
    public async Task A_nudge_burst_across_a_parent_change_starts_a_new_step()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();
        await SelectElementAsync(cut, "task-b");

        // A re-parent always announces immediately and never merges into the previous step.
        var laneNode = CutNode(cut, "lane-sales");
        await MoveNodeAsync(cut, "task-b", x: laneNode.X + 40, y: laneNode.Y + 30);

        cut.Instance.TestLayoutCommitCount.Should().Be(1);
        cut.Instance.TestStructureAnnouncement.Should().Contain("Sales",
            "a hierarchy change is announced immediately, naming the container");
        cut.Instance.TestModel!.Elements.Single(element => element.Id == "task-b").ParentId.Should().Be("lane-sales");
    }

    // ── Review U8: the dialog offers Arrange instead of the DE Layout menu ─

    [Fact]
    public async Task The_open_editor_dialog_replaces_the_de_layout_group_with_the_arrange_action()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.FindAll("[data-testid='modeling-open-diagram-editor'] [data-tm-group='layout']").Should().BeEmpty(
            "the persistent layout is arranged by the editor, not by the browser dagre menu (U8)");
        cut.Find("[data-testid='modeling-dialog-arrange']").Should().NotBeNull(
            "Arrange is the dialog-scoped replacement");
        cut.Find("[data-testid='modeling-dialog-move-to-container']").Should().NotBeNull();
    }

    [Fact]
    public async Task The_dialog_announces_route_through_the_in_dialog_live_region()
    {
        AddWriter(ModelingModelWriterCapabilities.All);
        using var cut = RenderEditor();

        cut.FindAll("[data-testid='modeling-structure-live']").Should().ContainSingle(
            "the outer live region is the only one while the dialog is closed");

        cut.Find("[data-testid='modeling-open-in-editor-button']").Click();
        cut.WaitForAssertion(() => cut.Find("[data-testid='modeling-open-diagram-editor']").Should().NotBeNull());

        cut.FindAll("[data-testid='modeling-dialog-structure-live']").Should().ContainSingle(
            "the live region moves inside the open dialog (U7)");
        cut.FindAll("[data-testid='modeling-structure-live']").Should().BeEmpty(
            "the outer region is suppressed while the dialog is open");
    }

    // ── Review A8: one move sync of a 300-element model ────────────────────

    [Fact]
    public async Task One_move_sync_of_a_three_hundred_element_model_stays_under_the_budget()
    {
        // The per-sync lookup context (element map, children index, area-sorted containers) replaced
        // the FirstOrDefault scans inside ResolveLayoutParentElementId / DescendantElementIds /
        // AddContainerGrowth — the sync of one move must stay well under 200 ms.
        AddWriter(ModelingModelWriterCapabilities.All);
        using var editor = Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, LargeModelingModelProvider.Key);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
        });

        editor.Instance.TestDocument.Should().NotBeNull();
        editor.Instance.TestDocument!.Nodes.Should().HaveCountGreaterThan(300);

        await SelectElementAsync(editor, "pool-0-lane-0-task-0");

        // Warm-up on ANOTHER element: the first call in a process pays the JIT of the layout path,
        // which is not what the budget measures (and a different element set keeps the two steps
        // from coalescing).
        var commitsBefore = editor.Instance.TestLayoutCommitCount;
        await MoveNodeAsync(editor, "pool-0-lane-0-task-1", x: 258, y: 152);
        await editor.InvokeAsync(() => editor.Instance.TestFlushLayoutAnnouncementAsync());

        await MoveNodeAsync(editor, "pool-0-lane-0-task-0", x: 260, y: 150);

        editor.Instance.TestLayoutCommitCount.Should().Be(commitsBefore + 2);
        // The seam measures the DIFF + COMMIT BUILD only — the bUnit render of the 300-node canvas
        // that follows is the renderer's cost, not the sync's (the quadratic lookups the per-sync
        // context removed lived in the diff).
        editor.Instance.TestLastLayoutSyncDuration.Should().BeLessThan(TimeSpan.FromMilliseconds(200),
            $"one move sync of 300 elements must stay under the budget (was {editor.Instance.TestLastLayoutSyncDuration.TotalMilliseconds:F1} ms)");
    }

    // ── Performance edge case (300 elements) ───────────────────────────────

    [Fact]
    public void Arrange_stays_responsive_for_three_hundred_elements()
    {
        // Plan phase 4 edge case: ~300 elements (3 pools x 5 lanes x 19 flow nodes = 303) must
        // arrange in one pass without a quadratic blow-up — the arranger measures each container
        // ONCE and the placement walks the tree once.
        const int pools = 3;
        const int lanesPerPool = 5;
        const int tasksPerLane = 19;
        var elements = new List<ModelingElementDto>();
        var nodes = new List<DiagramNode>();

        for (var poolIndex = 0; poolIndex < pools; poolIndex++)
        {
            var poolId = $"pool-{poolIndex}";
            elements.Add(new ModelingElementDto { Id = poolId, SemanticType = "Pool", Name = poolId });
            nodes.Add(SwimlaneNode(poolId, "bpmn2.pool", 40, 60 + (poolIndex * 400), 900, 380));

            for (var laneIndex = 0; laneIndex < lanesPerPool; laneIndex++)
            {
                var laneId = $"{poolId}-lane-{laneIndex}";
                elements.Add(new ModelingElementDto { Id = laneId, SemanticType = "Lane", Name = laneId, ParentId = poolId });
                nodes.Add(SwimlaneNode(laneId, "bpmn2.lane", 70, 100 + (poolIndex * 400) + (laneIndex * 70), 840, 70));

                for (var taskIndex = 0; taskIndex < tasksPerLane; taskIndex++)
                {
                    var taskId = $"{laneId}-task-{taskIndex}";
                    elements.Add(new ModelingElementDto { Id = taskId, SemanticType = "UserTask", Name = taskId, ParentId = laneId });
                    nodes.Add(new DiagramNode
                    {
                        Id = taskId,
                        X = 100,
                        Y = 100,
                        W = 160,
                        H = 60,
                        Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["modelElementId"] = taskId }
                    });
                }
            }
        }

        elements.Should().HaveCount(303);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var boxes = ModelingLayoutArranger.Arrange(
            elements,
            nodes,
            [],
            semanticType => semanticType is "Pool" or "Lane",
            ModelingLayoutArranger.Direction.LeftToRight);
        stopwatch.Stop();

        boxes.Should().HaveCount(303);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "arranging 300 elements is O(n)");
        boxes.Should().OnlyContain(box => box.Width > 0 && box.Height > 0);
    }

    private static DiagramNode SwimlaneNode(string id, string stencilId, double x, double y, double w, double h)
        => new()
        {
            Id = id,
            StencilId = stencilId,
            X = x,
            Y = y,
            W = w,
            H = h,
            SwimlaneData = new DiagramNodeSwimlaneData
            {
                IsHorizontal = true,
                RowCount = 1,
                ColumnCount = 1,
                HeaderSize = 30,
                CellLabels = [id]
            },
            Data = new Dictionary<string, object>(StringComparer.Ordinal) { ["modelElementId"] = id }
        };

    // ── Helpers ─────────────────────────────────────────────────────────────

    private AuthoringModelingModelWriter AddWriter(
        ModelingModelWriterCapabilities capabilities,
        string providerKey = AuthoringModelingModelProvider.Key)
    {
        var writer = new AuthoringModelingModelWriter(providerKey, capabilities);
        Services.AddSingleton<IModelingModelWriter>(writer);
        return writer;
    }

    private IRenderedComponent<TmModelingEditor> RenderEditor()
        => Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, AuthoringModelingModelProvider.Key);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
        });

    private IRenderedComponent<TmModelingEditor> RenderLanesEditor()
        => Render<TmModelingEditor>(builder =>
        {
            builder.Add(component => component.ProviderKey, LanesModelingModelProvider.Key);
            builder.Add(component => component.AllowModelEditing, true);
            builder.Add(component => component.WriterCapabilities, ModelingModelWriterCapabilities.All);
            builder.Add(component => component.OnModelChanged,
                EventCallback.Factory.Create<ModelingModelChangedEventArgs>(this, _ => { }));
        });

    private static async Task SelectElementAsync(IRenderedComponent<TmModelingEditor> cut, string elementId)
    {
        await cut.InvokeAsync(() => cut.Instance.TestSelectElementForTestAsync(elementId));
        cut.WaitForAssertion(() => cut.Instance.TestSelectedElementId.Should().Be(elementId));
    }

    /// <summary>Applies one geometry gesture to the document and lets the editor's diff run.</summary>
    private static async Task MoveNodeAsync(
        IRenderedComponent<TmModelingEditor> cut,
        string elementId,
        double x,
        double y,
        double? width = null,
        double? height = null)
    {
        await cut.InvokeAsync(async () =>
        {
            var document = cut.Instance.TestDocument!;
            var node = document.Nodes.Single(candidate => NodeElementId(candidate) == elementId);
            node.X = x;
            node.Y = y;
            if (width is { } w)
            {
                node.W = w;
            }

            if (height is { } h)
            {
                node.H = h;
            }

            await cut.Instance.SyncStructureFromDocumentAsync(document);
        });
    }

    private static DiagramNode CutNode(IRenderedComponent<TmModelingEditor> cut, string elementId)
        => cut.Instance.TestDocument!.Nodes.Single(candidate => NodeElementId(candidate) == elementId);

    private static ModelingViewNodeDto ViewNode(IRenderedComponent<TmModelingEditor> cut, string elementId)
        => cut.Instance.TestModel!.Views[0].Nodes.Single(node => node.ElementId == elementId);

    private static string? NodeElementId(DiagramNode node)
        => node.Data.TryGetValue("modelElementId", out var value) ? value?.ToString() : null;
}

/// <summary>Phase 4 demo scene as a test provider: ONE pool with two FLUSH lanes and one task each —
/// the scene the lane-growth and move-to-container reviews need (the authoring model has one lane).</summary>
internal sealed class LanesModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.lanes";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var model = new ModelingModelDto
        {
            Id = "modeling-editor-lanes",
            Title = "BPMN lanes",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Unit tests",
                SourceVersion = "2026.08",
                IsFresh = true
            },
            Elements =
            [
                new() { Id = "pool-lane", Notation = "bpmn2", SemanticType = "Pool", Name = "Order handling" },
                new() { Id = "lane-sales", Notation = "bpmn2", SemanticType = "Lane", Name = "Sales", ParentId = "pool-lane" },
                new() { Id = "lane-warehouse", Notation = "bpmn2", SemanticType = "Lane", Name = "Warehouse", ParentId = "pool-lane" },
                new() { Id = "task-validate", Notation = "bpmn2", SemanticType = "UserTask", Name = "Validate order", ParentId = "lane-sales" },
                new() { Id = "task-ship", Notation = "bpmn2", SemanticType = "ServiceTask", Name = "Ship order", ParentId = "lane-warehouse" }
            ],
            Views =
            [
                new()
                {
                    Id = "lanes-view",
                    Name = "Order handling",
                    Notation = "bpmn2",
                    Nodes =
                    [
                        new() { ElementId = "pool-lane", X = 40, Y = 60, Width = 900, Height = 280 },
                        new() { ElementId = "lane-sales", X = 70, Y = 60, Width = 870, Height = 140, ParentNodeId = "pool-lane" },
                        new() { ElementId = "lane-warehouse", X = 70, Y = 200, Width = 870, Height = 140, ParentNodeId = "pool-lane" },
                        new() { ElementId = "task-validate", X = 150, Y = 80, Width = 160, Height = 92, ParentNodeId = "lane-sales" },
                        new() { ElementId = "task-ship", X = 150, Y = 220, Width = 160, Height = 92, ParentNodeId = "lane-warehouse" }
                    ]
                }
            ]
        };

        return Task.FromResult(model);
    }
}

/// <summary>Review A1: ONE element at a deliberate off-grid position (123, 47), so a Modeling load
/// proves the embedded canvas does not re-snap the persisted layout.</summary>
internal sealed class FractionalModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.fractional";

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new ModelingModelDto
        {
            Id = "modeling-editor-fractional",
            Title = "Fractional layout",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Unit tests",
                SourceVersion = "2026.09",
                IsFresh = true
            },
            Elements =
            [
                new()
                {
                    Id = "task-fractional",
                    Notation = "bpmn2",
                    SemanticType = "UserTask",
                    Name = "Off-grid task"
                }
            ],
            Views =
            [
                new()
                {
                    Id = "fractional-view",
                    Name = "Off-grid",
                    Notation = "bpmn2",
                    Nodes = [new() { ElementId = "task-fractional", X = 123, Y = 47, Width = 161, Height = 91 }]
                }
            ]
        });
    }
}

/// <summary>Review A8: a 300+ element model (3 pools × 5 lanes × 19 tasks = 303) with every element
/// positioned, so ONE move sync can be timed.</summary>
internal sealed class LargeModelingModelProvider : IModelingModelProvider
{
    public const string Key = "tempo.tests.modeling.large";

    /// <summary>3 pools × 5 lanes × 19 tasks = 303 elements.</summary>
    public const int ElementCount = 3 * 5 * 19;

    public string ProviderKey => Key;

    public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var elements = new List<ModelingElementDto>();
        var nodes = new List<ModelingViewNodeDto>();
        for (var pool = 0; pool < 3; pool++)
        {
            var poolId = $"pool-{pool}";
            elements.Add(new ModelingElementDto { Id = poolId, Notation = "bpmn2", SemanticType = "Pool", Name = poolId });
            nodes.Add(new ModelingViewNodeDto { ElementId = poolId, X = 40, Y = 60 + (pool * 400), Width = 900, Height = 380 });
            for (var lane = 0; lane < 5; lane++)
            {
                var laneId = $"{poolId}-lane-{lane}";
                elements.Add(new ModelingElementDto
                {
                    Id = laneId,
                    Notation = "bpmn2",
                    SemanticType = "Lane",
                    Name = laneId,
                    ParentId = poolId
                });
                nodes.Add(new ModelingViewNodeDto
                {
                    ElementId = laneId,
                    X = 70,
                    Y = 60 + (pool * 400) + (lane * 76),
                    Width = 870,
                    Height = 76,
                    ParentNodeId = poolId
                });
                for (var task = 0; task < 19; task++)
                {
                    var taskId = $"{laneId}-task-{task}";
                    elements.Add(new ModelingElementDto
                    {
                        Id = taskId,
                        Notation = "bpmn2",
                        SemanticType = "UserTask",
                        Name = taskId,
                        ParentId = laneId
                    });
                    nodes.Add(new ModelingViewNodeDto
                    {
                        ElementId = taskId,
                        X = 110 + (task * 40),
                        Y = 62 + (pool * 400) + (lane * 76),
                        Width = 160,
                        Height = 60,
                        ParentNodeId = laneId
                    });
                }
            }
        }

        var model = new ModelingModelDto
        {
            Id = "modeling-editor-large",
            Title = "Large authoring model",
            Notation = "bpmn2",
            SupportedNotations = ["bpmn2"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Unit tests",
                SourceVersion = "2026.08",
                IsFresh = true
            },
            Elements = elements,
            Views = [new ModelingViewDto { Id = "large-view", Notation = "bpmn2", Nodes = nodes }]
        };

        return Task.FromResult(model);
    }
}
