using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Filters;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Filters;

/// <summary>
/// Auto-wire of the filter result preview: a data table and a multi-view list pass their own
/// count function into the <see cref="TmFilterBuilder"/> they render, counting through the data
/// provider (one row asked for) or the client-side items.
/// </summary>
public class FilterResultPreviewAutoWireTests : LocalizationTestBase
{
    public record Person(string Name, string Role);

    private static readonly List<Person> People =
    [
        new("Alice", "Admin"),
        new("Bob", "User"),
        new("Carol", "Admin"),
    ];

    private static RenderFragment Columns => b =>
    {
        b.OpenComponent<TmDataTableColumn<Person>>(0);
        b.AddAttribute(1, "Title", "Name");
        b.AddAttribute(2, "PropertyName", "Name");
        b.AddAttribute(3, "Field", (Func<Person, object?>)(x => x.Name));
        b.CloseComponent();
    };

    private static IDataTableViewProvider ViewProvider()
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));
        return provider;
    }

    private static List<FilterDefinition> NameFilter =>
        [new FilterDefinition { FieldName = "Name", FieldLabel = "Name", FieldType = FilterFieldType.Text }];

    /// <summary>A table with a data provider counts through GetDataAsync and asks for a single row.</summary>
    [Fact]
    public void DataTable_WithDataProvider_CountsWithPageSizeOne()
    {
        var data = Substitute.For<IDataTableDataProvider<Person>>();
        data.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new PagedResult<Person>
            {
                Items = [],
                TotalCount = 7,
                Page = 1,
                PageSize = call.Arg<DataTableQuery>().PageSize,
            }));

        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.DataProvider, data)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .AddChildContent(Columns));

        var builder = cut.FindComponent<TmFilterBuilder>();
        builder.Instance.ResultCountProvider.Should().NotBeNull();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("7 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        // The table's own load plus the preview. The preview never asks for a page of rows.
        var preview = data.ReceivedCalls()
            .Select(c => c.GetArguments().OfType<DataTableQuery>().Single())
            .Should().ContainSingle(q => q.PageSize == 1)
            .Which;
        preview.Page.Should().Be(1);
    }

    /// <summary>Client-side data is counted locally: three rows, no data provider involved.</summary>
    [Fact]
    public void DataTable_ClientItems_CountsMatchingRows()
    {
        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .AddChildContent(Columns));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("3 matching records"),
            timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>The preview stays off unless the host opts in, even when the filter builder renders.</summary>
    [Fact]
    public void DataTable_WithoutShowFilterResultPreview_WiresNoProvider()
    {
        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .AddChildContent(Columns));

        cut.FindComponent<TmFilterBuilder>().Instance.ResultCountProvider.Should().BeNull();
        cut.FindAll(".tm-filter-result-preview").Should().BeEmpty();
    }

    /// <summary>A list with a data provider counts through GetDataAsync and asks for a single row.</summary>
    [Fact]
    public void MultiViewList_WithDataProvider_CountsWithPageSizeOne()
    {
        var data = Substitute.For<IDataTableDataProvider<Person>>();
        data.GetDataAsync(Arg.Any<DataTableQuery>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(new PagedResult<Person>
            {
                Items = new List<Person>(),
                TotalCount = 4,
                Page = 1,
                PageSize = call.Arg<DataTableQuery>().PageSize,
            }));

        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.DataProvider, data)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .Add(c => c.TitleField, person => person.Name));

        cut.FindComponent<TmFilterBuilder>().Instance.ResultCountProvider.Should().NotBeNull();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("4 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        data.ReceivedCalls()
            .Select(c => c.GetArguments().OfType<DataTableQuery>().Single())
            .Should().ContainSingle(q => q.PageSize == 1);
    }

    /// <summary>Client-side list items are counted locally.</summary>
    [Fact]
    public void MultiViewList_ClientItems_CountsMatchingRows()
    {
        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .Add(c => c.TitleField, person => person.Name));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("3 matching records"),
            timeout: TimeSpan.FromSeconds(2));
    }
}
