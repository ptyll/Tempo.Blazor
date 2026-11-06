using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.DataDisplay;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 7 — <c>TmDelegateBoard</c>: work handed out to agents by dragging a card, by its Assign menu, or
/// by the automatic pass.
/// <para>
/// These facts pin the contract the surface promises: the backlog and one column per agent, a card per task with its
/// priority word and its estimate, the capacity line built from the HOST's numbers, a FULL column that refuses a drop
/// with an announcement (and paints the reason), the occupied-until preview that projects the pointed-at task onto an
/// agent, the Assign menu as the keyboard and tap alternative (with a full agent named as full), the automatic pass
/// (first free agent by free time, one slot per task, never fighting the reader), exactly ONE polite live region, and the
/// overlay rule: the reader's move is shown at once and dropped when the host supplies a new instance of the task.
/// </para>
/// </summary>
public class TmDelegateBoardTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(Ten);

    public TmDelegateBoardTests()
    {
        Services.AddSingleton<TimeProvider>(_time);
    }

    private static AgentRef Agent(string id, string name, int color = 0) => new(name, id) { ColorIndex = color };

    private static DelegateAgent AgentColumn(string id, string name, int slots = 2, int active = 0,
        DateTimeOffset? busyUntil = null)
        => new(Agent(id, name)) { MaxConcurrency = slots, ActiveCount = active, BusyUntil = busyUntil };

    private static DelegateTask Task(string id, string title, string? assignedTo = null,
        DelegateTaskPriority? priority = null, int minutes = 0)
        => new(id, title)
        {
            AssignedAgentId = assignedTo,
            Priority = priority,
            EstimatedDuration = minutes > 0 ? TimeSpan.FromMinutes(minutes) : null,
        };

    private static IReadOnlyList<DelegateAgent> Agents() =>
    [
        AgentColumn("planner", "Planner"),
        AgentColumn("writer", "Writer"),
        AgentColumn("reviewer", "Reviewer"),
    ];

    private static IReadOnlyList<DelegateTask> Tasks() =>
    [
        Task("t1", "Draft the summary", priority: DelegateTaskPriority.High, minutes: 30),
        Task("t2", "Collect the invoices"),
        Task("t3", "Review the draft", assignedTo: "writer", minutes: 45),
    ];

    private IRenderedComponent<TmDelegateBoard> RenderBoard(
        Action<ComponentParameterCollectionBuilder<TmDelegateBoard>>? configure = null,
        IReadOnlyList<DelegateTask>? tasks = null,
        IReadOnlyList<DelegateAgent>? agents = null)
        => Render<TmDelegateBoard>(p =>
        {
            p.Add(c => c.Tasks, tasks ?? Tasks());
            p.Add(c => c.Agents, agents ?? Agents());
            p.Add(c => c.TimeProvider, _time);
            configure?.Invoke(p);
        });

    private static AngleSharp.Dom.IElement Column(IRenderedComponent<TmDelegateBoard> cut, string column)
        => cut.Find($".tm-ai-delegate__column[data-column='{column}']");

    private static IReadOnlyList<string> CardTitles(AngleSharp.Dom.IElement column)
        => column.QuerySelectorAll(".tm-ai-delegate__task-title").Select(element => element.TextContent.Trim()).ToList();

    private static string Announcement(IRenderedComponent<TmDelegateBoard> cut)
        => cut.Find("[role='status']").TextContent.Trim();

    private static EventCallback<DelegateMoveRequest> Capture(Action<DelegateMoveRequest> sink)
        => EventCallback.Factory.Create<DelegateMoveRequest>(new object(), sink);

    private static void OpenMenu(AngleSharp.Dom.IElement card)
        => card.QuerySelector(".tm-context-menu__trigger")!.Click();

    private static IReadOnlyList<AngleSharp.Dom.IElement> MenuItems(IRenderedComponent<TmDelegateBoard> cut)
        => [.. cut.FindAll(".tm-context-menu__item")];

    /// <summary>
    /// Re-renders the SAME instance with a new parameter view — how a host applies (or refuses) a reported move.
    /// bUnit 2 dropped <c>SetParametersAndRender</c>, so a partial view goes through <c>SetParametersAsync</c>.
    /// </summary>
    private static Task RerenderAsync(IRenderedComponent<TmDelegateBoard> cut,
        IReadOnlyList<DelegateTask> tasks, IReadOnlyList<DelegateAgent> agents, bool autoAssign,
        Action<DelegateMoveRequest>? assign = null)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmDelegateBoard.Tasks)] = tasks,
                [nameof(TmDelegateBoard.Agents)] = agents,
                [nameof(TmDelegateBoard.TimeProvider)] = cut.Instance.TimeProvider,
                [nameof(TmDelegateBoard.AutoAssign)] = autoAssign,
                [nameof(TmDelegateBoard.OnAssign)] = assign is null
                    ? default(EventCallback<DelegateMoveRequest>)
                    : Capture(assign),
            })));

    private static void FocusCard(IRenderedComponent<TmDelegateBoard> cut, string taskId)
        => Card(cut, taskId).TriggerEvent("onfocusin", new FocusEventArgs());

    private static void HoverColumn(IRenderedComponent<TmDelegateBoard> cut, string column)
        => Column(cut, column).TriggerEvent("onmouseenter", new MouseEventArgs());

    // ---------- structure ----------

    [Fact]
    public void TheBoardRendersTheBacklogAndOneColumnPerAgent()
    {
        var cut = RenderBoard();

        cut.Find(".tm-ai-delegate__board").GetAttribute("role").Should().Be("group");
        cut.FindAll(".tm-ai-delegate__column").Should().HaveCount(4);

        CardTitles(Column(cut, "backlog")).Should().Equal("Draft the summary", "Collect the invoices");
        CardTitles(Column(cut, "planner")).Should().BeEmpty();
        CardTitles(Column(cut, "writer")).Should().Equal("Review the draft");
        Card(cut, "t3").GetAttribute("data-assigned").Should().Be("true");
        Card(cut, "t1").GetAttribute("data-assigned").Should().Be("false");
    }

    [Fact]
    public void TheHeadingIsLocalizedAndItsLevelIsClamped()
    {
        var cut = RenderBoard(p => p.Add(c => c.HeadingLevel, 9));

        cut.Find("h6.tm-ai-delegate__title").TextContent.Trim().Should().Be("Delegate board");
        cut.FindAll("h6.tm-ai-delegate__column-title").Should().NotBeEmpty("a column heading never outranks its section");
        RenderBoard(p => p.Add(c => c.Title, "Own heading")).Find(".tm-ai-delegate__title")
            .TextContent.Trim().Should().Be("Own heading");
        RenderBoard(p => p.Add(c => c.ShowTitle, false)).FindAll(".tm-ai-delegate__title").Should().BeEmpty();
    }

    [Fact]
    public void TheSummaryCountsTheBacklogAndTheFreeAgents()
    {
        var cut = RenderBoard(agents:
        [
            AgentColumn("planner", "Planner"),
            AgentColumn("writer", "Writer", slots: 2, active: 2),
        ]);

        cut.Find(".tm-ai-delegate__summary").TextContent.Trim()
            .Should().Be("Unassigned: 2 · Free agents: 1 of 2");
    }

    [Fact]
    public void AnEmptyBoardSaysSoInsteadOfRenderingNothing()
    {
        var cut = RenderBoard(tasks: [], agents: []);

        cut.Find("[data-testid='delegate-backlog-empty']").TextContent.Trim().Should().Be("No unassigned tasks.");
        cut.Find("[data-testid='delegate-agents-empty']").TextContent.Trim().Should().Be("No agents on this board.");
    }

    [Fact]
    public void ATaskAssignedToAnAgentTheBoardDoesNotHaveStaysInTheBacklog()
    {
        var cut = RenderBoard(tasks: [Task("t1", "Orphan", assignedTo: "ghost")]);

        CardTitles(Column(cut, "backlog")).Should().Equal("Orphan");
    }

    [Fact]
    public void ACardStatesItsPriorityAndItsEstimate()
    {
        var cut = RenderBoard();

        var card = Card(cut, "t1");
        card.GetAttribute("data-priority").Should().Be("high");
        card.QuerySelector(".tm-ai-delegate__priority-text")!.TextContent.Trim().Should().Be("High");
        card.QuerySelector(".tm-ai-delegate__task-meta")!.TextContent.Should().Contain("Estimated 30 min");

        Card(cut, "t2").GetAttribute("data-priority").Should().Be("none");
        Card(cut, "t2").QuerySelector(".tm-ai-delegate__priority").Should().BeNull("no grade is not a grade");
        Card(cut, "t2").QuerySelector(".tm-ai-delegate__task-meta").Should().BeNull("no estimate is not an estimate");
    }

    [Fact]
    public void AnUntitledTaskAndAnUnnamedAgentKeepALocalizedFallback()
    {
        var cut = RenderBoard(
            tasks: [Task("t1", "  ", assignedTo: "planner")],
            agents: [AgentColumn("planner", "  ")]);

        Card(cut, "t1").QuerySelector(".tm-ai-delegate__task-title")!.TextContent.Trim().Should().Be("Untitled task");
        cut.Find("[data-testid='delegate-agent-title']").TextContent.Should().Contain("Unnamed agent",
            "the shared agent badge keeps its own localized fallback for a nameless agent");
    }

    // ---------- capacity and the preview ----------

    [Fact]
    public void TheColumnStatesTheHostsCapacityAndItsCardCount()
    {
        var cut = RenderBoard(agents: [AgentColumn("planner", "Planner", slots: 3, active: 1)]);

        var column = Column(cut, "planner");
        column.QuerySelector("[data-testid='delegate-agent-capacity']")!.TextContent.Should()
            .Contain("1 of 3 running").And.Contain("Assigned: 0");
        column.GetAttribute("data-full").Should().Be("false");
    }

    [Fact]
    public void AFullColumnSaysSoWithAWordAndAnIcon()
    {
        var cut = RenderBoard(agents: [AgentColumn("planner", "Planner", slots: 2, active: 2)]);

        var column = Column(cut, "planner");
        column.GetAttribute("data-full").Should().Be("true");
        column.QuerySelector(".tm-ai-delegate__column--full, .tm-ai-delegate__full").Should().NotBeNull();
        column.QuerySelector(".tm-ai-delegate__full-text")!.TextContent.Trim().Should().Be("Full");
        column.QuerySelector(".tm-ai-delegate__full-icon").Should().NotBeNull("the state is never colour alone");
    }

    [Fact]
    public void AFreeAgentWithNoPointedTaskSaysFreeNow()
    {
        var cut = RenderBoard(agents: [AgentColumn("planner", "Planner")]);

        var preview = Column(cut, "planner").QuerySelector("[data-testid='delegate-agent-preview']")!;
        preview.TextContent.Trim().Should().Be("Free now");
        preview.GetAttribute("data-state").Should().Be("free");
    }

    [Fact]
    public void ABusyAgentStatesWhenItIsFree()
    {
        var cut = RenderBoard(agents: [AgentColumn("planner", "Planner", busyUntil: Ten.AddHours(2))]);

        var preview = Column(cut, "planner").QuerySelector("[data-testid='delegate-agent-preview']")!;
        preview.TextContent.Trim().Should().Be("Occupied until " + Ten.AddHours(2).ToString("t"));
        preview.GetAttribute("data-state").Should().Be("busy");
    }

    [Fact]
    public void PointingAtACardProjectsItsEstimateOntoTheAgent()
    {
        var cut = RenderBoard(agents: [AgentColumn("planner", "Planner", busyUntil: Ten.AddHours(1))]);

        // Focus points the projection, so a keyboard reader gets the same preview a mouse does.
        FocusCard(cut, "t1");
        HoverColumn(cut, "planner");

        var preview = Column(cut, "planner").QuerySelector("[data-testid='delegate-agent-preview']")!;
        preview.GetAttribute("data-estimate").Should().Be("true");
        preview.TextContent.Trim().Should().Be(
            "Occupied until " + Ten.AddHours(1).AddMinutes(30).ToString("t") + " with this task");
        preview.GetAttribute("data-state").Should().Be("preview");
    }

    [Fact]
    public void ACardWithoutAnEstimateProjectsNothingButTheAgentsOwnLoad()
    {
        var cut = RenderBoard(agents: [AgentColumn("planner", "Planner")]);

        FocusCard(cut, "t2");
        HoverColumn(cut, "planner");

        Column(cut, "planner").QuerySelector("[data-testid='delegate-agent-preview']")!.TextContent.Trim()
            .Should().Be("Free now");
    }

    [Fact]
    public async Task SteppingBetweenTheCardsOfOneColumnKeepsThePreview()
    {
        var planner = AgentColumn("planner", "Planner", busyUntil: Ten.AddHours(1));
        var cut = RenderBoard(agents: [planner]);

        FocusCard(cut, "t1");
        HoverColumn(cut, "planner");
        Column(cut, "planner").GetAttribute("data-preview").Should().Be("true");

        // The browser fires focusout of the first card and focusin of the next card of the SAME column back to
        // back. The leave must not clear the preview in between: the projection would blink off and on.
        var leave = cut.InvokeAsync(() => cut.Instance.OnAgentPreviewLeaveAsync(planner));
        await cut.InvokeAsync(() => cut.Instance.OnAgentPreviewEnter(planner));
        await leave;

        Column(cut, "planner").GetAttribute("data-preview").Should().Be("true");
        var preview = Column(cut, "planner").QuerySelector("[data-testid='delegate-agent-preview']")!;
        preview.GetAttribute("data-state").Should().Be("preview");
        preview.TextContent.Trim().Should().Be(
            "Occupied until " + Ten.AddHours(1).AddMinutes(30).ToString("t") + " with this task");
    }

    [Fact]
    public async Task LeavingTheColumnDropsThePreviewOnceTheGraceWindowCloses()
    {
        var planner = AgentColumn("planner", "Planner", busyUntil: Ten.AddHours(1));
        var cut = RenderBoard(agents: [planner]);

        FocusCard(cut, "t1");
        HoverColumn(cut, "planner");
        Column(cut, "planner").GetAttribute("data-preview").Should().Be("true");

        await cut.InvokeAsync(() => cut.Instance.OnAgentPreviewLeaveAsync(planner));

        Column(cut, "planner").GetAttribute("data-preview").Should().Be("false");
        Column(cut, "planner").QuerySelector("[data-testid='delegate-agent-preview']")!
            .GetAttribute("data-state").Should().Be("busy", "the agent's own load still shows once the card is gone");
    }

    // ---------- the menu (keyboard and tap alternative) ----------

    [Fact]
    public void EveryCardOffersAnAssignMenuWithEveryAgent()
    {
        var cut = RenderBoard();

        OpenMenu(Card(cut, "t1"));

        MenuItems(cut).Select(item => item.TextContent.Trim())
            .Should().Equal("Planner", "Writer", "Reviewer");
    }

    [Fact]
    public void AFullAgentIsNamedAsFullInTheMenu_AndCannotBePicked()
    {
        var cut = RenderBoard(agents:
        [
            AgentColumn("planner", "Planner", slots: 1, active: 1),
            AgentColumn("writer", "Writer"),
        ]);

        OpenMenu(Card(cut, "t1"));

        var items = MenuItems(cut);
        items[0].TextContent.Trim().Should().Be("Planner (full)");
        items[0].HasAttribute("disabled").Should().BeTrue("a menu item that cannot take the task is named, not hidden");
        items[1].HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AnAssignedCardAlsoOffersTheWayBackToTheBacklog()
    {
        var cut = RenderBoard();

        OpenMenu(Card(cut, "t3"));

        MenuItems(cut).Select(item => item.TextContent.Trim())
            .Should().Equal("Planner", "Writer", "Reviewer", "Move back to the backlog");
    }

    [Fact]
    public void TheAssignMenuReportsOneMoveAndShowsItAtOnce()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnAssign, Capture(request => raised = request)));

        OpenMenu(Card(cut, "t1"));
        MenuItems(cut)[1].Click();
        cut.WaitForAssertion(() => raised.Should().NotBeNull(), TimeSpan.FromSeconds(10));

        raised!.Task.Id.Should().Be("t1");
        raised.From.Should().BeNull("the task came from the backlog");
        raised.To!.Id.Should().Be("writer");
        raised.AssigneeId.Should().Be("writer");
        raised.IsAssignment.Should().BeTrue();

        CardTitles(Column(cut, "writer")).Should().Contain("Draft the summary",
            "the reader sees the move before the host round-trip");
        CardTitles(Column(cut, "backlog")).Should().Equal("Collect the invoices");
        Announcement(cut).Should().Be("Draft the summary assigned to Writer.");
    }

    [Fact]
    public void ReturningATaskReportsItAsAnUnassignment()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnUnassign, Capture(request => raised = request)));

        OpenMenu(Card(cut, "t3"));
        MenuItems(cut)[^1].Click();
        cut.WaitForAssertion(() => raised.Should().NotBeNull(), TimeSpan.FromSeconds(10));

        raised!.IsUnassignment.Should().BeTrue();
        raised.From!.Id.Should().Be("writer");
        raised.AssigneeId.Should().BeNull();
        CardTitles(Column(cut, "backlog")).Should().Contain("Review the draft");
        Announcement(cut).Should().Be("Review the draft moved back to the backlog.");
    }

    [Fact]
    public void MovingATaskToTheAgentThatAlreadyHasItReportsNothing()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnAssign, Capture(request => raised = request)));

        // The item for the card's own column is disabled, so the state cannot be reached from the menu; the direct path
        // still has to be a no-op instead of a duplicate change.
        cut.InvokeAsync(() => cut.Instance.MoveAsync(cut.Instance.AllTasks.Single(task => task.Id == "t3"),
            cut.Instance.FindAgent("writer")));

        raised.Should().BeNull();
    }

    // ---------- the drag (one commit from the shared module) ----------

    [Fact]
    public async Task ACommittedTransferFromTheBacklogAssignsTheTask()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnAssign, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("backlog", 0, "planner", 1, SortableReorderSource.Pointer)));

        raised.Should().NotBeNull();
        raised!.Task.Id.Should().Be("t1", "the first card of the backlog is the most urgent one");
        raised.To!.Id.Should().Be("planner");
        CardTitles(Column(cut, "planner")).Should().Equal("Draft the summary");
    }

    [Fact]
    public async Task ATransferBetweenTwoAgentsIsAHandover()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnAssign, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("writer", 0, "planner", 0, SortableReorderSource.Pointer)));

        raised.Should().NotBeNull();
        raised!.IsHandover.Should().BeTrue();
        raised.From!.Id.Should().Be("writer");
        raised.To!.Id.Should().Be("planner");
    }

    [Fact]
    public async Task ADropOnAFullAgentIsRefusedWithAnAnnouncementAndTheCardStaysPut()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(
            p => p.Add(c => c.OnAssign, Capture(request => raised = request)),
            agents:
            [
                AgentColumn("planner", "Planner", slots: 1, active: 1),
                AgentColumn("writer", "Writer"),
            ]);

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("backlog", 0, "planner", 0, SortableReorderSource.Pointer)));

        raised.Should().BeNull("a full column cannot take work, so nothing is reported");
        CardTitles(Column(cut, "planner")).Should().BeEmpty("the card stays where it was");
        CardTitles(Column(cut, "backlog")).Should().Contain("Draft the summary");
        Announcement(cut).Should().Be("Draft the summary not moved: Planner is full (1 of 1 running).");
        cut.Find("[data-testid='delegate-notice']").TextContent.Trim().Should().Be(Announcement(cut),
            "a reader who missed the announcement still sees why the card stayed put");
    }

    [Fact]
    public async Task ADropOnTheBacklogReturnsTheTask()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnUnassign, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("writer", 0, "backlog", 0, SortableReorderSource.Pointer)));

        raised.Should().NotBeNull();
        raised!.IsUnassignment.Should().BeTrue();
        raised.Task.Id.Should().Be("t3");
    }

    [Fact]
    public async Task ATransferOfAnIndexThatDoesNotExistChangesNothing()
    {
        DelegateMoveRequest? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.OnAssign, Capture(request => raised = request)));

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("backlog", 5, "planner", 0, SortableReorderSource.Pointer)));
        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(null!));

        raised.Should().BeNull();
    }

    [Fact]
    public void TheColumnsShareOneDragGroup_AndTheBacklogIdentifiesItself()
    {
        var cut = RenderBoard();

        var lists = cut.FindAll(".tm-sortable-list");
        lists.Should().HaveCount(4);
        cut.Instance.DragGroup.Should().NotBeNullOrEmpty();
        cut.Find("[data-testid='delegate-backlog-list']").GetAttribute("data-list-id").Should().Be("backlog");
        cut.FindAll("[data-testid='delegate-agent-list']").Select(list => list.GetAttribute("data-list-id"))
            .Should().Equal("planner", "writer", "reviewer");
        cut.Find("[data-testid='delegate-backlog-list']").GetAttribute("data-group")
            .Should().Be(cut.Instance.DragGroup);
    }

    [Fact]
    public void TheColumnsAreTransferOnlyLists_SoNothingPretendsToReorderInsideOne()
    {
        var cut = RenderBoard();

        cut.FindAll(".tm-sortable-list__item").Should().OnlyContain(item => item.GetAttribute("tabindex") == null);
        cut.FindAll(".tm-sortable-list__move").Should().BeEmpty();
    }

    // ---------- the automatic pass ----------

    [Fact]
    public void TheAutomaticPassHandsTheBacklogToTheFirstFreeAgent()
    {
        var raised = new List<DelegateMoveRequest>();
        var cut = RenderBoard(
            p => p.Add(c => c.OnAssign, Capture(request => raised.Add(request))),
            tasks: [Task("t1", "Draft"), Task("t2", "Collect"), Task("t3", "Review")],
            agents:
            [
                AgentColumn("planner", "Planner"),
                AgentColumn("writer", "Writer"),
                AgentColumn("reviewer", "Reviewer"),
            ]);

        cut.InvokeAsync(() => cut.Instance.OnAutoAssignChangedAsync(true));
        cut.WaitForAssertion(() => raised.Should().HaveCount(3));

        // Planner has two slots and the other two have one each, so the pass fills planner and then moves on: one render
        // never piles every task on one agent.
        raised.Select(request => request.To!.Id).Should().Equal("planner", "planner", "writer");
        cut.Instance.AutoAssign.Should().BeFalse("the parameter is the host's: the switch raises its change");
        Announcement(cut).Should().Be("3 tasks assigned automatically.");
    }

    [Fact]
    public void TheAutomaticPassPrefersTheAgentThatBecomesFreeFirst()
    {
        var raised = new List<DelegateMoveRequest>();
        var cut = RenderBoard(
            p => p.Add(c => c.OnAssign, Capture(request => raised.Add(request))),
            tasks: [Task("t1", "Draft")],
            agents:
            [
                AgentColumn("late", "Late", busyUntil: Ten.AddHours(3)),
                AgentColumn("soon", "Soon", busyUntil: Ten.AddMinutes(10)),
            ]);

        cut.InvokeAsync(() => cut.Instance.OnAutoAssignChangedAsync(true));
        cut.WaitForAssertion(() => raised.Should().HaveCount(1));

        raised[0].To!.Id.Should().Be("soon");
    }

    [Fact]
    public void TheAutomaticPassSaysWhenNobodyCanTakeATask()
    {
        var raised = new List<DelegateMoveRequest>();
        var cut = RenderBoard(
            p => p.Add(c => c.OnAssign, Capture(request => raised.Add(request))),
            tasks: [Task("t1", "Draft")],
            agents: [AgentColumn("planner", "Planner", slots: 1, active: 1)]);

        cut.InvokeAsync(() => cut.Instance.OnAutoAssignChangedAsync(true));
        cut.WaitForAssertion(() => Announcement(cut).Should().Be("No free agent for Draft."));

        raised.Should().BeEmpty();
        CardTitles(Column(cut, "backlog")).Should().Equal("Draft");
    }

    [Fact]
    public void TheAutomaticPassNeverFightsTheReader()
    {
        var raised = new List<DelegateMoveRequest>();
        var tasks = new[] { Task("t1", "Draft"), Task("t2", "Collect") };
        var agents = new[] { AgentColumn("planner", "Planner", slots: 4) };
        var cut = RenderBoard(
            p =>
            {
                p.Add(c => c.OnAssign, Capture(request => raised.Add(request)));
                p.Add(c => c.AutoAssign, true);
            },
            tasks: tasks,
            agents: agents);

        cut.WaitForAssertion(() => raised.Should().HaveCount(2, "auto mode places the backlog as it arrives"));
        raised.Clear();

        // The reader puts one task back; the host re-renders with auto mode still on.
        cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("planner", 0, "backlog", 0, SortableReorderSource.Pointer)));
        cut.WaitForAssertion(() => CardTitles(Column(cut, "backlog")).Should().HaveCount(1));

        cut.InvokeAsync(() => RerenderAsync(cut, tasks, agents, autoAssign: true, assign: request => raised.Add(request)));
        cut.WaitForAssertion(() => raised.Should().BeEmpty());

        CardTitles(Column(cut, "backlog")).Should().HaveCount(1,
            "the reader's return is final until the switch is re-armed");
    }

    [Fact]
    public void TheAutoSwitchRaisesItsOwnChange()
    {
        bool? raised = null;
        var cut = RenderBoard(p => p.Add(c => c.AutoAssignChanged, EventCallback.Factory.Create<bool>(this,
            value => raised = value)));

        cut.InvokeAsync(() => cut.Instance.OnAutoAssignChangedAsync(true));

        raised.Should().BeTrue();
        cut.Find(".tm-ai-delegate").GetAttribute("data-auto").Should().Be("true",
            "the reader sees the switch they flipped even when the host does not bind the two-way parameter");
    }

    // ---------- the overlay, the live region, localization ----------

    [Fact]
    public async Task TheReaderMoveSurvivesASameInstanceRerender_AndDropsWhenTheHostSendsANewOne()
    {
        var tasks = new List<DelegateTask> { Task("t1", "Draft"), Task("t2", "Collect") };
        var cut = RenderBoard(tasks: tasks);

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("backlog", 0, "planner", 0, SortableReorderSource.Pointer)));
        CardTitles(Column(cut, "planner")).Should().Equal("Draft");

        // The host re-renders the SAME instances (it has not applied the move yet): the reader's move stands.
        await cut.InvokeAsync(() => RerenderAsync(cut, tasks, Agents(), autoAssign: false));
        CardTitles(Column(cut, "planner")).Should().Equal("Draft");

        // A NEW instance is the host's word: the board follows it (here: it refused the move).
        var rejected = new List<DelegateTask> { new("t1", "Draft"), new("t2", "Collect") };
        await cut.InvokeAsync(() => RerenderAsync(cut, rejected, Agents(), autoAssign: false));
        CardTitles(Column(cut, "planner")).Should().BeEmpty();
        CardTitles(Column(cut, "backlog")).Should().HaveCount(2);
    }

    [Fact]
    public void TheBoardOwnsExactlyOnePoliteLiveRegion()
    {
        var cut = RenderBoard();

        var regions = cut.FindAll("[aria-live]");
        regions.Should().HaveCount(1);
        regions[0].GetAttribute("role").Should().Be("status");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("aria-atomic").Should().Be("true");
    }

    [Fact]
    public async Task TheAnnouncementIsRepeatedThroughAnInvisibleMarker()
    {
        var cut = RenderBoard(
            tasks: [Task("t1", "Draft"), Task("t2", "Collect")],
            agents: [AgentColumn("planner", "Planner", slots: 1, active: 1)]);

        // The refusal path names the same task twice, because nothing moves out of the backlog.
        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("backlog", 0, "planner", 0, SortableReorderSource.Pointer)));
        var first = Announcement(cut);

        await cut.InvokeAsync(() => cut.Instance.OnTransferAsync(
            new SortableTransferArgs("backlog", 0, "planner", 0, SortableReorderSource.Pointer)));
        var second = Announcement(cut);

        second.Should().NotBe(first, "a live region announces a MUTATION, so the same sentence needs a marker");
        second.Replace("\u200B", string.Empty).Should().Be(first,
            "the marker is invisible: the sentence a reader hears is the same one");
    }

    [Fact]
    public void NoUserVisibleStringIsHardCoded()
    {
        Services.AddSingleton<Tempo.Blazor.Localization.ITmLocalizer>(BuildCzechLocalizer());
        var cut = RenderBoard();

        cut.Find(".tm-ai-delegate__title").TextContent.Trim().Should().Be("Rozdělování úkolů");
        cut.Find(".tm-ai-delegate__instructions").TextContent.Should()
            .NotContain("Move a task", "the copy comes from the localizer, not from a literal in the markup");
    }

    private static AngleSharp.Dom.IElement Card(IRenderedComponent<TmDelegateBoard> cut, string taskId)
        => cut.Find($".tm-ai-delegate__task[data-task-id='{taskId}']");
}
