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

    private static IReadOnlyList<IMultiViewListItem> Items(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new TestItem(i.ToString(), $"Item {i}", $"Sub {i}"))
            .ToArray<IMultiViewListItem>();

    [Fact]
    public void MultiViewList_DefaultView_IsTable()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p.Add(c => c.Items, Items()));

        cut.FindAll(".tm-mvl-table").Should().HaveCount(1);
    }

    [Fact]
    public void MultiViewList_SwitchToCard_ChangesView()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p.Add(c => c.Items, Items()));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll(".tm-mvl-card-grid").Should().HaveCount(1);
        cut.FindAll(".tm-mvl-table").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_SwitchToList_ChangesView()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p.Add(c => c.Items, Items()));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll(".tm-mvl-list").Should().HaveCount(1);
        cut.FindAll(".tm-mvl-table").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_Empty_RendersEmptyState()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p
            .Add(c => c.Items,       Array.Empty<IMultiViewListItem>())
            .Add(c => c.EmptyTitle,  "Nothing here"));

        cut.Find(".tm-empty-state").Should().NotBeNull();
    }

    [Fact]
    public void MultiViewList_RowClick_FiresCallback()
    {
        IMultiViewListItem? clicked = null;
        var cut = RenderComponent<TmMultiViewList>(p => p
            .Add(c => c.Items,       Items())
            .Add(c => c.OnItemClick, (IMultiViewListItem item) => clicked = item));

        cut.FindAll(".tm-mvl-row").First().Click();

        clicked.Should().NotBeNull();
    }

    [Fact]
    public void MultiViewList_TableView_RendersColumns()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p.Add(c => c.Items, Items()));

        // Table view is default, should render item titles in rows
        cut.FindAll(".tm-mvl-row").Should().NotBeEmpty();
    }

    [Fact]
    public void MultiViewList_CardView_RendersCards()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p.Add(c => c.Items, Items(2)));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll(".tm-mvl-card").Should().HaveCount(2);
    }

    [Fact]
    public void MultiViewList_ListViewMode_RendersListItems()
    {
        var cut = RenderComponent<TmMultiViewList>(p => p.Add(c => c.Items, Items(2)));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll(".tm-mvl-list-item").Should().HaveCount(2);
    }
}
