using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 5 (plan 98c80bc3): the 2.12 additive TmTabs knobs measured where they actually live —
/// a real browser on the /feedback fixture (<c>tabs-sticky-overflow</c>).
/// <para>
/// The DOM half of the contract (panels rendered hidden, aria-controls, class names) is pinned by
/// <c>TmTabsKeepInactivePanelsTests</c> (bUnit). What bUnit cannot reach is LAYOUT: whether the
/// sticky strip really stays pinned to the top of its scroll container while the content moves
/// under it, and whether the scroll overflow kept the strip scrollable. Screenshots of the fixture
/// card land in <c>__screenshots__/tabs-fze5/</c> (light and dark) for the UX review.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTabsOverflowStickyE2ETests : WasmTestBase
{
    private const string Scroller = "[data-testid='tabs-sticky-scroller']";
    private const string Strip = "[data-testid='tabs-sticky-overflow']";
    private const string ThemeToggleSelector = "[data-testid='theme-toggle']";

    private static string OutputDirectory =>
        Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "tabs-fze5");

    private async Task<IPage> OpenFeedbackAsync(int width = 1440, int height = 900)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync($"{Strip} [role='tab']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    [TestMethod]
    public async Task KeepInactivePanels_RendersEveryPanel_AndSwapsHiddenOnClick()
    {
        var page = await OpenFeedbackAsync();

        var panels = page.Locator($"{Strip} [role='tabpanel']");
        await Assertions.Expect(panels).ToHaveCountAsync(8);

        // Every inactive panel is hidden, the active one is not, and hidden panels keep their
        // content in the DOM (the state survival the parameter exists for).
        for (var i = 0; i < 8; i++)
        {
            var hidden = await panels.Nth(i).GetAttributeAsync("hidden");
            if (i == 0)
            {
                Assert.IsNull(hidden, "the active panel must NOT be hidden");
            }
            else
            {
                Assert.IsNotNull(hidden, $"inactive panel {i + 1} must carry the hidden attribute");
            }
        }

        // aria-controls on EVERY tab (all panels exist), unlike the default lazy strip.
        var controls = await page.EvalOnSelectorAllAsync<string[]>(
            $"{Strip} [role='tab']", "els => els.map(e => e.getAttribute('aria-controls'))");
        CollectionAssert.AllItemsAreNotNull(controls);

        // Click tab 4: the hidden flags swap, the counter for the newly active tab climbs.
        await page.Locator($"{Strip} [role='tab']").Nth(3).ClickAsync();
        Assert.IsNull(await panels.Nth(3).GetAttributeAsync("hidden"), "the newly active panel must not be hidden");
        await Assertions.Expect(panels.Nth(0)).ToHaveAttributeAsync("hidden", "hidden");
        await Assertions.Expect(page.Locator($"{Strip} [role='tabpanel']").Nth(3))
            .ToContainTextAsync("1 time(s)");
    }

    [TestMethod]
    public async Task StickyHeader_StaysPinnedAtTheTopOfItsScrollContainer()
    {
        var page = await OpenFeedbackAsync();

        var scroller = page.Locator(Scroller);
        var header = page.Locator($"{Strip} .tm-tabs__header");

        var before = await header.BoundingBoxAsync();
        Assert.IsNotNull(before, "the sticky header has no layout box");

        // Scroll the container well past the strip's natural top and let the layout settle.
        await page.EvaluateAsync("sel => { const el = document.querySelector(sel); el.scrollTop = el.scrollHeight; }", Scroller);
        await page.WaitForTimeoutAsync(300);

        var scrollerBox = await scroller.BoundingBoxAsync();
        var after = await header.BoundingBoxAsync();
        Assert.IsNotNull(scrollerBox);
        Assert.IsNotNull(after);

        // The header must sit at (or within a pixel of) the scroller's top edge — pinned, not
        // scrolled away with the content.
        var delta = Math.Abs(after.Y - scrollerBox.Y);
        Assert.IsTrue(
            delta <= 1.0,
            $"the sticky header must stay pinned at the scroller's top edge; measured delta {delta:F2}px "
            + $"(header top {after.Y:F2} vs scroller top {scrollerBox.Y:F2})");

        // And it must actually have MOVED relative to the content, i.e. pinning is doing work:
        // without position:sticky the header would be at before.Y - scrollHeight + scroller height.
        Assert.IsTrue(
            after.Y > before.Y - scrollerBox.Height,
            "the header must not have scrolled out of view with the content");
    }

    [TestMethod]
    public async Task ScrollOverflow_KeepsTheStripScrollable_WithEdgeShadowsWired()
    {
        var page = await OpenFeedbackAsync();

        var header = page.Locator($"{Strip} .tm-tabs__header");

        // Eight labelled tabs in a ~1100px card: the scrollport really overflows.
        var scrollInfo = await header.EvaluateAsync<System.Text.Json.JsonElement>(
            "el => ({ scrollWidth: el.scrollWidth, clientWidth: el.clientWidth })");
        var scrollWidth = scrollInfo.GetProperty("scrollWidth").GetDouble();
        var clientWidth = scrollInfo.GetProperty("clientWidth").GetDouble();
        Assert.IsTrue(
            scrollWidth > clientWidth,
            $"the fixture strip must overflow horizontally to exercise the scroll shadows "
            + $"(scrollWidth {scrollWidth} vs clientWidth {clientWidth})");

        // The edge-shadow contract: the local/scroll background-attachment pair is present
        // (this is the CSS-only mechanism — no JS listener is involved).
        var attachment = await header.EvaluateAsync<string>(
            "el => getComputedStyle(el).backgroundAttachment + '|' + getComputedStyle(el).backgroundImage");
        StringAssert.Contains(attachment, "local");
        StringAssert.Contains(attachment, "linear-gradient");

        // The strip is NOT wrap mode.
        var rootClass = await page.Locator(Strip).GetAttributeAsync("class");
        Assert.IsFalse(
            (rootClass ?? string.Empty).Contains("tm-tabs--wrap"),
            "the scroll-overflow fixture must not carry the wrap class");
    }

    [TestMethod]
    public async Task PillStickyOverflow_KeepsTheEdgeShadowGradients()
    {
        // Regression guard: the sticky header paints background-COLOR in
        // --tm-tabs-scroll-cover — never the background shorthand, which would reset the
        // edge-shadow background-image/attachment of the scrollport. Pill exercises the worst
        // case, because its cover token (the muted tray) differs from the line variant's.
        var page = await OpenFeedbackAsync();

        var pillHeader = page.Locator("[data-testid='tabs-sticky-pill-overflow'] .tm-tabs__header");

        var pillStyles = await pillHeader.EvaluateAsync<System.Text.Json.JsonElement>(
            "el => { const s = getComputedStyle(el); return { image: s.backgroundImage, attachment: s.backgroundAttachment, color: s.backgroundColor }; }");
        StringAssert.Contains(pillStyles.GetProperty("image").GetString(), "linear-gradient");
        StringAssert.Contains(pillStyles.GetProperty("attachment").GetString(), "local");
        Assert.IsFalse(string.IsNullOrWhiteSpace(pillStyles.GetProperty("color").GetString()));

        // And the pill fixture really overflows, so the shadows have something to sit over.
        var pillScroll = await pillHeader.EvaluateAsync<System.Text.Json.JsonElement>(
            "el => ({ scrollWidth: el.scrollWidth, clientWidth: el.clientWidth })");
        Assert.IsTrue(
            pillScroll.GetProperty("scrollWidth").GetDouble() > pillScroll.GetProperty("clientWidth").GetDouble(),
            "the pill sticky fixture must overflow horizontally");
    }

    [TestMethod]
    public async Task StickyTop_IsOverridableFromAnAncestor()
    {
        // The --tm-tabs-sticky-top default lives on :root (tokens.css), NOT on .tm-tabs--sticky —
        // a declaration on the component's own class would beat every inherited value, so a host
        // override on an ancestor (`:root { --tm-tabs-sticky-top: 56px }`) would silently do
        // nothing. This pins the inheritance contract.
        var page = await OpenFeedbackAsync();

        await page.EvaluateAsync(@"
            () => {
                const style = document.createElement('style');
                style.id = 'tabs-sticky-top-override';
                style.textContent = `[data-testid='tabs-sticky-scroller'] { --tm-tabs-sticky-top: 56px; }`;
                document.head.appendChild(style);
            }");

        var top = await page.Locator($"{Strip} .tm-tabs__header").EvaluateAsync<string>(
            "el => getComputedStyle(el).top");
        Assert.AreEqual("56px", top, "the ancestor's --tm-tabs-sticky-top override must reach the sticky header");
    }

    [TestMethod]
    public async Task FixtureCard_CapturesLightAndDark()
    {
        var page = await OpenFeedbackAsync();

        var card = page.Locator($"{Scroller}").Locator("xpath=ancestor::div[contains(@class,'showcase-card')][1]");
        await Assertions.Expect(card).ToBeVisibleAsync();

        await CaptureAsync(page, card, "tabs-sticky-kept-alive-light");

        await page.Locator($"{ThemeToggleSelector}:visible").First.ClickAsync();
        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });

        await CaptureAsync(page, card, "tabs-sticky-kept-alive-dark");
    }

    private static async Task CaptureAsync(IPage page, ILocator card, string name)
    {
        // Park the pointer so no leftover hover state is in the shot.
        await page.Mouse.MoveAsync(0, 0);
        await page.WaitForTimeoutAsync(200);
        Directory.CreateDirectory(OutputDirectory);
        var path = Path.Combine(OutputDirectory, $"{name}.png");
        await card.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Timeout = 60_000 });
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
        Assert.IsTrue(new FileInfo(path).Length > 2048, $"the screenshot is suspiciously small: {path}");
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
