using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Feedback;

/// <summary>
/// Review round 5, against real hosts: a drawer with SwipeToDismiss="false" must survive a fast
/// downward flick, an icon-only TmButton must keep its exact content width (the empty label span
/// used to add a flex gap), a prompt dialog must type straight into its input, a short confirm must
/// start on Cancel with no content tab stop, and an overflowing dialog scroller must become a
/// keyboard-reachable region the Tab key can reach without ever taking initial focus.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class OverlayRound5E2ETests : WasmTestBase
{
    private static readonly string ShotDir =
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResults", "overlay-round5");

    private async Task<IPage> OpenAsync(int width, int height = 1000)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            HasTouch = width < 1024,
            ViewportSize = new ViewportSize { Width = width, Height = height },
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        return page;
    }

    private async Task GotoAsync(IPage page, string path)
    {
        await page.GotoAsync($"{BaseUrl}{path}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 90000 });
        await WaitForAppReadyAsync(page);
    }

    private static async Task<string> ShootAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        var path = Path.Combine(ShotDir, $"{name}.png");
        // FullPage resets touch emulation; a viewport shot keeps (pointer: coarse) honest.
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
            Timeout = 60_000,
        });
        return path;
    }

    private static async Task<string> ActiveElementAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(
            """
            () => {
                const a = document.activeElement;
                if (!a) return '(null)';
                return a.tagName.toLowerCase() + '.' + (a.className || '').toString().trim().split(/\s+/).join('.');
            }
            """);
    }

    [TestMethod]
    public async Task LockedDrawer_AFastDownwardFlick_DoesNotDismiss()
    {
        var page = await OpenAsync(390, 844);
        await GotoAsync(page, "/modal-dialog");

        await page.GetByTestId("open-inline-drawer-locked").ClickAsync();
        var panel = page.Locator(".tm-drawer__panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(700);

        var handle = page.Locator(".tm-drawer .tm-sheet__handle").First;
        var box = await handle.BoundingBoxAsync();
        Assert.IsNotNull(box, "the locked inline drawer must render its grabber");
        var x = box.X + box.Width / 2;
        var y = box.Y + box.Height / 2;

        // A fast two-step flick downward (~4 px/ms over the tail) — the gesture that dismissed a
        // SwipeToDismiss=false drawer before the settle() gate.
        await page.Mouse.MoveAsync(x, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, y + 20, new MouseMoveOptions { Steps = 1 });
        await page.Mouse.MoveAsync(x, y + 100, new MouseMoveOptions { Steps = 1 });
        await page.WaitForTimeoutAsync(16);
        await page.Mouse.MoveAsync(x, y + 170, new MouseMoveOptions { Steps = 1 });
        await page.Mouse.UpAsync();

        await page.WaitForTimeoutAsync(800);
        await Assertions.Expect(panel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

        var state = await page.EvaluateAsync<string>(
            """
            () => {
                const drawer = document.querySelector('.tm-drawer');
                const panel = document.querySelector('.tm-drawer__panel');
                return JSON.stringify({
                    drawer: !!drawer,
                    panelHeight: panel ? Math.round(panel.getBoundingClientRect().height) : 0,
                });
            }
            """);
        TestContext.WriteLine($"locked-drawer-after-flick {state}");
        Assert.IsTrue(state.Contains("\"drawer\":true"), $"the locked drawer must survive the flick: {state}");
        Assert.IsTrue(!state.Contains("\"panelHeight\":0"), $"the drawer panel must keep its height: {state}");

        await ShootAsync(page, "390-locked-drawer-after-flick");
    }

    [TestMethod]
    public async Task IconOnlyButtons_KeepTheirWidth_WithoutTheLabelGap()
    {
        var page = await OpenAsync(1440);
        await GotoAsync(page, "/layout");

        // An icon-only TmButton is exactly padding + icon wide. The empty label span used to sit in
        // the button's flex gap and widen every icon-only button by 8px (42 -> 50 at sm).
        var report = await page.EvaluateAsync<string>(
            """
            () => {
                const iconOnly = [...document.querySelectorAll('button.tm-btn')]
                    .filter(b => b.querySelector('svg') && !b.textContent.trim())
                    .map(b => {
                        const r = b.getBoundingClientRect();
                        const svg = b.querySelector('svg').getBoundingClientRect();
                        const cs = getComputedStyle(b);
                        const content = parseFloat(cs.paddingLeft) + parseFloat(cs.paddingRight) + svg.width;
                        return { w: +r.width.toFixed(1), content: +content.toFixed(1) };
                    });
                return JSON.stringify(iconOnly);
            }
            """);
        TestContext.WriteLine($"icon-only buttons {report}");
        var boxes = JsonSerializer.Deserialize<JsonElement[]>(report);
        Assert.IsTrue(boxes.Length >= 1, $"expected at least one icon-only TmButton on /layout, got: {report}");
        foreach (var box in boxes)
        {
            var w = box.GetProperty("w").GetDouble();
            var content = box.GetProperty("content").GetDouble();
            Assert.IsTrue(Math.Abs(w - content) <= 1.5,
                $"an icon-only TmButton must be exactly padding + icon wide ({content}px); an empty label span adds a flex gap and widens it: {report}");
        }

        await ShootAsync(page, "1440-icon-only-width");
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task PromptDialog_FocusesTheInput_AndEnterSubmits(int width)
    {
        var page = await OpenAsync(width);
        await GotoAsync(page, "/modal-dialog");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Enter Name" }).ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.tagName === 'INPUT' && !!document.activeElement.closest('.tm-dialog-content')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var active = await ActiveElementAsync(page);
        TestContext.WriteLine($"prompt {width}: initial focus {active}");
        Assert.IsTrue(active.Contains("tm-dialog-input"),
            $"a prompt dialog must focus its input, got '{active}'");

        await page.Keyboard.TypeAsync("Pavel");
        await page.Keyboard.PressAsync("Enter");
        await page.Locator(".tm-modal-overlay").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });
        await Assertions.Expect(page.GetByText("Pavel").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

        await ShootAsync(page, $"{width}-prompt-enter-submits");
    }

    [TestMethod]
    [DataRow(390)]
    [DataRow(1440)]
    public async Task ShortConfirmDialog_StartsOnCancel_AndHasNoContentTabStop(int width)
    {
        var page = await OpenAsync(width);
        await GotoAsync(page, "/modal-dialog");

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Show Confirm" }).ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.classList.contains('tm-dialog-btn-cancel')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        // Shift+Tab from Cancel wraps inside the trap. With no content tab stop the previous
        // focusable is the OK button (the list is [cancel, ok]); a hardcoded tabindex=0 on the
        // scroller would park the wrap on the title block instead.
        await page.Keyboard.PressAsync("Shift+Tab");
        var afterShiftTab = await ActiveElementAsync(page);
        TestContext.WriteLine($"confirm {width}: after Shift+Tab focus {afterShiftTab}");
        Assert.IsTrue(afterShiftTab.Contains("tm-dialog-btn-ok"),
            $"Shift+Tab from Cancel must wrap to OK, proving the scroller is not a tab stop: got '{afterShiftTab}'");

        await ShootAsync(page, $"{width}-short-confirm-focus");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-modal-overlay").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 10000 });
    }

    [TestMethod]
    public async Task LongDialog_OverflowTurnsTheContentIntoARegion_TabReachesIt_AndArrowScrolls()
    {        var page = await OpenAsync(1440);
        await GotoAsync(page, "/modal-dialog");

        await page.GetByTestId("open-long-dialog").ClickAsync();
        await page.Locator(".tm-dialog").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Initial focus is the footer Cancel, never the region — even when it overflows.
        await page.WaitForFunctionAsync(
            "() => document.activeElement && document.activeElement.classList.contains('tm-dialog-btn-cancel')",
            null,
            new PageWaitForFunctionOptions { Timeout = 15000 });

        var overflow = await page.EvaluateAsync<string>(
            """
            () => {
                const c = document.querySelector('.tm-dialog-content');
                return JSON.stringify({
                    scrollH: c.scrollHeight, clientH: c.clientHeight,
                    tabindex: c.getAttribute('tabindex'), role: c.getAttribute('role'),
                    labelledby: c.getAttribute('aria-labelledby'),
                });
            }
            """);
        TestContext.WriteLine($"long dialog content {overflow}");
        Assert.IsTrue(overflow.Contains("\"tabindex\":\"0\""),
            $"a long dialog's scroller must become keyboard-reachable: {overflow}");
        Assert.IsTrue(overflow.Contains("\"role\":\"region\""), $"the overflowing scroller is a named region: {overflow}");
        var labelledBy = JsonDocument.Parse(overflow).RootElement.GetProperty("labelledby").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(labelledBy), $"the region is named by its title: {overflow}");
        Assert.AreEqual(labelledBy,
            await page.Locator(".tm-dialog-title").GetAttributeAsync("id"),
            "the region's aria-labelledby points at the title");

        // Tab reaches the region; ArrowDown scrolls it.
        await page.Keyboard.PressAsync("Tab");
        var regionFocus = await page.EvaluateAsync<string>(
            "() => document.activeElement.classList.contains('tm-dialog-content') ? 'region' : document.activeElement.className");
        Assert.AreEqual("region", regionFocus, $"Tab must reach the overflowing scroller, got '{regionFocus}'");

        var before = await page.EvaluateAsync<double>("() => document.querySelector('.tm-dialog-content').scrollTop");
        for (var i = 0; i < 3; i++)
        {
            await page.Keyboard.PressAsync("ArrowDown");
            await page.WaitForTimeoutAsync(80);
        }
        var after = await page.EvaluateAsync<double>("() => document.querySelector('.tm-dialog-content').scrollTop");
        Assert.IsTrue(after > before, $"ArrowDown must scroll the focused region (scrollTop {before} -> {after})");

        await ShootAsync(page, "1440-long-dialog-region-scroll");
        await page.Keyboard.PressAsync("Escape");
    }

    [TestMethod]
    [DataRow(320)]
    [DataRow(375)]
    [DataRow(390)]
    public async Task SheetFooter_RealisticLabelsAreNotTruncated_AndShortPairsStaySideBySide(int width)
    {
        var page = await OpenAsync(width, 844);
        await GotoAsync(page, "/modal-dialog");

        await page.GetByTestId("open-sheet").ClickAsync();
        await page.Locator(".tm-modal-overlay").WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(700);

        // Realistic ≤30-char labels from the FR and CS resources — the ones a forced 50/50 split
        // used to cut in half before the footer ever got to wrap.
        await page.EvaluateAsync(
            """
            () => {
                const labels = document.querySelectorAll('.tm-modal-overlay .tm-modal-footer .tm-btn .tm-btn-label');
                labels[0].textContent = 'Enregistrer les modifications';
                labels[1].textContent = 'Uložit změny a zavřít';
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var report = await ReadLabelMetricsAsync(page);
        TestContext.WriteLine($"sheet footer {width}: {report}");
        foreach (var label in JsonDocument.Parse(report).RootElement.GetProperty("labels").EnumerateArray())
        {
            Assert.IsTrue(label.GetProperty("scroll").GetDouble() <= label.GetProperty("client").GetDouble() + 1,
                $"a realistic ≤30-char label must NOT truncate at {width}px — it wraps to its own row instead: {report}");
        }

        // The default short pair stays side by side on one row.
        await page.EvaluateAsync(
            """
            () => {
                const labels = document.querySelectorAll('.tm-modal-overlay .tm-modal-footer .tm-btn .tm-btn-label');
                labels[0].textContent = 'Cancel';
                labels[1].textContent = 'OK';
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var pair = await ReadLabelMetricsAsync(page);
        using (var doc = JsonDocument.Parse(pair))
        {
            var buttons = doc.RootElement.GetProperty("buttons");
            var rows = buttons.EnumerateArray().Select(b => b.GetProperty("top").GetInt32()).Distinct().ToList();
            Assert.AreEqual(1, rows.Count, $"a short Cancel/OK pair must stay side by side at {width}px: {pair}");
            Assert.IsTrue(doc.RootElement.GetProperty("overflow").GetBoolean() == false,
                $"the short pair must not overflow the footer at {width}px: {pair}");
        }
        await ShootAsync(page, $"{width}-short-pair-side-by-side");

        // A 61-char label is wider than the whole row: that one, and only that one, truncates.
        await page.EvaluateAsync(
            """
            () => {
                const labels = document.querySelectorAll('.tm-modal-overlay .tm-modal-footer .tm-btn .tm-btn-label');
                labels[0].textContent = 'Supprimer définitivement cet élément et toutes ses pièces jointes associées';
                labels[1].textContent = 'OK';
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var longReport = await ReadLabelMetricsAsync(page);
        using (var doc = JsonDocument.Parse(longReport))
        {
            var labels = doc.RootElement.GetProperty("labels").EnumerateArray().ToList();
            Assert.IsTrue(labels[0].GetProperty("scroll").GetDouble() > labels[0].GetProperty("client").GetDouble(),
                $"a 61-char label is wider than the whole row and must truncate: {longReport}");
            Assert.IsTrue(labels[1].GetProperty("scroll").GetDouble() <= labels[1].GetProperty("client").GetDouble() + 1,
                $"the short label next to it must not truncate: {longReport}");
        }
        await ShootAsync(page, $"{width}-61char-label-truncates");

        await page.Keyboard.PressAsync("Escape");
    }

    private static async Task<string> ReadLabelMetricsAsync(IPage page)
    {
        return await page.EvaluateAsync<string>(
            """
            () => {
                const footer = document.querySelector('.tm-modal-overlay .tm-modal-footer, .tm-modal-overlay .tm-dialog-footer');
                return JSON.stringify({
                    overflow: footer.scrollWidth > footer.clientWidth + 1,
                    buttons: [...footer.querySelectorAll('.tm-btn')].map(b => {
                        const r = b.getBoundingClientRect();
                        return { top: Math.round(r.top), width: Math.round(r.width) };
                    }),
                    labels: [...footer.querySelectorAll('.tm-btn-label')].map(l => ({
                        scroll: l.scrollWidth, client: l.clientWidth,
                    })),
                });
            }
            """);
    }
}
