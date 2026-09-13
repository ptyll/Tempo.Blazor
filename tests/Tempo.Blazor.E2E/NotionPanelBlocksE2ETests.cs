using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class NotionPanelBlocksE2ETests : NotionE2ETestBase
{
    private static readonly PanelCase[] Panels =
    [
        new("Info panel", "info", "ℹ️", "eb800000-0000-0000-0000-000000000400"),
        new("Note panel", "note", "📝", "eb800000-0000-0000-0000-000000000401"),
        new("Warning panel", "warning", "⚠️", "eb800000-0000-0000-0000-000000000402"),
        new("Error panel", "error", "❌", "eb800000-0000-0000-0000-000000000403"),
        new("Success panel", "success", "✅", "eb800000-0000-0000-0000-000000000404")
    ];

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("CF1: slash menu inserts all semantic panel variants and captures light/dark desktop/mobile baselines")]
    public async Task CF1_PanelBlocks_InsertAllVariantsAndCaptureBaselines()
    {
        var page = await OpenPanelEditorAsync();

        foreach (var panel in Panels)
        {
            await ConvertBlockWithSlashPanelAsync(page, panel.BlockId, panel.Name);
            var callout = PanelCallout(page, panel.BlockId);
            await callout.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
            await ExpectPanelAsync(callout, panel);
        }

        await BlurEditorAsync(page);
        await AssertPanelTextContrastAsync(page);
        await CapturePanelClusterBaselineAsync(page, "desktop-light-all-variants");

        await SetThemeAsync(page, dark: true);
        await CapturePanelClusterBaselineAsync(page, "desktop-dark-all-variants");

        await SetThemeAsync(page, dark: false);
        await SetViewportAsync(390, 860);
        await CapturePanelClusterBaselineAsync(page, "mobile-light-all-variants", closeMobileSidebar: true);

        await SetThemeAsync(page, dark: true);
        await CapturePanelClusterBaselineAsync(page, "mobile-dark-all-variants", closeMobileSidebar: true);
    }

    [TestMethod]
    [Description("CF1: existing panels can change type, long and empty content render safely, and panels work inside columns")]
    public async Task CF1_PanelBlocks_EdgeCases_Work()
    {
        var page = await OpenPanelEditorAsync();

        var warning = Panels[2];
        await ConvertBlockWithSlashPanelAsync(page, warning.BlockId, warning.Name);
        await ChangePanelTypeFromMenuAsync(page, warning.BlockId, "Error panel");
        await ExpectPanelAsync(PanelCallout(page, warning.BlockId), new PanelCase("Error panel", "error", "❌", warning.BlockId));

        const string LongText = "This warning panel contains a deliberately long operational note that should wrap across several lines without overlapping the icon, controls, or following content in either desktop or mobile layouts.";
        await SetEditableTextAsync(page, $"{BlockSelector(warning.BlockId)} .tm-notion-callout__body", LongText);
        await BlurEditorAsync(page);
        var longPanelMetrics = await PanelCallout(page, warning.BlockId).EvaluateAsync<PanelMetrics>(
            """
            el => {
                const rect = el.getBoundingClientRect();
                const body = el.querySelector('.tm-notion-callout__body').getBoundingClientRect();
                return { height: rect.height, bodyRight: body.right, panelRight: rect.right };
            }
            """);
        Assert.IsTrue(longPanelMetrics.Height > 64, "Long panel content should wrap into a taller panel.");
        Assert.IsTrue(longPanelMetrics.BodyRight <= longPanelMetrics.PanelRight + 1, "Long panel content should remain inside the panel bounds.");

        var empty = Panels[3];
        await ConvertBlockWithSlashPanelAsync(page, empty.BlockId, empty.Name);
        await SetEditableTextAsync(page, $"{BlockSelector(empty.BlockId)} .tm-notion-callout__body", string.Empty);
        await BlurEditorAsync(page);
        var emptyHeight = await PanelCallout(page, empty.BlockId).EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        Assert.IsTrue(emptyHeight >= 36, "Empty panels should preserve a usable editing target.");

        const string ColumnParagraphId = "eb800000-0000-0000-0002-000000000100";
        await ConvertBlockWithSlashPanelAsync(page, ColumnParagraphId, "Success panel");
        var columnPanel = PanelCallout(page, ColumnParagraphId);
        await ExpectPanelAsync(columnPanel, new PanelCase("Success panel", "success", "✅", ColumnParagraphId));
        var isInsideColumn = await columnPanel.EvaluateAsync<bool>("el => !!el.closest('.tm-notion-column')");
        Assert.IsTrue(isInsideColumn, "Panel converted inside a column should stay in that column.");
    }

    private async Task<IPage> OpenPanelEditorAsync()
    {
        var page = await OpenNotionEditorAsync();
        await SeedLayoutPageAsync();
        await page.WaitForSelectorAsync(BlockSelector(Panels[0].BlockId), new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        return page;
    }

    private static async Task ConvertBlockWithSlashPanelAsync(IPage page, string blockId, string itemName)
    {
        var editable = page.Locator($"{BlockSelector(blockId)} [contenteditable='true']").First;
        await editable.ScrollIntoViewIfNeededAsync();
        await editable.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await editable.EvaluateAsync("""
            el => {
                el.innerHTML = '';
                el.focus();
                const range = document.createRange();
                range.selectNodeContents(el);
                range.collapse(false);
                const selection = window.getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                el.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);
        await page.WaitForTimeoutAsync(250);
        await page.Keyboard.TypeAsync("/");

        var slash = page.Locator(".tm-notion-slash").First;
        await slash.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await slash.Locator(".tm-notion-slash__input").FillAsync(itemName);
        await page.WaitForTimeoutAsync(250);

        var item = slash.Locator(".tm-notion-slash__item").Filter(new LocatorFilterOptions { HasText = itemName }).First;
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await item.ClickAsync();

        await page.Locator($"{BlockSelector(blockId)} .tm-notion-callout").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await SetEditableTextAsync(page, $"{BlockSelector(blockId)} .tm-notion-callout__body", $"{itemName} content");
    }

    private static async Task ChangePanelTypeFromMenuAsync(IPage page, string blockId, string itemName)
    {
        var block = page.Locator(BlockSelector(blockId)).First;
        await block.ScrollIntoViewIfNeededAsync();
        await block.HoverAsync();
        var menuButton = block.Locator(".tm-notion-handle__btn[aria-haspopup='true']").First;
        await menuButton.ClickAsync(new LocatorClickOptions { Force = true });

        var panelType = page.Locator(".tm-notion-ctx__item", new PageLocatorOptions { HasTextString = "Panel type" }).First;
        await panelType.HoverAsync();
        var item = page.Locator(".tm-notion-ctx-sub .tm-notion-ctx__item", new PageLocatorOptions { HasTextString = itemName }).First;
        await item.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await item.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(750);
    }

    private static async Task SetEditableTextAsync(IPage page, string selector, string text)
    {
        await page.EvaluateAsync(
            """
            args => {
                const el = document.querySelector(args.selector);
                if (!el) throw new Error(`Editable not found: ${args.selector}`);
                el.textContent = args.text;
                el.focus();
                const range = document.createRange();
                const node = el.firstChild || document.createTextNode('');
                if (!node.parentNode) el.appendChild(node);
                range.setStart(node, node.textContent.length);
                range.collapse(true);
                const selection = window.getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                el.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """,
            new { selector, text });
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task BlurEditorAsync(IPage page)
    {
        await page.Locator(".tm-notion-h1").First.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(1200);
    }

    private async Task<NotionBaselineCapture> CapturePanelClusterBaselineAsync(IPage page, string state, bool closeMobileSidebar = false)
    {
        await PreparePanelBaselineViewportAsync(page, closeMobileSidebar);

        var outputDir = GetPanelBaselineDirectory();
        var fullPath = Path.Combine(outputDir, $"{state}.png");
        var regionPath = Path.Combine(outputDir, $"{state}.region.png");

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = fullPath,
            Type = ScreenshotType.Png,
            FullPage = true
        });

        var clip = await page.EvaluateAsync<PanelClusterClip>(
            """
            ids => {
                const panels = ids
                    .map(id => document.querySelector(`[data-block-id="${id}"] .tm-notion-callout`))
                    .filter(Boolean);

                if (panels.length !== ids.length) {
                    throw new Error(`Expected ${ids.length} panel callouts, found ${panels.length}.`);
                }

                const padding = 12;
                const rects = panels.map(panel => panel.getBoundingClientRect());
                const left = Math.min(...rects.map(rect => rect.left));
                const top = Math.min(...rects.map(rect => rect.top));
                const right = Math.max(...rects.map(rect => rect.right));
                const bottom = Math.max(...rects.map(rect => rect.bottom));
                const x = Math.max(0, left);
                const y = Math.max(0, top);
                const width = Math.min(window.innerWidth, right + padding) - x;
                const height = Math.min(window.innerHeight, bottom + padding) - y;
                return { x, y, width, height };
            }
            """,
            Panels.Select(panel => panel.BlockId).ToArray());

        Assert.IsTrue(clip.Width > 0 && clip.Height > 0, "Panel cluster screenshot clip must have positive dimensions.");

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = regionPath,
            Type = ScreenshotType.Png,
            Clip = new Clip
            {
                X = (float)clip.X,
                Y = (float)clip.Y,
                Width = (float)clip.Width,
                Height = (float)clip.Height
            }
        });

        TestContext.AddResultFile(fullPath);
        TestContext.AddResultFile(regionPath);
        return new NotionBaselineCapture(fullPath, regionPath);
    }

    private static async Task PreparePanelBaselineViewportAsync(IPage page, bool closeMobileSidebar = false)
    {
        if (closeMobileSidebar)
        {
            var overlay = page.Locator(".tm-notion-sidebar-overlay").First;
            if (await overlay.IsVisibleAsync())
            {
                await overlay.ClickAsync(new LocatorClickOptions { Force = true });
                await overlay.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Hidden,
                    Timeout = 10000
                });
            }

            var visibleSidebar = page.Locator(".tm-notion-sidebar--visible").First;
            if (await visibleSidebar.IsVisibleAsync())
            {
                await page.Locator(".tm-notion-sidebar-toggle").First.EvaluateAsync("el => el.click()");
                await page.Locator(".tm-notion-sidebar--hidden").First.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Attached,
                    Timeout = 10000
                });
            }
        }

        var firstPanel = PanelCallout(page, Panels[0].BlockId);
        await firstPanel.ScrollIntoViewIfNeededAsync();
        await firstPanel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.EvaluateAsync("() => { document.activeElement?.blur(); window.getSelection()?.removeAllRanges(); }");
        await firstPanel.EvaluateAsync(
            """
            el => {
                const main = el.closest('.tm-notion-main');
                const topbar = document.querySelector('.tm-notion-topbar');
                const topbarHeight = topbar ? topbar.getBoundingClientRect().height : 44;
                const margin = 20;
                if (main) {
                    const mainRect = main.getBoundingClientRect();
                    const rect = el.getBoundingClientRect();
                    main.scrollBy({
                        top: rect.top - mainRect.top - topbarHeight - margin,
                        behavior: 'instant'
                    });
                } else {
                    const rect = el.getBoundingClientRect();
                    window.scrollBy({
                        top: rect.top - topbarHeight - margin,
                        behavior: 'instant'
                    });
                }
            }
            """);
        await page.WaitForTimeoutAsync(350);
    }

    private static async Task ExpectPanelAsync(ILocator callout, PanelCase panel)
    {
        var classes = await callout.GetAttributeAsync("class") ?? string.Empty;
        StringAssert.Contains(classes, $"tm-notion-callout--{panel.Variant}");
        Assert.AreEqual(panel.Variant, await callout.GetAttributeAsync("data-variant"));
        StringAssert.Contains(await callout.Locator(".tm-notion-callout__icon").First.TextContentAsync() ?? string.Empty, panel.Icon);
    }

    private static async Task AssertPanelTextContrastAsync(IPage page)
    {
        var minimum = await page.Locator(".tm-notion-callout[data-variant]:not([data-variant='default'])")
            .EvaluateAllAsync<double>(
                """
                panels => {
                    function parseColor(value) {
                        const m = value.match(/rgba?\(([^)]+)\)/);
                        if (!m) return [255, 255, 255, 1];
                        const parts = m[1].split(',').map(v => Number.parseFloat(v.trim()));
                        return [parts[0], parts[1], parts[2], parts.length >= 4 ? parts[3] : 1];
                    }
                    function composite(fg, bg) {
                        const a = fg[3] + bg[3] * (1 - fg[3]);
                        return [
                            (fg[0] * fg[3] + bg[0] * bg[3] * (1 - fg[3])) / a,
                            (fg[1] * fg[3] + bg[1] * bg[3] * (1 - fg[3])) / a,
                            (fg[2] * fg[3] + bg[2] * bg[3] * (1 - fg[3])) / a,
                            a
                        ];
                    }
                    function luminance(rgb) {
                        const c = rgb.slice(0, 3).map(v => {
                            v /= 255;
                            return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
                        });
                        return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
                    }
                    function contrast(a, b) {
                        const l1 = luminance(a);
                        const l2 = luminance(b);
                        return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
                    }
                    return Math.min(...panels.map(panel => {
                        const body = panel.querySelector('.tm-notion-callout__body') || panel;
                        const panelStyle = getComputedStyle(panel);
                        const bodyStyle = getComputedStyle(body);
                        const pageStyle = getComputedStyle(document.body);
                        const bg = composite(parseColor(panelStyle.backgroundColor), parseColor(pageStyle.backgroundColor));
                        return contrast(parseColor(bodyStyle.color), bg);
                    }));
                }
                """);

        Assert.IsTrue(minimum >= 4.5, $"Panel text contrast should satisfy WCAG AA. Actual minimum: {minimum:0.00}");
    }

    private static async Task SetThemeAsync(IPage page, bool dark)
    {
        await page.EvaluateAsync(
            """
            dark => {
                document.documentElement.toggleAttribute('data-theme', dark);
                if (dark) {
                    document.documentElement.setAttribute('data-theme', 'dark');
                    document.body.classList.add('tm-dark');
                } else {
                    document.documentElement.removeAttribute('data-theme');
                    document.body.classList.remove('tm-dark');
                }
            }
            """,
            dark);
        await page.WaitForTimeoutAsync(250);
    }

    private static ILocator PanelCallout(IPage page, string blockId) =>
        page.Locator($"{BlockSelector(blockId)} .tm-notion-callout").First;

    private static string BlockSelector(string blockId) => $"[data-block-id='{blockId}']";

    private static string GetPanelBaselineDirectory()
    {
        var dir = BaselineOutput.DirectoryFor(null, "notion",
            "panel-blocks");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed record PanelCase(string Name, string Variant, string Icon, string BlockId);

    private sealed class PanelMetrics
    {
        public double Height { get; set; }
        public double BodyRight { get; set; }
        public double PanelRight { get; set; }
    }

    private sealed class PanelClusterClip
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
