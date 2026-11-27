using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign plan 146c7e1b, phase 6 (DE-2.2 / DE-2.3, decision DE2): a selected object or an open find replaces the tab bar of
/// the inspector with a contextual header - the title, a back arrow to the tabs and an X. No new <see cref="DocumentSidePanelTab"/>
/// value exists for it (Q2-1 b); the context is a slot of the panel.
/// </summary>
public sealed class TmDocumentSidePanelContextTests : LocalizationTestBase
{
    private IRenderedComponent<TmDocumentSidePanel> RenderPanel(
        bool withContext,
        Action<ComponentParameterCollectionBuilder<TmDocumentSidePanel>>? configure = null)
        => Render<TmDocumentSidePanel>(parameters =>
        {
            parameters
                .Add(p => p.TextTabs, true)
                .Add(p => p.ActiveTab, DocumentSidePanelTab.Properties)
                .Add(p => p.ShowComments, true)
                .Add(p => p.ShowRevisions, true)
                .Add(p => p.PropertiesContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p data-testid='tab-body'>tab body</p>")));
            if (withContext)
            {
                parameters
                    .Add(p => p.ContextKind, "image")
                    .Add(p => p.ContextTitle, "Image tools")
                    .Add(p => p.ContextIcon, "image")
                    .Add(p => p.ContextContent, (RenderFragment)(b => b.AddMarkupContent(0, "<p data-testid='context-body'>context body</p>")));
            }

            configure?.Invoke(parameters);
        });

    [Fact]
    public void WithoutContext_TheTabBarIsRendered_AndNoContextHeader()
    {
        var cut = RenderPanel(withContext: false);

        cut.FindAll("[role='tablist']").Should().HaveCount(1);
        cut.FindAll("[data-testid='document-side-panel-context-header']").Should().BeEmpty();
        cut.Find("[data-testid='document-side-panel']").HasAttribute("data-context").Should().BeFalse();
        cut.Find("[data-testid='tab-body']").Should().NotBeNull();
    }

    [Fact]
    public void WithContext_TheTabBarIsHidden_AndTheContextHeaderTitlesTheBody()
    {
        var cut = RenderPanel(withContext: true);

        cut.FindAll("[role='tablist']").Should().BeEmpty("the contextual content owns the whole inspector");
        cut.FindAll("[data-testid^='document-side-panel-tab-']").Should().BeEmpty();
        cut.Find("[data-testid='document-side-panel']").GetAttribute("data-context").Should().Be("image");

        var header = cut.Find("[data-testid='document-side-panel-context-header']");
        header.QuerySelector("h2")!.TextContent.Should().Be("Image tools");
        cut.Find("[data-testid='context-body']").Should().NotBeNull();
        cut.FindAll("[data-testid='tab-body']").Should().BeEmpty();

        var body = cut.Find("[data-testid='document-side-panel-body']");
        body.GetAttribute("aria-labelledby").Should().Be(header.QuerySelector("h2")!.Id, "the region is named by its title");
    }

    [Fact]
    public void Back_RaisesOnContextBack_AndClose_RaisesOnContextClose()
    {
        var back = 0;
        var close = 0;
        var cut = RenderPanel(withContext: true, parameters => parameters
            .Add(p => p.OnContextBack, () => back++)
            .Add(p => p.OnContextClose, () => close++)
            .Add(p => p.ContextCloseTestId, "document-find-close"));

        cut.Find("[data-testid='document-side-panel-context-back']").Click();
        back.Should().Be(1);
        close.Should().Be(0);

        cut.Find("[data-testid='document-find-close']").Click();
        close.Should().Be(1);
        back.Should().Be(1);
    }

    [Fact]
    public void NoBackArrow_WhenNoBackHandlerIsBound()
    {
        var cut = RenderPanel(withContext: true, parameters => parameters
            .Add(p => p.OnContextClose, () => { }));

        cut.FindAll("[data-testid='document-side-panel-context-back']").Should().BeEmpty();
        cut.Find("[data-testid='document-side-panel-context-close']").Should().NotBeNull();
    }

    [Fact]
    public void ClosedPanel_RendersNeitherTabsNorContext()
    {
        var cut = RenderPanel(withContext: true, parameters => parameters.Add(p => p.IsOpen, false));

        cut.FindAll("[data-testid='document-side-panel']").Should().BeEmpty();
        cut.FindAll("[data-testid='context-body']").Should().BeEmpty();
    }
}
