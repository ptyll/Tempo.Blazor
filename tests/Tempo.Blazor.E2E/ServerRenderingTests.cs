using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Smoke tests for server rendering mode - verifies basic components render correctly.
/// </summary>
[TestClass]
public class ServerRenderingTests : ServerTestBase
{
    [TestMethod]
    [Description("Verify TmButton renders correctly")]
    public async Task TmButton_Renders()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Buttons");

        // Verify button elements are present
        var buttons = page.Locator(".tm-btn");
        var firstButton = buttons.First;
        await firstButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var count = await buttons.CountAsync();
        Assert.IsTrue(count > 0, "Expected at least one button to be rendered");

        await TakeScreenshotAsync(page, "buttons_page");
    }

    [TestMethod]
    [Description("Verify TmCard renders correctly")]
    public async Task TmCard_Renders()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Data Display");

        // Verify card elements are present
        var cards = page.Locator(".tm-card");
        await cards.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await TakeScreenshotAsync(page, "cards_page");
    }

    [TestMethod]
    [Description("Verify TmBadge renders correctly")]
    public async Task TmBadge_Renders()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Data Display");

        // Verify badge elements are present
        var badges = page.Locator(".tm-badge");
        var count = await badges.CountAsync();
        Assert.IsTrue(count > 0, "Expected at least one badge to be rendered");

        await TakeScreenshotAsync(page, "badges_page");
    }

    [TestMethod]
    [Description("Verify TmAlert renders correctly")]
    public async Task TmAlert_Renders()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Feedback");

        // Verify alert elements are present
        var alerts = page.Locator(".tm-alert");
        await alerts.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await TakeScreenshotAsync(page, "alerts_page");
    }

    [TestMethod]
    [Description("Verify form inputs render correctly")]
    public async Task FormInputs_Renders()
    {
        var page = await CreatePageAsync();
        await NavigateToPageAsync(page, "Forms");

        // Verify text input
        var textInputs = page.Locator(".tm-input");
        await textInputs.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Verify checkbox
        var checkboxes = page.Locator(".tm-checkbox");
        var checkboxCount = await checkboxes.CountAsync();
        Assert.IsTrue(checkboxCount > 0, "Expected at least one checkbox");

        // Verify select
        var selects = page.Locator(".tm-select");
        var selectCount = await selects.CountAsync();
        Assert.IsTrue(selectCount > 0, "Expected at least one select");

        await TakeScreenshotAsync(page, "forms_page");
    }

    [TestMethod]
    [Description("Verify dark mode toggle works")]
    public async Task DarkMode_Toggle_Works()
    {
        var page = await CreatePageAsync();

        // The ThemeService-driven dark flag lands on the layout root element via data-theme
        // (MainLayout renders class="… dark …" + data-theme="dark"), not on <body>.
        var initialTheme = await ThemeAttributeAsync(page);

        // Toggle dark mode
        await ToggleDarkModeAsync(page);

        // Verify theme changed
        var themeAfterToggle = await ThemeAttributeAsync(page);
        Assert.AreNotEqual(initialTheme, themeAfterToggle, "Dark mode should have toggled");

        // Toggle back
        await ToggleDarkModeAsync(page);
        var themeAfterSecondToggle = await ThemeAttributeAsync(page);
        Assert.AreEqual(initialTheme, themeAfterSecondToggle, "Dark mode should have toggled back");

        await TakeScreenshotAsync(page, "dark_mode_test");
    }

    private static Task<string?> ThemeAttributeAsync(IPage page)
        => page.EvaluateAsync<string?>(
            "() => document.querySelector('[data-theme]')?.getAttribute('data-theme') ?? null");

    [TestMethod]
    [Description("Verify localization switch works - Czech language")]
    public async Task Localization_SwitchToCzech_Works()
    {
        var page = await CreatePageAsync();

        // Switch to Czech
        await SwitchLanguageAsync(page, "cs");

        // Verify the page contains Czech text (common words)
        var pageContent = await page.ContentAsync();
        Assert.IsTrue(
            pageContent.Contains("Tlačítka") ||
            pageContent.Contains("Komponenty") ||
            pageContent.Contains("Nastavení"),
            "Page should contain Czech text after switching language"
        );

        await TakeScreenshotAsync(page, "localization_czech");
    }

    [TestMethod]
    [Description("Verify page navigation works without errors")]
    public async Task PageNavigation_Works()
    {
        var page = await CreatePageAsync();

        // Navigate through multiple pages
        var pages = new[] { "Buttons", "Forms", "Data Display", "Feedback", "Pickers" };

        foreach (var pageName in pages)
        {
            await NavigateToPageAsync(page, pageName);

            // Verify no error UI is shown
            var errorUi = page.Locator("#blazor-error-ui");
            var isVisible = await errorUi.IsVisibleAsync();
            Assert.IsFalse(isVisible, $"Error UI should not be visible on {pageName} page");

            // Verify main content is present
            var main = page.Locator("main");
            await main.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }

        await TakeScreenshotAsync(page, "navigation_test");
    }
}
