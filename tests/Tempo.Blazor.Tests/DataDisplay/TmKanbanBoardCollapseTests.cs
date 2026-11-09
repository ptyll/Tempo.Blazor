using System.Reflection;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Contract of plan a98e4a71 — "TmKanbanBoard: even collapsed columns catch cards".
/// <para>
/// Phase 1 wrote these facts RED, phase 2 implemented column collapse and phase 3 the drop zones on
/// collapsed columns and swimlanes (the <c>tm-kanban__swimlane-slots</c> strip, the
/// <c>tm-kanban__lane-slot--drop-target</c> highlight and the root's <c>tm-kanban--dragging</c> state).
/// Nothing here is "written to pass" — every fact is the DOM contract the component owes its hosts.
/// </para>
/// <para>
/// DOM CONTRACT asserted below:
/// </para>
/// <list type="bullet">
/// <item>
/// Toggle: a native <c>button.tm-kanban__column-toggle[type=button]</c> with
/// <c>data-testid="column-toggle-{columnId}"</c>, <c>aria-expanded="true|false"</c> and the exact
/// <c>aria-label</c> <c>"Collapse column {title}"</c> / <c>"Expand column {title}"</c>
/// (<c>TmKanban_CollapseColumn</c>/<c>TmKanban_ExpandColumn</c>). It is a real button — no
/// <c>role</c>/<c>tabindex</c> override, no key emulation. It sits in the column header but OUTSIDE
/// <c>ColumnHeaderTemplate</c>, and it exists in the classic layout as well as in the swimlane
/// layout's <c>colhead-{id}</c>.
/// </item>
/// <item>
/// A collapsed classic column is the same <c>[data-testid="board-column-{id}"]</c> element plus
/// <c>tm-kanban__column--collapsed</c>; the strip carries <c>role="group"</c> and an <c>aria-label</c>
/// with the generated <c>TmKanban_CollapsedColumnSummary</c>, and it keeps the title, the
/// <c>tm-kanban__count</c> badge, the WIP badge and <c>tm-kanban__column--over-limit</c> — but drops
/// <c>tm-kanban__cards</c> and every card (so no <c>&lt;Virtualize&gt;</c> either). An expanded column
/// carries neither the group role nor the summary label.
/// </item>
/// <item>
/// In the swimlane layout a collapsed column adds <c>tm-kanban__column--collapsed</c> to
/// <c>colhead-{id}</c> AND to every <c>cell-{id}-{lane}</c>; the cells stay drop targets.
/// </item>
/// <item>
/// A collapsed swimlane replaces <c>tm-kanban__swimlane-body</c> with
/// <c>tm-kanban__swimlane-slots</c> holding one <c>.tm-kanban__lane-slot</c> per column, test id
/// <c>lane-slot-{columnId}-{laneToken}</c> (<c>none</c> is the "No value" lane, like
/// <c>cell-todo-none</c>). A drop anywhere in a collapsed zone appends.
/// </item>
/// <item>
/// The zone under the drag gets <c>tm-kanban__column--drop-target</c> /
/// <c>tm-kanban__lane-slot--drop-target</c>; <c>ondragend</c> clears it. The board root carries
/// <c>tm-kanban--dragging</c> for the whole gesture (added on <c>dragstart</c>, removed on
/// <c>dragend</c>). A collapsed zone never renders the horizontal
/// <c>.tm-kanban__drop-indicator</c> insertion line — not even a narrowed swimlane cell.
/// </item>
/// <item>
/// <c>CollapsedColumns</c>/<c>CollapsedLanes</c> are host-owned when non-null (the host set wins; a
/// toggle only raises the matching <c>…Changed</c> callback), <c>null</c> keeps the internal state.
/// The exact surface is pinned by reflection: <c>bool ColumnsCollapsible</c>, both sets as
/// <c>IReadOnlyCollection&lt;string&gt;</c>, both callbacks as
/// <c>EventCallback&lt;IReadOnlyCollection&lt;string&gt;&gt;</c>, every one a <c>[Parameter]</c> (D7).
/// The "No value" lane is addressed by the public <c>TmKanbanBoard&lt;TItem&gt;.NoSwimlaneId</c>
/// constant while <c>KanbanMoveEvent.ToSwimlane</c> keeps mapping it to <c>null</c>.
/// </item>
/// <item>
/// <c>Disabled</c> only stops the drop: the column toggle and the swimlane toggle still collapse,
/// the collapsed swimlane still renders its slots, but no drop zone reacts and no callback is raised.
/// </item>
/// </list>
/// <para>
/// HOW THE NEW API IS REFERENCED: every test passes the parameters through the strongly typed
/// <c>p.Add(x =&gt; x.Name, value)</c> and addresses the "No value" lane with the public
/// <c>TmKanbanBoard&lt;TItem&gt;.NoSwimlaneId</c> constant. Phase 3 landed the collapsed-swimlane drop
/// slots and the board root's <c>tm-kanban--dragging</c> state, so every fact here is green.
/// </para>
/// </summary>
public class TmKanbanBoardCollapseTests : LocalizationTestBase
{
    private const string DragStart = "ondragstart";
    private const string DragOver = "ondragover";
    private const string Drop = "ondrop";
    private const string DragEnd = "ondragend";

    private record KanbanTask(int Id, string Title, string Status);

    private static readonly IReadOnlyList<KanbanColumn> Columns =
    [
        new("todo", "To Do", "#3b82f6"),
        new("doing", "In Progress", "#f59e0b"),
        new("done", "Done", "#10b981"),
    ];

