using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListToolbarTests : LocalizationTestBase
{
    [Fact]
    public void IconsOnly_DoesNotRenderViewNames()
    {
        var cut = Render<TmMultiViewList<string>>(p => p.Add(c => c.Items, new List<string>()));
        cut.Find(".tm-mvl-switch-table").TextContent.Should().NotContain("Table view");
    }

    [Fact]
    public void IconsAndText_RendersViewNames()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new List<string>())
            .Add(c => c.ViewSwitcherLabels, ViewSwitcherLabels.IconsAndText));
        cut.Find(".tm-mvl-switch-table").TextContent.Should().Contain("Table view");
        cut.Find(".tm-mvl-switch-card").TextContent.Should().Contain("Card view");
        cut.Find(".tm-mvl-switch-list").TextContent.Should().Contain("List view");
    }
}
