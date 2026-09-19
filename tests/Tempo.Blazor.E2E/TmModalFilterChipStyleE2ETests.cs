using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// 2.9.0 visual-regression E2E: the declarations 2.8.26 deleted as "dead duplicates" were painting
/// real pixels — a descendant rule (<c>.tm-modal-header h3</c>, <c>.tm-filter-chip button</c>)
/// out-ranked the owning class, and three properties had no other declaration at all. The fix put
/// every value back on the OWNING class; these tests read <c>getComputedStyle</c> in a real browser
/// (px, resolved tokens — not the CssCascade model) so a future removal fails here, in Chromium.
/// Expected values are the 2.8.25 computed values: --tm-font-size-lg 1.125rem = 18px,
/// --tm-font-size-xl 1.25rem = 20px, --tm-space-4 1rem = 16px at the demo's 16px root.
/// </summary>
[TestClass]
public sealed class TmModalFilterChipStyleE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task TmModal_TitleCloseAndHeader_PaintRestoredComputedValues()
    {
        var page = await OpenModalPageAsync(1366, 900);
        await page.Locator("[data-testid='open-basic-modal']").ClickAsync();
        var modal = page.Locator(".tm-modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // The descendant .tm-modal-header h3 (0,1,1) pinned the title to lg = 18px in 2.8.25;
        // its removal let the bare .tm-modal-title paint xl = 20px. Restored on the owner.
        var titleFontSize = await ComputedAsync(page, ".tm-modal-title", "fontSize");
        Assert.AreEqual("18px", titleFontSize,
            "nadpis modalu maluje 18px (--tm-font-size-lg) — hodnota, kterou v 2.8.25 přikovalo " +
            "potomkové pravidlo .tm-modal-header h3");

        // font-size existed only in _dashboard.css's .tm-modal-close — removal dropped the close
        // glyph to the user-agent button size. Restored: xl = 20px.
        var closeFontSize = await ComputedAsync(page, ".tm-modal-close", "fontSize");
        Assert.AreEqual("20px", closeFontSize,
            "zavírací tlačítko maluje 20px (--tm-font-size-xl) — jediná deklarace žila v cizím souboru");

        // justify-content was a single-declaration property in _dashboard.css's .tm-modal-header.
        var justify = await ComputedAsync(page, ".tm-modal-header", "justifyContent");
        Assert.AreEqual("space-between", justify,
            "hlavička drží titulek vlevo a křížek vpravo — space-between maloval cizí stylesheet");
    }

    [TestMethod]
    public async Task TmModal_Below768px_KeepsTheRestoredMargin()
    {
        var page = await OpenModalPageAsync(500, 800);
        await page.Locator("[data-testid='open-basic-modal']").ClickAsync();
        var modal = page.Locator(".tm-modal");
        await modal.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30000 });

        // The @media (max-width: 768px) .tm-modal { margin: var(--tm-space-4) } rule was the only
        // declaration of the property anywhere — deleting it zeroed the mobile margin.
        var marginTop = await ComputedAsync(page, ".tm-modal", "marginTop");
        Assert.AreEqual("16px", marginTop,
            "pod 768px má .tm-modal margin 16px (--tm-space-4) — pravidlo přežilo přesun do _modal.css");
    }

    [TestMethod]
    public async Task TmFilterChip_RemoveButton_PaintsZeroPaddingAnd18pxGlyph()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1366, 900);
        await page.GotoAsync($"{BaseUrl}/data-table", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);

        // Build one filter through the real UI so a live .tm-filter-chip-remove exists.
        var addButton = page.Locator(".tm-filter-builder-add").First;
        await addButton.ScrollIntoViewIfNeededAsync();
        await addButton.ClickAsync();
        await page.Locator(".tm-filter-field-option").First.ClickAsync();
        var valueInput = page.Locator(".tm-filter-value-input");
        await valueInput.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await valueInput.FillAsync("Alice");
        await page.Locator(".tm-filter-apply").ClickAsync();

        var remove = page.Locator(".tm-filter-chip-remove").First;
        await remove.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // The descendant .tm-filter-chip button (0,1,1) kept the × glyph at padding 0 and lg = 18px
        // in 2.8.25, out-ranking the owner's md/16px and 0 var(--tm-space-1). Restored on the owner.
        var fontSize = await ComputedAsync(remove, "fontSize");
        Assert.AreEqual("18px", fontSize,
            "křížek chipu maluje 18px (--tm-font-size-lg) jako v 2.8.25");
        var paddingLeft = await ComputedAsync(remove, "paddingLeft");
        var paddingRight = await ComputedAsync(remove, "paddingRight");
        Assert.AreEqual("0px", paddingLeft, "křížek chipu nemá horizontální padding");
        Assert.AreEqual("0px", paddingRight, "křížek chipu nemá horizontální padding");
    }

    private async Task<IPage> OpenModalPageAsync(int width, int height)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/modal-dialog", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task<string?> ComputedAsync(IPage page, string selector, string property) =>
        await page.Locator(selector).First.EvaluateAsync<string>(
            $"(el) => getComputedStyle(el).{property}");

    private static async Task<string?> ComputedAsync(ILocator locator, string property) =>
        await locator.EvaluateAsync<string>(
            $"(el) => getComputedStyle(el).{property}");
}
