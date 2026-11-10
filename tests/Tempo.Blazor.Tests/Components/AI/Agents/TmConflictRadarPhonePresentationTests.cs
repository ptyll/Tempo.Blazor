using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan 7ba95ed6, Fáze 7 — the phone presentation of <c>TmConflictRadar</c>, the honest row/column counts and the
/// two-way directory collapse.
/// <para>
/// The surface has ONE computed model (<c>ConflictMatrix</c>) and TWO presentations of it: the file × agent table for a
/// container wide enough to scroll it, and a card per file with the agents' changes stacked under it once the container
/// is narrower than the shared mobile breakpoint. Exactly ONE of them is in the DOM at any moment — a second, hidden
/// copy would double every interactive radio group and lie to a screen reader about how many tables the page has.
/// </para>
/// <para>
/// The measurement is the <c>TmLayoutObserver</c>'s width report, so a test drives it through
/// <see cref="TmConflictRadar.OnContainerWidthAsync"/> instead of a browser: 375 px is a phone, 1440 px is a desktop,
/// and <c>Presentation</c> pins either one regardless of the measured width.
/// </para>
/// </summary>
public class TmConflictRadarPhonePresentationTests : LocalizationTestBase
{
    private static readonly AgentRef Ana = new("Ana", "a1") { Role = "Orchestrator" };
    private static readonly AgentRef Bo = new("Bo", "a2");
    private static readonly AgentRef Cy = new("Cy", "a3");

    private static ChangedPath Change(string path, ChangeKind kind, string? previous = null)
        => new(path, kind, previous);

    private static IReadOnlyList<AgentBranchChanges> Branches() =>
    [
        new(Ana, "feat/radar",
            [
                Change("src/AI/TmConflictRadar.razor", ChangeKind.Modified),
                Change("src/AI/ConflictMatrix.cs", ChangeKind.Added),
                Change("README.md", ChangeKind.Modified),
            ]),
        new(Bo, "feat/lanes",
            [
                Change("src/AI/TmConflictRadar.razor", ChangeKind.Modified),
                Change("docs/conflict-radar.md", ChangeKind.Added),
            ]),
        new(Cy, "feat/rename",
            [
                Change("README.md", ChangeKind.Deleted),
                Change("src/AI/TmConflictRadar.razor", ChangeKind.Renamed, "src/AI/Radar.razor"),
            ]),
    ];

    private IRenderedComponent<TmConflictRadar> RenderRadar(
        Action<ComponentParameterCollectionBuilder<TmConflictRadar>>? configure = null)
        => Render<TmConflictRadar>(p =>
        {
            p.Add(c => c.Agents, Branches());
            configure?.Invoke(p);
        });

    /// <summary>Drives the measurement the <c>TmLayoutObserver</c> would report from the real DOM.</summary>
    private static Task MeasureAsync(IRenderedComponent<TmConflictRadar> cut, double width)
        => cut.InvokeAsync(() => cut.Instance.OnContainerWidthAsync(width));

