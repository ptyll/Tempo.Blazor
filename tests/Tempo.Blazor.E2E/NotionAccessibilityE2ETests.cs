using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// EB17 responsive, keyboard, and accessibility backfill for the Notion editor.
/// </summary>
[TestClass]
public class NotionAccessibilityE2ETests : NotionE2ETestBase
{
    private const string FirstFormattingBlockId = "eb100000-0000-0000-0000-000000000003";
    private const string SecondFormattingBlockId = "eb100000-0000-0000-0000-000000000004";
    private const string FourColumnListId = "eb800000-0000-0000-0000-000000000030";

    [TestMethod]
    [TestCategory("NotionAccessibility")]
    [TestCategory("NotionUxBaseline")]
    [Description("EB17: mobile viewport keeps key Notion editor surfaces responsive and captures baseline screenshots")]
    public async Task EB17_MobileViewport_KeySurfaces_AreResponsiveAndCaptured()
    {
        await SetViewportAsync(390, 844);
        var page = await OpenNotionEditorAsync();

        await SeedLayoutPageAsync();
        var columns = page.Locator($"[data-block-id='{FourColumnListId}'] .tm-notion-column");
        await columns.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        var columnTops = await columns.EvaluateAllAsync<double[]>("els => els.map(el => el.getBoundingClientRect().top)");
        for (var i = 1; i < columnTops.Length; i++)
            Assert.IsTrue(columnTops[i] > columnTops[i - 1], "Columns should stack vertically at the EB17 mobile viewport.");
        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "layout mobile editor");
        await CaptureBaselineAsync("accessibility", "eb17-mobile-columns", page.Locator($"[data-block-id='{FourColumnListId}']").First);

