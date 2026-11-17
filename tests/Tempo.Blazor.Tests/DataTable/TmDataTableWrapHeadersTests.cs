using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

internal record WrapPerson(string Name, string Department);

/// <summary>
/// Fáze 7: table headers are hard <c>nowrap</c>. Hosts that need wrapped headers patch
/// <c>::deep .tm-data-table th</c> today. <c>WrapHeaders</c> (table-wide) and
/// <c>HeaderWrap</c> (per column, opt-in over the table default) do it additively.
/// </summary>
public class TmDataTableWrapHeadersTests : LocalizationTestBase
{
    private static List<WrapPerson> People => [new("Alice", "Engineering")];

    private ComponentParameterCollectionBuilder<TmDataTable<WrapPerson>> Columns(
        ComponentParameterCollectionBuilder<TmDataTable<WrapPerson>> p,
        bool? secondColumnHeaderWrap = null)
    {
        p.AddChildContent<TmDataTableColumn<WrapPerson>>(c => c
            .Add(x => x.Title, "Name")
            .Add(x => x.Field, (Func<WrapPerson, object?>)(x => x.Name)));
        p.AddChildContent<TmDataTableColumn<WrapPerson>>(c =>
        {
            c.Add(x => x.Title, "Department with a very long header")
             .Add(x => x.Field, (Func<WrapPerson, object?>)(x => x.Department));
            if (secondColumnHeaderWrap is { } wrap)
                c.Add(x => x.HeaderWrap, wrap);
        });
        return p;
    }

    [Fact]
    public void WrapHeaders_DefaultOff_TableKeepsNowrapClassFreeRoot()
    {
        var cut = Render<TmDataTable<WrapPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            Columns(p);
        });

        cut.Find("table.tm-data-table").ClassList.Should().NotContain("tm-data-table--wrap-headers");
    }

    [Fact]
    public void WrapHeaders_True_TableCarriesWrapClass()
    {
        var cut = Render<TmDataTable<WrapPerson>>(p =>
        {
            p.Add(c => c.Items, People).Add(c => c.WrapHeaders, true);
            Columns(p);
        });

        cut.Find("table.tm-data-table").ClassList.Should().Contain("tm-data-table--wrap-headers");
    }

    [Fact]
    public void HeaderWrap_True_ColumnHeaderCarriesWrapClass()
    {
        var cut = Render<TmDataTable<WrapPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            Columns(p, secondColumnHeaderWrap: true);
        });

        cut.FindAll("thead th").Count(th => th.ClassList.Contains("tm-col-header--wrap")).Should().Be(1,
            "only the opted-in column wraps");
    }

    [Fact]
    public void HeaderWrap_Default_ColumnHeaderStaysNowrap()
    {
        var cut = Render<TmDataTable<WrapPerson>>(p =>
        {
            p.Add(c => c.Items, People);
            Columns(p);
        });

        cut.FindAll("thead th.tm-col-header--wrap").Should().BeEmpty();
    }
}
