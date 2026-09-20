using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 16.5 TEST artefakt — fotografie seřazeného sloupce (<c>aria-sort="ascending"</c>) ve dvou
/// stavech, které musí přežít vedle sebe: hover nad <c>.tm-th-sort</c> a klávesové
/// <c>:focus-visible</c>. Čtyři PNG padají do <c>__screenshots__/data-table/</c> jako UX evidence
/// specu — <c>sort-button-{hover,focus}-{light,dark}.png</c> — a commit zpráva nese verdikt.
/// <para>
/// Kontrakt, který snímky dokumentují (měřený computed stylem, ne okem): seřazený sloupec maluje
/// label i pod hoverem akcentem <c>--tm-color-primary-text</c> (<c>color: inherit</c> na
/// <c>:hover</c> nesmí přebarvit zpět na klidový inkoust — to byl bug N89), a focus ring je
/// <c>outline: 2px solid</c> s <c>outline-offset: +2px</c>, tedy rámeček KOLEM boxu, ne čára přes
/// glyfy. Oboje musí platit ve světle i ve tmě.
/// </para>
/// <para>
/// Dark varianta se přepíná atributem <c>data-theme</c> na layout rootu (spec dovoluje ThemeService
/// toggle NEBO atribut; konvence <c>ToggleDarkModeAsync</c> kliká do sidebaru a klávesový fokus —
/// tedy i <c>:focus-visible</c> důkaz — by tím přešel na toggle button). Pro přepnutý atribut platí
/// stejný selektor <c>[data-theme="dark"]</c> v <c>tokens-dark.css</c>, takže pixely jsou totožné
/// s reálným toggle. Firefox běží stejnou asertaci bez zápisu PNG — artefakty vydává jediný engine,
/// aby commitované soubory neměly nondeterministického autora.
/// </para>
/// </summary>
[TestClass]
public class TmDataTableSortButtonVisualE2ETests : WasmTestBase
{
    private const string DataTablePage = "/data-table";

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_SortedColumn_HoverFocus_Screenshots_LightAndDark_Chromium()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenDataTableAsync(page);
        await AssertSortedButtonStatesAndCaptureAsync(page, capture: true);
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task SortButton_SortedColumn_HoverFocus_LightAndDark_Firefox()
    {
        // The shared browser of the base class is Chromium; Firefox is launched per-test so the
        // same hover/focus contract is measured against the second engine the matrix claims.
        await using var firefox = await NewFirefoxAsync();
        await OpenDataTableAsync(firefox.Page);
        await AssertSortedButtonStatesAndCaptureAsync(firefox.Page, capture: false);
    }

    private async Task OpenDataTableAsync(IPage page)
    {
        await page.SetViewportSizeAsync(1440, 1100);
        await page.GotoAsync($"{BaseUrl}{DataTablePage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='dt-ergonomics-section']").WaitForAsync(
            new LocatorWaitForOptions { Timeout = 30000 });
    }

