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
/// Result-preview correctness bugs found in review: the client-side count in
/// <see cref="TmDataTable{TItem}"/> must not count an edited external filter twice, and both
/// hosts must push an invalidation token (<c>ResultCountKey</c>) so the preview recounts when
/// the search text, column filters or the data change behind the builder's back.
/// </summary>
public class FilterResultPreviewEditTests : LocalizationTestBase
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
        var provider = NSubstitute.Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));
        return provider;
    }

    private static List<FilterDefinition> NameFilter =>
        [new FilterDefinition { FieldName = "Name", FieldLabel = "Name", FieldType = FilterFieldType.Text }];

    /// <summary>
    /// Editing an applied filter must count only the new value: the table has already copied the
    /// applied filter into its own column-filter map, and counting it together with the draft
    /// produced "contains a AND contains b" — zero rows.
    /// </summary>
    [Fact]
    public void DataTable_EditedFilter_CountsOnlyTheNewValue()
    {
        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .AddChildContent(Columns));

        var builder = cut.FindComponent<TmFilterBuilder>();
        builder.Instance.ResultCountDebounceMs = 10;

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("3 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        // Apply Name contains "a" → Alice and Carol.
        cut.Find(".tm-filter-builder-add").Click();
        cut.Find(".tm-filter-field-option").Click();
        cut.Find(".tm-filter-value-input").Change("a");
        cut.Find(".tm-filter-apply").Click();

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("2 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        // Open the chip for editing and change the value to "b" → only Bob.
        cut.Find(".tm-filter-chip").Click();
        cut.Find(".tm-filter-value-input").Change("b");

        cut.WaitForAssertion(() =>
        {
            var preview = cut.Find(".tm-filter-result-preview").TextContent.Trim();
            preview.Should().Be("1 matching record");
            preview.Should().NotBe("0 matching records");
        }, timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>A search typed into the table's own search box must invalidate the preview count.</summary>
    [Fact]
    public void DataTable_SearchChange_UpdatesPreviewCount()
    {
        var cut = Render<TmDataTable<Person>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .AddChildContent(Columns));

        var builder = cut.FindComponent<TmFilterBuilder>();
        builder.Instance.ResultCountDebounceMs = 10;

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("3 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        cut.Render(p => p.Add(c => c.SearchText, "bob"));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("1 matching record"),
            timeout: TimeSpan.FromSeconds(2));
    }

    /// <summary>A search typed into the list's own search box must invalidate the preview count.</summary>
    [Fact]
    public void MultiViewList_SearchChange_UpdatesPreviewCount()
    {
        var cut = Render<TmMultiViewList<Person>>(p => p
            .Add(c => c.Items, People)
            .Add(c => c.ViewContext, "preview")
            .Add(c => c.ViewProvider, ViewProvider())
            .Add(c => c.ExternalFilterDefinitions, NameFilter)
            .Add(c => c.ShowFilterResultPreview, true)
            .Add(c => c.TitleField, (Func<Person, string>)(person => person.Name)));

        var builder = cut.FindComponent<TmFilterBuilder>();
        builder.Instance.ResultCountDebounceMs = 10;

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("3 matching records"),
            timeout: TimeSpan.FromSeconds(2));

        cut.Render(p => p.Add(c => c.SearchText, "bob"));

        cut.WaitForAssertion(() =>
            cut.Find(".tm-filter-result-preview").TextContent.Trim().Should().Be("1 matching record"),
            timeout: TimeSpan.FromSeconds(2));
    }
}
