using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SmokeTests
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("APP_URL") ?? "http://localhost:5000";

    [TestMethod]
    public async Task DemoApp_Loads_Successfully()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });

        var page = await browser.NewPageAsync();

        // Navigate to the demo app home page
        var response = await page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30_000,
        });

        // Verify the HTTP response is successful
        response.Should().NotBeNull();
        response!.Ok.Should().BeTrue("the demo app should return a successful HTTP status");

        // Verify the page title contains the expected text
        var title = await page.TitleAsync();
        title.Should().Contain("Tempo.Blazor", "the page title should identify the Tempo.Blazor demo app");
    }

    [TestMethod]
    public async Task DemoApp_Renders_HeroHeading()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });

        var page = await browser.NewPageAsync();

        await page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30_000,
        });

        // Wait for Blazor to render the main heading
        var heading = page.Locator("h1");
        await heading.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var headingText = await heading.TextContentAsync();
        headingText.Should().NotBeNullOrWhiteSpace();
        headingText.Should().Contain("Tempo.Blazor", "the hero heading should mention Tempo.Blazor");
    }

    [TestMethod]
    public async Task DemoApp_Sidebar_Shows_Brand()
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });

        var page = await browser.NewPageAsync();

        // Use a desktop viewport so the sidebar is visible
        await page.SetViewportSizeAsync(1280, 720);

        await page.GotoAsync(BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
            Timeout = 30_000,
        });

        // The sidebar brand text "Tempo.Blazor" should be visible on desktop
        var brand = page.Locator("aside >> text=Tempo.Blazor").First;
        await brand.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        var isVisible = await brand.IsVisibleAsync();
        isVisible.Should().BeTrue("the sidebar brand should be visible on desktop viewports");
    }
}
