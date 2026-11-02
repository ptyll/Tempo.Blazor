using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 2: internal-comment wash and consumer badges on the /activity standalone thread.
/// The demo seeds an internal+edited comment (demo-c1, one badge) and a public comment with a
/// long badge plus one badge of every tone (demo-c2). Colour is not the only internal cue —
/// IconNames.Lock and the localized "Internal" label must be present in both themes.
/// Committed captures land in <c>__screenshots__/activity-comments/fze-2/</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class ActivityCommentsVisualE2ETests : WasmTestBase
{
    private const string SectionSelector = "[data-testid='activity-comments-section']";
    private const string ThemeToggleSelector = "[data-testid='theme-toggle']";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "activity-comments", "fze-2");

    private async Task<IPage> OpenActivityAsync(int width = 1440, int height = 900, string? culture = null)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = culture == "cs" ? "cs-CZ" : "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        if (culture is not null)
        {
            // The WASM host applies the culture stored here before the app renders.
            await context.AddInitScriptAsync($"window.localStorage.setItem('tm-demo-culture','{culture}');");
        }

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/activity",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id='demo-c1']"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        return page;
    }

    private async Task CaptureSectionAsync(IPage page, string name)
    {
        // Park the pointer so a sticky header or a leftover tooltip is not in the shot.
        await page.Mouse.MoveAsync(0, 0);
        await WaitForLayoutToSettleAsync(page, ".tm-comments");
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, $"{name}.png");
        await page.Locator(".tm-comments").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Timeout = 60_000
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
        TestContext.AddResultFile(path);
    }

    private static async Task AssertVisualContractAsync(IPage page, string internalLabel, string longBadge)
    {
        var internalItem = page.Locator($"{SectionSelector} [data-comment-id='demo-c1']");
        await Assertions.Expect(internalItem).ToHaveAttributeAsync("data-internal", "true");
        await Assertions.Expect(internalItem).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("tm-comment-item--internal"));
        await Assertions.Expect(internalItem.Locator(".tm-comment-internal-label")).ToBeVisibleAsync();
        await Assertions.Expect(internalItem.Locator(".tm-comment-internal-label")).ToContainTextAsync(internalLabel);
        await Assertions.Expect(internalItem.Locator(".tm-comment-internal-label .tm-icon")).ToBeVisibleAsync();
        await Assertions.Expect(internalItem.Locator(".tm-comment-edited")).ToBeVisibleAsync();
        // One consumer badge, and it is not the internal lock (different tone and icon).
        await Assertions.Expect(internalItem.Locator(".tm-comment-badge")).ToHaveCountAsync(1);
        await Assertions.Expect(internalItem.Locator("[data-badge-tone='info']")).ToBeVisibleAsync();

        var publicItem = page.Locator($"{SectionSelector} [data-comment-id='demo-c2']");
        await Assertions.Expect(publicItem).ToHaveAttributeAsync("data-internal", "false");
        await Assertions.Expect(publicItem.Locator(".tm-comment-internal-label")).ToHaveCountAsync(0);
        await Assertions.Expect(publicItem.Locator(".tm-comment-badge")).ToHaveCountAsync(6);
        await Assertions.Expect(publicItem.Locator("[data-badge-tone='info']")).ToContainTextAsync(longBadge);
        foreach (var tone in new[] { "info", "success", "accent", "neutral", "warning", "danger" })
        {
            await Assertions.Expect(publicItem.Locator($"[data-badge-tone='{tone}']")).ToHaveCountAsync(1);
        }

        var overflow = await publicItem.Locator(".tm-comment-badge").First.EvaluateAsync<bool>(
            "el => el.scrollWidth > el.clientWidth + 1");
        Assert.IsFalse(overflow, "a long badge must wrap inside the meta row, not overflow it");
    }

    [TestMethod]
    public async Task NarrowViewport_WrapsLongBadge_WithoutOverflow()
    {
        var page = await OpenActivityAsync(width: 390, height: 844);
        await AssertVisualContractAsync(page, "Internal", "Waiting on the external vendor");
        await Assertions.Expect(page.Locator($"{SectionSelector} [data-comment-id='demo-c3'] .tm-comment-badge"))
            .ToHaveCountAsync(0);
        await CaptureSectionAsync(page, "one-badge-many-badges-long-badge-internal-edited-light-390");

        await page.Locator($"{ThemeToggleSelector}:visible").First.ClickAsync();
        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await AssertVisualContractAsync(page, "Internal", "Waiting on the external vendor");
        await CaptureSectionAsync(page, "one-badge-many-badges-long-badge-internal-edited-dark-390");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task InternalComment_AndBadges_AreVisibleInLightAndDark()
    {
        var page = await OpenActivityAsync();

        await AssertVisualContractAsync(page, "Internal", "Waiting on the external vendor");
        await CaptureSectionAsync(page, "one-badge-many-badges-long-badge-internal-edited-light");

        await page.Locator($"{ThemeToggleSelector}:visible").First.ClickAsync();
        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });

        await AssertVisualContractAsync(page, "Internal", "Waiting on the external vendor");
        await CaptureSectionAsync(page, "one-badge-many-badges-long-badge-internal-edited-dark");
    }

    [TestMethod]
    public async Task Czech_InternalLabel_IsLocalized()
    {
        var page = await OpenActivityAsync(culture: "cs");
        await AssertVisualContractAsync(page, "Interní", "Waiting on the external vendor");
        await CaptureSectionAsync(page, "one-badge-many-badges-long-badge-internal-edited-czech");
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}
