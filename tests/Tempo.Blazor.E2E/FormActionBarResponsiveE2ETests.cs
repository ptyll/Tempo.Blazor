using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Gap register #13, proven in a real browser: <c>Position="FloatingBottomFromMd"</c> is static
/// in normal flow below the md boundary (768px) and fixed to the viewport bottom from md up —
/// and <c>--tm-form-action-bar-reserve-block-size</c> follows the same boundary, collapsing to
/// zero when the bar stops overlaying content. bUnit cannot see any of this: it has no layout,
/// no media queries and no cascade, so the contract is measured here as computed style.
/// </summary>
[TestClass]
public class FormActionBarResponsiveE2ETests : WasmTestBase
{
    private const string ToolbarFormsPage = "/toolbar-forms";

    private async Task<IPage> OpenToolbarFormsAsync(int width, int height)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{ToolbarFormsPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>Resolved pixel width of the reserve when applied as real padding to a probe
    /// element — reading the custom property directly would return the unresolved calc() text.</summary>
    private static async Task<string> ResolvedReserveAsync(IPage page)
    {
        return await page.EvaluateAsync<string>("""            
            () => {
                const probe = document.createElement('div');
                probe.style.paddingBlockEnd = 'var(--tm-form-action-bar-reserve-block-size)';
                document.body.appendChild(probe);
                const value = getComputedStyle(probe).paddingBlockEnd;
                probe.remove();
                return value;
            }
            """);
    }

    [TestMethod]
    [Description("390×844 (below md): the bar is in document flow — position: static — and the " +
        "reserve token resolves to 0px, so no dead space is left under it")]
    public async Task FloatingBottomFromMd_BelowMd_IsStaticAndTheReserveIsZero()
    {
        var page = await OpenToolbarFormsAsync(390, 844);

        var bar = page.Locator("[data-testid='demo-fab-responsive']");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var position = await bar.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.AreEqual(
            "static", position,
            "below 768px the responsive bar must be in normal flow — a fixed bar clips the "
            + "viewport height it shares with content and stays on top of the open mobile menu");

        var reserve = await ResolvedReserveAsync(page);
        Assert.AreEqual(
            "0px", reserve,
            "with the bar in flow there is nothing to reserve — the token collapses to zero "
            + "below the same boundary (one library-owned number decides both)");
    }

    [TestMethod]
    [Description("1440×900 (md and above): the bar is fixed to the viewport bottom and the " +
        "reserve token resolves to a positive height the host leaves under it")]
    public async Task FloatingBottomFromMd_FromMd_IsFixedToViewportBottomAndReserveIsPositive()
    {
        var page = await OpenToolbarFormsAsync(1440, 900);

        var bar = page.Locator("[data-testid='demo-fab-responsive']");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var position = await bar.EvaluateAsync<string>("el => getComputedStyle(el).position");
        Assert.AreEqual("fixed", position,
            "from 768px up the responsive mode must keep the floating-bottom contract");

        var box = await bar.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.AreEqual(
            900.0, box!.Y + box.Height, 1.0,
            "the floating bar pins to the viewport bottom edge");

        var reserve = await ResolvedReserveAsync(page);
        Assert.IsTrue(
            double.Parse(reserve.Replace("px", string.Empty), CultureInfo.InvariantCulture) > 0,
            $"the host reserve must be positive while the bar overlays content, got {reserve}");
    }

    [TestMethod]
    [Description("Below md the bar cannot cover anything: the last control on the page stays " +
        "clickable and the bar's box lies inside the document flow, not pinned to the viewport")]
    public async Task FloatingBottomFromMd_BelowMd_DoesNotObscurePageControls()
    {
        var page = await OpenToolbarFormsAsync(390, 844);

        var bar = page.Locator("[data-testid='demo-fab-responsive']");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // A statically-positioned bar scrolls with the document — its viewport position is not
        // pinned. Scroll to the top and assert the bar is wherever the document puts it, which
        // for a fixed overlay would instead be glued to the bottom edge.
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        var box = await bar.BoundingBoxAsync();

        // The click proves reachability: had the bar stayed fixed, Playwright's actionability
        // check would refuse the click because the bar's box would sit over the control.
        var save = page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).First;
        await save.ScrollIntoViewIfNeededAsync();
        await save.ClickAsync();
    }

    [TestMethod]
    [Description("From md the host reserve keeps the page's last card clear of the floating bar — " +
        "its bottom edge stays above the bar's top edge after scrolling to the end")]
    public async Task FloatingBottomFromMd_FromMd_ReserveKeepsContentClearOfTheBar()
    {
        var page = await OpenToolbarFormsAsync(1440, 900);

        var bar = page.Locator("[data-testid='demo-fab-responsive']");
        await bar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        // Instant assignment — scrollTo() honours scroll-behavior:smooth and the measurement
        // would race the animation.
        await page.EvaluateAsync(
            "() => { const s = document.scrollingElement; s.scrollTop = s.scrollHeight; }");
        await page.WaitForFunctionAsync(
            "() => { const s = document.scrollingElement; "
            + "return s.scrollTop >= s.scrollHeight - window.innerHeight - 1; }",
            options: new PageWaitForFunctionOptions { Timeout = 10000 });

        // Measure both boxes in ONE evaluate: getBoundingClientRect is viewport-relative for
        // both, so no coordinate-space guessing across two calls.
        var geometry = await page.EvaluateAsync<string>("""
            () => {
                const cards = document.querySelectorAll('main .showcase-card');
                const last = cards[cards.length - 1].getBoundingClientRect();
                const bar = document.querySelector(
                    "[data-testid='demo-fab-responsive']").getBoundingClientRect();
                return JSON.stringify({
                    cardBottom: last.bottom, barTop: bar.top,
                    scrollY: window.scrollY,
                    scrollHeight: document.scrollingElement.scrollHeight,
                    innerHeight: window.innerHeight });
            }
            """);
        using var doc = System.Text.Json.JsonDocument.Parse(geometry);
        var cardBottom = doc.RootElement.GetProperty("cardBottom").GetDouble();
        var barTop = doc.RootElement.GetProperty("barTop").GetDouble();
        var scrollY = doc.RootElement.GetProperty("scrollY").GetDouble();
        var scrollHeight = doc.RootElement.GetProperty("scrollHeight").GetDouble();
        var innerHeight = doc.RootElement.GetProperty("innerHeight").GetDouble();

        Assert.AreEqual(
            scrollHeight - innerHeight, scrollY, 1.5,
            $"the page must actually be scrolled to the end (scrollY={scrollY}, "
            + $"scrollHeight={scrollHeight}, innerHeight={innerHeight})");
        Assert.IsTrue(
            cardBottom <= barTop + 1,
            $"content ends at {cardBottom} but the bar starts at {barTop} — "
            + "the reserve is missing or smaller than the bar");
    }
}
