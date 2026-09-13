using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Kompletní baseline screenshoty pro TmDocumentEditor – ribbon, dropdowny, dialogy,
/// float toolbary, kontextová menu, panely, bannery, módy a dark theme.
/// Každý krok má vlastní try/catch – selhání jednoho screenshotu nezastaví ostatní.
/// </summary>
[TestClass]
public class DocumentEditorFullBaselineScreenshots : BaselineGeneratorTestBase
{
    private static string OutputDir
    {
        get
        {
            var dir = BaselineOutput.DirectoryFor(null, "document-editor-full");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private int _step = 0;

    private async Task<IPage> OpenEditorAsync(int width = 1600, int height = 900)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/document-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(800);
        return page;
    }

    private async Task CaptureAsync(IPage page, string fileName)
    {
        await page.WaitForTimeoutAsync(400);
        var path = Path.Combine(OutputDir, $"{_step:00}-{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = false });
        Console.WriteLine($"[baseline] {_step:00} {fileName}");
        _step++;
    }

    private async Task TryCaptureAsync(IPage page, string fileName, Func<Task> setup)
    {
        try
        {
            await setup();
            await CaptureAsync(page, fileName);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[baseline] SKIP {_step:00}-{fileName}: {ex.Message}");
            _step++;
        }
    }

    private static async Task ClickTabAsync(IPage page, string tabTestId)
    {
        await page.Locator($"[data-testid='{tabTestId}']").ClickAsync();
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task<string> SelectFirstInlineRangeAsync(IPage page, int start, int end)
    {
        return await page.EvaluateAsync<string>("""
            ({ start, end }) => {
                const host = document.querySelector('[data-testid="document-wysiwyg-host"]');
                const isVisible = el => {
                    if (!el || el.closest('[aria-hidden="true"], .tm-wysiwyg-page--virtual')) return false;
                    const rect = el.getBoundingClientRect();
                    const style = getComputedStyle(el);
                    return rect.width > 0 && rect.height > 0 && style.visibility !== 'hidden' && style.display !== 'none';
                };
                const paragraphBlocks = Array.from(host?.querySelectorAll('.tm-wysiwyg-page__body p.tm-wysiwyg-block') || []).filter(isVisible);
                const block = paragraphBlocks[1] || paragraphBlocks[0] || Array.from(host?.querySelectorAll('.tm-wysiwyg-page__body [data-block-id]') || []).find(isVisible);
                if (!block) {
                    const inline = Array.from(host?.querySelectorAll('.tm-wysiwyg-page__body [data-inline-id]') || []).find(isVisible);
                    if (inline) {
                        const resolveInline = absoluteOffset => {
                            const walker = document.createTreeWalker(inline, NodeFilter.SHOW_TEXT);
                            let current = 0, node;
                            while ((node = walker.nextNode())) {
                                const length = node.textContent.length;
                                if (absoluteOffset <= current + length) return { node, offset: Math.max(0, Math.min(absoluteOffset - current, length)) };
                                current += length;
                            }
                            return null;
                        };
                        const textLength = inline.textContent.length;
                        const rangeStart = Math.max(0, Math.min(start, textLength));
                        const rangeEnd = Math.max(rangeStart, Math.min(end, textLength));
                        const startPos = resolveInline(rangeStart);
                        const endPos = resolveInline(rangeEnd);
                        if (!startPos || !endPos) throw new Error('Editable inline text node was not found.');
                        const range = document.createRange();
                        range.setStart(startPos.node, startPos.offset);
                        range.setEnd(endPos.node, endPos.offset);
                        inline.closest('[contenteditable="true"]')?.focus();
                        const selection = window.getSelection();
                        selection.removeAllRanges();
                        selection.addRange(range);
                        document.dispatchEvent(new Event('selectionchange'));
                        return range.toString();
                    }
                    throw new Error('Editable paragraph block was not found.');
                }
                const resolve = absoluteOffset => {
                    const walker = document.createTreeWalker(block, NodeFilter.SHOW_TEXT);
                    let current = 0, node;
                    while ((node = walker.nextNode())) {
                        const length = node.textContent.length;
                        if (absoluteOffset <= current + length) return { node, offset: Math.max(0, Math.min(absoluteOffset - current, length)) };
                        current += length;
                    }
                    return null;
                };
                const textLength = block.textContent.length;
                const rangeStart = Math.max(0, Math.min(start, textLength));
                const rangeEnd = Math.max(rangeStart, Math.min(end, textLength));
                const startPos = resolve(rangeStart);
                const endPos = resolve(rangeEnd);
                if (!startPos || !endPos) throw new Error('Editable text node was not found.');
                const range = document.createRange();
                range.setStart(startPos.node, startPos.offset);
                range.setEnd(endPos.node, endPos.offset);
                block.closest('[contenteditable="true"]')?.focus();
                const selection = window.getSelection();
                selection.removeAllRanges();
                selection.addRange(range);
                document.dispatchEvent(new Event('selectionchange'));
                return range.toString();
            }
        """, new { start, end });
    }

    private static async Task OpenSelectionContextMenuAsync(IPage page)
    {
        await page.EvaluateAsync("""
            () => {
                const selection = window.getSelection();
                if (!selection || selection.rangeCount === 0) throw new Error('No selection');
                const range = selection.getRangeAt(0);
                const rect = range.getBoundingClientRect();
                const x = Math.max(8, rect.left + Math.min(12, Math.max(1, rect.width / 2)));
                const y = Math.max(8, rect.top + Math.min(12, Math.max(1, rect.height / 2)));
                const target = document.elementFromPoint(x, y) || selection.anchorNode?.parentElement || document.querySelector('[data-testid="document-wysiwyg-host"] [data-inline-id]');
                target.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true, button: 2, clientX: x, clientY: y }));
            }
        """);
    }

    private static async Task<string> InsertTableFromRibbonAsync(IPage page, int rows = 2, int columns = 2)
    {
        var body = page.Locator("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-page__body[contenteditable='true']").First;
        await body.ClickAsync();
        await page.Keyboard.PressAsync("End");
        await page.WaitForTimeoutAsync(200);
        await ClickTabAsync(page, "document-ribbon-tab-insert");
        await page.Locator("[data-testid='document-toolbar-table']").ClickAsync();
        await page.WaitForSelectorAsync("[data-testid='document-table-grid-picker']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
        await page.Locator($"[data-testid='document-table-grid-cell-{rows - 1}-{columns - 1}']").ClickAsync();
        await page.WaitForTimeoutAsync(600);
        return "table-inserted";
    }

    [TestMethod]
    public async Task GenerateAllBaselines()
    {
        var page = await OpenEditorAsync();

        // ═══════════════════════════════════════════════════════════════
        // RIBBON – základní taby (01–07)
        // ═══════════════════════════════════════════════════════════════
        await CaptureAsync(page, "ribbon-home");
        await TryCaptureAsync(page, "ribbon-insert", async () => await ClickTabAsync(page, "document-ribbon-tab-insert"));
        await TryCaptureAsync(page, "ribbon-layout", async () => await ClickTabAsync(page, "document-ribbon-tab-layout"));
        await TryCaptureAsync(page, "ribbon-references", async () => await ClickTabAsync(page, "document-ribbon-tab-references"));
        await TryCaptureAsync(page, "ribbon-review", async () => await ClickTabAsync(page, "document-ribbon-tab-review"));
        await TryCaptureAsync(page, "ribbon-view", async () => await ClickTabAsync(page, "document-ribbon-tab-view"));

        // ═══════════════════════════════════════════════════════════════
        // RIBBON DROPDOWNY / PICKERY v Home tabu (08–14)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "ribbon-font-family-dropdown", async () => {
            await ClickTabAsync(page, "document-ribbon-tab-home");
            await page.Locator("[data-testid='document-font-family']").ClickAsync();
            await page.WaitForTimeoutAsync(600);
        });
        await TryCaptureAsync(page, "ribbon-font-size-dropdown", async () => {
            await page.Locator("[data-testid='document-font-size']").ClickAsync();
            await page.WaitForTimeoutAsync(600);
        });
        await TryCaptureAsync(page, "ribbon-text-color-picker", async () => {
            await page.Locator("[data-testid='document-font-color-trigger']").ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "ribbon-highlight-color-picker", async () => {
            await page.Locator("[data-testid='document-highlight-color-trigger']").ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "ribbon-line-spacing-dropdown", async () => {
            await page.Locator("[data-testid='document-line-spacing']").ClickAsync();
            await page.WaitForTimeoutAsync(600);
        });
        await TryCaptureAsync(page, "ribbon-alignment-active", async () => {
            await SelectFirstInlineRangeAsync(page, 0, 5);
            await page.Locator("[data-testid='document-align-center']").ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // INSERT TAB – pickery a menu (15–19)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "insert-table-grid-picker", async () => {
            await ClickTabAsync(page, "document-ribbon-tab-insert");
            await page.Locator("[data-testid='document-toolbar-table']").ClickAsync();
            await page.WaitForSelectorAsync("[data-testid='document-table-grid-picker']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "insert-image-menu", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-insert");
            await page.Locator("[data-testid='document-toolbar-image']").ClickAsync();
            await page.WaitForTimeoutAsync(600);
        });
        await TryCaptureAsync(page, "insert-menu-dropdown", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-insert");
            await page.Locator("[data-testid='document-insert-menu']").ClickAsync();
            await page.WaitForTimeoutAsync(600);
        });

        // ═══════════════════════════════════════════════════════════════
        // LINK DIALOG (20)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "link-dialog", async () => {
            await ClickTabAsync(page, "document-ribbon-tab-home");
            await SelectFirstInlineRangeAsync(page, 0, 5);
            await page.Locator("[data-testid='document-link']").ClickAsync();
            await page.WaitForSelectorAsync("[data-testid='document-link-dialog']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // TOOLBAR MÓDY (21–23)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "toolbar-compact", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?toolbarMode=compact", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "toolbar-distraction-free", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?toolbarMode=distractionFree", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // FLOAT TOOLBARY (24–28)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "float-toolbar-text", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await SelectFirstInlineRangeAsync(page, 0, 5);
            await page.WaitForSelectorAsync("[data-testid='document-mini-toolbar']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        });
        await TryCaptureAsync(page, "context-menu-text", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(300);
            await SelectFirstInlineRangeAsync(page, 0, 5);
            await OpenSelectionContextMenuAsync(page);
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "context-menu-table", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(300);
            await InsertTableFromRibbonAsync(page, 3, 3);
            var table = page.Locator("[data-testid='document-wysiwyg-host'] table").First;
            await table.ClickAsync();
            await page.Mouse.ClickAsync(400, 400, new MouseClickOptions { Button = MouseButton.Right });
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // TABLE TOOLBAR (29)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "table-toolbar", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await InsertTableFromRibbonAsync(page, 3, 3);
            var table = page.Locator("[data-testid='document-wysiwyg-host'] table").First;
            await table.ClickAsync();
            await page.WaitForTimeoutAsync(400);
            var toolbar = page.Locator("[data-testid='document-table-toolbar']");
            if (await toolbar.IsVisibleAsync()) { /* ok */ } else { throw new Exception("Table toolbar not visible"); }
        });

        // ═══════════════════════════════════════════════════════════════
        // IMAGE – float toolbar / wrap panel (30–31)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "image-selected-with-float", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=exhibits-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] img, [data-testid='document-wysiwyg-host'] figure", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var img = page.Locator("[data-testid='document-wysiwyg-host'] img, [data-testid='document-wysiwyg-host'] figure").First;
            await img.ClickAsync();
            await page.WaitForTimeoutAsync(600);
        });
        await TryCaptureAsync(page, "image-wrap-panel", async () => {
            var wrapPanel = page.Locator("[data-testid='document-image-wrap-panel']");
            if (!await wrapPanel.IsVisibleAsync()) throw new Exception("Image wrap panel not visible");
        });

        // ═══════════════════════════════════════════════════════════════
        // SIDE PANEL – všechy taby (32–38)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "side-panel-comments", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-side-panel']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Comments')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "side-panel-revisions", async () => {
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Revisions')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "side-panel-versions", async () => {
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Versions')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "side-panel-properties", async () => {
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Properties')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "side-panel-outline", async () => {
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Outline')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "side-panel-pages", async () => {
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Pages')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "side-panel-image-inspector", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=exhibits-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] img, [data-testid='document-wysiwyg-host'] figure", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var img = page.Locator("[data-testid='document-wysiwyg-host'] img, [data-testid='document-wysiwyg-host'] figure").First;
            await img.ClickAsync();
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Properties')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // DIALOGY A PANELY (39–47)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "find-replace-panel", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var body = page.Locator("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-page__body[contenteditable]").First;
            await body.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            await page.Keyboard.PressAsync("Control+f");
            await page.WaitForTimeoutAsync(1000);
        });
        await TryCaptureAsync(page, "command-palette", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await page.Keyboard.PressAsync("Control+Shift+p");
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "compare-dialog", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-review");
            var btn = page.Locator("[data-testid='document-compare-open']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "version-dialog", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-open-versions']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "json-debug-modal", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-view-json']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "clipboard-html-debug-modal", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-view-clipboard-html']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "import-docx-panel", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-references");
            var btn = page.Locator("[data-testid='document-import-docx-label']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // BANNERY A SUMMARY (48–52)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "review-summary-banner", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var summary = page.Locator("[data-testid='document-review-summary']");
            if (!await summary.IsVisibleAsync()) throw new Exception("Review summary not visible");
        });
        await TryCaptureAsync(page, "status-bar", async () => {
            var bar = page.Locator("[data-testid='document-status-bar']");
            if (!await bar.IsVisibleAsync()) throw new Exception("Status bar not visible");
        });
        await TryCaptureAsync(page, "offline-banner", async () => {
            // Offline banner se simuluje přes evaluate – nastavíme offline state
            await page.EvaluateAsync("""
                () => {
                    const editor = document.querySelector('[data-testid="document-editor-demo"]');
                    if (editor) {
                        const banner = document.createElement('div');
                        banner.className = 'tm-document-editor__offline-banner';
                        banner.setAttribute('data-testid', 'document-offline-banner');
                        banner.innerHTML = '<div class="tm-document-editor__offline-main"><span>Offline draft available</span></div>';
                        editor.insertBefore(banner, editor.firstChild);
                    }
                }
            """);
            await page.WaitForTimeoutAsync(400);
        });
        await TryCaptureAsync(page, "format-banner", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await page.EvaluateAsync("""
                () => {
                    const editor = document.querySelector('[data-testid="document-editor-demo"]');
                    if (editor) {
                        const banner = document.createElement('div');
                        banner.className = 'tm-document-editor__format-banner';
                        banner.setAttribute('data-testid', 'document-format-message');
                        banner.innerHTML = '<span>Format changed</span>';
                        editor.insertBefore(banner, editor.firstChild);
                    }
                }
            """);
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // FULLSCREEN A TEMPLATE PREVIEW (53–54)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "fullscreen-mode", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-fullscreen']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });
        await TryCaptureAsync(page, "template-preview-mode", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-template-preview']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // DARK MODE (55)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "dark-mode-overview", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await ToggleDarkModeAsync(page);
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // TABLE PROPERTIES PANEL (56)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "table-properties-panel", async () => {
            await ToggleDarkModeAsync(page); // toggle back to light
            await page.GotoAsync($"{BaseUrl}/document-editor", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await InsertTableFromRibbonAsync(page, 3, 3);
            var table = page.Locator("[data-testid='document-wysiwyg-host'] table").First;
            await table.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            var btn = page.Locator("[data-testid='document-table-toolbar-table-properties']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // CELL PROPERTIES PANEL (57)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "cell-properties-panel", async () => {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
            var table = page.Locator("[data-testid='document-wysiwyg-host'] table").First;
            await table.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            var btn = page.Locator("[data-testid='document-table-toolbar-cell-properties']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // HEADER-FOOTER EDITING MÓD (58)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "header-footer-mode", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            // pokusíme se otevřít header/footer přes double-click na header oblast
            await page.EvaluateAsync("""
                () => {
                    const host = document.querySelector('[data-testid="document-wysiwyg-host"]');
                    const header = host?.querySelector('.tm-wysiwyg-page__header');
                    if (header) {
                        header.dispatchEvent(new MouseEvent('dblclick', { bubbles: true }));
                    }
                }
            """);
            await page.WaitForTimeoutAsync(800);
        });

        // ═══════════════════════════════════════════════════════════════
        // TOKEN MENU / AUTOCOMPLETE (59)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "token-autocomplete-menu", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var body = page.Locator("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-page__body[contenteditable]").First;
            await body.ClickAsync();
            await page.Keyboard.TypeAsync("{{");
            await page.WaitForTimeoutAsync(1000);
        });

        // ═══════════════════════════════════════════════════════════════
        // RULER VISIBLE (60)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "ribbon-view-with-ruler", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var rulerBtn = page.Locator("[data-testid='document-toggle-ruler']");
            if (await rulerBtn.IsVisibleAsync()) await rulerBtn.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // NON-PRINTING CHARACTERS (61)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "non-printing-chars", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-toggle-nonprinting']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // SHOW BLOCKS (62)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "show-blocks", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            await ClickTabAsync(page, "document-ribbon-tab-view");
            var btn = page.Locator("[data-testid='document-show-blocks']");
            if (await btn.IsVisibleAsync()) await btn.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });

        // ═══════════════════════════════════════════════════════════════
        // COMMENT COMPOSER OPEN (63)
        // ═══════════════════════════════════════════════════════════════
        await TryCaptureAsync(page, "comment-composer-open", async () => {
            await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
            await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
            await page.WaitForTimeoutAsync(600);
            var tab = page.Locator("[data-testid='document-side-panel'] [role='tab']:has-text('Comments')").First;
            if (await tab.IsVisibleAsync()) await tab.ClickAsync();
            await page.WaitForTimeoutAsync(200);
            var addBtn = page.Locator("[data-testid='document-comment-new-composer']").First;
            if (await addBtn.IsVisibleAsync()) await addBtn.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        });
    }
}
