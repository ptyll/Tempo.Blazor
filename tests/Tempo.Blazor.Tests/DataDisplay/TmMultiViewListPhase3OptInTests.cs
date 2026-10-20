using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>
/// Phase 3: card-view group chrome and the card-column cap are opt-in.
/// Table view keeps the pre-redesign group row; card and list views keep the card group.
/// </summary>
public class TmMultiViewListPhase3OptInTests : LocalizationTestBase
{
    private sealed record Person(string Name, string Department);

    private static IReadOnlyList<GroupFieldDefinition<Person>> Fields =>
    [
        new()
        {
            FieldName = "Department",
            Label = "Department",
            FieldAccessor = person => person.Department,
        },
    ];

    private IRenderedComponent<TmMultiViewList<Person>> RenderList(
        ListViewMode mode,
        int? maxCardColumns = null,
        DataTableAppearance appearance = DataTableAppearance.Card)
    {
        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.Items, new List<Person> { new("Ada", "Engineering"), new("Bea", "Engineering") })
            .Add(c => c.ViewMode, mode)
            .Add(c => c.Appearance, appearance)
            .Add(c => c.ShowPagination, false)
            .Add(c => c.GroupsCollapsedByDefault, false)
            .Add(c => c.GroupableFields, Fields)
            .Add(c => c.TitleField, (Func<Person, string?>)(person => person.Name))
            .Add(c => c.MaxCardColumns, maxCardColumns));
        cut.Find(".tm-mvl-group-btn").Click();
        cut.Find(".tm-mvl-group-option input").Change(true);
        return cut;
    }

    [Fact]
    public void TableView_KeepsCompactGroupToggle()
    {
        var cut = RenderList(ListViewMode.Table);

        var toggle = cut.Find(".tm-mvl-group-toggle");
        toggle.QuerySelector(".tm-mvl-group-label").Should().BeNull();
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        toggle.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.Find(".tm-mvl-group-label").Should().NotBeNull();
        cut.Markup.Should().NotContain("tm-mvl-group-card");
    }

    [Fact]
    public void CardView_UsesCardGroupChrome()
    {
        var cut = RenderList(ListViewMode.Card);

        cut.Find(".tm-mvl-group-card").Should().NotBeNull();
        cut.Find(".tm-mvl-group-toggle").GetAttribute("aria-controls").Should().NotBeNullOrEmpty();
        cut.Find(".tm-mvl-group-toggle .tm-mvl-group-label").Should().NotBeNull();
    }

    [Fact]
    public void MaxCardColumns_IsUnsetByDefault()
    {
        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.Items, new List<Person> { new("Ada", "Engineering") })
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.TitleField, (Func<Person, string?>)(person => person.Name)));

        cut.Instance.MaxCardColumns.Should().BeNull();
        cut.Find(".tm-mvl-card-grid").ClassList.Should().NotContain("tm-mvl-card-grid--capped");
    }

    [Fact]
    public void MaxCardColumns_CapsTheGrid()
    {
        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.Items, new List<Person> { new("Ada", "Engineering") })
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.MaxCardColumns, 3)
            .Add(c => c.TitleField, (Func<Person, string?>)(person => person.Name)));

        var grid = cut.Find(".tm-mvl-card-grid");
        grid.ClassList.Should().Contain("tm-mvl-card-grid--capped");
        grid.GetAttribute("style").Should().Contain("--tm-mvl-card-columns-max: 3");
    }

    [Fact]
    public void MaxCardColumns_BelowOne_IsIgnored()
    {
        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.Items, new List<Person> { new("Ada", "Engineering") })
            .Add(c => c.ViewMode, ListViewMode.Card)
            .Add(c => c.MaxCardColumns, 0)
            .Add(c => c.TitleField, (Func<Person, string?>)(person => person.Name)));

        cut.Find(".tm-mvl-card-grid").ClassList.Should().NotContain("tm-mvl-card-grid--capped");
    }

    [Fact]
    public void Plain_MatchesThePreRedesignRoot()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new List<string>())
            .Add(c => c.Appearance, DataTableAppearance.Plain));

        cut.Find(".tm-multi-view-list").ClassList.Should().Contain("tm-multi-view-list--plain");
        cut.Find(".tm-multi-view-list").ClassList.Should().NotContain("tm-multi-view-list--card");
    }

    [Fact]
    public void DefaultCardGrid_FillsFromMinimumWidth()
    {
        var source = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Tempo.Blazor/wwwroot/css/components/_multi-view-list.css")));
        var bundle = File.ReadAllText(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "../../../../../src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css")));

        var sourceRule = Regex.Match(source, @"\.tm-mvl-card-grid\s*\{(?<body>[^}]*)\}").Groups["body"].Value;
        sourceRule.Should().Contain("grid-template-columns: repeat(auto-fill, minmax(var(--tm-mvl-card-min), 1fr))");
        sourceRule.Should().NotContain("/ 3");

        var bundleRule = Regex.Match(bundle, @"\.tm-mvl-card-grid\{(?<body>[^}]*)\}").Groups["body"].Value;
        bundleRule.Should().Contain("grid-template-columns:repeat(auto-fill,minmax(var(--tm-mvl-card-min),1fr))");
        bundleRule.Should().NotContain("/ 3");
    }
}
