using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E smoke and screenshot checks for the phase 3 diagram toolbox UX.</summary>
[TestClass]
[TestCategory("WASM")]
public class DiagramToolboxPhase3E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Diagram toolbox renders grouped palettes, supports search, and captures the phase 3 UX screenshot")]
    public async Task Toolbox_RendersGroupedPalettes_SearchesAndScreenshots()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);

        // The docked toolbox exists only in the DESKTOP band: the demo column sits in the
        // tablet band at every default viewport (920px at 1280), where the palette rails
        // (phase 6). Pin the editor container to desktop, exactly like a full-bleed host.
        await DiagramEditorE2E.PinDesktopAsync(page);

        var toolbox = page.Locator(".tm-diagram-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var paletteHeaders = page.Locator(".tm-diagram-toolbox__category-header");
        Assert.IsTrue(await paletteHeaders.CountAsync() > 0, "The toolbox should render at least one palette header.");

        var firstItem = page.Locator(".tm-diagram-toolbox__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("0", await firstItem.GetAttributeAsync("tabindex"), "Stencil items should be keyboard focusable.");
        Assert.AreEqual("button", await firstItem.GetAttributeAsync("role"), "Stencil items should expose button semantics.");

        await TakeScreenshotAsync(page, "diagram_toolbox_phase3_grouped_palettes");

        var search = page.Locator(".tm-diagram-toolbox__search input");
        await search.FillAsync("Cloud");
        await page.WaitForTimeoutAsync(300);

        var cloudItems = page.Locator(".tm-diagram-toolbox__item").Filter(new LocatorFilterOptions
        {
            HasTextRegex = new Regex("Cloud", RegexOptions.IgnoreCase)
        });
        Assert.IsTrue(await cloudItems.CountAsync() >= 1, "Toolbox search should keep matching stencil items visible.");

        await TakeScreenshotAsync(page, "diagram_toolbox_phase3_search_cloud");
    }
}
