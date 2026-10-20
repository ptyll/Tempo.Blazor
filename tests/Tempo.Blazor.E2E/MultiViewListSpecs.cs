using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DataTableE2EKit;

namespace Tempo.Blazor.E2E;

/// <summary>Phase 13 (13.5) spec bodies for <c>TmMultiViewList</c>; state assertions only.</summary>
internal static class MultiViewListSpecs
{
    private static ILocator List(IPage page) => page.Locator("[data-testid='mvl-redesign-list']");

    internal static async Task RadiogroupAndCardsAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/multi-view-list", new Options(), register);
        var list = List(page);
        await list.ScrollIntoViewIfNeededAsync();

        // One Tab stop for the whole switcher; arrows move focus AND the view.
        var radios = list.Locator(".tm-mvl-switcher [role='radio']");
        Assert.AreEqual(1, await radios.EvaluateAllAsync<int>("els => els.filter(e => e.tabIndex === 0).length"), "roving tabindex: exactly one Tab stop");
        await list.Locator(".tm-mvl-switch-table").FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(list.Locator(".tm-mvl-switch-card")).ToBeFocusedAsync();
        await Assertions.Expect(list.Locator(".tm-mvl-switch-card")).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(list.Locator(".tm-mvl-card-grid")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await Assertions.Expect(list.Locator(".tm-mvl-switch-list")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("ArrowLeft");
        await Assertions.Expect(list.Locator(".tm-mvl-switch-card")).ToBeFocusedAsync();

        // Initials fallback, item count that follows the search, and the column cap.
        await SelectAsync(page, "mvl-redesign-avatar-fallback", "Initials");
        await ToggleAsync(page, "mvl-redesign-item-count");
        await ToggleAsync(page, "mvl-redesign-max-columns");
        var cards = list.Locator(".tm-mvl-card");
        await Assertions.Expect(cards).ToHaveCountAsync(3);
        await Assertions.Expect(cards.First.Locator(".tm-avatar-fallback")).ToBeVisibleAsync();
        await Assertions.Expect(cards.First.Locator(".tm-avatar-fallback")).ToHaveTextAsync("AL");
        var count = list.Locator(".tm-mvl-item-count");
        await Assertions.Expect(count).ToContainTextAsync("3");
        await list.Locator("input[type='search']").FillAsync("Ada");
        await Assertions.Expect(count).ToContainTextAsync("1");
        await Assertions.Expect(cards).ToHaveCountAsync(1);
        await list.Locator("input[type='search']").FillAsync(string.Empty);
        await Assertions.Expect(cards).ToHaveCountAsync(3);

        var rows = await cards.EvaluateAllAsync<double[]>("els => els.map(e => Math.round(e.getBoundingClientRect().top))");
        var perRow = rows.GroupBy(r => r).Max(g => g.Count());
        Assert.IsTrue(perRow <= 2, $"MaxCardColumns=2 allows at most two cards per row, got {perRow}");
        Assert.IsTrue(rows.Distinct().Count() >= 2, "three cards wrap onto a second row under a cap of two");

        // Uncapped at 1440 the same three cards sit on one row.
        await ToggleAsync(page, "mvl-redesign-max-columns");
        await Assertions.Expect(cards).ToHaveCountAsync(3);
        await page.WaitForFunctionAsync(
            "() => new Set([...document.querySelectorAll(\"[data-testid='mvl-redesign-list'] .tm-mvl-card\")].map(e => Math.round(e.getBoundingClientRect().top))).size === 1",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    internal static async Task StretchedLinkAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/data-display", new Options(), register);
        var section = page.GetByTestId("mvl-stretched-link");
        await section.ScrollIntoViewIfNeededAsync();
        var card = section.Locator(".tm-mvl-card--stretched").First;
        await card.WaitForAsync();

        Assert.AreEqual(1, await card.Locator("button, a, [tabindex]:not([tabindex='-1'])").CountAsync(), "one Tab stop per stretched-link card");

        var clicks = page.GetByTestId("mvl-stretched-clicks");
        var before = int.Parse(await clicks.InnerTextAsync());
        var box = await BoxAsync(card);
        await page.Mouse.ClickAsync(box.X + box.Width - 12, box.Y + box.Height - 12);
        await Assertions.Expect(clicks).ToHaveTextAsync((before + 1).ToString());

        await card.Locator("button.tm-stretched-link").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(clicks).ToHaveTextAsync((before + 2).ToString());
        await page.WaitForTimeoutAsync(300);
        await Assertions.Expect(clicks).ToHaveTextAsync((before + 2).ToString()); // exactly once per press
    }

    internal static async Task GroupPickerFocusAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/multi-view-list", new Options(), register);
        var list = List(page);
        await list.ScrollIntoViewIfNeededAsync();
        var button = list.Locator(".tm-mvl-group-btn");
        await button.ClickAsync();
        var panel = page.Locator(".tm-mvl-group-dropdown");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        var b = await BoxAsync(button);
        var p = await BoxAsync(panel);
        Assert.IsTrue(p.Y - (b.Y + b.Height) is >= -1 and <= 8, "the group picker is anchored under its button");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        await Assertions.Expect(button).ToBeFocusedAsync();

        await button.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        await page.Mouse.ClickAsync(5, 300);
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        Assert.IsFalse(await button.EvaluateAsync<bool>("el => el === document.activeElement"), "an outside click does not return focus to the trigger");
    }

    internal static async Task EdgeAsync(IBrowser browser, string baseUrl, Action<IBrowserContext> register)
    {
        var page = await OpenAsync(browser, baseUrl, "/multi-view-list", new Options(), register);
        var empty = page.Locator("[data-testid='mvl-edge-empty']");
        await empty.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(empty.Locator(".tm-empty-state")).ToContainTextAsync("Nobody here yet");
        Assert.AreEqual(0, await empty.Locator(".tm-mvl-card").CountAsync());

        var longHost = page.Locator("[data-testid='mvl-edge-long']");
        await longHost.ScrollIntoViewIfNeededAsync();
        var cards = longHost.Locator(".tm-mvl-card");
        await Assertions.Expect(cards).ToHaveCountAsync(2);
        var host = await BoxAsync(longHost);
        foreach (var card in await cards.EvaluateAllAsync<double[][]>("els => els.map(e => { const r = e.getBoundingClientRect(); return [r.left, r.right]; })"))
        {
            Assert.IsTrue(card[0] >= host.X - 1 && card[1] <= host.X + host.Width + 1, "a card with a very long title stays inside its container");
        }

        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth + 1"), "no horizontal page overflow");
    }
}
