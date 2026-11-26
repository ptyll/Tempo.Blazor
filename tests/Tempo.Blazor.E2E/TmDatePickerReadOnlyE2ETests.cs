using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4 — a locked TmDatePicker/TmDateTimePicker trigger announces the lock instead
/// of advertising a popup it can never open (EmusCz 2.13.0 finding): with <c>ReadOnly</c> the
/// trigger carries <c>aria-haspopup="false"</c>, no <c>aria-expanded</c> and
/// <c>aria-disabled="true"</c> (<c>aria-readonly</c> is not a supported attribute on
/// <c>role=button</c>), stays focusable and does not open on click / Enter / Space. The editable
/// trigger keeps the popup contract. The scoped axe scan guards the ARIA attribute validity
/// (aria-allowed-attr) in both themes. Screenshots land in
/// <c>__screenshots__/pickers/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmDatePickerReadOnlyE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string DateCard = "[data-testid='date-picker-readonly-card']";
    private const string DateTimeCard = "[data-testid='datetime-picker-readonly-card']";

    private readonly List<string> _clientErrors = [];

    private async Task<IPage> OpenPageAsync(int width = 1440, int height = 1000)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/pickers");
        await page.Locator(DateCard).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    [TestMethod]
    public async Task ReadOnlyDatePicker_AnnouncesTheLock_NotAPopup()
    {
        var page = await OpenPageAsync();
        // Focus/click below scroll the card into view; html uses smooth scrolling, so scroll FIRST
        // and wait for the settled state — focusing an off-screen element starts another smooth
        // scroll and a click landing during that animation hits stale coordinates (body).
        var trigger = page.Locator($"{DateCard} .tm-date-picker-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, DateCard);
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-haspopup"),
            "a locked trigger has no popup to advertise");
        Assert.IsNull(await trigger.GetAttributeAsync("aria-expanded"),
            "aria-expanded on a button that opens nothing announces a collapsed expandable control");
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-disabled"),
            "the lock is announced as disabled — aria-readonly is not valid on role=button");
        Assert.IsNull(await trigger.GetAttributeAsync("aria-readonly"));
        Assert.IsNull(await trigger.GetAttributeAsync("disabled"),
            "aria-disabled names the state; the trigger stays focusable and readable");

        // The trigger keeps its natural tab stop and takes focus.
        await trigger.FocusAsync();
        var focused = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList?.contains('tm-date-picker-trigger') === true");
        Assert.IsTrue(focused, "the locked trigger stays keyboard reachable");

        // Click / Enter / Space do not open the calendar. The click is FORCED: Playwright's
        // actionability check already refuses to click an aria-disabled button ("element is not
        // enabled") — itself proof the lock is announced — so the force-click exercises the
        // component's own guard instead of the browser's.
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Space");
        Assert.AreEqual(0, await page.Locator($"{DateCard} .tm-calendar").CountAsync(),
            "no activation path opens the locked calendar");

        // None of the activation attempts moved the focus: the locked trigger still owns it
        // (aria-disabled, not the native disabled attribute, so the browser never drops focus).
        Assert.AreEqual("OK", await TriggerFocusStateAsync(page, DateCard), "after Enter/Space the locked date trigger must still be document.activeElement");

        // The demo card describes WHY the field is locked through AriaDescribedBy.
        StringAssert.Contains(await trigger.GetAttributeAsync("aria-describedby") ?? string.Empty,
            "date-readonly-reason");

        await AssertNoAxeViolationsAsync(page, DateCard, "read-only date picker card");

        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task ReadOnlyDateTimePicker_AnnouncesTheLock_NotAPopup()
    {
        var page = await OpenPageAsync();
        // See the date test: scroll the card into view and let the smooth scroll settle BEFORE any
        // focus/click, or a click during the animation lands on stale coordinates (body).
        var trigger = page.Locator($"{DateTimeCard} .tm-date-picker-trigger");
        await trigger.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, DateTimeCard);
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-haspopup"));
        Assert.IsNull(await trigger.GetAttributeAsync("aria-expanded"));
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-disabled"));
        Assert.IsNull(await trigger.GetAttributeAsync("aria-readonly"));
        Assert.IsNull(await trigger.GetAttributeAsync("disabled"));

        // The nested time field is read-only too, and the clear button is hidden.
        StringAssert.Contains(
            await page.Locator($"{DateTimeCard} .tm-time-input").GetAttributeAsync("class"),
            "tm-time-input--readonly");
        Assert.AreEqual(0, await page.Locator($"{DateTimeCard} .tm-picker-clear").CountAsync());

        // The demo card describes WHY the field is locked through AriaDescribedBy.
        StringAssert.Contains(await trigger.GetAttributeAsync("aria-describedby") ?? string.Empty,
            "datetime-readonly-reason");

        // The trigger keeps its natural tab stop and takes focus.
        await trigger.FocusAsync();
        Assert.AreEqual("OK", await TriggerFocusStateAsync(page, DateTimeCard), "the locked datetime trigger stays keyboard reachable");

        // No activation path opens the locked calendar (the click is forced — Playwright already
        // refuses to click an aria-disabled button, see the date test).
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Space");
        await page.Keyboard.PressAsync("Alt+ArrowDown");
        Assert.AreEqual(0, await page.Locator($"{DateTimeCard} .tm-calendar").CountAsync(),
            "no activation path opens the locked calendar");

        Assert.AreEqual("OK", await TriggerFocusStateAsync(page, DateTimeCard), "after click/Enter/Space/Alt+ArrowDown the locked datetime trigger must still be document.activeElement");

        await AssertNoAxeViolationsAsync(page, DateTimeCard, "read-only datetime picker card");

        AssertNoClientErrors();
    }

    /// <summary>
    /// The locked trigger reads like every other read-only input: the SAME computed background as
    /// the read-only time input sitting next to it in the datetime card (both are painted by
    /// <c>--tm-bg-surface-secondary</c>), and never a fade. Asserted in light AND dark.
    /// </summary>
    [TestMethod]
    public async Task ReadOnlyTriggers_PaintTheReadOnlySurface_InBothThemes()
    {
        var page = await OpenPageAsync();

        await AssertLockedSurfaceMatchesTimeInputAsync(page, $"{DateCard} .tm-date-picker-trigger");
        await AssertLockedSurfaceMatchesTimeInputAsync(page, $"{DateTimeCard} .tm-date-picker-trigger");

        await ToggleDarkModeAsync(page);
        await AssertLockedSurfaceMatchesTimeInputAsync(page, $"{DateCard} .tm-date-picker-trigger");
        await AssertLockedSurfaceMatchesTimeInputAsync(page, $"{DateTimeCard} .tm-date-picker-trigger");

        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task EditableDatePicker_KeepsThePopupContract()
    {
        var page = await OpenPageAsync();

        // The basic (editable) card is the first date picker on the page.
        var picker = page.Locator(".tm-date-picker").First;
        var trigger = picker.Locator(".tm-date-picker-trigger");
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-haspopup"));
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));
        Assert.IsNull(await trigger.GetAttributeAsync("aria-disabled"));
        Assert.IsNull(await trigger.GetAttributeAsync("aria-readonly"));

        await trigger.ClickAsync();
        await picker.Locator(".tm-calendar").WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "true");

        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(picker.Locator(".tm-calendar")).ToHaveCountAsync(0);
        await Assertions.Expect(trigger).ToHaveAttributeAsync("aria-expanded", "false");

        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Screenshots_ReadOnlyCards_LightDarkAndPhone()
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "pickers");
        Directory.CreateDirectory(directory);

        var page = await OpenPageAsync();
        await page.Locator(DateCard).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, DateCard);
        await page.Locator(DateCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4-datepicker-readonly-light-1440.png"),
        });
        await page.Locator(DateTimeCard).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, DateTimeCard);
        await page.Locator(DateTimeCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4-datetimepicker-readonly-light-1440.png"),
        });

        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, DateCard);
        await page.Locator(DateCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4-datepicker-readonly-dark-1440.png"),
        });
        await page.Locator(DateTimeCard).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, DateTimeCard);
        await page.Locator(DateTimeCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4-datetimepicker-readonly-dark-1440.png"),
        });

        // Edge case: the phone width the EmusCz panel lives at.
        var phone = await OpenPageAsync(390, 844);
        await phone.Locator(DateCard).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, DateCard);
        await phone.Locator(DateCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4-datepicker-readonly-light-390.png"),
        });

        // No horizontal overflow on the phone — the locked trigger fits its card.
        var overflow = await phone.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflow, "the read-only cards must not stretch the page horizontally");

        await AssertNoAxeViolationsAsync(phone, DateCard, "read-only date picker card (phone)");

        AssertNoClientErrors();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    /// <summary>"OK" when document.activeElement IS the locked trigger inside the given demo card,
    /// otherwise a diagnostic describing what owns the focus instead.</summary>
    private static Task<string> TriggerFocusStateAsync(IPage page, string card) =>
        page.EvaluateAsync<string>(
            """
            (card) => {
                const trigger = document.querySelector(card + " .tm-date-picker-trigger");
                const active = document.activeElement;
                if (trigger !== null && active === trigger) return "OK";
                const describe = (el) => el ? `${el.tagName}.${String(el.className).slice(0, 80)}` : "null";
                return `active=${describe(active)} trigger=${describe(trigger)}`;
            }
            """,
            card);

    /// <summary>
    /// The locked trigger's computed background must equal the read-only time input's in the
    /// datetime card — one token (<c>--tm-bg-surface-secondary</c>), one surface reading.
    /// </summary>
    private static async Task AssertLockedSurfaceMatchesTimeInputAsync(IPage page, string triggerSelector)
    {
        var colors = await page.EvaluateAsync<string[]>(
            """
            (triggerSelector) => {
                const trigger = document.querySelector(triggerSelector);
                const time = document.querySelector("[data-testid='datetime-picker-readonly'] .tm-time-input--readonly");
                if (!trigger || !time) return ["missing", "missing"];
                return [getComputedStyle(trigger).backgroundColor, getComputedStyle(time).backgroundColor];
            }
            """,
            triggerSelector);
        Assert.AreNotEqual("missing", colors[0], "both the locked trigger and the read-only time input must render");
        Assert.AreEqual(colors[1], colors[0],
            $"the locked trigger must paint the read-only surface token (trigger {colors[0]}, " +
            $"time input {colors[1]}) — a mismatch means the surface drifted from the other read-only inputs");
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
