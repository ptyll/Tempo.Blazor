using System.IO;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3 — E2E coverage for TmNotionEditor SinglePageMode. Verifies the embedded single-page editor on the
/// demo renders as a self-contained surface (no sidebar / navigation chrome) while the full editor above it keeps
/// its sidebar, and captures screenshots for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class NotionSinglePageModeE2ETests : WasmTestBase
{
    private static readonly string StableShotDir = Path.Combine(
        Environment.GetEnvironmentVariable("TM_E2E_SHOT_DIR") ?? Path.GetTempPath(), "kanban-e2e-shots");

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(StableShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(StableShotDir, name + ".png"),
            Type = ScreenshotType.Png,
            FullPage = true
        });
        await TakeScreenshotAsync(page, name);
    }

    private async Task<(IPage page, ILocator singlePageEditor)> OpenSinglePageDemoAsync()
    {
        var page = await CreatePageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);

        var section = page.Locator("[data-testid='notion-single-page-demo']");
        await section.ScrollIntoViewIfNeededAsync();
        // The demo mounts the second editor on demand — a second live TmNotionEditor on the same page
        // shares block ids and the global notion-editor.js handlers with the full editor, which breaks
        // every page-wide locator the rest of the suite relies on.
        await section.Locator("[data-testid='notion-single-page-load']").ClickAsync();
        var editor = section.Locator(".tm-notion-editor").First;
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        // wait for the page content to load inside the single-page editor
        await section.Locator(".tm-notion-page").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });
        return (page, editor);
    }

    [TestMethod]
    [Description("E2E-P3.0: The single-page editor renders with the single-page modifier and no sidebar/navigation chrome")]
    public async Task SinglePage_Editor_Renders_Without_Sidebar_Or_Nav()
    {
        var (page, editor) = await OpenSinglePageDemoAsync();

        var isSinglePage = await editor.EvaluateAsync<bool>("el => el.classList.contains('tm-notion-editor--single-page')");
        Assert.IsTrue(isSinglePage, "Editor should carry the tm-notion-editor--single-page modifier");

        var section = page.Locator("[data-testid='notion-single-page-demo']");
        Assert.AreEqual(0, await section.Locator(".tm-notion-sidebar").CountAsync(), "Single-page editor must not render a sidebar");
        Assert.AreEqual(0, await section.Locator(".tm-notion-sidebar-toggle").CountAsync(), "Single-page editor must not render the sidebar toggle");
        Assert.AreEqual(0, await section.Locator(".tm-notion-topbar__back").CountAsync(), "Single-page editor must not render a back button");

        await ShotAsync(page, "notion_p3_single_page_light");
    }

    [TestMethod]
    [Description("E2E-P3.1 (contrast): The full editor above still renders a sidebar — backward compatibility")]
    public async Task FullEditor_StillHasSidebar_WhileSinglePageDoesNot()
    {
        var (page, _) = await OpenSinglePageDemoAsync();

        // The full interactive editor (not single-page) keeps its sidebar.
        var fullSidebars = page.Locator(".tm-notion-editor:not(.tm-notion-editor--single-page) .tm-notion-sidebar");
        Assert.IsTrue(await fullSidebars.CountAsync() > 0, "The full editor should still render a sidebar");

        // The single-page editor has none.
        var singlePageSidebars = page.Locator(".tm-notion-editor--single-page .tm-notion-sidebar");
        Assert.AreEqual(0, await singlePageSidebars.CountAsync(), "The single-page editor should not render a sidebar");
    }

    [TestMethod]
    [Description("E2E-P3.2: Single-page editor renders correctly in dark mode")]
    public async Task SinglePage_Editor_DarkMode()
    {
        var (page, _) = await OpenSinglePageDemoAsync();

        await page.EvaluateAsync("() => document.documentElement.setAttribute('data-theme','dark')");
        await page.WaitForTimeoutAsync(300);
        await page.Locator("[data-testid='notion-single-page-demo']").ScrollIntoViewIfNeededAsync();

        await ShotAsync(page, "notion_p3_single_page_dark");
    }
}
