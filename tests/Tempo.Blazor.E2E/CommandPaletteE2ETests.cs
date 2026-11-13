using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TG-010: TmCommandPalette entity search over the async SearchProvider — opening, debounced
/// typing, section grouping, keyboard navigation + Enter, the custom empty state, transient
/// failure with retry, Escape during loading, ellipsis clipping of very long titles/codes, the
/// 200-row scroll case, rapid typing/deleting, the cs culture, the 390 px layout with the footer
/// hidden on a coarse pointer, and dark mode. Screenshots land in
/// <c>tests/Tempo.Blazor.E2E/TestResults/command-palette/</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class CommandPaletteE2ETests : WasmTestBase
{
    private const string LayoutPage = "/layout";

    private static string ScreenshotDirectory()
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "command-palette");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private async Task<string> SaveScreenshotAsync(IPage page, string name)
    {
        var directory = ScreenshotDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, Path = path, Timeout = 60_000 });
        TestContext.AddResultFile(path);
        return path;
    }

    private async Task<IPage> OpenLayoutPageAsync(int width = 1440, int height = 900, bool hasTouch = false, string culture = "en")
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = culture == "en" ? "en-US" : culture,
            HasTouch = hasTouch,
            IsMobile = hasTouch,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            $"localStorage.setItem('tm-demo-culture', '{culture}'); document.cookie = 'tm-demo-culture={culture};path=/;max-age=31536000;samesite=lax';");
        await GotoAppReadyAsync(page, $"{BaseUrl}{LayoutPage}");
        return page;
    }

    private static async Task OpenEntityPaletteAsync(IPage page)
    {
        await page.Locator("[data-testid='command-palette-entity-open']").ClickAsync();
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
    }

    private static async Task TypeQueryAsync(IPage page, string query)
    {
        var input = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input");
        await input.FillAsync(string.Empty);
        await input.PressSequentiallyAsync(query, new LocatorPressSequentiallyOptions { Delay = 30 });
    }

    // ── Core flow: debounce, sections, badge/code, footer ────────────────────

    [TestMethod]
    [Description("TG-010: debounced typing shows local commands first, then server results in category sections with badge/code and the footer")]
    public async Task EntitySearch_1440_Light_ShowsSections_Badge_Code_And_Footer()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);

        // One rapid burst — the palette debounces it into a single server request. "entity" matches
        // the local "New entity" command AND the server's long-title requirement row.
        await TypeQueryAsync(page, "entity");

        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
                new PageLocatorOptions { HasText = "exceptionally long display title" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var panel = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette");
        // Local commands come first (O3), server results follow.
        var titles = await panel.Locator(".tm-command-palette-title").AllInnerTextsAsync();
        titles[0].Trim().Should().Be("New entity");
        titles.Should().Contain(t => t.Contains("exceptionally long display title"));

        // Sections via Category.
        var labels = await panel.Locator(".tm-command-palette-group-label").AllInnerTextsAsync();
        labels.Select(l => l.Trim().ToLowerInvariant()).Should().Contain(["commands", "requirements"]); // CSS uppercases the label

        // Badge + code chips in the trailing slot.
        var item = panel.Locator(".tm-command-palette-item", new LocatorLocatorOptions { HasText = "exceptionally long display title" });
        (await item.Locator(".tm-command-palette-trailing .tm-badge").InnerTextAsync()).Trim().Should().Be("REQ");
        (await item.Locator(".tm-command-palette-code").InnerTextAsync()).Trim().Should().StartWith("REQ-209");
        var local = panel.Locator(".tm-command-palette-item", new LocatorLocatorOptions { HasText = "New entity" });
        (await local.Locator(".tm-command-palette-code").InnerTextAsync()).Trim().Should().Be("CMD-NEW");

        // Keyboard-hint footer with kbd chips.
        var kbds = await panel.Locator(".tm-command-palette-footer kbd").AllInnerTextsAsync();
        kbds.Select(k => k.Trim()).Should().Contain(["↑", "↓", "↵", "Esc"]);

        await SaveScreenshotAsync(page, "entity-results-1440-light");
    }

    [TestMethod]
    [Description("TG-010: arrow keys move the highlight and Enter executes the action (toast) and closes the palette")]
    public async Task Keyboard_Arrows_And_Enter_Execute()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "uc-042");
        var item = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
            new PageLocatorOptions { HasText = "Command palette async source" });
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var input = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input");
        await input.PressAsync("ArrowDown");
        await input.PressAsync("ArrowUp");
        await input.PressAsync("Enter");

        await page.Locator(".tm-toast-container, [class*='toast']", new PageLocatorOptions { HasText = "Selected UC-042" })
            .First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });
    }

    [TestMethod]
    [Description("TG-010: a query with no matches renders the host empty template with the query")]
    public async Task Empty_Query_Renders_The_Custom_Empty_State()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "zzzz-nothing");
        var empty = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-empty-slot");
        await empty.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        (await empty.InnerTextAsync()).Should().Contain("zzzz-nothing");

        await SaveScreenshotAsync(page, "entity-empty-1440-light");
    }

    [TestMethod]
    [Description("TG-010: with random failures on, a transient error renders inline with a retry; retry eventually recovers")]
    public async Task Transient_Error_Renders_Inline_And_Retry_Recovers()
    {
        var page = await OpenLayoutPageAsync();
        await page.Locator("[data-testid='entity-fail-toggle'] input").CheckAsync();
        await OpenEntityPaletteAsync(page);

        ILocator? error = null;
        for (var attempt = 0; attempt < 8 && error is null; attempt++)
        {
            await TypeQueryAsync(page, "fail");
            try
            {
                var found = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-error[role='alert']");
                await found.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                error = found;
            }
            catch (TimeoutException)
            {
                // This round succeeded (or the debounce landed late) — try again.
            }
        }

        error.Should().NotBeNull("with the failure toggle on, a query containing \"fail\" always produces a transient error");
        await SaveScreenshotAsync(page, "entity-error-1440-light");

        // The component never retries on its own — the user does. The host's failure switch is
        // flipped off (dispatched, the modal backdrop covers the real control) so the retry succeeds;
        // "fail" matches no entity, so a healthy server answers with the Empty state, not an error.
        await page.Locator("[data-testid='entity-fail-toggle'] input").DispatchEventAsync("click");
        // Keyboard path: the retry is a native button outside the arrow-key navigation, so Tab
        // reaches it (input -> close -> retry) and Enter is its own activation. After the retry the
        // alert (and the focused button) unmounts — focus must land on the search input, not <body>.
        for (var tab = 0; tab < 4; tab++)
        {
            if (await page.EvaluateAsync<bool>("() => document.activeElement?.classList.contains('tm-command-palette-retry') === true"))
            {
                break;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        (await page.EvaluateAsync<bool>("() => document.activeElement?.classList.contains('tm-command-palette-retry') === true"))
            .Should().BeTrue("Tab reaches the retry button");
        await page.Keyboard.PressAsync("Enter");
        var empty = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-empty-slot");
        await empty.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
        (await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-error").CountAsync())
            .Should().Be(0, "a recovered retry replaces the inline error");
        await Assertions.Expect(page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input")).ToBeFocusedAsync();
        (await page.EvaluateAsync<bool>("() => document.activeElement?.classList.contains('tm-command-palette-input') === true"))
            .Should().BeTrue("after the retry unmounts the alert, focus moves to the search input");
    }

        [TestMethod]
    [Description("TG-010: Escape while the server round trip is still running closes the palette and leaves no stale state on reopen")]
    public async Task Escape_During_Loading_Closes_And_Reopen_Is_Fresh()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);

        var input = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input");
        await input.PressSequentiallyAsync("invoice", new LocatorPressSequentiallyOptions { Delay = 30 });
        // The 600 ms simulated round trip is in flight — the loading rows show, Escape closes.
        var loading = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-loading");
        try
        {
            await loading.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 2_000 });
            await SaveScreenshotAsync(page, "entity-loading-1440-light");
        }
        catch (TimeoutException)
        {
            // The round trip finished before we looked — the close assertion below still holds.
        }

        await input.PressAsync("Escape");
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10_000 });

        await OpenEntityPaletteAsync(page);
        var value = await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input")
            .InputValueAsync();
        value.Should().Be(string.Empty, "reopening starts with a fresh query and no stale results");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [TestMethod]
    [Description("TG-010: a very long title and code clip with an ellipsis instead of overflowing the panel")]
    public async Task Long_Title_And_Code_Clip_Without_Overflow()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "exceptionally");
        var item = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
            new PageLocatorOptions { HasText = "exceptionally long display title" });
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await SaveScreenshotAsync(page, "entity-long-title-1440-light");

        var overflows = await page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector('[data-testid="command-palette-entity"] .tm-command-palette');
                const clipped = [...panel.querySelectorAll('.tm-command-palette-title, .tm-command-palette-code')];
                return clipped.some(el => el.scrollWidth > el.clientWidth + 1 && getComputedStyle(el).textOverflow !== 'ellipsis')
                    || panel.scrollWidth > panel.clientWidth + 1;
            }
            """);
        overflows.Should().BeFalse("long titles/codes must clip, never push the panel wider");
    }

    [TestMethod]
    [Description("TG-010: the 200-row bulk case renders inside the scrolling list without breaking the layout")]
    public async Task Bulk_200_Rows_Scroll_Inside_The_List()
    {
        var page = await OpenLayoutPageAsync();
        await page.Locator("[data-testid='entity-bulk-toggle'] input").CheckAsync();
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "bulk");
        var first = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item", new PageLocatorOptions { HasText = "Bulk attachment row number 1 " })
            .First;
        await first.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        var stats = await page.EvaluateAsync<JsonBulkStats>(
            """
            () => {
                const list = document.querySelector('[data-testid="command-palette-entity"] .tm-command-palette-list');
                return { count: list.querySelectorAll('.tm-command-palette-item').length,
                         scrollable: list.scrollHeight > list.clientHeight + 1 };
            }
            """);
        stats.Count.Should().BeGreaterThanOrEqualTo(150, "the bulk toggle contributes ~200 rows");
        stats.Scrollable.Should().BeTrue("the list caps its height and scrolls");

        await SaveScreenshotAsync(page, "entity-bulk-200-1440-light");
    }

    [TestMethod]
    [Description("TG-010: rapid typing and deleting recovers to the local command list without errors")]
    public async Task Rapid_Typing_And_Deleting_Recovers()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);
        var input = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input");

        await input.PressSequentiallyAsync("uc-042", new LocatorPressSequentiallyOptions { Delay = 25 });
        for (var i = 0; i < 6; i++)
        {
            await input.PressAsync("Backspace");
        }

        // Empty query with MinChars=0 fires the provider for the empty query; the local commands
        // render either way and the component must be intact.
        var panel = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette");
        await panel.Locator(".tm-command-palette-item", new LocatorLocatorOptions { HasText = "New entity" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
    }

    [TestMethod]
    [Description("TG-010: an item without an icon keeps its title aligned with the icon-carrying items (empty icon slot)")]
    public async Task Item_Without_Icon_Keeps_Title_Alignment()
    {
        var page = await OpenLayoutPageAsync();
        await OpenEntityPaletteAsync(page);
        // Icon-less only: the one visible item has no icon, so the slot is dropped altogether.
        await TypeQueryAsync(page, "diagram canvas");
        var only = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
            new PageLocatorOptions { HasText = "Diagram canvas colors" });
        await only.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        (await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-icon").CountAsync())
            .Should().Be(0, "no visible item has an icon, so no dead gutter is rendered");

        // Mixed: icon-carrying and icon-less decisions together keep the (empty) slot for alignment.
        await TypeQueryAsync(page, "DEC-00");
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-icon svg").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        var withoutIcon = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
            new PageLocatorOptions { HasText = "Diagram canvas colors" });
        await withoutIcon.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var hasSlot = await withoutIcon.Locator(".tm-command-palette-icon").CountAsync();
        hasSlot.Should().Be(1, "the empty icon slot still renders so the title aligns with icon-carrying items");
        var hasSvg = await withoutIcon.Locator(".tm-command-palette-icon svg").CountAsync();
        hasSvg.Should().Be(0);

        var aligned = await page.EvaluateAsync<bool>(
            """
            () => {
                const items = [...document.querySelectorAll('[data-testid="command-palette-entity"] .tm-command-palette-item')];
                if (items.length < 2) return true;
                const firstTitle = (item) => {
                    const title = item.querySelector('.tm-command-palette-title');
                    return title ? title.getBoundingClientRect().left : 0;
                };
                const left = firstTitle(items[0]);
                return items.every(i => Math.abs(firstTitle(i) - left) < 1.5);
            }
            """);
        aligned.Should().BeTrue("titles of icon-less and icon-carrying items must share the same left edge");
    }

    // ── Culture, width, pointer, theme ───────────────────────────────────────

    [TestMethod]
    [Description("TG-010: the cs culture localizes the palette chrome (placeholder, footer hints)")]
    public async Task Czech_Culture_Localizes_The_Chrome()
    {
        var page = await OpenLayoutPageAsync(culture: "cs");
        await OpenEntityPaletteAsync(page);

        var placeholder = await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input")
            .GetAttributeAsync("placeholder");
        placeholder.Should().Be("Zadejte příkaz nebo hledejte...");

        var footer = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-footer");
        var footerText = await footer.InnerTextAsync();
        footerText.Should().Contain("Navigovat").And.Contain("Vybrat").And.Contain("Zavřít");

        await SaveScreenshotAsync(page, "entity-results-1440-cs-light");
    }

    [TestMethod]
    [Description("TG-010: at 390 px the panel fits the viewport and the footer still shows for a fine pointer")]
    public async Task Width_390_FinePointer_Footer_Visible()
    {
        var page = await OpenLayoutPageAsync(390, 844);
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "uc-042");
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
                new PageLocatorOptions { HasText = "Command palette async source" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var footerVisible = await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-footer").IsVisibleAsync();
        footerVisible.Should().BeTrue("a fine pointer keeps the keyboard-hint footer even at 390 px");

        var fits = await page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector('[data-testid="command-palette-entity"] .tm-command-palette');
                const r = panel.getBoundingClientRect();
                return r.left >= -1 && r.right <= window.innerWidth + 1;
            }
            """);
        fits.Should().BeTrue("the panel must not overflow a 390 px viewport");

        await SaveScreenshotAsync(page, "entity-results-390-light");
    }

    [TestMethod]
    [Description("TG-010: on a coarse pointer the footer hides (CSS pointer: coarse)")]
    public async Task Width_390_CoarsePointer_Footer_Hidden()
    {
        var page = await OpenLayoutPageAsync(390, 844, hasTouch: true);
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "uc-042");
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
                new PageLocatorOptions { HasText = "Command palette async source" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        var footerVisible = await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-footer").IsVisibleAsync();
        footerVisible.Should().BeFalse("a coarse pointer hides the keyboard-hint footer via CSS");

        await SaveScreenshotAsync(page, "entity-results-390-coarse-light");
    }

    [TestMethod]
    [Description("TG-010: dark mode renders the sections, chips and footer with the dark tokens")]
    public async Task EntitySearch_1440_Dark()
    {
        var page = await OpenLayoutPageAsync();
        await ToggleDarkModeAsync(page);
        await OpenEntityPaletteAsync(page);
        await TypeQueryAsync(page, "uc-04");
        await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
                new PageLocatorOptions { HasText = "Command palette async source" })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });

        await SaveScreenshotAsync(page, "entity-results-1440-dark");

        var errorShotTaken = false;
        try
        {
            await page.Locator("[data-testid='entity-fail-toggle'] input").CheckAsync();
            for (var attempt = 0; attempt < 6 && !errorShotTaken; attempt++)
            {
                await TypeQueryAsync(page, "invoice");
                try
                {
                    await page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-error[role='alert']")
                        .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3_000 });
                    await SaveScreenshotAsync(page, "entity-error-1440-dark");
                    errorShotTaken = true;
                }
                catch (TimeoutException)
                {
                }
            }
        }
        catch
        {
            // The error screenshot is a best-effort artifact; the dark results shot above is the assertion.
        }
    }

    private sealed class JsonBulkStats
    {
        public int Count { get; set; }
        public bool Scrollable { get; set; }
    }
}

/// <summary>
/// TG-010 smoke against the Blazor Server demo (:7107): the SignalR round trip renders the same
/// async entity search — sections, badge/code and execution work there too.
/// </summary>
[TestClass]
[TestCategory("Server")]
[TestCategory("Smoke")]
public class CommandPaletteServerE2ETests : ServerTestBase
{
    [TestMethod]
    [Description("TG-010 (Server smoke): entity search shows sections and badge/code on the Server demo")]
    public async Task EntitySearch_Server_Shows_Sections_And_Chips()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}/layout");

        await page.Locator("[data-testid='command-palette-entity-open']").ClickAsync();
        var input = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-input");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await input.PressSequentiallyAsync("uc-042", new LocatorPressSequentiallyOptions { Delay = 40 });

        var item = page.Locator("[data-testid='command-palette-entity'] .tm-command-palette-item",
            new PageLocatorOptions { HasText = "Command palette async source" });
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        (await item.Locator(".tm-command-palette-code").InnerTextAsync()).Trim().Should().Be("UC-042");
    }
}
