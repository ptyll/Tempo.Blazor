using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

/// <summary>
/// Fáze 10a (HeatControl mocks): <c>class</c> is a reserved <c>RowAttributes</c> name (the table owns the row
/// class), so a host had no way to style the current / highlighted row without <c>Selectable</c>.
/// <see cref="TmDataTable{TItem}.RowClass"/> is that way: its result is merged into the classes the row
/// already carries — never replacing them.
/// </summary>
public class TmDataTableRowClassTests : LocalizationTestBase
{
    private sealed record Person(string Name, string Role);

    private static readonly List<Person> People =
    [
        new("Alice", "Admin"),
        new("Bob", "User"),
        new("Carol", "Manager"),
    ];

    private static void Columns(Bunit.ComponentParameterCollectionBuilder<TmDataTable<Person>> p) =>
        p.AddChildContent<TmDataTableColumn<Person>>(c => c
            .Add(x => x.Title, "Name")
            .Add(x => x.PropertyName, "Name")
            .Add(x => x.Field, x => x.Name));

    private static string? RowClassOf(AngleSharp.Dom.IElement row) => row.GetAttribute("class");

    private AngleSharp.Dom.IElement RowOf(IRenderedComponent<TmDataTable<Person>> cut, string name) =>
        cut.FindAll("tbody tr").Single(r => r.TextContent.Contains(name, StringComparison.Ordinal));

    [Fact]
    public void RowClass_IsAppliedToTheRowItReturnsItFor_AndOnlyThere()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.RowClass, row => row.Name == "Bob" ? "is-current" : null);
            Columns(p);
        });

        RowOf(cut, "Bob").ClassList.Should().Contain("is-current");
        RowOf(cut, "Alice").ClassList.Should().NotContain("is-current");
        RowOf(cut, "Carol").ClassList.Should().NotContain("is-current");
    }

    [Fact]
    public void RowClass_IsMergedWithTheInternalSelectedClass_NotReplacingIt()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.Selectable, true);
            p.Add(c => c.RowClass, _ => "status-row");
            Columns(p);
        });

        cut.FindAll("tbody tr td input[type='checkbox']")[1].Change(true);

        var row = RowOf(cut, "Bob");
        row.ClassList.Should().Contain("tm-row-selected", "the table's own state class survives");
        row.ClassList.Should().Contain("status-row");
        RowClassOf(RowOf(cut, "Alice")).Should().Be("status-row");
    }

    [Fact]
    public void RowClass_SeveralClasses_AreAllApplied_AndStraySpacesAreTidied()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.RowClass, _ => "  a   b  ");
            Columns(p);
        });

        RowClassOf(RowOf(cut, "Alice")).Should().Be("a b");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RowClass_NullOrBlank_AddsNothing(string? returned)
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.RowClass, _ => returned);
            Columns(p);
        });

        var row = RowOf(cut, "Alice");
        (RowClassOf(row) ?? string.Empty).Should().BeEmpty("a blank result must not leave a stray class token");
    }

    [Fact]
    public void RowClass_NotSet_LeavesTheRowMarkupUnchanged()
    {
        var plain = Render<TmDataTable<Person>>(p => { p.Add(c => c.Items, People); Columns(p); });
        var withNull = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.RowClass, (Func<Person, string?>?)null);
            Columns(p);
        });

        // The event-handler ids (blazor:onclick="21") are per-render counters, not markup.
        static string Stable(string html) => System.Text.RegularExpressions.Regex.Replace(
            html, @"blazor:on\w+=""\d+""", string.Empty, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));

        withNull.FindAll("tbody tr").Select(r => Stable(r.OuterHtml))
            .Should().Equal(plain.FindAll("tbody tr").Select(r => Stable(r.OuterHtml)));
    }

    [Fact]
    public void RowAttributes_ClassIsStillReserved_RowClassIsTheWayToStyleARow()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.RowAttributes, _ => new Dictionary<string, object> { ["class"] = "from-attributes", ["data-x"] = "1" });
            p.Add(c => c.RowClass, _ => "from-row-class");
            Columns(p);
        });

        var row = RowOf(cut, "Alice");
        row.ClassList.Should().Contain("from-row-class");
        row.ClassList.Should().NotContain("from-attributes", "the reserved name keeps being dropped (documented), RowClass replaces the need");
        row.GetAttribute("data-x").Should().Be("1");
    }

    [Fact]
    public void RowClass_IsAppliedToGroupedLeafRows()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ShowGrouping, true);
            p.Add(c => c.GroupsCollapsedByDefault, false);
            p.Add(c => c.RowClass, row => row.Name == "Bob" ? "is-current" : null);
            p.AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Name").Add(x => x.PropertyName, "Name").Add(x => x.Field, x => x.Name));
            p.AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Role").Add(x => x.PropertyName, "Role").Add(x => x.Field, x => x.Role)
                .Add(x => x.Groupable, true));
        });

        cut.InvokeAsync(() => cut.Instance.AddGroupColumn("Role")).GetAwaiter().GetResult();

        var row = cut.FindAll("tbody tr:not(.tm-data-table-group-row)")
            .Single(r => r.TextContent.Contains("Bob", StringComparison.Ordinal));
        row.ClassList.Should().Contain("is-current");
        cut.FindAll("tbody tr.is-current").Should().HaveCount(1);
    }

    [Fact]
    public void RowClass_IsAppliedToVirtualizedRows()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.ScrollMode, DataTableScrollMode.Virtualized);
            p.Add(c => c.VirtualScrollHeight, "300px");
            p.Add(c => c.RowClass, row => row.Name == "Carol" ? "is-current" : null);
            Columns(p);
        });

        RowOf(cut, "Carol").ClassList.Should().Contain("is-current");
        cut.FindAll("tbody tr.is-current").Should().HaveCount(1);
    }

    [Fact]
    public void RowClass_FollowsTheItem_WhenTheHostChangesWhichRowIsCurrent()
    {
        var current = "Alice";
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.RowClass, row => row.Name == current ? "is-current" : null);
            Columns(p);
        });
        RowOf(cut, "Alice").ClassList.Should().Contain("is-current");

        current = "Carol";
        cut.Render(); // the host re-renders the table; the delegate is read again

        RowOf(cut, "Carol").ClassList.Should().Contain("is-current");
        RowOf(cut, "Alice").ClassList.Should().NotContain("is-current");
    }

    [Fact]
    public void RowClass_StaysOnTheRowWhileItIsBeingEdited()
    {
        var cut = Render<TmDataTable<Person>>(p =>
        {
            p.Add(c => c.Items, People);
            p.Add(c => c.Editable, true);
            p.Add(c => c.RowClass, row => row.Name == "Bob" ? "is-current" : null);
            p.AddChildContent<TmDataTableColumn<Person>>(c => c
                .Add(x => x.Title, "Name")
                .Add(x => x.Field, x => x.Name)
                .Add(x => x.Editable, true)
                .Add(x => x.EditTemplate, item => (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddMarkupContent(0, $"<input value=\"{item.Name}\" />"))));
        });

        RowOf(cut, "Bob").DoubleClick();

        var editing = cut.Find("tr.tm-row-editing");
        editing.ClassList.Should().Contain("is-current", "the current-row styling must not vanish the moment the row enters edit mode");
        editing.ClassList.Should().Contain("tm-data-table-row--editing");
    }
}
