using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>Tests for TmMultiViewList parameter behavior (ShowSearch, ShowViewSwitcher, ViewModeChanged, Class).</summary>
public class TmMultiViewListParameterTests : LocalizationTestBase
{
    private record MvlItem(
        string Title,
        string? SubTitle    = null,
        string? StatusLabel = null,
        string? StatusColor = null,
        DateTimeOffset? Date = null,
        string? AvatarUrl   = null) : IMultiViewListItem
    {
        public string Id => Title;
        public IReadOnlyList<ITag>? Tags => null;
    }

    private static IReadOnlyList<MvlItem> SampleItems(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new MvlItem($"Item {i}", $"Sub {i}"))
            .ToArray();

    [Fact]
    public void MultiViewList_ShowSearch_False_HidesSearchInput()
    {
        // Arrange & Act
        var cut = RenderComponent<TmMultiViewList<MvlItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ShowSearch, false));

        // Assert - when ShowSearch=false, the toolbar-left containing the search input should not render
        cut.FindAll(".tm-mvl-toolbar-left").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_ShowViewSwitcher_False_HidesViewButtons()
    {
        // Arrange & Act
        var cut = RenderComponent<TmMultiViewList<MvlItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ShowViewSwitcher, false));

        // Assert - when ShowViewSwitcher=false, the switcher container should not render
        cut.FindAll(".tm-mvl-switcher").Should().BeEmpty();
        cut.FindAll(".tm-mvl-switch-table").Should().BeEmpty();
        cut.FindAll(".tm-mvl-switch-card").Should().BeEmpty();
        cut.FindAll(".tm-mvl-switch-list").Should().BeEmpty();
    }

    [Fact]
    public void MultiViewList_ViewModeChanged_FiresCallback()
    {
        // Arrange
        ListViewMode? changedMode = null;
        var cut = RenderComponent<TmMultiViewList<MvlItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.ViewModeChanged, (ListViewMode mode) => changedMode = mode));

        // Act - click the card view button to switch from default (Table) to Card
        cut.Find(".tm-mvl-switch-card").Click();

        // Assert
        changedMode.Should().Be(ListViewMode.Card);
    }

    [Fact]
    public void MultiViewList_Class_AppendsCustomCssClass()
    {
        // Arrange & Act
        var cut = RenderComponent<TmMultiViewList<MvlItem>>(p => p
            .Add(c => c.Items, SampleItems())
            .Add(c => c.Class, "my-custom"));

        // Assert - the root .tm-multi-view-list element should also have the custom class
        cut.Find(".tm-multi-view-list").ClassList.Should().Contain("my-custom");
    }
}
