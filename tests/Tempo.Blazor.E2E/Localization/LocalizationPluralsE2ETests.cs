using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Localization;

/// <summary>
/// c88f6e52 + 4c92ba05 #4: the /localization demo page shows grammatical plurals. For each
/// (culture, count) the TmModelingIssuePanel_Count cell must read the CLDR form of that culture
/// ("1 issue", "2 nálezy", "1 problème" — French uses the one form for 0 and 1), the plural
/// category label must match, and the TmNotificationBell_NewCount cell must agree in Czech.
/// Screenshots land in TestResults/localization-plurals for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class LocalizationPluralsE2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "localization-plurals");

    private async Task<IPage> OpenAsync(string culture, int width, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
            Locale = culture + "-US",
        });
        // The demo's default culture follows the browser — pin it (fr is seeded through
        // localStorage/cookie; the visible switcher now offers en/cs/fr).
        await context.AddInitScriptAsync(
            $"""
            localStorage.setItem('tm-demo-culture', '{culture}');
            document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';
            """);
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/localization", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        return path;
    }

    [TestMethod]
    [DataRow("en", 0, "Other", "0 issues")]
    [DataRow("en", 1, "One", "1 issue")]
    [DataRow("en", 2, "Other", "2 issues")]
    [DataRow("en", 5, "Other", "5 issues")]
    [DataRow("en", 21, "Other", "21 issues")]
    [DataRow("en", 22, "Other", "22 issues")]
    [DataRow("en", 25, "Other", "25 issues")]
    [DataRow("cs", 0, "Other", "0 nálezů")]
    [DataRow("cs", 1, "One", "1 nález")]
    [DataRow("cs", 2, "Few", "2 nálezy")]
    [DataRow("cs", 5, "Other", "5 nálezů")]
    [DataRow("cs", 21, "Other", "21 nálezů")]
    [DataRow("cs", 22, "Other", "22 nálezů")]
    [DataRow("cs", 25, "Other", "25 nálezů")]
    [DataRow("fr", 0, "One", "0 problème")]
    [DataRow("fr", 1, "One", "1 problème")]
    [DataRow("fr", 2, "Other", "2 problèmes")]
    [DataRow("fr", 5, "Other", "5 problèmes")]
    [DataRow("fr", 21, "Other", "21 problèmes")]
    [DataRow("fr", 22, "Other", "22 problèmes")]
    [DataRow("fr", 25, "Other", "25 problèmes")]
    public async Task PluralCell_AgreesWithTheCultureAndCount(string culture, int count, string category, string expected)
    {
        var page = await OpenAsync(culture, 1440);

        await page.GetByTestId($"plural-preset-{count}").ClickAsync();

        await Assertions.Expect(page.GetByTestId("plural-TmModelingIssuePanel_Count"))
            .ToHaveTextAsync(expected, new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByTestId("plural-category"))
            .ToHaveTextAsync(category, new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
    }

    [TestMethod]
    public async Task MillionPreset_French_RendersTheManyForm_WithoutAnError()
    {
        var page = await OpenAsync("fr", 1440);

        await page.GetByTestId("plural-preset-1000000").ClickAsync();

        // French "many" with {0:N0}: the group separator is U+202F or U+00A0, so match loosely.
        await Assertions.Expect(page.GetByTestId("plural-TmModelingIssuePanel_Count"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^1\s000\s000 de problèmes$"),
                new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByTestId("plural-category"))
            .ToHaveTextAsync("Many", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        // The relative-time row must still render (no unhandled-error UI from a pre-year-1 date).
        await Assertions.Expect(page.GetByTestId("plural-relative-time")).Not.ToBeEmptyAsync();
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [TestMethod]
    // 22 is "Other" in Czech — the tempting "Few" reading is wrong (2-4 only, 22 is 5+).
    [DataRow("cs", 2, "2 nová oznámení")]
    [DataRow("cs", 22, "22 nových oznámení")]
    public async Task NotificationBellCountCell_AgreesInCzech(string culture, int count, string expected)
    {
        var page = await OpenAsync(culture, 1440);

        await page.GetByTestId($"plural-preset-{count}").ClickAsync();

        await Assertions.Expect(page.GetByTestId("plural-TmNotificationBell_NewCount"))
            .ToHaveTextAsync(expected, new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
    }

    [TestMethod]
    public async Task Screenshot_Desktop_ShowsTheStepperAndFamilies()
    {
        var page = await OpenAsync("cs", 1440);

        await page.GetByTestId("plural-preset-5").ClickAsync();
        await Assertions.Expect(page.GetByTestId("plural-TmModelingIssuePanel_Count"))
            .ToHaveTextAsync("5 nálezů", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        TestContext.WriteLine($"desktop screenshot: {await ShootAsync(page, "1440-localization-plurals")}");
    }

    [TestMethod]
    public async Task Screenshot_Mobile_ShowsTheStepperAndFamilies()
    {
        var page = await OpenAsync("cs", 390, 844);

        await page.GetByTestId("plural-preset-2").ClickAsync();
        await Assertions.Expect(page.GetByTestId("plural-TmModelingIssuePanel_Count"))
            .ToHaveTextAsync("2 nálezy", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        TestContext.WriteLine($"mobile screenshot: {await ShootAsync(page, "390-localization-plurals")}");
    }
}
