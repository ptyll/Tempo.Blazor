using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>Simple POCO - does NOT implement IMultiViewListItem.</summary>
public record MvlProject(string Id, string Name, string Manager, string Status, string StatusBg, DateTimeOffset? DueDate);

public class TmMultiViewListGenericTests : LocalizationTestBase
{
    private static List<MvlProject> Projects =>
    [
        new("1", "Alpha", "Alice", "Active", "#22c55e", DateTimeOffset.Now),
        new("2", "Beta", "Bob", "Paused", "#f59e0b", null),
        new("3", "Gamma", "Charlie", "Done", "#6b7280", DateTimeOffset.Now.AddDays(-10)),
    ];

    private IRenderedComponent<TmMultiViewList<MvlProject>> RenderWithMapping(
        ListViewMode viewMode = ListViewMode.Table,
        List<MvlProject>? items = null)
    {
        return RenderComponent<TmMultiViewList<MvlProject>>(p =>
        {
            p.Add(c => c.Items, items ?? Projects);
            p.Add(c => c.ViewMode, viewMode);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.SubTitleField, x => x.Manager);
            p.Add(c => c.StatusLabelField, x => x.Status);
            p.Add(c => c.StatusColorField, x => x.StatusBg);
            p.Add(c => c.DateField, x => x.DueDate);
            p.Add(c => c.IdField, x => x.Id);
        });
    }

    // --- Mapping parameters ---

    [Fact]
    public void MultiViewList_Generic_TitleField_DisplaysInTableView()
    {
        var cut = RenderWithMapping(ListViewMode.Table);

        var rows = cut.FindAll(".tm-mvl-row");
        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("Alpha");
    }

    [Fact]
    public void MultiViewList_Generic_TitleField_DisplaysInCardView()
    {
        var cut = RenderWithMapping(ListViewMode.Card);

        var cards = cut.FindAll(".tm-mvl-card");
        cards.Should().HaveCount(3);
        cards[0].TextContent.Should().Contain("Alpha");
    }

    [Fact]
    public void MultiViewList_Generic_TitleField_DisplaysInListView()
    {
        var cut = RenderWithMapping(ListViewMode.List);

        var items = cut.FindAll(".tm-mvl-list-item");
        items.Should().HaveCount(3);
        items[0].TextContent.Should().Contain("Alpha");
    }

    [Fact]
    public void MultiViewList_Generic_SubTitleField_DisplaysInTableView()
    {
        var cut = RenderWithMapping(ListViewMode.Table);

        cut.FindAll(".tm-mvl-row")[0].TextContent.Should().Contain("Alice");
    }

    [Fact]
    public void MultiViewList_Generic_StatusField_DisplaysInTableView()
    {
        var cut = RenderWithMapping(ListViewMode.Table);

        var status = cut.FindAll(".tm-mvl-status");
        status.Should().NotBeEmpty();
        status[0].TextContent.Should().Contain("Active");
    }

    // --- Template overrides ---

    [Fact]
    public void MultiViewList_Generic_CardTemplate_OverridesDefault()
    {
        var cut = RenderComponent<TmMultiViewList<MvlProject>>(p =>
        {
            p.Add(c => c.Items, Projects);
            p.Add(c => c.ViewMode, ListViewMode.Card);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.CardTemplate, item => b =>
            {
                b.OpenElement(0, "div");
                b.AddAttribute(1, "class", "custom-card");
                b.AddContent(2, $"Custom: {item.Name}");
                b.CloseElement();
            });
        });

        cut.FindAll(".custom-card").Should().HaveCount(3);
        cut.FindAll(".custom-card")[0].TextContent.Should().Contain("Custom: Alpha");
    }

    [Fact]
    public void MultiViewList_Generic_ListItemTemplate_OverridesDefault()
    {
        var cut = RenderComponent<TmMultiViewList<MvlProject>>(p =>
        {
            p.Add(c => c.Items, Projects);
            p.Add(c => c.ViewMode, ListViewMode.List);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.ListItemTemplate, item => b =>
            {
                b.OpenElement(0, "span");
                b.AddAttribute(1, "class", "custom-list-item");
                b.AddContent(2, $"LI: {item.Name}");
                b.CloseElement();
            });
        });

        cut.FindAll(".custom-list-item").Should().HaveCount(3);
    }

    [Fact]
    public void MultiViewList_Generic_TableColumns_OverridesDefault()
    {
        var cut = RenderComponent<TmMultiViewList<MvlProject>>(p =>
        {
            p.Add(c => c.Items, Projects);
            p.Add(c => c.ViewMode, ListViewMode.Table);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.TableRowTemplate, item => b =>
            {
                b.OpenElement(0, "td");
                b.AddAttribute(1, "class", "custom-cell");
                b.AddContent(2, item.Name);
                b.CloseElement();
            });
        });

        cut.FindAll(".custom-cell").Should().HaveCount(3);
    }

    // --- IMultiViewListItem auto-mapping ---

    [Fact]
    public void MultiViewList_Generic_IMultiViewListItem_AutoMapping()
    {
        var items = new List<AutoMappedItem>
        {
            new("1", "AutoItem1", "AutoSub1"),
            new("2", "AutoItem2", "AutoSub2"),
        };

        var cut = RenderComponent<TmMultiViewList<AutoMappedItem>>(p =>
        {
            p.Add(c => c.Items, items);
            p.Add(c => c.ViewMode, ListViewMode.Table);
        });

        var rows = cut.FindAll(".tm-mvl-row");
        rows.Should().HaveCount(2);
        rows[0].TextContent.Should().Contain("AutoItem1");
    }

    // --- View switching ---

    [Fact]
    public void MultiViewList_Generic_SwitchView_MaintainsData()
    {
        var cut = RenderWithMapping(ListViewMode.Table);

        cut.FindAll(".tm-mvl-row").Should().HaveCount(3);

        cut.Find(".tm-mvl-switch-card").Click();
        cut.FindAll(".tm-mvl-card").Should().HaveCount(3);

        cut.Find(".tm-mvl-switch-list").Click();
        cut.FindAll(".tm-mvl-list-item").Should().HaveCount(3);
    }

    [Fact]
    public void MultiViewList_Generic_ViewSwitcher_AllThreeModesWork()
    {
        var cut = RenderWithMapping(ListViewMode.Table);

        cut.FindAll(".tm-mvl-table").Should().HaveCount(1);

        cut.Find(".tm-mvl-switch-card").Click();
        cut.FindAll(".tm-mvl-card-grid").Should().HaveCount(1);

        cut.Find(".tm-mvl-switch-list").Click();
        cut.FindAll(".tm-mvl-list").Should().HaveCount(1);

        cut.Find(".tm-mvl-switch-table").Click();
        cut.FindAll(".tm-mvl-table").Should().HaveCount(1);
    }

    // --- Empty state ---

    [Fact]
    public void MultiViewList_Generic_Empty_RendersEmptyState()
    {
        var cut = RenderComponent<TmMultiViewList<MvlProject>>(p =>
        {
            p.Add(c => c.Items, new List<MvlProject>());
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.EmptyTitle, "No projects");
        });

        cut.Find(".tm-empty-state").Should().NotBeNull();
    }

    // --- OnItemClick ---

    [Fact]
    public void MultiViewList_Generic_RowClick_FiresCallback()
    {
        MvlProject? clicked = null;
        var cut = RenderComponent<TmMultiViewList<MvlProject>>(p =>
        {
            p.Add(c => c.Items, Projects);
            p.Add(c => c.TitleField, x => x.Name);
            p.Add(c => c.OnItemClick, (MvlProject item) => clicked = item);
        });

        cut.FindAll(".tm-mvl-row").First().Click();

        clicked.Should().NotBeNull();
        clicked!.Name.Should().Be("Alpha");
    }
}

/// <summary>Helper that implements IMultiViewListItem for auto-mapping test.</summary>
public record AutoMappedItem(string Id, string Title, string? SubTitle) : IMultiViewListItem
{
    public string? AvatarUrl => null;
    public IReadOnlyList<ITag>? Tags => null;
    public string? StatusLabel => null;
    public string? StatusColor => null;
    public DateTimeOffset? Date => null;
}
