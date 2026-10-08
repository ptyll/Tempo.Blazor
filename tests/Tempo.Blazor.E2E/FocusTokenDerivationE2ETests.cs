using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Gap register D, proven in a real browser: the focus ring DERIVES from the primary scale —
/// <c>var(--tm-focus-ring)</c> ends in <c>var(--tm-color-primary)</c> (<c>0 0 0 4px</c> outer
/// layer), which resolves to primary-600 in the light theme and primary-400 in the dark — so
/// repointing a scale step repaints it. The token-file tests pin the declared expression; only
/// a computed style can prove the chain actually resolves — a literal colour looks identical
/// right up to the moment somebody rebrands and the ring stays Tempo blue.
/// </summary>
/// <remarks>
/// The ring resolves to a literal hex through <c>var()</c> chains, so Chromium serialises the
/// layers as <c>rgb(r, g, b) 0px 0px 0px Npx</c> — the assertions match the outer layer's
/// colour and spread rather than a <c>color(srgb …)</c> relative-colour form.
/// </remarks>
[TestClass]
public class FocusTokenDerivationE2ETests : WasmTestBase
{
    private const string FormsPage = "/forms";

    /// <summary>The light ring's outer layer is <c>--tm-color-primary</c> → primary-600 #2563eb.</summary>
    private const string LightRing = "rgb(37, 99, 235) 0px 0px 0px 4px";

    /// <summary>The dark ring's outer layer is <c>--tm-color-primary</c> → primary-400 #60a5fa.</summary>
    private const string DarkRing = "rgb(96, 165, 250) 0px 0px 0px 4px";

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
    /// paints <c>var(--tm-focus-ring)</c>, so this is the ring the user actually sees.</summary>
    private static async Task<string> FocusedRingAsync(IPage page)
    {
        var input = page.Locator("input.tm-input").First;
        await input.ScrollIntoViewIfNeededAsync();
        await input.FocusAsync();
        return await input.EvaluateAsync<string>("el => getComputedStyle(el).boxShadow");
    }

    [TestMethod]
    [Description("Repointing --tm-color-primary-600 repaints the light focus ring; repointing " +
        "primary-400 repaints the dark one — the ring is derived, not literal")]
    public async Task ShadowFocus_FollowsThePrimaryScaleStep_InBothThemes()
    {
        var page = await OpenFormsPageAsync();

        StringAssert.Contains(await FocusedRingAsync(page), LightRing,
            "the default light ring's outer layer is primary-600");

        // Rebrand: repoint the 600 step, which --tm-color-primary aliases. A derived ring must
        // repaint; a literal would not move.
        await page.EvaluateAsync(
            "document.documentElement.style.setProperty('--tm-color-primary-600', '#ff0000')");
        StringAssert.Contains(await FocusedRingAsync(page), "rgb(255, 0, 0) 0px 0px 0px 4px",
            "overriding --tm-color-primary-600 must alter the computed focus colour");
        await page.EvaluateAsync(
            "document.documentElement.style.removeProperty('--tm-color-primary-600')");

        // Dark: the demo marks the theme on the layout wrapper; click the visible toggle.
        await page.Locator("button[title*='dark' i]:visible").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-theme='dark']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(400);

        StringAssert.Contains(await FocusedRingAsync(page), DarkRing,
            "the default dark ring's outer layer is the dark primary step (primary-400)");

        await page.EvaluateAsync(
            "document.documentElement.style.setProperty('--tm-color-primary-400', '#00ff00')");
        StringAssert.Contains(await FocusedRingAsync(page), "rgb(0, 255, 0) 0px 0px 0px 4px",
            "overriding --tm-color-primary-400 must alter the computed dark focus colour");
    }
}
