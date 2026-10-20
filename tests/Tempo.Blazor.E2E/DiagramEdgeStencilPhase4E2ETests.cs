using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for phase 4 relationship edge stencils.</summary>
[TestClass]
[TestCategory("WASM")]
public class DiagramEdgeStencilPhase4E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Relationship edge stencils render as first-class toolbox items with distinct edge previews")]
    public async Task Toolbox_RendersRelationshipEdgeStencils_WithDistinctPreview()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);

        // The docked toolbox exists only in the DESKTOP band (the demo column is tablet at
        // every default viewport, phase 6) — pin the container like a full-bleed host.
        await DiagramEditorE2E.PinDesktopAsync(page);

        var toolbox = page.Locator(".tm-diagram-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var search = page.Locator(".tm-diagram-toolbox__search input");
        await search.FillAsync("Dependency");
        await page.WaitForTimeoutAsync(300);

        var relationshipLibrary = page.Locator("[data-library-id='relationships']");
        await relationshipLibrary.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var dependency = page.Locator("[data-stencil-id='relationships.dependency']");
        await dependency.ScrollIntoViewIfNeededAsync();
        await dependency.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("edge", await dependency.GetAttributeAsync("data-stencil-kind"));
        Assert.IsTrue((await dependency.GetAttributeAsync("class"))?.Contains("tm-diagram-toolbox__item--edge") == true);
        Assert.AreEqual("0", await dependency.GetAttributeAsync("tabindex"));
        Assert.AreEqual("button", await dependency.GetAttributeAsync("role"));

        var edgePreview = dependency.Locator(".tm-diagram-toolbox__edge-preview svg");
        await edgePreview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await dependency.HoverAsync();
        await TakeScreenshotAsync(page, "diagram_edge_stencil_phase4_dependency_toolbox");
    }
}
