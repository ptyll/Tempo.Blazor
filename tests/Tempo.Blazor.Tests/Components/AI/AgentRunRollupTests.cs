using FluentAssertions;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 3 — the pure half of <c>TmAgentRunTree</c>: the subtree roll-up
/// (<see cref="AgentRunRollup"/>) that turns a flat node list into the "this node and everything under it"
/// numbers a parent row shows, the layout that turns the same list into a forest (orphans attached, cycles
/// rejected, duplicates dropped), and the cost formatter that renders micro-units in the reader's currency.
/// <para>
/// The roll-up is deliberately a PUBLIC helper: a host that opens a detail panel or a summary elsewhere
/// computes exactly what the row shows, instead of re-deriving it from a private implementation.
/// </para>
/// </summary>
public class AgentRunRollupTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static AgentRunNode Node(
        string id,
        string? parentId = null,
        AgentStepStatus status = AgentStepStatus.Done,
        long? cost = null,
        long? tokens = null,
        int? startedSeconds = 0,
        int? completedSeconds = null)
        => new(id, id)
        {
            ParentId = parentId,
            Status = status,
            CostMicroUnits = cost,
            Tokens = tokens,
            StartedAt = startedSeconds is { } start ? Start.AddSeconds(start) : null,
            CompletedAt = completedSeconds is { } end ? Start.AddSeconds(end) : null,
        };

    /// <summary>A three-level run: root → (planner → lookup) + writer.</summary>
    private static AgentRunNode[] Forest() =>
    [
        Node("root", cost: 1_000_000, tokens: 1_000, completedSeconds: 10),
        Node("planner", "root", cost: 500_000, tokens: 400, startedSeconds: 10, completedSeconds: 16),
        Node("lookup", "planner", cost: 250_000, tokens: 100, startedSeconds: 16, completedSeconds: 21),
        Node("writer", "root", cost: 750_000, tokens: 600, startedSeconds: 21, completedSeconds: 30),
    ];

    [Fact]
    public void ARootRollup_SumsCostTokensAndDurationOverItsWholeSubtree()
    {
        var rollup = AgentRunRollup.For("root", Forest(), Start.AddMinutes(1));

        rollup.CostMicroUnits.Should().Be(2_500_000, "the node's own cost plus every descendant's");
        rollup.Tokens.Should().Be(2_100);
        rollup.Duration.Should().Be(TimeSpan.FromSeconds(30), "10 + 6 + 5 + 9 seconds");
        rollup.NodeCount.Should().Be(4);
        rollup.DescendantCount.Should().Be(3);
        rollup.IsSummary.Should().BeTrue();
    }

    [Fact]
    public void AmIntermediateRollup_CountsOnlyItsOwnSubtree()
    {
        var rollup = AgentRunRollup.For("planner", Forest(), Start.AddMinutes(1));

        rollup.CostMicroUnits.Should().Be(750_000);
        rollup.Tokens.Should().Be(500);
        rollup.Duration.Should().Be(TimeSpan.FromSeconds(11));
        rollup.NodeCount.Should().Be(2);
    }

    [Fact]
    public void ALeafRollup_IsItsOwnNumbers_AndIsNotASummary()
    {
        var rollup = AgentRunRollup.For("writer", Forest(), Start.AddMinutes(1));

        rollup.CostMicroUnits.Should().Be(750_000);
        rollup.Tokens.Should().Be(600);
        rollup.Duration.Should().Be(TimeSpan.FromSeconds(9));
        rollup.NodeCount.Should().Be(1);
        rollup.DescendantCount.Should().Be(0);
        rollup.IsSummary.Should().BeFalse();
    }

    /// <summary>
    /// A running node has no <c>CompletedAt</c>: its contribution is the time between its start and the
    /// caller's "now". A finished sibling keeps its recorded duration, so the sum mixes facts and one
    /// live estimate exactly as the screen does.
    /// </summary>
    [Fact]
    public void ARunningNodeWithoutCompletedAt_CountsUpToNow()
    {
        var nodes = new[]
        {
            Node("root", status: AgentStepStatus.Running, startedSeconds: null, cost: 100),
            Node("done", "root", startedSeconds: 0, completedSeconds: 4),
            Node("live", "root", status: AgentStepStatus.Running, startedSeconds: 10),
        };

        var at30 = AgentRunRollup.For("root", nodes, Start.AddSeconds(30));
        var at40 = AgentRunRollup.For("root", nodes, Start.AddSeconds(40));

        at30.Duration.Should().Be(TimeSpan.FromSeconds(24), "4 recorded seconds plus 20 live ones");
        at40.Duration.Should().Be(TimeSpan.FromSeconds(34), "the live child keeps counting");
        at30.CostMicroUnits.Should().Be(100, "a node that reports no cost does not add a zero");
    }

    [Fact]
    public void ANodeThatReportsNeitherCostNorTokens_KeepsThemUnreported()
    {
        var nodes = new[]
        {
            Node("root", startedSeconds: null),
            Node("child", "root", startedSeconds: null),
        };

        var rollup = AgentRunRollup.For("root", nodes, Start.AddMinutes(1));

        rollup.CostMicroUnits.Should().BeNull("not reported is not the same as zero");
        rollup.Tokens.Should().BeNull();
        rollup.Duration.Should().BeNull("no node carries a start");
    }

    [Fact]
    public void AnUnknownNodeId_YieldsAnEmptyRollup()
    {
        var rollup = AgentRunRollup.For("missing", Forest(), Start.AddMinutes(1));

        rollup.NodeCount.Should().Be(0);
        rollup.CostMicroUnits.Should().BeNull();
        rollup.Duration.Should().BeNull();
    }

    /// <summary>
    /// A host can hand the component a malformed run. A parent cycle must terminate (a tree that loops
    /// is a denial of service, not a rendering bug) and every member still counts exactly once.
    /// </summary>
    [Fact]
    public void ACycle_Terminates_AndCountsEveryMemberOnce()
    {
        var nodes = new[]
        {
            Node("a", "b", cost: 1, completedSeconds: 1),
            Node("b", "a", cost: 2),
            Node("self", "self", cost: 4),
        };

        var rollup = AgentRunRollup.For("a", nodes, Start.AddMinutes(1));

        rollup.NodeCount.Should().Be(2, "a and b, each visited once");
        rollup.CostMicroUnits.Should().Be(3);

        AgentRunRollup.For("self", nodes, Start.AddMinutes(1)).NodeCount.Should().Be(1);
    }

    [Fact]
    public void ForAll_ReturnsOneRollupPerNode()
    {
        var all = AgentRunRollup.ForAll(Forest(), Start.AddMinutes(1));

        all.Keys.Should().BeEquivalentTo("root", "planner", "lookup", "writer");
        all["root"].CostMicroUnits.Should().Be(2_500_000);
        all["writer"].IsSummary.Should().BeFalse();
    }

    [Fact]
    public void ADuplicateId_IsReadOnceByTheLookup()
    {
        var nodes = new[]
        {
            Node("root", cost: 1),
            Node("child", "root", cost: 2),
            Node("child", "root", cost: 99),
        };

        AgentRunRollup.For("root", nodes, Start.AddMinutes(1)).CostMicroUnits
            .Should().Be(3, "the first node with an id wins, exactly like the layout's duplicate rule");
    }

    // ── The layout: orphans, cycles, duplicates, filtering, roll-ups ──────────

    [Fact]
    public void TheLayout_FlattensTheForestInPreOrder_WithDepth()
    {
        var layout = AgentRunTreeLayout.Build(Forest(), AgentRunFilter.All, new HashSet<string>(), Start.AddMinutes(1));

        layout.Rows.Select(row => (row.Node.Id, row.Depth)).Should().Equal(
            ("root", 0), ("planner", 1), ("lookup", 2), ("writer", 1));
        layout.Rows.Select(row => row.Index).Should().Equal(0, 1, 2, 3);
        layout.Rows.Select(row => row.HasChildren).Should().Equal(true, true, false, false);
    }

    [Fact]
    public void TheLayout_AttachesAnOrphanUnderTheRoot_InsteadOfDroppingIt()
    {
        var nodes = new[]
        {
            Node("root"),
            Node("child", "root"),
            Node("stray", "does-not-exist"),
            Node("stray-child", "stray"),
        };

        var layout = AgentRunTreeLayout.Build(nodes, AgentRunFilter.All, new HashSet<string>(), Start.AddMinutes(1));

        layout.Rows.Select(row => row.Node.Id).Should().Equal("root", "child", "stray", "stray-child");
        layout.Rows.Single(row => row.Node.Id == "stray").IsOrphan.Should().BeTrue();
        layout.Rows.Single(row => row.Node.Id == "stray-child").IsOrphan.Should().BeFalse(
            "a node whose parent is present is not an orphan, even when that parent is one");
        layout.OrphanCount.Should().Be(1);
    }

    [Fact]
    public void TheLayout_RejectsACycle_AndHangsItsChildrenUnderTheRoot()
    {
        var nodes = new[]
        {
            Node("a", "b"),
            Node("b", "a"),
            Node("child-of-a", "a"),
            Node("clean"),
        };

        var layout = AgentRunTreeLayout.Build(nodes, AgentRunFilter.All, new HashSet<string>(), Start.AddMinutes(1));

        layout.Rows.Select(row => row.Node.Id).Should().Equal("child-of-a", "clean");
        layout.RejectedCycleCount.Should().Be(2);
        layout.Rows.Single(row => row.Node.Id == "child-of-a").IsOrphan.Should().BeTrue(
            "its parent was rejected, so it hangs under the root");
    }

    [Fact]
    public void TheLayout_DropsADuplicateId()
    {
        var nodes = new[]
        {
            Node("root"),
            Node("child", "root"),
            Node("child", "root"),
        };

        var layout = AgentRunTreeLayout.Build(nodes, AgentRunFilter.All, new HashSet<string>(), Start.AddMinutes(1));

        layout.Rows.Select(row => row.Node.Id).Should().Equal("root", "child");
        layout.DuplicateCount.Should().Be(1);
    }

    [Fact]
    public void TheLayout_HonoursTheCollapsedSet_AndReportsHiddenDescendants()
    {
        var layout = AgentRunTreeLayout.Build(
            Forest(), AgentRunFilter.All, new HashSet<string> { "planner" }, Start.AddMinutes(1));

        layout.Rows.Select(row => row.Node.Id).Should().Equal("root", "planner", "writer");
        layout.Rows.Single(row => row.Node.Id == "planner").IsCollapsed.Should().BeTrue();
        layout.Rows.Single(row => row.Node.Id == "planner").HasChildren.Should().BeTrue();
    }

    [Fact]
    public void TheLayout_FiltersToRunningNodes_ButKeepsTheirAncestors()
    {
        var nodes = new[]
        {
            Node("root", status: AgentStepStatus.Running),
            Node("done", "root", status: AgentStepStatus.Done),
            Node("live", "root", status: AgentStepStatus.Running),
            Node("nested", "live", status: AgentStepStatus.Done),
            Node("failed", "root", status: AgentStepStatus.Failed),
        };

        var running = AgentRunTreeLayout.Build(nodes, AgentRunFilter.Running, new HashSet<string>(), Start.AddMinutes(1));
        running.Rows.Select(row => row.Node.Id).Should().Equal("root", "live");

        var failed = AgentRunTreeLayout.Build(nodes, AgentRunFilter.Failed, new HashSet<string>(), Start.AddMinutes(1));
        failed.Rows.Select(row => row.Node.Id).Should().Equal("root", "failed");

        var all = AgentRunTreeLayout.Build(nodes, AgentRunFilter.All, new HashSet<string>(), Start.AddMinutes(1));
        all.Rows.Should().HaveCount(5);
    }

    [Fact]
    public void TheLayout_CarriesTheRollupOfEveryRow()
    {
        var layout = AgentRunTreeLayout.Build(Forest(), AgentRunFilter.All, new HashSet<string>(), Start.AddMinutes(1));

        var root = layout.Rows[0];
        root.Rollup.CostMicroUnits.Should().Be(2_500_000);
        root.IsSummary.Should().BeTrue();
        layout.Rows[3].IsSummary.Should().BeFalse();
    }

    [Fact]
    public void TheCostFormatter_ReadsMicroUnits_InTheCulturesCurrency()
    {
        AiCostFormatter.Format(1_500_000, System.Globalization.CultureInfo.GetCultureInfo("en-US"))
            .Should().Be("$1.50");
        var czech = System.Globalization.CultureInfo.GetCultureInfo("cs-CZ");
        AiCostFormatter.Format(1_500_000, czech).Should().Be(1.5m.ToString("C2", czech));
        AiCostFormatter.Format(1_500_000, czech).Should().Contain("Kč");
    }

    /// <summary>
    /// A cent is the unit a reader thinks in, but an agent run routinely costs a fraction of one. A plain
    /// currency format would render every such row as "0.00" — so the formatter keeps micro-unit precision
    /// below one hundredth of a unit.
    /// </summary>
    [Fact]
    public void TheCostFormatter_KeepsMicroUnitPrecisionBelowACent()
    {
        AiCostFormatter.Format(1_234, System.Globalization.CultureInfo.GetCultureInfo("en-US"))
            .Should().Be("$0.001234");
        AiCostFormatter.Format(0, System.Globalization.CultureInfo.GetCultureInfo("en-US"))
            .Should().Be("$0.00");
    }

    /// <summary>
    /// "Two decimals" is the rule of a two-decimal currency, not of every currency: a yen amount has no minor
    /// unit at all, so "$1.50-style" rounding would invent precision the bill does not have. The ordinary
    /// amount uses the culture's own <c>CurrencyDecimalDigits</c>; only the sub-cent case keeps micro-units.
    /// </summary>
    [Fact]
    public void TheCostFormatter_FollowsTheCurrencysOwnDecimalDigits()
    {
        var japanese = System.Globalization.CultureInfo.GetCultureInfo("ja-JP");

        AiCostFormatter.Format(2_000_000, japanese).Should().Be(2m.ToString("C0", japanese));
        AiCostFormatter.Format(1_234, japanese).Should().Be(0.001234m.ToString("C6", japanese));
    }

    [Fact]
    public void TheClockFormatter_ReadsAsADuration()
    {
        AiClockFormatter.FormatClock(TimeSpan.FromSeconds(42)).Should().Be("0:42");
        AiClockFormatter.FormatClock(TimeSpan.FromSeconds(84)).Should().Be("1:24");
        AiClockFormatter.FormatClock(TimeSpan.FromSeconds(3727)).Should().Be("1:02:07");
        AiClockFormatter.FormatClock(TimeSpan.FromSeconds(-5)).Should().Be("0:00");
    }
}
