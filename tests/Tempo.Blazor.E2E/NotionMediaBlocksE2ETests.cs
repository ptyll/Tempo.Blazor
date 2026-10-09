using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests covering media and embed blocks in the Notion editor.
/// Phase 7: Image, Video, Bookmark, Embed, File, Audio blocks.
/// </summary>
[TestClass]
public class NotionMediaBlocksE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = new HttpClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* API may not be running; ignore */ }

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Inserts a block via the slash menu. Searches for <paramref name="searchTerm"/>
    /// and clicks the item whose visible name matches <paramref name="itemName"/>.
    /// </summary>
    private async Task<ILocator> InsertBlockViaSlashMenuAsync(IPage page, string searchTerm, string itemName, string expectedBlockType)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await para.ClickAsync();
        await page.Keyboard.PressAsync("End");

        var sourceBlockId = await para.EvaluateAsync<string?>(
            "el => el.closest('[data-notion-block]')?.getAttribute('data-block-id')");
        Assert.IsFalse(string.IsNullOrWhiteSpace(sourceBlockId), "The clicked paragraph must carry a block id.");

        await page.Keyboard.PressAsync("Enter");
        // The Enter split is an async aggregate-session round-trip; keystrokes typed meanwhile
        // are buffered and replayed into the new block once it exists. A fixed delay can still
        // observe the PRE-split block — the recorded id then never changes type, because the
        // buffered "/" + selection convert the real new block instead (deterministic timeout).
        await NotionE2ETestBase.WaitForBlockFocusToMoveAsync(page, sourceBlockId!);

        var insertedBlockId = await page.EvaluateAsync<string?>("""
            () => document.activeElement?.closest?.('[data-notion-block]')?.getAttribute('data-block-id')
            """);
        Assert.IsFalse(string.IsNullOrWhiteSpace(insertedBlockId), "A fresh editable block should be focused after pressing Enter.");

        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var searchInput = page.Locator(".tm-notion-slash__input");
        await searchInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await searchInput.ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.TypeAsync(searchTerm);
        await page.WaitForTimeoutAsync(600);

        var item = page.Locator(".tm-notion-slash__item")
                        .Filter(new() { Has = page.Locator(".tm-notion-slash__item-name").Filter(new() { HasText = itemName }) })
                        .First;
        await item.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await item.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(800);

        var insertedBlock = page.Locator($"[data-block-id='{insertedBlockId}']").First;
        await insertedBlock.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        // Conversion persists through the aggregate session, which serializes mutations behind
        // any in-flight autosave — under a contended runner the full click → queued save →
        // re-render chain exceeded both the old 5s and the 20s window even though the conversion
        // completes correctly, so the wait uses the suite's 60s contended-wait convention.
        await page.WaitForFunctionAsync(
            "args => document.querySelector(`[data-block-id='${args.id}']`)?.getAttribute('data-block-type') === args.type",
            new { id = insertedBlockId, type = expectedBlockType },
            new PageWaitForFunctionOptions { Timeout = 60000 });

        return insertedBlock;
    }

    /// <summary>
    /// Opens the media upload dialog, switches to the Embed tab, types the URL,
    /// and confirms. Used for Image, Video, Audio, File blocks.
    /// </summary>
    private async Task SetMediaUrlViaDialogAsync(IPage page, ILocator block, string url)
    {
        var uploadZone = block.Locator(".tm-notion-media-upload-zone").First;
        await uploadZone.ClickAsync();

        // Wait for dialog
        var dialog = page.Locator(".tm-media-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        // Determine number of tabs (Upload may be hidden when ITmFileProvider is not registered)
        var tabs = dialog.Locator(".tm-media-dialog__tab");
        var tabCount = await tabs.CountAsync();
        if (tabCount > 1)
        {
            // Switch to Embed tab if Upload is active
            var embedTab = tabs.Nth(1);
            var isEmbedActive = await embedTab.EvaluateAsync<bool>("el => el.classList.contains('tm-media-dialog__tab--active')");
            if (!isEmbedActive)
            {
                await embedTab.ClickAsync();
                await page.WaitForTimeoutAsync(300);
            }
        }
        // When tabCount == 1, only the Embed tab exists and is already active

        // Type URL and confirm
        var urlInput = dialog.Locator(".tm-media-dialog__url-input");
        await urlInput.FillAsync(url);
        await page.WaitForTimeoutAsync(200);

        var confirmBtn = dialog.Locator(".tm-media-dialog__embed-btn");
        await confirmBtn.ClickAsync();
        await page.WaitForTimeoutAsync(800);
    }

    /// <summary>
    /// Types a URL into a bookmark block's inline input and confirms.
    /// </summary>
    private async Task SetBookmarkUrlAsync(IPage page, ILocator block, string url)
    {
        var input = block.Locator(".tm-notion-bookmark-block__url-input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await input.FillAsync(url);
        await page.WaitForTimeoutAsync(200);

        var confirmBtn = block.Locator(".tm-notion-bookmark-block__confirm-btn");
        await confirmBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1200);
    }

    /// <summary>
    /// Types a URL into an embed block's inline input and confirms.
    /// </summary>
    private async Task SetEmbedUrlAsync(IPage page, ILocator block, string url)
    {
        var input = block.Locator(".tm-notion-embed-block__url-input");
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await input.FillAsync(url);
        await page.WaitForTimeoutAsync(200);

        var confirmBtn = block.Locator(".tm-notion-embed-block__confirm-btn");
        await confirmBtn.ClickAsync();
        await page.WaitForTimeoutAsync(1200);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Image Block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Empty image block shows upload zone button")]
    public async Task ImageBlock_Empty_ShowsUploadZone()
    {
        var page = await OpenNotionEditorAsync();
        var imageBlock = await InsertBlockViaSlashMenuAsync(page, "image", "Image", "Image");

        var uploadZone = imageBlock.Locator(".tm-notion-media-upload-zone--image");
        await uploadZone.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await uploadZone.IsVisibleAsync(), "Upload zone should be visible for empty image block");

        await TakeScreenshotAsync(page, "image_block_empty");
    }

    [TestMethod]
    [Description("Entering an image URL displays the image")]
    public async Task ImageBlock_EnterUrl_DisplaysImage()
    {
        var page = await OpenNotionEditorAsync();
        var imageBlock = await InsertBlockViaSlashMenuAsync(page, "image", "Image", "Image");

        await SetMediaUrlViaDialogAsync(page, imageBlock, "https://via.placeholder.com/150");

        var img = imageBlock.Locator(".tm-notion-image-block__img");
        await img.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 8000 });
        var src = await img.GetAttributeAsync("src");
        Assert.IsTrue(!string.IsNullOrEmpty(src), "Image src should be set");
        StringAssert.Contains(src, "placeholder.com", "Image src should contain the placeholder domain");

        await TakeScreenshotAsync(page, "image_block_with_url");
    }

    [TestMethod]
    [Description("Image caption is editable")]
    public async Task ImageBlock_Caption_Editable()
    {
        var page = await OpenNotionEditorAsync();
        // Use the existing image block from demo data (it already has a caption)
        var imageBlock = page.Locator("[data-block-type='Image']").First;
        var caption = imageBlock.Locator(".tm-notion-image-block__caption");
        await caption.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var contentEditable = await caption.GetAttributeAsync("contenteditable");
        Assert.AreEqual("true", contentEditable, "Caption should be contenteditable");

        await TakeScreenshotAsync(page, "image_block_caption");
    }

    [TestMethod]
    [Description("Resize handle appears when hovering over an image")]
    public async Task ImageBlock_Resize_HandleVisible()
    {
        var page = await OpenNotionEditorAsync();
        // Use the existing image block from demo data (it has a width, so resize handle is active)
        var imageBlock = page.Locator("[data-block-type='Image']").First;
        var imgWrap = imageBlock.Locator(".tm-notion-image-block__img-wrap");
        await imgWrap.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await imgWrap.HoverAsync();
        await page.WaitForTimeoutAsync(600);

        // The resize handle is injected by JS into the img-wrap
        var handle = imgWrap.Locator(".tm-notion-resize-handle");
        var count = await handle.CountAsync();
        Assert.AreEqual(1, count, "Resize handle should be injected into the image wrap");

        await TakeScreenshotAsync(page, "image_block_resize_handle");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Video Block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Entering a YouTube URL shows an iframe embed")]
    public async Task VideoBlock_EnterUrl_ShowsEmbed()
    {
        var page = await OpenNotionEditorAsync();
        var videoBlock = await InsertBlockViaSlashMenuAsync(page, "video", "Video", "Video");

        await SetMediaUrlViaDialogAsync(page, videoBlock, "https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        // The block renders either an iframe (YouTube/Vimeo/Loom) or a native <video> tag.
        var iframe = videoBlock.Locator(".tm-notion-video-block__embed");
        var video  = videoBlock.Locator(".tm-notion-video-block__video");

        // Use CountAsync first to stabilise the locator before attribute assertions
        var iframeCount = await iframe.CountAsync();
        var videoCount  = await video.CountAsync();
        Assert.IsTrue(iframeCount > 0 || videoCount > 0,
            "Video block should contain either an iframe embed or a native <video> element after setting URL");

        if (iframeCount > 0)
        {
            var src = await iframe.GetAttributeAsync("src");
            Assert.IsTrue(!string.IsNullOrEmpty(src), "Embed iframe src should be set");
            StringAssert.Contains(src, "youtube", "Embed src should contain youtube domain");
        }
        else
        {
            var src = await video.GetAttributeAsync("src");
            Assert.IsTrue(!string.IsNullOrEmpty(src), "Video src should be set");
        }

        await TakeScreenshotAsync(page, "video_block_embed");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Bookmark Block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Entering a URL shows bookmark preview card")]
    public async Task BookmarkBlock_EnterUrl_ShowsPreview()
    {
        var page = await OpenNotionEditorAsync();
        var bookmarkBlock = await InsertBlockViaSlashMenuAsync(page, "bookmark", "Web bookmark", "Bookmark");

        await SetBookmarkUrlAsync(page, bookmarkBlock, "https://example.com");

        var card = bookmarkBlock.Locator(".tm-notion-bookmark-block__card");
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await card.IsVisibleAsync(), "Bookmark card should be visible after resolving URL");

        var domain = bookmarkBlock.Locator(".tm-notion-bookmark-block__domain");
        var domainText = await domain.InnerTextAsync();
        StringAssert.Contains(domainText, "example.com", "Bookmark domain should display the URL host");

        await TakeScreenshotAsync(page, "bookmark_block_preview");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Embed Block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Entering a URL shows an iframe")]
    public async Task EmbedBlock_EnterUrl_ShowsIframe()
    {
        var page = await OpenNotionEditorAsync();
        var embedBlock = await InsertBlockViaSlashMenuAsync(page, "embed", "Embed", "Embed");

        await SetEmbedUrlAsync(page, embedBlock, "https://example.com");

        var iframe = embedBlock.Locator(".tm-notion-embed-block__frame");
        await iframe.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await iframe.IsVisibleAsync(), "Embed iframe should be visible after confirming URL");
        var src = await iframe.GetAttributeAsync("src");
        Assert.IsTrue(!string.IsNullOrEmpty(src), "Embed iframe src should be set");

        await TakeScreenshotAsync(page, "embed_block_iframe");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  File Block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("File block with URL shows download link")]
    public async Task FileBlock_Shows_DownloadLink()
    {
        var page = await OpenNotionEditorAsync();
        var fileBlock = await InsertBlockViaSlashMenuAsync(page, "file", "File", "File");

        await SetMediaUrlViaDialogAsync(page, fileBlock, "https://www.w3.org/WAI/ER/tests/xhtml/testfiles/resources/pdf/dummy.pdf");

        var downloadLink = fileBlock.Locator(".tm-notion-file-block__download");
        await downloadLink.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await downloadLink.IsVisibleAsync(), "Download link should be visible for file block");
        var href = await downloadLink.GetAttributeAsync("href");
        Assert.IsTrue(!string.IsNullOrEmpty(href), "Download link href should be set");
        StringAssert.Contains(href, "dummy.pdf", "Download link should point to the file URL");

        await TakeScreenshotAsync(page, "file_block_download");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Audio Block
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Entering an audio URL shows the audio player")]
    public async Task AudioBlock_EnterUrl_ShowsPlayer()
    {
        var page = await OpenNotionEditorAsync();
        var audioBlock = await InsertBlockViaSlashMenuAsync(page, "audio", "Audio", "Audio");

        await SetMediaUrlViaDialogAsync(page, audioBlock, "https://www.soundhelix.com/examples/mp3/SoundHelix-Song-1.mp3");

        // SoundCloud URL would render iframe; generic MP3 renders native <audio>
        // With our detector the URL is generic, so we expect <audio> element.
        await page.WaitForTimeoutAsync(800);
        var audio = audioBlock.Locator(".tm-notion-audio-block__audio");
        var embed = audioBlock.Locator(".tm-notion-audio-block__embed");

        var audioCount = await audio.CountAsync();
        var embedCount = await embed.CountAsync();

        Assert.IsTrue(audioCount > 0 || embedCount > 0,
            "Audio player (native <audio> or iframe embed) should be visible");

        await TakeScreenshotAsync(page, "audio_block_player");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Media Library tab
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Upload dialog shows Library tab when MediaLibraryProvider is registered")]
    public async Task MediaLibrary_LibraryTab_IsVisible()
    {
        var page = await OpenNotionEditorAsync();
        var imageBlock = await InsertBlockViaSlashMenuAsync(page, "image", "Image", "Image");

        var uploadZone = imageBlock.Locator(".tm-notion-media-upload-zone").First;
        await uploadZone.ClickAsync();

        var dialog = page.Locator(".tm-media-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var libraryTab = dialog.Locator("[data-tab='library']");
        await libraryTab.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 3000 });

        await TakeScreenshotAsync(page, "media_library_tab_visible");
    }

    [TestMethod]
    [Description("Library tab shows grid of images from DemoNotionMediaLibraryProvider")]
    public async Task MediaLibrary_LibraryTab_ShowsItems()
    {
        var page = await OpenNotionEditorAsync();
        var imageBlock = await InsertBlockViaSlashMenuAsync(page, "image", "Image", "Image");

        var uploadZone = imageBlock.Locator(".tm-notion-media-upload-zone").First;
        await uploadZone.ClickAsync();

        var dialog = page.Locator(".tm-media-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var libraryTab = dialog.Locator("[data-tab='library']");
        await libraryTab.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var items = dialog.Locator(".tm-media-library__item");
        var count = await items.CountAsync();
        Assert.IsTrue(count > 0, "Library grid should contain at least one item");

        await TakeScreenshotAsync(page, "media_library_items");
    }

    [TestMethod]
    [Description("Clicking a library item closes the dialog and sets the image on the block")]
    public async Task MediaLibrary_SelectItem_SetsImageOnBlock()
    {
        var page = await OpenNotionEditorAsync();
        var imageBlock = await InsertBlockViaSlashMenuAsync(page, "image", "Image", "Image");

        var uploadZone = imageBlock.Locator(".tm-notion-media-upload-zone").First;
        await uploadZone.ClickAsync();

        var dialog = page.Locator(".tm-media-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var libraryTab = dialog.Locator("[data-tab='library']");
        await libraryTab.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var firstItem = dialog.Locator(".tm-media-library__item").First;
        await firstItem.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await firstItem.ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        // Dialog should close and image should appear on block
        var dialogAfter = page.Locator(".tm-media-dialog");
        var dialogVisible = await dialogAfter.IsVisibleAsync();
        Assert.IsFalse(dialogVisible, "Dialog should close after selecting a library item");

        var img = imageBlock.Locator(".tm-notion-image-block__img");
        await img.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        await TakeScreenshotAsync(page, "media_library_image_set");
    }

    [TestMethod]
    [Description("Library search input filters items")]
    public async Task MediaLibrary_Search_FiltersItems()
    {
        var page = await OpenNotionEditorAsync();
        var imageBlock = await InsertBlockViaSlashMenuAsync(page, "image", "Image", "Image");

        var uploadZone = imageBlock.Locator(".tm-notion-media-upload-zone").First;
        await uploadZone.ClickAsync();

        var dialog = page.Locator(".tm-media-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var libraryTab = dialog.Locator("[data-tab='library']");
        await libraryTab.ClickAsync();
        await page.WaitForTimeoutAsync(600);

        var countBefore = await dialog.Locator(".tm-media-library__item").CountAsync();

        // Type a very specific query that matches only one item
        var searchInput = dialog.Locator(".tm-media-library__search-input");
        await searchInput.FillAsync("Mountain");
        await page.WaitForTimeoutAsync(500);

        var countAfter = await dialog.Locator(".tm-media-library__item").CountAsync();
        Assert.IsTrue(countAfter < countBefore, "Search should reduce the number of visible items");
        Assert.IsTrue(countAfter > 0, "At least one item should match 'Mountain'");

        await TakeScreenshotAsync(page, "media_library_search");
    }
}
