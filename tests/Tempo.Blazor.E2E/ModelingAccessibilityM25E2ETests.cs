using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>Accessibility E2E checks for modeling editor phase M25.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class ModelingAccessibilityM25E2ETests : WasmTestBase
{
    private const string ModelingEditorUrl = "/modeling-editor";

    [TestMethod]
    [Description("Axe reports no critical or serious WCAG violations inside the loaded modeling editor")]
    public async Task AxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('[data-testid="modeling-editor"]');
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });

                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """);

        Assert.AreEqual(0, violations.Length, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    [Description("Keyboard-only path can reload, generate, navigate the model tree, select a node, and show inspector detail")]
    public async Task KeyboardOnly_CanReloadGenerateNavigateTreeAndInspect()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        await FocusByTabAsync(page, "modeling-source-load-button", 120);
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await FocusByTabAsync(page, "modeling-generate-diagram-button", 160);
        await page.Keyboard.PressAsync("Enter");
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);

        var focusedTreeNodeTestId = await FocusTreeNodeByTabAsync(page, 220);
        await SaveStableScreenshotAsync(page, "accessibility-focus-ring.png");

        var focusedElementId = await ReadActiveElementAttributeAsync(page, "data-element-id");
        Assert.IsFalse(string.IsNullOrWhiteSpace(focusedElementId), $"Focused tree node '{focusedTreeNodeTestId}' should expose data-element-id.");

        await page.Keyboard.PressAsync("ArrowDown");
        var navigatedElementId = await ReadActiveElementAttributeAsync(page, "data-element-id");
        if (string.Equals(focusedElementId, navigatedElementId, StringComparison.Ordinal))
        {
            await page.Keyboard.PressAsync("ArrowUp");
            navigatedElementId = await ReadActiveElementAttributeAsync(page, "data-element-id");
        }

        Assert.IsFalse(string.IsNullOrWhiteSpace(navigatedElementId), "Arrow navigation should keep focus on a model tree node.");
        Assert.AreNotEqual(focusedElementId, navigatedElementId, "Arrow navigation should move between visible model tree nodes.");

        await page.Keyboard.PressAsync("Enter");
        await WaitForEditorAttributeAsync(page, "data-selected-element-id", navigatedElementId!);
        await Assertions.Expect(page.Locator("[data-testid='modeling-inspector']"))
            .ToHaveAttributeAsync("data-selected-element-id", navigatedElementId);
    }

    [TestMethod]
    [Description("Open-in-editor overlay behaves as a modal dialog and restores focus to the trigger")]
    public async Task OpenInEditorOverlay_ReturnsFocusToTriggerAfterClose()
    {
        var page = await OpenLoadedModelingPageAsync();

        await page.Locator("[data-testid='modeling-generate-diagram-button']").ClickAsync();
        await WaitForPreviewNodeCountAtLeastAsync(page, 1);
        await page.Locator("[data-testid='modeling-open-in-editor-button']").FocusAsync();
        await page.Locator("[data-testid='modeling-open-in-editor-button']").ClickAsync();

        var overlay = page.Locator("[data-testid='modeling-open-diagram-editor']");
        await Assertions.Expect(overlay).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        await Assertions.Expect(overlay).ToHaveAttributeAsync("role", "dialog");
        await Assertions.Expect(overlay).ToHaveAttributeAsync("aria-modal", "true");
        await WaitForActiveTestIdAsync(page, "modeling-open-diagram-close");

        await page.Locator("[data-testid='modeling-open-diagram-close']").ClickAsync();
        await Assertions.Expect(overlay).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await WaitForActiveTestIdAsync(page, "modeling-open-in-editor-button");
    }

    [TestMethod]
    [Description("Issues expose screen-reader metadata: navigable issues are labeled buttons, sourceless issues keep accessible text")]
    public async Task IssuePanelAndStatusStrip_ExposeScreenReaderMetadata()
    {
        var page = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");

        var issueItems = page.Locator("[data-testid='modeling-issue-list'] > li[role='listitem']");
        await Assertions.Expect(issueItems).Not.ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        var itemCount = await issueItems.CountAsync();

        // Only issues WITH a source element/relationship render as labeled buttons.
        var navigableButtons = page.Locator("[data-testid='modeling-issue-list'] button[aria-label]");
        var navigableCount = await navigableButtons.CountAsync();
        Assert.IsTrue(navigableCount > 0, "Navigable issues should expose a labeled button target.");

        var unlabeledIssues = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('[data-testid="modeling-issue-list"] button'))
                .filter(button => (button.getAttribute('aria-label') || '').trim().length < 12)
                .map(button => button.textContent.trim())
            """);
        Assert.AreEqual(0, unlabeledIssues.Length, "Issue buttons should have readable aria-labels.");

        // Sourceless issues are NOT buttons — they keep their accessible text in the reading order.
        var staticItems = page.Locator("[data-testid='modeling-issue-list'] > li[role='listitem'] > div[data-severity]");
        var staticCount = await staticItems.CountAsync();
        Assert.AreEqual(itemCount, navigableCount + staticCount,
            "Every issue item is either a labeled button (navigable) or a text-only item (sourceless).");
        Assert.IsTrue(staticCount > 0, "The issues-mixed scenario carries a sourceless issue.");

        var staticTexts = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('[data-testid="modeling-issue-list"] > li[role="listitem"] > div[data-severity]'))
                .map(item => (item.textContent || '').replace(/\s+/g, ' ').trim())
            """);
        Assert.IsTrue(staticTexts.All(text => text.Length >= 12),
            "Sourceless issue items keep their accessible text: " + string.Join(" | ", staticTexts));

        var status = page.Locator("[data-testid='modeling-status-strip']");
        await Assertions.Expect(status).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(status).ToHaveAttributeAsync("aria-live", "polite");
        await Assertions.Expect(status).ToHaveAttributeAsync("aria-atomic", "true");
    }

    [TestMethod]
    [Description("Text contrast in the modeling editor meets WCAG AA in light and dark theme")]
    public async Task TextContrast_LightAndDarkMeetAa()
    {
        var light = await OpenLoadedModelingPageAsync("?scenario=issues-mixed");
        var lightFailures = await CaptureContrastFailuresAsync(light);
        Assert.AreEqual(0, lightFailures.Length, "Light theme contrast failures:" + Environment.NewLine + string.Join(Environment.NewLine, lightFailures));

        var dark = await OpenLoadedModelingPageAsync("?scenario=issues-mixed", dark: true);
        var darkFailures = await CaptureContrastFailuresAsync(dark);
        Assert.AreEqual(0, darkFailures.Length, "Dark theme contrast failures:" + Environment.NewLine + string.Join(Environment.NewLine, darkFailures));
    }

    [TestMethod]
    [Description("Forced-colors mode keeps toolbar, panels, focus outlines, and status surface distinguishable")]
    public async Task ForcedColors_KeepsToolbarAndPanelsDistinguishable()
    {
        IBrowserContext? context = null;
        try
        {
            context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
                ForcedColors = ForcedColors.Active
            });
            await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
            var page = await context.NewPageAsync();
            await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
            await WaitForAppReadyAsync(page);
            await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(forced-colors: active)').matches"));
            var failures = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const selectors = [
                        '[data-testid="modeling-editor-toolbar"]',
                        '[data-testid="modeling-model-tree-panel"]',
                        '[data-testid="modeling-preview-panel"]',
                        '[data-testid="modeling-inspector-panel"]',
                        '[data-testid="modeling-status-strip"]'
                    ];
                    const problems = [];
                    for (const selector of selectors) {
                        const element = document.querySelector(selector);
                        if (!element) {
                            problems.push(`${selector}: missing`);
                            continue;
                        }
                        const style = getComputedStyle(element);
                        if (style.borderColor === 'rgba(0, 0, 0, 0)' && style.outlineColor === 'rgba(0, 0, 0, 0)') {
                            problems.push(`${selector}: no visible boundary color`);
                        }
                    }
                    return problems;
                }
                """);
            Assert.AreEqual(0, failures.Length, string.Join(Environment.NewLine, failures));

            await page.Locator("[data-testid='modeling-generate-diagram-button']").FocusAsync();
            var hasVisibleFocus = await page.EvaluateAsync<bool>(
                """
                () => {
                    const element = document.activeElement;
                    const style = getComputedStyle(element);
                    return style.outlineStyle !== 'none' && style.outlineWidth !== '0px';
                }
                """);
            Assert.IsTrue(hasVisibleFocus, "Focused toolbar button should have a visible forced-colors outline.");
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("forced", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Inconclusive($"Forced-colors emulation is not available in this Playwright/browser environment: {ex.Message}");
        }
        finally
        {
            if (context is not null)
            {
                await context.CloseAsync();
            }
        }
    }

    private async Task<IPage> OpenLoadedModelingPageAsync(string query = "", bool dark = false)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 720);
        await page.GotoAsync($"{BaseUrl}{ModelingEditorUrl}{query}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='modeling-editor'][data-state='loaded']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.Locator("[data-testid='modeling-diagram-preview']")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        if (dark)
        {
            await page.EvaluateAsync(
                """
                () => {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.documentElement.classList.add('tm-dark', 'dark');
                    document.body.classList.add('tm-dark', 'dark');
                    document.querySelector('[data-theme]')?.setAttribute('data-theme', 'dark');
                }
                """);
            await page.WaitForTimeoutAsync(300);
        }

        return page;
    }

    private static async Task FocusByTabAsync(IPage page, string expectedTestId, int maxTabs)
    {
        for (var index = 0; index < maxTabs; index++)
        {
            var activeTestId = await ReadActiveTestIdAsync(page);
            if (string.Equals(activeTestId, expectedTestId, StringComparison.Ordinal))
            {
                return;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        var finalTestId = await ReadActiveTestIdAsync(page);
        Assert.Fail($"Tab navigation did not reach '{expectedTestId}' within {maxTabs} tabs. Active test id: '{finalTestId}'.");
    }

    private static async Task<string> FocusTreeNodeByTabAsync(IPage page, int maxTabs)
    {
        for (var index = 0; index < maxTabs; index++)
        {
            var activeTestId = await ReadActiveTestIdAsync(page);
            if (activeTestId.StartsWith("modeling-tree-node-", StringComparison.Ordinal))
            {
                return activeTestId;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        var finalTestId = await ReadActiveTestIdAsync(page);
        Assert.Fail($"Tab navigation did not reach a modeling tree node within {maxTabs} tabs. Active test id: '{finalTestId}'.");
        return string.Empty;
    }

    private static async Task WaitForActiveTestIdAsync(IPage page, string expectedTestId)
    {
        await page.WaitForFunctionAsync(
            """
            expectedTestId => document.activeElement?.closest?.('[data-testid]')?.getAttribute('data-testid') === expectedTestId
            """,
            expectedTestId,
            new PageWaitForFunctionOptions { Timeout = 5000 });
    }

    private static Task<string> ReadActiveTestIdAsync(IPage page) =>
        page.EvaluateAsync<string>(
            """
            () => document.activeElement?.closest?.('[data-testid]')?.getAttribute('data-testid') || ''
            """);

    private static Task<string?> ReadActiveElementAttributeAsync(IPage page, string attributeName) =>
        page.EvaluateAsync<string?>(
            """
            attributeName => document.activeElement?.getAttribute(attributeName) || null
            """,
            attributeName);

    private static Task WaitForEditorAttributeAsync(IPage page, string attributeName, string expectedValue) =>
        page.WaitForFunctionAsync(
            """
            ([attributeName, expectedValue]) => document.querySelector("[data-testid='modeling-editor']")?.getAttribute(attributeName) === expectedValue
            """,
            new[] { attributeName, expectedValue },
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task WaitForPreviewNodeCountAtLeastAsync(IPage page, int minimumCount) =>
        page.WaitForFunctionAsync(
            """
            minimumCount => {
                const preview = document.querySelector("[data-testid='modeling-diagram-preview']");
                return Number(preview?.getAttribute('data-node-count') ?? '0') >= minimumCount;
            }
            """,
            minimumCount,
            new PageWaitForFunctionOptions { Timeout = 10000 });

    private static Task<string[]> CaptureContrastFailuresAsync(IPage page) =>
        page.EvaluateAsync<string[]>(
            """
            () => {
                const root = document.querySelector('[data-testid="modeling-editor"]');
                const selectors = 'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), h1, h2, h3, h4, h5, h6, p, span, strong, dt, dd, label, li';
                const candidates = Array.from(root.querySelectorAll(selectors));
                const failures = [];

                const parseColor = value => {
                    if (!value || value === 'transparent') return null;
                    const match = value.match(/rgba?\(([^)]+)\)/);
                    if (match) {
                        const parts = match[1].split(',').map(part => Number.parseFloat(part.trim()));
                        return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
                    }

                    const colorMatch = value.match(/color\(srgb\s+([0-9.]+)\s+([0-9.]+)\s+([0-9.]+)(?:\s*\/\s*([0-9.]+))?\)/);
                    if (colorMatch) {
                        return {
                            r: Number.parseFloat(colorMatch[1]) * 255,
                            g: Number.parseFloat(colorMatch[2]) * 255,
                            b: Number.parseFloat(colorMatch[3]) * 255,
                            a: colorMatch[4] ? Number.parseFloat(colorMatch[4]) : 1
                        };
                    }

                    return null;
                };
                const blend = (fg, bg) => ({
                    r: fg.r * fg.a + bg.r * (1 - fg.a),
                    g: fg.g * fg.a + bg.g * (1 - fg.a),
                    b: fg.b * fg.a + bg.b * (1 - fg.a),
                    a: 1
                });
                const luminance = color => {
                    const values = [color.r, color.g, color.b].map(channel => {
                        const value = channel / 255;
                        return value <= 0.03928 ? value / 12.92 : Math.pow((value + 0.055) / 1.055, 2.4);
                    });
                    return 0.2126 * values[0] + 0.7152 * values[1] + 0.0722 * values[2];
                };
                const contrast = (a, b) => {
                    const first = luminance(a);
                    const second = luminance(b);
                    return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
                };
                const backgroundFor = element => {
                    let current = element;
                    let color = { r: 255, g: 255, b: 255, a: 1 };
                    while (current) {
                        const parsed = parseColor(getComputedStyle(current).backgroundColor);
                        if (parsed && parsed.a > 0) {
                            color = parsed.a < 1 ? blend(parsed, color) : parsed;
                            if (parsed.a >= 1) break;
                        }
                        current = current.parentElement;
                    }
                    return color;
                };

                for (const element of candidates) {
                    if (failures.length >= 20) break;
                    const rect = element.getBoundingClientRect();
                    const text = (element.innerText || element.value || element.textContent || '').replace(/\s+/g, ' ').trim();
                    if (!text || rect.width < 1 || rect.height < 1) continue;
                    if (!element.matches('button, select') && Array.from(element.children).some(child => (child.innerText || child.textContent || '').trim())) continue;

                    const style = getComputedStyle(element);
                    if (style.visibility === 'hidden' || style.display === 'none') continue;

                    const fg = parseColor(style.color);
                    if (!fg) continue;
                    const bg = backgroundFor(element);
                    const ratio = contrast(fg.a < 1 ? blend(fg, bg) : fg, bg);
                    if (ratio < 4.5) {
                        const id = element.getAttribute('data-testid') || element.closest('[data-testid]')?.getAttribute('data-testid') || element.tagName.toLowerCase();
                        failures.push(`${id}: ${ratio.toFixed(2)} "${text.slice(0, 80)}"`);
                    }
                }

                return failures;
            }
            """);

    private async Task SaveStableScreenshotAsync(IPage page, string fileName)
    {
        var directory = Path.Combine(
            Path.GetDirectoryName(typeof(ModelingAccessibilityM25E2ETests).Assembly.Location)!,
            "..",
            "..",
            "..",
            "TestResults",
            "modeling-m25");
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var path = Path.GetFullPath(Path.Combine(directory, fileName));
        await File.WriteAllBytesAsync(path, bytes);
        TestContext.AddResultFile(path);
    }
}