    /// <summary>Re-renders the SAME instance with a partial parameter view — how a host echoes a value back.</summary>
    private static Task RerenderAsync(
        IRenderedComponent<TmConflictRadar> cut,
        string name,
        object? value)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [name] = value })));

    private static string[] Paths(IRenderedComponent<TmConflictRadar> cut, string testId)
        => [.. cut.FindAll($"[data-testid='{testId}']").Select(element => element.GetAttribute("data-path")!)];

    // ── the measured presentation ────────────────────────────────────────────

    [Fact]
    public async Task APhoneContainer_DrawsOneCardPerFile_WithTheAgentsChangesUnderIt()
    {
        var cut = RenderRadar();

        await MeasureAsync(cut, 375);

        cut.Find("[data-testid='conflict-radar']").GetAttribute("data-presentation").Should().Be("cards");
        cut.FindAll("table").Should().BeEmpty(
            "the table is the WIDE presentation: leaving a horizontally scrolling table on a 375 px phone is exactly " +
            "the defect this presentation removes");
        cut.FindAll("[data-testid='radar-grid-wrap']").Should().BeEmpty("no scroller is rendered when there is no grid");

        var cards = cut.FindAll("[data-testid='radar-card']");
        cards.Should().HaveCount(4, "one card per changed file");
        Paths(cut, "radar-card").Should().BeEquivalentTo(
            "README.md",
            "docs/conflict-radar.md",
            "src/AI/ConflictMatrix.cs",
            "src/AI/TmConflictRadar.razor");

        // The cards ARE a list, so a screen reader announces how many files a directory holds.
        cut.FindAll("[data-testid='radar-cards'] ul > li[data-testid='radar-card']").Should().HaveCount(4);

        // The changes hang UNDER the file: the first card is a conflict whose agents are Ana and Cy.
        var first = cards[0];
        first.GetAttribute("data-conflict").Should().Be("true");
        first.TextContent.Should().Contain("README.md").And.Contain("Conflict");
        var changes = first.QuerySelectorAll("[data-testid='radar-card-change']");
        changes.Should().HaveCount(2, "only the agents that actually changed the file are listed under it");
        changes[0].GetAttribute("data-agent-id").Should().Be("a1");
        changes[0].GetAttribute("data-kind").Should().Be("modified");
        changes[0].TextContent.Should().Contain("Ana").And.Contain("Modified");
        changes[1].GetAttribute("data-kind").Should().Be("deleted");

        // A conflicting card still carries its own side choice, and the merge gate is unchanged.
        var sides = first.QuerySelectorAll("[role='radiogroup'] input[type='radio']");
        sides.Should().HaveCount(3, "Ana, Cy and the manual merge");
        first.QuerySelector("[role='radiogroup']")!.GetAttribute("aria-label").Should().Be("Resolve README.md");
    }

    [Fact]
    public async Task AWideContainer_KeepsTheTable_AndRendersNoCards()
    {
        var cut = RenderRadar();

        await MeasureAsync(cut, 1440);

        cut.Find("[data-testid='conflict-radar']").GetAttribute("data-presentation").Should().Be("grid");
        cut.Find("[data-testid='conflict-radar-grid']").TagName.Should().Be("TABLE");
        cut.FindAll("[data-testid='radar-card']").Should().BeEmpty(
            "the two presentations are alternatives, never duplicates");
    }

    [Fact]
    public void ThePresentationParameter_PinsASurface_WhateverTheContainerMeasures()
    {
        var pinnedGrid = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Grid));
        pinnedGrid.Find("[data-testid='conflict-radar']").GetAttribute("data-presentation").Should().Be("grid",
            "the surface is not measured yet, and a pinned grid never waits for one");

        var pinnedCards = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Cards));
        pinnedCards.Find("[data-testid='conflict-radar']").GetAttribute("data-presentation").Should().Be("cards");
        pinnedCards.FindAll("table").Should().BeEmpty();
    }

    [Fact]
    public async Task APinnedPresentation_IgnoresTheMeasurement()
    {
        var cut = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Grid));

        await MeasureAsync(cut, 375);

        cut.Find("[data-testid='conflict-radar']").GetAttribute("data-presentation").Should().Be("grid",
            "a host that pinned the table keeps the table on a phone");
        cut.FindAll("[data-testid='radar-card']").Should().BeEmpty();
    }

    [Fact]
    public async Task TheSurfaceSwitchesBack_WhenTheContainerGrows()
    {
        var cut = RenderRadar();

        await MeasureAsync(cut, 375);
        cut.FindAll("[data-testid='radar-card']").Should().HaveCount(4);

        await MeasureAsync(cut, 1200);
        cut.FindAll("[data-testid='radar-card']").Should().BeEmpty();
        cut.Find("[data-testid='conflict-radar-grid']").TagName.Should().Be("TABLE");
    }

    // ── one model, two presentations ─────────────────────────────────────────

    [Fact]
    public async Task TheCardsCarryTheSameChangesAsTheGrid()
    {
        var grid = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Grid));
        var cards = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Cards));

        IReadOnlyList<string> FromGrid() =>
        [
            .. grid.FindAll("[data-testid='radar-row']").SelectMany(row =>
                row.QuerySelectorAll("[data-testid='radar-cell']")
                    .Where(cell => cell.GetAttribute("data-kind") != "none")
                    .Select(cell =>
                        row.GetAttribute("data-path") + "|" + cell.GetAttribute("data-agent-id") + "|"
                        + cell.GetAttribute("data-kind"))),
        ];

        IReadOnlyList<string> FromCards() =>
        [
            .. cards.FindAll("[data-testid='radar-card']").SelectMany(card =>
                card.QuerySelectorAll("[data-testid='radar-card-change']")
                    .Select(change =>
                        card.GetAttribute("data-path") + "|" + change.GetAttribute("data-agent-id") + "|"
                        + change.GetAttribute("data-kind"))),
        ];

        FromCards().Should().Equal(FromGrid(),
            "the cards are a presentation of the SAME computed matrix, so a file, an agent and a kind cannot drift " +
            "between the two");

        // And the decisions are one model too: deciding in the cards settles the merge gate exactly like the grid.
        var group = cards.FindAll("[role='radiogroup']")[0];
        group.QuerySelectorAll("input[type='radio']")[0].Change(true);
        cards.Find("[data-testid='radar-merge-hint']").TextContent.Should().Contain("1 unresolved conflict");
    }

    [Fact]
    public async Task TheCardsKeepTheConflictBadgeAndTheLiveRegionSingular()
    {
        var cut = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Cards));

        cut.FindAll("[aria-live]").Should().ContainSingle("one announcement channel, whichever presentation is up");
        cut.FindAll("[data-testid='radar-row-conflict']").Should().HaveCount(2);
        cut.FindAll("[role='grid']").Should().BeEmpty(
            "a card list is a list; declaring a grid role would promise an arrow-key model no surface implements");

        await cut.InvokeAsync(() => cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click());
        cut.Find("[aria-live]").TextContent.Should().Contain("src/AI collapsed");
    }

    [Fact]
    public void TheCardsRespectTheConflictsOnlyFilter()
    {
        var cut = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Cards));

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(true);

        Paths(cut, "radar-card").Should().Equal("README.md", "src/AI/TmConflictRadar.razor");
        cut.FindAll("[data-testid='radar-card-directory']").Select(group => group.GetAttribute("data-directory"))
            .Should().Equal(string.Empty, "src/AI");
    }

    [Fact]
    public void TheCardsRespectACollapsedDirectory()
    {
        var cut = RenderRadar(p => p.Add(c => c.Presentation, ConflictRadarPresentation.Cards));
        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();

        Paths(cut, "radar-card").Should().Equal("README.md", "docs/conflict-radar.md");
        cut.FindAll("[data-testid='radar-card-directory']")
            .Single(group => group.GetAttribute("data-directory") == "src/AI")
            .GetAttribute("data-collapsed").Should().Be("true");
    }

    // ── the row and column counts stay honest ────────────────────────────────

    [Fact]
    public void TheTableDeclaresTheCountsItActuallyRenders()
    {
        var cut = RenderRadar();
        var table = cut.Find("[data-testid='conflict-radar-grid']");

        // header + root(1+1 file+1 resolution) + src/AI(1+2+1) + docs(1+1)
        table.GetAttribute("aria-rowcount").Should().Be("10");
        table.GetAttribute("aria-colcount").Should().Be("4", "the file column plus one column per agent");

        var rendered = cut.FindAll("tr").Count;
        rendered.Should().Be(10, "the declared count and the rendered rows are the same number");
    }

    [Fact]
    public void TheCountsFollowTheFilterAndTheCollapsedDirectories()
    {
        var cut = RenderRadar();
        var table = cut.Find("[data-testid='conflict-radar-grid']");

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(true);
        table = cut.Find("[data-testid='conflict-radar-grid']");
        table.GetAttribute("aria-rowcount").Should().Be("7", "header + root(1+1+1) + src/AI(1+1+1)");
        cut.FindAll("tr").Should().HaveCount(7);

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(false);
        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();
        table = cut.Find("[data-testid='conflict-radar-grid']");
        table.GetAttribute("aria-rowcount").Should().Be("7", "header + root(1+1+1) + src/AI(1) + docs(1+1)");
        cut.FindAll("tr").Should().HaveCount(7);
    }

    // ── the collapse is the host's state ─────────────────────────────────────

    [Fact]
    public void ACollapsedDirectoryIsReportedTwoWay()
    {
        IReadOnlySet<string>? reported = null;
        var cut = RenderRadar(p => p.Add(c => c.CollapsedDirectoriesChanged, value => reported = value));

        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();

        reported.Should().NotBeNull();
        reported!.Should().BeEquivalentTo(["src/AI"]);
        cut.Instance.IsCollapsed("src/AI").Should().BeTrue();
    }

    [Fact]
    public void AReSeededCollapseSet_IsWhatTheReaderSees()
    {
        var cut = RenderRadar(p => p.Add(c => c.CollapsedDirectories, new HashSet<string>(["docs"])));

        cut.Instance.IsCollapsed("docs").Should().BeTrue("the host's set is the initial state");
        Paths(cut, "radar-row").Should().NotContain("docs/conflict-radar.md");
    }

    [Fact]
    public async Task AHostThatDoesNotMoveTheSet_CannotUndoTheReader()
    {
        HashSet<string>? reported = null;
        var hostState = new HashSet<string>();
        var cut = RenderRadar(p =>
        {
            p.Add(c => c.CollapsedDirectories, hostState);
            p.Add(c => c.CollapsedDirectoriesChanged, value => reported = (HashSet<string>)value);
        });

        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();
        cut.Instance.IsCollapsed("src/AI").Should().BeTrue();
        reported.Should().NotBeNull().And.BeEquivalentTo(["src/AI"]);

        // The host re-renders the SAME instance it already had — that is not the host speaking.
        await RerenderAsync(cut, nameof(TmConflictRadar.CollapsedDirectories), hostState);
        cut.Instance.IsCollapsed("src/AI").Should().BeTrue("a re-render is not the host MOVING the parameter");

        // Even an echo of the reported set (a NEW, content-equal instance) keeps what the reader folded.
        await RerenderAsync(cut, nameof(TmConflictRadar.CollapsedDirectories), reported);
        cut.Instance.IsCollapsed("src/AI").Should().BeTrue();
    }

    [Fact]
    public async Task AHostThatMovesTheCollapseSet_TakesTheSurfaceWithIt()
    {
        var first = new HashSet<string>();
        var cut = RenderRadar(p =>
        {
            p.Add(c => c.CollapsedDirectories, first);
            p.Add(c => c.CollapsedDirectoriesChanged, _ => { });
        });

        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();
        cut.Instance.IsCollapsed("src/AI").Should().BeTrue();

        // A NEW instance is the host speaking: the reader's folds are replaced, not merged.
        await RerenderAsync(cut, nameof(TmConflictRadar.CollapsedDirectories), new HashSet<string>(["docs"]));
        cut.Instance.IsCollapsed("src/AI").Should().BeFalse();
        cut.Instance.IsCollapsed("docs").Should().BeTrue();
    }

    [Fact]
    public async Task ADirectoryThatNoLongerExists_LeavesTheCollapseSet()
    {
        var cut = RenderRadar(p => p.Add(c => c.CollapsedDirectories, new HashSet<string>(["src/AI"])));
        cut.Instance.IsCollapsed("src/AI").Should().BeTrue();

        // The host hands a completely different set of branches: the stale directory has nothing left to collapse.
        await cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmConflictRadar.Agents)] = new List<AgentBranchChanges>
                {
                    new(Ana, "feat/radar", [Change("README.md", ChangeKind.Modified)]),
                    new(Bo, "feat/lanes", [Change("README.md", ChangeKind.Added)]),
                },
                [nameof(TmConflictRadar.CollapsedDirectories)] = new HashSet<string>(["src/AI"]),
            })));

        cut.Instance.IsCollapsed("src/AI").Should().BeFalse("there is no src/AI group left to hide");
    }
}
