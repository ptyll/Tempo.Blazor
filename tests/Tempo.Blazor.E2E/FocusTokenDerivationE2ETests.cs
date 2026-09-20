using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Gap register D, proven in a real browser: the focus ring DERIVES from the primary scale via
/// relative colour syntax (<c>rgb(from var(--tm-color-primary-500) r g b / 0.4)</c>), so repointing
/// a scale step repaints it. The token-file tests pin the declared expression; only a computed
/// style can prove the chain actually resolves — a literal <c>rgba()</c> looks identical right up
/// to the moment somebody rebrands and the ring stays Tempo blue.
/// </summary>
/// <remarks>
/// Chromium serialises the resolved relative colour in <c>color(srgb …)</c> form, so the
/// assertions match the normalised channel triplets rather than 0–255 rgba() text.
/// </remarks>
[TestClass]
public class FocusTokenDerivationE2ETests : WasmTestBase
{
    private const string FormsPage = "/forms";

    /// <summary>--tm-color-primary-500 #3b82f6 serialised: 59/255, 130/255, 246/255.</summary>
    private const string LightRing = "color(srgb 0.231373 0.509804 0.964706 / 0.4)";

    /// <summary>The dark ring derives from the dark primary step, primary-400 #60a5fa.</summary>
    private const string DarkRing = "color(srgb 0.376471 0.647059 0.980392 / 0.5)";

    private async Task<IPage> OpenFormsPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{FormsPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>The computed <c>box-shadow</c> of a focused <c>.tm-input</c> — <c>.tm-input:focus</c>
    /// paints <c>var(--tm-shadow-focus)</c>, so this is the ring the user actually sees.</summary>
    private static async Task<string> FocusedRingAsync(IPage page)
    {
        var input = page.Locator("input.tm-input").First;
        await input.ScrollIntoViewIfNeededAsync();
        await input.FocusAsync();
        return await input.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
    }

    [TestMethod]
    [Description("Repointing --tm-color-primary-500 repaints the light focus ring; repointing " +
        "primary-400 repaints the dark one — the ring is derived, not literal")]
    public async Task ShadowFocus_FollowsThePrimaryScaleStep_InBothThemes()
    {
        var page = await OpenFormsPageAsync();

        StringAssert.Contains(await FocusedRingAsync(page), LightRing,
            "the default light ring is primary-500 at 0.4 alpha");

        // Rebrand: repoint the 500 step. A derived ring must repaint; a literal would not move.
        await page.EvaluateAsync(
            "document.documentElement.style.setProperty('--tm-color-primary-500', '#ff0000')");
        StringAssert.Contains(await FocusedRingAsync(page), "color(srgb 1 0 0 / 0.4)",
            "overriding --tm-color-primary-500 must alter the computed focus colour");
        await page.EvaluateAsync(
            "document.documentElement.style.removeProperty('--tm-color-primary-500')");

        // Dark: the demo marks the theme on the layout wrapper; click the visible toggle.
        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        StringAssert.Contains(await FocusedRingAsync(page), DarkRing,
            "the default dark ring is the dark primary step (primary-400) at 0.5 alpha");

        await page.EvaluateAsync(
            "document.documentElement.style.setProperty('--tm-color-primary-400', '#00ff00')");
        StringAssert.Contains(await FocusedRingAsync(page), "color(srgb 0 1 0 / 0.5)",
            "overriding --tm-color-primary-400 must alter the computed dark focus colour");
    }
}
