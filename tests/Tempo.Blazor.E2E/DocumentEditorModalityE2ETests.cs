using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 1 (wave 0): behavioural assertions of the modality / token / localization fixes.
/// Writes NO files (the screenshots are <see cref="DocumentEditorRedesignBaselineE2ETests"/>, a gated generator).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorModalityE2ETests : WasmTestBase
{
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task MainSurface_NeverOverflowsHorizontally_AtFourWidths()
    {
        foreach (var viewport in SurfaceViewports)
        {
            var page = await OpenAsync(HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            var overflow = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
            Assert.IsTrue(overflow <= 1, $"{viewport.Name}: document must not overflow horizontally (by {overflow}px)");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task FindPanel_UsesDefinedTokens_FollowsTheTheme_AndStaysInsideNarrowViewports()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false);
        await OpenFindAsync(page);
        var light = await ReadPanelPaintAsync(page);
        await ApplyDarkThemeAsync(page);
        var dark = await ReadPanelPaintAsync(page);

        // The panel used undefined aliases (--tm-surface, --tm-border ...) whose literal fallbacks stayed white in dark.
        Assert.AreNotEqual(light.Background, dark.Background, "the find panel must follow the theme (dark background differs)");
        Assert.AreNotEqual("rgba(0, 0, 0, 0)", dark.Background, "the dark find panel must paint a surface");
        Assert.AreNotEqual("rgb(224, 224, 224)", light.InputBorder, "the input border comes from --tm-border-color, not the old #e0e0e0 fallback");
        Assert.AreNotEqual("rgb(224, 224, 224)", dark.InputBorder, "the dark input border must not keep the light fallback");
        await page.Context.CloseAsync();

        // Edge: the panel clamps to the viewport (it was clipped at 390 / 320 by a fixed 20rem minimum).
        foreach (var (width, height) in new[] { (390, 844), (320, 640) })
        {
            var phone = await OpenAsync(HistoryDocumentId, width, height, true);
            await OpenFindAsync(phone);
            var box = await phone.GetByTestId("document-find-panel").BoundingBoxAsync();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.X >= -0.5, $"{width}px: the find panel must not start left of the viewport (x={box.X})");
            Assert.IsTrue(box.X + box.Width <= width + 0.5, $"{width}px: the find panel must not extend past the right edge (right={box.X + box.Width})");
            await phone.Context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task CompareDialog_HasScrim_IsCentered_DoesNotShiftTheWorkspace_AndFollowsTheTheme()
    {
        foreach (var viewport in PanelViewports)
        {
            var page = await OpenAsync(HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
            var before = await DocumentEditorLayoutShiftGuard.MeasureWorkspaceAsync(page);
            await DocumentEditorRedesignE2EHelpers.ClickRibbonAsync(page, "document-compare-open");
            var dialog = page.GetByTestId("document-compare-dialog");
            await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(300);

            await DocumentEditorLayoutShiftGuard.AssertWorkspaceUnchangedAsync(page, before, $"compare dialog @{viewport.Name}");
            // Phase 5 (G6): the compare dialog is a core TmModal - the overlay is the scrim, .tm-modal-container the card (fullscreen on a phone).
            await AssertModalAsync(dialog.Locator(".tm-modal-container"), dialog, "compare dialog", viewport.Width, viewport.Height);
            Assert.AreEqual(0, await page.Locator("[data-testid='document-floating-root'] [data-testid='document-compare-dialog']").CountAsync(),
                "the compare dialog is a sibling of the floating root, never its child");
            await AssertFooterButtonsAreCompactAsync(dialog, "compare dialog", viewport.Name);

            var lightBackground = await BackgroundOfAsync(dialog.Locator("[role='dialog']"));
            await ApplyDarkThemeAsync(page);
            Assert.AreNotEqual(lightBackground, await BackgroundOfAsync(dialog.Locator("[role='dialog']")), $"compare dialog @{viewport.Name} must follow the dark theme");
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task VersionDialogAndPanel_UseLocalizedKind_AScrim_AndCompactFooterButtons()
    {
        foreach (var viewport in PanelViewports)
        {
            // The canvas-engine-host demo hides the version history; the full editor page offers it.
            var page = await OpenAsync(HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch, "/document-editor");
            await ClickRibbonAsync(page, "document-ribbon-tab-view");
            await ClickRibbonAsync(page, "document-open-versions");
            await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

            await page.GetByTestId("document-version-create-open").ClickAsync();
            var dialog = page.GetByTestId("document-version-dialog");
            await Assertions.Expect(dialog).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(300);
            if (viewport.Width >= 640)
            {
                await AssertModalAsync(dialog.Locator(".tm-modal-container"), dialog, "version dialog", viewport.Width, viewport.Height);
            }
            else
            {
                // A bottom sheet on a phone: inside the viewport, full width, resting on the bottom edge.
                var sheet = (await dialog.Locator(".tm-modal-container").BoundingBoxAsync())!;
                Assert.IsTrue(sheet.X >= -0.5 && sheet.X + sheet.Width <= viewport.Width + 0.5, "the version sheet fits the phone width");
                Assert.IsTrue(sheet.Y + sheet.Height >= viewport.Height - 2, $"the version sheet rests on the bottom edge ({sheet.Y + sheet.Height} of {viewport.Height})");
            }

            await AssertFooterButtonsAreCompactAsync(dialog, "version dialog", viewport.Name);

            await page.GetByTestId("document-version-label").FillAsync("Baseline");
            await page.GetByTestId("document-version-create-submit").ClickAsync();
            await Assertions.Expect(dialog).ToBeHiddenAsync(new() { Timeout = 10_000 });
            await Assertions.Expect(page.GetByTestId("document-version-item").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

            var meta = await page.Locator(".tm-document-version-panel__item-meta").AllInnerTextsAsync();
            Assert.IsTrue(meta.Count > 0, "a created version must be listed");
            foreach (var text in meta)
            {
                var kind = text.Split('•')[0].Trim();
                Assert.IsFalse(Regex.IsMatch(kind, "^(Minor|Major|Autosave|Restore)$"),
                    $"version meta must show the localized kind, got the enum name '{kind}'");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task RevisionPanel_NeverShowsRawPayload()
    {
        foreach (var viewport in PanelViewports)
        {
            var page = await OpenAsync(RevisionsDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
            await Assertions.Expect(page.GetByTestId("document-revision-item").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

            var previews = await page.Locator(".tm-document-revision-panel__preview").AllInnerTextsAsync();
            Assert.IsTrue(previews.Count > 0, "the seeded document carries pending revisions");
            foreach (var preview in previews)
            {
                var trimmed = preview.Trim();
                Assert.IsFalse(trimmed.StartsWith('{') || trimmed.StartsWith('['), $"revision preview must not be raw JSON: '{trimmed}'");
                Assert.IsFalse(Regex.IsMatch(trimmed, "^(splitBlock|InsertBlock|mergeBlock)$", RegexOptions.IgnoreCase), $"revision preview must not be an internal id: '{trimmed}'");
            }

            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    public async Task DebugModals_HaveScrim_AreCentered_AndDoNotShiftTheWorkspace()
    {
        foreach (var viewport in PanelViewports)
        {
            var page = await OpenAsync(HistoryDocumentId, viewport.Width, viewport.Height, viewport.Touch);
            await ClickRibbonAsync(page, "document-ribbon-tab-view");
            var before = await DocumentEditorLayoutShiftGuard.MeasureWorkspaceAsync(page);

            await ClickRibbonAsync(page, "document-view-json");
            var json = page.GetByTestId("document-json-debug-modal");
            await Assertions.Expect(json).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(300);
            await DocumentEditorLayoutShiftGuard.AssertWorkspaceUnchangedAsync(page, before, $"JSON debug modal @{viewport.Name}");
            await AssertModalAsync(json.Locator(".tm-modal-container"), json, "JSON debug modal", viewport.Width, viewport.Height);
            Assert.AreEqual(0, await page.Locator("[data-testid='document-floating-root'] [data-testid='document-json-debug-modal']").CountAsync(), "the core modal is a sibling of the floating root");

            var lightBackground = await BackgroundOfAsync(json.Locator("[role='dialog']"));
            await ApplyDarkThemeAsync(page);
            Assert.AreNotEqual(lightBackground, await BackgroundOfAsync(json.Locator("[role='dialog']")), $"JSON modal @{viewport.Name} must follow the dark theme");
            await page.GetByTestId("document-json-debug-close").ClickAsync();
            await Assertions.Expect(json).ToBeHiddenAsync(new() { Timeout = 5_000 });
            await DocumentEditorLayoutShiftGuard.AssertWorkspaceUnchangedAsync(page, before, $"closing the JSON debug modal @{viewport.Name}");

            await ClickRibbonAsync(page, "document-view-clipboard-html");
            var clipboard = page.GetByTestId("document-clipboard-html-debug-modal");
            await Assertions.Expect(clipboard).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(300);
            await DocumentEditorLayoutShiftGuard.AssertWorkspaceUnchangedAsync(page, before, $"clipboard debug modal @{viewport.Name}");
            await AssertModalAsync(clipboard.Locator(".tm-modal-container"), clipboard, "clipboard HTML debug modal", viewport.Width, viewport.Height);
            Assert.AreEqual(0, await page.Locator("[data-testid='document-floating-root'] [data-testid='document-clipboard-html-debug-modal']").CountAsync(), "the core modal is a sibling of the floating root");
            await page.Context.CloseAsync();
        }
    }

    /// <summary>The footer buttons of a dialog keep their natural height (a phone dialog stretched them to ~250 px).</summary>
    private static async Task AssertFooterButtonsAreCompactAsync(ILocator dialog, string name, string viewportName)
    {
        var buttons = dialog.Locator(".tm-modal-footer button");
        var count = await buttons.CountAsync();
        Assert.IsTrue(count > 0, $"{name} must have footer buttons");
        for (var i = 0; i < count; i++)
        {
            var box = await buttons.Nth(i).BoundingBoxAsync();
            Assert.IsNotNull(box);
            Assert.IsTrue(box.Height <= 48, $"{name} @{viewportName}: footer button {i} is {box.Height}px tall (stretched layout)");
        }
    }

    private static async Task OpenFindAsync(IPage page)
    {
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+F");
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-find-input").FillAsync("history");
        await page.GetByTestId("document-find-panel").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
    }

    private static async Task<(string Background, string InputBorder)> ReadPanelPaintAsync(IPage page)
    {
        var values = await page.EvaluateAsync<string[]>(
            """
            () => {
                const panel = document.querySelector('[data-testid="document-find-panel"]');
                const input = document.querySelector('[data-testid="document-find-input"]');
                return [getComputedStyle(panel).backgroundColor, getComputedStyle(input).borderTopColor];
            }
            """);
        return (values[0], values[1]);
    }

    private async Task<IPage> OpenAsync(string documentId, int width, int height, bool touch, string route = "/canvas-engine-host")
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, OpenUrl(BaseUrl, documentId, route), WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 45_000);
        await page.WaitForTimeoutAsync(400);
        return page;
    }
}
