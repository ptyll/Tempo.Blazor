using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 3 — <c>TmAgentRunTree</c> (Tempo.Blazor.AI): the run of an agent as a
/// <c>role="treegrid"</c> over the six columns name / status / model / time / cost / tokens.
/// <para>
/// The tree comes from a FLAT node list (<see cref="AgentRunNode.ParentId"/>), a parent row shows its whole
/// subtree's numbers (marked as a summary), an orphan hangs under the root, a parent cycle is rejected, the
/// reader walks the grid with the APG treegrid keys, Enter selects (two-way
/// <c>@bind-SelectedNodeId</c> + <c>DetailTemplate</c>), the "show only" filter keeps the ancestors of the
/// rows it keeps, and a running node counts its live clock from the injected <see cref="TimeProvider"/>.
/// </para>
/// </summary>
public class TmAgentRunTreeTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private string? _selectedId;

    private readonly FakeTimeProvider _clock;

    /// <summary>
    /// The pinned clock every render reads: without it the component would read the machine's wall clock and
    /// every duration assertion would describe the test run, not the run it renders. It is registered in the
    /// constructor because bUnit's service provider locks after the first retrieval.
    /// </summary>
    public TmAgentRunTreeTests()
    {
        _clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(_clock);
    }

    private FakeTimeProvider UseFakeClock() => _clock;

    private static AgentRunNode Node(
        string id,
        string title,
        string? parentId = null,
        AgentStepStatus status = AgentStepStatus.Done,
        string? model = null,
        long? cost = null,
        long? tokens = null,
        int? startedSeconds = null,
        int? completedSeconds = null)
        => new(title, id)
        {
            ParentId = parentId,
            Status = status,
            Model = model,
            CostMicroUnits = cost,
            Tokens = tokens,
            StartedAt = startedSeconds is { } start ? Start.AddSeconds(start) : null,
            CompletedAt = completedSeconds is { } end ? Start.AddSeconds(end) : null,
        };

    /// <summary>A three-level run: the root, a planner with a lookup under it, and a writer.</summary>
    private static AgentRunNode[] Run() =>
    [
        Node("root", "Nightly reconciliation", status: AgentStepStatus.Running, model: "gpt-4o",
            cost: 1_000_000, tokens: 1_000, startedSeconds: 0),
        Node("planner", "Plan the reconciliation", "root", model: "gpt-4o",
            cost: 500_000, tokens: 400, startedSeconds: 2, completedSeconds: 8),
        Node("lookup", "Look up the ledger", "planner", status: AgentStepStatus.Failed,
            model: "gpt-4o-mini", cost: 250_000, tokens: 100, startedSeconds: 8, completedSeconds: 13),
        Node("writer", "Write the summary", "root", status: AgentStepStatus.Pending,
            model: "gpt-4o", cost: 750_000, tokens: 600),
    ];

    private IRenderedComponent<TmAgentRunTree> RenderTree(
        IReadOnlyList<AgentRunNode>? nodes = null,
        Action<ComponentParameterCollectionBuilder<TmAgentRunTree>>? configure = null)
    {
        _selectedId = null;
        UseFakeClock();

        return Render<TmAgentRunTree>(parameters =>
        {
            parameters
                .Add(tree => tree.Nodes, nodes ?? Run())
                .Add(tree => tree.Culture, CultureInfo.GetCultureInfo("en-US"))
                .Add(tree => tree.SelectedNodeIdChanged,
                    EventCallback.Factory.Create<string?>(this, id => _selectedId = id));

            configure?.Invoke(parameters);
        });
    }

    private static IReadOnlyList<IElement> Rows(IRenderedComponent<TmAgentRunTree> cut)
        => cut.FindAll("[data-testid='agent-run-tree-row']").ToList();

    private static string TextOf(IRenderedComponent<TmAgentRunTree> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static IReadOnlyList<string> RowIds(IRenderedComponent<TmAgentRunTree> cut)
        => Rows(cut).Select(row => row.GetAttribute("data-node-id")!).ToList();

    private static string Cell(IElement row, string column) => row.QuerySelector($".tm-ai-agent-run-tree__{column}")!.TextContent.Trim();

    private static string Announcement(IRenderedComponent<TmAgentRunTree> cut)
        => cut.Find("[data-testid='agent-run-tree-announcement']").TextContent.Trim();

    private static IElement ActiveRow(IRenderedComponent<TmAgentRunTree> cut)
        => Rows(cut).Single(row => row.GetAttribute("tabindex") == "0");

    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (string?)((ElementReference)invocation.Arguments[0]!).Id)
            .ToList();

    private static Task ApplySelectedAsync(IRenderedComponent<TmAgentRunTree> cut, string? id)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmAgentRunTree.SelectedNodeId)] = id })));

    private static async Task PumpAsync<TComponent>(IRenderedComponent<TComponent> cut, int cycles = 6)
        where TComponent : IComponent
    {
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            await cut.InvokeAsync(() => { });
        }
    }

    // ── Structure ────────────────────────────────────────────────────────────

    [Fact]
    public void TheRun_IsATreegrid_WithTheSixColumns()
    {
        var cut = RenderTree();

        var grid = cut.Find("[data-testid='agent-run-tree-grid']");
        grid.GetAttribute("role").Should().Be("treegrid");
        grid.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty("the rendered heading names the grid");
        grid.GetAttribute("aria-describedby").Should().NotBeNullOrEmpty("the hidden instructions describe it");
        grid.GetAttribute("aria-rowcount").Should().Be("5", "four rows plus the header row");
        grid.GetAttribute("aria-colcount").Should().Be("6");

        var headers = grid.QuerySelectorAll("[role='columnheader']").Select(cell => cell.TextContent.Trim()).ToList();
        headers.Should().Equal("Name", "Status", "Model", "Time", "Cost", "Tokens");
    }

    [Fact]
    public void EveryRow_CarriesItsLevel_StateAndRovingTabStop()
    {
        var cut = RenderTree();

        var rows = Rows(cut);
        rows.Select(row => row.GetAttribute("aria-level")).Should().Equal("1", "2", "3", "2");
        rows.Select(row => row.GetAttribute("data-status")).Should().Equal("running", "done", "failed", "pending");
        rows.Select(row => row.GetAttribute("aria-expanded")).Should().Equal("true", "true", null, null);
        rows.Select(row => row.GetAttribute("tabindex")).Should().Equal("0", "-1", "-1", "-1");

        // The state is never colour only: an icon AND the word, and the kind is spelled out for a screen
        // reader while its icon stays decorative.
        Cell(rows[2], "status").Should().Be("Failed");
        rows[2].QuerySelector(".tm-ai-agent-run-tree__status [aria-hidden='true']").Should().NotBeNull();
        Cell(rows[1], "model").Should().Be("gpt-4o");
        rows[0].QuerySelector(".tm-ai-agent-run-tree__kind [aria-hidden='true']").Should().NotBeNull();
        rows[0].TextContent.Should().Contain("Agent");
    }

    [Fact]
    public void AnUnknownModelAndAnUnreportedValue_RenderADash_NotAnEmptyCell()
    {
        var cut = RenderTree(
        [
            Node("a", "Only node"),
        ]);

        var row = Rows(cut)[0];
        Cell(row, "model").Should().Be("—");
        row.QuerySelector(".tm-ai-agent-run-tree__model")!.GetAttribute("aria-label").Should().Be("Not reported");
        Cell(row, "cost").Should().Be("—");
        Cell(row, "tokens").Should().Be("—");
        Cell(row, "time").Should().Be("—");
    }

    [Fact]
    public void AParentRow_ShowsTheSubtreeTotal_MarkedAsASummary()
    {
        var cut = RenderTree();

        var root = Rows(cut)[0];
        Cell(root, "cost").Should().Contain("$2.50", "1.00 + 0.50 + 0.25 + 0.75");
        Cell(root, "tokens").Should().Contain("2,100");
        Cell(root, "time").Should().Contain("0:11", "6 + 5 recorded seconds, plus the root's live clock");

        root.QuerySelector(".tm-ai-agent-run-tree__cost")!.GetAttribute("data-rollup").Should().Be("true");
        root.QuerySelector(".tm-ai-agent-run-tree__cost")!.GetAttribute("aria-label")
            .Should().Be("Total for Nightly reconciliation including its sub-agents: $2.50");
        root.QuerySelector(".tm-ai-agent-run-tree__tokens")!.GetAttribute("data-rollup").Should().Be("true");
        root.QuerySelector(".tm-ai-agent-run-tree__time")!.GetAttribute("data-rollup").Should().Be("true");

        // A leaf row is not a summary and shows its own numbers.
        var writer = Rows(cut)[3];
        writer.QuerySelector(".tm-ai-agent-run-tree__cost")!.GetAttribute("data-rollup").Should().BeNull();
        Cell(writer, "cost").Should().Be("$0.75");
    }

    [Fact]
    public void AParentWhoseSubtreeReportsNothing_ShowsAPlainDash_NotASummary()
    {
        var cut = RenderTree(
        [
            Node("root", "Container"),
            Node("child", "Ledger tool", "root"),
        ]);

        var root = Rows(cut)[0];
        Cell(root, "cost").Should().Be("—");
        Cell(root, "tokens").Should().Be("—");
        Cell(root, "time").Should().Be("—");

        // A Σ over "not reported" would claim a total that does not exist.
        root.QuerySelector(".tm-ai-agent-run-tree__cost")!.GetAttribute("data-rollup").Should().BeNull();
        root.QuerySelector(".tm-ai-agent-run-tree__cost")!.GetAttribute("aria-label").Should().BeNull();
        root.QuerySelector(".tm-ai-agent-run-tree__sum").Should().BeNull();
    }

    [Fact]
    public void ACycleIsRejected_AndAnOrphanIsMarked()
    {
        var cut = RenderTree(
        [
            Node("a", "Loops to b", "b"),
            Node("b", "Loops to a", "a"),
            Node("stray", "No parent in the run", "ghost"),
        ]);

        RowIds(cut).Should().Equal("stray");
        cut.Find("[data-testid='agent-run-tree-row']").TextContent.Should().Contain("Orphan");
        cut.Find("[data-testid='agent-run-tree-grid']").GetAttribute("aria-rowcount").Should().Be("2");
    }

    [Fact]
    public void AnEmptyRun_RendersTheEmptyState_InsteadOfAnEmptyTreegrid()
    {
        var cut = RenderTree([]);

        TextOf(cut, "agent-run-tree-empty").Should().Be("No nodes yet.");
        cut.FindAll("[data-testid='agent-run-tree-grid']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-run-tree-announcement']").Should().HaveCount(1);
    }

    // ── Keyboard (APG treegrid) ──────────────────────────────────────────────

    [Fact]
    public void TheArrowKeys_WalkTheVisibleRows_AndHomeAndEndJump()
    {
        var cut = RenderTree();

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("planner");

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("lookup");

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("planner");

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "End" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("writer");

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "Home" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("root");

        FocusTargets().Should().NotBeEmpty("the roving tabindex moves the focus with it");
        Announcement(cut).Should().BeEmpty("moving the focus is not a selection and is not announced");
    }

    [Fact]
    public void ArrowRightWalksOrExpands_AndArrowLeftCollapsesOrWalksToTheParent()
    {
        var cut = RenderTree();

        // An EXPANDED row walks into its first child.
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("planner");
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("lookup");

        // A leaf walks to its parent, and a COLLAPSED row expands instead of moving.
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("planner");
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        RowIds(cut).Should().Equal("root", "planner", "writer");
        Announcement(cut).Should().Be("Plan the reconciliation collapsed.");

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        RowIds(cut).Should().Equal("root", "planner", "lookup", "writer");
        Announcement(cut).Should().Be("Plan the reconciliation expanded.");

        // Collapsed again, the same key now walks to the parent.
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });
        ActiveRow(cut).GetAttribute("data-node-id").Should().Be("root");
        RowIds(cut).Should().Equal("root", "planner", "writer");
    }

    [Fact]
    public void EnterSelectsTheActiveRow_AndReportsItTwoWay()
    {
        var cut = RenderTree();

        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _selectedId.Should().Be("planner", "Enter selects the focused row");
        Rows(cut)[1].GetAttribute("aria-selected").Should().Be("true");
        Announcement(cut).Should().Be("Selected Plan the reconciliation.");

        // Space is the other activation key of a grid, and it fires on KEYUP like a native button.
        ActiveRow(cut).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveRow(cut).KeyUp(new KeyboardEventArgs { Key = " " });
        _selectedId.Should().Be("lookup");
    }

    [Fact]
    public void ARepeatedEnter_IsNotASecondActivation()
    {
        var cut = RenderTree();

        Rows(cut)[0].KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });

        _selectedId.Should().BeNull("an auto-repeated key is not a second activation");
        Announcement(cut).Should().BeEmpty();
    }

    // ── Selection and detail ─────────────────────────────────────────────────

    [Fact]
    public async Task TheDetailTemplate_RendersForTheSelectedNode_AndFollowsTheBinding()
    {
        var cut = RenderTree(configure: parameters => parameters
            .Add(tree => tree.DetailTemplate, (AgentRunNode node) => builder =>
            {
                builder.OpenElement(0, "p");
                builder.AddAttribute(1, "data-testid", "detail-body");
                builder.AddContent(2, "Detail: " + node.Title);
                builder.CloseElement();
            }));

        cut.FindAll("[data-testid='agent-run-tree-detail']").Should().BeEmpty();

        Rows(cut)[2].Click();
        _selectedId.Should().Be("lookup");

        var detail = cut.Find("[data-testid='agent-run-tree-detail']");
        detail.GetAttribute("role").Should().Be("region");
        detail.GetAttribute("aria-label").Should().Be("Node detail");
        detail.QuerySelector("[data-testid='detail-body']")!.TextContent.Should().Be("Detail: Look up the ledger");

        // The host drives the binding: a new id renders a new detail.
        await ApplySelectedAsync(cut, "writer");
        cut.Find("[data-testid='agent-run-tree-detail'] [data-testid='detail-body']").TextContent
            .Should().Be("Detail: Write the summary");
        Rows(cut)[3].GetAttribute("aria-selected").Should().Be("true");
    }

    // ── Filter ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheShowOnlyFilter_KeepsTheAncestorsOfWhatItKeeps()
    {
        var cut = RenderTree();

        cut.FindAll(".tm-segmented__option").Single(option => option.TextContent.Contains("Failed")).Click();

        RowIds(cut).Should().Equal("root", "planner", "lookup");
        Rows(cut).Select(row => row.GetAttribute("data-status")).Should().Equal("running", "done", "failed");
        Announcement(cut).Should().Be("Showing 3 of 4 nodes.");

        cut.FindAll(".tm-segmented__option").Single(option => option.TextContent.Contains("All")).Click();
        Rows(cut).Should().HaveCount(4);

        cut.FindAll(".tm-segmented__option").Single(option => option.TextContent.Contains("Running")).Click();
        RowIds(cut).Should().Equal("root");
    }

    [Fact]
    public void TheFilterIsBound_AndAHostValueWinsOnTheNextRender()
    {
        var cut = RenderTree(configure: parameters => parameters
            .Add(tree => tree.ShowOnly, AgentRunFilter.Failed)
            .Add(tree => tree.ShowOnlyChanged, EventCallback.Factory.Create<AgentRunFilter>(this, _ => { })));

        RowIds(cut).Should().Equal("root", "planner", "lookup");

        cut.Find("[data-testid='agent-run-tree-filter']").Should().NotBeNull();
        cut.Find("[data-testid='agent-run-tree']").GetAttribute("data-filter").Should().Be("failed");
    }

    // ── Live clock ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ARunningNode_CountsItsLiveClock_FromTheInjectedTimeProvider()
    {
        var clock = UseFakeClock();
        var cut = RenderTree();

        Cell(Rows(cut)[0], "time").Should().Contain("0:11");

        clock.Advance(TimeSpan.FromSeconds(5));
        await PumpAsync(cut);

        Cell(Rows(cut)[0], "time").Should().Contain("0:16", "the running root keeps counting");
        Cell(Rows(cut)[1], "time").Should().Contain("0:11", "a parent row shows its subtree total");
        Cell(Rows(cut)[2], "time").Should().Contain("0:05", "a finished leaf keeps its recorded duration");

        cut.Instance.Dispose();
    }

    [Fact]
    public async Task TheTimeProviderParameter_WinsOverTheRegisteredClock()
    {
        UseFakeClock();
        var pinned = new FakeTimeProvider(Start.AddHours(1));
        var cut = RenderTree(
        [
            Node("a", "Running node", status: AgentStepStatus.Running, startedSeconds: 0),
        ], parameters => parameters.Add(tree => tree.TimeProvider, pinned));

        Cell(Rows(cut)[0], "time").Should().Contain("1:00:00");

        pinned.Advance(TimeSpan.FromSeconds(1));
        await PumpAsync(cut, 3);

        Cell(Rows(cut)[0], "time").Should().Contain("1:00:01");
        cut.Instance.Dispose();
    }

    // ── Accessibility bookkeeping ────────────────────────────────────────────

    [Fact]
    public void TheTree_HasExactlyOneLiveRegion_AndNoValueIsLiveByItself()
    {
        var cut = RenderTree();

        var live = cut.FindAll("[aria-live]").Where(element => element.GetAttribute("aria-live") != "off").ToList();
        live.Should().ContainSingle("one polite region carries every announcement");
        live[0].GetAttribute("aria-live").Should().Be("polite");
        live[0].GetAttribute("role").Should().Be("status");
        live[0].GetAttribute("aria-atomic").Should().Be("true");
    }

    [Fact]
    public void TheDisclosureButton_IsNotASecondTabStop_AndStopsItsClick()
    {
        var cut = RenderTree();

        // The root's disclosure hides both of its children; the planner's hides only its own.
        var rootToggle = Rows(cut)[0].QuerySelector(".tm-ai-agent-run-tree__toggle")!;
        rootToggle.GetAttribute("tabindex").Should().Be("-1");
        rootToggle.GetAttribute("aria-hidden").Should().Be("true");
        rootToggle.GetAttribute("aria-label").Should().Be("Collapse Nightly reconciliation");

        var toggle = Rows(cut)[1].QuerySelector(".tm-ai-agent-run-tree__toggle")!;
        toggle.GetAttribute("aria-label").Should().Be("Collapse Plan the reconciliation");

        toggle.Click();
        RowIds(cut).Should().Equal("root", "planner", "writer");
        Announcement(cut).Should().Be("Plan the reconciliation collapsed.");
    }

    [Fact]
    public void AWhitespaceTitle_ReadsAsUntitled_AndStaysNameable()
    {
        var cut = RenderTree(
        [
            Node("a", "   "),
        ]);

        Rows(cut)[0].QuerySelector(".tm-ai-agent-run-tree__label")!.TextContent.Should().Be("Untitled node");
        Rows(cut)[0].QuerySelector(".tm-ai-agent-run-tree__label")!.GetAttribute("data-placeholder").Should().Be("true");
    }

    /// <summary>
    /// The name and model cells clip to one line inside the horizontally scrolling grid, and a host without a
    /// <c>DetailTemplate</c> has no other place to read a long title in full: both cells carry the whole text in
    /// their <c>title</c>. A cell that reports nothing keeps no title (an em dash has nothing to reveal).
    /// </summary>
    [Fact]
    public void ALongNameAndModel_AreReadableInFull_ThroughTheirTitle()
    {
        const string title = "Look up every unsettled ledger entry of the last quarter";
        const string model = "openai/gpt-4o-2026-01-01-preview";

        var cut = RenderTree(
        [
            Node("a", title, model: model),
            Node("b", "No model reported"),
        ]);

        var named = Rows(cut)[0];
        named.QuerySelector(".tm-ai-agent-run-tree__label")!.GetAttribute("title").Should().Be(title);
        named.QuerySelector(".tm-ai-agent-run-tree__model")!.GetAttribute("title").Should().Be(model);

        var unreported = Rows(cut)[1];
        unreported.QuerySelector(".tm-ai-agent-run-tree__model")!.GetAttribute("title").Should().BeNull();
    }
}
