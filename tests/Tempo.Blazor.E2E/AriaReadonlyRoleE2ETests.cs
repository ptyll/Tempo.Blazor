using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4g — <c>aria-readonly</c> only on the roles ARIA 1.2 supports it on.
/// The locked <c>TmSigningFieldEditorPanel</c> root (an <c>&lt;aside&gt;</c>, implicit
/// complementary) no longer carries <c>aria-readonly</c> — the lock is announced on the disabled
/// inner editors and hosts key the locked panel off the stable
/// <c>tm-signing-field-editor-panel--readonly</c> root class. The <c>TmNotionPageHistory</c>
/// version preview (a display-only container) is now a named <c>role="region"</c> — the
/// <c>aria-label</c> was prohibited on the generic div and <c>aria-readonly</c> is not supported
/// on a region either. Scoped axe scans (aria-allowed-attr, aria-prohibited-attr) guard both
/// surfaces in both themes; screenshots land in <c>__screenshots__/fze-4g/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class AriaReadonlyRoleE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string ReadOnlyCard = "[data-testid='field-editor-readonly-card']";
    private const string HistoryRoot = ".tm-nph";

    private readonly List<string> _clientErrors = [];

    private static Task PinCultureAsync(IBrowserContext context) =>
        context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");

    private async Task<IPage> OpenSigningComponentsAsync(int width = 1440, int height = 1200, bool touch = false)
    {
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/signing-components",
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
        await page.Locator(ReadOnlyCard).WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* ignore if API unavailable or cert untrusted */ }

        var page = await OpenAppPageAsync(
            $"{BaseUrl}/notion-editor",
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 1000 },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            },
            PinCultureAsync);
        page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        return page;
    }

    /// <summary>Opens the page settings menu and clicks "Page history".</summary>
    private static async Task<ILocator> OpenPageHistoryAsync(IPage page)
    {
        var trigger = page.Locator(".tm-npsm-trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await trigger.ClickAsync();

        var menu = page.Locator(".tm-npsm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var historyItem = menu.Locator(".tm-npsm__item")
                             .Filter(new LocatorFilterOptions { HasText = "Page history" })
                             .First;
        await historyItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await historyItem.ClickAsync();

        var panel = page.Locator(HistoryRoot).First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return panel;
    }

    [TestMethod]
    public async Task SigningFieldEditor_ReadOnlyPanel_KeepsTheLockOffTheLandmark()
    {
        var page = await OpenSigningComponentsAsync();

        var card = page.Locator(ReadOnlyCard);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, ReadOnlyCard);

        var aside = card.Locator("aside.tm-signing-field-editor-panel");
        Assert.AreEqual(1, await aside.CountAsync(), "the read-only card renders the panel");
        Assert.IsNull(await aside.GetAttributeAsync("aria-readonly"),
            "aria-readonly is not a supported attribute on the aside's complementary role (ARIA 1.2)");
        StringAssert.Contains(await aside.GetAttributeAsync("class") ?? string.Empty,
            "tm-signing-field-editor-panel--readonly",
            "hosts key the locked panel off the stable root class");

        // The lock is announced where it applies — on the disabled controls.
        var name = card.Locator(".tm-signing-field-editor-panel__name");
        Assert.IsNotNull(await name.GetAttributeAsync("disabled"),
            "the name editor is disabled — the lock is announced on the control");
        var type = card.Locator(".tm-signing-field-editor-panel__type");
        Assert.IsNotNull(await type.GetAttributeAsync("disabled"),
            "the type editor is disabled — the lock is announced on the control");

        await AssertNoAriaRoleViolationsAsync(page, ReadOnlyCard, "read-only field editor card");

        // Dark pass on the same card.
        await ToggleDarkModeAsync(page);
        await WaitForLayoutToSettleAsync(page, ReadOnlyCard);
        await AssertNoAriaRoleViolationsAsync(page, ReadOnlyCard, "read-only field editor card (dark)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task SigningFieldEditor_ReadOnlyPanel_Phone_KeepsTheLockOffTheLandmark()
    {
        // Edge case: the phone layout — the same contract on a narrow touch viewport.
        var page = await OpenSigningComponentsAsync(390, 844, touch: true);

        var card = page.Locator(ReadOnlyCard);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(page, ReadOnlyCard);

        var aside = card.Locator("aside.tm-signing-field-editor-panel");
        Assert.IsNull(await aside.GetAttributeAsync("aria-readonly"));
        StringAssert.Contains(await aside.GetAttributeAsync("class") ?? string.Empty,
            "tm-signing-field-editor-panel--readonly");

        var overflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        Assert.IsFalse(overflow, "the read-only card must not stretch the page horizontally");

        await AssertNoAriaRoleViolationsAsync(page, ReadOnlyCard, "read-only field editor card (phone)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task NotionPageHistory_Preview_IsANamedRegion_WithoutAriaReadonly()
    {
        var page = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);

        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await versionItems.Nth(1).ClickAsync();

        var preview = panel.Locator(".tm-nph__preview").First;
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        Assert.AreEqual("region", await preview.GetAttributeAsync("role"),
            "the preview is a named region — aria-label is prohibited on a generic div");
        var label = await preview.GetAttributeAsync("aria-label");
        Assert.IsFalse(string.IsNullOrWhiteSpace(label), "the region carries an accessible name");
        Assert.IsNull(await preview.GetAttributeAsync("aria-readonly"),
            "aria-readonly is not a supported attribute on role=region — the preview is display-only");

        await AssertNoAriaRoleViolationsAsync(page, HistoryRoot, "page history preview");

        // Dark pass on the same surface.
        await EnsureDemoThemeAsync(page, "dark");
        await AssertNoAriaRoleViolationsAsync(page, HistoryRoot, "page history preview (dark)");
        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Screenshots_ReadOnlyFieldEditor_LightDarkAndPhone()
    {
        var directory = ScreenshotDirectory();

        var page = await OpenSigningComponentsAsync();
        await page.Locator(ReadOnlyCard).ScrollIntoViewIfNeededAsync();
        await CaptureCardAsync(page, ReadOnlyCard,
            Path.Combine(directory, "fze-4g-field-editor-readonly-light-1440.png"));

        await ToggleDarkModeAsync(page);
        await CaptureCardAsync(page, ReadOnlyCard,
            Path.Combine(directory, "fze-4g-field-editor-readonly-dark-1440.png"));

        // Edge case: the phone layout.
        var phone = await OpenSigningComponentsAsync(390, 844, touch: true);
        await phone.Locator(ReadOnlyCard).ScrollIntoViewIfNeededAsync();
        await WaitForLayoutToSettleAsync(phone, ReadOnlyCard);
        await SetStickyHeaderVisibleAsync(phone, false);
        try
        {
            await phone.Locator(ReadOnlyCard).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(directory, "fze-4g-field-editor-readonly-light-390.png"),
            });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(phone, true);
        }

        AssertNoClientErrors();
    }

    [TestMethod]
    public async Task Screenshots_NotionHistoryPreview_LightAndDark()
    {
        var directory = ScreenshotDirectory();

        var page = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);
        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await versionItems.Nth(1).ClickAsync();
        var preview = panel.Locator(".tm-nph__preview").First;
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await Assertions.Expect(preview.Locator(".tm-nph__preview-block").First).ToBeVisibleAsync();

        var modal = page.Locator(".tm-nph-host").First;
        await modal.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4g-notion-history-preview-light-1440.png"),
        });

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, HistoryRoot);
        await modal.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(directory, "fze-4g-notion-history-preview-dark-1440.png"),
        });

        AssertNoClientErrors();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static string ScreenshotDirectory()
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "fze-4g");
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>
    /// The demo shell's sticky header would cover the top of the card in an element screenshot;
    /// hide it for the capture and restore it right after (same helper as in
    /// <see cref="TmPickerAriaStateE2ETests"/>).
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

    /// <summary>Captures a card with the sticky header hidden and restored afterwards.</summary>
    private static async Task CaptureCardAsync(IPage page, string selector, string screenshotPath)
    {
        await WaitForLayoutToSettleAsync(page, selector);
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            await page.Locator(selector).ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = screenshotPath,
            });
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }
    }

    /// <summary>
    /// The phase-4g contract scan: exactly the two ARIA attribute rules (aria-allowed-attr,
    /// aria-prohibited-attr) — every violation is reported regardless of impact.
    /// </summary>
    private static async Task AssertNoAriaRoleViolationsAsync(IPage page, string selector, string label)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector) || document.body;
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'rule', values: ['aria-allowed-attr', 'aria-prohibited-attr'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            selector);
        Assert.AreEqual(0, violations.Length,
            $"aria-allowed-attr / aria-prohibited-attr violations on the {label}:" + Environment.NewLine
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
