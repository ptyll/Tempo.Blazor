using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListTests : LocalizationTestBase
{
    private record TestItem(
        string Id,
        string Title,
        string? SubTitle    = null,
        string? AvatarUrl   = null,
        string? StatusLabel = null,
        string? StatusColor = null) : IMultiViewListItem
    {
        public IReadOnlyList<ITag>? Tags => null;
        public DateTimeOffset?      Date => null;
    }

    private static IReadOnlyList<TestItem> Items(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new TestItem(i.ToString(), $"Item {i}", $"Sub {i}"))
            .ToArray();

    [Fact]
    public void MultiViewList_DefaultView_IsTable()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p.Add(c => c.Items, Items()));

        cut.FindAll(".tm-mvl-table").Should().HaveCount(1);
    }

    [Fact]
    public void MultiViewList_SwitchToCard_ChangesView()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p.Add(c => c.Items, Items()));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll(".tm-mvl-card-grid").Should().HaveCount(1);
        cut.FindAll(".tm-mvl-table").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_SwitchToList_ChangesView()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p.Add(c => c.Items, Items()));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll(".tm-mvl-list").Should().HaveCount(1);
        cut.FindAll(".tm-mvl-table").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_Empty_RendersEmptyState()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items,       Array.Empty<TestItem>())
            .Add(c => c.EmptyTitle,  "Nothing here"));

        cut.Find(".tm-empty-state").Should().NotBeNull();
    }

    [Fact]
    public void MultiViewList_RowClick_FiresCallback()
    {
        TestItem? clicked = null;
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items,       Items())
            .Add(c => c.OnItemClick, (TestItem item) => clicked = item));

        cut.FindAll(".tm-mvl-row").First().Click();

        clicked.Should().NotBeNull();
    }

    // ── Keyboard activation (keyboard-activation-convention) ────────────────
    // Rows/cards/items are non-native focusables — Enter fires on keydown (repeat-
    // guarded), Space fires on keyup only. Native children inside them are fenced.

    [Fact]
    public void MultiViewList_RowEnterKeyDown_FiresItemClick()
    {
        TestItem? clicked = null;
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.OnItemClick, (TestItem item) => clicked = item));

        cut.FindAll(".tm-mvl-row").First()
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        clicked.Should().NotBeNull();
    }

    [Fact]
    public void MultiViewList_RowRepeatedEnterKeydown_DoesNotRefire()
    {
        var clicks = 0;
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.OnItemClick, (TestItem _) => clicks++));

        cut.FindAll(".tm-mvl-row").First()
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter", Repeat = true });

        clicks.Should().Be(0, "a held Enter's auto-repeat stream must not re-fire the click");
    }

    [Fact]
    public void MultiViewList_RowSpaceKeyUp_FiresItemClick_KeydownDoesNot()
    {
        TestItem? clicked = null;
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.OnItemClick, (TestItem item) => clicked = item));

        cut.FindAll(".tm-mvl-row").First()
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        clicked.Should().BeNull("Space keydown must not activate — the release does");
        // Re-find: every dispatched event completes with a re-render, so the
        // previously found element's handler IDs are stale by now.
        cut.FindAll(".tm-mvl-row").First()
            .KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });

        clicked.Should().NotBeNull();
    }

    [Fact]
    public void MultiViewList_SelectCheckbox_SpaceKeyUp_DoesNotFireItemClick()
    {
        // The selection cell's keydown fence needs the matching keyup barrier — a
        // bubbled Space release off the checkbox would otherwise fire the row click
        // on top of the checkbox's own toggle.
        var clicks = 0;
        var cut = Render<TmMultiViewList<TestItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.OnItemClick, (TestItem _) => clicks++));

        var checkbox = cut.Find(".tm-mvl-col-select input[type='checkbox']");
        var act = () => checkbox.KeyUp(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = " " });
        act.Should().Throw<Bunit.MissingEventHandlerException>(
            "the selection cell must isolate its keyup from the row's Space handler");
        clicks.Should().Be(0);
    }

    [Fact]
    public void MultiViewList_TableView_RendersColumns()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p.Add(c => c.Items, Items()));

        // Table view is default, should render item titles in rows
        cut.FindAll(".tm-mvl-row").Should().NotBeEmpty();
    }

    [Fact]
    public void MultiViewList_CardView_RendersCards()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p.Add(c => c.Items, Items(2)));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll(".tm-mvl-card").Should().HaveCount(2);
    }

    [Fact]
    public void MultiViewList_ListViewMode_RendersListItems()
    {
        var cut = Render<TmMultiViewList<TestItem>>(p => p.Add(c => c.Items, Items(2)));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll(".tm-mvl-list-item").Should().HaveCount(2);
    }
}
