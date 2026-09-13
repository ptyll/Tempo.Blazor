using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Generates baseline screenshots for the TmDocumentEditor component covering
/// all ribbon tabs, toolbar modes, floating toolbars, context menus, side panels,
/// dialogs, and the overall Blazor shell.
/// </summary>
[TestClass]
public class DocumentEditorBaselineScreenshots : BaselineGeneratorTestBase
{
    private static string OutputDir
    {
        get
        {
            var dir = BaselineOutput.DirectoryFor(null, "document-editor");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

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
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Attached,
            Timeout = 60000
        });
        await page.WaitForTimeoutAsync(600);
        return page;
    }

    private static async Task CaptureAsync(IPage page, string fileName, ILocator? locator = null)
    {
        await page.WaitForTimeoutAsync(400);
        var path = Path.Combine(OutputDir, fileName);
        if (locator is not null)
        {
            await locator.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = path,
                Type = ScreenshotType.Png,
                OmitBackground = false
            });
        }
        else
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = path,
                Type = ScreenshotType.Png,
                FullPage = false
            });
        }
        Console.WriteLine($"[baseline] wrote {path}");
    }

    private static async Task ClickTabAsync(IPage page, string tabTestId)
    {
        await page.Locator($"[data-testid='{tabTestId}']").ClickAsync();
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task OpenSidePanelTabAsync(IPage page, string tabName)
    {
        var tab = page.Locator($"[data-testid='document-side-panel'] [role='tab']:has-text('{tabName}')").First;
        if (await tab.IsVisibleAsync())
        {
            await tab.ClickAsync();
            await page.WaitForTimeoutAsync(400);
        }
    }

    private static async Task<string> SelectFirstInlineRangeAsync(IPage page, int start, int end)
    {
        return await page.EvaluateAsync<string>(
            """
            ({ start, end }) => {
                const host = document.querySelector('[data-testid="document-wysiwyg-host"]');
                const isVisible = el => {
                    if (!el || el.closest('[aria-hidden="true"], .tm-wysiwyg-page--virtual')) return false;
                    const rect = el.getBoundingClientRect();
                    const style = getComputedStyle(el);
                    return rect.width > 0
                        && rect.height > 0
                        && style.visibility !== 'hidden'
                        && style.display !== 'none';
                };
                const paragraphBlocks = Array.from(host?.querySelectorAll('.tm-wysiwyg-page__body p.tm-wysiwyg-block') || [])
                    .filter(isVisible);
                const block = paragraphBlocks[1] || paragraphBlocks[0]
                    || Array.from(host?.querySelectorAll('.tm-wysiwyg-page__body [data-block-id]') || []).find(isVisible);
                if (!block) {
                    const inline = Array.from(host?.querySelectorAll('.tm-wysiwyg-page__body [data-inline-id]') || []).find(isVisible);
                    if (inline) {
                        const resolveInline = absoluteOffset => {
                            const walker = document.createTreeWalker(inline, NodeFilter.SHOW_TEXT);
                            let current = 0;
                            let node;
                            while ((node = walker.nextNode())) {
                                const length = node.textContent.length;
                                if (absoluteOffset <= current + length) {
                                    return { node, offset: Math.max(0, Math.min(absoluteOffset - current, length)) };
                                }
                                current += length;
                            }
                            return null;
                        };
                        const textLength = inline.textContent.length;
                        const rangeStart = Math.max(0, Math.min(start, textLength));
                        const rangeEnd = Math.max(rangeStart, Math.min(end, textLength));
                        const startPos = resolveInline(rangeStart);
                        const endPos = resolveInline(rangeEnd);
                        if (!startPos || !endPos) {
                            throw new Error('Editable inline text node was not found.');
                        }

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
                    let current = 0;
                    let node;
                    while ((node = walker.nextNode())) {
                        const length = node.textContent.length;
                        if (absoluteOffset <= current + length) {
                            return { node, offset: Math.max(0, Math.min(absoluteOffset - current, length)) };
                        }
                        current += length;
                    }
                    return null;
                };

                const textLength = block.textContent.length;
                const rangeStart = Math.max(0, Math.min(start, textLength));
                const rangeEnd = Math.max(rangeStart, Math.min(end, textLength));
                const startPos = resolve(rangeStart);
                const endPos = resolve(rangeEnd);
                if (!startPos || !endPos) {
                    throw new Error('Editable text node was not found.');
                }

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
        await page.EvaluateAsync(
            """
            () => {
                const selection = window.getSelection();
                if (!selection || selection.rangeCount === 0) {
                    throw new Error('Selection is required before opening the context menu.');
                }

                const range = selection.getRangeAt(0);
                const rect = range.getBoundingClientRect();
                const x = Math.max(8, rect.left + Math.min(12, Math.max(1, rect.width / 2)));
                const y = Math.max(8, rect.top + Math.min(12, Math.max(1, rect.height / 2)));
                const target = document.elementFromPoint(x, y)
                    || selection.anchorNode?.parentElement
                    || document.querySelector('[data-testid="document-wysiwyg-host"] [data-inline-id]');
                target.dispatchEvent(new MouseEvent('contextmenu', {
                    bubbles: true,
                    cancelable: true,
                    button: 2,
                    clientX: x,
                    clientY: y
                }));
            }
            """);
    }

    private static async Task ClickOnTableAsync(IPage page)
    {
        var table = page.Locator("[data-testid='document-wysiwyg-host'] table").First;
        await table.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await table.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task ClickOnImageAsync(IPage page)
    {
        var img = page.Locator("[data-testid='document-wysiwyg-host'] img, [data-testid='document-wysiwyg-host'] figure").First;
        await img.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await img.ClickAsync();
        await page.WaitForTimeoutAsync(300);
    }

    [TestMethod]
    public async Task GenerateAllBaselines()
    {
        // ═══════════════════════════════════════════════════════════════
        // 1. RIBBON HOME (default)
        // ═══════════════════════════════════════════════════════════════
        var page = await OpenEditorAsync();
        await CaptureAsync(page, "01-ribbon-home.png");

        // ═══════════════════════════════════════════════════════════════
        // 2. RIBBON INSERT
        // ═══════════════════════════════════════════════════════════════
        await ClickTabAsync(page, "document-ribbon-tab-insert");
        await CaptureAsync(page, "02-ribbon-insert.png");

        // ═══════════════════════════════════════════════════════════════
        // 3. RIBBON REFERENCES
        // ═══════════════════════════════════════════════════════════════
        await ClickTabAsync(page, "document-ribbon-tab-references");
        await CaptureAsync(page, "03-ribbon-references.png");

        // ═══════════════════════════════════════════════════════════════
        // 4. RIBBON REVIEW
        // ═══════════════════════════════════════════════════════════════
        await ClickTabAsync(page, "document-ribbon-tab-review");
        await CaptureAsync(page, "04-ribbon-review.png");

        // ═══════════════════════════════════════════════════════════════
        // 5. RIBBON VIEW
        // ═══════════════════════════════════════════════════════════════
        await ClickTabAsync(page, "document-ribbon-tab-view");
        await CaptureAsync(page, "05-ribbon-view.png");

        // ═══════════════════════════════════════════════════════════════
        // 6. COMPACT TOOLBAR
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?toolbarMode=compact", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(800);
        await CaptureAsync(page, "06-toolbar-compact.png");

        // ═══════════════════════════════════════════════════════════════
        // 7. DISTRACTION-FREE
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?toolbarMode=distractionFree", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(800);
        await CaptureAsync(page, "07-toolbar-distraction-free.png");

        // ═══════════════════════════════════════════════════════════════
        // 8. MINI TOOLBAR (text selection)
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-block", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(600);
        await SelectFirstInlineRangeAsync(page, 0, 5);
        await page.WaitForSelectorAsync("[data-testid='document-mini-toolbar']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await CaptureAsync(page, "08-float-toolbar-text.png");

        // ═══════════════════════════════════════════════════════════════
        // 9. TEXT CONTEXT MENU
        // ═══════════════════════════════════════════════════════════════
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        await SelectFirstInlineRangeAsync(page, 0, 5);
        await OpenSelectionContextMenuAsync(page);
        await page.WaitForTimeoutAsync(1200);
        await CaptureAsync(page, "09-context-menu-text.png");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        // ═══════════════════════════════════════════════════════════════
        // 10. TABLE CONTEXT MENU (skipped – table-demo not available in API)
        // ═══════════════════════════════════════════════════════════════
        // Table context menu would show: insert row/col, merge cells, split cell,
        // toggle header, delete row/col. Documented from source inspection instead.
        await page.WaitForTimeoutAsync(100);

        // ═══════════════════════════════════════════════════════════════
        // 11. SIDE PANEL – COMMENTS
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-side-panel']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(600);
        await OpenSidePanelTabAsync(page, "Comments");
        await CaptureAsync(page, "11-side-panel-comments.png");

        // ═══════════════════════════════════════════════════════════════
        // 12. SIDE PANEL – REVISIONS
        // ═══════════════════════════════════════════════════════════════
        await OpenSidePanelTabAsync(page, "Revisions");
        await CaptureAsync(page, "12-side-panel-revisions.png");

        // ═══════════════════════════════════════════════════════════════
        // 13. SIDE PANEL – VERSIONS
        // ═══════════════════════════════════════════════════════════════
        await OpenSidePanelTabAsync(page, "Versions");
        await CaptureAsync(page, "13-side-panel-versions.png");

        // ═══════════════════════════════════════════════════════════════
        // 14. SIDE PANEL – PROPERTIES
        // ═══════════════════════════════════════════════════════════════
        await OpenSidePanelTabAsync(page, "Properties");
        await CaptureAsync(page, "14-side-panel-properties.png");

        // ═══════════════════════════════════════════════════════════════
        // 15. SIDE PANEL – OUTLINE
        // ═══════════════════════════════════════════════════════════════
        await OpenSidePanelTabAsync(page, "Outline");
        await CaptureAsync(page, "15-side-panel-outline.png");

        // ═══════════════════════════════════════════════════════════════
        // 16. SIDE PANEL – PAGES
        // ═══════════════════════════════════════════════════════════════
        await OpenSidePanelTabAsync(page, "Pages");
        await CaptureAsync(page, "16-side-panel-pages.png");

        // ═══════════════════════════════════════════════════════════════
        // 17. SIDE PANEL – IMAGE INSPECTOR
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId=exhibits-demo", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-wysiwyg-host'] img, [data-testid='document-wysiwyg-host'] figure", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(600);
        await ClickOnImageAsync(page);
        await OpenSidePanelTabAsync(page, "Properties");
        await page.WaitForTimeoutAsync(400);
        await CaptureAsync(page, "17-side-panel-image-inspector.png");

        // ═══════════════════════════════════════════════════════════════
        // 18. FIND / REPLACE PANEL
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(600);
        var docBody2 = page.Locator("[data-testid='document-wysiwyg-host'] .tm-wysiwyg-page__body[contenteditable]").First;
        await docBody2.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.PressAsync("Control+f");
        await page.WaitForTimeoutAsync(800);
        try
        {
            await page.WaitForSelectorAsync("[data-testid='document-find-panel']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 4000 });
        }
        catch { /* fallback */ }
        await CaptureAsync(page, "18-find-replace-panel.png");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        // ═══════════════════════════════════════════════════════════════
        // 19. COMMAND PALETTE
        // ═══════════════════════════════════════════════════════════════
        await page.Keyboard.PressAsync("Control+Shift+p");
        await page.WaitForSelectorAsync("[data-testid='document-command-palette']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await CaptureAsync(page, "19-command-palette.png");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);

        // ═══════════════════════════════════════════════════════════════
        // 20. REVIEW SUMMARY
        // ═══════════════════════════════════════════════════════════════
        var reviewSummary = page.Locator("[data-testid='document-review-summary']");
        if (await reviewSummary.IsVisibleAsync())
        {
            await CaptureAsync(page, "20-review-summary.png");
        }

        // ═══════════════════════════════════════════════════════════════
        // 21. STATUS BAR
        // ═══════════════════════════════════════════════════════════════
        var statusBar = page.Locator("[data-testid='document-status-bar']");
        if (await statusBar.IsVisibleAsync())
        {
            await CaptureAsync(page, "21-status-bar.png", statusBar);
        }

        // ═══════════════════════════════════════════════════════════════
        // 22. FULL SHELL (overview)
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId=filing-demo", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(800);
        await CaptureAsync(page, "22-full-shell-filing.png");

        // ═══════════════════════════════════════════════════════════════
        // 23. COMPARE DIALOG (trigger via toolbar)
        // ═══════════════════════════════════════════════════════════════
        await ClickTabAsync(page, "document-ribbon-tab-review");
        var compareBtn = page.Locator("[data-testid='document-compare-documents']");
        if (await compareBtn.IsVisibleAsync())
        {
            await compareBtn.ClickAsync();
            await page.WaitForTimeoutAsync(600);
            var dialog = page.Locator("[data-testid='document-compare-dialog']").First;
            if (await dialog.IsVisibleAsync())
            {
                await CaptureAsync(page, "23-compare-dialog.png");
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(200);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 24. VERSION DIALOG
        // ═══════════════════════════════════════════════════════════════
        await ClickTabAsync(page, "document-ribbon-tab-view");
        var versionsBtn = page.Locator("[data-testid='document-open-versions']");
        if (await versionsBtn.IsVisibleAsync())
        {
            await versionsBtn.ClickAsync();
            await page.WaitForTimeoutAsync(400);
            var versionDialog = page.Locator("[data-testid='document-version-dialog']").First;
            if (await versionDialog.IsVisibleAsync())
            {
                await CaptureAsync(page, "24-version-dialog.png");
                await page.Keyboard.PressAsync("Escape");
                await page.WaitForTimeoutAsync(200);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // 25. RIBBON WITH RULER VISIBLE
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(600);
        await ClickTabAsync(page, "document-ribbon-tab-view");
        var rulerBtn = page.Locator("[data-testid='document-toggle-ruler']");
        if (await rulerBtn.IsVisibleAsync())
        {
            await rulerBtn.ClickAsync();
            await page.WaitForTimeoutAsync(400);
            await CaptureAsync(page, "25-ribbon-view-ruler.png");
        }

        // ═══════════════════════════════════════════════════════════════
        // 26. DARK MODE OVERVIEW
        // ═══════════════════════════════════════════════════════════════
        await page.GotoAsync($"{BaseUrl}/document-editor?documentId=contract-demo", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await page.WaitForSelectorAsync("[data-testid='document-editor-demo']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
        await page.WaitForTimeoutAsync(600);
        await ToggleDarkModeAsync(page);
        await page.WaitForTimeoutAsync(600);
        await CaptureAsync(page, "26-dark-mode-overview.png");
        await ToggleDarkModeAsync(page); // toggle back
    }
}
