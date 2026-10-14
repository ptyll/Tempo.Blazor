using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for file drag-and-drop on media blocks in TmNotionEditor (Phase 2.5).
/// Requires DemoNotionFileProvider registered as FileProvider.
/// </summary>
[TestClass]
public class NotionDragDropMediaE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* ignore */ }

        var context = await CreateContextAsync();
        var page    = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Inserts a block using the slash menu.
    /// Uses JS focus/cursor placement (same as TypeTriggerAsync in token tests) to avoid
    /// viewport and focus issues with Playwright's native click.
    /// </summary>
    private async Task InsertBlockViaSlashMenuAsync(IPage page, string searchTerm, string itemName)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });

        await page.EvaluateAsync(@"() => {
            const el = document.querySelector('.tm-notion-paragraph[contenteditable=""true""]');
            if (!el) return;
            el.focus();
            el.scrollIntoView({ block: 'start', behavior: 'instant' });
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }");

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1200);

        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var searchInput = page.Locator(".tm-notion-slash__input");
        await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await searchInput.ClickAsync();
        await page.Keyboard.TypeAsync(searchTerm);
        await page.WaitForTimeoutAsync(400);

        // Wait for the matching item to appear, then press Enter to select it (most reliable)
        var item = page.Locator(".tm-notion-slash__item")
                        .Filter(new() { Has = page.Locator(".tm-notion-slash__item-name").Filter(new() { HasText = itemName }) })
                        .First;
        await item.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(2000);
    }

    /// <summary>
    /// Simulates a file drop by using JS to dispatch a drop event with a mock DataTransfer
    /// containing a file created from a data URL.
    /// </summary>
    private async Task SimulateFileDropAsync(IPage page, ILocator dropZone, string fileName, string mimeType, string base64Content)
    {
        var element = await dropZone.ElementHandleAsync();
        if (element == null) return;

        await page.EvaluateAsync(
            """
            (args) => {
                const el = args.element;
                if (!el) return;

                // Create a File object from base64
                const byteString = atob(args.base64);
                const ab = new ArrayBuffer(byteString.length);
                const ia = new Uint8Array(ab);
                for (let i = 0; i < byteString.length; i++) ia[i] = byteString.charCodeAt(i);
                const blob = new Blob([ab], { type: args.mimeType });
                const file = new File([blob], args.fileName, { type: args.mimeType });

                // Build DataTransfer
                const dt = new DataTransfer();
                Object.defineProperty(dt, 'files', {
                    value: [file],
                    writable: false
                });
                Object.defineProperty(dt, 'items', {
                    value: [{ kind: 'file', type: args.mimeType, getAsFile: () => file }],
                    writable: false
                });

                el.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: dt }));
                el.dispatchEvent(new DragEvent('dragover',  { bubbles: true, cancelable: true, dataTransfer: dt }));
                el.dispatchEvent(new DragEvent('drop',      { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """,
            new { element, base64 = base64Content, fileName, mimeType });

        await page.WaitForTimeoutAsync(2000);
    }

    // A tiny 1×1 transparent PNG in base64
    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

    // A minimal valid PDF in base64 (about 200 bytes, just enough to be a PDF)
    private const string TinyPdfBase64 =
        "JVBERi0xLjAKMSAwIG9iago8PCAvVHlwZSAvQ2F0YWxvZyAvUGFnZXMgMiAwIFIgPj4KZW5kb2JqCjIgMCBvYmoK" +
        "PDwgL1R5cGUgL1BhZ2VzIC9LaWRzIFszIDAgUl0gL0NvdW50IDEgPj4KZW5kb2JqCjMgMCBvYmoKPDwgL1R5cGUg" +
        "L1BhZ2UgL1BhcmVudCAyIDAgUiAvTWVkaWFCb3ggWzAgMCAzIDNdID4+CmVuZG9iagp4cmVmCjAgNAowMDAwMDAw" +
        "MDAwIDY1NTM1IGYgCjAwMDAwMDAwMDkgMDAwMDAgbiAKMDAwMDAwMDA1OCAwMDAwMCBuIAowMDAwMDAwMTE1IDAw" +
        "MDAwIG4gCnRyYWlsZXIKPDwgL1NpemUgNCAvUm9vdCAxIDAgUiA+PgpzdGFydHhyZWYKMTkwCiUlRU9G";

    // Minimal text file content in base64
    private const string TinyTextBase64 = "SGVsbG8gV29ybGQ="; // "Hello World"

    // ══════════════════════════════════════════════════════════════════════════
    //  Phase 2.5 tests
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Dragging over an empty image block activates the drop overlay visual indicator")]
    public async Task ImageBlock_DragOver_ShowsActiveOverlay()
    {
        var page = await OpenNotionEditorAsync();
        await InsertBlockViaSlashMenuAsync(page, "image", "Image");

        // The newly inserted image block is empty so it has .tm-notion-drop-zone;
        // pre-existing image blocks already have images and no drop zone.
        var dropZone = page.Locator(".tm-notion-drop-zone").First;
        await dropZone.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Simulate dragenter
        await page.EvaluateAsync(
            """
            (el) => {
                const dt = new DataTransfer();
                el.dispatchEvent(new DragEvent('dragenter', { bubbles: true, cancelable: true, dataTransfer: dt }));
            }
            """,
            await dropZone.ElementHandleAsync());

        await page.WaitForTimeoutAsync(400);

        var overlay = dropZone.Locator(".tm-notion-drop-overlay").First;
        var hasActive = await overlay.EvaluateAsync<bool>(
            "el => el.classList.contains('tm-notion-drop-overlay--active')");
        Assert.IsTrue(hasActive, "Drop overlay should have --active class while dragging");

        await TakeScreenshotAsync(page, "image_block_drag_active_overlay");
    }

    [TestMethod]
    [Description("Dragging a PNG file onto an empty image block uploads and displays the image")]
    public async Task ImageBlock_DropFile_UploadsAndDisplaysImage()
    {
        var page = await OpenNotionEditorAsync();
        await InsertBlockViaSlashMenuAsync(page, "image", "Image");

        var dropZone = page.Locator(".tm-notion-drop-zone").First;
        await dropZone.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        await SimulateFileDropAsync(page, dropZone, "test.png", "image/png", TinyPngBase64);

        // Image should now be visible in the block
        var img = page.Locator(".tm-notion-image-block__img").First;
        await img.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var src = await img.GetAttributeAsync("src");
        Assert.IsTrue(!string.IsNullOrEmpty(src), "Image src should be set after file drop");

        await TakeScreenshotAsync(page, "image_block_drop_uploaded");
    }

    [TestMethod]
    [Description("Dragging a PDF file onto an empty PDF block uploads and shows the PDF viewer")]
    public async Task PdfBlock_DropFile_UploadsAndShowsViewer()
    {
        var page = await OpenNotionEditorAsync();
        await InsertBlockViaSlashMenuAsync(page, "pdf", "PDF");

        // The newly inserted PDF block is empty — it has .tm-notion-drop-zone.
        // Pre-existing PDF block has a URL so it has no drop zone.
        var dropZone = page.Locator(".tm-notion-drop-zone").First;
        await dropZone.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        await SimulateFileDropAsync(page, dropZone, "test.pdf", "application/pdf", TinyPdfBase64);

        // After the drop, the PDF upload zone should have disappeared
        await page.WaitForTimeoutAsync(1000);
        var uploadZone = page.Locator(".tm-notion-media-upload-zone--pdf");
        var stillEmpty = await uploadZone.IsVisibleAsync();
        Assert.IsFalse(stillEmpty, "PDF upload zone should no longer be visible after file drop");

        await TakeScreenshotAsync(page, "pdf_block_drop_uploaded");
    }

    [TestMethod]
    [Description("Dragging a file onto an empty file block uploads and shows the download card")]
    public async Task FileBlock_DropFile_UploadsAndShowsDownloadCard()
    {
        var page = await OpenNotionEditorAsync();
        await InsertBlockViaSlashMenuAsync(page, "attachment", "File");

        // The newly inserted file block is empty — it has .tm-notion-drop-zone.
        // Pre-existing File block has a URL so it has no drop zone.
        var dropZone = page.Locator(".tm-notion-drop-zone").First;
        await dropZone.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        await SimulateFileDropAsync(page, dropZone, "document.txt", "text/plain", TinyTextBase64);

        // After the drop, download link should appear
        var downloadLink = page.Locator(".tm-notion-file-block__download").First;
        await downloadLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await downloadLink.IsVisibleAsync(), "Download link should be visible after file drop");

        await TakeScreenshotAsync(page, "file_block_drop_uploaded");
    }
}
