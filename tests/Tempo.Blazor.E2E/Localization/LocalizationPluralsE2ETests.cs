using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Localization;

/// <summary>
/// c88f6e52 + 4c92ba05 #4: the /localization demo page shows grammatical plurals. For each
/// (culture, count) the TmModelingModelTree_Count cell must read the CLDR form of that culture
/// ("1 element", "2 prvky", "1 élément" — French uses the one form for 0 and 1), the plural
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
        // The demo's default culture follows the browser — pin it (fr/sk are seeded through
        // localStorage/cookie; the visible switcher offers en/cs/fr/sk).
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
    [DataRow("en", 0, "Other", "0 elements")]
    [DataRow("en", 1, "One", "1 element")]
    [DataRow("en", 2, "Other", "2 elements")]
    [DataRow("en", 5, "Other", "5 elements")]
    [DataRow("en", 21, "Other", "21 elements")]
    [DataRow("en", 22, "Other", "22 elements")]
    [DataRow("en", 25, "Other", "25 elements")]
    [DataRow("cs", 0, "Other", "0 prvků")]
    [DataRow("cs", 1, "One", "1 prvek")]
    [DataRow("cs", 2, "Few", "2 prvky")]
    [DataRow("cs", 5, "Other", "5 prvků")]
    [DataRow("cs", 21, "Other", "21 prvků")]
    [DataRow("cs", 22, "Other", "22 prvků")]
    [DataRow("cs", 25, "Other", "25 prvků")]
    [DataRow("fr", 0, "One", "0 élément")]
    [DataRow("fr", 1, "One", "1 élément")]
    [DataRow("fr", 2, "Other", "2 éléments")]
    [DataRow("fr", 5, "Other", "5 éléments")]
    [DataRow("fr", 21, "Other", "21 éléments")]
    [DataRow("fr", 22, "Other", "22 éléments")]
    [DataRow("fr", 25, "Other", "25 éléments")]
    [DataRow("sk", 0, "Other", "0 prvkov")]
    [DataRow("sk", 1, "One", "1 prvok")]
    [DataRow("sk", 2, "Few", "2 prvky")]
    [DataRow("sk", 5, "Other", "5 prvkov")]
    [DataRow("sk", 21, "Other", "21 prvkov")]
    [DataRow("sk", 22, "Other", "22 prvkov")]
    [DataRow("sk", 25, "Other", "25 prvkov")]
    public async Task PluralCell_AgreesWithTheCultureAndCount(string culture, int count, string category, string expected)
    {
        var page = await OpenAsync(culture, 1440);

        await page.GetByTestId($"plural-preset-{count}").ClickAsync();

        await Assertions.Expect(page.GetByTestId("plural-TmModelingModelTree_Count"))
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
        await Assertions.Expect(page.GetByTestId("plural-TmModelingModelTree_Count"))
            .ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^1\s000\s000 d'éléments$"),
                new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        await Assertions.Expect(page.GetByTestId("plural-category"))
            .ToHaveTextAsync("Many", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });
        // The relative-time row must still render (no unhandled-error UI from a pre-year-1 date).
        await Assertions.Expect(page.GetByTestId("plural-relative-time")).Not.ToBeEmptyAsync();
        await Assertions.Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
    }

    [TestMethod]
    // 22 is "Other" in Czech and Slovak — the tempting "Few" reading is wrong (2-4 only, 22 is 5+).
    [DataRow("cs", 2, "2 nová oznámení")]
    [DataRow("cs", 22, "22 nových oznámení")]
    [DataRow("sk", 2, "2 nové oznámenia")]
    [DataRow("sk", 22, "22 nových oznámení")]
    public async Task NotificationBellCountCell_AgreesInCzechAndSlovak(string culture, int count, string expected)
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
        await Assertions.Expect(page.GetByTestId("plural-TmModelingModelTree_Count"))
            .ToHaveTextAsync("5 prvků", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        TestContext.WriteLine($"desktop screenshot: {await ShootAsync(page, "1440-localization-plurals")}");
    }

    [TestMethod]
    public async Task Screenshot_Mobile_ShowsTheStepperAndFamilies()
    {
        var page = await OpenAsync("cs", 390, 844);

        await page.GetByTestId("plural-preset-2").ClickAsync();
        await Assertions.Expect(page.GetByTestId("plural-TmModelingModelTree_Count"))
            .ToHaveTextAsync("2 prvky", new LocatorAssertionsToHaveTextOptions { Timeout = 10000 });

        TestContext.WriteLine($"mobile screenshot: {await ShootAsync(page, "390-localization-plurals")}");
    }
}
