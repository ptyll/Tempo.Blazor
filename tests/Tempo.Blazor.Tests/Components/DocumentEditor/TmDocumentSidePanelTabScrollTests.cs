using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DocumentEditor;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DocumentEditor;

/// <summary>
/// Redesign plan 146c7e1b, phase 6 review: the desktop text tabs sit on one row that scrolls sideways. After every render of the tab bar the
/// active tab is scrolled into the row (so a tab activated from code - the review summary, find - is never hidden past the edge) and the
/// overflow cue is refreshed.
/// </summary>
public sealed class TmDocumentSidePanelTabScrollTests : LocalizationTestBase
{
    private BunitJSModuleInterop SetupModule() => SetupSidePanelTabsModule();

    private IRenderedComponent<TmDocumentSidePanel> RenderPanel(DocumentSidePanelTab active, bool textTabs = true)
        => Render<TmDocumentSidePanel>(parameters => parameters
            .Add(p => p.TextTabs, textTabs)
            .Add(p => p.ActiveTab, active)
            .Add(p => p.ShowComments, true)
            .Add(p => p.ShowRevisions, true)
            .Add(p => p.ShowVersions, true));

    [Fact]
    public void TextTabs_AttachTheScroller_AndSyncAfterRender()
    {
        var module = SetupModule();

        var cut = RenderPanel(DocumentSidePanelTab.Properties);

        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "syncTabScroller").Should().BeGreaterThan(0));
        module.Invocations.Count(i => i.Identifier == "attachTabScroller").Should().Be(1);
        module.Invocations.First(i => i.Identifier == "syncTabScroller").Arguments[0].Should().NotBeNull();
    }

    [Fact]
    public void ActivatingATabFromCode_SyncsAgain_WithoutAttachingASecondTime()
    {
        var module = SetupModule();
        var cut = RenderPanel(DocumentSidePanelTab.Properties);
        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "syncTabScroller").Should().BeGreaterThan(0));
        var syncsBefore = module.Invocations.Count(i => i.Identifier == "syncTabScroller");

        // A programmatic change: the parent passes another ActiveTab (a review-summary button, find, Ctrl+...).
        cut.Render(parameters => parameters.Add(p => p.ActiveTab, DocumentSidePanelTab.Versions));

        cut.WaitForAssertion(() => module.Invocations.Count(i => i.Identifier == "syncTabScroller").Should().BeGreaterThan(syncsBefore));
        cut.Find("[data-testid='document-side-panel-tab-versions']").GetAttribute("aria-selected").Should().Be("true");
        module.Invocations.Count(i => i.Identifier == "attachTabScroller").Should().Be(1);
    }

    [Fact]
    public void IconTabs_DoNotUseTheScroller()
    {
        var module = SetupModule();

        RenderPanel(DocumentSidePanelTab.Comments, textTabs: false);

        module.Invocations.Should().BeEmpty("only the text-tab row scrolls sideways");
    }
}
