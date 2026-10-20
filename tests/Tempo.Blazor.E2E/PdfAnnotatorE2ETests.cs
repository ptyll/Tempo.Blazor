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
public class PdfAnnotatorE2ETests : WasmTestBase
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
        // canvas is 300x150, so wait until the synced size reflects an actual PDF page.
        await surface.Page.WaitForFunctionAsync(
            "el => el.clientWidth > 350 && el.clientHeight > 400",
            await surface.ElementHandleAsync(),
            new PageWaitForFunctionOptions { Timeout = 60000 });
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
        await alice.Locator("[data-testid='pdf-annotator-mode-comment']").ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 120 } });
        var composer = alice.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Prosím zkontrolovat tento odstavec.");
        await SaveScreenshotAsync(page, "two-users-draft");
        await alice.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        // The thread appears in BOTH panes (shared provider).
        await alice.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await bob.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "two-users-thread-shared");

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
        await SaveScreenshotAsync(page, "two-users-reply");

        // Bob resolves — the thread disappears from the default (open) filter in both panes.
        await bob.Locator("[data-testid='pdf-annotator-resolve']").ClickAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(bob.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // The resolved filter shows it again, marked as resolved.
        await alice.Locator("[data-testid='pdf-annotator-show-resolved']").CheckAsync();
        await alice.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "two-users-resolved");
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

        await alice.Locator("[data-testid='pdf-annotator-mode-comment']").ClickAsync();
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

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        // Stamp: pick REJECTED from the picker and click the page — created immediately.
        await alice.Locator("[data-testid='pdf-annotator-mode-stamp']").ClickAsync();
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
        await alice.Locator("[data-testid='pdf-annotator-mode-draw']").ClickAsync();
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

        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);

        // Edge 1: cancelling a comment draft creates nothing.
        await alice.Locator("[data-testid='pdf-annotator-mode-comment']").ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 120, Y = 160 } });
        var cancel = alice.Locator("[data-testid='pdf-annotator-new-cancel']");
        await cancel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "edge-draft-before-cancel");
        await cancel.ClickAsync();
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-new']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

        // Edge 2: in draw mode a click without dragging (single-point stroke) must not open a draft.
        await alice.Locator("[data-testid='pdf-annotator-mode-draw']").ClickAsync();
        var point = await VisibleSurfacePointAsync(alice, 0.5, 0.4);
        await page.Mouse.MoveAsync((float)point.X, (float)point.Y);
        await page.Mouse.DownAsync();
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(500);
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-new']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 5000 });

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
        await alice.Locator("[data-testid='pdf-annotator-mode-comment']").ClickAsync();
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

    // ── Helpers ──────────────────────────────────────────────────────────────

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
        var dir = Path.Combine(FindRepoRoot().FullName,
            "tests", "Tempo.Blazor.E2E", "__screenshots__", "pdf-annotator");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{fileName}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = true });
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
