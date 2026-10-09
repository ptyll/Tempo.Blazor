using FluentAssertions;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Actions;

/// <summary>
/// TDD tests for <see cref="ActionOverflowLayout.Partition"/> (F5 review round 1, X9). The user
/// decision (plan faf503eb) makes <c>TmActionItem.Priority</c> ONLY an overflow rank: the bar and
/// the "More" menu render in Items order; the lowest-priority items overflow first; ties drop the
/// last item first; disabled items still count; <c>MaxVisible</c> below 1 clamps to 1.
/// </summary>
public class ActionOverflowLayoutTests
{
    private static TmActionItem Item(string id, int priority = 0, bool disabled = false)
        => new() { Id = id, Label = id, Priority = priority, Disabled = disabled };

    private static IReadOnlyList<TmActionItem> Items(params TmActionItem[] items) => items;

    [Fact]
    public void Partition_PreservesItemsOrder_InBothLists()
    {
        var items = Items(Item("a", 0), Item("b", 10), Item("c", 5));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 2);

        // One item overflows (the lowest priority, "a"); both lists keep the Items order.
        visible.Select(i => i.Id).Should().Equal("b", "c");
        overflow.Select(i => i.Id).Should().Equal("a");
    }

    [Fact]
    public void Partition_NoOverflow_ReturnsEverythingVisible()
    {
        var items = Items(Item("a", 0), Item("b", 10), Item("c", 5));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 5);

        visible.Select(i => i.Id).Should().Equal("a", "b", "c");
        overflow.Should().BeEmpty();
    }

    [Fact]
    public void Partition_LowestPriorityOverflowsFirst_TiesDropTheLastItem()
    {
        // All equal priority: the LAST items in Items order drop first.
        var tied = Items(Item("a", 5), Item("b", 5), Item("c", 5));
        var (visibleTied, overflowTied) = ActionOverflowLayout.Partition(tied, maxVisible: 1);
        visibleTied.Select(i => i.Id).Should().Equal("a");
        overflowTied.Select(i => i.Id).Should().Equal("b", "c");

        // Mixed: the lowest priority leaves first; the remaining tie drops the later item.
        var mixed = Items(Item("a", 0), Item("b", 10), Item("c", 5), Item("d", 5));
        var (visible, overflow) = ActionOverflowLayout.Partition(mixed, maxVisible: 2);
        visible.Select(i => i.Id).Should().Equal("b", "c");
        overflow.Select(i => i.Id).Should().Equal("a", "d");
    }

    [Fact]
    public void Partition_DisabledItemsStillCount()
    {
        var items = Items(Item("blocked", 0, disabled: true), Item("free", 10));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 1);

        // The disabled item is still the lowest priority, so it overflows even though it cannot
        // be invoked — the visible budget counts it.
        visible.Select(i => i.Id).Should().Equal("free");
        overflow.Select(i => i.Id).Should().Equal("blocked");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Partition_MaxVisibleBelowOne_ClampsToOne(int maxVisible)
    {
        var items = Items(Item("a", 10), Item("b", 20));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible);

        visible.Select(i => i.Id).Should().Equal("b");
        overflow.Select(i => i.Id).Should().Equal("a");
    }

    [Fact]
    public void Partition_EmptyItems_ReturnsEmptyLists()
    {
        var (visible, overflow) = ActionOverflowLayout.Partition([], maxVisible: 3);

        visible.Should().BeEmpty();
        overflow.Should().BeEmpty();
    }

    // ── Y10 (review round 2): the generic partition — F4's toolbar overflow ranks its own item
    //    type through the same helper instead of duplicating the ordering rules. ──────────────

    [Fact]
    public void Partition_GenericRankSelector_AppliesTheSameOrderingRules()
    {
        var items = new[]
        {
            (Id: "a", Rank: 0),
            (Id: "b", Rank: 10),
            (Id: "c", Rank: 5),
            (Id: "d", Rank: 5),
        };

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 2, rank: static item => item.Rank);

        // The visible list keeps the Items order; the overflow picks the lowest rank first and a
        // tie drops the last item — the same rules as the TmActionItem overload.
        visible.Select(i => i.Id).Should().Equal("b", "c");
        overflow.Select(i => i.Id).Should().Equal("a", "d");
    }

    [Fact]
    public void Partition_GenericMaxVisibleBelowOne_ClampsToOne()
    {
        var items = new[] { (Id: "a", Rank: 10), (Id: "b", Rank: 20) };

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 0, rank: static item => item.Rank);

        visible.Select(i => i.Id).Should().Equal("b");
        overflow.Select(i => i.Id).Should().Equal("a");
    }

    [Fact]
    public void Partition_ActionItemOverload_DelegatesToTheGenericPartition()
    {
        // The TmActionItem overload is a thin facade: same lists, same order — the single
        // implementation the ordering rules live in.
        var items = Items(Item("a", 0), Item("b", 10), Item("c", 5));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 2);

        visible.Select(i => i.Id).Should().Equal("b", "c");
        overflow.Select(i => i.Id).Should().Equal("a");
    }
}
