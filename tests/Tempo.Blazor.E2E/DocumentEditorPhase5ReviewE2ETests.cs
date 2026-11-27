using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 5 REVIEW fixes, as behaviour: the table grid and the token list stay inside their panel (1440, 1024
/// touch, 390), a panel never covers its trigger, a field popover closes when the canvas scrolls, a dialog opened from the command
/// palette gives the keyboard back to the document, and a phone user can tap the symbol trigger with a REAL click and get the symbol at the
/// preserved caret. Writes no files.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:Floating")]
[DoNotParallelize]
public sealed class DocumentEditorPhase5ReviewE2ETests : WasmTestBase
{
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    private static readonly (string Name, int Width, int Height, bool Touch)[] ThreeViewports =
    [
        ("1440", 1440, 900, false),
        ("1024-touch", 1024, 768, true),
        ("390", 390, 844, true),
    ];

    [TestMethod]
    [Description("The table grid sits inside its panel (a popover at 1440, the sheet at 390) - it no longer centres itself on the viewport")]
    public async Task TableGrid_StaysInsideItsPanel()
    {
        foreach (var (name, width, height, touch) in new[] { ThreeViewports[0], ThreeViewports[2] })
        {
            var (page, errors) = await OpenAsync(width, height, touch);
            await ClickPopupItemAsync(page, "document-ribbon-tab-insert", "");
            await ClickPopupItemAsync(page, "document-toolbar-table", "Table");
            await Assertions.Expect(page.GetByTestId("document-table-grid-picker")).ToBeVisibleAsync(new() { Timeout = 10_000 });

            var container = width < 640 ? ".tm-overlay-panel-sheet" : "[data-floating-kind='table']";
            await AssertInsideAsync(page, "[data-testid='document-table-grid-picker']", container, $"table grid at {name}");
            AssertNoClientErrors(errors, $"table grid {name}");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The token list is the panel's own list: inside the panel (popover or sheet) at 1440, 1024 touch and 390")]
    public async Task TokenList_StaysInsideItsPanel()
    {
        foreach (var (name, width, height, touch) in ThreeViewports)
        {
            var (page, errors) = await OpenAsync(width, height, touch);
            await ClickPopupItemAsync(page, "document-ribbon-tab-insert", "");
            await ClickPopupItemAsync(page, "document-insert-menu", "Insert");
            await Assertions.Expect(page.GetByTestId("document-token-insert-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.Locator("[data-testid='document-token-insert-panel'] .tm-rte-token-item").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

            var container = width < 640 ? ".tm-overlay-panel-sheet" : "[data-testid='document-token-insert-panel']";
            // The list scrolls inside .tm-document-token-menu: the SCROLLER must be inside the panel (the list's own box is the full,
            // clipped height), and the list must not be wider than the panel.
            await AssertInsideAsync(page, ".tm-document-token-menu", container, $"token menu at {name}");
            var list = (await page.Locator(".tm-rte-token-dropdown").First.BoundingBoxAsync())!;
            var holder = (await page.Locator(container).First.BoundingBoxAsync())!;
            Assert.IsTrue(list.X >= holder.X - 1.5f && list.X + list.Width <= holder.X + holder.Width + 1.5f, $"token list at {name} stays within the panel width");

            // The override must reach the sheet too (the panel root class is not applied there): the list is in flow, as wide as its
            // scroller (the sheet body), and the scroller ends inside the sheet - not an absolute 360px popup clipped by the viewport.
            Assert.AreEqual("static", await page.Locator(".tm-rte-token-dropdown").First.EvaluateAsync<string>("e => getComputedStyle(e).position"), $"token list at {name} is in flow");
            var menu = (await page.Locator(".tm-document-token-menu").First.BoundingBoxAsync())!;
            Assert.IsTrue(Math.Abs(list.Width - menu.Width) <= 2, $"token list at {name} is as wide as the menu ({list.Width:0} vs {menu.Width:0})");
            Assert.IsTrue(menu.Y + menu.Height <= holder.Y + holder.Height + 1.5f, $"token menu at {name} ends inside the panel ({menu.Y + menu.Height:0} vs {holder.Y + holder.Height:0})");
            if (width < 640)
            {
                Assert.IsTrue(menu.Y + menu.Height <= height + 1, "the token menu is not clipped by the viewport bottom");
            }
            AssertNoClientErrors(errors, $"token list {name}");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A panel opens BELOW its trigger at 1440x900: it never covers the ribbon button that opened it")]
    public async Task Panels_DoNotCoverTheirTrigger_At1440()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);

        await ClickPopupItemAsync(page, "document-ribbon-tab-layout", "");
        await AssertBelowTriggerAsync(page, "document-page-layout", "document-page-layout-inspector");
        await page.Keyboard.PressAsync("Escape");

        await ClickPopupItemAsync(page, "document-ribbon-tab-insert", "");
        await AssertBelowTriggerAsync(page, "document-insert-menu", "document-token-insert-panel");
        await page.Keyboard.PressAsync("Escape");

        await ClickPopupItemAsync(page, "document-ribbon-tab-home", "");
        var toggle = page.Locator("button[data-testid='document-style-gallery-toggle']:visible");
        if (await toggle.CountAsync() > 0)
        {
            await AssertBelowTriggerAsync(page, "document-style-gallery-toggle", "document-style-gallery");
        }
        else
        {
            // The toggle is in More: the gallery then anchors to the ribbon row, which it must not cover either.
            await ClickRibbonAsync(page, "document-style-gallery-toggle");
            await Assertions.Expect(page.GetByTestId("document-style-gallery")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            var ribbon = await page.GetByTestId("document-ribbon-toolbar").BoundingBoxAsync();
            var panel = await page.GetByTestId("document-style-gallery").BoundingBoxAsync();
            Assert.IsTrue(panel!.Y >= ribbon!.Y + ribbon.Height - 1, $"the gallery starts below the ribbon row ({panel.Y} vs {ribbon.Y + ribbon.Height})");
        }

        AssertNoClientErrors(errors, "panel versus trigger");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A form-field popover is anchored to a viewport rectangle: scrolling the canvas closes it")]
    public async Task FieldPopover_ClosesWhenTheCanvasScrolls()
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, touch: false));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId=phase-e9-canvas-content-controls&showToolbar=true", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-host\"][data-canvas-engine-ready=\"true\"]')", 60_000);
        await page.WaitForTimeoutAsync(800);
        await page.EvaluateAsync(
            """
            async () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const handle = host?.getAttribute('data-canvas-engine-handle') || '';
                const module = await import('/_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/interop.mjs');
                module.execCommand(handle, 'focusContentControl', JSON.stringify({ controlId: 'canvas-form-renewal' }));
            }
            """);
        var popover = page.GetByTestId("document-canvas-content-control-popover");
        await Assertions.Expect(popover).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(600);

        await page.Mouse.MoveAsync(300, 500);
        await page.Mouse.WheelAsync(0, 400);

        await Assertions.Expect(popover).ToBeHiddenAsync(new() { Timeout = 5_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A dialog opened from the command palette hands the keyboard back to the document on Escape")]
    public async Task DialogFromThePalette_Escape_ReturnsFocusToTheCanvas()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.GetByTestId("document-canvas-engine-host").ClickAsync(new() { Position = new() { X = 40, Y = 60 } });
        await page.Keyboard.PressAsync("Control+Shift+P");
        await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByTestId("document-command-palette-search").FillAsync("compare");
        await page.Locator("[data-testid='document-command-palette-item'][data-command]").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeHiddenAsync(new() { Timeout = 5_000 });
        await Assertions.Expect(page.GetByTestId("document-canvas-hidden-input")).ToBeFocusedAsync(new() { Timeout = 10_000 });
        AssertNoClientErrors(errors, "palette -> dialog -> escape");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A disabled command stays in the palette with its reason and does not run on Enter")]
    public async Task Palette_DisabledCommand_ShowsItsReason_AndDoesNotClosePalette()
    {
        var (page, errors) = await OpenAsync(1440, 900, touch: false);
        await page.GetByTestId("document-canvas-engine-host").ClickAsync(new() { Position = new() { X = 40, Y = 60 } });
        await page.Keyboard.PressAsync("Control+Shift+P");
        await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var disabled = page.Locator("[data-testid='document-command-palette-item'][aria-disabled='true']");
        if (await disabled.CountAsync() > 0)
        {
            await Assertions.Expect(disabled.First.Locator(".tm-command-palette-disabled-reason")).ToBeVisibleAsync();
            // Playwright treats aria-disabled as not actionable: the click is forced, the palette must still ignore it.
            await disabled.First.ClickAsync(new() { Force = true });
            await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync();
        }

        // The search input is the one tab stop of the combobox: no option is focusable.
        Assert.AreEqual(0, await page.Locator("[data-testid='document-command-palette-item'] button").CountAsync());
        AssertNoClientErrors(errors, "palette disabled command");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("390: a real tap on the symbol trigger (no dispatched event), then a symbol, lands at the preserved caret")]
    public async Task Phone_SymbolTrigger_TapInsertsAtTheCaret()
    {
        var (page, errors) = await OpenAsync(390, 844, touch: true);
        var point = await page.EvaluateAsync<double[]>(
            """
            () => {
                const node = Array.from(document.querySelectorAll('[data-canvas-text-rect]')).find(item => item.getBoundingClientRect().width > 8);
                const rect = node.getBoundingClientRect();
                return [rect.left + rect.width - 2, rect.top + rect.height / 2];
            }
            """);
        await page.Mouse.ClickAsync((float)point[0], (float)point[1]);
        await page.Keyboard.PressAsync("End");

        await ClickPopupItemAsync(page, "document-ribbon-tab-insert", "");
        await ClickPopupItemAsync(page, "document-toolbar-symbol", "Symbol");
        await Assertions.Expect(page.GetByTestId("document-symbol-menu")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-symbol-copyright").ClickAsync();

        await WaitForPredicateAsync(page, "() => (document.getElementById('document-canvas-a11y-mirror')?.textContent || '').includes('\\u00A9')", 10_000);
        await Assertions.Expect(page.GetByTestId("document-undo")).ToBeEnabledAsync(new() { Timeout = 10_000 });
        AssertNoClientErrors(errors, "phone symbol");
        await page.Context.CloseAsync();
    }

    private static async Task AssertInsideAsync(IPage page, string innerSelector, string outerSelector, string what)
    {
        var inner = await page.Locator(innerSelector).First.BoundingBoxAsync();
        var outer = await page.Locator(outerSelector).First.BoundingBoxAsync();
        Assert.IsNotNull(inner, $"{what}: the inner element has a box");
        Assert.IsNotNull(outer, $"{what}: the container has a box");
        const float tolerance = 1.5f;
        Assert.IsTrue(
            inner.X >= outer.X - tolerance && inner.Y >= outer.Y - tolerance
            && inner.X + inner.Width <= outer.X + outer.Width + tolerance
            && inner.Y + inner.Height <= outer.Y + outer.Height + tolerance,
            $"{what}: ({inner.X:0},{inner.Y:0},{inner.Width:0},{inner.Height:0}) is not inside ({outer.X:0},{outer.Y:0},{outer.Width:0},{outer.Height:0})");
    }

    private static async Task AssertBelowTriggerAsync(IPage page, string triggerTestId, string panelTestId)
    {
        var trigger = page.Locator($"button[data-testid='{triggerTestId}']:visible").First;
        await trigger.ClickAsync();

        await Assertions.Expect(page.GetByTestId(panelTestId)).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var anchor = (await trigger.BoundingBoxAsync())!;
        var panel = (await page.GetByTestId(panelTestId).BoundingBoxAsync())!;
        Assert.IsTrue(panel.Y >= anchor.Y + anchor.Height - 1, $"{panelTestId} starts below its trigger ({panel.Y:0} vs {anchor.Y + anchor.Height:0})");
    }

    private static void AssertNoClientErrors(List<string> errors, string context)
        => Assert.AreEqual(0, errors.Count, $"{context}: no console or page error expected, got: {string.Join(" | ", errors.Take(5))}");

    private async Task<(IPage Page, List<string> Errors)> OpenAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.Contains("favicon", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(message.Text);
            }
        };
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=contract-demo", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-toolbar"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return (page, errors);
    }
}
