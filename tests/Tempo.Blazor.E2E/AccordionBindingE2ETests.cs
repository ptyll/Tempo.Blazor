using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4l — the accordion's additive two-way expansion on the /feedback
/// <c>accordion-url-sync-card</c>: a deep link <c>/feedback?expanded=dark-mode</c> opens the item,
/// a click syncs the URL through <c>NavigateTo(…, replace: true)</c> (no new history entry),
/// collapsing removes the parameter, and the two-way <c>@bind-IsExpanded</c> sample follows the
/// external checkbox in both directions. The pre-existing Single/Multiple cards keep their
/// behavior. Scoped axe scans guard the card in both themes; screenshots land in
/// <c>__screenshots__/feedback/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class AccordionBindingE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string Card = "[data-testid='accordion-url-sync-card']";
    private const string Readout = "[data-testid='accordion-url-readout']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenPageAsync(string url, int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, url);
        await page.Locator(Card).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static ILocator UrlSyncAccordion(IPage page) =>
        page.Locator($"{Card} .tm-accordion").First;

    [TestMethod]
    public async Task DeepLink_OpensTheSeededItem()
    {
        var page = await OpenPageAsync($"{BaseUrl}/feedback?expanded=dark-mode");

        var accordion = UrlSyncAccordion(page);
        await accordion.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, Card);

        var headers = accordion.Locator(".tm-accordion-item__header");
        Assert.AreEqual("false", await headers.Nth(0).GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("true", await headers.Nth(1).GetAttributeAsync("aria-expanded"),
            "the deep link seeds the dark-mode item open");
        Assert.AreEqual("false", await headers.Nth(2).GetAttributeAsync("aria-expanded"));
        StringAssert.Contains(await page.Locator(Readout).TextContentAsync(), "?expanded=dark-mode");

        await AssertNoAxeViolationsAsync(page, Card, "accordion URL-sync card (deep link)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Click_SyncsTheUrl_WithoutANewHistoryEntry()
    {
        var page = await OpenPageAsync($"{BaseUrl}/feedback?expanded=dark-mode");

        var accordion = UrlSyncAccordion(page);
        await accordion.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, Card);
        var headers = accordion.Locator(".tm-accordion-item__header");

        // Single mode: opening install closes dark-mode — the URL carries exactly one key.
        await headers.Nth(0).ClickAsync();
        await page.WaitForURLAsync("**/feedback?expanded=install", new PageWaitForURLOptions { Timeout = 10000 });
        Assert.AreEqual("true", await headers.Nth(0).GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("false", await headers.Nth(1).GetAttributeAsync("aria-expanded"));
        StringAssert.Contains(await page.Locator(Readout).TextContentAsync(), "?expanded=install");

        // replace: true — Back returns to the page BEFORE the deep link, not to ?expanded=dark-mode.
        var historyLength = await page.EvaluateAsync<int>("() => history.length");
        await headers.Nth(2).ClickAsync(); // license
        await page.WaitForURLAsync("**/feedback?expanded=license", new PageWaitForURLOptions { Timeout = 10000 });
        Assert.AreEqual(historyLength, await page.EvaluateAsync<int>("() => history.length"),
            "NavigateTo(replace: true) must not pile up history entries");

        // Focus stays on the clicked header across the URL round-trip (no focus theft).
        var focused = await page.EvaluateAsync<bool>(
            """
            () => {
                const active = document.activeElement;
                return active?.classList?.contains('tm-accordion-item__header') === true
                    && active.closest("[data-testid='accordion-url-sync-card']") !== null;
            }
            """);
        Assert.IsTrue(focused, "focus stays on the header after the replace navigation");

        // Closing the open item removes the parameter entirely.
        await headers.Nth(2).ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("expanded="), new PageWaitForURLOptions { Timeout = 10000 });
        StringAssert.Contains(await page.Locator(Readout).TextContentAsync(), "(no expanded parameter)");
        Assert.AreEqual(0, await accordion.Locator(".tm-accordion-item--expanded").CountAsync());

        await AssertNoAxeViolationsAsync(page, Card, "accordion URL-sync card (after clicks)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task DeepLink_ScrollsTheOpenItemIntoView_AndKeepsTheFragmentOnClick()
    {
        var page = await OpenPageAsync($"{BaseUrl}/feedback?expanded=license#accordion-url-sync");

        // No manual ScrollIntoView — the page itself must bring the deep-linked card into view.
        // The demo helper focuses the card once the initial layout settles; wait for that focus
        // (the instant scroll happens in the same JS turn), then for the layout to be quiet.
        await page.WaitForFunctionAsync(
            "() => document.activeElement?.id === 'accordion-url-sync'",
            null, new PageWaitForFunctionOptions { Timeout = 15000 });
        await WaitForLayoutToSettleAsync(page, Card);
        var headers = UrlSyncAccordion(page).Locator(".tm-accordion-item__header");
        Assert.AreEqual("true", await headers.Nth(2).GetAttributeAsync("aria-expanded"));
        var inViewport = await headers.Nth(2).EvaluateAsync<bool>(
            """
            el => {
                const r = el.getBoundingClientRect();
                return r.height > 0 && r.top >= 0 && r.bottom <= window.innerHeight;
            }
            """);
        Assert.IsTrue(inViewport, "after a deep link the open item's header is inside the viewport");

        // GetUriWithQueryParameter preserves the fragment, so the URL-synced click keeps the anchor.
        await headers.Nth(0).ClickAsync();
        await page.WaitForURLAsync("**/feedback?expanded=install#accordion-url-sync",
            new PageWaitForURLOptions { Timeout = 10000 });
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task DeepLink_SingleMode_TwoKeys_OpensOnlyTheFirstRenderedItem()
    {
        var page = await OpenPageAsync($"{BaseUrl}/feedback?expanded=install,license");

        var accordion = UrlSyncAccordion(page);
        await accordion.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, Card);

        var headers = accordion.Locator(".tm-accordion-item__header");
        Assert.AreEqual("true", await headers.Nth(0).GetAttributeAsync("aria-expanded"),
            "single mode keeps the first registered seeded key (install)");
        Assert.AreEqual("false", await headers.Nth(1).GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("false", await headers.Nth(2).GetAttributeAsync("aria-expanded"),
            "license is seeded too, but single mode renders at most one seeded key");
        // The read-out intentionally echoes the RAW query — the contract lets unknown/unrendered
        // keys survive, so the demo does not normalize the parameter down to the rendered key.
        StringAssert.Contains(await page.Locator(Readout).TextContentAsync(), "?expanded=install,license");

        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "feedback");
        Directory.CreateDirectory(directory);
        await page.Locator(Card).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4l-accordion-url-sync-single-two-keys-light-1440.png"),
        });

        await AssertNoAxeViolationsAsync(page, Card, "accordion URL-sync card (single mode, two seeded keys)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task CheckboxAndItem_FollowEachOther_BothWays()
    {
        var page = await OpenPageAsync($"{BaseUrl}/feedback");

        var card = page.Locator(Card);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, Card);

        // The bound item is the LAST accordion on the card (the URL-synced one is first).
        var boundAccordion = card.Locator(".tm-accordion").Nth(1);
        var boundHeader = boundAccordion.Locator(".tm-accordion-item__header");
        var checkbox = card.Locator("[data-testid='accordion-bound-checkbox'] input[type='checkbox']");

        Assert.AreEqual("false", await boundHeader.GetAttributeAsync("aria-expanded"));
        Assert.IsFalse(await checkbox.IsCheckedAsync());

        // Checkbox -> item.
        await checkbox.CheckAsync();
        await Assertions.Expect(boundHeader).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.AreEqual(1, await boundAccordion.Locator(".tm-accordion-item--expanded").CountAsync());

        // Item -> checkbox.
        await boundHeader.ClickAsync();
        await Assertions.Expect(boundHeader).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.IsFalse(await checkbox.IsCheckedAsync());

        // And the item opens from its own header, checking the box back.
        await boundHeader.ClickAsync();
        await Assertions.Expect(boundHeader).ToHaveAttributeAsync("aria-expanded", "true");
        Assert.IsTrue(await checkbox.IsCheckedAsync());

        await AssertNoAxeViolationsAsync(page, Card, "accordion two-way card");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task ExistingCards_KeepTheirBehavior()
    {
        var page = await OpenPageAsync($"{BaseUrl}/feedback");

        // Single mode card (first accordion on the page): one item at a time.
        var single = page.Locator(".tm-accordion").First;
        await single.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, ".tm-accordion");
        var singleHeaders = single.Locator(".tm-accordion-item__header");
        await singleHeaders.Nth(0).ClickAsync();
        Assert.AreEqual(1, await single.Locator(".tm-accordion-item--expanded").CountAsync());
        await singleHeaders.Nth(1).ClickAsync();
        Assert.AreEqual(1, await single.Locator(".tm-accordion-item--expanded").CountAsync(),
            "single mode still closes the previous item");
        StringAssert.Contains(
            await single.Locator(".tm-accordion-item--expanded .tm-accordion-item__body").TextContentAsync(),
            "NuGet");

        // Multiple mode card: several items stay open.
        var multiple = page.Locator(".tm-accordion").Nth(1);
        var multipleHeaders = multiple.Locator(".tm-accordion-item__header");
        await multipleHeaders.Nth(0).ClickAsync();
        await multipleHeaders.Nth(1).ClickAsync();
        Assert.AreEqual(2, await multiple.Locator(".tm-accordion-item--expanded").CountAsync(),
            "multiple mode still allows several open items");

        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Screenshots_UrlSyncCard_LightDarkAndPhone()
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "feedback");
        Directory.CreateDirectory(directory);

        var page = await OpenPageAsync($"{BaseUrl}/feedback");
        var card = page.Locator(Card);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, Card);

        // Before any click: all collapsed, read-out shows no parameter.
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4l-accordion-url-sync-collapsed-light-1440.png"),
        });

        // After a click: the item is open and the URL carries the key.
        await UrlSyncAccordion(page).Locator(".tm-accordion-item__header").Nth(1).ClickAsync();
        await page.WaitForURLAsync("**/feedback?expanded=dark-mode", new PageWaitForURLOptions { Timeout = 10000 });
        await WaitForLayoutToSettleAsync(page, Card);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4l-accordion-url-sync-expanded-light-1440.png"),
        });

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, Card);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4l-accordion-url-sync-expanded-dark-1440.png"),
        });
        await AssertNoAxeViolationsAsync(page, Card, "accordion URL-sync card (dark)");

        // Edge case: the phone width — the card must fit without horizontal overflow.
        var phone = await OpenPageAsync($"{BaseUrl}/feedback?expanded=install", 390, 844);
        var phoneCard = phone.Locator(Card);
        await phoneCard.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, Card);
        await phoneCard.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4l-accordion-url-sync-expanded-light-390.png"),
        });
        var overflow = await phone.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflow, "the URL-sync card must not stretch the page horizontally");
        await AssertNoAxeViolationsAsync(phone, Card, "accordion URL-sync card (phone)");

        AssertNoClientErrors();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static async Task AssertNoAxeViolationsAsync(IPage page, string selector, string label)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector) || document.body;
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            selector);
        Assert.AreEqual(0, violations.Length,
            $"axe critical/serious violations on the {label}:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent!;
        }

        throw new InvalidOperationException("repo root not found");
    }
}

