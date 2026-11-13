using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Accessibility;

/// <summary>
/// Plan 92e96b25, phase 11 (KG-008) — the WCAG AA contrast audit measured ON THE REAL DEMO
/// (https API + WASM): every element the backlog (743dc7bb + ca474f34) flagged must hold 4.5:1 in
/// light AND dark, at 1440 px AND 390 px, with locator screenshots into
/// <c>TestResults/contrast-audit/</c> for the UX review. Edge cases: a disabled TmMultiSelect with
/// 0 / 1 / overflowing chips and a long chip label, an open list, an error state, TmChangeDiff with
/// an empty old value, an RTE driven over its MaxLength, the cs culture plural families and
/// <c>prefers-contrast: more</c>.
///
/// The measurement composes the element's whole background chain (a translucent chip wash is
/// composited over the box behind it, exactly what the eye sees) — the same arithmetic as the
/// bUnit <c>ThemeCss</c> guards and <c>TmDiagramThemeE2ETests.Contrast</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ContrastAuditE2ETests : WasmTestBase
{
    private static string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "contrast-audit");

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
        {
            dir = dir.Parent!;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private async Task<IPage> OpenAsync(string route, int width = 1440, int height = 1000, string culture = "en")
    {
        var context = await CreateContextAsync();
        // The init script re-runs on EVERY reload, so a culture switch mid-test would be clobbered
        // back to 'en' — bind the script to the culture the test actually wants.
        await context.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}{route}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static async Task ToggleDarkAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-theme=\"dark\"]') !== null", null, new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(400);
    }

    private async Task ShotAsync(IPage page, ILocator locator, string name)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(150);
        var path = Path.Combine(ShotDir, $"{name}.png");
        await locator.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        Assert.IsTrue(new FileInfo(path).Length > 2 * 1024, $"screenshot {name} looks empty");
    }

    /// <summary>
    /// The ratio of an element's text over the composited background a viewer sees: every
    /// translucent background layer from the outermost ancestor down to the element itself is
    /// alpha-blended in order, then WCAG contrast is computed. Mirrors <c>ThemeCss.PaintedRatio</c>.
    /// </summary>
    private static Task<double> RatioAsync(ILocator element, string label)
        => ContrastProbe.RatioAsync(element, label);

    // ── TmMultiSelect ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MultiSelect_LockedChips_And_Placeholder_HoldAa_In_BothThemes_And_Viewports()
    {
        foreach (var (width, height, viewport) in new[] { (1440, 1000, "desktop"), (390, 844, "phone") })
        {
            var page = await OpenAsync("/forms", width, height);
            var locked = page.Locator("[data-testid='multiselect-locked-section']");
            await locked.WaitForAsync();

            // Select one option in the ENABLED chip-mode card so the enabled chip is measured too.
            var enabledCard = page.Locator(".showcase-card", new() { Has = page.Locator("text=Chip Mode") }).First;
            await enabledCard.Locator(".tm-multiselect").First.ClickAsync();
            await page.Locator(".tm-multiselect__options [role='option']").First.ClickAsync();

            foreach (var dark in new[] { false, true })
            {
                if (dark)
                {
                    await ToggleDarkAsync(page);
                }

                await RatioAsync(enabledCard.Locator(".tm-multiselect__chip").First,
                    $"enabled chip ({viewport}, {(dark ? "dark" : "light")})");
                await RatioAsync(page.Locator("[data-testid='multiselect-disabled-empty'] .tm-multiselect__placeholder"),
                    $"disabled placeholder, 0 chips ({viewport}, {(dark ? "dark" : "light")})");
                await RatioAsync(page.Locator("[data-testid='multiselect-disabled-single'] .tm-multiselect__chip").First,
                    $"disabled chip, 1 long chip ({viewport}, {(dark ? "dark" : "light")})");
                var manyChips = page.Locator("[data-testid='multiselect-disabled-many'] .tm-multiselect__chip");
                Assert.IsTrue(await manyChips.CountAsync() >= 5, "the overflowing card must render its chips");
                await RatioAsync(manyChips.First,
                    $"disabled chip, overflowing ({viewport}, {(dark ? "dark" : "light")})");

                // Disabled box paint: identical to .tm-input[disabled] — muted, never light in dark.
                var disabledBox = page.Locator("[data-testid='multiselect-disabled-single'] .tm-multiselect");
                var boxBg = await disabledBox.EvaluateAsync<string>(
                    "e => getComputedStyle(e).backgroundColor");
                Assert.AreEqual(
                    dark ? "rgb(30, 41, 59)" : "rgb(249, 250, 251)",
                    boxBg,
                    $"the disabled box must paint --tm-color-gray-50 ({(dark ? "dark" : "light")}), never the inverted light gray-800");
                Assert.AreEqual(
                    "not-allowed",
                    await disabledBox.EvaluateAsync<string>("e => getComputedStyle(e).cursor"),
                    "disabledness carries the cursor");

                await ShotAsync(page, locked, $"multiselect-locked-{(dark ? "dark" : "light")}-{viewport}");
            }

            await page.CloseAsync();
        }
    }

    [TestMethod]
    public async Task MultiSelect_Error_Open_And_MaxReached_States_Render()
    {
        var page = await OpenAsync("/forms");

        // Error state: danger border token, error text present.
        var errorCard = page.Locator("[data-testid='multiselect-error']");
        await errorCard.Locator(".tm-multiselect--error").WaitForAsync();
        var errorBorder = await errorCard.Locator(".tm-multiselect--error")
            .EvaluateAsync<string>("e => getComputedStyle(e).borderColor");
        StringAssert.StartsWith(errorBorder, "rgb(220, 38, 38)",
            "the error border paints --tm-color-danger");
        await RatioAsync(errorCard.Locator(".tm-input-error-message"), "multiselect error message");

        // Open list: the popup paints options with readable text in both themes.
        var enabledCard = page.Locator(".showcase-card", new() { Has = page.Locator("text=Chip Mode") }).First;
        await enabledCard.Locator(".tm-multiselect").First.ClickAsync();
        var options = page.Locator(".tm-multiselect__options [role='option']");
        await options.First.WaitForAsync();
        await RatioAsync(options.First, "open list option (light)");
        await ShotAsync(page, page.Locator(".tm-multiselect__popup").First, "multiselect-open-light");
        await ToggleDarkAsync(page);
        await RatioAsync(options.First, "open list option (dark)");
        await ShotAsync(page, page.Locator(".tm-multiselect__popup").First, "multiselect-open-dark");

        await page.CloseAsync();
    }

    // ── TmChangeDiff ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ChangeDiff_Old_And_New_Values_HoldAa_In_BothThemes()
    {
        var page = await OpenAsync("/data-display");
        var section = page.Locator("section:has(.tm-change-diff)").First;
        await section.Locator(".tm-change-diff-new").First.WaitForAsync();

        // The demo's _nullChanges row covers the EMPTY old value edge case.
        Assert.IsTrue(await page.Locator(".tm-change-diff-old").CountAsync() >= 3,
            "the demo renders several diff rows, including the empty-old-value one");

        foreach (var dark in new[] { false, true })
        {
            if (dark)
            {
                await ToggleDarkAsync(page);
            }

            var rows = page.Locator(".tm-change-diff-row");
            for (var i = 0; i < await rows.CountAsync(); i++)
            {
                await RatioAsync(rows.Nth(i).Locator(".tm-change-diff-new"),
                    $"change-diff new value row {i} ({(dark ? "dark" : "light")})");
                await RatioAsync(rows.Nth(i).Locator(".tm-change-diff-old"),
                    $"change-diff old value row {i} ({(dark ? "dark" : "light")})");
            }

            await ShotAsync(page, section, $"change-diff-{(dark ? "dark" : "light")}");
        }

        await page.CloseAsync();
    }

    // ── Rich editor counters ─────────────────────────────────────────────────

    [TestMethod]
    public async Task RichText_Counters_And_ClearFormat_HoldAa_In_BothThemes()
    {
        var page = await OpenAsync("/richtext");
        var fullEditor = page.Locator(".tm-rich-editor-full").First;
        await fullEditor.Locator(".tm-rte-word-count").WaitForAsync();

        foreach (var dark in new[] { false, true })
        {
            if (dark)
            {
                await ToggleDarkAsync(page);
            }

            await RatioAsync(page.Locator(".tm-rte-word-count").First,
                $"RTE word count ({(dark ? "dark" : "light")})");
            await RatioAsync(page.Locator(".tm-rte-char-count").First,
                $"RTE char count ({(dark ? "dark" : "light")})");
            await RatioAsync(page.Locator(".tm-rte-clear-format").First,
                $"RTE clear-format glyph ({(dark ? "dark" : "light")})");

            await ShotAsync(page, fullEditor, $"richtext-counters-{(dark ? "dark" : "light")}");
        }

        await page.CloseAsync();
    }

    [TestMethod]
    public async Task RichText_OverTheLimit_Counter_HoldsAa_In_BothThemes()
    {
        var page = await OpenAsync("/richtext");
        // The simple editor carries MaxLength="500" — type past it to reach tm-rte-char-count-over.
        var simpleEditor = page.Locator(".tm-rich-editor-simple").First;
        var surface = simpleEditor.Locator(".tm-rte-editor-content");
        await surface.WaitForAsync();
        await surface.FillAsync(new string('x', 600));
        await surface.BlurAsync();
        var over = simpleEditor.Locator(".tm-rte-char-count-over");
        await over.WaitForAsync(new LocatorWaitForOptions { Timeout = 10_000 });

        foreach (var dark in new[] { false, true })
        {
            if (dark)
            {
                await ToggleDarkAsync(page);
            }

            await RatioAsync(over.First, $"RTE over-limit counter ({(dark ? "dark" : "light")})");
            await ShotAsync(page, simpleEditor, $"richtext-over-limit-{(dark ? "dark" : "light")}");
        }

        await page.CloseAsync();
    }

    [TestMethod]
    public async Task RichText_Counters_Use_Czech_Plural_Forms()
    {
        // The init script binds 'cs' directly — see OpenAsync: it re-runs on every reload, so
        // clicking the switcher would be clobbered back to English on the force-load.
        var page = await OpenAsync("/richtext", culture: "cs");

        var wordCount = await page.Locator(".tm-rte-word-count").First.TextContentAsync();
        StringAssert.Contains(wordCount ?? string.Empty, "slov",
            "the cs word counter renders the plural family (… slov / slovo / slova)");
        // The FULL editor's counter carries the unit word; the simple one is the bare "n / m".
        var charCount = await page.Locator(".tm-rich-editor-full .tm-rte-char-count").First.TextContentAsync();
        StringAssert.Contains(charCount ?? string.Empty, "znak",
            "the cs char counter renders the znak/znaky/znaků family");

        await ShotAsync(page, page.Locator(".tm-rich-editor-full").First, "richtext-counters-cs");
        await page.CloseAsync();
    }

    // ── TmAlert — every variant ──────────────────────────────────────────────

    [TestMethod]
    public async Task Alerts_AllVariants_Descriptions_HoldAa_In_BothThemes()
    {
        var page = await OpenAsync("/feedback");
        var section = page.Locator("[data-testid='alerts-section']");
        await section.Locator(".tm-alert").First.WaitForAsync();

        var alerts = section.Locator(".tm-alert");
        var count = await alerts.CountAsync();
        Assert.AreEqual(13, count,
            "the grid is 4 severities × 3 variants (12) plus the 'with actions' alert — the audit measures every one");

        foreach (var dark in new[] { false, true })
        {
            if (dark)
            {
                await ToggleDarkAsync(page);
            }

            for (var i = 0; i < count; i++)
            {
                var alert = alerts.Nth(i);
                var variant = await alert.EvaluateAsync<string>("e => e.className");
                await RatioAsync(alert.Locator(".tm-alert__description, .tm-alert__body").First,
                    $"alert[{i}] ({variant}) description ({(dark ? "dark" : "light")})");
            }

            await ShotAsync(page, section, $"alerts-grid-{(dark ? "dark" : "light")}");
        }

        await page.CloseAsync();
    }

    // ── TmTopBar search hint ─────────────────────────────────────────────────

    [TestMethod]
    public async Task TopBar_SearchHint_HoldsAa_In_BothThemes()
    {
        var page = await OpenAsync("/layout");
        var hint = page.Locator("[data-testid='topbar-demo'] .tm-topbar-search-hint");
        await hint.WaitForAsync();

        foreach (var dark in new[] { false, true })
        {
            if (dark)
            {
                await ToggleDarkAsync(page);
            }

            await RatioAsync(hint, $"topbar search hint ({(dark ? "dark" : "light")})");
            await ShotAsync(page, page.Locator("[data-testid='topbar-demo']"),
                $"topbar-hint-{(dark ? "dark" : "light")}");
        }

        await page.CloseAsync();
    }

    // ── prefers-contrast: more ───────────────────────────────────────────────

    [TestMethod]
    public async Task PrefersContrastMore_Keeps_The_Audited_Pairs_At_Aa()
    {
        var page = await OpenAsync("/forms");
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { Contrast = Contrast.More });
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load });
        await WaitForAppReadyAsync(page);

        await RatioAsync(page.Locator("[data-testid='multiselect-disabled-single'] .tm-multiselect__chip").First,
            "disabled chip (prefers-contrast: more)");
        await RatioAsync(page.Locator("[data-testid='multiselect-disabled-empty'] .tm-multiselect__placeholder"),
            "disabled placeholder (prefers-contrast: more)");

        await page.GotoAsync($"{BaseUrl}/data-display", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        await WaitForAppReadyAsync(page);
        await RatioAsync(page.Locator(".tm-change-diff-new").First,
            "change-diff new value (prefers-contrast: more)");

        await ShotAsync(page, page.Locator("section:has(.tm-change-diff)").First,
            "changediff-prefers-contrast-more");
        await page.CloseAsync();
    }
}
