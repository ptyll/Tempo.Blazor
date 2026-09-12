using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public sealed class TmNotionLabelEditorTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public TmNotionLabelEditorTests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Loading"] = "Loading",
            ["Tm_Close"] = "Close",
            ["Notion_Labels_Title"] = "Page labels",
            ["Notion_Labels_Add"] = "Add",
            ["Notion_Labels_Placeholder"] = "Add label",
            ["Notion_Labels_None"] = "No labels",
            ["Notion_Labels_Remove"] = "Remove label",
            ["Notion_Labels_Suggestions"] = "Label suggestions",
            ["Notion_Labels_FilterTitle"] = "Pages labeled {0}",
            ["Notion_Labels_FilterEmpty"] = "No pages use this label.",
            ["Notion_Labels_FilterResults"] = "Pages with this label"
        });
    }

    [Fact]
    public void LabelEditor_AddsLabelAndIgnoresDuplicateCaseInsensitiveValue()
    {
        var provider = new LabelDataProvider();
        IReadOnlyList<string>? changed = null;
        var cut = RenderLabelEditor(provider, ["release"], labels => changed = labels);

        cut.Find(".tm-notion-labels__input").Input(" qa ");
        cut.Find(".tm-notion-labels__add").Click();

        cut.WaitForAssertion(() =>
        {
            provider.Labels[PageId].Should().Equal("release", "qa");
            changed.Should().Equal("release", "qa");
            cut.Markup.Should().Contain("qa");
        });

        cut.Find(".tm-notion-labels__input").Input("RELEASE");
        cut.Find(".tm-notion-labels__add").Click();

        cut.WaitForAssertion(() =>
            provider.Labels[PageId].Should().Equal("release", "qa"));
    }

    [Fact]
    public void LabelEditor_ShowsAutocompleteAndFiltersPagesByClickedLabel()
    {
        var provider = new LabelDataProvider();
        string? navigatedPageId = null;
        var cut = RenderLabelEditor(
            provider,
            ["release"],
            _ => { },
            pageId =>
            {
                navigatedPageId = pageId;
                return Task.CompletedTask;
            });

        cut.Find(".tm-notion-labels__input").Input("cust");

        cut.WaitForAssertion(() =>
            cut.Find(".tm-notion-labels__suggestion").TextContent.Should().Be("customer success"));

        cut.Find(".tm-notion-labels__chip-filter").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-labels__filter").TextContent.Should().Contain("Release Plan");
            cut.Find(".tm-notion-labels__filter").TextContent.Should().Contain("Release Companion");
        });

        cut.FindAll(".tm-notion-labels__filter-page")[0].Click();
        navigatedPageId.Should().Be("22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public void LabelEditor_EnterOnSuggestion_AddsOnlyTheSuggestion()
    {
        // The suggestion is a native <button>: the browser turns Enter into a
        // click on keydown. A section-level Enter->add case would ALSO add the
        // typed input text, so one gesture added two labels.
        var provider = new LabelDataProvider();
        var changed = new List<IReadOnlyList<string>>();
        var cut = RenderLabelEditor(provider, ["release"], labels => changed.Add(labels));

        cut.Find(".tm-notion-labels__input").Input("cust");
        cut.WaitForAssertion(() =>
            cut.Find(".tm-notion-labels__suggestion").TextContent.Should().Be("customer success"));

        // Real sequence for Enter on a focused <button>: keydown -> click.
        var suggestion = cut.Find(".tm-notion-labels__suggestion");
        suggestion.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-notion-labels__suggestion").Click();

        cut.WaitForAssertion(() =>
            provider.Labels[PageId].Should().Equal("release", "customer success"));
        changed.Should().NotBeEmpty();
        changed.Last().Should().Equal("release", "customer success");
    }

    [Fact]
    public void LabelEditor_EnterInInput_StillAddsTypedLabel()
    {
        var provider = new LabelDataProvider();
        var changed = new List<IReadOnlyList<string>>();
        var cut = RenderLabelEditor(provider, ["release"], labels => changed.Add(labels));

        var input = cut.Find(".tm-notion-labels__input");
        input.Input("qa");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() =>
            provider.Labels[PageId].Should().Equal("release", "qa"));
    }

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderLabelEditor(
        LabelDataProvider provider,
        IReadOnlyList<string> labels,
        Action<IReadOnlyList<string>> changed,
        Func<string, Task>? navigateTo = null)
    {
        var context = new NotionEditorContext
        {
            DataProvider = provider,
            NavigateTo = navigateTo
        };

        return Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionLabelEditor>(child => child
                .Add(component => component.PageId, PageId)
                .Add(component => component.Labels, labels)
                .Add(component => component.OnLabelsChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, changed))));
    }

    private sealed class LabelDataProvider : INotionDataProvider
    {
        private readonly Dictionary<Guid, NotionPage> _pages = new()
        {
            [PageId] = new NotionPage
            {
                Id = PageId,
                Title = "Release Plan",
                IconEmoji = "R",
                Labels = ["release"]
            },
            [Guid.Parse("22222222-2222-2222-2222-222222222222")] = new NotionPage
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Title = "Release Companion",
                IconEmoji = "C",
                Labels = ["release", "customer success"]
            }
        };

        public Dictionary<Guid, IReadOnlyList<string>> Labels { get; } = new()
        {
            [PageId] = ["release"]
        };

        public Task<INotionPage> GetPageAsync(string pageId)
            => Task.FromResult<INotionPage>(_pages[Guid.Parse(pageId)]);

        public Task<IEnumerable<INotionPage>> GetChildPagesAsync(string? parentId)
            => Task.FromResult(_pages.Values.Cast<INotionPage>());

        public Task<IEnumerable<INotionPage>> GetFavoritesAsync()
            => Task.FromResult(_pages.Values.Where(page => page.IsFavorite).Cast<INotionPage>());

        public Task<IEnumerable<INotionPage>> GetRecentPagesAsync(int count)
            => Task.FromResult(_pages.Values.Take(count).Cast<INotionPage>());

        public Task<IEnumerable<INotionPage>> GetTrashAsync()
            => Task.FromResult(_pages.Values.Where(page => page.IsDeleted).Cast<INotionPage>());

        public Task<INotionPage> CreatePageAsync(string? parentId, string title)
            => throw new NotSupportedException();

        public Task UpdatePageAsync(INotionPage page)
            => throw new NotSupportedException();

        public Task DeletePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task RestorePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task PermanentlyDeletePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task ToggleFavoriteAsync(string pageId, bool isFavorite)
            => throw new NotSupportedException();

        public Task MovePageAsync(string pageId, string? newParentId)
            => throw new NotSupportedException();

        public Task<INotionPage> DuplicatePageAsync(string pageId)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<INotionPage>> GetPagesByLabelAsync(string label, CancellationToken cancellationToken = default)
        {
            var pages = _pages.Values
                .Where(page => page.Labels.Any(existing => string.Equals(existing, label, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(page => page.Title, StringComparer.OrdinalIgnoreCase)
                .Cast<INotionPage>()
                .ToArray();

            return Task.FromResult<IReadOnlyList<INotionPage>>(pages);
        }

        public Task<IReadOnlyList<string>> GetAllLabelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(["customer success", "documentation", "release"]);

        public Task SetPageLabelsAsync(Guid pageId, IReadOnlyList<string> labels, CancellationToken cancellationToken = default)
        {
            Labels[pageId] = labels.ToArray();
            _pages[pageId].Labels = labels.ToArray();
            return Task.CompletedTask;
        }
    }
}
