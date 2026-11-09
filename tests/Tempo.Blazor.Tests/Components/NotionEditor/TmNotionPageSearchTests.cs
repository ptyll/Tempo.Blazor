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
        host.WaitForAssertion(() => host.Find(".tm-nps-host").Should().NotBeNull());

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
        host.WaitForAssertion(() => host.Find(".tm-nps-host").Should().NotBeNull());

        await host.Find(".tm-nps__search-input").InputAsync(new ChangeEventArgs { Value = "beacon" });
        host.WaitForAssertion(() => host.FindAll(".tm-nps__item").Should().NotBeEmpty());

        // Real sequence for Enter on a focused <button>: keydown -> click.
        var toggle = host.Find(".tm-nps__filter-toggle");
        await toggle.KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await host.Find(".tm-nps__filter-toggle").ClickAsync(new MouseEventArgs());

        navigated.Should().BeEmpty("Enter on the filter toggle is not a result selection");
        host.Find(".tm-nps__filters").Should().NotBeNull();
        host.Find(".tm-nps-host").Should().NotBeNull("the panel must stay open");
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

    [Fact]
    public async Task Filters_AreLabelled_TheToggleCounts_AndAPageResultCarriesItsPath()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmNotionPageSearch_ResultCount"] = "Results: {0}",
            ["TmNotionPageSearch_FilterLabel"] = "Filter"
        });
        var provider = new PathSearchProvider();
        var context = new NotionEditorContext
        {
            DataProvider = new PathDataProvider(),
            SearchProvider = provider
        };
        context.RememberPages([new NotionPage
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Parent"
        }]);

        var host = Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionPageSearch>());
        await host.FindComponent<TmNotionPageSearch>().InvokeAsync(() =>
            host.FindComponent<TmNotionPageSearch>().Instance.OpenPageSearch());

        var toggle = host.Find(".tm-nps__filter-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");

        await toggle.ClickAsync(new MouseEventArgs());

        toggle = host.Find(".tm-nps__filter-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true");

        await host.Find("[data-testid='notion-search-filter-author']")
            .ChangeAsync(new ChangeEventArgs { Value = "Ada" });
        host.Find(".tm-nps__filter-toggle").TextContent.Should().Contain("1");

        var author = host.Find("[data-testid='notion-search-filter-author']");
        host.Find($"label[for='{author.Id}']").Should().NotBeNull();

        await host.Find(".tm-nps__search-input").InputAsync(new ChangeEventArgs { Value = "child" });
        host.WaitForAssertion(() => host.FindAll(".tm-nps__item").Should().NotBeEmpty());

        var input = host.Find(".tm-nps__search-input");
        input.GetAttribute("aria-activedescendant").Should().NotBeNullOrEmpty();
        host.Find(".tm-nps__item-path").TextContent.Should().Contain("Parent");
        host.Find(".tm-nps__item-path").TextContent.Should().Contain("Child");
        host.Find(".tm-nps__footer").TextContent.Should().Contain("Results: 1");
        host.Markup.Should().NotContain("LastEditedByUserId");
        host.Find(".tm-nps__section-header").TextContent.Should().Contain("(1)");
    }

    private sealed class PathSearchProvider : INotionSearchProvider
    {
        private static readonly Guid ChildId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        public Task<IEnumerable<INotionPage>> SearchPagesAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult<IEnumerable<INotionPage>>([Page()]);

        public Task<IEnumerable<NotionSearchResult>> SearchBlocksAsync(string query, NotionSearchFilter? filter)
            => Task.FromResult<IEnumerable<NotionSearchResult>>([]);

        public Task<(IEnumerable<INotionPage> Pages, IEnumerable<NotionSearchResult> Blocks)> SearchAllAsync(
            string query, NotionSearchFilter? filter, int maxResults)
            => Task.FromResult<(IEnumerable<INotionPage>, IEnumerable<NotionSearchResult>)>(([Page()], []));

        private static INotionPage Page() => new NotionPage
        {
            Id = ChildId,
            ParentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Title = "Child",
            LastEditedAt = new DateTime(2026, 3, 1),
            LastEditedByUserId = "Ada"
        };
    }

    private sealed class PathDataProvider : INotionDataProvider
    {
        public Task<INotionPage> GetPageAsync(string pageId) => Task.FromResult<INotionPage>(new NotionPage
        {
            Id = Guid.Parse(pageId),
            Title = pageId.StartsWith("1111", StringComparison.Ordinal) ? "Parent" : "Child"
        });

        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetFavoritesAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count) => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IEnumerable<INotionPage>> GetTrashAsync() => Task.FromResult<IEnumerable<INotionPage>>([]);
        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<INotionPage>>([]);
        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<INotionPage> CreatePageAsync(string? parentId, string title) => throw new NotSupportedException();
        public Task UpdatePageAsync(INotionPage page) => Task.CompletedTask;
        public Task DeletePageAsync(string pageId) => Task.CompletedTask;
        public Task RestorePageAsync(string pageId) => Task.CompletedTask;
        public Task PermanentlyDeletePageAsync(string pageId) => Task.CompletedTask;
        public Task ToggleFavoriteAsync(string pageId, bool isFavorite) => Task.CompletedTask;
        public Task MovePageAsync(string pageId, string? newParentId) => Task.CompletedTask;
        public Task<INotionPage> DuplicatePageAsync(string pageId) => throw new NotSupportedException();
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
