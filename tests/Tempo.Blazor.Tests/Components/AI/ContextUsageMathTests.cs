using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Plan 3e96f6d8, phase 5 — the arithmetic of <c>TmContextUsage</c> (<see cref="ContextUsageMath"/>): which number
/// a section contributes, what the snapshot totals, how the rounded shares stay honest, when a context is over
/// its budget and which sections a compaction can actually drop. Nothing here touches markup, so the four
/// decisions that are easy to get wrong are testable on their own.
/// </summary>
public class ContextUsageMathTests
{
    private static ContextUsageSection Section(
        string key = "system",
        int tokens = 0,
        ContextUsageKind kind = ContextUsageKind.System,
        bool required = false,
        bool summarized = false,
        bool omitted = false,
        bool canRemove = false,
        params ContextUsageItem[] items)
        => new(key, key)
        {
            Kind = kind,
            Tokens = tokens,
            IsRequired = required,
            IsSummarized = summarized,
            IsOmitted = omitted,
            CanRemove = canRemove,
            Items = items.Length == 0 ? null : items,
        };

    private static ContextUsageItem Item(string id, int tokens, bool canRemove = true)
        => new(id, id) { Tokens = tokens, CanRemove = canRemove };

    [Fact]
    public void AReportedSectionTotal_IsWhatTheSectionContributes()
    {
        ContextUsageMath.TokensOf(Section(tokens: 4_200, items: [Item("a", 100), Item("b", 200)]))
            .Should().Be(4_200, "the host's section total is the authoritative number — the items only break it down");
    }

    [Fact]
    public void ASectionWithoutATotal_FallsBackToWhatItsItemsReport()
    {
        ContextUsageMath.TokensOf(Section(tokens: 0, items: [Item("a", 100), Item("b", 250)]))
            .Should().Be(350, "a host that only lists items still gets a measured section");
    }

    [Fact]
    public void NegativeTokens_CountAsNothing()
    {
        ContextUsageMath.TokensOf(Section(tokens: -50)).Should().Be(0);
        ContextUsageMath.TokensOf(Section(tokens: -50, items: [Item("a", 100)]))
            .Should().Be(100, "a negative total is a data defect, not a negative contribution");
        ContextUsageMath.TokensOf(Section(tokens: 10, items: [Item("a", -5)])).Should().Be(10);
    }

    [Fact]
    public void AStructuredRecord_IsNeverNullButAnItemListHoldingNullStillCounts()
    {
        var section = Section(tokens: 10) with { Items = [null!, Item("b", 5)] };

        ContextUsageMath.TokensOf(section).Should().Be(10, "the reported total wins, so a null entry cannot throw");
    }

    [Fact]
    public void TheSnapshotTotal_IsTheSumOfItsSections()
    {
        var snapshot = new ContextUsageSnapshot
        {
            BudgetTokens = 16_000,
            Sections = [Section("system", 1_000), Section("tools", 2_500), Section("chat", 6_500)],
        };

        ContextUsageMath.UsedTokens(snapshot).Should().Be(10_000);
    }

    [Fact]
    public void ANullOrEmptySnapshot_MeasuresZero()
    {
        ContextUsageMath.UsedTokens(null).Should().Be(0);
        ContextUsageMath.UsedTokens(new ContextUsageSnapshot { Sections = [] }).Should().Be(0);
        ContextUsageMath.UsedTokens(new ContextUsageSnapshot { Sections = null }).Should().Be(0);
    }

    [Fact]
    public void OverBudget_NeedsAKnownBudget_AndIsStrictlyAboveIt()
    {
        ContextUsageMath.IsOverBudget(new ContextUsageSnapshot
        {
            BudgetTokens = 1_000,
            Sections = [Section(tokens: 1_000)],
        }).Should().BeFalse("exactly at the budget is full, not over");

        ContextUsageMath.IsOverBudget(new ContextUsageSnapshot
        {
            BudgetTokens = 1_000,
            Sections = [Section(tokens: 1_001)],
        }).Should().BeTrue();

        ContextUsageMath.IsOverBudget(new ContextUsageSnapshot
        {
            Sections = [Section(tokens: 1_001)],
        }).Should().BeFalse("an unknown budget cannot be exceeded — 0 of 0 is not a violation");
    }

    [Fact]
    public void PercentOfTheBudget_IsNotClampedSoOverBudgetReadsAsItIs()
    {
        ContextUsageMath.BudgetPercent(new ContextUsageSnapshot
        {
            BudgetTokens = 1_000,
            Sections = [Section(tokens: 1_500)],
        }).Should().Be(150);

        ContextUsageMath.BudgetPercent(new ContextUsageSnapshot
        {
            BudgetTokens = 1_000,
            Sections = [Section(tokens: 750)],
        }).Should().Be(75);

        ContextUsageMath.BudgetPercent(new ContextUsageSnapshot { Sections = [Section(tokens: 750)] })
            .Should().BeNull("an unknown budget has no percentage");
    }

    [Fact]
    public void SharesAddUpToExactlyOneHundred()
    {
        ContextUsageMath.Shares([1_000, 2_500, 6_500], 10_000).Should().Equal(10, 25, 65);

        var thirds = ContextUsageMath.Shares([1, 1, 1], 3);
        thirds.Should().Equal([34, 33, 33], "the largest-remainder step gives the leftover point to the first row");
        thirds.Sum().Should().Be(100, "three rounded thirds are not 99 %");
    }

    [Fact]
    public void SharesOfAZeroTotal_AreAllZero()
    {
        ContextUsageMath.Shares([0, 0], 0).Should().Equal(0, 0);
        ContextUsageMath.Shares([], 0).Should().BeEmpty();
    }

    [Fact]
    public void SharesKeepThePlainFloors_WhenTheRowsDoNotAccountForTheWhole()
    {
        // 500 of a declared 10 000 total: the remaining 99 points belong to usage no row explains, so they
        // must not be spread over the rows that are there (which would inflate them to 10 % each).
        ContextUsageMath.Shares([200, 300], 10_000).Should().Equal(2, 3);
    }

    [Fact]
    public void SharesTreatANegativeRowAsZero()
    {
        ContextUsageMath.Shares([-500, 1_000], 1_000).Should().Equal(0, 100);
    }

    [Theory]
    [InlineData(false, false, false, 100, true)]
    [InlineData(true, false, false, 100, false)]
    [InlineData(false, true, false, 100, false)]
    [InlineData(false, false, true, 100, false)]
    [InlineData(false, false, false, 0, false)]
    public void CompactableSections_AreOptionalNonSummarisedNonOmittedOnesWithTokens(
        bool required,
        bool summarized,
        bool omitted,
        int tokens,
        bool expected)
    {
        var section = Section(tokens: tokens, required: required, summarized: summarized, omitted: omitted);

        ContextUsageMath.IsCompactable(section).Should().Be(expected);
    }

    [Fact]
    public void TheContextIsCompactable_WhenAnySectionIs()
    {
        var snapshot = new ContextUsageSnapshot
        {
            BudgetTokens = 10_000,
            Sections = [Section("system", 1_000, required: true), Section("files", 2_000)],
        };

        ContextUsageMath.IsCompactable(snapshot).Should().BeTrue();

        var rigid = new ContextUsageSnapshot
        {
            BudgetTokens = 10_000,
            Sections = [Section("system", 1_000, required: true), Section("chat", 2_000, summarized: true)],
        };

        ContextUsageMath.IsCompactable(rigid).Should().BeFalse();
        ContextUsageMath.IsCompactable((ContextUsageSnapshot?)null).Should().BeFalse();
    }
}