/// <summary>
/// Server-render smoke of the phase-4l accordion binding: the deep link seeds the item and a
/// click syncs the URL — the full matrix lives in <see cref="AccordionBindingE2ETests"/> (WASM).
/// </summary>
[TestClass]
[TestCategory("Server")]
public sealed class AccordionBindingServerE2ETests : ServerTestBase
{
    private const string Card = "[data-testid='accordion-url-sync-card']";

    [TestMethod]
    public async Task DeepLink_AndClick_SyncTheExpansionState()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/feedback?expanded=license");
        await page.Locator(Card).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        var accordion = page.Locator($"{Card} .tm-accordion").First;
        await accordion.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, Card);
        var headers = accordion.Locator(".tm-accordion-item__header");
        Assert.AreEqual("true", await headers.Nth(2).GetAttributeAsync("aria-expanded"),
            "the deep link seeds the license item open on the server render too");

        await headers.Nth(0).ClickAsync();
        await page.WaitForURLAsync("**/feedback?expanded=install", new PageWaitForURLOptions { Timeout = 10000 });
        Assert.AreEqual("true", await headers.Nth(0).GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("false", await headers.Nth(2).GetAttributeAsync("aria-expanded"));

        // Focus stays on the clicked header across the replace navigation (same as WASM).
        var focused = await page.EvaluateAsync<bool>(
            """
            () => {
                const active = document.activeElement;
                return active?.classList?.contains('tm-accordion-item__header') === true
                    && active.closest("[data-testid='accordion-url-sync-card']") !== null;
            }
            """);
        Assert.IsTrue(focused, "focus stays on the clicked header after the replace navigation (server render)");
    }
}
