using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionPageSearchTests : LocalizationTestBase
{
    [Fact]
    public async Task SearchPanel_AppliesAdvancedFiltersAndRendersHighlightedBlockResult()
    {
        var provider = new CapturingSearchProvider();
        var context = new NotionEditorContext
        {
            SearchProvider = provider
        };

        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageSearch>());
        var search = host.FindComponent<TmNotionPageSearch>();

        await search.InvokeAsync(() => search.Instance.OpenPageSearch());
        host.WaitForAssertion(() => host.Find(".tm-nps").Should().NotBeNull());

        await host.Find(".tm-nps__filter-toggle").ClickAsync(new MouseEventArgs());
        await host.Find("[data-testid='notion-search-filter-author']").ChangeAsync(new ChangeEventArgs { Value = "alice" });
        await host.Find("[data-testid='notion-search-filter-label']").ChangeAsync(new ChangeEventArgs { Value = "engineering" });
        await host.Find("[data-testid='notion-search-filter-type']").ChangeAsync(new ChangeEventArgs { Value = "Paragraph" });
        await host.Find("[data-testid='notion-search-filter-space']").ChangeAsync(new ChangeEventArgs { Value = "CF22 Knowledge Space" });
        await host.Find("[data-testid='notion-search-filter-created-after']").ChangeAsync(new ChangeEventArgs { Value = "2026-01-01" });
        await host.Find("[data-testid='notion-search-filter-edited-before']").ChangeAsync(new ChangeEventArgs { Value = "2026-01-31" });
        await host.Find(".tm-nps__search-input").InputAsync(new ChangeEventArgs { Value = "beacon" });

        host.WaitForAssertion(() =>
        {
            provider.LastQuery.Should().Be("beacon");
            provider.LastFilter.Should().NotBeNull();
            provider.LastFilter!.Author.Should().Be("alice");
            provider.LastFilter.LabelFilter.Should().Be("engineering");
            provider.LastFilter.ContentType.Should().Be("Paragraph");
            provider.LastFilter.BlockType.Should().Be(BlockType.Paragraph);
            provider.LastFilter.SpaceId.Should().Be("CF22 Knowledge Space");
            provider.LastFilter.CreatedAfter.Should().Be(new DateTime(2026, 1, 1));
            provider.LastFilter.LastEditedBefore.Should().Be(new DateTime(2026, 1, 31));
            host.Find(".tm-nps__item-snippet mark").TextContent.Should().Be("beacon");
        });
    }

    [Fact]
    public async Task EnterOnFilterToggle_TogglesFilters_WithoutNavigatingOrClosing()
    {
        // The filter toggle is a native <button>: the browser turns Enter into
        // a click on keydown. A card-level Enter->select would ALSO navigate
        // the highlighted result (closing the panel) for the same gesture.
        var provider = new CapturingSearchProvider();
        var navigated = new List<string>();
        var context = new NotionEditorContext
        {
            SearchProvider = provider,
            NavigateTo = id => { navigated.Add(id); return Task.CompletedTask; }
        };

        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageSearch>());
        var search = host.FindComponent<TmNotionPageSearch>();

        await search.InvokeAsync(() => search.Instance.OpenPageSearch());
        host.WaitForAssertion(() => host.Find(".tm-nps").Should().NotBeNull());

        await host.Find(".tm-nps__search-input").InputAsync(new ChangeEventArgs { Value = "beacon" });
        host.WaitForAssertion(() => host.FindAll(".tm-nps__item").Should().NotBeEmpty());

        // Real sequence for Enter on a focused <button>: keydown -> click.
        var toggle = host.Find(".tm-nps__filter-toggle");
        await toggle.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await host.Find(".tm-nps__filter-toggle").ClickAsync(new MouseEventArgs());

        navigated.Should().BeEmpty("Enter on the filter toggle is not a result selection");
        host.Find(".tm-nps__filters").Should().NotBeNull();
        host.Find(".tm-nps").Should().NotBeNull("the panel must stay open");
    }

    [Fact]
    public async Task EnterInSearchInput_SelectsHighlightedResult()
    {
        var provider = new CapturingSearchProvider();
        var navigated = new List<string>();
        var context = new NotionEditorContext
        {
            SearchProvider = provider,
            NavigateTo = id => { navigated.Add(id); return Task.CompletedTask; }
        };

        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageSearch>());
        var search = host.FindComponent<TmNotionPageSearch>();

        await search.InvokeAsync(() => search.Instance.OpenPageSearch());
        await host.Find(".tm-nps__search-input").InputAsync(new ChangeEventArgs { Value = "beacon" });
        host.WaitForAssertion(() => host.FindAll(".tm-nps__item").Should().NotBeEmpty());

        await host.Find(".tm-nps__search-input")
            .KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });

        navigated.Should().HaveCount(1);
    }

    private sealed class CapturingSearchProvider : INotionSearchProvider
    {
        public string LastQuery { get; private set; } = string.Empty;
        public NotionSearchFilter? LastFilter { get; private set; }

        public Task<IEnumerable<INotionPage>> SearchPagesAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult<IEnumerable<INotionPage>>([]);

        public Task<IEnumerable<NotionSearchResult>> SearchBlocksAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult<IEnumerable<NotionSearchResult>>([BuildResult()]);

        public Task<(IEnumerable<INotionPage> Pages, IEnumerable<NotionSearchResult> Blocks)> SearchAllAsync(
            string query,
            NotionSearchFilter? filter,
            int maxResults)
        {
            LastQuery = query;
            LastFilter = filter;
            return Task.FromResult<(IEnumerable<INotionPage>, IEnumerable<NotionSearchResult>)>(
                ([], [BuildResult()]));
        }

        private static NotionSearchResult BuildResult() => new()
        {
            PageId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PageTitle = "CF22 Knowledge Space",
            BlockId = Guid.Parse("cf220000-0000-0000-0000-000000000002"),
            BlockType = BlockType.Paragraph,
            MatchSnippet = "Architecture beacon release notes",
            HighlightRanges = [new NotionSearchHighlightRange(13, 19)]
        };
    }
}
