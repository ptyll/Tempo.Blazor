using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.DocumentEditor.Models;
using Tempo.Blazor.DocumentEditor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>The desktop left column (phase 3, DE-1.1): it composes the outline and the page navigator and adds no search logic.</summary>
public class TmDocumentNavigationPanelTests : LocalizationTestBase
{
    private static readonly IReadOnlyList<DocumentOutlineItem> Outline =
    [
        new DocumentOutlineItem("h1", 1, "Introduction"),
        new DocumentOutlineItem("h2", 2, "Scope"),
    ];

    [Fact]
    public void Renders_OutlineAndPages_WithTheLocalizedTitle()
    {
        var cut = Render<TmDocumentNavigationPanel>(p => p
            .Add(c => c.Outline, Outline)
            .Add(c => c.Metrics, new WysiwygPageMetrics { TotalPages = 2, RenderedPages = 2, Pages = [new WysiwygPageMetric { PageIndex = 0, PageNumber = 1 }, new WysiwygPageMetric { PageIndex = 1, PageNumber = 2 }] }));

        cut.Find(".tm-document-navigation__title").TextContent.Should().Be("Document structure");
        cut.FindAll("[data-testid='document-outline-item']").Should().HaveCount(2);
        cut.FindAll("[data-testid='document-page-navigator-item']").Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchEntry_RaisesOnSearch_AndOwnsNoSearchState()
    {
        var raised = 0;
        var cut = Render<TmDocumentNavigationPanel>(p => p.Add(c => c.OnSearch, () => { raised++; }));

        var search = cut.Find("[data-testid='document-navigation-search']");
        search.TagName.Should().Be("BUTTON", "it looks like a field but only opens the editor's find panel");
        search.TextContent.Should().Contain("Search in document");
        await cut.InvokeAsync(() => search.Click());

        raised.Should().Be(1);
    }

    [Fact]
    public void Collapse_FoldsIntoARail_AndExpandRestoresTheColumn()
    {
        var collapsed = false;
        var cut = Render<TmDocumentNavigationPanel>(p => p
            .Add(c => c.Outline, Outline)
            .Add(c => c.Collapsed, collapsed)
            .Add(c => c.CollapsedChanged, value => collapsed = value));

        cut.Find("[data-testid='document-navigation-collapse']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[data-testid='document-navigation-collapse']").Click();
        collapsed.Should().BeTrue();

        cut.Render(p => p.Add(c => c.Collapsed, collapsed));
        cut.FindAll("[data-testid='document-outline-panel']").Should().BeEmpty("the folded column keeps only its rail");
        var expand = cut.Find("[data-testid='document-navigation-expand']");
        expand.GetAttribute("aria-expanded").Should().Be("false");
        expand.GetAttribute("aria-label").Should().Be("Show navigation");

        expand.Click();
        collapsed.Should().BeFalse();
    }

    [Fact]
    public void NavigateToBlock_BubblesFromTheOutline()
    {
        string? navigated = null;
        var cut = Render<TmDocumentNavigationPanel>(p => p
            .Add(c => c.Outline, Outline)
            .Add(c => c.OnNavigateToBlock, id => { navigated = id; }));

        cut.FindAll("[data-testid='document-outline-item'] button")[1].Click();

        navigated.Should().Be("h2");
    }
}
