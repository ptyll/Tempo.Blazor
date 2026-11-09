using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public sealed class NotionReadingModeE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF19: Reading and presentation modes hide editor chrome, toggle back, exit with Escape, work on mobile, and capture UX baselines.")]
    public async Task ReadingAndPresentationModes_ToggleEscapeMobileAndBaseline()
    {
        var page = await OpenNotionEditorAsync();
        await SeedRichPageAsync();

        await ClickNotionCommandAsync(page, "notion-reading-toggle");
        await WaitForViewModeAsync(page, "Reading");
        await AssertCleanReadSurfaceAsync(page, "desktop reading");
        await AssertNoDuplicatedReadOnlyTextAsync(page, "desktop reading");
        await AssertReadableLineWidthAsync(page);

        var readingCapture = await CaptureBaselineAsync("reading-mode", "cf19-reading-mode-baseline", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine($"UX CF19 reading baseline captured: {readingCapture.FullPagePath} / {readingCapture.RegionPath}");

        await page.Locator(".tm-notion-reading-exit").ClickAsync();
        await WaitForViewModeAsync(page, "Normal");
        Assert.AreEqual(0, await page.Locator(".tm-notion-page--readonly").CountAsync(),
            "Exiting reading mode should restore the editable page surface.");

        await ClickNotionCommandAsync(page, "notion-presentation-toggle");
        await WaitForViewModeAsync(page, "Presentation");
        await AssertCleanReadSurfaceAsync(page, "presentation");
        await AssertNoDuplicatedReadOnlyTextAsync(page, "presentation");
        Assert.IsTrue(await page.Locator(".tm-notion-editor").EvaluateAsync<bool>("""
            el => {
                const rect = el.getBoundingClientRect();
                return rect.width >= window.innerWidth - 2 && rect.height >= window.innerHeight - 2;
            }
            """), "Presentation mode should fill the viewport.");

        var presentationCapture = await CaptureBaselineAsync("reading-mode", "cf19-presentation-mode-baseline", page.Locator(".tm-notion-editor").First);
        TestContext.WriteLine($"UX CF19 presentation baseline captured: {presentationCapture.FullPagePath} / {presentationCapture.RegionPath}");

        await page.Keyboard.PressAsync("Escape");
        await WaitForViewModeAsync(page, "Normal");

        await page.SetViewportSizeAsync(390, 844);
        await ClickNotionCommandAsync(page, "notion-reading-toggle");
        await WaitForViewModeAsync(page, "Reading");
        await AssertCleanReadSurfaceAsync(page, "mobile reading");
        await AssertNoDuplicatedReadOnlyTextAsync(page, "mobile reading");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
            "Mobile reading mode should not overflow horizontally.");

        var mobileCapture = await CaptureBaselineAsync("reading-mode", "cf19-reading-mode-mobile-baseline", page.Locator(".tm-notion-page").First);
        TestContext.WriteLine($"UX CF19 mobile reading baseline captured: {mobileCapture.FullPagePath} / {mobileCapture.RegionPath}");

        await page.Keyboard.PressAsync("Escape");
        await WaitForViewModeAsync(page, "Normal");
    }

    private static async Task WaitForViewModeAsync(IPage page, string mode)
    {
        await page.Locator($".tm-notion-editor[data-view-mode='{mode}']").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
    }

    private static async Task AssertCleanReadSurfaceAsync(IPage page, string label)
    {
        Assert.AreEqual(0, await page.Locator(".tm-notion-sidebar:visible").CountAsync(),
            $"{label}: sidebar should be hidden.");
        Assert.AreEqual(1, await page.Locator(".tm-notion-page--readonly").CountAsync(),
            $"{label}: page should be rendered read-only.");

        var forbiddenVisibleControls = new[]
        {
            ".tm-notion-handle",
            ".tm-notion-inline-toolbar",
            ".tm-notion-slash-menu",
            ".tm-notion-mention-menu",
            ".tm-notion-token-dropdown",
            ".tm-notion-page__settings-btn"
        };

        foreach (var selector in forbiddenVisibleControls)
        {
            Assert.AreEqual(0, await page.Locator($"{selector}:visible").CountAsync(),
                $"{label}: selector '{selector}' should not be visible.");
        }
    }

    private static async Task AssertReadableLineWidthAsync(IPage page)
    {
        Assert.IsTrue(await page.Locator(".tm-notion-page__content").First.EvaluateAsync<bool>("""
            el => {
                const rect = el.getBoundingClientRect();
                return rect.width > 520 && rect.width < 860;
            }
            """), "Reading mode should keep a comfortable line width.");
    }

    private static async Task AssertNoDuplicatedReadOnlyTextAsync(IPage page, string label)
    {
        var heading = await page.Locator(".tm-notion-h1").First.InnerTextAsync();
        AssertTextDoesNotRepeatItself(heading, label, "H1");

        var paragraph = await page.Locator(".tm-notion-paragraph").First.InnerTextAsync();
        AssertTextDoesNotRepeatItself(paragraph, label, "paragraph");

        var firstBullet = await page.Locator(".tm-notion-bullet__body").First.InnerTextAsync();
        AssertTextDoesNotRepeatItself(firstBullet, label, "list item");
    }

    private static void AssertTextDoesNotRepeatItself(string value, string label, string elementName)
    {
        var text = value.Trim();
        Assert.IsFalse(string.IsNullOrWhiteSpace(text), $"{label}: {elementName} text should not be empty.");

        if (text.Length % 2 != 0)
        {
            return;
        }

        var half = text.Length / 2;
        Assert.AreNotEqual(
            text[..half],
            text[half..],
            $"{label}: {elementName} text should render once without immediate duplication.");
    }
}
