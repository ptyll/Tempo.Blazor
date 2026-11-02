using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Signing redesign phase 10 — the editor-bridge demo is one workspace (Edit → Place fields →
/// Preview) and the components page shows the new parameters. Behaviour tests are always on;
/// captures run only when <c>TM_WRITE_BASELINES</c> is set:
/// <code>
/// TM_WRITE_BASELINES=1 dotnet test tests/Tempo.Blazor.E2E --filter "SigningRedesignPhase10E2ETests"
/// </code>
/// </summary>
internal static class SigningRedesignPhase10Specs
{
    public const string BridgeUrl = "/signing-from-editor";

    public static string Phase10Dir
    {
        get
        {
            var dir = Path.Combine(FindRepoRoot().FullName,
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-editor-bridge", "redesign");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static async Task<IPage> OpenBridgeAsync(
        IBrowser browser,
        string baseUrl,
        int width,
        int height,
        bool touch,
        Action<IBrowserContext> registerContext,
        Func<IPage, Task> waitForReady)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            HasTouch = touch,
            IsMobile = touch && width < 640
        });
        registerContext(context);
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(
            "() => { localStorage.setItem('tm-demo-culture', 'en'); " +
            "const style = document.createElement('style'); style.textContent = " +
            System.Text.Json.JsonSerializer.Serialize(width <= 1024 ? SigningRedesignPhase9Specs.WideLayoutStyle : string.Empty) +
            "; document.documentElement.appendChild(style); }");
        await page.GotoAsync($"{baseUrl}{BridgeUrl}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60_000
        });
        await waitForReady(page);
        await page.WaitForSelectorAsync("[data-testid='signing-from-editor-tabs']", new PageWaitForSelectorOptions
        {
            Timeout = 60_000
        });
        await page.Locator(".signing-bridge__title").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 30_000
        });
        await page.Locator("[data-testid='signing-from-editor-document'] [data-testid='document-canvas-engine-host']")
            .WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 60_000
            });
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-export']"))
            .ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 60_000 });
        return page;
    }

    public static async Task CaptureAsync(IPage page, string name, List<string> written)
    {
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase9Specs.CaptureChromeStyle });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".sticky.top-0, .fixed.top-0 { pointer-events: none !important; }"
        });
        await page.AddStyleTagAsync(new PageAddStyleTagOptions { Content = SigningRedesignPhase9Specs.CaptureSurfaceStyle });
        var bridge = page.Locator(".signing-bridge");
        await page.Locator(".signing-bridge__title").EvaluateAsync("el => el.scrollIntoView({ block: 'start', inline: 'nearest' })");
        await page.WaitForTimeoutAsync(250);
        var path = Path.Combine(Phase10Dir, name);
        await bridge.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Type = ScreenshotType.Png });
        Assert.IsTrue(new FileInfo(path).Length > 10_000, $"{name} must be a real capture, not a blank frame");
        Console.WriteLine($"[signing-phase-10] wrote {path} ({new FileInfo(path).Length} bytes)");
        written.Add(path);
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }
}

