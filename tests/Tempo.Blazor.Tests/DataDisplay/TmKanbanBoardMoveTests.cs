using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// The non-drag move (plan a98e4a71, phase 4 / WCAG 2.5.7): a card is a focusable element with one
/// roving tab stop per cell, a "Move…" menu on the card offers every column × swimlane target
/// (collapsed zones included) and a Move up / Move down pair for the cell's own order, and the whole
/// path reports the SAME <c>OnItemMoved</c> / <c>OnItemReordered</c> contract as a drop.
/// <para>
/// DOM CONTRACT asserted below:
/// </para>
/// <list type="bullet">
/// <item><c>.tm-kanban__card</c> carries <c>tabindex</c> (<c>0</c> for the cell's roving stop,
/// <c>-1</c> elsewhere), <c>role="group"</c>, the localized <c>aria-roledescription</c>,
/// <c>aria-describedby</c> pointing at the board's hidden card instructions, and its own
/// <c>data-testid="card-{column}-{lane}-{index}"</c>.</item>
/// <item>Enter (keydown, auto-repeat guarded) and Space (keyup) emulate <c>OnCardClick</c> — the card
/// is a non-native focusable — while the card's own content and its move menu are fenced
/// (<c>@onkeydown:stopPropagation</c> / <c>@onkeyup:stopPropagation</c>) so a control inside
/// <c>CardTemplate</c> keeps its own keys.</item>
/// <item><c>.tm-kanban__card-move</c> wraps a <c>TmContextMenu</c> whose trigger names the card
/// (<c>TmKanban_MoveCard</c>, <c>"Move {0}"</c>) and whose items are Move up, Move down and one
/// target per column (per column × lane with swimlanes), test id <c>move-to-{column}</c> /
/// <c>move-to-{column}-{lane}</c>.</item>
/// <item>One polite live region (<c>data-testid="kanban-status"</c>) announces
/// <c>TmKanban_CardMoved</c> — <c>"{0} moved to {1}"</c>.</item>
/// </list>
/// </summary>
public class TmKanbanBoardMoveTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

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

    private IRenderedComponent<TmKanbanBoard<KanbanTask>> RenderClassicBoard(
        Action<ComponentParameterCollectionBuilder<TmKanbanBoard<KanbanTask>>>? extra = null,
        IReadOnlyList<KanbanTask>? items = null,
        bool cardLabel = true)
        => Render<TmKanbanBoard<KanbanTask>>(p =>
        {
            p.Add(x => x.Columns, Columns)
             .Add(x => x.Items, items ?? Tasks)
             .Add(x => x.ColumnSelector, t => t.Status)
             .Add(x => x.CardTemplate, CardFragment);
            if (cardLabel) p.Add(x => x.CardLabelSelector, t => t.Title);
            extra?.Invoke(p);
        });

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

    private IRenderedComponent<TmKanbanBoard<LaneTask>> RenderSwimlaneBoard(
        Action<ComponentParameterCollectionBuilder<TmKanbanBoard<LaneTask>>>? extra = null)
        => Render<TmKanbanBoard<LaneTask>>(p =>
        {
            p.Add(x => x.Columns, Columns)
             .Add(x => x.Items, LaneTasks)
             .Add(x => x.ColumnSelector, t => t.Status)
             .Add(x => x.CardTemplate, LaneCard)
             .Add(x => x.SwimlaneSelector, t => t.Assignee)
             .Add(x => x.CardLabelSelector, t => t.Title);
            extra?.Invoke(p);
        });

    private static EventCallback<T> Capture<T>(Action<T> capture)
        => EventCallback.Factory.Create(new object(), capture);

    private static IElement Card<TComponent>(IRenderedComponent<TComponent> cut, string columnId, int index = 0)
        where TComponent : IComponent
        => cut.FindAll($"[data-testid='board-column-{columnId}'] .tm-kanban__card")[index];

    private static void OpenMoveMenu<TComponent>(IRenderedComponent<TComponent> cut, string columnId, int index = 0)
        where TComponent : IComponent
        => cut.FindAll($"[data-testid='board-column-{columnId}'] .tm-kanban__card")[index]
            .QuerySelector(".tm-kanban__card-move .tm-context-menu__trigger")!.Click();

    private static IReadOnlyList<string> MenuLabels<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => cut.FindAll(".tm-context-menu__item-label").Select(label => label.TextContent.Trim()).ToList();

    private static void CloseMoveMenu<TComponent>(IRenderedComponent<TComponent> cut, string columnId, int index = 0)
        where TComponent : IComponent
        => cut.FindAll($"[data-testid='board-column-{columnId}'] .tm-kanban__card")[index]
            .QuerySelector(".tm-kanban__card-move .tm-context-menu__trigger")!.Click();

    /// <summary>
    /// Activates one menu item and DRAINS the renderer. <c>TmContextMenuItem</c> closes its menu and
    /// yields before it runs the action, and bUnit drains that continuation only when the dispatcher
    /// is pumped (reading the DOM, then a no-op <c>InvokeAsync</c>) — <c>WaitForAssertion</c> alone
    /// only re-checks on renders, and a pending continuation produces none. Without the drain the
    /// assertions race the menu.
    /// </summary>
    private static void ClickMenuItem<TComponent>(IRenderedComponent<TComponent> cut, string label)
        where TComponent : IComponent
    {
        cut.FindAll(".tm-context-menu__item")
            .Single(item => item.TextContent.Contains(label, StringComparison.Ordinal))
            .Click();

        _ = cut.Markup;
        cut.InvokeAsync(() => { }).GetAwaiter().GetResult();
    }

    private IReadOnlyList<object?> FocusArguments<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => invocation.Arguments[0])
            .ToList();

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 1 — the focusable card and the "Move…" action
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EveryCard_IsAFocusableGroup_WithOneRovingStopPerCell()
    {
        var cut = RenderClassicBoard();

        var todo = cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card");
        todo.Should().HaveCount(2);
        todo[0].GetAttribute("tabindex").Should().Be("0", "the first card of a cell is its roving stop");
        todo[1].GetAttribute("tabindex").Should().Be("-1", "the other cards of the cell leave the tab order");

        var done = cut.Find("[data-testid='board-column-done'] .tm-kanban__card");
        done.GetAttribute("tabindex").Should().Be("0", "every cell owns exactly one stop");

        todo[0].GetAttribute("role").Should().Be("group");
        todo[0].GetAttribute("aria-roledescription").Should().Be("card");
        todo[0].GetAttribute("data-testid").Should().Be("card-todo-none-0");
    }

    [Fact]
    public void TheCardInstructions_AreReachableFromEveryCard()
    {
        var cut = RenderClassicBoard();

        var instructions = cut.Find("p.tm-sr-only");
        instructions.TextContent.Should().Contain("Alt+ArrowDown", "the hidden instructions name the move shortcut");

        foreach (var card in cut.FindAll(".tm-kanban__card"))
        {
            var describedBy = card.GetAttribute("aria-describedby");
            describedBy.Should().NotBeNullOrEmpty("a card without a name for its keys is a trap");
            cut.Find("#" + describedBy).Should().NotBeNull();
        }
    }

    [Fact]
    public void TheMoveMenu_ListsEveryColumn_AndAMoveAppendsToTheTargetCell()
    {
        KanbanMoveEvent<KanbanTask>? moved = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemMoved, Capture<KanbanMoveEvent<KanbanTask>>(m => moved = m)));

        OpenMoveMenu(cut, "todo");

        MenuLabels(cut).Should().Equal(
            ["Move up", "Move down", "To Do (current)", "In Progress", "Done"],
            "the non-drag path offers the cell's own order and every column, and names the cell the " +
            "card already sits in as the current one");

        ClickMenuItem(cut, "Done");

        cut.WaitForAssertion(() => moved.Should().NotBeNull());
        moved!.Item.Title.Should().Be("Task A");
        moved.FromColumn.Should().Be("todo");
        moved.ToColumn.Should().Be("done");
        moved.TargetIndex.Should().Be(1, "Done holds one card, so the move appends");
        moved.TargetBeforeItem.Should().BeNull();
        moved.FromSwimlane.Should().BeNull();
        moved.ToSwimlane.Should().BeNull();
    }

    [Fact]
    public void TheMoveMenu_RendersEvenWithACustomColumnHeaderTemplate()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ColumnHeaderTemplate, column => (RenderFragment)(b =>
        {
            b.OpenElement(0, "span");
            b.AddAttribute(1, "class", "custom-col-header");
            b.AddContent(2, column.Title);
            b.CloseElement();
        })));

        OpenMoveMenu(cut, "todo");

        MenuLabels(cut).Should().Contain("Done", "the target list reads the COLUMN definitions, not the header markup");
    }

    [Fact]
    public void TheMoveTrigger_NamesItsCard()
    {
        var cut = RenderClassicBoard();

        var trigger = cut.Find("[data-testid='board-column-todo'] .tm-kanban__card-move .tm-context-menu__trigger");
        trigger.TextContent.Should().Contain("Move Task A");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 2 — Move up / Move down keep the OnItemReordered contract
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveDown_ReportsThePositionAfterTheNextCard()
    {
        KanbanMoveEvent<KanbanTask>? reordered = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemReordered, Capture<KanbanMoveEvent<KanbanTask>>(m => reordered = m)));

        OpenMoveMenu(cut, "todo");
        ClickMenuItem(cut, "Move down");

        cut.WaitForAssertion(() => reordered.Should().NotBeNull());
        reordered!.Item.Title.Should().Be("Task A");
        reordered.ToColumn.Should().Be("todo");
        reordered.TargetIndex.Should().Be(2, "Task A becomes the last card of the two-card column");
        reordered.TargetBeforeItem.Should().BeNull();
    }

    [Fact]
    public void MoveUp_ReportsThePositionBeforeThePreviousCard()
    {
        KanbanMoveEvent<KanbanTask>? reordered = null;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnItemReordered, Capture<KanbanMoveEvent<KanbanTask>>(m => reordered = m)));

        OpenMoveMenu(cut, "todo", 1);
        ClickMenuItem(cut, "Move up");

        cut.WaitForAssertion(() => reordered.Should().NotBeNull());
        reordered!.Item.Title.Should().Be("Task B");
        reordered.TargetIndex.Should().Be(0);
        reordered.TargetBeforeItem!.Title.Should().Be("Task A");
    }

    [Fact]
    public void TheBoundaryMoves_AreDisabled()
    {
        var cut = RenderClassicBoard();

        OpenMoveMenu(cut, "todo");
        cut.FindAll(".tm-context-menu__item").Single(i => i.TextContent.Contains("Move up", StringComparison.Ordinal))
            .HasAttribute("disabled").Should().BeTrue("the first card cannot move up");

        cut.FindAll(".tm-context-menu__item").Single(i => i.TextContent.Contains("Move down", StringComparison.Ordinal))
            .HasAttribute("disabled").Should().BeFalse();

        // A second menu cannot be opened over the first one (the overlay would dismiss it in a
        // browser), so close this one before asking the next card for its own.
        CloseMoveMenu(cut, "todo");
        OpenMoveMenu(cut, "todo", 1);
        cut.FindAll(".tm-context-menu__item").Single(i => i.TextContent.Contains("Move down", StringComparison.Ordinal))
            .HasAttribute("disabled").Should().BeTrue("the last card cannot move down");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 1 (D8) — swimlane targets, the no-value lane and collapsed zones
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheMoveMenu_ListsEverySwimlaneCell_AndReportsToSwimlane()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.OnItemMoved, Capture<KanbanMoveEvent<LaneTask>>(m => moved = m)));

        cut.Find("[data-testid='cell-todo-Alice'] .tm-kanban__card-move .tm-context-menu__trigger").Click();

        MenuLabels(cut).Should().Contain("Done / Bob", "a swimlane target names both axes");

        ClickMenuItem(cut, "Done / Bob");

        cut.WaitForAssertion(() => moved.Should().NotBeNull());
        moved!.Item.Title.Should().Be("A");
        moved.ToColumn.Should().Be("done");
        moved.ToSwimlane.Should().Be("Bob");
        moved.TargetIndex.Should().Be(1, "Done / Bob holds one card, so the move appends");
    }

    [Fact]
    public void TheNoValueLane_IsAReachableTarget_AndReportsANullSwimlane()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p => p.Add(x => x.OnItemMoved, Capture<KanbanMoveEvent<LaneTask>>(m => moved = m)));

        cut.Find("[data-testid='cell-todo-Alice'] .tm-kanban__card-move .tm-context-menu__trigger").Click();
        ClickMenuItem(cut, "Done / No value");

        cut.WaitForAssertion(() => moved.Should().NotBeNull());
        moved!.ToColumn.Should().Be("done");
        moved.ToSwimlane.Should().BeNull("the trailing \"no value\" lane keeps mapping to null");
        moved.TargetIndex.Should().Be(0, "Done / No value is empty");
    }

    [Fact]
    public void ACollapsedColumn_StaysAReachableTarget_AndReceivesTheFocus()
    {
        KanbanMoveEvent<KanbanTask>? moved = null;
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.ColumnsCollapsible, true);
            p.Add(x => x.CollapsedColumns, new[] { "done" });
            p.Add(x => x.OnItemMoved, Capture<KanbanMoveEvent<KanbanTask>>(m => moved = m));
        });

        OpenMoveMenu(cut, "todo");
        ClickMenuItem(cut, "Done");

        cut.WaitForAssertion(() => moved.Should().NotBeNull());
        moved!.ToColumn.Should().Be("done");

        // A collapsed strip renders no card, so the hand-back names the ZONE it went to: the root's
        // data-focus-target is the board's own record of the target, while FocusArguments proves a
        // focus call followed (the E2E test asserts the real document.activeElement).
        cut.WaitForAssertion(() => cut.Find(".tm-kanban").GetAttribute("data-focus-target")
            .Should().Be("column:done"));
        cut.Find("[data-testid='board-column-done']").GetAttribute("tabindex").Should().Be("-1",
            "a collapsed zone is a programmatic focus target");
        FocusArguments(cut).Should().NotBeEmpty("the strip receives the focus, not the closing menu");
    }

    [Fact]
    public void ACollapsedLane_StaysAReachableTarget_AndItsSlotReceivesTheFocus()
    {
        KanbanMoveEvent<LaneTask>? moved = null;
        var cut = RenderSwimlaneBoard(p =>
        {
            p.Add(x => x.CollapsedLanes, new[] { "Bob" });
            p.Add(x => x.OnItemMoved, Capture<KanbanMoveEvent<LaneTask>>(m => moved = m));
        });

        cut.Find("[data-testid='cell-todo-Alice'] .tm-kanban__card-move .tm-context-menu__trigger").Click();
        ClickMenuItem(cut, "Done / Bob");

        cut.WaitForAssertion(() => moved.Should().NotBeNull());
        moved!.ToSwimlane.Should().Be("Bob");

        cut.WaitForAssertion(() => cut.Find(".tm-kanban").GetAttribute("data-focus-target")
            .Should().Be("lane:done:Bob"));
        cut.Find("[data-testid='lane-slot-done-Bob']").GetAttribute("tabindex").Should().Be("-1",
            "a collapsed lane's slot is a programmatic focus target");
        FocusArguments(cut).Should().NotBeEmpty("the slot receives the focus, not the closing menu");
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 3 — the keyboard activation convention on the card itself
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Enter_OnTheCard_RaisesTheClickExactlyOnce()
    {
        var clicks = 0;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnCardClick, Capture<KanbanTask>(_ => clicks++)));

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        clicks.Should().Be(1, "the card is non-native, so Enter is emulated exactly once");
    }

    [Fact]
    public void AHeldEnter_DoesNotRepeatTheClick()
    {
        var clicks = 0;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnCardClick, Capture<KanbanTask>(_ => clicks++)));

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });

        clicks.Should().Be(0, "auto-repeat must not re-open the card");
    }

    [Fact]
    public void Space_OnTheCard_RaisesTheClickOnKeyUpOnly()
    {
        var clicks = 0;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnCardClick, Capture<KanbanTask>(_ => clicks++)));

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = " " });
        clicks.Should().Be(0, "Space activates on keyup, exactly where a native button fires its click");

        // The event callback re-renders the board, so the handler ids of the previous element are gone.
        Card(cut, "todo").KeyUp(new KeyboardEventArgs { Key = " " });
        clicks.Should().Be(1);
    }

    [Fact]
    public void TheMoveMenusKeys_NeverReachTheCardsHandler()
    {
        var clicks = 0;
        var cut = RenderClassicBoard(p => p.Add(x => x.OnCardClick, Capture<KanbanTask>(_ => clicks++)));

        cut.Find("[data-testid='board-column-todo'] .tm-kanban__card-move .tm-context-menu__trigger")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        clicks.Should().Be(0, "the menu trigger is a native-keyed child and owns its own Enter");
    }

    [Fact]
    public void TheMKey_OpensTheFocusedCardsMoveMenu()
    {
        var cut = RenderClassicBoard();

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "m" });

        cut.WaitForAssertion(() => MenuLabels(cut).Should().Contain("Done"));
    }

    [Fact]
    public void AltArrowDown_OpensTheFocusedCardsMoveMenu()
    {
        var cut = RenderClassicBoard();

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        cut.WaitForAssertion(() => MenuLabels(cut).Should().Contain("Done"));
    }

    [Fact]
    public void ArrowDown_MovesTheRovingStopAndTheFocusToTheNextCardOfTheCell()
    {
        var cut = RenderClassicBoard();

        var cards = cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card");
        cards[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => FocusArguments(cut).Should().ContainSingle());
        FocusArguments(cut).Single().ShouldBeElementReferenceTo(cards[1]);
        cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card")[1]
            .GetAttribute("tabindex").Should().Be("0", "the roving stop follows the focus");
    }

    [Fact]
    public void ArrowDown_StopsAtTheLastCardOfTheCell()
    {
        var cut = RenderClassicBoard();

        var cards = cut.FindAll("[data-testid='board-column-todo'] .tm-kanban__card");
        cards[1].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        FocusArguments(cut).Should().BeEmpty("the cell's last card has nowhere to go");
    }

    [Fact]
    public void AMoveHandsTheFocusBackToTheMovedCard()
    {
        var cut = RenderClassicBoard();

        var card = Card(cut, "todo");
        OpenMoveMenu(cut, "todo");
        ClickMenuItem(cut, "Done");

        cut.WaitForAssertion(() => cut.Find(".tm-kanban").GetAttribute("data-focus-target").Should().Be("card"));
        cut.WaitForAssertion(() => FocusArguments(cut).Last().ShouldBeElementReferenceTo(card));
    }

    [Fact]
    public void AMoveTheHostAppliesByReplacingTheItem_FocusesTheCardAtTheReportedPosition()
    {
        // The demo (and most hosts) apply a move as `item with { Status = … }` — a NEW record. An
        // identity-keyed hand-back would keep pointing at the element that just left the DOM, so the
        // focus must resolve from the reported position instead.
        var cut = Render<ReplacingHost>();

        var card = cut.Find("[data-testid='board-column-todo'] .tm-kanban__card");
        card.QuerySelector(".tm-kanban__card-move .tm-context-menu__trigger")!.Click();
        ClickMenuItem(cut, "Done");

        cut.WaitForAssertion(() => cut.FindAll("[data-testid='board-column-done'] .tm-kanban__card")
            .Should().HaveCount(2, "the host applied the move"));

        var movedCard = cut.FindAll("[data-testid='board-column-done'] .tm-kanban__card")[1];
        movedCard.TextContent.Should().Contain("Task A");
        cut.Find(".tm-kanban").GetAttribute("data-focus-target").Should().Be("card",
            "the hand-back resolves a CARD, not the collapsed zone fallback");
        movedCard.GetAttribute("tabindex").Should().Be("0",
            "the tab stop follows the card to the position the host was told to put it at");
        cut.WaitForAssertion(() => FocusArguments(cut).Should().NotBeEmpty());
        ((ElementReference)FocusArguments(cut).Last()!).Id.Should().NotBeNullOrEmpty(
            "the hand-back called focus on the element it resolved from the reported position");
    }

    /// <summary>A host that applies a move the way the demo does — by replacing the item instance.</summary>
    private sealed class ReplacingHost : ComponentBase
    {
        private List<KanbanTask> _items = [.. Tasks];

        private void HandleMove(KanbanMoveEvent<KanbanTask> move)
        {
            var moved = move.Item with { Status = move.ToColumn };
            _items.RemoveAll(task => task.Id == move.Item.Id);

            var insertAt = move.TargetBeforeItem is not null
                ? _items.FindIndex(task => task.Id == move.TargetBeforeItem.Id)
                : _items.FindLastIndex(task => task.Status == move.ToColumn) + 1;
            _items.Insert(insertAt < 0 ? _items.Count : Math.Min(insertAt, _items.Count), moved);
        }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<TmKanbanBoard<KanbanTask>>(0);
            builder.AddAttribute(1, "Columns", Columns);
            builder.AddAttribute(2, "Items", (IReadOnlyList<KanbanTask>)_items);
            builder.AddAttribute(3, "ColumnSelector", (Func<KanbanTask, string>)(task => task.Status));
            builder.AddAttribute(4, "CardTemplate", CardFragment);
            builder.AddAttribute(5, "CardLabelSelector", (Func<KanbanTask, string>)(task => task.Title));
            builder.AddAttribute(6, "OnItemMoved", EventCallback.Factory.Create<KanbanMoveEvent<KanbanTask>>(this, HandleMove));
            builder.CloseComponent();
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 4 — the live region
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TheBoardsLiveRegion_AnnouncesTheMove()
    {
        var cut = RenderClassicBoard();

        OpenMoveMenu(cut, "todo");
        ClickMenuItem(cut, "Done");


        var status = cut.Find("[data-testid='kanban-status']");
        status.GetAttribute("role").Should().Be("status");
        status.GetAttribute("aria-live").Should().Be("polite");
        cut.WaitForAssertion(() => cut.Find("[data-testid='kanban-status']").TextContent.Trim()
            .Should().Be("Task A moved to Done"));
    }

    [Fact]
    public void AReorder_IsAnnouncedInTheSameRegion()
    {
        var cut = RenderClassicBoard();

        OpenMoveMenu(cut, "todo");
        ClickMenuItem(cut, "Move down");

        cut.WaitForAssertion(() => cut.Find("[data-testid='kanban-status']").TextContent.Trim()
            .Should().Be("Task A moved to To Do"));
    }

    // ══════════════════════════════════════════════════════════════════════
    // Subtask 2 (D6) / opt-out — Disabled hides the action, ShowMoveMenu turns it off
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Disabled_HidesTheNonDragAction_AndKeepsTheDragMarkup()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.Disabled, true));

        cut.FindAll(".tm-kanban__card-move").Should().BeEmpty("a disabled board offers no move action");
        cut.FindAll(".tm-kanban__card-actions").Should().BeEmpty();
        cut.FindAll(".tm-kanban__card").Should().OnlyContain(card =>
            card.GetAttribute("draggable") == "true"
            && card.GetAttribute("tabindex") == null,
            "the inert card is not a tab stop either");
        cut.Markup.Should().NotContain("Move up");
    }

    [Fact]
    public void ShowMoveMenuFalse_RendersThePrePhase4CardExactly()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ShowMoveMenu, false));

        cut.FindAll(".tm-kanban__card-move").Should().BeEmpty();
        cut.FindAll(".tm-kanban__card-actions").Should().BeEmpty();
        cut.FindAll(".tm-kanban__card-content").Should().BeEmpty(
            "the host template renders straight into the card again");
        cut.FindAll(".tm-kanban__card").Should().OnlyContain(card =>
            card.GetAttribute("draggable") == "true"
            && card.GetAttribute("tabindex") == null
            && card.GetAttribute("aria-describedby") == null);
        cut.FindAll("[data-testid='kanban-status']").Should().BeEmpty("no move path, no live region");
    }

    [Fact]
    public void TheMKey_HasNoHandlerAtAllWhileTheMenuIsOff()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ShowMoveMenu, false));

        // bUnit's MissingEventHandlerException is the observable proof that the card owns no key
        // handler: the pre-2.11 card is a pointer-only affordance again.
        var keyDown = () => Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "m" });
        keyDown.Should().Throw<MissingEventHandlerException>();

        cut.FindAll(".tm-context-menu__item").Should().BeEmpty();
    }

    // ══════════════════════════════════════════════════════════════════════
    // Review fze-4 — one tab stop per cell, chord guards, current target, labels
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EveryCell_HasExactlyOneTabStop_AndTheMoveTriggerIsNotASecondOne()
    {
        var cut = RenderClassicBoard();

        foreach (var columnId in new[] { "todo", "doing", "done" })
        {
            var cell = cut.Find($"[data-testid='board-column-{columnId}']");
            cell.QuerySelectorAll("[tabindex='0']").Should().ContainSingle(
                "a cell owns ONE tab stop, so Tab walks the cells instead of every card and every trigger");
        }

        cut.FindAll(".tm-kanban__card-move .tm-context-menu__trigger").Should()
            .OnlyContain(trigger => trigger.GetAttribute("tabindex") == "-1",
                "the card's M / Alt+ArrowDown shortcut opens the menu, so the trigger leaves the tab order");
    }

    [Fact]
    public void TheSwimlaneCell_HasExactlyOneTabStop()
    {
        var cut = RenderSwimlaneBoard();

        cut.Find("[data-testid='cell-todo-Alice']").QuerySelectorAll("[tabindex='0']").Should().ContainSingle();
        cut.Find("[data-testid='cell-todo-Bob']").QuerySelectorAll("[tabindex='0']").Should().ContainSingle();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CtrlOrMetaM_DoesNotOpenTheMoveMenu(bool ctrl, bool meta)
    {
        var cut = RenderClassicBoard();

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "m", CtrlKey = ctrl, MetaKey = meta });

        cut.FindAll(".tm-context-menu__item").Should().BeEmpty(
            "a Ctrl/⌘ chord belongs to the browser (⌘+M minimizes the window), never to the board");
    }

    [Fact]
    public void CtrlAltArrowDown_DoesNotOpenTheMoveMenu()
    {
        var cut = RenderClassicBoard();

        Card(cut, "todo").KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true, CtrlKey = true });

        cut.FindAll(".tm-context-menu__item").Should().BeEmpty();
    }

    [Fact]
    public void TheCardsOwnCell_IsMarkedCurrent_AndIsNotATarget()
    {
        KanbanMoveEvent<KanbanTask>? moved = null;
        KanbanMoveEvent<KanbanTask>? reordered = null;
        var cut = RenderClassicBoard(p =>
        {
            p.Add(x => x.OnItemMoved, Capture<KanbanMoveEvent<KanbanTask>>(m => moved = m));
            p.Add(x => x.OnItemReordered, Capture<KanbanMoveEvent<KanbanTask>>(m => reordered = m));
        });

        OpenMoveMenu(cut, "todo");

        var current = cut.Find("[data-testid='move-to-todo']");
        current.TextContent.Should().Contain("To Do (current)",
            "the cell the card already sits in is named, not offered as a destination");
        current.GetAttribute("aria-current").Should().Be("true");
        current.HasAttribute("disabled").Should().BeTrue();

        current.Click();
        moved.Should().BeNull("a card cannot be moved into the cell it already sits in");
        reordered.Should().BeNull();
    }

    [Fact]
    public void TheSwimlaneCellsOwnTarget_IsMarkedCurrent_AndTheOtherLanesStayTargets()
    {
        var cut = RenderSwimlaneBoard();

        cut.Find("[data-testid='cell-todo-Alice'] .tm-kanban__card-move .tm-context-menu__trigger").Click();

        var current = cut.Find("[data-testid='move-to-todo-Alice']");
        current.GetAttribute("aria-current").Should().Be("true");
        current.HasAttribute("disabled").Should().BeTrue();

        var other = cut.Find("[data-testid='move-to-todo-Bob']");
        other.HasAttribute("aria-current").Should().BeFalse();
        other.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void TheInstructions_OmitTheActivationSentence_WhenTheHostHasNoCardClick()
    {
        var without = RenderClassicBoard();

        var instructions = without.Find("p.tm-sr-only").TextContent;
        instructions.Should().Contain("Alt+ArrowDown");
        instructions.Should().NotContain("Press Enter",
            "a board with no OnCardClick must not tell the reader to press a key that does nothing");

        var with = RenderClassicBoard(p => p.Add(x => x.OnCardClick, Capture<KanbanTask>(_ => { })));
        with.Find("p.tm-sr-only").TextContent.Should().StartWith("Press Enter to open a card.");
    }

    /// <summary>A card type that does NOT override <c>ToString()</c>, as most POCO hosts are.</summary>
    private sealed class PlainTask(string title, string status)
    {
        public string Title { get; } = title;

        public string Status { get; } = status;
    }

    [Fact]
    public void ACardWithoutALabelSelector_UsesTheLocalizedPositionalName_ForAPlainType()
    {
        var cut = Render<TmKanbanBoard<PlainTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, new List<PlainTask> { new("Task A", "todo"), new("Task B", "todo") })
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        var trigger = cut.Find("[data-testid='board-column-todo'] .tm-kanban__card-move .tm-context-menu__trigger");
        trigger.TextContent.Should().Contain("Move card 1",
            "object.ToString() only prints the type name, so the board falls back to the card's position");
        trigger.TextContent.Should().NotContain("PlainTask");

        var second = cut.Find("[data-testid='board-column-todo'] [data-testid='card-move-todo-none-1']");
        second.TextContent.Should().Contain("Move card 2");
    }

    [Fact]
    public void ACardWithoutALabelSelector_UsesTheTypesToString_WhenItOverridesIt()
    {
        var cut = RenderClassicBoard(cardLabel: false);

        var trigger = cut.Find("[data-testid='board-column-todo'] .tm-kanban__card-move .tm-context-menu__trigger");
        trigger.TextContent.Should().Contain("Move KanbanTask { Id = 1, Title = Task A, Status = todo }",
            "a record's generated ToString() names its fields, so it is a usable card label");
    }

    /// <summary>
    /// A virtualized cell can only render the WINDOW <c>&lt;Virtualize&gt;</c> asks for, so a roving card
    /// outside that window would leave the cell with no tab stop at all. bUnit has no viewport (Virtualize
    /// renders the whole cell), so the window rule is driven through the board's own methods here.
    /// </summary>
    [Fact]
    public void AVirtualizedCell_PointsItsSingleTabStopAtTheWindowsFirstCard_WhenTheRovingCardIsNotRendered()
    {
        var many = Enumerable.Range(1, 60).Select(i => new KanbanTask(i, $"Task {i}", "todo")).ToList();
        var cut = RenderClassicBoard(items: many, extra: p => p.Add(x => x.VirtualizeCards, true));
        var board = cut.Instance;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        var isTabStop = typeof(TmKanbanBoard<KanbanTask>).GetMethod("IsCardTabStop", flags)!;
        var refresh = typeof(TmKanbanBoard<KanbanTask>).GetMethod("RefreshVirtualTabStops", flags)!;
        var rovingField = typeof(TmKanbanBoard<KanbanTask>).GetField("_rovingItem", flags)!;

        bool Stop(int index, int roving) => (bool)isTabStop.Invoke(board, ["todo", null, index, roving, false])!;

        // A window that starts AFTER the roving card cannot contain it: its first card is the stop,
        // decided from the first card alone.
        Stop(20, 3).Should().BeTrue();
        Stop(21, 3).Should().BeFalse();

        // A window that ENDS before the roving card needs the post-render pass, which re-points the
        // stop at the first card the window rendered.
        rovingField.SetValue(board, many[40]);
        var changed = (bool)refresh.Invoke(board, null)!;
        changed.Should().BeTrue("the tracked window held no card that could carry the stop");
        Stop(20, 40).Should().BeTrue("the first card of the rendered window becomes the cell's tab stop");
        Stop(21, 40).Should().BeFalse("...and it is still exactly ONE stop per cell");
    }

    [Fact]
    public void AVirtualizedCell_RendersExactlyOneTabStop()
    {
        var many = Enumerable.Range(1, 60).Select(i => new KanbanTask(i, $"Task {i}", "todo")).ToList();
        var cut = RenderClassicBoard(items: many, extra: p => p.Add(x => x.VirtualizeCards, true));

        cut.Find("[data-testid='cards-virtual-todo']").QuerySelectorAll("[tabindex='0']").Should().ContainSingle();
    }

    [Fact]
    public void ShowMoveMenuFalse_LeavesNoFocusTargetStateOnTheBoard()
    {
        var cut = RenderClassicBoard(p => p.Add(x => x.ShowMoveMenu, false));

        cut.Find(".tm-kanban").GetAttribute("data-focus-target").Should().BeNull(
            "the focus hand-back attribute belongs to the non-drag path only");
        cut.FindAll("[data-testid^='board-column-']").Should().OnlyContain(zone =>
            zone.GetAttribute("tabindex") == null,
            "no programmatic focus targets without the move path");
        cut.FindAll("p.tm-sr-only").Should().BeEmpty("no instructions without the move path");
    }
}