        await page.Locator(".tm-notion-sidebar-toggle").First.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator(".tm-notion-sidebar--visible").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await AssertTouchTargetsAsync(page, ".tm-notion-sidebar-toggle, .tm-notion-sidebar button:visible, .tm-notion-sidebar a:visible", "mobile sidebar");
        await CaptureBaselineAsync("accessibility", "eb17-mobile-sidebar", page.Locator(".tm-notion-editor").First);
        // Phase 3: below desktop the sidebar is a non-modal bottom sheet with no backdrop, so a
        // coordinate click no longer dismisses it — the sheet's own close button does.
        await page.Locator(".tm-editor-shell__sheet-close").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.tm-notion-sidebar--visible')",
            new PageWaitForFunctionOptions { Timeout = 5000 });

        await SeedTablePageAsync();
        var tableBlock = page.Locator("[data-block-id='eb700000-0000-0000-0000-000000000010']").First;
        await tableBlock.ScrollIntoViewIfNeededAsync();
        await AssertNoHorizontalOverflowAsync(page, ".tm-notion-editor", "table mobile editor");
        await AssertTouchTargetsAsync(page, ".tm-notion-table-block button:visible", "mobile table controls");
        await CaptureBaselineAsync("accessibility", "eb17-mobile-table", tableBlock);

        await SeedTextFormattingPageAsync();
        await OpenNotionInlineToolbarForBlockAsync(page, FirstFormattingBlockId, ".tm-notion-paragraph");
        await AssertTouchTargetsAsync(page, ".tm-notion-inline-toolbar button:visible", "mobile inline toolbar");
        await CaptureBaselineAsync("accessibility", "eb17-mobile-inline-toolbar", page.Locator(".tm-notion-inline-toolbar").First);

        await SeedMediaPageAsync();
        await page.Locator("[data-block-id='eb600000-0000-0000-0000-000000000011'] .tm-notion-media-upload-zone--image").First.ClickAsync();
        var dialog = page.Locator(".tm-media-dialog").First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await AssertTouchTargetsAsync(page, ".tm-media-dialog button:visible, .tm-media-dialog input:visible", "mobile media dialog");
        await CaptureBaselineAsync("accessibility", "eb17-mobile-media-dialog", dialog);

        TestContext.WriteLine("UX EB17: mobile editor surfaces keep 44px touch targets on key controls, columns stack, tables scroll inside the editor, and dialog/toolbar/sidebar baselines were captured.");
    }

    [TestMethod]
    [TestCategory("NotionAccessibility")]
    [Description("EB17: keyboard navigation moves between blocks and Escape closes page settings")]
    public async Task EB17_KeyboardNavigation_ArrowBlocksAndEscape_CloseMenus()
    {
        var page = await OpenNotionEditorAsync();
        await SeedTextFormattingPageAsync();

        var first = page.Locator($"[data-block-id='{FirstFormattingBlockId}'] .tm-notion-paragraph").First;
        var second = page.Locator($"[data-block-id='{SecondFormattingBlockId}'] .tm-notion-paragraph").First;
        await first.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await second.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await first.EvaluateAsync("el => { el.focus(); const range = document.createRange(); range.selectNodeContents(el); range.collapse(false); const sel = window.getSelection(); sel.removeAllRanges(); sel.addRange(range); }");
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForFunctionAsync(
            "id => document.activeElement?.closest('[data-block-id]')?.dataset.blockId === id",
            SecondFormattingBlockId,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        await page.Keyboard.PressAsync("ArrowUp");
        await page.WaitForFunctionAsync(
            "id => document.activeElement?.closest('[data-block-id]')?.dataset.blockId === id",
            FirstFormattingBlockId,
            new PageWaitForFunctionOptions { Timeout = 5000 });

        var tabOrder = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('.tm-notion-editor button:not([disabled]), .tm-notion-editor a[href], .tm-notion-editor input:not([disabled])'))
                .filter(el => {
                    const rect = el.getBoundingClientRect();
                    const style = getComputedStyle(el);
                    return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
                })
                .map(el => el.getAttribute('aria-label') || el.getAttribute('title') || el.className || el.tagName)
            """);
        Assert.IsTrue(tabOrder.Length >= 4, "Editor should expose a meaningful visible tab order.");

        var settingsTrigger = page.Locator(".tm-npsm-trigger").First;
        await settingsTrigger.ClickAsync();
        await page.Locator(".tm-npsm").First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-npsm")).ToHaveCountAsync(0);
        var focusReturned = await settingsTrigger.EvaluateAsync<bool>("el => document.activeElement === el");
        Assert.IsTrue(focusReturned, "Page settings trigger should regain focus after Escape closes the menu.");
    }

    [TestMethod]
    [TestCategory("NotionAccessibility")]
    [Description("EB17: focus remains trapped in block menus and media dialogs and Escape closes them")]
    public async Task EB17_FocusManagement_MenuAndDialog_TrapFocusAndCloseOnEscape()
    {
        var page = await OpenNotionEditorAsync();
        await SeedDragDropPageAsync();

        var block = page.Locator("[data-block-id='eb160000-0000-0000-0000-000000000004']").First;
        await block.HoverAsync();
        var menuButton = block.Locator(".tm-notion-handle__menu-anchor > .tm-notion-handle__btn").First;
        await menuButton.ClickAsync();

        var contextMenu = page.Locator(".tm-notion-ctx").First;
        await contextMenu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await ActiveElementIsInsideAsync(page, ".tm-notion-ctx"), "Block menu should receive focus after opening.");

        for (var i = 0; i < 8; i++)
        {
            await page.Keyboard.PressAsync(i % 2 == 0 ? "Tab" : "Shift+Tab");
            Assert.IsTrue(await ActiveElementIsInsideAsync(page, ".tm-notion-ctx"), "Tab traversal should stay inside the block context menu.");
        }

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-notion-ctx")).ToHaveCountAsync(0);
        Assert.IsTrue(await menuButton.EvaluateAsync<bool>("el => document.activeElement === el"), "Block menu trigger should regain focus after Escape.");

        await SeedMediaPageAsync();
        await page.Locator("[data-block-id='eb600000-0000-0000-0000-000000000011'] .tm-notion-media-upload-zone--image").First.ClickAsync();
        var dialog = page.Locator(".tm-media-dialog").First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.IsTrue(await ActiveElementIsInsideAsync(page, ".tm-media-dialog"), "Media dialog should receive focus after opening.");

        for (var i = 0; i < 10; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.IsTrue(await ActiveElementIsInsideAsync(page, ".tm-media-dialog"), "Tab traversal should stay inside the media dialog.");
        }

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".tm-media-dialog")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [TestCategory("NotionAccessibility")]
    [Description("EB17: axe-core scan has zero critical violations in the Notion editor")]
    public async Task EB17_AxeCore_CriticalViolations_AreZero()
    {
        var page = await OpenNotionEditorAsync();
        await SeedLayoutPageAsync();

        var editor = page.Locator(".tm-notion-editor").First;
        var results = await editor.RunAxe(new AxeRunOptions
        {
            ResultTypes = [ResultType.Violations]
        });

        var critical = results.Violations
            .Where(v => string.Equals(v.Impact, "critical", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (critical.Count > 0)
        {
            var details = string.Join(Environment.NewLine, critical.Select(v =>
                $"{v.Id}: {v.Help} ({v.HelpUrl}) targets={string.Join(", ", v.Nodes.Select(n => n.Target.ToString()))}"));
            Assert.Fail($"Expected zero critical axe-core violations in the Notion editor.{Environment.NewLine}{details}");
        }
    }

    private static async Task AssertNoHorizontalOverflowAsync(IPage page, string selector, string label)
    {
        var overflow = await page.Locator(selector).First.EvaluateAsync<double>(
            "el => Math.max(0, el.scrollWidth - el.clientWidth)");
        Assert.IsTrue(overflow <= 2, $"{label} should not horizontally overflow its own shell. Overflow={overflow}.");
    }

    private static async Task AssertTouchTargetsAsync(IPage page, string selector, string label)
    {
        var failures = await page.Locator(selector).EvaluateAllAsync<string[]>(
            """
            els => els
                .filter(el => {
                    const rect = el.getBoundingClientRect();
                    const style = getComputedStyle(el);
                    return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
                })
                .map(el => {
                    const rect = el.getBoundingClientRect();
                    return {
                        label: el.getAttribute('aria-label') || el.getAttribute('title') || el.textContent.trim() || el.className || el.tagName,
                        width: rect.width,
                        height: rect.height
                    };
                })
                .filter(item => item.width < 44 || item.height < 44)
                .map(item => `${item.label} ${Math.round(item.width)}x${Math.round(item.height)}`)
            """);

        Assert.AreEqual(0, failures.Length, $"{label} should keep visible touch targets at least 44px. Offenders: {string.Join("; ", failures)}");
    }

    private static async Task<bool> ActiveElementIsInsideAsync(IPage page, string selector) =>
        await page.EvaluateAsync<bool>(
            "selector => !!document.activeElement?.closest(selector)",
            selector);
}