/// <summary>Writes the phase-10 editor-bridge captures at 1440 / 1024 / 390.</summary>
[TestClass]
public class SigningRedesignPhase10E2ETests : BaselineGeneratorTestBase
{
    [TestMethod]
    [Description("Writes the editor-bridge workspace captures (Edit tab, light/dark × default/indigo)")]
    public async Task CaptureSigningRedesignPhase10Bridge()
    {
        var written = new List<string>();
        var themes = new[]
        {
            (false, false, "light", "default"),
            (true, false, "dark", "default"),
            (false, true, "light", "indigo"),
            (true, true, "dark", "indigo")
        };
        var widths = new[] { 1440, 1024, 390 };

        foreach (var width in widths)
        {
            foreach (var (dark, indigo, theme, brand) in themes)
            {
                var page = await SigningRedesignPhase10Specs.OpenBridgeAsync(
                    Browser, BaseUrl, width, width == 390 ? 844 : 900, touch: width < 1200,
                    RegisterContext, WaitForAppReadyAsync);
                await SigningRedesignPhase5Specs.ApplyThemeAsync(page, dark, indigo);
                await SigningRedesignPhase10Specs.CaptureAsync(
                    page, $"bridge-edit--{width}--{theme}--{brand}.png", written);

                await page.Locator("[data-testid='signing-from-editor-export']").ClickAsync();
                await page.Locator("[data-testid='signing-from-editor-designer']").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
                await SigningRedesignPhase10Specs.CaptureAsync(
                    page, $"bridge-place--{width}--{theme}--{brand}.png", written);

                var addField = page.Locator("[data-testid='signing-from-editor-add-field']");
                await addField.ScrollIntoViewIfNeededAsync();
                await addField.ClickAsync(new LocatorClickOptions { Force = true });
                await page.Locator("[data-testid='signing-from-editor-preview']").ClickAsync();
                await page.Locator("[data-testid='signing-from-editor-runner']").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
                await SigningRedesignPhase10Specs.CaptureAsync(
                    page, $"bridge-preview--{width}--{theme}--{brand}.png", written);

                await page.Locator("[data-testid='signing-from-editor-tab-edit']").ClickAsync();
                await page.Locator("[data-testid='signing-from-editor-document'] [data-testid='document-canvas-page']").First
                    .ClickAsync();
                await page.Locator("[data-testid='signing-from-editor-insert-body']").ClickAsync();
                await page.Locator("[data-testid='signing-from-editor-inline-preview']").ClickAsync();
                await page.Locator("[data-testid='signing-from-editor-inline-runner']").WaitForAsync(
                    new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
                await SigningRedesignPhase10Specs.CaptureAsync(
                    page, $"bridge-inline--{width}--{theme}--{brand}.png", written);
                await page.Context.CloseAsync();
            }
        }

        Assert.IsTrue(written.Count >= 48, $"expected the phase-10 matrix, wrote {written.Count}");
    }
}

/// <summary>
/// Phase-10 behaviour (always on): the bridge is a tabbed workspace, the designer is not wrapped
/// in a showcase card, and the components page shows the new parameters through TmSelect.
/// </summary>
[TestClass]
[TestCategory("WASM")]
[TestCategory("Signing")]
public class SigningRedesignPhase10BehaviorE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("The editor bridge is one workspace: Edit is live, later tabs stay disabled, actions are TmButtons")]
    public async Task SigningBridge_IsOneWorkspace_LaterTabsDisabledUntilExport()
    {
        var page = await SigningRedesignPhase10Specs.OpenBridgeAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        var tabs = page.Locator("[data-testid='signing-from-editor-tabs']");
        await Assertions.Expect(tabs).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-export']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-edit-stage']"))
            .ToHaveAttributeAsync("role", "tabpanel");
        var tabLabels = await page.Locator("[data-testid='signing-from-editor-tabs'] [role='tab']").AllInnerTextsAsync();
        CollectionAssert.AreEqual(
            new[] { "Edit", "Place fields", "Preview", "Inline preview" },
            tabLabels.Select(label => label.Trim()).ToArray());

        // Place fields and Preview stay disabled until an export. The tab button is the panel's.
        var place = page.Locator("[data-testid='signing-from-editor-tab-place']");
        var preview = page.Locator("[data-testid='signing-from-editor-tab-preview']");
        await Assertions.Expect(place).ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(preview).ToHaveAttributeAsync("aria-disabled", "true");

        // The designer is not on the page yet, and it is not wrapped in a showcase card.
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-designer']")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".showcase-card")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [Description("Export → Place → Edit → Place stays enabled and keeps fields; an inserted body field survives")]
    public async Task SigningBridge_RoundTrip_KeepsDesignerFieldsAndInsertedBodyField()
    {
        var page = await SigningRedesignPhase10Specs.OpenBridgeAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        await page.Locator("[data-testid='signing-from-editor-document'] [data-testid='document-canvas-page']").First
            .ClickAsync();
        await page.Locator("[data-testid='signing-from-editor-insert-body']").ClickAsync();

        await page.Locator("[data-testid='signing-from-editor-export']").ClickAsync();
        await page.Locator("[data-testid='signing-from-editor-designer']").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-layout-select'] select")).ToBeVisibleAsync();
        await page.Locator("[data-testid='signing-from-editor-add-field']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-designer-status']"))
            .ToContainTextAsync("1 signing field");

        await page.Locator("[data-testid='signing-from-editor-tab-edit']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-export']")).ToBeEnabledAsync();

        var place = page.Locator("[data-testid='signing-from-editor-tab-place']");
        await place.ClickAsync();
        await Assertions.Expect(place).Not.ToHaveAttributeAsync("aria-disabled", "true");
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-designer-status']"))
            .ToContainTextAsync("1 signing field");

        await page.Locator("[data-testid='signing-from-editor-tab-edit']").ClickAsync();
        await page.Locator("[data-testid='signing-from-editor-inline-preview']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='signing-from-editor-inline-status']"))
            .ToContainTextAsync("1 field", new LocatorAssertionsToContainTextOptions { Timeout = 30_000 });
    }

    [TestMethod]
    [Description("Comments layout and the culture preview are TmSelects, and the runner shows DocumentTitle plus DefaultSignatureMode")]
    public async Task SigningComponents_ShowsNewParameters_ThroughTmSelect()
    {
        var page = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 1440, 900, touch: false, RegisterContext, WaitForAppReadyAsync);

        var layout = page.Locator("[data-testid='document-comments-layout'] select");
        await Assertions.Expect(layout).ToBeVisibleAsync();
        await layout.SelectOptionAsync("Mobile");

        var culture = page.Locator("[data-testid='condition-builder-culture'] select");
        await Assertions.Expect(culture).ToBeVisibleAsync();
        await culture.SelectOptionAsync("cs-CZ");

        var runner = page.Locator("[data-testid='signing-runner-demo']");
        await runner.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(runner.Locator(".tm-signing-form-runner__document-title"))
            .ToContainTextAsync("Service order approval");
        await Assertions.Expect(page.Locator("[data-testid='signing-runner-signature-mode'] select"))
            .ToBeVisibleAsync();
    }
}

