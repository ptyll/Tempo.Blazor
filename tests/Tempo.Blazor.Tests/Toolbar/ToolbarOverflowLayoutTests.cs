using FluentAssertions;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// F4: <see cref="ToolbarOverflowLayout"/> — the toolbar vocabulary over the ONE shared action
/// partition. Mirrors the cases of <c>tm-toolbar.test.mjs</c> (the JS side computes how many buttons
/// fit; this side decides WHICH ones leave).
/// </summary>
public class ToolbarOverflowLayoutTests
{
    private sealed record Btn(string Id, ToolbarButtonPriority Priority);

    private static IReadOnlyList<Btn> Buttons(params (string Id, ToolbarButtonPriority P)[] items)
        => items.Select(i => new Btn(i.Id, i.P)).ToList();

    private static (string[] Bar, string[] Menu) Run(IReadOnlyList<Btn> items, int? maxVisible)
    {
        var (bar, menu) = ToolbarOverflowLayout.Resolve(items, maxVisible, b => b.Priority);
        return (bar.Select(b => b.Id).ToArray(), menu.Select(b => b.Id).ToArray());
    }

    private const ToolbarButtonPriority P = ToolbarButtonPriority.Primary;
    private const ToolbarButtonPriority S = ToolbarButtonPriority.Secondary;
    private const ToolbarButtonPriority O = ToolbarButtonPriority.OverflowOnly;
    private const ToolbarButtonPriority N = ToolbarButtonPriority.Pinned;

    [Fact]
    public void Rank_PutsSecondaryBelowPrimary_AndOverflowOnlyBelowBoth()
    {
        ToolbarOverflowLayout.Rank(S).Should().BeLessThan(ToolbarOverflowLayout.Rank(P));
        ToolbarOverflowLayout.Rank(O).Should().BeLessThan(ToolbarOverflowLayout.Rank(S));
    }

    [Fact]
    public void Unmeasured_KeepsEverythingOnTheBar_ExceptOverflowOnly()
    {
        var (bar, menu) = Run(Buttons(("a", P), ("b", S), ("c", O), ("d", P)), maxVisible: null);

        bar.Should().Equal("a", "b", "d");
        menu.Should().Equal("c");
    }

    [Fact]
    public void OverflowOnly_IsAlwaysInTheMenu_AndNeverCountsAgainstTheBudget()
    {
        var (bar, menu) = Run(Buttons(("a", P), ("b", O), ("c", P)), maxVisible: 2);

        bar.Should().Equal("a", "c");
        menu.Should().Equal("b");
    }

    [Fact]
    public void Secondary_LeavesBeforePrimary_AndBothListsKeepTheWrittenOrder()
    {
        // Four Auto buttons, room for three: one leaves — the Secondary one; of two Secondary
        // buttons the LATER one goes first (ties drop the last).
        var (bar, menu) = Run(Buttons(("a", P), ("b", S), ("c", P), ("d", S), ("e", O)), maxVisible: 3);

        bar.Should().Equal("a", "b", "c");
        menu.Should().Equal("d", "e");
    }

    [Fact]
    public void ScarceRoom_DropsAllSecondaryBeforeTheFirstPrimary()
    {
        var (bar, menu) = Run(Buttons(("a", S), ("b", P), ("c", S), ("d", P)), maxVisible: 2);

        bar.Should().Equal("b", "d");
        menu.Should().Equal("a", "c");
    }

    [Fact]
    public void MaxVisibleBelowOne_KeepsOneButtonOnTheBar()
    {
        var (bar, menu) = Run(Buttons(("a", P), ("b", P), ("c", S)), maxVisible: 0);

        bar.Should().HaveCount(1, "the shared partition never empties a bar of Auto buttons");
        menu.Should().HaveCount(2);
    }

    [Fact]
    public void NothingToMove_ReturnsTheSameOrderWithAnEmptyMenu()
    {
        var (bar, menu) = Run(Buttons(("a", P), ("b", S)), maxVisible: 5);

        bar.Should().Equal("a", "b");
        menu.Should().BeEmpty();
    }

    [Fact]
    public void Pinned_IsTheLastEnumMember_SoExistingValuesKeepTheirNumbers()
    {
        // Q1=A: Pinned is additive - Primary/Secondary/OverflowOnly keep 0/1/2.
        ((int)ToolbarButtonPriority.Primary).Should().Be(0);
        ((int)ToolbarButtonPriority.Secondary).Should().Be(1);
        ((int)ToolbarButtonPriority.OverflowOnly).Should().Be(2);
        ((int)ToolbarButtonPriority.Pinned).Should().Be(3);
        Enum.GetValues<ToolbarButtonPriority>().Last().Should().Be(ToolbarButtonPriority.Pinned);
    }

    [Fact]
    public void Pinned_NeverMovesIntoTheMenu_WhateverTheMeasuredRoom()
    {
        // The measured number counts only the collapsible (Auto) buttons: the pinned one is fixed width.
        var (bar, menu) = Run(Buttons(("a", P), ("b", S), ("c", N), ("d", P)), maxVisible: 1);
        bar.Should().Equal("a", "c");
        menu.Should().Equal("b", "d");

        var (scarceBar, scarceMenu) = Run(Buttons(("a", P), ("b", S), ("c", N), ("d", P)), maxVisible: 0);
        scarceBar.Should().Equal(new[] { "c" }, "the pinned button stays even when no collapsible one fits");
        scarceMenu.Should().Equal("a", "b", "d");
    }

    [Fact]
    public void Pinned_Unmeasured_StaysOnTheBar_AndOnlyPinnedButtonsNeverNeedAMenu()
    {
        var (bar, menu) = Run(Buttons(("a", N), ("b", N)), maxVisible: 0);
        bar.Should().Equal("a", "b");
        menu.Should().BeEmpty();

        var (bar2, menu2) = Run(Buttons(("a", P), ("b", O), ("c", N)), maxVisible: null);
        bar2.Should().Equal("a", "c");
        menu2.Should().Equal("b");
    }
}