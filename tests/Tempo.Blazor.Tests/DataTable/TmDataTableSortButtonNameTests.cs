using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// The accessible-name contract of the <c>.tm-th-sort</c> button.
/// <para>
/// WCAG 4.1.2: every control must have a name a screen reader can announce — and a sort button's
/// name cannot be the sort ICON, because the icon span is <c>aria-hidden</c>. The name is now
/// emitted as an explicit <c>aria-label</c> on every render, in two parts:
/// <c>"Sort by {header} — {next action}"</c>. The header is the column <c>Title</c>, else
/// <c>SortLabel</c>, else — RELEASE only — the localized positional name
/// <c>"Column {n}"</c>; a templated header with neither is a developer error and throws in DEBUG.
/// The next action is the state the SAME tri-state cycle the click/Enter path walks reaches next:
/// none → ascending → descending → cleared — so the button never announces "sort ascending" on a
/// press that sorts descending.
/// </para>
/// </summary>
public class TmDataTableSortButtonNameTests : LocalizationTestBase
{
    private sealed record Person(string Name, int Age);

    private static readonly List<Person> People = [new("Alice", 34), new("Bob", 28)];

    private IRenderedComponent<TmDataTable<Person>> RenderTable(
        Action<Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder> configureColumn,
        string? defaultSortColumn = null,
        DataTableSortDirection defaultSortDirection = DataTableSortDirection.Ascending)
        => Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            if (defaultSortColumn is not null)
            {
                p.Add(c => c.DefaultSortColumn, defaultSortColumn);
                p.Add(c => c.DefaultSortDirection, defaultSortDirection);
            }

            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                configureColumn(b);
                b.AddAttribute(90, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

    [Fact]
    public void ASortButton_Announces_SortBy_Title_And_The_Next_Ascending()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Name — Sort ascending",
                "the name is 'Sort by {header} — {next action}': an unsorted column's next " +
                "activation sorts ascending");
    }

    [Fact]
    public void AColumnSortedAscending_Announces_SortDescending_As_The_Next_Action()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
        }, defaultSortColumn: "Name");

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Name — Sort descending",
                "the next activation on an ascending column sorts descending — the name must " +
                "track the cycle, not stay a static 'sort ascending'");
    }

    [Fact]
    public void AColumnSortedDescending_Announces_ClearSort_As_The_Next_Action()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
        }, defaultSortColumn: "Name", defaultSortDirection: DataTableSortDirection.Descending);

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Name — Clear sort",
                "the third step of the cycle clears the sort — the name says so");
    }

    [Fact]
    public void The_Next_Action_In_The_Name_Tracks_The_Sort_Cycle()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
        });
        var header = cut.Find("th[data-sortable='true']");
        var button = cut.Find("button.tm-th-sort");

        header.Click(); // → ascending
        button.GetAttribute("aria-label").Should().Be("Sort by Name — Sort descending");

        header.Click(); // → descending
        button.GetAttribute("aria-label").Should().Be("Sort by Name — Clear sort");

        header.Click(); // → cleared
        button.GetAttribute("aria-label").Should().Be("Sort by Name — Sort ascending");
    }

    [Fact]
    public void Title_Wins_As_The_Subject_When_SortLabel_Is_Also_Set()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "PropertyName", "Name");
            b.AddAttribute(3, "Sortable", true);
            b.AddAttribute(4, "SortLabel", "last name");
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Name — Sort ascending",
                "the visible caption is what a sighted user points at — the name says the same " +
                "subject; SortLabel is the replacement for a column that has no Title");
    }

    [Fact]
    public void SortLabel_Is_The_Subject_When_There_Is_No_Title()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "SortLabel", "last name");
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by last name — Sort ascending",
                "SortLabel exists exactly for the column whose header is not plain text");
    }

    [Fact]
    public void AHeaderTemplate_Keeps_The_Title_As_The_Subject()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "Title", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "HeaderTemplate",
                (RenderFragment)(hb => hb.AddContent(0, "filter ui")));
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Name — Sort ascending",
                "the template replaced the caption visually — the name still names the column");
    }

    [Fact]
    public void AHeaderTemplate_Without_Title_Uses_SortLabel()
    {
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "SortLabel", "last name");
            b.AddAttribute(4, "HeaderTemplate",
                (RenderFragment)(hb => hb.AddContent(0, "filter ui")));
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by last name — Sort ascending");
    }

    [Fact]
    public void AHeaderTemplate_Without_Title_Or_SortLabel_Is_A_Developer_Error()
    {
        // A template consumed the caption and supplied nothing to name the button — in DEBUG that
        // is thrown, in RELEASE the button degrades to the localized positional name. Both halves
        // are compiled; which one runs is the build configuration.
        var render = () => RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
            b.AddAttribute(3, "HeaderTemplate",
                (RenderFragment)(hb => hb.AddContent(0, "filter ui")));
        });