/// <summary>
/// Recheck captures for the phase-10 mobile footer / comments fixes. Writes only into
/// <c>signing-redesign/phase-10-recheck/</c>. Never touches phase 1–9 baselines.
/// </summary>
[TestClass]
public class SigningRedesignPhase10RecheckE2ETests : BaselineGeneratorTestBase
{
    [TestMethod]
    [Description("Writes the 320 runner and 390 comments recheck into phase-10-recheck only")]
    public async Task CapturePhase10MobileRecheck()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TempoBlazor.slnx")))
        {
            root = root.Parent;
        }

        var dir = Path.Combine(root!.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "signing-redesign", "phase-10-recheck");
        Directory.CreateDirectory(dir);

        IPage page;
        try
        {
            page = await SigningRedesignPhase9Specs.OpenAsync(
                Browser, BaseUrl, 320, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        }
        catch (TimeoutException)
        {
            page = await SigningRedesignPhase9Specs.OpenAsync(
                Browser, BaseUrl, 320, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        }
        var runner = SigningRedesignPhase9Specs.Runner(page);
        await runner.ScrollIntoViewIfNeededAsync();
        var expand = runner.Locator(".tm-signing-form-runner__mobile-expand").First;
        if (await expand.CountAsync() > 0)
        {
            await expand.EvaluateAsync("el => el.click()");
        }
        await page.WaitForTimeoutAsync(200);
        var row = runner.Locator(".tm-signing-form-runner__field-button").First;
        if (await row.CountAsync() > 0)
        {
            await row.ClickAsync(new LocatorClickOptions { Force = true });
        }

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, "runner-full--320--light--default.png"),
            Type = ScreenshotType.Png
        });
        await page.Context.CloseAsync();

        var commentsPage = await SigningRedesignPhase9Specs.OpenAsync(
            Browser, BaseUrl, 390, 844, touch: true, RegisterContext, WaitForAppReadyAsync);
        var comments = SigningRedesignPhase9Specs.Comments(commentsPage);
        var sheet = comments.Locator(".tm-document-page-viewer__comments-sheet").First;
        await comments.ScrollIntoViewIfNeededAsync();
        var handle = sheet.Locator(".tm-sheet__handle").First;
        if (await handle.CountAsync() > 0)
        {
            await handle.FocusAsync();
            await commentsPage.Keyboard.PressAsync("End");
        }

        var panel = sheet.Locator(".tm-drawer__panel").First;
        await panel.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(dir, "comments--390--light--default.png"),
            Type = ScreenshotType.Png
        });
        await commentsPage.Context.CloseAsync();
    }
}
