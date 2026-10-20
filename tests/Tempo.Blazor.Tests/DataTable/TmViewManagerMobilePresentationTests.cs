using Bunit;
using FluentAssertions;
using NSubstitute;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Phase 13: the Views panel is the one toolbar overlay that never offered the mobile sheet.
/// <c>MobilePresentation</c> defaults to Popover (today's behaviour) and the table/list pass Auto
/// together with their own mobile opt-in, like the export menu and the group picker already do.
/// </summary>
public class TmViewManagerMobilePresentationTests : LocalizationTestBase
{
    private static IDataTableViewProvider Provider()
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>(new List<DataTableView>
            {
                new() { Id = "v1", Name = "All", Scope = ViewScope.Personal, IsDefault = true, CreatedBy = "ada" },
            }));
        return provider;
    }

    [Fact]
    public void Panel_DefaultsToPopover()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider())
            .Add(c => c.ViewContext, "ctx"));

        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation.Should().Be(PanelPresentation.Popover);
    }

    [Fact]
    public void Panel_FollowsTheMobilePresentationParameter()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, Provider())
            .Add(c => c.ViewContext, "ctx")
            .Add(c => c.MobilePresentation, PanelPresentation.Auto));

        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation.Should().Be(PanelPresentation.Auto);
    }

    [Theory]
    [InlineData(DataTableMobileLayout.None, PanelPresentation.Popover)]
    [InlineData(DataTableMobileLayout.Scroll, PanelPresentation.Auto)]
    [InlineData(DataTableMobileLayout.Cards, PanelPresentation.Auto)]
    public void DataTable_PassesAutoWithItsMobileLayout(DataTableMobileLayout layout, PanelPresentation expected)
    {
        var cut = Render<TmDataTable<string>>(p => p
            .Add(c => c.ViewContext, "items")
            .Add(c => c.Items, new[] { "A" })
            .Add(c => c.ViewProvider, Provider())
            .Add(c => c.ShowPagination, false)
            .Add(c => c.MobileLayout, layout)
            .AddChildContent<TmDataTableColumn<string>>(c => c
                .Add(x => x.Title, "Value")
                .Add(x => x.PropertyName, "Value")));

        cut.FindComponent<TmViewManager>().Instance.MobilePresentation.Should().Be(expected);
    }

    [Theory]
    [InlineData(false, PanelPresentation.Popover)]
    [InlineData(true, PanelPresentation.Auto)]
    public void MultiViewList_PassesAutoWhenResponsive(bool responsive, PanelPresentation expected)
    {
        var cut = Render<TmMultiViewList<Row>>(p => p
            .Add(c => c.ViewContext, "rows")
            .Add(c => c.Items, new List<Row> { new("Ada") })
            .Add(c => c.ViewProvider, Provider())
            .Add(c => c.Responsive, responsive)
            .Add(c => c.TitleField, (Func<Row, string>)(row => row.Name)));

        cut.FindComponent<TmViewManager>().Instance.MobilePresentation.Should().Be(expected);
    }

    private sealed record Row(string Name);
}
