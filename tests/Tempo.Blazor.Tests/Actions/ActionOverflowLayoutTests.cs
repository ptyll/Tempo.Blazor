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

    // ── F4: the Overflow pins (Auto / Never / Always) ────────────────────────────────────────

    private static TmActionItem Pinned(string id, ActionOverflow pin, int priority = 0)
        => new() { Id = id, Label = id, Priority = priority, Overflow = pin };

    [Fact]
    public void Partition_AlwaysItems_AreInTheOverflowWhateverTheBudget()
    {
        var items = Items(Item("a", 10), Pinned("b", ActionOverflow.Always, 100), Item("c", 5));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 10);

        visible.Select(i => i.Id).Should().Equal("a", "c");
        overflow.Select(i => i.Id).Should().Equal("b");
    }

    [Fact]
    public void Partition_AlwaysItems_DoNotCountAgainstTheVisibleBudget()
    {
        // Budget 2: a and c fit although the Always item sits between them.
        var items = Items(Item("a", 1), Pinned("x", ActionOverflow.Always), Item("c", 2));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 2);

        visible.Select(i => i.Id).Should().Equal("a", "c");
        overflow.Select(i => i.Id).Should().Equal("x");
    }

    [Fact]
    public void Partition_NeverItems_StayVisible_AndCountAgainstTheBudget()
    {
        // The Never item has the LOWEST priority (it would overflow first) yet stays; with a
        // budget of 2 it leaves room for exactly one Auto item — the higher priority one.
        var items = Items(Pinned("keep", ActionOverflow.Never, priority: 0), Item("a", 5), Item("b", 9));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 2);

        visible.Select(i => i.Id).Should().Equal("keep", "b");
        overflow.Select(i => i.Id).Should().Equal("a");
    }

    [Fact]
    public void Partition_NeverItems_ExceedingTheBudget_AllStayVisible_AndEveryAutoOverflows()
    {
        var items = Items(
            Pinned("n1", ActionOverflow.Never), Pinned("n2", ActionOverflow.Never), Item("a", 50));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 1);

        visible.Select(i => i.Id).Should().Equal("n1", "n2");
        overflow.Select(i => i.Id).Should().Equal("a");
    }

    [Fact]
    public void Partition_MixedPins_PreserveItemsOrder_InBothLists()
    {
        var items = Items(
            Item("a", 1), Pinned("b", ActionOverflow.Always), Pinned("c", ActionOverflow.Never),
            Item("d", 7), Item("e", 3), Pinned("f", ActionOverflow.Always));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 3);

        // Budget 3: c (Never) + the two best Auto items by rank (d=7, e=3); a (1) overflows.
        visible.Select(i => i.Id).Should().Equal("c", "d", "e");
        overflow.Select(i => i.Id).Should().Equal("a", "b", "f");
    }

    [Fact]
    public void Partition_AutoOnly_IsUnchangedByThePinOverload()
    {
        var items = Items(Item("a", 0), Item("b", 10), Item("c", 5), Item("d", 5));

        var (visible, overflow) = ActionOverflowLayout.Partition(items, maxVisible: 2);

        visible.Select(i => i.Id).Should().Equal("b", "c");
        overflow.Select(i => i.Id).Should().Equal("a", "d");
    }

    [Fact]
    public void Partition_GenericPinSelector_AppliesTheSamePinRules()
    {
        var items = new[]
        {
            (Id: "a", Rank: 1, Pin: ActionOverflow.Auto),
            (Id: "b", Rank: 9, Pin: ActionOverflow.Always),
            (Id: "c", Rank: 0, Pin: ActionOverflow.Never),
        };

        var (visible, overflow) = ActionOverflowLayout.Partition(
            items, maxVisible: 1, rank: static i => i.Rank, pin: static i => i.Pin);

        visible.Select(i => i.Id).Should().Equal("c");
        overflow.Select(i => i.Id).Should().Equal("a", "b");
    }
}