#if DEBUG
        render.Should().Throw<InvalidOperationException>(
            "a templated sortable header with no Title and no SortLabel leaves the button " +
            "unnamable — DEBUG refuses to ship the unnamed control");
#else
        using var cut = render();
        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Column 1 — Sort ascending",
                "RELEASE degrades to the positional name rather than crash a published page");
#endif
    }

    [Fact]
    public void AnIconOnlySortableHeader_Names_Itself_By_Position()
    {
        // PropertyName carries the column key; no Title, no template, no SortLabel.
        var cut = RenderTable(b =>
        {
            b.AddAttribute(1, "PropertyName", "Name");
            b.AddAttribute(2, "Sortable", true);
        });

        cut.Find("button.tm-th-sort").GetAttribute("aria-label")
            .Should().Be("Sort by Column 1 — Sort ascending",
                "a header with no text at all still gets a localized positional name");
    }

    [Fact]
    public void The_Positional_Fallback_Uses_The_Column_Index()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Sortable", true);
                b.AddAttribute(3, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<Person>>(4);
                b.AddAttribute(5, "PropertyName", "Name2");
                b.AddAttribute(6, "Sortable", true);
                b.AddAttribute(7, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        var buttons = cut.FindAll("button.tm-th-sort");
        buttons.Should().HaveCount(2);
        buttons[0].GetAttribute("aria-label").Should().Be("Sort by Name — Sort ascending");
        buttons[1].GetAttribute("aria-label").Should().Be("Sort by Column 2 — Sort ascending",
            "the fallback names the column by its position, so two unnamed headers still get " +
            "distinct names");
    }

    [Fact]
    public void AColumnPartOfAMultiSort_Announces_SortAscending_NotAStaleTriState()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name"); b.AddAttribute(2, "Sortable", true);
                b.AddAttribute(3, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
                b.OpenComponent<TmDataTableColumn<Person>>(10);
                b.AddAttribute(11, "Title", "Age"); b.AddAttribute(12, "Sortable", true);
                b.AddAttribute(13, "Field", (Func<Person, object?>)(x => x.Age));
                b.CloseComponent();
            });
        });

        cut.FindAll("th[data-sortable='true']")[0].Click();                                       // Name ascending (sole)
        cut.FindAll("th[data-sortable='true']")[1].Click(new MouseEventArgs { ShiftKey = true }); // + Age ascending → Count == 2
        cut.FindAll("th[data-sortable='true']")[1].Click(new MouseEventArgs { ShiftKey = true }); //   Age asc → desc, still inside the 2-descriptor multi-sort

        // Both columns are part of a 2-descriptor multi-sort. A PLAIN click on EITHER resets
        // every descriptor and sorts just that column ascending — the name must say so, never
        // "sort descending"/"clear sort" from the single-column cycle (N202, review 2026-09-22).
        var buttons = cut.FindAll("button.tm-th-sort");
        buttons[0].GetAttribute("aria-label").Should().Be("Sort by Name — Sort ascending",
            "Name is ascending but is one of TWO descriptors — a plain click does not cycle to " +
            "descending, it resets the whole sort to Name-ascending only");
        buttons[1].GetAttribute("aria-label").Should().Be("Sort by Age — Sort ascending",
            "Age is descending but is one of TWO descriptors — a plain click does not clear the " +
            "sort, it resets the whole sort to Age-ascending only");
    }

    [Fact]
    public void Every_Sortable_Column_Gets_A_Name_Not_Just_The_First()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.AddChildContent(b =>
            {
                b.OpenComponent<TmDataTableColumn<Person>>(0);
                b.AddAttribute(1, "Title", "Name");
                b.AddAttribute(2, "Sortable", true);
                b.AddAttribute(3, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();

                b.OpenComponent<TmDataTableColumn<Person>>(4);
                b.AddAttribute(5, "PropertyName", "Name2");
                b.AddAttribute(6, "Sortable", true);
                b.AddAttribute(7, "Field", (Func<Person, object?>)(x => x.Name));
                b.CloseComponent();
            });
        });

        var buttons = cut.FindAll("button.tm-th-sort");
        buttons.Should().HaveCount(2);
        buttons.Should().AllSatisfy(button => button.GetAttribute("aria-label").Should().NotBeNullOrEmpty(
            "the guarantee holds for every sort button, not only the one under test"));
    }
}
