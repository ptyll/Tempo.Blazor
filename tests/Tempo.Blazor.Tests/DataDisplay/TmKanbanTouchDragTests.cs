using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Plan 92e96b25, phase 5 (TG-005, user decision O7): the board's TOUCH drag rides the shared
/// <c>pointer-drag.js</c> helper (long-press to start, so a tap and a page scroll keep working) and
/// commits through two <c>[JSInvokable]</c> entry points — one for the drop, one for the cancel
/// (pointercancel / Escape / a second finger), which rolls back and is announced. The drop reports
/// the source <c>(column, lane, index)</c> triple PLUS the card's STABLE key (its index in the
/// host's full list — verified at release, so a host refresh under the gesture cancels instead of
/// mis-moving) and the target by the FULL-LIST index of the card before which it lands (<c>-1</c> =
/// append), so a <c>VirtualizeCards</c> cell reads the same slot as a fully-rendered one. HTML5
/// drag and drop stays the mouse path; the "Move…" menu stays the WCAG 2.5.7 alternative.
/// </summary>
public class TmKanbanTouchDragTests : LocalizationTestBase
{
    private record TouchTask(int Id, string Title, string Status, string? Assignee);

    private static readonly IReadOnlyList<KanbanColumn> Columns =
    [
        new("todo", "To Do"),
        new("doing", "In Progress"),
        new("done", "Done", MaxItems: 2),
    ];

    private static readonly IReadOnlyList<TouchTask> Tasks =
    [
        new(1, "Task A", "todo", "Alice"),
        new(2, "Task B", "todo", "Bob"),
        new(3, "Task C", "doing", "Alice"),
        new(4, "Task D", "done", null),
    ];

    private static RenderFragment<TouchTask> CardTemplate => task => builder
        => builder.AddContent(0, task.Title);

    private IRenderedComponent<TmKanbanBoard<TouchTask>> RenderClassicBoard(
        Action<ComponentParameterCollectionBuilder<TmKanbanBoard<TouchTask>>>? extra = null)
        => Render<TmKanbanBoard<TouchTask>>(p =>
        {
            p.Add(x => x.Columns, Columns)
                .Add(x => x.Items, Tasks)
                .Add(x => x.ColumnSelector, t => t.Status)
                .Add(x => x.CardTemplate, CardTemplate)
                .Add(x => x.CardLabelSelector, t => t.Title);
            extra?.Invoke(p);
        });

    private IRenderedComponent<TmKanbanBoard<TouchTask>> RenderSwimlaneBoard(
        Action<ComponentParameterCollectionBuilder<TmKanbanBoard<TouchTask>>>? extra = null)
        => Render<TmKanbanBoard<TouchTask>>(p =>
        {
            p.Add(x => x.Columns, Columns)
                .Add(x => x.Items, Tasks)
                .Add(x => x.ColumnSelector, t => t.Status)
                .Add(x => x.SwimlaneSelector, t => t.Assignee)
                .Add(x => x.CardTemplate, CardTemplate)
                .Add(x => x.CardLabelSelector, t => t.Title);
            extra?.Invoke(p);
        });

    // ══════════════════════════════════════════════════════════════════
    // DOM contract: the module's selectors (data-tm-kanban-*)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void Classic_Zones_And_Cards_CarryTheModuleSelectors()
    {
        var cut = RenderClassicBoard();

        var column = cut.Find("[data-testid='board-column-todo']");
        column.GetAttribute("data-tm-kanban-column").Should().Be("todo");
        column.GetAttribute("data-tm-kanban-lane").Should().BeNull("the classic layout has no lanes");

        var cards = column.QuerySelectorAll("[data-tm-kanban-card]");
        cards.Length.Should().Be(2);
        cards[0].GetAttribute("data-tm-kanban-index").Should().Be("0");
        cards[1].GetAttribute("data-tm-kanban-index").Should().Be("1");

        // The STABLE key: the card's index in the host's FULL list — the identity the release
        // report is verified against.
        cards[0].GetAttribute("data-tm-kanban-key").Should().Be("0");
        cards[1].GetAttribute("data-tm-kanban-key").Should().Be("1");
    }

