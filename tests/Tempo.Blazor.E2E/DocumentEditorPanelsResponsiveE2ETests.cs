using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 8 (DE-2.7 + the phone parts of DE-1.1 / DE-1.2). A phone (390 and 320, touch) shows viewing plus light edits
/// (decision G2): every panel is a sheet that opens and closes, nothing scrolls sideways, the canvas selection survives a sheet, and the read ->
/// find -> comment -> accept -> format -> save flow works from the action bar and the sheets. A tablet (1180 and 1024, touch) has ONE side sheet that
/// overlays the document, and every touch target is at least 44 px. The screenshots are
/// <see cref="DocumentEditorRedesignPhase8CaptureE2ETests"/> (a gated generator); this class writes no files.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:ResponsiveShell")]
[DoNotParallelize]
public sealed class DocumentEditorPanelsResponsiveE2ETests : WasmTestBase
{
    private const double TouchTarget = 44;

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    private static readonly (string Name, int Width, int Height)[] PhoneViewports = [("390", 390, 844), ("320", 320, 640)];

    [TestMethod]
    [Description("A phone opens and closes every panel as a sheet - comments, revisions, versions, outline, pages, properties and find - with no horizontal scroll")]
    public async Task Phone_EveryPanel_OpensAndClosesAsASheet_WithoutHorizontalScroll()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true);
            Assert.AreEqual("mobile", await LayoutAsync(page), $"{name}: a phone container");
            Assert.AreEqual(0, await page.GetByTestId("document-side-panel").CountAsync(), $"{name}: nothing hides the page until the reader asks");
            await AssertNoHorizontalScrollAsync(page, $"{name} idle");

            foreach (var tab in new[] { "comments", "revisions", "versions", "outline", "pages", "properties" })
            {
                await OpenInspectorTabAsync(page, tab);
                await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await Assertions.Expect(page.GetByTestId("document-side-panel")).ToHaveAttributeAsync("data-active-tab", tab);
                var box = await page.GetByTestId("document-inspector-sheet").BoundingBoxAsync()
                    ?? await page.Locator(".tm-overlay-panel-sheet .tm-drawer__panel").First.BoundingBoxAsync();
                Assert.IsNotNull(box, $"{name}/{tab}: the sheet is measurable");
                Assert.IsTrue(box.X >= -0.5 && box.X + box.Width <= width + 0.5, $"{name}/{tab}: the sheet fits the viewport width ({box.X}, {box.Width})");
                await AssertNoHorizontalScrollAsync(page, $"{name}/{tab}");
                await CloseSheetAsync(page);
                Assert.AreEqual(0, await page.GetByTestId("document-side-panel").CountAsync(), $"{name}/{tab}: the sheet closed");
            }

            await page.GetByTestId("document-header-search").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await AssertNoHorizontalScrollAsync(page, $"{name}/find");
            await CloseSheetAsync(page);
            Assert.AreEqual(0, await page.GetByTestId("document-find-panel").CountAsync(), $"{name}/find: the sheet closed");

            await AssertNoHorizontalScrollAsync(page, $"{name} after the sheets");
            CollectionAssert.AreEqual(Array.Empty<string>(), errors, $"{name}: no console or page error");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Only one sheet at a time on a phone: opening find closes the inspector sheet and opening the inspector closes find")]
    public async Task Phone_OneSheetAtATime()
    {
        var (page, errors) = await OpenAsync(RevisionsDocumentId, 390, 844, touch: true);

        await OpenInspectorTabAsync(page, "revisions");
        await CloseSheetAsync(page);
        await page.GetByTestId("document-header-search").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await CloseSheetAsync(page);

        await page.GetByTestId("document-mobile-comments").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-find-panel").CountAsync(), "the inspector sheet and find never stack");
        Assert.AreEqual(1, await page.Locator(".tm-overlay-panel-sheet").CountAsync(), "exactly one sheet is in the DOM");
        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The selection in the canvas survives opening and closing a sheet")]
    public async Task Phone_CanvasSelection_SurvivesAClosedSheet()
    {
        var (page, errors) = await OpenAsync(RevisionsDocumentId, 390, 844, touch: true);
        var text = page.Locator("[data-canvas-text-rect]").First;
        await text.ScrollIntoViewIfNeededAsync();
        var box = await text.BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync((float)(box.X + Math.Min(box.Width / 2, 40)), (float)(box.Y + box.Height / 2));
        var before = await ReadSelectionAsync(page);
        Assert.IsFalse(string.IsNullOrEmpty(before.Block), "the caret is in a block");

        await OpenInspectorTabAsync(page, "comments");
        await CloseSheetAsync(page);
        var after = await ReadSelectionAsync(page);

        Assert.AreEqual(before.Block, after.Block, "the same block keeps the caret");
        Assert.AreEqual(before.Offset, after.Offset, "the same offset keeps the caret");
        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The phone flow of decision G2: read, find, comment, accept a revision, format B/I/U, save - from the action bar and the sheets")]
    public async Task Phone_G2Flow_ReadFindCommentAcceptFormatSave()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            // The canvas-engine host page is the one whose demo provider accepts replies and revision decisions.
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true, route: "/canvas-engine-host");

            // find
            await page.GetByTestId("document-header-search").ClickAsync();
            await page.GetByTestId("document-find-input").FillAsync("clause");
            await Assertions.Expect(page.GetByTestId("document-find-result").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await CloseSheetAsync(page);

            // comment: the Comments tile opens the comments sheet; a reply is written and submitted
            await page.GetByTestId("document-mobile-comments").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-side-panel-tab-comments")).ToHaveAttributeAsync("aria-selected", "true", new() { Timeout = 10_000 });
            var reply = $"phone reply {name}";
            var entriesBefore = await page.GetByTestId("document-comment-entry").CountAsync();
            await page.GetByTestId("document-comment-reply-input").First.FillAsync(reply);
            await page.GetByTestId("document-comment-reply-submit").First.ClickAsync();
            await WaitForPredicateAsync(page, "count => document.querySelectorAll('[data-testid=\"document-comment-entry\"]').length > count", entriesBefore, 10_000);

            // accept a revision
            await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
            var badge = page.GetByTestId("document-side-panel-tab-revisions").Locator(".tm-document-side-panel__badge");
            var pending = int.Parse(await badge.TextContentAsync() ?? "0");
            Assert.IsTrue(pending >= 1, $"{name}: the document has pending revisions");
            await page.GetByTestId("document-revision-accept").First.ClickAsync();
            await Assertions.Expect(badge).ToHaveTextAsync((pending - 1).ToString(), new() { Timeout = 10_000 });
            await CloseSheetAsync(page);

            // B / I / U on a selection: the phone ribbon's primary row
            var text = page.Locator("[data-canvas-text-rect]").First;
            await text.ScrollIntoViewIfNeededAsync();
            var box = await text.BoundingBoxAsync();
            Assert.IsNotNull(box);
            await page.Mouse.DblClickAsync((float)(box.X + Math.Min(box.Width / 2, 30)), (float)(box.Y + box.Height / 2));
            foreach (var command in new[] { "document-bold", "document-italic", "document-underline" })
            {
                var button = page.Locator($"button[data-testid='{command}']:visible");
                await Assertions.Expect(button.First).ToBeVisibleAsync(new() { Timeout = 10_000 });
                await button.First.ClickAsync();
            }

            // save: the primary tile of the action bar, once
            Assert.AreEqual(1, await page.GetByTestId("document-save").CountAsync(), $"{name}: exactly one Save");
            await page.GetByTestId("document-save").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-header-status")).ToHaveAttributeAsync("data-state", "saved", new() { Timeout = 15_000 });

            await AssertNoHorizontalScrollAsync(page, $"{name} G2 flow");
            CollectionAssert.AreEqual(Array.Empty<string>(), errors, $"{name}: no console or page error");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The action bar: Comments with the review count, Print preview, the primary Save, More with the rest; it hides on a tablet")]
    public async Task Phone_ActionBar_HasCommentsPrintSaveMore_AndTheBadge()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true);
            var tiles = await page.Locator("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__action").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-action-id'))");
            CollectionAssert.AreEqual(new[] { "comments", "print-preview", "save" }, tiles, $"{name}: the three tiles");
            await Assertions.Expect(page.Locator("[data-action-id='comments'] .tm-mobile-action-bar__badge")).ToBeVisibleAsync();
            Assert.AreEqual(0, await page.GetByTestId("document-review-summary").CountAsync(), $"{name}: the summary banner is the badge now");

            // The bar sits at the bottom of the editor's container and never covers what is above it (the last tile is reachable).
            foreach (var tile in await page.Locator("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__action, [data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__more").AllAsync())
            {
                await AssertTouchTargetAsync(tile, $"{name}: action bar tile");
            }

            await page.Locator("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__more").ClickAsync();
            foreach (var id in new[] { "document-mobile-find", "document-mobile-revisions", "document-mobile-outline", "document-mobile-pages", "document-mobile-properties" })
            {
                await Assertions.Expect(page.GetByTestId(id)).ToBeVisibleAsync(new() { Timeout = 10_000 });
            }

            await page.GetByTestId("document-mobile-outline").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-side-panel")).ToHaveAttributeAsync("data-active-tab", "outline", new() { Timeout = 10_000 });
            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The phone header is one row - title, state, search, undo, redo - and the ribbon is the tabs row plus ONE tools row with Home / Insert / Review / View only")]
    public async Task Phone_Header_And_Ribbon_AreCompact()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true);

            var header = await page.GetByTestId("document-header").BoundingBoxAsync();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.Height <= 70, $"{name}: the header is one row ({header.Height}px)");
            await AssertTouchTargetAsync(page.GetByTestId("document-header-search"), $"{name}: document-header-search");
            if (width < 360)
            {
                // A 320 px row keeps the title: Undo and Redo are in the kebab panel (phase 9, TmDocumentEditorHeader.Dense).
                Assert.AreEqual(0, await page.GetByTestId("document-header-actions").GetByTestId("document-undo").CountAsync(), $"{name}: no undo on the row");
                await page.GetByTestId("document-header-kebab").ClickAsync();
                foreach (var id in new[] { "document-undo", "document-redo" })
                {
                    await AssertTouchTargetAsync(page.GetByTestId("document-header-kebab-panel").GetByTestId(id), $"{name}: {id} in the kebab");
                }

                await page.Keyboard.PressAsync("Escape");
            }
            else
            {
                foreach (var id in new[] { "document-undo", "document-redo" })
                {
                    await AssertTouchTargetAsync(page.GetByTestId(id), $"{name}: {id}");
                }
            }

            var tabs = await page.Locator("[data-testid^='document-ribbon-tab-']").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-testid').replace('document-ribbon-tab-', ''))");
            CollectionAssert.AreEqual(new[] { "home", "insert", "review", "view" }, tabs, $"{name}: G2 hides Math, Layout and References on a phone");
            foreach (var tab in await page.Locator("[data-testid^='document-ribbon-tab-']").AllAsync())
            {
                var box = await tab.BoundingBoxAsync();
                Assert.IsNotNull(box);
                Assert.IsTrue(box.Height >= TouchTarget - 0.5, $"{name}: a ribbon tab is a touch target ({box.Height}px)");
            }

            // One tools row: every command button of the bar shares a row (their tops are within a few pixels) and B / I / U are on it.
            var tops = await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-btn:visible").EvaluateAllAsync<double[]>("els => els.map(e => Math.round(e.getBoundingClientRect().top))");
            Assert.IsTrue(tops.Length >= 3 && tops.Max() - tops.Min() <= 6, $"{name}: one tools row (tops {string.Join(",", tops)})");
            foreach (var command in new[] { "document-bold", "document-italic", "document-underline" })
            {
                Assert.IsTrue(await page.Locator($"button[data-testid='{command}']:visible").CountAsync() > 0, $"{name}: {command} stays on the primary row");
            }

            // The More menu is a sheet on a phone.
            await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-more").ClickAsync();
            await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet [role='menu']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.Keyboard.PressAsync("Escape");

            await AssertNoHorizontalScrollAsync(page, name);
            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The page fills the width of a phone's column (at least 90 percent of it) and sits inside it - no bezel, no sideways scroll")]
    public async Task Phone_PageIsFittedToTheColumnWidth()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true);
            await page.WaitForTimeoutAsync(1500);

            var fit = await page.EvaluateAsync<double[]>("""
                () => {
                    // the scroll viewport's CLIENT box: a desktop-grade scrollbar (the test browser's) is not part of the column the page is fitted to
                    const el = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const host = el.getBoundingClientRect();
                    const p = document.querySelector('[data-testid="document-canvas-page"]').getBoundingClientRect();
                    return [p.left, p.right, host.left, host.left + el.clientWidth, p.width, el.clientWidth, el.scrollWidth];
                }
                """);
            Assert.IsTrue(fit[1] <= fit[3] + 1 && fit[0] >= fit[2] - 1, $"{name}: the page [{fit[0]}, {fit[1]}] sits inside the column [{fit[2]}, {fit[3]}]");
            Assert.IsTrue(fit[4] >= 0.9 * fit[5], $"{name}: the page ({fit[4]} px) fills at least 90 percent of the column ({fit[5]} px)");
            Assert.IsTrue(fit[6] <= fit[5] + 1, $"{name}: no sideways scroll in the column ({fit[6]} > {fit[5]})");
            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A tablet container narrower than the page fits the page too: the page right edge stays inside the document region")]
    public async Task Tablet_NarrowerThanThePage_FitsThePage()
    {
        foreach (var (name, width, height, route) in new[] { ("1024", 1024, 768, "/document-editor"), ("1180", 1180, 820, "/canvas-engine-host") })
        {
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true, route: route);
            Assert.AreEqual("tablet", await LayoutAsync(page));
            await page.WaitForTimeoutAsync(1500);

            var fit = await page.EvaluateAsync<double[]>("""
                () => {
                    const el = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const host = el.getBoundingClientRect();
                    const p = document.querySelector('[data-testid="document-canvas-page"]').getBoundingClientRect();
                    return [p.left, p.right, host.left, host.left + el.clientWidth];
                }
                """);
            Assert.IsTrue(fit[1] <= fit[3] + 1, $"{name}: the page right edge ({fit[1]}) stays inside the document column ({fit[3]})");
            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The action-bar labels are whole words in every language: no label is clipped or breaks inside a word at 390 and 320")]
    public async Task Phone_ActionBarLabels_AreNeverClipped_InFourLanguages()
    {
        foreach (var culture in new[] { "en", "cs", "sk", "fr" })
        {
            foreach (var (name, width, height) in PhoneViewports)
            {
                var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true, culture: culture);
                var labels = await page.Locator("[data-testid='document-mobile-action-bar'] .tm-mobile-action-bar__label-text")
                    .EvaluateAllAsync<double[][]>("els => els.map(e => [e.scrollWidth, e.clientWidth, e.scrollHeight, e.clientHeight])");
                Assert.IsTrue(labels.Length >= 4, $"{culture}/{name}: the tiles and More have labels");
                foreach (var label in labels)
                {
                    Assert.IsTrue(label[0] <= label[1] + 1, $"{culture}/{name}: a label is wider than its tile ({label[0]} > {label[1]})");
                    Assert.IsTrue(label[2] <= label[3] + 1, $"{culture}/{name}: a label is cut after its lines ({label[2]} > {label[3]})");
                }

                CollectionAssert.AreEqual(Array.Empty<string>(), errors, $"{culture}/{name}");
                await page.Context.CloseAsync();
            }
        }
    }

    [TestMethod]
    [Description("A phone's status bar is hidden or fully on screen - never a clipped strip of it; the counts and zoom are tablet-and-up")]
    public async Task Phone_StatusBar_IsHiddenOrFullyVisible()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            var (page, errors) = await OpenAsync(RevisionsDocumentId, width, height, touch: true);
            Assert.AreEqual(0, await page.GetByTestId("document-status-zoom-controls").Locator("visible=true").CountAsync(), $"{name}: no zoom controls on a phone");
            Assert.AreEqual(0, await page.GetByTestId("document-status-page-count").Locator("visible=true").CountAsync(), $"{name}: no page count on a phone");
            var bar = page.GetByTestId("document-status-bar");
            if (await bar.IsVisibleAsync())
            {
                var barBox = await bar.BoundingBoxAsync();
                var actionBar = await page.GetByTestId("document-mobile-action-bar").BoundingBoxAsync();
                Assert.IsNotNull(barBox);
                Assert.IsNotNull(actionBar);
                Assert.IsTrue(barBox.Y + barBox.Height <= actionBar.Y + 1, $"{name}: a visible status bar sits fully above the action bar");
            }

            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The phone's Home row keeps alignment, the bulleted list and the image before More; the sheet of the inspector has no title of the panel and no gap above its tabs")]
    public async Task Phone_ToolsRow_And_InspectorSheet_AreTight()
    {
        var (page, errors) = await OpenAsync(RevisionsDocumentId, 390, 844, touch: true);
        foreach (var id in new[] { "document-align", "document-bullet-list" })
        {
            Assert.IsTrue(await page.Locator($"[data-testid='{id}']:visible").CountAsync() > 0, $"{id} stays on the phone's primary row");
        }

        await page.GetByTestId("document-mobile-comments").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel-tab-comments")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.WaitForTimeoutAsync(800); // the sheet slides in: two boxes read mid-slide would disagree
        var tabs = await page.GetByTestId("document-side-panel-tab-comments").BoundingBoxAsync();
        var sheet = await page.GetByTestId("document-inspector-sheet").BoundingBoxAsync();
        Assert.IsNotNull(tabs);
        Assert.IsNotNull(sheet);
        Assert.IsTrue(tabs.Y - sheet.Y <= 6, $"no gap above the tabs: they open the sheet's content ({tabs.Y - sheet.Y} px)");
        Assert.AreEqual(0, await page.Locator(".tm-document-comment-rail__header h3:visible").CountAsync(), "the sheet title names the panel; the panel's own heading is gone");
        foreach (var id in new[] { "document-comment-filter", "document-comment-sort" })
        {
            var field = await page.GetByTestId(id).BoundingBoxAsync();
            Assert.IsNotNull(field);
            Assert.IsTrue(field.Height >= 47.5, $"{id} is 3rem tall on a phone ({field.Height}px)");
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The diff of a phone: Unified, a three-segment switch (Older / Newer / Differences), Older hides what was added")]
    public async Task Phone_Diff_HasTheOlderNewerDifferencesSwitch()
    {
        foreach (var (name, width, height) in PhoneViewports)
        {
            await DocumentEditorE2EReset.ResetAsync();
            // The two versions are made with a keyboard and a mouse on a desktop-sized page (typing into the virtualized canvas of a phone is
            // not what this spec is about); the viewport then shrinks to the phone and the sheet's Versions tab drives the comparison.
            var (page, errors) = await OpenAsync("contract-demo", 1440, 900, touch: false);
            await DocumentEditorRedesignPhase7E2ETests.CreateTwoVersionsAsync(page);
            await page.SetViewportSizeAsync(width, height);
            await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.dataset.layout === 'mobile'", 15_000);
            await page.WaitForTimeoutAsync(500);
            await DocumentEditorRedesignE2EHelpers.OpenInspectorTabAsync(page, "versions");
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Draft").GetByTestId("document-version-diff-base").ClickAsync();
            await DocumentEditorRedesignPhase7E2ETests.VersionEntry(page, "Final").GetByTestId("document-version-diff-compare").ClickAsync();
            await page.GetByTestId("document-version-show-diff").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToBeVisibleAsync(new() { Timeout = 15_000 });

            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToHaveAttributeAsync("data-view-mode", "unified");
            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToHaveAttributeAsync("data-segment", "differences");
            Assert.AreEqual(0, await page.GetByTestId("document-diff-view-mode").CountAsync(), $"{name}: no side-by-side selector on a phone");
            var segments = page.GetByTestId("document-diff-segments").GetByRole(AriaRole.Radio);
            Assert.AreEqual(3, await segments.CountAsync(), $"{name}: three segments");
            await segments.Nth(0).ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-diff-viewer")).ToHaveAttributeAsync("data-segment", "older", new() { Timeout = 10_000 });
            Assert.AreEqual(0, await page.Locator("[data-testid='document-diff-viewer'] [data-diff-kind]").CountAsync(), $"{name}: Older shows plain text");
            await AssertNoHorizontalScrollAsync(page, $"{name} diff");
            CollectionAssert.AreEqual(Array.Empty<string>(), errors);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A tablet (1180 and 1024, touch) has ONE side sheet that overlays the document: it never narrows the page, find replaces the tabs in it, targets are 44 px")]
    public async Task Tablet_HasOneSideSheet_ThatOverlaysTheDocument_AndTouchTargetsAre44px()
    {
        foreach (var (name, width, height, route, docId) in new[]
        {
            ("1180", 1180, 820, "/canvas-engine-host", RevisionsDocumentId),
            ("1024", 1024, 768, "/document-editor", RevisionsDocumentId),
        })
        {
            var (page, errors) = await OpenAsync(docId, width, height, touch: true, route: route);
            Assert.AreEqual("tablet", await LayoutAsync(page), $"{name}: a tablet container");
            var closed = await page.Locator("[data-region='doc']").BoundingBoxAsync();
            Assert.IsNotNull(closed);
            Assert.AreEqual(0, await page.GetByTestId("document-side-panel").CountAsync(), $"{name}: the sheet starts closed");
            await AssertTouchTargetAsync(page.GetByTestId("document-side-panel-edge-toggle"), $"{name}: edge toggle");
            await AssertNoOverlapAsync(page.GetByTestId("document-side-panel-edge-toggle"), page.GetByTestId("document-review-summary"), $"{name}: the closed sheet's edge toggle and the review summary");

            await page.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            var open = await page.Locator("[data-region='doc']").BoundingBoxAsync();
            Assert.IsNotNull(open);
            Assert.AreEqual(closed.Width, open.Width, 1.0, $"{name}: the sheet overlays the document, its width never changes");
            var panel = await page.GetByTestId("document-side-panel").BoundingBoxAsync();
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.X + panel.Width <= width + 0.5 && panel.X >= open.X - 1, $"{name}: the sheet sits over the document's end");

            // The sheet follows the reader down a tall document (it parks below the sticky ribbon) and never leaves the screen.
            // Phase 11: the engine fits a page wider than the column (this document's pages are about 1250 px wide), which makes the document SHORTER - the
            // scroll stays inside the document region: half of its height, at most 900 px.
            var scrollDistance = Math.Min(900, Math.Floor(open.Height / 2));
            await page.EvaluateAsync("distance => window.scrollBy(0, distance)", scrollDistance);
            await page.WaitForTimeoutAsync(400);
            var parked = await page.GetByTestId("document-side-panel").BoundingBoxAsync();
            Assert.IsNotNull(parked);
            Assert.IsTrue(parked.Y >= -1 && parked.Y < height / 2.0 && parked.Y + parked.Height <= height + 1, $"{name}: the sheet is parked on screen after scrolling ({parked.Y}, {parked.Height} in {height})");

            foreach (var tab in await page.Locator("[data-testid^='document-side-panel-tab-']").AllAsync())
            {
                await AssertTouchTargetAsync(tab, $"{name}: side panel tab");
            }

            await page.GetByTestId("document-side-panel-tab-comments").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-comment-filter")).ToBeVisibleAsync(new() { Timeout = 10_000 });

            // The fields and buttons of the panels are finger-sized: selects and inputs 3rem, comment buttons 44 px.
            foreach (var id in new[] { "document-comment-filter", "document-comment-sort" })
            {
                var field = await page.GetByTestId(id).BoundingBoxAsync();
                Assert.IsNotNull(field);
                Assert.IsTrue(field.Height >= 47.5, $"{name}: {id} is 3rem tall ({field.Height}px)");
            }

            foreach (var button in await page.Locator("[data-testid='document-comment-rail'] button:visible").AllAsync())
            {
                var box = await button.BoundingBoxAsync();
                Assert.IsNotNull(box);
                Assert.IsTrue(box.Height >= TouchTarget - 0.5, $"{name}: a comment button is a touch target ({box.Height}px)");
            }

            await AssertTouchTargetAsync(page.GetByTestId("document-side-panel-close"), $"{name}: side panel close");

            // One sheet: find replaces the tabs in the same sheet.
            await page.GetByTestId("document-header-search").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            Assert.AreEqual(1, await page.GetByTestId("document-side-panel").CountAsync(), $"{name}: still one sheet");
            await Assertions.Expect(page.GetByTestId("document-side-panel")).ToHaveAttributeAsync("data-context", "find");
            Assert.AreEqual(0, await page.GetByTestId("document-find-floating").CountAsync(), $"{name}: no floating find beside the sheet");

            // The find options and the clear button are finger-sized on a coarse pointer.
            await page.GetByTestId("document-find-input").FillAsync("the");
            foreach (var option in await page.Locator("[data-testid='document-find-options'] label").AllAsync())
            {
                var optionBox = await option.BoundingBoxAsync();
                Assert.IsNotNull(optionBox);
                Assert.IsTrue(optionBox.Height >= TouchTarget - 0.5, $"{name}: a find option row is a touch target ({optionBox.Height}px)");
            }

            await AssertTouchTargetAsync(page.GetByTestId("document-find-clear"), $"{name}: find clear");

            // The ribbon and the header controls are finger-sized too.
            foreach (var id in new[] { "document-header-search", "document-save" }.Concat(width < 360 ? Array.Empty<string>() : new[] { "document-undo", "document-redo" }))
            {
                await AssertTouchTargetAsync(page.GetByTestId(id).First, $"{name}: {id}");
            }

            foreach (var button in await page.Locator("[data-testid='document-ribbon-toolbar'] .tm-toolbar-btn:visible").AllAsync())
            {
                await AssertTouchTargetAsync(button, $"{name}: ribbon button");
            }

            await AssertNoHorizontalScrollAsync(page, name);
            CollectionAssert.AreEqual(Array.Empty<string>(), errors, $"{name}: no console or page error");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("The desktop is unchanged: navigation, document and the docked inspector, no action bar, find in the inspector (a 1600 viewport: the 1440 viewport's 1046 px editor presents the inspector as a sheet, see the readable floor)")]
    public async Task Desktop_1440_IsUnchanged()
    {
        var (page, errors) = await OpenAsync(RevisionsDocumentId, 1600, 900, touch: false);

        Assert.AreEqual("desktop", await LayoutAsync(page));
        Assert.AreEqual(0, await page.GetByTestId("document-mobile-action-bar").CountAsync(), "no action bar on a desktop");
        Assert.AreEqual(1, await page.GetByTestId("document-side-panel").CountAsync(), "the docked inspector is open");
        Assert.AreEqual(1, await page.GetByTestId("document-review-summary").CountAsync(), "the review summary is a banner");
        var doc = await page.Locator("[data-region='doc']").BoundingBoxAsync();
        var panel = await page.GetByTestId("document-side-panel").BoundingBoxAsync();
        Assert.IsTrue(doc is not null && panel is not null && doc.X + doc.Width <= panel.X + 1, "the inspector is docked BESIDE the document");
        var tabs = await page.Locator("[data-testid^='document-ribbon-tab-']").EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-testid').replace('document-ribbon-tab-', ''))");
        CollectionAssert.AreEqual(new[] { "home", "insert", "math", "layout", "references", "review", "view" }, tabs);
        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A pinned LayoutMode=Tablet is a tablet at ANY width (a full-width 1024 px editor resolves Desktop on its own): the side sheet, the edge toggle and finger-sized targets")]
    public async Task Tablet_PinnedLayoutMode_GivesTheTabletChrome()
    {
        var (page, errors) = await OpenAsync("contract-demo", 1440, 900, touch: true);
        await page.GetByTestId("document-editor-layout-mode").SelectOptionAsync("Tablet");
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === 'tablet' && document.querySelector('.tm-document-editor')?.getAttribute('data-layout-forced') === 'true'", 15_000);
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual(0, await page.GetByTestId("document-side-panel").CountAsync(), "the sheet starts closed");
        await AssertTouchTargetAsync(page.GetByTestId("document-side-panel-edge-toggle"), "pinned tablet: edge toggle");
        await page.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.GetByTestId("document-mobile-action-bar").CountAsync(), "no action bar on a tablet");
        CollectionAssert.AreEqual(Array.Empty<string>(), errors);
        await page.Context.CloseAsync();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Brings a canvas element to the middle of the screen and returns its centre. The canvas draws the pages that are on screen only, so the
    /// document is walked until the element exists, and the element is searched for again after every scroll (it can be redrawn).
    /// </summary>
    private static async Task<string> LayoutAsync(IPage page)
        => await page.Locator(".tm-document-editor").First.GetAttributeAsync("data-layout") ?? string.Empty;

    private static async Task CloseSheetAsync(IPage page)
    {
        var done = page.Locator(".tm-overlay-panel-sheet .tm-overlay-panel-sheet__done");
        await done.First.ClickAsync();
        await Assertions.Expect(page.Locator(".tm-overlay-panel-sheet")).ToHaveCountAsync(0, new() { Timeout = 10_000 });
    }

    private static async Task AssertNoHorizontalScrollAsync(IPage page, string context)
    {
        var overflow = await page.EvaluateAsync<double[]>("() => [document.documentElement.scrollWidth, window.innerWidth, document.body.scrollWidth]");
        Assert.IsTrue(overflow[0] <= overflow[1] + 1 && overflow[2] <= overflow[1] + 1, $"{context}: the page scrolls sideways ({overflow[0]} / {overflow[2]} > {overflow[1]})");
    }

    private static async Task AssertNoOverlapAsync(ILocator first, ILocator second, string what)
    {
        if (await second.CountAsync() == 0 || await first.CountAsync() == 0)
        {
            return;
        }

        var a = await first.BoundingBoxAsync();
        var b = await second.BoundingBoxAsync();
        if (a is null || b is null)
        {
            return;
        }

        var overlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
        Assert.IsFalse(overlap, $"{what} overlap ({a.X:0},{a.Y:0},{a.Width:0},{a.Height:0}) vs ({b.X:0},{b.Y:0},{b.Width:0},{b.Height:0})");
    }

    private static async Task AssertTouchTargetAsync(ILocator locator, string what)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, $"{what}: measurable");
        Assert.IsTrue(box.Height >= TouchTarget - 0.5 && box.Width >= TouchTarget - 0.5, $"{what}: at least {TouchTarget}px ({box.Width} x {box.Height})");
    }

    private static async Task<(string Block, string Offset)> ReadSelectionAsync(IPage page)
    {
        var values = await page.EvaluateAsync<string[]>("""
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]') ?? document.querySelector('[data-canvas-selection-focus-block-id]');
                return [root?.getAttribute('data-canvas-selection-focus-block-id') ?? '', root?.getAttribute('data-canvas-selection-focus-offset') ?? ''];
            }
            """);
        return (values[0], values[1]);
    }

    private async Task<(IPage Page, List<string> Errors)> OpenAsync(string documentId, int width, int height, bool touch, string route = "/document-editor", string? culture = null)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        if (culture is not null)
        {
            // The demo reads its culture from localStorage (the language switcher writes it); a context that starts with it needs no click on a phone-sized page.
            await context.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}')");
        }

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add($"pageerror: {message}");
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.Contains("favicon", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"console: {message.Text}");
            }
        };

        var url = route == "/document-editor" ? $"{BaseUrl}{route}?documentId={documentId}" : OpenUrl(BaseUrl, documentId, route);
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(600);
        return (page, errors);
    }
}
