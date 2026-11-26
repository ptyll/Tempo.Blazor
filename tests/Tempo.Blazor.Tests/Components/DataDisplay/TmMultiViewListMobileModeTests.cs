using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Phase 10 for <c>TmMultiViewList</c>: the responsive opt-in becomes a named container, the title
/// column sticks through custom properties, and the card/list views gain the sort/filter toolbar.
/// </summary>
public class TmMultiViewListMobileModeTests : LocalizationTestBase
{
    [Fact]
    public void Responsive_MarksTheRootAsAContainerAndSticksTheTitle()
    {
        var cut = Render<TmMultiViewList<MvlPerson>>(p => p
            .Add(c => c.Items, new List<MvlPerson> { new("Ada", "Engineer") })
            .Add(c => c.Responsive, true)
            .Add(c => c.AllowSelection, true)
            .Add(c => c.TitleField, (Func<MvlPerson, string>)(person => person.Name)));

        cut.Find(".tm-multi-view-list--container").Should().NotBeNull();
        cut.Find(".tm-mvl-col-select").GetAttribute("style").Should().Contain("--tm-pin-left:0px");
        cut.Find("th.tm-mvl-title-column").GetAttribute("style").Should().Contain("--tm-pin-left:var(--tm-mvl-select-column)");
    }

    [Fact]
    public void ResponsiveOff_SticksNothing()
    {
        var cut = Render<TmMultiViewList<MvlPerson>>(p => p
            .Add(c => c.Items, new List<MvlPerson> { new("Ada", "Engineer") })
            .Add(c => c.TitleField, (Func<MvlPerson, string>)(person => person.Name)));

        cut.Markup.Should().NotContain("tm-multi-view-list--container");
        cut.Markup.Should().NotContain("--tm-pin-left");
    }

    [Fact]
    public void Cards_SortAndFilterOpenASheet()
    {
        var cut = Render<TmMultiViewList<MvlPerson>>(p => p
            .Add(c => c.Items, new List<MvlPerson> { new("Ada", "Engineer"), new("Bea", "Designer") })
            .Add(c => c.Responsive, true)
            .Add(c => c.TitleField, (Func<MvlPerson, string>)(person => person.Name))
            .Add(c => c.SubTitleField, (Func<MvlPerson, string?>)(person => person.Role))
            .Add(c => c.ViewMode, ListViewMode.Card));

        cut.Find(".tm-mvl-cards-toolbar").Should().NotBeNull();
        cut.Find(".tm-mvl-cards-toolbar__sort").Click();

        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation
            .Should().Be(PanelPresentation.Auto);
        cut.Markup.Should().Contain("data-card-sort=\"title\"");
        cut.Markup.Should().Contain("data-card-sort=\"subtitle\"");

        cut.Find("[data-card-sort=\"title\"]").GetAttribute("aria-pressed").Should().Be("false");
        cut.Find("[data-card-sort=\"title\"]").Click();
        cut.Find("[data-card-sort=\"title\"]").GetAttribute("aria-pressed").Should().Be("true");
        cut.Markup.Should().Contain("tm-sort-asc");
        cut.FindAll(".tm-mvl-card-title").Select(card => card.TextContent.Trim())
            .Should().ContainInOrder("Ada", "Bea");

        cut.Find("[data-card-sort=\"title\"]").Click();
        cut.FindAll(".tm-mvl-card-title").Select(card => card.TextContent.Trim())
            .Should().ContainInOrder("Bea", "Ada");
    }

    [Fact]
    public void Cards_SortTriggerAriaExpanded_IsAlwaysAString()
    {
        // Fáze 4c (plan b75365fb): a bool aria-expanded renders the invalid empty attribute when
        // open and drops the attribute entirely when closed — the state must be a string.
        var cut = Render<TmMultiViewList<MvlPerson>>(p => p
            .Add(c => c.Items, new List<MvlPerson> { new("Ada", "Engineer"), new("Bea", "Designer") })
            .Add(c => c.Responsive, true)
            .Add(c => c.TitleField, (Func<MvlPerson, string>)(person => person.Name))
            .Add(c => c.SubTitleField, (Func<MvlPerson, string?>)(person => person.Role))
            .Add(c => c.ViewMode, ListViewMode.Card));

        var sort = cut.Find(".tm-mvl-cards-toolbar__sort");
        sort.GetAttribute("aria-expanded").Should().Be("false",
            "a closed sort sheet announces collapsed with the string \"false\"");

        sort.Click();
        cut.Find(".tm-mvl-cards-toolbar__sort").GetAttribute("aria-expanded").Should().Be("true",
            "an open sort sheet announces expanded with the string \"true\", never the invalid empty value");
    }

    [Fact]
    public void Cards_FilterTriggerAriaExpanded_IsAlwaysAString()
    {
        var cut = Render<TmMultiViewList<MvlPerson>>(p => p
            .Add(c => c.Items, new List<MvlPerson> { new("Ada", "Engineer"), new("Bea", "Designer") })
            .Add(c => c.Responsive, true)
            .Add(c => c.TitleField, (Func<MvlPerson, string>)(person => person.Name))
            .Add(c => c.SubTitleField, (Func<MvlPerson, string?>)(person => person.Role))
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.ViewProvider, NSubstitute.Substitute.For<IDataTableViewProvider>())
            .Add(c => c.ExternalFilterDefinitions, new List<FilterDefinition>
            {
                new() { FieldName = "Name", FieldLabel = "Name", FieldType = FilterFieldType.Text },
            }));

        var filter = cut.Find(".tm-mvl-cards-toolbar__filter");
        filter.GetAttribute("aria-expanded").Should().Be("false");

        filter.Click();
        cut.Find(".tm-mvl-cards-toolbar__filter").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void SourceCss_QueriesItsOwnContainerAndSticksTheTitleLogically()
    {
        var css = ThemeCss.ComponentCss("_multi-view-list.css");

        css.Should().Contain("container-type: inline-size");
        css.Should().Contain("container-name: tm-multi-view-list");
        css.Should().Contain("@container tm-multi-view-list (width < 640px)");

        ThemeCss.Property("_multi-view-list.css", ".tm-mvl-title-column", "position").Should().Be("sticky");
        ThemeCss.Property("_multi-view-list.css", ".tm-mvl-title-column", "inset-inline-start")
            .Should().Be("var(--tm-pin-left, 0)");
    }

    private sealed record MvlPerson(string Name, string Role);
}