    [Fact]
    public void Swimlane_Cells_And_Slots_CarryColumnAndLaneIdentity()
    {
        // The slots of a lane render only while the lane is collapsed.
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.CollapsedLanes, [TmKanbanBoard<TouchTask>.NoSwimlaneId]));

        var cell = cut.Find("[data-testid='cell-todo-Alice']");
        cell.GetAttribute("data-tm-kanban-column").Should().Be("todo");
        cell.GetAttribute("data-tm-kanban-lane").Should().Be("Alice");

        // The trailing "no value" lane is addressed by the public sentinel, never the internal NUL id.
        var noLaneId = TmKanbanBoard<TouchTask>.NoSwimlaneId;
        var slot = cut.Find("[data-testid='lane-slot-todo-none']");
        slot.GetAttribute("data-tm-kanban-column").Should().Be("todo");
        slot.GetAttribute("data-tm-kanban-lane").Should().Be(noLaneId);
        slot.GetAttribute("data-tm-kanban-lane").Should().NotContain("\0");
    }

    // ══════════════════════════════════════════════════════════════════
    // The drop commit (called from tm-kanban.js on pointer release)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TouchDrop_CrossColumn_ResolvesTheCard_AndReportsTheSameContractAsAHtml5Drop()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        // "Task B" (todo, cell index 1, full-list key 1) is dropped at the end of doing (append).
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 1, 1, "doing", null, -1));

        moved.Should().NotBeNull();
        moved!.Item.Title.Should().Be("Task B");
        moved.FromColumn.Should().Be("todo");
        moved.ToColumn.Should().Be("doing");
        moved.TargetIndex.Should().Be(1, "the insertion slot at the end of the one-card doing cell");
        moved.TargetBeforeItem.Should().BeNull("beforeIndex -1 appends");
    }

    [Fact]
    public async Task TouchDrop_BeforeAnotherCard_ReportsItAsTheAnchor()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        // "Task C" (doing, index 0, key 2) dropped before "Task B" — todo's card at the FULL-LIST
        // index 1 (after "Task A").
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("doing", null, 0, 2, "todo", null, 1));

        moved.Should().NotBeNull();
        moved!.Item.Title.Should().Be("Task C");
        moved.TargetBeforeItem.Should().NotBeNull();
        moved.TargetBeforeItem!.Title.Should().Be("Task B");
        moved.TargetIndex.Should().Be(1);
    }

    [Fact]
    public async Task TouchDrop_BeforeIndexFromAVirtualizedWindow_UsesTheAnchorNotTheWindowPosition()
    {
        // The B1 fix: a VirtualizeCards cell renders only a WINDOW of its cards, so the module can
        // only hover cards whose data-tm-kanban-index is a slice of the cell's list — and reports
        // that CELL index as the anchor. The board resolves it against the cell's full list, so the
        // same slot reads whether the cell is windowed or fully rendered.
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        // "Task D" is the done cell's card at index 0 (its data-tm-kanban-index — while its stable
        // KEY is 3, the position in the host's full list; the two must not be confused). Dropping
        // before it anchors on D.
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 0, "done", null, 0));

        moved.Should().NotBeNull();
        moved!.Item.Title.Should().Be("Task A");
        moved.TargetBeforeItem.Should().NotBeNull();
        moved.TargetBeforeItem!.Title.Should().Be("Task D");
        moved.TargetIndex.Should().Be(0);
    }

    [Theory]
    [InlineData(42)]
    [InlineData(3)]
    public async Task TouchDrop_BeforeIndexOutOfRange_OrPointingAtTheDraggedCard_Appends(int beforeIndex)
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        KanbanMoveEvent<TouchTask>? reordered = null;
        var cut = RenderClassicBoard(p => p
            .Add(x => x.OnItemMoved, e => moved = e)
            .Add(x => x.OnItemReordered, e => reordered = e));

        // Out of range — 42 is plainly past the cell, 3 is the dragged card's stable KEY (its
        // host-list position), which is NOT a cell index: the done cell holds one card. Both append.
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 0, "done", null, beforeIndex));
        moved.Should().NotBeNull();
        moved!.TargetBeforeItem.Should().BeNull();
        moved.TargetIndex.Should().Be(1);

        // Same-cell drop whose anchor resolves to the dragged card itself (a stale report): the
        // card cannot anchor on itself → append after the cell's cards.
        moved = null;
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 0, "todo", null, 0));
        reordered.Should().NotBeNull("a same-cell touch drop reorders");
        reordered!.Item.Title.Should().Be("Task A");
        reordered.TargetBeforeItem.Should().BeNull("before itself appends — the cell counts the card");
        reordered.TargetIndex.Should().Be(2);
        moved.Should().BeNull("OnItemMoved is not raised for a same-cell drop");
    }

    [Fact]
    public async Task TouchDrop_WithinTheSameCell_RaisesOnItemReordered()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        KanbanMoveEvent<TouchTask>? reordered = null;
        var cut = RenderClassicBoard(p => p
            .Add(x => x.OnItemMoved, e => moved = e)
            .Add(x => x.OnItemReordered, e => reordered = e));

        // "Task A" (index 0 of todo) moved below "Task B" (append, beforeIndex -1).
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 0, "todo", null, -1));

        reordered.Should().NotBeNull("a same-cell touch drop reorders");
        reordered!.Item.Title.Should().Be("Task A");
        reordered.TargetBeforeItem.Should().BeNull("after the last card of the cell");
        moved.Should().BeNull("OnItemMoved is not raised for a same-cell drop");
    }

    [Fact]
    public async Task TouchDrop_InSwimlanes_CarriesTheLane_AndMapsTheNoValueSentinelToNull()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        // "Task D" (done, no-value lane, cell index 0, stable key 3) dropped into (todo, Alice)
        // before its first card.
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop(
            "done", TmKanbanBoard<TouchTask>.NoSwimlaneId, 0, 3, "todo", "Alice", 0));

        moved.Should().NotBeNull();
        moved!.Item.Title.Should().Be("Task D");
        moved.FromSwimlane.Should().BeNull("the no-value lane maps to null");
        moved.ToSwimlane.Should().Be("Alice");
        moved.TargetBeforeItem.Should().NotBeNull();
        moved.TargetBeforeItem!.Title.Should().Be("Task A");
    }

    [Fact]
    public async Task TouchDrop_AnnouncesTheMoveThroughTheOnePoliteLiveRegion()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, _ => { }));

        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 1, 1, "doing", null, -1));

        var status = cut.Find("[data-testid='kanban-status']");
        status.TextContent.Should().Be("Task B moved to In Progress");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public async Task TouchDrop_WithAnUnknownSourceIndex_RaisesNothing(int fromIndex)
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        KanbanMoveEvent<TouchTask>? reordered = null;
        var cut = RenderClassicBoard(p => p
            .Add(x => x.OnItemMoved, e => moved = e)
            .Add(x => x.OnItemReordered, e => reordered = e));

        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, fromIndex, 1, "doing", null, 0));

        moved.Should().BeNull();
        reordered.Should().BeNull();
    }

    [Fact]
    public async Task TouchDrop_OnADisabledBoard_RaisesNothing()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p
            .Add(x => x.Disabled, true)
            .Add(x => x.OnItemMoved, e => moved = e));

        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 0, "doing", null, 0));

        moved.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════
    // The stable key: a host refresh under the gesture cancels instead of mis-moving
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TouchDrop_WithAKeyMismatch_Cancels_AndAnnouncesTheRollback()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        // The module pressed "Task A" (todo, index 0) but its key 2 now addresses "Task C" — the
        // host list changed under the gesture. Nothing moves; the key names the card to announce.
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 2, "doing", null, -1));

        moved.Should().BeNull("a mismatched key cancels the drop");
        var status = cut.Find("[data-testid='kanban-status']");
        status.TextContent.Should().Be("Move of Task C cancelled — the card stayed in In Progress");
    }

    [Fact]
    public async Task TouchDrop_WithAnOutOfRangeKey_Cancels_NamingTheIndexCard()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 0, 42, "doing", null, -1));

        moved.Should().BeNull("a key outside the list cancels the drop");
        var status = cut.Find("[data-testid='kanban-status']");
        status.TextContent.Should().Be("Move of Task A cancelled — the card stayed in To Do");
    }

    [Fact]
    public async Task TouchDrop_WithoutAKey_LegacyMarkup_StillResolves()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        // A card without the data-tm-kanban-key attribute reports -1: the verification is skipped.
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDrop("todo", null, 1, -1, "doing", null, -1));

        moved.Should().NotBeNull();
        moved!.Item.Title.Should().Be("Task B");
    }

    [Fact]
    public async Task TouchCancel_WithAKeyMismatch_NamesTheKeyedCardsCurrentCell()
    {
        var cut = RenderClassicBoard();

        // The press named todo/0 but the key addresses "Task C", now in doing: the announcement
        // names the keyed card and the cell it actually stayed in.
        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDragCancelled("todo", null, 0, 2));

        var status = cut.Find("[data-testid='kanban-status']");
        status.TextContent.Should().Be("Move of Task C cancelled — the card stayed in In Progress");
    }

    // ══════════════════════════════════════════════════════════════════
    // The cancel (pointercancel / Escape / a second finger) rolls back and is announced
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TouchCancel_RaisesNoMove_AnnouncesTheRollback_NamesTheCardAndItsCell()
    {
        KanbanMoveEvent<TouchTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDragCancelled("todo", null, 1, 1));

        moved.Should().BeNull("a cancelled gesture commits nothing");
        var status = cut.Find("[data-testid='kanban-status']");
        status.TextContent.Should().Be("Move of Task B cancelled — the card stayed in To Do");
    }

    [Fact]
    public async Task TouchCancel_WithoutAMatchingCard_AnnouncesNothing()
    {
        var cut = RenderClassicBoard();

        await cut.InvokeAsync(() => cut.Instance.OnKanbanTouchDragCancelled("todo", null, 42, 1));

        var status = cut.Find("[data-testid='kanban-status']");
        status.TextContent.Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════
    // The collapsed lane slot names its (column, lane) target — the hint during a drag
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void LaneSlot_NamesItsColumnAndLaneTarget()
    {
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.CollapsedLanes, [TmKanbanBoard<TouchTask>.NoSwimlaneId]));

        var slot = cut.Find("[data-testid='lane-slot-todo-none']");
        slot.GetAttribute("title").Should().Be("To Do in lane No value");
        slot.GetAttribute("aria-label").Should().Be("To Do in lane No value");
    }

    [Fact]
    public void LaneSlot_IsAGroup_LikeTheCollapsedColumnStrip()
    {
        var cut = RenderSwimlaneBoard(p =>
            p.Add(x => x.CollapsedLanes, [TmKanbanBoard<TouchTask>.NoSwimlaneId, "Alice"]));

        cut.Find("[data-testid='lane-slot-todo-none']").GetAttribute("role").Should().Be("group");
        cut.Find("[data-testid='lane-slot-done-Alice']").GetAttribute("role").Should().Be("group");
    }

    [Fact]
    public void LaneSlot_RendersTheColumnColorStrip_WhenTheColumnHasAColor()
    {
        var columns = new[] { new KanbanColumn("todo", "To Do", "var(--tm-kanban-column-todo)") };
        var cut = Render<TmKanbanBoard<TouchTask>>(p => p
            .Add(x => x.Columns, columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.SwimlaneSelector, t => t.Assignee)
            .Add(x => x.CardTemplate, CardTemplate)
            .Add(x => x.CollapsedLanes, ["Alice"]));

        var strip = cut.Find(".tm-kanban__slot-color");
        strip.GetAttribute("style").Should().Contain("background-color: var(--tm-kanban-column-todo)");
    }
}