    /// <summary>
    /// One continuous session: Tab-walk to a sort button (real keyboard modality is what makes
    /// <c>:focus-visible</c> match — a scripted <c>.focus()</c> is not guaranteed to), Enter sorts
    /// the column ascending with focus kept on the button, then the four photographs are taken:
    /// focus ring in light, focus ring in dark (focus survives the in-place attribute switch),
    /// pure hover in dark, pure hover in light (the cursor never left the button).
    /// </summary>
    private async Task AssertSortedButtonStatesAndCaptureAsync(IPage page, bool capture)
    {
        var section = page.Locator("[data-testid='dt-ergonomics-section']");

        var index = await TabToSortButtonIndexAsync(page);
        Assert.IsTrue(index >= 0,
            "Tab navigation must land on a .tm-th-sort inside the ergonomics table within 40 presses");

        var sortedButton = section.Locator("button.tm-th-sort").Nth(index);
        var sortedHeader = sortedButton.Locator("xpath=..");

        // Enter activates through the button's native click → the th owns the sort and the
        // button keeps keyboard focus, so the first photograph catches sorted+focused together.
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(sortedHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        await sortedHeader.ScrollIntoViewIfNeededAsync();

        var accentLight = await ResolveTokenColorAsync(page, "--tm-color-primary-text");
        var surfaceLight = await ResolveTokenColorAsync(page, "--tm-bg-surface-secondary");

        // ── LIGHT · :focus-visible — the ring frames the box, the accent stays on the label ──
        await AssertFocusRingKeepsAccentAsync(sortedButton, accentLight);
        await CaptureAsync(sortedHeader, "sort-button-focus-light", capture);

        // ── DARK · :focus-visible — the theme attribute flips in place; focus never leaves ──
        await SetThemeAsync(page, dark: true);
        var accentDark = await ResolveTokenColorAsync(page, "--tm-color-primary-text");
        var surfaceDark = await ResolveTokenColorAsync(page, "--tm-bg-surface-secondary");
        Assert.AreNotEqual(surfaceLight, surfaceDark,
            "the [data-theme] switch must actually re-paint — the header surface token must resolve differently");
        Assert.AreNotEqual(accentLight, accentDark,
            "the sorted accent is a theme token (primary-800 → primary-300) — dark must resolve a different colour");

        await AssertFocusRingKeepsAccentAsync(sortedButton, accentDark);
        await CaptureAsync(sortedHeader, "sort-button-focus-dark", capture);

        // ── DARK · :hover — drop focus so the photograph shows hover ALONE, then park the real
        //    cursor on the button. The sorted accent must survive the pointer (N89). ──
        await page.EvaluateAsync("() => document.activeElement && document.activeElement.blur()");
        await sortedButton.HoverAsync();
        Assert.AreEqual(accentDark, await ComputedColorAsync(sortedButton),
            "hovering a SORTED sort button keeps the sorted accent — dark theme");
        await CaptureAsync(sortedHeader, "sort-button-hover-dark", capture);

        // ── LIGHT · :hover — the cursor never moved, so :hover still applies after the switch
        //    back; the accent must be the LIGHT token again. ──
        await SetThemeAsync(page, dark: false);
        Assert.AreEqual(accentLight, await ComputedColorAsync(sortedButton),
            "hovering a SORTED sort button keeps the sorted accent — light theme");
        await CaptureAsync(sortedHeader, "sort-button-hover-light", capture);

        TestContext.WriteLine(
            $"[ux-evidence] sorted accent: light={accentLight} dark={accentDark}; " +
            $"header surface: light={surfaceLight} dark={surfaceDark}");
    }

    /// <summary>
    /// The ring contract, asserted in the theme the caller already switched to:
    /// the button is still the active element, still matches <c>:focus-visible</c>, paints the
    /// library's own 2px ring drawn 2px OUTSIDE the box, and keeps the sorted accent on the label.
    /// </summary>
    private static async Task AssertFocusRingKeepsAccentAsync(ILocator button, string expectedAccent)
    {
        var state = await button.EvaluateAsync<string[]>(
            """
            el => {
                const cs = getComputedStyle(el);
                return [String(document.activeElement === el),
                        String(el.matches(':focus-visible')),
                        cs.outlineStyle, cs.outlineWidth, cs.outlineOffset, cs.color];
            }
            """);
        Assert.AreEqual("true", state[0], "the sort button must still be the focused element");
        Assert.AreEqual("true", state[1],
            "a sort button reached by Tab must match :focus-visible — that is the state the ring is painted for");
        Assert.AreEqual("solid", state[2], "the library's own ring, not the platform default of 'none'");
        Assert.AreEqual("2px", state[3]);
        Assert.AreEqual("2px", state[4],
            "the ring is drawn OUTSIDE the box (outline-offset: 2px) — a negative offset painted it over the label's glyphs");
        Assert.AreEqual(expectedAccent, state[5],
            "color: inherit on :focus-visible keeps the sorted accent under the ring");
    }

    /// <summary>
    /// Tabs forward until the focused element is a <c>.tm-th-sort</c> inside the ergonomics
    /// section, then returns its index among that section's sort buttons (DOM order == tab order).
    /// Returns -1 when no sort button is reached within the press budget.
    /// </summary>
    private static async Task<int> TabToSortButtonIndexAsync(IPage page)
    {
        for (var tab = 0; tab < 40; tab++)
        {
            await page.Keyboard.PressAsync("Tab");
            var index = await page.EvaluateAsync<int>(
                """
                () => {
                    const el = document.activeElement;
                    if (!el || !el.classList || !el.classList.contains('tm-th-sort')) return -1;
                    const section = el.closest("[data-testid='dt-ergonomics-section']");
                    if (!section) return -1;
                    return [...section.querySelectorAll('button.tm-th-sort')].indexOf(el);
                }
                """);
            if (index >= 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// Mirrors what <see cref="PlaywrightTestBase.ToggleDarkModeAsync"/> reaches through
    /// ThemeService — the layout root's <c>data-theme</c> attribute and <c>dark</c> class — but
    /// in place, so it never moves focus. The component tokens key off
    /// <c>[data-theme="dark"]</c> alone, so the painted result is identical to the real toggle.
    /// </summary>
    private static async Task SetThemeAsync(IPage page, bool dark)
    {
        await page.EvaluateAsync(
            """
            dark => {
                const root = document.querySelector('[data-theme]');
                root.setAttribute('data-theme', dark ? 'dark' : 'light');
                root.classList.toggle('dark', dark);
            }
            """, dark);
        await Assertions.Expect(page.Locator("[data-theme]").First)
            .ToHaveAttributeAsync("data-theme", dark ? "dark" : "light");
        // Token flips are synchronous in the cascade, but the layout paints a 300ms colour
        // transition — give the capture a settle budget so the photograph is not mid-fade.
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>
    /// Resolves a CSS token to its computed colour on a throwaway probe element. The probe must
    /// live INSIDE the <c>[data-theme]</c> wrapper — the demo scopes its theme tokens to that div,
    /// so a probe on <c>document.body</c> sits outside the themed subtree and would resolve the
    /// :root (light) values regardless of the attribute.
    /// </summary>
    private static async Task<string> ResolveTokenColorAsync(IPage page, string token)
        => await page.EvaluateAsync<string>(
            """
            token => {
                const probe = document.createElement('span');
                probe.style.color = `var(${token})`;
                (document.querySelector('[data-theme]') || document.body).appendChild(probe);
                const value = getComputedStyle(probe).color;
                probe.remove();
                return value;
            }
            """, token);

    private static async Task<string> ComputedColorAsync(ILocator locator)
        => await locator.EvaluateAsync<string>("el => getComputedStyle(el).color");

    /// <summary>
    /// Element screenshot of the whole sorted <c>&lt;th&gt;</c> — not the button alone, because
    /// the 2px-offset ring lives in the header's padding and a button-tight crop would clip it.
    /// Firefox deliberately does NOT write: the committed PNGs have a single authoring engine.
    /// </summary>
    private static async Task CaptureAsync(ILocator header, string fileName, bool capture)
    {
        if (!capture)
        {
            return;
        }

        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "data-table");
        Directory.CreateDirectory(dir);
        await header.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, $"{fileName}.png"),
        });
    }

    /// <summary>The per-test Firefox chain — disposed with the test.</summary>
    private sealed class FirefoxSession : IAsyncDisposable
    {
        public required IPlaywright Playwright { get; init; }
        public required IBrowser Browser { get; init; }
        public required IBrowserContext Context { get; init; }
        public required IPage Page { get; init; }

        public async ValueTask DisposeAsync()
        {
            try { await Context.DisposeAsync(); } catch { /* teardown */ }
            try { await Browser.DisposeAsync(); } catch { /* teardown */ }
            try { Playwright.Dispose(); } catch { /* teardown */ }
        }
    }

    private static async Task<FirefoxSession> NewFirefoxAsync()
    {
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var browser = await playwright.Firefox.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1440, Height = 1100 },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        return new FirefoxSession
        {
            Playwright = playwright,
            Browser = browser,
            Context = context,
            Page = await context.NewPageAsync(),
        };
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

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
