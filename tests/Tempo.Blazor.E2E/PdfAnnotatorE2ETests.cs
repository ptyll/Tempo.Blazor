using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E for TmPdfAnnotator on the PDF Annotator demo page (WASM demo at 7106, API at 5100).
/// Covers the seeded showcase (all four annotation kinds with author colors), the two-user
/// collaboration flow (comment → visible in the other pane → reply → resolve), reload
/// persistence through the localStorage provider, freehand drawing, stamps, export
/// downloads (real annotations + flattened), and edge cases (cancelled draft, click
/// without drag in draw mode). Screenshots land in <c>__screenshots__/pdf-annotator/</c>.
/// </summary>
[TestClass]
public partial class PdfAnnotatorE2ETests : WasmTestBase
{
    private const string AnnotatorPage = "/pdf-annotator";

    private sealed record AnnotatorPageHandle(IPage Page, List<string> Errors);

    private async Task<AnnotatorPageHandle> OpenAnnotatorPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 1000);

        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add(message);
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Unhandled exception"))
            {
                errors.Add(msg.Text);
            }
        };

        await page.GotoAsync($"{BaseUrl}{AnnotatorPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return new AnnotatorPageHandle(page, errors);
    }

    private static void AssertNoBlazorErrors(AnnotatorPageHandle handle)
        => Assert.AreEqual(0, handle.Errors.Count,
            "The page raised unhandled exceptions: " + string.Join(" | ", handle.Errors));

    private static async Task WaitForSurfaceReadyAsync(ILocator pane)
    {
        var surface = pane.Locator("[data-testid='pdf-annotator-surface']").First;
        await surface.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000, State = WaitForSelectorState.Attached });
        // The interaction surface is kept aligned with the rendered PDF canvas; a pre-render
        // canvas is 300x150, so wait until the synced size reflects an actual PDF page. The
        // page may be fit-width on a narrow pane (ZoomMode=Auto), so the check is the page's
        // portrait ratio, not an absolute pane width.
        await surface.Page.WaitForFunctionAsync(
            "el => el.clientHeight > 200 && el.clientWidth > 150 && el.clientHeight > el.clientWidth",
            await surface.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 60000 });
    }

    /// <summary>
    /// Picks an annotator tool: clicks the toolbar button when it is on the bar, otherwise
    /// through the pane's "More" menu — on a narrow pane the mode pills overflow into the
    /// merged toolbar's menu, where each item carries the button's data-tm-toolbar-item id
    /// as data-action-id (locale-independent).
    /// </summary>
    private static async Task ClickToolAsync(ILocator pane, string testId, string menuLabel)
    {
        var tool = pane.Locator($"[data-testid='{testId}']").First;
        if (await tool.IsVisibleAsync() && await tool.IsEnabledAsync())
        {
            await tool.ClickAsync();
            return;
        }

        var menuId = await tool.GetAttributeAsync("data-tm-toolbar-item");
        Assert.IsNotNull(menuId, $"the overflowed tool {testId} keeps its toolbar item id");

        var more = pane.Locator("button.tm-toolbar-more").First;
        await more.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        var item = pane.Page.Locator($"[data-action-id='{menuId}']").First;
        await item.ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        // The menu closes on invoke; give the mode switch a beat before the next step.
        await pane.Page.WaitForTimeoutAsync(200);
    }

    // ── Seeded showcase: all four kinds + author colors ──────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_Showcase_RendersAllKindsWithAuthorColors()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var showcase = page.Locator("[data-testid='pdf-annotator-showcase']");
        await showcase.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(showcase);

        var highlight = showcase.Locator("[data-testid='pdf-annotator-highlight']").First;
        await highlight.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        var stamp = showcase.Locator("[data-testid='pdf-annotator-stamp']").First;
        await stamp.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await stamp.InnerTextAsync(), "APPROVED");
        var ink = showcase.Locator("[data-testid='pdf-annotator-ink']").First;
        await ink.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
        var marker = showcase.Locator("[data-testid='pdf-annotator-marker']").First;
        await marker.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Author colors: Alena's highlight is amber (#b45309), Bedřich's marker violet (#7c3aed).
        StringAssert.Contains((await highlight.GetAttributeAsync("style")) ?? "", "#b45309");
        StringAssert.Contains((await marker.GetAttributeAsync("style")) ?? "", "#7c3aed");

        // Panel lists all four seeded threads.
        Assert.AreEqual(4, await showcase.Locator("[data-testid='pdf-annotator-thread']").CountAsync());

        await SaveScreenshotAsync(page, "showcase-all-kinds");
        AssertNoBlazorErrors(handle);
    }

    // ── Two users: comment → other pane → reply → resolve ───────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_TwoUsers_CommentReplyResolveFlow()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        var bob = page.Locator("[data-testid='pdf-annotator-pane-bob']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);
        await WaitForSurfaceReadyAsync(bob);

        // Alice adds a comment pin.
        await ClickToolAsync(alice, "pdf-annotator-mode-comment", "Add comment");
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 120 } });
        var composer = alice.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Prosím zkontrolovat tento odstavec.");
        // Phase 12 flake fix: pane-clipped captures — a full-page screenshot resizes the
        // viewport, which re-resolves the Auto layout to mobile and closes the open panel.
        await SavePaneScreenshotAsync(page, "two-users-draft", alice);
        await EnsurePanelVisibleAsync(alice);
        await alice.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        // The thread appears in BOTH panes (shared provider).
        await alice.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await bob.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SavePaneScreenshotAsync(page, "two-users-thread-shared", alice, bob);
        await EnsurePanelVisibleAsync(alice);
        await EnsurePanelVisibleAsync(bob);

        // Bob opens the thread and replies.
        await bob.Locator("[data-testid='pdf-annotator-thread']").First.ClickAsync();
        var replyInput = bob.Locator("[data-testid='pdf-annotator-reply-input']");
        await replyInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await replyInput.FillAsync("Opraveno, díky.");
        await bob.Locator("[data-testid='pdf-annotator-reply-submit']").ClickAsync();

        // The reply shows up in Alice's detail as well. Alice's detail is already open
        // (creating a thread selects it), so clicking the head again would toggle it closed.
        if (await alice.Locator("[data-testid='pdf-annotator-detail']").CountAsync() == 0)
        {
            await alice.Locator("[data-testid='pdf-annotator-thread']").First.ClickAsync();
        }

        await alice.Locator("[data-testid='pdf-annotator-comment']").Nth(1)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SavePaneScreenshotAsync(page, "two-users-reply", alice);
        await EnsurePanelVisibleAsync(alice);
        await EnsurePanelVisibleAsync(bob);

        // Bob resolves — the thread disappears from the default (open) filter in both panes.
        await bob.Locator("[data-testid='pdf-annotator-resolve']").ClickAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(bob.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // The All filter shows it again, marked as resolved (phase 12: the segmented
        // "All | Unresolved" filter replaced the "Show resolved" checkbox).
        await alice.Locator(".tm-pdf-thread-filter__segment:has(input[data-testid='pdf-annotator-filter-all'])").ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SavePaneScreenshotAsync(page, "two-users-resolved", alice);
        await EnsurePanelVisibleAsync(alice);
        AssertNoBlazorErrors(handle);
    }

    // ── Reload persistence (localStorage provider) ───────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_Annotations_SurviveReload()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        await ClickToolAsync(alice, "pdf-annotator-mode-comment", "Add comment");
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 200, Y = 200 } });
        var composer = alice.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Persistence check");
        await alice.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Reload the page: the same browser context keeps localStorage.
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var aliceAfter = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await aliceAfter.ScrollIntoViewIfNeededAsync();
        var thread = aliceAfter.Locator("[data-testid='pdf-annotator-thread']").First;
        await thread.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await thread.ClickAsync();
        var comment = aliceAfter.Locator("[data-testid='pdf-annotator-comment']").First;
        await comment.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await comment.InnerTextAsync(), "Persistence check");

        await SaveScreenshotAsync(page, "reload-persistence");
        AssertNoBlazorErrors(handle);
    }

    // ── Stamp + freehand drawing ─────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_StampAndDrawing_CreateAnnotations()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        // The two-user panes are at least tablet width (the demo grid keeps one column until
        // each pane can hold 768px), so the full editing tools (stamp, freehand draw) are
        // available on pane-alice; the forced-tablet card stays as extra coverage.
        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        // Stamp: pick REJECTED from the picker and click the page — created immediately.
        await ClickToolAsync(alice, "pdf-annotator-mode-stamp", "Place stamp");
        var stampSelect = alice.Locator("[data-testid='pdf-annotator-stamp-select']");
        await stampSelect.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await stampSelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 260, Y = 100 } });
        var stamp = alice.Locator("[data-testid='pdf-annotator-stamp']").First;
        await stamp.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "stamp-placed");

        // Drawing: drag a stroke, then save the draft. The stroke must start inside the
        // VISIBLE part of the canvas — the page canvas may overflow (and be clipped by) the
        // scrollable canvas wrap when the panel narrows the viewer, and raw mouse coordinates
        // from the surface's full bounding box would then land on the clipped zone.
        await ClickToolAsync(alice, "pdf-annotator-mode-draw", "Draw freehand");
        var p1 = await VisibleSurfacePointAsync(alice, 0.30, 0.45);
        var p2 = await VisibleSurfacePointAsync(alice, 0.55, 0.55);
        var p3 = await VisibleSurfacePointAsync(alice, 0.75, 0.50);
        await page.Mouse.MoveAsync((float)p1.X, (float)p1.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)p2.X, (float)p2.Y, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.MoveAsync((float)p3.X, (float)p3.Y, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        var composer = alice.Locator("[data-testid='pdf-annotator-new']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await alice.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        var ink = alice.Locator("[data-testid='pdf-annotator-ink']").First;
        await ink.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
        await SaveScreenshotAsync(page, "drawing-saved");
        AssertNoBlazorErrors(handle);
    }

    // ── Export: real annotations + flattened variant download ───────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_Export_DownloadsAnnotatedPdf()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var showcase = page.Locator("[data-testid='pdf-annotator-showcase']");
        await showcase.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(showcase);
        await showcase.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        // Export with embedded PDF annotations.
        IDownload download;
        try
        {
            download = await page.RunAndWaitForDownloadAsync(
                () => showcase.Locator("[data-testid='pdf-annotator-export']").ClickAsync(),
                new PageRunAndWaitForDownloadOptions { Timeout = 90000 });
        }
        catch (TimeoutException)
        {
            // Surface the real JS failure instead of a bare timeout.
            var diagnostic = await page.EvaluateAsync<string>(
                """
                async () => {
                    try {
                        await window.tmPdfAnnotator.exportPdf(
                            'https://mozilla.github.io/pdf.js/web/compressed.tracemonkey-pldi-09.pdf',
                            '[]',
                            '{"flatten":false,"fileName":"diag.pdf"}');
                        return 'direct exportPdf succeeded — the button path failed silently';
                    } catch (e) {
                        return 'exportPdf failed: ' + (e && (e.stack || e.message || String(e)));
                    }
                }
                """);
            throw new AssertFailedException(
                $"Download event did not arrive. Diagnostic: {diagnostic} | PageErrors: {string.Join(" | ", handle.Errors)}");
        }

        Assert.AreEqual("tempo-annotated.pdf", download.SuggestedFilename);
        var exportSize = await AssertDownloadedPdfAsync(page, download, "annotation-object export");

        // Flattened export (annotations drawn into page content + summary page).
        var flatDownload = await page.RunAndWaitForDownloadAsync(
            () => showcase.Locator("[data-testid='pdf-annotator-export-flat']").ClickAsync(),
            new PageRunAndWaitForDownloadOptions { Timeout = 90000 });
        var flatSize = await AssertDownloadedPdfAsync(page, flatDownload, "flattened export");

        // The flattened file must differ from the annotation-object file.
        Assert.AreNotEqual(exportSize, flatSize,
            "Flattened export should produce different content than the annotation-object export.");

        await SaveScreenshotAsync(page, "export-buttons");
        AssertNoBlazorErrors(handle);
    }

    private static async Task<long> AssertDownloadedPdfAsync(IPage page, IDownload download, string label)
    {
        // Read the exported bytes through the blob URL rather than the download artifact:
        // Playwright tracing instruments blob downloads and the artifact ends up "canceled",
        // while the blob content itself stays readable (the export revokes it after 60 s).
        var info = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            async (url) => {
                const response = await fetch(url);
                const buffer = await response.arrayBuffer();
                return {
                    size: buffer.byteLength,
                    header: new TextDecoder().decode(new Uint8Array(buffer.slice(0, 4)))
                };
            }
            """,
            download.Url);

        var size = info.GetProperty("size").GetInt64();
        Assert.IsTrue(size > 1000, $"The {label} is suspiciously small ({size} bytes).");
        Assert.AreEqual("%PDF", info.GetProperty("header").GetString(),
            $"The {label} should start with the %PDF header.");
        return size;
    }

    // ── Edge cases ───────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_EdgeCases_CancelDraftAndClickWithoutDrag()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        // The two-user panes are at least tablet width (the demo grid keeps one column until
        // each pane can hold 768px), so the full editing flow runs on pane-alice again; the
        // shared provider starts empty in a fresh context, so the thread counts are absolute
        // AND relative-safe (nothing is created by these edges).
        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);
        var threadsBefore = await alice.Locator("[data-testid='pdf-annotator-thread']").CountAsync();

        // Edge 1: cancelling a comment draft creates nothing.
        await ClickToolAsync(alice, "pdf-annotator-mode-comment", "Add comment");
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 120, Y = 160 } });
        var cancel = alice.Locator("[data-testid='pdf-annotator-new-cancel']");
        await cancel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-draft-before-cancel");
        await cancel.ClickAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-new']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(threadsBefore, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

        // Edge 2: in draw mode a click without dragging (single-point stroke) must not open a draft.
        await ClickToolAsync(alice, "pdf-annotator-mode-draw", "Draw freehand");
        var point = await VisibleSurfacePointAsync(alice, 0.5, 0.4);
        await page.Mouse.MoveAsync((float)point.X, (float)point.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.UpAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-new']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(threadsBefore, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

        // Edge 3: empty panel shows the localized empty state.
        var empty = alice.Locator("[data-testid='pdf-annotator-empty']");
        await empty.WaitForAsync(new LocatorWaitForOptions { Timeout = 10000 });
        await SaveScreenshotAsync(page, "edge-empty-state");
        AssertNoBlazorErrors(handle);
    }

    // ── Edit comment: saved through the provider, visible to the other user ──

    /// <summary>
    /// Phase 4 (shared thread cards): Alice edits her comment through the ⋯ menu; the provider
    /// persists the body and Bob sees the edited text (and the edited card preview) in his pane.
    /// Ends with the <c>panel-cards</c> screenshot of the redesigned card panels.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_EditComment_VisibleToOtherUser()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        var bob = page.Locator("[data-testid='pdf-annotator-pane-bob']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);
        await WaitForSurfaceReadyAsync(bob);

        // Alice adds a comment thread.
        await ClickToolAsync(alice, "pdf-annotator-mode-comment", "Add comment");
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 120 } });
        var composer = alice.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Původní text komentáře");
        await alice.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        // The thread appears in BOTH panes; creating selects it in Alice's panel.
        await bob.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Alice edits her comment via its ⋯ action menu.
        await alice.Locator(".tm-pdf-annotator-panel .tm-pdf-comment-item__menu .tm-dropdown-trigger")
            .First.ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-edit']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        var editInput = alice.Locator("[data-testid='pdf-annotator-edit-input']").First;
        await editInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await editInput.FillAsync("Upravený text viditelný pro druhého uživatele");
        await alice.Locator("[data-testid='pdf-annotator-edit-save']").First.ClickAsync();

        // Bob opens the thread and sees the edited body (provider round-trip).
        await bob.Locator("[data-testid='pdf-annotator-thread']").First.ClickAsync();
        var bobComment = bob.Locator("[data-testid='pdf-annotator-comment']").First;
        await bobComment.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await Assertions.Expect(bobComment).ToContainTextAsync(
            "Upravený text viditelný pro druhého uživatele",
            new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        // Alice's collapsed card header shows the new preview as well.
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']").First)
            .ToContainTextAsync("Upravený text viditelný",
                new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        await SaveScreenshotAsync(page, "panel-cards");
        AssertNoBlazorErrors(handle);
    }

    // ── Phase 12: the "All | Unresolved" filter and the runtime color picker ──

    /// <summary>
    /// Phase 12: the panels' header carries the segmented "All (N) | Unresolved (M)" filter
    /// and its counts follow resolve/reopen mutations live. The docked desktop panel starts
    /// with the Unresolved view; resolving one of the four seeded threads updates the counts
    /// and hides the thread. Ends with the <c>thread-filter-desktop</c> screenshot.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_ThreadFilter_CountsUpdateOnResolve()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var showcase = page.Locator("[data-testid='pdf-annotator-showcase']");
        await showcase.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(showcase);

        var filter = showcase.Locator("[data-testid='pdf-annotator-filter']").First;
        await filter.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        Assert.AreEqual(0, await showcase.Locator("[data-testid='pdf-annotator-show-resolved']").CountAsync(),
            "the checkbox was replaced by the segmented filter");

        // Four seeded threads, all open: both segments count 4.
        var text = await filter.InnerTextAsync();
        StringAssert.Contains(text, "All (4)");
        StringAssert.Contains(text, "Unresolved (4)");
        await SaveScreenshotAsync(page, "thread-filter-desktop");

        // Resolve one thread through its expanded card.
        await showcase.Locator("[data-testid='pdf-annotator-thread']").First.ClickAsync();
        await showcase.Locator("[data-testid='pdf-annotator-resolve']").First.ClickAsync();

        // The counts follow the mutation and the resolved thread leaves the Unresolved view.
        await Assertions.Expect(showcase.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(3, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        var updated = await filter.InnerTextAsync();
        StringAssert.Contains(updated, "All (4)");
        StringAssert.Contains(updated, "Unresolved (3)");

        // The All segment brings the resolved thread back.
        await showcase.Locator(".tm-pdf-thread-filter__segment:has(input[data-testid='pdf-annotator-filter-all'])").ClickAsync();
        await Assertions.Expect(showcase.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    /// <summary>Phase 12 edge: an empty document under the Unresolved filter shows the
    /// localized "no unresolved" empty state (never the plain "no annotations" text).</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_ThreadFilter_EmptyUnresolved_Edge()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        var empty = alice.Locator("[data-testid='pdf-annotator-empty']");
        await empty.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains(await empty.InnerTextAsync(), "No unresolved",
            "the Unresolved filter is the default, so an empty panel shows its empty state");

        // The All segment keeps the panel's own "no annotations" text.
        await alice.Locator(".tm-pdf-thread-filter__segment:has(input[data-testid='pdf-annotator-filter-all'])").ClickAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-empty']"))
            .ToContainTextAsync("No annotations", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        // …and back to the unresolved filter's own localized empty state.
        await alice.Locator(".tm-pdf-thread-filter__segment:has(input[data-testid='pdf-annotator-filter-unresolved'])").ClickAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-empty']"))
            .ToContainTextAsync("No unresolved", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });

        await SaveScreenshotAsync(page, "thread-filter-empty");
        AssertNoBlazorErrors(handle);
    }

    /// <summary>
    /// Phase 12: the toolbar color trigger opens the palette; the picked color applies to NEW
    /// annotations — here a text highlight created after the pick. The
    /// <c>color-picker-open</c> screenshot is a clipped toolbar+popover shot (reduced motion,
    /// popover asserted below the chip); <c>highlight-custom-color</c> ends the flow.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_ColorPicker_NewHighlightUsesPickedColor()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        // The popover animation must not race the screenshot below: emulate reduced motion and
        // wait for the visible panel explicitly.
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });

        var showcase = page.Locator("[data-testid='pdf-annotator-showcase']");
        await showcase.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(showcase);

        var trigger = showcase.Locator("[data-testid='pdf-annotator-color-trigger']").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Pick the red swatch of the demo's custom palette first (the palette closes on a pick).
        await trigger.ClickAsync();
        var palette = showcase.Locator(".tm-color-palette").First;
        await palette.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await showcase.Locator(".tm-color-palette-swatch[style*='#b91c1c']").First.ClickAsync();
        await Assertions.Expect(palette).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains((await showcase.Locator(".tm-pdf-annotator__color-chip").First
            .GetAttributeAsync("style")) ?? "", "#b91c1c", "the trigger chip shows the picked color");

        // Reopen with the picked color checked: the screenshot shows the toolbar plus the
        // popover anchored BELOW the chip (never covering the toolbar).
        await trigger.ClickAsync();
        var panel = showcase.Locator(".tm-pdf-annotator__color-panel").First;
        await panel.WaitForAsync(new LocatorWaitForOptions
        {
            Timeout = 15000,
            State = WaitForSelectorState.Visible
        });
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-expanded"));
        await page.WaitForTimeoutAsync(300);

        var chipBox = await showcase.Locator(".tm-pdf-annotator__color-chip").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The color chip has no bounding box.");
        var panelBox = await panel.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The color panel has no bounding box.");
        Assert.IsTrue(panelBox.Y >= chipBox.Y + chipBox.Height,
            $"The popover top ({panelBox.Y}) must be below the chip bottom ({chipBox.Y + chipBox.Height}).");

        var toolbarBox = await showcase.Locator("[data-testid='pdf-viewer-toolbar']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The toolbar has no bounding box.");
        var left = Math.Min(toolbarBox.X, panelBox.X) - 8;
        var top = Math.Min(toolbarBox.Y, panelBox.Y) - 8;
        var right = Math.Max(toolbarBox.X + toolbarBox.Width, panelBox.X + panelBox.Width) + 8;
        var bottom = Math.Max(toolbarBox.Y + toolbarBox.Height, panelBox.Y + panelBox.Height) + 8;
        await SaveScreenshotAsync(page, "color-picker-open", new Clip
        {
            X = left,
            Y = top,
            Width = right - left,
            Height = bottom - top
        });

        // Close the popover and create the highlight with the picked color.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(palette).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // Highlight mode + a real text-layer selection → the draft carries the picked color.
        await ClickToolAsync(showcase, "pdf-annotator-mode-highlight", "Highlight text");
        Assert.IsTrue(await SelectTextAsync(showcase), "the annotator's text layer is selectable");
        var composer = showcase.Locator("[data-testid='pdf-annotator-new-input']").First;
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Picked-color highlight");
        await showcase.Locator("[data-testid='pdf-annotator-new-submit']").First.ClickAsync();

        // The NEW highlight is the fifth thread of the seeded document (its layer rect is the
        // last one rendered) — the seeded amber highlight keeps its own color.
        var highlightCount = await showcase.Locator("[data-testid='pdf-annotator-highlight']").CountAsync();
        Assert.IsTrue(highlightCount >= 2, "the new highlight joined the seeded one");
        var highlight = showcase.Locator("[data-testid='pdf-annotator-highlight']").Last;
        await highlight.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        StringAssert.Contains((await highlight.GetAttributeAsync("style")) ?? "", "#b91c1c",
            "a NEW highlight carries the picked color (existing threads are never recolored)");

        await SaveScreenshotAsync(page, "highlight-custom-color");
        AssertNoBlazorErrors(handle);
    }

    /// <summary>Phase 12: "Reset to author color" returns new annotations to the
    /// per-author/role resolution — the picked color stops applying.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_ColorPicker_ResetToAuthorColor()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        // Pick green (the 5th swatch of the default palette).
        await alice.Locator("[data-testid='pdf-annotator-color-trigger']").First.ClickAsync();
        var palette = alice.Locator(".tm-color-palette").First;
        await palette.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await alice.Locator(".tm-color-palette-swatch[style*='#15803d']").First.ClickAsync();
        await Assertions.Expect(palette).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains((await alice.Locator(".tm-pdf-annotator__color-chip").First
            .GetAttributeAsync("style")) ?? "", "#15803d");

        // A stamp created now carries the picked color.
        await ClickToolAsync(alice, "pdf-annotator-mode-stamp", "Place stamp");
        var point = await VisibleSurfacePointAsync(alice, 0.5, 0.35);
        await page.Mouse.ClickAsync((float)point.X, (float)point.Y);
        var stamp = alice.Locator("[data-testid='pdf-annotator-stamp']").First;
        await stamp.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
        StringAssert.Contains((await stamp.GetAttributeAsync("style")) ?? "", "#15803d",
            "the stamp created after the pick carries the picked color");

        // Reopen the palette and reset: new annotations fall back to the role color again.
        await alice.Locator("[data-testid='pdf-annotator-color-trigger']").First.ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-color-reset']").First.ClickAsync();
        await Assertions.Expect(palette).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains((await alice.Locator(".tm-pdf-annotator__color-chip").First
            .GetAttributeAsync("style")) ?? "", "#b45309", "the reset returns to the per-role color");

        await ClickToolAsync(alice, "pdf-annotator-mode-stamp", "Place stamp");
        var point2 = await VisibleSurfacePointAsync(alice, 0.7, 0.35);
        await page.Mouse.ClickAsync((float)point2.X, (float)point2.Y);
        var stamp2 = alice.Locator("[data-testid='pdf-annotator-stamp']").Last;
        await stamp2.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
        StringAssert.Contains((await stamp2.GetAttributeAsync("style")) ?? "", "#b45309",
            "the stamp created after the reset resolves per role again");
        AssertNoBlazorErrors(handle);
    }

    /// <summary>Phase 12: the palette is keyboard-operable without any pointer — Enter opens
    /// it from the trigger, Tab reaches the swatches, the arrows move the roving focus, Enter
    /// picks and closes, and Escape closes without picking.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_ColorPicker_KeyboardOnly()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        var trigger = alice.Locator("[data-testid='pdf-annotator-color-trigger']").First;
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        var palette = alice.Locator(".tm-color-palette").First;
        await palette.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.AreEqual("true", await trigger.GetAttributeAsync("aria-expanded"));

        // Escape closes without picking.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(palette).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        Assert.AreEqual("false", await trigger.GetAttributeAsync("aria-expanded"));

        // Reopen, focus the first swatch, arrow twice (the palette's roving tabindex moves the
        // real focus), Enter picks (and closes).
        await trigger.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await palette.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await palette.Locator(".tm-color-palette-swatch").First.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(palette).ToHaveCountAsync(0,
            new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains((await alice.Locator(".tm-pdf-annotator__color-chip").First
            .GetAttributeAsync("style")) ?? "", "#0e7490",
            "the keyboard walk picked the third default-palette swatch (cyan 700)");
        AssertNoBlazorErrors(handle);
    }

    // ── Two-user panes at tablet width (phase 8 review 6) ─────────────────────

    /// <summary>
    /// The two-user grid keeps ONE column until each pane can hold the 768px tablet breakpoint
    /// (a <c>min-[1700px]</c> two-column split), so at 1600×1000 both panes render a tablet+
    /// presentation: the full tool set (highlight/stamp/draw) is offered, the G2 palette is
    /// absent, the pane has no horizontal overflow and the Print command stays inside the pane
    /// bounds. The mobile behaviors are covered by PdfAnnotatorResponsiveE2ETests.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task Annotator_TwoUserPanes_TableViewportWidth_FullEditingIntact()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        // A full-width pane is at or above the tablet breakpoint: not the G2 presentation.
        var layout = await alice.Locator(".tm-pdf-annotator").First.GetAttributeAsync("data-editor-layout");
        Assert.AreNotEqual("mobile", layout,
            $"a full-width two-user pane must resolve tablet+ at 1600x1000, got '{layout}'");

        // The full tool set is offered again (the stamp/draw flows run on this pane).
        Assert.IsNotNull(await alice.Locator("[data-testid='pdf-annotator-mode-highlight']").First.ElementHandleAsync(),
            "the editing tools are offered on the tablet+ presentation");
        Assert.AreEqual(0, await alice.Locator("[data-testid='pdf-annotator-palette']").CountAsync(),
            "the G2 tool palette belongs to the mobile presentation only");

        var overflow = await page.EvaluateAsync<bool>(
            "el => el.scrollWidth > el.clientWidth + 1",
            await alice.Locator(".tm-pdf-annotator").First.ElementHandleAsync()
                ?? throw new InvalidOperationException("The annotator root was not found."));
        Assert.IsFalse(overflow, "the wide pane must not introduce horizontal overflow");

        // Print (bar tile on mobile, secondary button on desktop/tablet) stays inside the pane.
        var print = alice.Locator("[data-testid='pdf-annotator-print']").First;
        await Assertions.Expect(print).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        var printBox = await print.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The Print button has no bounding box.");
        var paneBox = await alice.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The pane has no bounding box.");
        Assert.IsTrue(printBox.X >= paneBox.X - 1
            && printBox.X + printBox.Width <= paneBox.X + paneBox.Width + 1
            && printBox.Y >= paneBox.Y - 1
            && printBox.Y + printBox.Height <= paneBox.Y + paneBox.Height + 1,
            $"The Print button ({printBox.Width}x{printBox.Height} at {printBox.X},{printBox.Y}) " +
            $"escaped the pane bounds ({paneBox.Width}x{paneBox.Height} at {paneBox.X},{paneBox.Y}).");

        // The viewer's own toolbar (paging and zoom) is intact inside the pane.
        var pageInput = alice.Locator(".tm-pdf-viewer__page-input").First;
        await Assertions.Expect(pageInput).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        await SaveScreenshotAsync(page, "panes-tablet-width");
        AssertNoBlazorErrors(handle);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects a run of text-layer spans through the DOM range API — the same path the touch
    /// selection uses, so the annotator's <c>OnTextSelected</c> draft flow (and thus the picked
    /// color of a highlight) is exercised without synthetic mouse gestures.
    /// </summary>
    private static async Task<bool> SelectTextAsync(ILocator pane)
        => await pane.EvaluateAsync<bool>(@"(pane) => {
            const spans = [...pane.querySelectorAll('.tm-pdf-viewer__text-layer span')];
            if (spans.length < 2) return false;
            const sel = window.getSelection();
            sel.removeAllRanges();
            const range = document.createRange();
            range.setStartBefore(spans[0]);
            range.setEndAfter(spans[Math.min(spans.length - 1, 4)]);
            sel.addRange(range);
            return (sel.toString() || '').length > 0;
        }");

    /// <summary>
    /// A point inside the visible intersection of the annotator surface and the scrollable
    /// canvas wrap, at relative coordinates (<paramref name="fx"/>, <paramref name="fy"/>).
    /// The page canvas may overflow the wrap (and be clipped), so raw coordinates from the
    /// surface's full bounding box can land on a zone the pointer can never reach.
    /// </summary>
    private static async Task<(double X, double Y)> VisibleSurfacePointAsync(
        ILocator pane, double fx, double fy)
    {
        var surface = await pane.Locator("[data-testid='pdf-annotator-surface']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The annotator surface has no bounding box.");
        var wrap = await pane.Locator(".tm-pdf-viewer__canvas-wrap").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The canvas wrap has no bounding box.");

        var left = Math.Max(surface.X, wrap.X);
        var right = Math.Min(surface.X + surface.Width, wrap.X + wrap.Width);
        var top = Math.Max(surface.Y, wrap.Y);
        var bottom = Math.Min(surface.Y + surface.Height, wrap.Y + wrap.Height);
        Assert.IsTrue(right > left && bottom > top,
            "The annotator surface has no visible intersection with the canvas wrap.");

        return (left + (right - left) * fx, top + (bottom - top) * fy);
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        await SaveScreenshotAsync(page, fileName, clip: null);
    }

    /// <summary>Saves an annotator screenshot, optionally clipped (a clip replaces the
    /// full-page capture — its resize is what re-resolves the Auto layout mid-test).</summary>
    private static async Task SaveScreenshotAsync(IPage page, string fileName, Clip? clip)
    {
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "pdf-annotator");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = clip is null, Clip = clip });
    }

    /// <summary>Saves a screenshot clipped to the union of the given pane boxes (plus a small
    /// margin) — a full-page capture resizes the viewport, which can re-resolve the Auto
    /// layout (the phase 12 two-users flake), so the pane evidence is captured by clip.</summary>
    private static async Task SavePaneScreenshotAsync(IPage page, string fileName, params ILocator[] panes)
    {
        const float margin = 8;
        float? left = null, top = null, right = null, bottom = null;
        foreach (var pane in panes)
        {
            var box = await pane.BoundingBoxAsync()
                ?? throw new InvalidOperationException("A captured annotator pane has no bounding box.");
            left = Math.Min(left ?? box.X, box.X);
            top = Math.Min(top ?? box.Y, box.Y);
            right = Math.Max(right ?? box.X + box.Width, box.X + box.Width);
            bottom = Math.Max(bottom ?? box.Y + box.Height, box.Y + box.Height);
        }

        var clip = new Clip
        {
            X = left!.Value - margin,
            Y = top!.Value - margin,
            Width = right!.Value - left.Value + margin * 2,
            Height = bottom!.Value - top.Value + margin * 2
        };
        await SaveScreenshotAsync(page, fileName, clip);
    }

    /// <summary>Phase 12 two-users flake guard: after a capture, the pane's thread panel must
    /// be visible again — a transient Auto re-resolve to mobile closes it, and the panel
    /// toggle reopens it.</summary>
    private static async Task EnsurePanelVisibleAsync(ILocator pane)
    {
        var panel = pane.Locator("[data-testid='pdf-annotator-panel']").First;
        if (await panel.IsVisibleAsync())
        {
            return;
        }

        await pane.Locator("[data-testid='pdf-annotator-panel-toggle']").First.ClickAsync();
        await panel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
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

        throw new InvalidOperationException("Repository root was not found.");
    }
}