    private static readonly IReadOnlyList<KanbanTask> Tasks =
    [
        new(1, "Task A", "todo"),
        new(2, "Task B", "todo"),
        new(3, "Task C", "doing"),
        new(4, "Task D", "done"),
    ];

    private static readonly RenderFragment<KanbanTask> CardFragment =
        t => (RenderFragment)(b => b.AddContent(0, t.Title));

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 1 — ColumnsCollapsible=true renders a toggle per column
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ColumnsCollapsible_True_RendersNativeToggleInEveryClassicColumnHeader()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));

        foreach (var column in Columns)
        {
            var toggle = cut.Find($"[data-testid='board-column-{column.Id}'] .tm-kanban__header " +
                                   $"[data-testid='column-toggle-{column.Id}']");
            toggle.TagName.Should().Be("BUTTON");
            toggle.GetAttribute("type").Should().Be("button", "the toggle is a native button (no key emulation)");
            toggle.GetAttribute("aria-expanded").Should().Be("true");
            toggle.GetAttribute("aria-label").Should().Contain(column.Title);
        }

        cut.FindAll(".tm-kanban__column-toggle").Count.Should().Be(Columns.Count);
    }

    [Fact]
    public void ColumnsCollapsible_True_RendersToggleEvenWithCustomColumnHeaderTemplate()
    {
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.ColumnHeaderTemplate, col => (RenderFragment)(b =>
            {
                b.OpenElement(0, "span");
                b.AddAttribute(1, "class", "custom-col-header");
                b.AddContent(2, col.Title);
                b.CloseElement();
            }));
        });

        // D7: the host template replaces the header CONTENT only — it can never take the toggle away.
        cut.FindAll(".custom-col-header").Count.Should().Be(Columns.Count);
        cut.FindAll(".tm-kanban__column-toggle").Count.Should().Be(Columns.Count);
        cut.FindAll("[data-testid='column-toggle-todo']").Count.Should().Be(1);
    }

    [Fact]
    public void ColumnsCollapsible_True_RendersToggleInSwimlaneColumnHeaders()
    {
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.ColumnsCollapsible, true));

        foreach (var column in Columns)
        {
            cut.FindAll($"[data-testid='colhead-{column.Id}'] [data-testid='column-toggle-{column.Id}']")
                .Count.Should().Be(1);
        }

        cut.FindAll(".tm-kanban__column-toggle").Count.Should().Be(Columns.Count);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 2 — the default is backward compatible: no toggle at all
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ColumnsCollapsible_DefaultFalse_RendersNoToggleAndKeepsClassicMarkup()
    {
        var cut = RenderClassicBoard();

        cut.FindAll(".tm-kanban__column-toggle").Should().BeEmpty();
        cut.FindAll("[data-testid^='column-toggle-']").Should().BeEmpty();
        cut.FindAll(".tm-kanban__column--collapsed").Should().BeEmpty();
        cut.FindAll(".tm-kanban__card").Count.Should().Be(Tasks.Count);
    }

    [Fact]
    public void ColumnsCollapsible_ExplicitFalse_RendersNoToggle()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, false));

        cut.FindAll(".tm-kanban__column-toggle").Should().BeEmpty();
        cut.FindAll("[data-testid^='column-toggle-']").Should().BeEmpty();
        cut.FindAll(".tm-kanban__card").Count.Should().Be(Tasks.Count);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 3 — clicking the toggle collapses the column (D1)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Toggle_CollapsesColumn_HidesCards_KeepsTitleCountAndSummary_AndFlipsAriaExpanded()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));

        cut.Find("[data-testid='board-column-todo'] .tm-kanban__cards").Should().NotBeNull();

        cut.Find("[data-testid='column-toggle-todo']").Click();

        var collapsed = cut.Find("[data-testid='board-column-todo']");
        collapsed.ClassList.Should().Contain("tm-kanban__column--collapsed");
        collapsed.QuerySelectorAll(".tm-kanban__cards").Should().BeEmpty();
        collapsed.QuerySelectorAll(".tm-kanban__card").Should().BeEmpty();
        collapsed.TextContent.Should().Contain("To Do");
        collapsed.QuerySelector(".tm-kanban__count")!.TextContent.Trim().Should().Be("2");
        collapsed.GetAttribute("aria-label").Should().Be("To Do, 2 cards",
            "the narrow strip summarises itself for screen readers (TmKanban_CollapsedColumnSummary)");

        // only the toggled column collapses
        cut.FindAll("[data-testid='board-column-doing'] .tm-kanban__card").Count.Should().Be(1);

        var toggle = cut.Find("[data-testid='column-toggle-todo']");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.GetAttribute("aria-label").Should().Contain("To Do");

        // expanding brings the cards back
        cut.Find("[data-testid='column-toggle-todo']").Click();
        cut.Find("[data-testid='board-column-todo']").ClassList.Should().NotContain("tm-kanban__column--collapsed");
        cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card").Count.Should().Be(2);
        cut.Find("[data-testid='column-toggle-todo']").GetAttribute("aria-expanded").Should().Be("true");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 4 — a collapsed column renders no virtualizer
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_WithVirtualizeCards_RendersNoVirtualizedBody()
    {
        var many = Enumerable.Range(1, 60).Select(i => new KanbanTask(i, $"Task {i}", "todo")).ToList();
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.VirtualizeCards, true);
        }, items: many);

        cut.FindAll("[data-testid='cards-virtual-todo']").Count.Should().Be(1, "the expanded column virtualizes");

        cut.Find("[data-testid='column-toggle-todo']").Click();

        cut.FindAll("[data-testid='cards-virtual-todo']").Should().BeEmpty();
        cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__cards--virtual").Should().BeEmpty();
        cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card").Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 5 — D2: a collapsed column narrows header AND every lane cell
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_InSwimlaneLayout_NarrowsColumnHeaderAndEveryLaneCell_KeepingCellsDroppable()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.OnItemMoved, e => moved = e);
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();

        cut.Find("[data-testid='colhead-todo']").ClassList.Should().Contain("tm-kanban__column--collapsed");

        foreach (var laneToken in new[] { "Alice", "Bob", "none" })
        {
            cut.Find($"[data-testid='cell-todo-{laneToken}']").ClassList
                .Should().Contain("tm-kanban__column--collapsed",
                    "a collapsed column must keep the grid aligned across every lane (cell-todo-{0})", laneToken);
        }

        // sibling columns are untouched
        cut.Find("[data-testid='cell-doing-Alice']").ClassList
            .Should().NotContain("tm-kanban__column--collapsed");

        // the narrowed cell is still a drop target (append)
        cut.Find("[data-testid='cell-doing-Alice'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='cell-todo-Alice']").TriggerEvent(DragOver, new DragEventArgs());
        cut.Find("[data-testid='cell-todo-Alice']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().NotBeNull();
        moved!.ToColumn.Should().Be("todo");
        moved.ToSwimlane.Should().Be("Alice");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 6 — a drop on the collapsed classic column appends
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Drop_OnCollapsedClassicColumn_FiresOnItemMoved_AppendingToTheEnd()
    {
        KanbanMoveEvent<KanbanTask>? moved = null;
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.OnItemMoved, e => moved = e);
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();

        cut.Find("[data-testid='board-column-doing'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='board-column-todo']").TriggerEvent(DragOver, new DragEventArgs());
        cut.Find("[data-testid='board-column-todo']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().NotBeNull("a collapsed column is still a drop target — that is the point of the plan");
        moved!.FromColumn.Should().Be("doing");
        moved.ToColumn.Should().Be("todo");
        moved.TargetIndex.Should().Be(2, "a drop on the collapsed strip appends to the column's card count");
        moved.TargetBeforeItem.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 7 — a drop on the narrowed lane cell appends for (column, lane)
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Drop_OnNarrowedLaneCellOfCollapsedColumn_FiresOnItemMoved_WithColumnLaneAndAppend()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.OnItemMoved, e => moved = e);
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();

        // B lives in (doing, Alice); (todo, Alice) already holds A → append at index 1.
        cut.Find("[data-testid='cell-doing-Alice'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='cell-todo-Alice']").TriggerEvent(DragOver, new DragEventArgs());
        cut.Find("[data-testid='cell-todo-Alice']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().NotBeNull();
        moved!.FromColumn.Should().Be("doing");
        moved.FromSwimlane.Should().Be("Alice");
        moved.ToColumn.Should().Be("todo");
        moved.ToSwimlane.Should().Be("Alice");
        moved.TargetIndex.Should().Be(1);
        moved.TargetBeforeItem.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 8 — D3: a collapsed swimlane renders one drop slot per column
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedSwimlane_RendersOneDropSlotPerColumn_AlignedWithTheColumnHeaders()
    {
        var cut = RenderSwimlaneBoard();

        cut.Find("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").Click();

        var lane = cut.Find("[data-testid='swimlane-Alice']");
        lane.QuerySelectorAll(".tm-kanban__swimlane-body").Should().BeEmpty();
        lane.QuerySelectorAll(".tm-kanban__swimlane-slots").Count.Should().Be(1);

        var slotIds = lane.QuerySelectorAll("[data-testid^='lane-slot-']")
            .Select(e => e.GetAttribute("data-testid"))
            .ToList();
        slotIds.Should().BeEquivalentTo(
            "lane-slot-todo-Alice", "lane-slot-doing-Alice", "lane-slot-done-Alice");

        // other lanes keep their body
        cut.FindAll("[data-testid='swimlane-Bob'] .tm-kanban__swimlane-body").Count.Should().Be(1);
    }

    [Fact]
    public void Drop_InCollapsedLaneSlot_FiresOnItemMoved_WithColumnLaneAndAppend()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        cut.Find("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").Click();

        // C lives in (todo, Bob); (todo, Alice) already holds A → index 1.
        cut.Find("[data-testid='cell-todo-Bob'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-todo-Alice']").TriggerEvent(DragOver, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-todo-Alice']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().NotBeNull("a collapsed swimlane must catch the card in the slot under its column");
        moved!.FromColumn.Should().Be("todo");
        moved.FromSwimlane.Should().Be("Bob");
        moved.ToColumn.Should().Be("todo");
        moved.ToSwimlane.Should().Be("Alice");
        moved.TargetIndex.Should().Be(1);
        moved.TargetBeforeItem.Should().BeNull();
    }

    [Fact]
    public void Drop_InNoValueLaneSlot_FiresOnItemMoved_WithNullSwimlane()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.OnItemMoved, e => moved = e));

        cut.Find("[data-testid='swimlane-none'] .tm-kanban__swimlane-toggle").Click();

        // A lives in (todo, Alice); (todo, none) already holds E → index 1.
        cut.Find("[data-testid='cell-todo-Alice'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-todo-none']").TriggerEvent(DragOver, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-todo-none']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().NotBeNull();
        moved!.ToColumn.Should().Be("todo");
        moved.ToSwimlane.Should().BeNull("the no-value lane keeps mapping to null in KanbanMoveEvent");
        moved.TargetIndex.Should().Be(1);
        moved.TargetBeforeItem.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 9 — D4: the collapsed zone under the drag is highlighted
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dragover_CollapsedColumn_AddsDropTargetClass_AndDragendRemovesIt()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));

        cut.Find("[data-testid='column-toggle-todo']").Click();

        cut.Find("[data-testid='board-column-doing'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='board-column-todo']").TriggerEvent(DragOver, new DragEventArgs());

        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().Contain("tm-kanban__column--drop-target");
        // a collapsed zone never renders the horizontal insertion line
        cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__drop-indicator").Should().BeEmpty();

        cut.Find("[data-testid='board-column-doing'] .tm-kanban__card").TriggerEvent(DragEnd, new DragEventArgs());

        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().NotContain("tm-kanban__column--drop-target");
    }

    [Fact]
    public void Dragover_LaneSlot_AddsDropTargetClass_AndDragendRemovesIt()
    {
        var cut = RenderSwimlaneBoard();

        cut.Find("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").Click();

        cut.Find("[data-testid='cell-todo-Bob'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-doing-Alice']").TriggerEvent(DragOver, new DragEventArgs());

        cut.Find("[data-testid='lane-slot-doing-Alice']").ClassList
            .Should().Contain("tm-kanban__lane-slot--drop-target");

        cut.Find("[data-testid='cell-todo-Bob'] .tm-kanban__card").TriggerEvent(DragEnd, new DragEventArgs());

        cut.Find("[data-testid='lane-slot-doing-Alice']").ClassList
            .Should().NotContain("tm-kanban__lane-slot--drop-target");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 10 — D5/D7: two-way binding, host state wins
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumns_HostSetWins_ColumnStartsCollapsedWithoutAClick()
    {
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.CollapsedColumns, (IReadOnlyCollection<string>)new[] { "doing" });
        });

        cut.Find("[data-testid='board-column-doing']").ClassList.Should().Contain("tm-kanban__column--collapsed");
        cut.FindAll("[data-testid='board-column-doing'] .tm-kanban__card").Should().BeEmpty();
        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().NotContain("tm-kanban__column--collapsed");
        cut.Find("[data-testid='column-toggle-doing']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void Toggle_RaisesCollapsedColumnsChanged_WithTheNewSet_AndHostStateKeepsTheView()
    {
        IReadOnlyCollection<string>? captured = null;
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.CollapsedColumns, (IReadOnlyCollection<string>)Array.Empty<string>());
            p.Add(x => x.CollapsedColumnsChanged,
                EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, set => captured = set));
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();

        captured.Should().NotBeNull();
        captured.Should().BeEquivalentTo(new[] { "todo" });

        // the host set was not updated, so nothing collapsed: host state wins over the internal one
        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().NotContain("tm-kanban__column--collapsed");
    }

    [Fact]
    public void CollapsedLanes_HostSetWins_AndNoValueLaneIsAddressedByNoSwimlaneId()
    {
        var noLaneId = TmKanbanBoard<LaneTask>.NoSwimlaneId;
        noLaneId.Should().NotBeNullOrWhiteSpace();

        var cut = RenderSwimlaneBoard(p => p.Add(x => x.CollapsedLanes, (IReadOnlyCollection<string>)new[] { noLaneId }));

        cut.FindAll("[data-testid='swimlane-none'] .tm-kanban__swimlane-slots").Count.Should().Be(1);
        cut.FindAll("[data-testid='lane-slot-todo-none']").Count.Should().Be(1);
        cut.FindAll("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-body").Count.Should().Be(1);
    }

    [Fact]
    public void ToggleLane_RaisesCollapsedLanesChanged_KeepingTheNoValueSentinel()
    {
        var noLaneId = TmKanbanBoard<LaneTask>.NoSwimlaneId;
        IReadOnlyCollection<string>? captured = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.CollapsedLanes, (IReadOnlyCollection<string>)new[] { noLaneId });
            p.Add(x => x.CollapsedLanesChanged,
                EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, set => captured = set));
        });

        cut.Find("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").Click();

        captured.Should().NotBeNull();
        captured.Should().BeEquivalentTo(new[] { noLaneId, "Alice" });
    }

    [Fact]
    public void NoSwimlaneId_IsAPublicConstantOnTheBoard()
    {
        var field = typeof(TmKanbanBoard<LaneTask>)
            .GetField("NoSwimlaneId", BindingFlags.Public | BindingFlags.Static);

        field.Should().NotBeNull("D5 publishes the no-value lane id as TmKanbanBoard<TItem>.NoSwimlaneId");
        field!.IsLiteral.Should().BeTrue("it must be a constant, so hosts can use it in switch/attribute positions");
        field.GetValue(null).Should().BeOfType<string>().Which.Should().NotBeNullOrWhiteSpace();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 11 — D6: Disabled collapses but never catches a card
    // ══════════════════════════════════════════════════════════════════════

    // Review (phase fze-1): this covers the classic layout. The swimlane layout has its own D6
    // contract — the lane toggle keeps working and the slots still render while the drop stays inert;
    // see Disabled_SwimlaneLayout_LaneToggleStillCollapsesAndRendersSlots_ButDropZonesStayInert.
    [Fact]
    public void Disabled_ToggleStillCollapses_ButCollapsedDropZoneStaysInert()
    {
        KanbanMoveEvent<KanbanTask>? moved = null;
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.Disabled, true);
            p.Add(x => x.OnItemMoved, e => moved = e);
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();
        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().Contain("tm-kanban__column--collapsed", "collapse is pure view state");

        cut.Find("[data-testid='board-column-doing'] .tm-kanban__card").TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='board-column-todo']").TriggerEvent(DragOver, new DragEventArgs());

        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().NotContain("tm-kanban__column--drop-target");

        cut.Find("[data-testid='board-column-todo']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 12 (review) — D7: the public surface is pinned by reflection
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void D7_Parameters_AreDeclaredWithTheContractedTypesAndAreRealParameters()
    {
        var board = typeof(TmKanbanBoard<LaneTask>);

        AssertParameter(board, "ColumnsCollapsible", typeof(bool));
        AssertParameter(board, "CollapsedColumns", typeof(IReadOnlyCollection<string>));
        AssertParameter(board, "CollapsedColumnsChanged",
            typeof(EventCallback<IReadOnlyCollection<string>>));
        AssertParameter(board, "CollapsedLanes", typeof(IReadOnlyCollection<string>));
        AssertParameter(board, "CollapsedLanesChanged",
            typeof(EventCallback<IReadOnlyCollection<string>>));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 13 (review) — the collapsed strip is a named group, not a bare div
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_StripCarriesRoleGroup_AndTheSummaryLabel()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));

        // an expanded column is an anonymous region: no role, no summary label
        var expanded = cut.Find("[data-testid='board-column-todo']");
        expanded.GetAttribute("role").Should().BeNull("only the collapsed strip is a group");
        expanded.GetAttribute("aria-label").Should().BeNull("the expanded column keeps its visible header");

        cut.Find("[data-testid='column-toggle-todo']").Click();

        var strip = cut.Find("[data-testid='board-column-todo']");
        strip.TagName.Should().Be("DIV");
        strip.GetAttribute("role").Should().Be("group",
            "the narrow strip is a named group for assistive technology — not a bare div with a label");
        strip.GetAttribute("aria-label").Should().Be("To Do, 2 cards",
            "the group name is the generated TmKanban_CollapsedColumnSummary");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 14 (review) — the exact toggle labels in BOTH layouts
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ColumnToggle_UsesTheExactCollapseAndExpandLabels_AndFlipsAriaExpanded_InBothLayouts()
    {
        // classic layout
        var classic = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        var classicToggle = classic.Find("[data-testid='column-toggle-todo']");
        classicToggle.GetAttribute("aria-label").Should().Be("Collapse column To Do");
        classicToggle.GetAttribute("aria-expanded").Should().Be("true");

        classicToggle.Click();

        classicToggle = classic.Find("[data-testid='column-toggle-todo']");
        classicToggle.GetAttribute("aria-label").Should().Be("Expand column To Do");
        classicToggle.GetAttribute("aria-expanded").Should().Be("false");

        // swimlane layout: the same toggle lives in the column header row
        var swimlane = RenderSwimlaneBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        var colheadToggle = swimlane.Find("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']");
        colheadToggle.GetAttribute("aria-label").Should().Be("Collapse column To Do");
        colheadToggle.GetAttribute("aria-expanded").Should().Be("true");

        colheadToggle.Click();

        colheadToggle = swimlane.Find("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']");
        colheadToggle.GetAttribute("aria-label").Should().Be("Expand column To Do");
        colheadToggle.GetAttribute("aria-expanded").Should().Be("false");
        swimlane.Find("[data-testid='colhead-todo']").ClassList
            .Should().Contain("tm-kanban__column--collapsed");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 15 (review) — WIP state survives the collapse
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_KeepsTheWipBadge_AndTheOverLimitClass()
    {
        // 2 cards in "todo" against a MaxItems of 1 → the column is over its WIP limit.
        var wipColumns = new List<KanbanColumn>
        {
            new("todo", "To Do", "#3b82f6", 1),
            new("doing", "In Progress", "#f59e0b"),
            new("done", "Done", "#10b981"),
        };
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true), columns: wipColumns);

        var expanded = cut.Find("[data-testid='board-column-todo']");
        expanded.ClassList.Should().Contain("tm-kanban__column--over-limit");
        expanded.QuerySelector(".tm-kanban__wip-limit")!.TextContent.Trim().Should().Be("/ 1");

        cut.Find("[data-testid='column-toggle-todo']").Click();

        var collapsed = cut.Find("[data-testid='board-column-todo']");
        collapsed.ClassList.Should().Contain("tm-kanban__column--collapsed");
        collapsed.ClassList.Should().Contain("tm-kanban__column--over-limit",
            "collapsing is pure view state — the WIP breach stays visible on the strip");
        collapsed.QuerySelector(".tm-kanban__wip-limit")!.TextContent.Trim()
            .Should().Be("/ 1", "the WIP badge stays on the collapsed column");
        collapsed.QuerySelector(".tm-kanban__count")!.TextContent.Trim().Should().Be("2");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 16 (review) — the board root reports the in-flight drag
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void DragStart_AddsTheDraggingClassToTheBoardRoot_AndDragEndRemovesIt()
    {
        var cut = RenderClassicBoard();

        cut.Find(".tm-kanban").ClassList.Should().NotContain("tm-kanban--dragging");

        cut.Find("[data-testid='board-column-todo'] .tm-kanban__card")
            .TriggerEvent(DragStart, new DragEventArgs());

        cut.Find(".tm-kanban").ClassList.Should().Contain("tm-kanban--dragging",
            "the root exposes the gesture so collapsed zones can style themselves");
        cut.Find(".tm-kanban").ClassList.Should().Contain("tm-kanban",
            "the modifier is additive — the base class stays");

        cut.Find("[data-testid='board-column-todo'] .tm-kanban__card")
            .TriggerEvent(DragEnd, new DragEventArgs());

        cut.Find(".tm-kanban").ClassList.Should().NotContain("tm-kanban--dragging");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 17 (review) — collapsed column INSIDE a collapsed lane
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_InCollapsedLane_KeepsTheSlotDroppable_WithTheCollapsedModifier()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.OnItemMoved, e => moved = e);
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();
        cut.Find("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").Click();

        var slot = cut.Find("[data-testid='lane-slot-todo-Alice']");
        slot.ClassList.Should().Contain("tm-kanban__lane-slot--collapsed",
            "the slot of a collapsed column keeps the column narrow AND stays a drop zone");

        // D lives in (done, Bob); (todo, Alice) already holds A → the slot appends at index 1.
        cut.Find("[data-testid='cell-done-Bob'] .tm-kanban__card")
            .TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-todo-Alice']").TriggerEvent(DragOver, new DragEventArgs());
        cut.Find("[data-testid='lane-slot-todo-Alice']").TriggerEvent(Drop, new DragEventArgs());

        moved.Should().NotBeNull("a collapsed column inside a collapsed lane still catches cards");
        moved!.FromColumn.Should().Be("done");
        moved.FromSwimlane.Should().Be("Bob");
        moved.ToColumn.Should().Be("todo");
        moved.ToSwimlane.Should().Be("Alice");
        moved.TargetIndex.Should().Be(1);
        moved.TargetBeforeItem.Should().BeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 18 (review) — narrowed lane cells never show the insertion line
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dragover_NarrowedLaneCellOfACollapsedColumn_AddsDropTargetClass_WithoutTheInsertionLine()
    {
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.ColumnsCollapsible, true));

        cut.Find("[data-testid='column-toggle-todo']").Click();

        cut.Find("[data-testid='cell-doing-Alice'] .tm-kanban__card")
            .TriggerEvent(DragStart, new DragEventArgs());
        cut.Find("[data-testid='cell-todo-Alice']").TriggerEvent(DragOver, new DragEventArgs());

        var cell = cut.Find("[data-testid='cell-todo-Alice']");
        cell.ClassList.Should().Contain("tm-kanban__column--drop-target");
        cell.QuerySelectorAll(".tm-kanban__drop-indicator").Should().BeEmpty(
            "a narrowed cell has no room for the horizontal insertion line — the drop appends");

        cut.Find("[data-testid='cell-doing-Alice'] .tm-kanban__card")
            .TriggerEvent(DragEnd, new DragEventArgs());

        cut.Find("[data-testid='cell-todo-Alice']").ClassList
            .Should().NotContain("tm-kanban__column--drop-target");
        cut.FindAll("[data-testid='cell-todo-Alice'] .tm-kanban__drop-indicator").Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 19 (review) — Disabled: lane collapse stays, the drop does not
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Disabled_SwimlaneLayout_LaneToggleStillCollapsesAndRendersSlots_ButDropZonesStayInert()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.Disabled, true);
            p.Add(x => x.OnItemMoved, e => moved = e);
        });

        cut.Find("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-toggle").Click();

        cut.Find("[data-testid='swimlane-Alice']").ClassList.Should()
            .Contain("tm-kanban__swimlane--collapsed", "collapsing a lane is pure view state");
        cut.FindAll("[data-testid='swimlane-Alice'] [data-testid^='lane-slot-']").Count
            .Should().Be(Columns.Count, "the collapsed lane still renders one slot per column");

        cut.Find("[data-testid='cell-todo-Bob'] .tm-kanban__card")
            .TriggerEvent(DragStart, new DragEventArgs());

        var slot = cut.Find("[data-testid='lane-slot-todo-Alice']");
        slot.TriggerEvent(DragOver, new DragEventArgs());
        slot.ClassList.Should().NotContain("tm-kanban__lane-slot--drop-target",
            "a disabled board never highlights a drop zone");

        slot.TriggerEvent(Drop, new DragEventArgs());
        moved.Should().BeNull("a disabled board never commits a move");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 20 (review) — host ownership round-trips, uncontrolled stays internal
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumns_HostRoundTrip_AcrossReRender_KeepsTheColumnCollapsed()
    {
        IReadOnlyCollection<string>? captured = null;
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.CollapsedColumns, (IReadOnlyCollection<string>)Array.Empty<string>());
            p.Add(x => x.CollapsedColumnsChanged,
                EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, set => captured = set));
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();
        captured.Should().BeEquivalentTo(new[] { "todo" });

        // The host applies the callback value through a re-render — the ordinary @bind-… round-trip.
        cut.Render(p => p.Add(x => x.CollapsedColumns, captured!));

        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().Contain("tm-kanban__column--collapsed");
        cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card").Should().BeEmpty();
        cut.Find("[data-testid='column-toggle-todo']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void CollapsedLanes_HostRoundTrip_AcrossReRender_KeepsTheNoValueLaneCollapsed()
    {
        var noLaneId = TmKanbanBoard<LaneTask>.NoSwimlaneId;
        IReadOnlyCollection<string>? captured = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.CollapsedLanes, (IReadOnlyCollection<string>)Array.Empty<string>());
            p.Add(x => x.CollapsedLanesChanged,
                EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, set => captured = set));
        });

        cut.Find("[data-testid='swimlane-none'] .tm-kanban__swimlane-toggle").Click();

        captured.Should().BeEquivalentTo(new[] { noLaneId });
        cut.FindAll("[data-testid='swimlane-none'] .tm-kanban__swimlane-body").Count.Should()
            .Be(1, "the unchanged host set wins, so the lane stays expanded until the callback lands");

        cut.Render(p => p.Add(x => x.CollapsedLanes, captured!));

        cut.Find("[data-testid='swimlane-none']").ClassList
            .Should().Contain("tm-kanban__swimlane--collapsed");
        cut.FindAll("[data-testid='swimlane-none'] .tm-kanban__swimlane-body").Should().BeEmpty();
        cut.FindAll("[data-testid='lane-slot-todo-none']").Count.Should().Be(1,
            "the no-value lane round-trips through the public NoSwimlaneId sentinel");
        cut.FindAll("[data-testid='swimlane-Alice'] .tm-kanban__swimlane-body").Count.Should().Be(1);
    }

    [Fact]
    public void UncontrolledBoard_ToggleRaisesCollapsedColumnsChanged_AndUpdatesItsOwnState()
    {
        var raised = new List<IReadOnlyCollection<string>>();
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            // no CollapsedColumns parameter → the board owns the set, the callback only observes it
            p.Add(x => x.CollapsedColumnsChanged,
                EventCallback.Factory.Create<IReadOnlyCollection<string>>(this, set => raised.Add(set)));
        });

        cut.Find("[data-testid='column-toggle-todo']").Click();

        raised.Should().HaveCount(1);
        raised[0].Should().BeEquivalentTo(new[] { "todo" });
        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().Contain("tm-kanban__column--collapsed",
                "an uncontrolled board keeps the state it just reported");

        cut.Find("[data-testid='column-toggle-todo']").Click();

        raised.Should().HaveCount(2);
        raised[1].Should().BeEmpty();
        cut.Find("[data-testid='board-column-todo']").ClassList
            .Should().NotContain("tm-kanban__column--collapsed");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 21 (review) — the toggle is a plain native button
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ColumnToggle_IsANativeButton_WithNoRoleOrTabindexOverride_InBothLayouts()
    {
        var classic = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        var classicToggle = classic.Find("[data-testid='column-toggle-todo']");
        classicToggle.TagName.Should().Be("BUTTON");
        classicToggle.GetAttribute("type").Should().Be("button",
            "no implicit submit inside a surrounding form");
        classicToggle.HasAttribute("role").Should().BeFalse("a native button advertises its own role");
        classicToggle.HasAttribute("tabindex").Should().BeFalse("a native button is focusable as-is");
        classicToggle.GetAttribute("aria-expanded").Should().NotBeNull();

        var swimlane = RenderSwimlaneBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        var colheadToggle = swimlane.Find("[data-testid='colhead-todo'] [data-testid='column-toggle-todo']");
        colheadToggle.TagName.Should().Be("BUTTON");
        colheadToggle.GetAttribute("type").Should().Be("button");
        colheadToggle.HasAttribute("role").Should().BeFalse();
        colheadToggle.HasAttribute("tabindex").Should().BeFalse();
        colheadToggle.GetAttribute("aria-expanded").Should().NotBeNull();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 22 (review fze-2) — the collapsed summary names the WIP limit
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_AriaLabel_AppendsTheWipLimit_AndTheOverLimitWord()
    {
        var wipColumns = new List<KanbanColumn>
        {
            new("todo", "To Do", "#3b82f6", 1),        // 2 cards against a limit of 1 → over limit
            new("doing", "In Progress", "#f59e0b", 5), // 1 card → within the limit
            new("done", "Done", "#10b981"),            // no MaxItems at all
        };

        var classic = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true), columns: wipColumns);
        classic.Find("[data-testid='column-toggle-todo']").Click();
        classic.Find("[data-testid='column-toggle-doing']").Click();
        classic.Find("[data-testid='column-toggle-done']").Click();

        classic.Find("[data-testid='board-column-todo']").GetAttribute("aria-label")
            .Should().Be("To Do, 2 cards, limit 1, over limit",
                "the strip must not hide its WIP breach from assistive technology");
        classic.Find("[data-testid='board-column-doing']").GetAttribute("aria-label")
            .Should().Be("In Progress, 1 card, limit 5",
                "the over-limit word appears only when the count exceeds the limit");
        classic.Find("[data-testid='board-column-done']").GetAttribute("aria-label")
            .Should().Be("Done, 1 card", "a column without MaxItems keeps the plain summary");

        // The swimlane layout's sticky header cell exposes the same named group and the same summary.
        var swimlane = RenderSwimlaneBoard(p => p.Add(x => x.ColumnsCollapsible, true), columns: wipColumns);
        swimlane.Find("[data-testid='column-toggle-todo']").Click();

        var colhead = swimlane.Find("[data-testid='colhead-todo']");
        colhead.GetAttribute("role").Should().Be("group");
        colhead.GetAttribute("aria-label").Should().Be("To Do, 3 cards, limit 1, over limit");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 23 (review fze-2) — the collapsed classic strip has a title tip
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumn_CarriesTheTitleTip_InBothLayouts()
    {
        var classic = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        classic.Find("[data-testid='board-column-todo']").GetAttribute("title").Should().BeNull(
            "an expanded column shows its title in the header, so no tooltip is needed");

        classic.Find("[data-testid='column-toggle-todo']").Click();
        classic.Find("[data-testid='board-column-todo']").GetAttribute("title").Should().Be("To Do",
            "the vertical strip keeps the full column name as its title tip");

        classic.Find("[data-testid='column-toggle-todo']").Click();
        classic.Find("[data-testid='board-column-todo']").GetAttribute("title").Should().BeNull();

        // The swimlane layout's header cell keeps the tip it already had.
        var swimlane = RenderSwimlaneBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        swimlane.Find("[data-testid='column-toggle-todo']").Click();
        swimlane.Find("[data-testid='colhead-todo']").GetAttribute("title").Should().Be("To Do");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 24 (review fze-2) — the collapsed header is a pointer-only expand target
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumnHeader_IsAPointerOnlyExpandTarget_WithTheNativeToggleAsItsKeyboardPath()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnsCollapsible, true));
        cut.Find("[data-testid='column-toggle-todo']").Click();

        var header = cut.Find("[data-testid='board-column-todo'] .tm-kanban__header");
        header.HasAttribute("tabindex").Should().BeFalse("the pointer affordance never enters the tab order");
        header.HasAttribute("role").Should().BeFalse("the header stays a plain container");
        header.HasAttribute("aria-expanded").Should().BeFalse(
            "the state lives on the native toggle, the keyboard path");

        var act = () => header.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the header is pointer-only: there is no Enter/Space emulation to find");

        header.Click();
        cut.Find("[data-testid='board-column-todo']").ClassList.Should()
            .NotContain("tm-kanban__column--collapsed", "clicking anywhere on the collapsed header expands it");
        cut.Find("[data-testid='column-toggle-todo']").GetAttribute("aria-expanded").Should().Be("true");

        // The toggle is the keyboard path: each activation toggles exactly once.
        cut.Find("[data-testid='column-toggle-todo']").Click();
        cut.Find("[data-testid='board-column-todo']").ClassList.Should()
            .Contain("tm-kanban__column--collapsed");
        cut.Find("[data-testid='column-toggle-todo']").Click();
        cut.Find("[data-testid='board-column-todo']").ClassList.Should()
            .NotContain("tm-kanban__column--collapsed");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 25 (review fze-2) — collapse state only applies to a collapsible board
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CollapsedColumns_HostSet_IsIgnoredWhileColumnsCollapsibleIsFalse()
    {
        var classic = RenderClassicBoard(p =>
            p.Add(x => x.CollapsedColumns, (IReadOnlyCollection<string>)new[] { "todo" }));

        classic.FindAll(".tm-kanban__column--collapsed").Should().BeEmpty(
            "IsColumnCollapsed honours ColumnsCollapsible — the feature's state cannot outrun its switch");
        classic.FindAll(".tm-kanban__column-toggle").Should().BeEmpty();
        classic.FindAll("[data-testid='board-column-todo'] .tm-kanban__card").Count.Should().Be(2);
        classic.Find("[data-testid='board-column-todo']").GetAttribute("aria-label").Should().BeNull();

        var swimlane = RenderSwimlaneBoard(p =>
            p.Add(x => x.CollapsedColumns, (IReadOnlyCollection<string>)new[] { "todo" }));

        swimlane.FindAll(".tm-kanban__column--collapsed").Should().BeEmpty();
        swimlane.FindAll("[data-testid='cell-todo-Alice'] .tm-kanban__card").Count.Should().Be(1);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Swimlane fixtures
    // ══════════════════════════════════════════════════════════════════════

    private record LaneTask(int Id, string Title, string Status, string? Assignee);

    private static readonly IReadOnlyList<LaneTask> LaneTasks =
    [
        new(1, "A", "todo", "Alice"),
        new(2, "B", "doing", "Alice"),
        new(3, "C", "todo", "Bob"),
        new(4, "D", "done", "Bob"),
        new(5, "E", "todo", null), // no assignee → the "No value" lane
    ];

    private static readonly RenderFragment<LaneTask> LaneCard =
        t => (RenderFragment)(b => b.AddContent(0, t.Title));

    private IRenderedComponent<TmKanbanBoard<KanbanTask>> RenderClassicBoard(
        Action<ComponentParameterCollectionBuilder<TmKanbanBoard<KanbanTask>>>? extra = null,
        IReadOnlyList<KanbanTask>? items = null,
        IReadOnlyList<KanbanColumn>? columns = null)
        => Render<TmKanbanBoard<KanbanTask>>(p =>
        {
            p.Add(x => x.Columns, columns ?? Columns)
             .Add(x => x.Items, items ?? Tasks)
             .Add(x => x.ColumnSelector, t => t.Status)
             .Add(x => x.CardTemplate, CardFragment);
            extra?.Invoke(p);
        });

    private IRenderedComponent<TmKanbanBoard<LaneTask>> RenderSwimlaneBoard(
        Action<ComponentParameterCollectionBuilder<TmKanbanBoard<LaneTask>>>? extra = null,
        IReadOnlyList<KanbanColumn>? columns = null)
        => Render<TmKanbanBoard<LaneTask>>(p =>
        {
            p.Add(x => x.Columns, columns ?? Columns)
             .Add(x => x.Items, LaneTasks)
             .Add(x => x.ColumnSelector, t => t.Status)
             .Add(x => x.CardTemplate, LaneCard)
             .Add(x => x.SwimlaneSelector, t => t.Assignee);
            extra?.Invoke(p);
        });

    /// <summary>
    /// Pins one D7 parameter: public, settable (so <c>@bind-…</c> works), declared with the contracted
    /// type and carrying <see cref="ParameterAttribute"/>.
    /// </summary>
    private static void AssertParameter(Type board, string name, Type expectedType)
    {
        var property = board.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);

        property.Should().NotBeNull($"D7 declares {name} on TmKanbanBoard<TItem>");
        property!.PropertyType.Should().Be(expectedType, $"{name} has the contracted type");
        property.CanWrite.Should().BeTrue($"{name} must be settable for @bind-{name}");
        property.GetCustomAttribute<ParameterAttribute>().Should()
            .NotBeNull($"{name} must be a [Parameter], or a host binding silently does nothing");
    }

}
