using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4c — the remaining picker triggers speak the phase-4 ARIA contract:
/// a locked (ReadOnly) <c>TmTimePicker</c> renders NO overlay trigger where the segments render
/// (the read-only segments carry the value and the lock); on a phone the trigger stays as the
/// value's carrier — without the opening icon, announcing the lock
/// (<c>aria-haspopup="false"</c>, no <c>aria-expanded</c>/<c>aria-controls</c>,
/// <c>aria-disabled="true"</c>, focusable, no activation opens anything,
/// <c>AriaDescribedBy</c> reaches it) instead of advertising a popup it can never open. The
/// <c>TmDateRangePicker</c> / <c>TmMultiViewList</c> cards-toolbar triggers carry
/// <c>aria-expanded</c> as the STRING "true"/"false" (a bool value rendered the invalid empty
/// attribute when open and dropped the attribute when closed). Scoped axe scans guard
/// aria-allowed-attr / aria-valid-attr-value in both themes. Screenshots land in
/// <c>__screenshots__/pickers/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmPickerAriaStateE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string TimeReadOnlyCard = "[data-testid='time-picker-readonly-card']";
    private const string MvlToolbarCard = "[data-testid='mvl-cards-toolbar-card']";

    private readonly List<string> _clientErrors = [];

    private static Task PinCultureAsync(IBrowserContext context) =>
        context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

    private async Task<IPage> OpenTimeInputAsync(int width = 1440, int height = 1000, bool touch = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/time-input",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = width, Height = height },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                HasTouch = touch,
                IsMobile = touch,
            },
            PinCultureAsync);
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.Locator(TimeReadOnlyCard).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private async Task<IPage> OpenPickersAsync()
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/pickers",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            },
            PinCultureAsync);
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.Locator(".tm-date-range-picker").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private async Task<IPage> OpenDataDisplayAsync()
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/data-display",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            },
            PinCultureAsync);
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.Locator(MvlToolbarCard).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    [TestMethod]
    public async Task ReadOnlyTimePicker_AnnouncesTheLock_NotAPopup()
    {
        var page = await OpenTimeInputAsync();

        // Auto on a desktop viewport resolves to the popup presentation: the segments render,
        // so the locked picker renders NO trigger at all — a button that opens nothing would be
        // a dead tab stop. The read-only segments carry the value and the lock.
        var card = page.Locator(TimeReadOnlyCard);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, TimeReadOnlyCard);
        Assert.AreEqual(0, await card.Locator(".tm-time-picker__trigger").CountAsync(),
            "where the segments render, the locked trigger is not rendered at all");

        var hours = card.Locator(".tm-time-seg--hours");
        Assert.IsNotNull(await hours.GetAttributeAsync("readonly"),
            "the segments are the read-only surface");
        Assert.AreEqual("true", await hours.GetAttributeAsync("aria-readonly"));
        StringAssert.Contains(await hours.GetAttributeAsync("aria-describedby") ?? string.Empty,
            "time-picker-readonly-reason",
            "AriaDescribedBy reaches the segments, so the host can describe WHY the field is locked");
        Assert.IsNull(await card.Locator(".tm-time-picker-body").GetAttributeAsync("aria-readonly"),
            "aria-readonly is not a supported attribute on role=group (ARIA 1.2)");

        // No activation path opens anything — there is no trigger to activate.
        Assert.AreEqual(0, await page.Locator(".tm-time-picker__popup").CountAsync(),
            "no activation path opens the locked overlay");
        Assert.AreEqual(0, await page.Locator(".tm-drawer").CountAsync(),
            "no activation path opens the locked sheet either");

        await AssertNoAxeViolationsAsync(page, TimeReadOnlyCard, "read-only time picker card");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task EditableTimePicker_KeepsThePopupContract()
    {
        var page = await OpenTimeInputAsync();

        var trigger = page.Locator("[data-testid='time-picker-auto-card'] .tm-time-picker__trigger");
        Assert.AreEqual("dialog", await trigger.GetAttributeAsync("aria-haspopup"));
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));
        Assert.IsNull(await trigger.GetAttributeAsync("aria-disabled"));
        Assert.IsNotNull(await trigger.GetAttributeAsync("aria-controls"));

        await trigger.ClickAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.Locator(".tm-time-picker__popup")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-time-picker__popup")).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task ReadOnlyTimePicker_Phone_KeepsTheValueVisible_AndLocked()
    {
        // Edge case: on a phone Auto renders the trigger ONLY — it carries the value, so the
        // locked trigger must stay visible, without the opening icon, and still refuse to open.
        // The full locked-trigger ARIA contract lives here (the desktop renders no trigger).
        var page = await OpenTimeInputAsync(390, 844, touch: true);

        var card = page.Locator(TimeReadOnlyCard);
        var trigger = card.Locator(".tm-time-picker__trigger");
        await Assertions.Expect(card.Locator(".tm-time-seg")).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToContainTextAsync("9:15");

        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-haspopup"),
            "a locked trigger has no popup to advertise");
        Assert.IsNull(await trigger.GetAttributeAsync("aria-expanded"),
            "aria-expanded on a button that opens nothing announces a collapsed expandable control");
        Assert.IsNull(await trigger.GetAttributeAsync("aria-controls"),
            "there is no overlay to control while the picker is locked");
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-disabled"),
            "the lock is announced as disabled — aria-readonly is not valid on role=button");
        Assert.IsNull(await trigger.GetAttributeAsync("aria-readonly"));
        Assert.IsNull(await trigger.GetAttributeAsync("disabled"),
            "aria-disabled names the state; the trigger stays focusable and readable");
        Assert.AreEqual(0, await trigger.Locator(".tm-time-picker__icon").CountAsync(),
            "no opening icon on the locked trigger — nothing advertises a popup that can never open");
        StringAssert.Contains(await trigger.GetAttributeAsync("aria-describedby") ?? string.Empty,
            "time-picker-readonly-reason",
            "AriaDescribedBy reaches the locked trigger");

        // The trigger keeps its natural tab stop and takes focus; Enter/Space open nothing.
        await trigger.FocusAsync();
        var focused = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList?.contains('tm-time-picker__trigger') === true");
        Assert.IsTrue(focused, "the locked trigger stays keyboard reachable");

        // No activation path opens the sheet (the click is FORCED — Playwright already refuses
        // to click an aria-disabled button, itself proof the lock is announced).
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Space");
        Assert.AreEqual(0, await page.Locator(".tm-drawer").CountAsync(),
            "a tap on the locked phone trigger opens no sheet");

        // The locked trigger reads like the read-only time input: the same computed background
        // (--tm-bg-surface-secondary), and never a fade.
        var backgrounds = await page.EvaluateAsync<string[]>(
            """
            () => {
                const trigger = document.querySelector("[data-testid='time-picker-readonly-card'] .tm-time-picker__trigger");
                const probe = document.createElement('span');
                probe.style.backgroundColor = 'var(--tm-bg-surface-secondary)';
                document.body.appendChild(probe);
                const colors = [getComputedStyle(trigger).backgroundColor, getComputedStyle(probe).backgroundColor];
                probe.remove();
                return colors;
            }
            """);
        Assert.AreEqual(backgrounds[1], backgrounds[0],
            $"the locked trigger paints the read-only surface token (trigger {backgrounds[0]}, token {backgrounds[1]})");

        // No horizontal overflow on the phone.
        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflow, "the read-only card must not stretch the page horizontally");

        await AssertNoAxeViolationsAsync(page, TimeReadOnlyCard, "read-only time picker card (phone)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task DateRangePicker_TriggerAriaExpanded_IsAString_InBothStates()
    {
        var page = await OpenPickersAsync();

        var picker = page.Locator(".tm-date-range-picker").First;
        var trigger = picker.Locator(".tm-date-range-trigger");
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"),
            "closed — the string \"false\", never an omitted attribute");
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-haspopup"));

        await trigger.ClickAsync();
        await Assertions.Expect(picker.Locator(".tm-date-range-popup")).ToBeVisibleAsync();
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        await AssertNoAxeViolationsAsync(page, ".tm-date-range-picker", "date range picker");

        // Dark axe pass on the same picker.
        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, ".tm-date-range-picker");
        await AssertNoAxeViolationsAsync(page, ".tm-date-range-picker", "date range picker (dark)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task MultiViewList_CardsToolbar_Triggers_AnnounceTheStringState()
    {
        var page = await OpenDataDisplayAsync();

        var sort = page.Locator($"{MvlToolbarCard} .tm-mvl-cards-toolbar__sort");
        Assert.AreEqual("dialog", await sort.GetAttributeAsync("aria-haspopup"));
        Assert.AreEqual("false", await sort.GetAttributeAsync("aria-expanded"),
            "closed — the string \"false\", never an omitted attribute");

        await sort.ClickAsync();
        await Assertions.Expect(sort).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.Locator(".tm-mvl-cards-sheet")).ToBeVisibleAsync();

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sort).ToHaveAttributeAsync("aria-expanded", "false");

        var filter = page.Locator($"{MvlToolbarCard} .tm-mvl-cards-toolbar__filter");
        Assert.AreEqual("false", await filter.GetAttributeAsync("aria-expanded"));

        await filter.ClickAsync();
        await Assertions.Expect(filter).ToHaveAttributeAsync("aria-expanded", "true");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(filter).ToHaveAttributeAsync("aria-expanded", "false");

        await AssertNoAxeViolationsAsync(page, MvlToolbarCard, "multi-view list cards toolbar");

        // Dark axe pass on the same card.
        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, MvlToolbarCard);
        await AssertNoAxeViolationsAsync(page, MvlToolbarCard, "multi-view list cards toolbar (dark)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Screenshots_ReadOnlyTimePicker_LightDarkAndPhone()
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "pickers");
        Directory.CreateDirectory(directory);

        var page = await OpenTimeInputAsync();
        await page.Locator(TimeReadOnlyCard).ScrollIntoViewIfNeededAsync();
        await CaptureCardAsync(page, Path.Combine(directory, "fze-4c-timepicker-readonly-light-1440.png"));

        await ToggleDarkModeAsync(page);
        await CaptureCardAsync(page, Path.Combine(directory, "fze-4c-timepicker-readonly-dark-1440.png"));

        // Dark axe pass on the same card.
        await AssertNoAxeViolationsAsync(page, TimeReadOnlyCard, "read-only time picker card (dark)");

        // Edge case: the phone layout — the trigger-only presentation carries the value.
        var phone = await OpenTimeInputAsync(390, 844, touch: true);
        await phone.Locator(TimeReadOnlyCard).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, TimeReadOnlyCard);
        await SetStickyHeaderVisibleAsync(phone, false);
        try
        {
            await phone.Locator(TimeReadOnlyCard).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(directory, "fze-4c-timepicker-readonly-light-390.png"),
            });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(phone, true);
        }

        AssertNoClientErrors();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The demo shell's sticky header would cover the top of the card in an element screenshot;
    /// hide it for the capture and restore it right after (same helper as in
    /// <see cref="AI.AiConversationHostDrivenE2ETests"/>).
    /// </summary>
    private static Task SetStickyHeaderVisibleAsync(IPage page, bool visible)
        => page.EvaluateAsync(
            """
            (visible) => {
                const header = document.querySelector('[data-demo-shell="main"] > .sticky');
                if (header) {
                    header.style.visibility = visible ? '' : 'hidden';
                }
            }
            """, visible);

    /// <summary>Captures the read-only card with the sticky header hidden and restored afterwards.</summary>
    private static async Task CaptureCardAsync(IPage page, string screenshotPath)
    {
        await WaitForLayoutToSettleAsync(page, TimeReadOnlyCard);
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            await page.Locator(TimeReadOnlyCard).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = screenshotPath,
            });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }
    }

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
