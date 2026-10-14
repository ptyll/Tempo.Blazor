using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E tests for the Notion editor page history panel.
/// Pre-seeded data: 3 versions for Page 1 ("Getting Started") — Demo User (2h ago),
/// Bob Smith (1d ago), Alice Johnson (3d ago). The first version is always "current".
/// </summary>
[TestClass]
public class NotionPageHistoryE2ETests : WasmTestBase
{
    // ══════════════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════════════

    private async Task<IPage> OpenNotionEditorAsync()
    {
        using var http = DemoApiTestHttp.CreateClient();
        try { await http.PostAsync("https://localhost:5100/api/notion/reset", null); }
        catch { /* ignore if API unavailable or cert untrusted */ }

        var context = await CreateContextAsync();
        var page    = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor");
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(2000);
        return page;
    }

    /// <summary>
    /// Opens the page settings menu and clicks "Page history".
    /// Returns the visible history panel locator.
    /// </summary>
    private async Task<ILocator> OpenPageHistoryAsync(IPage page)
    {
        var trigger = page.Locator(".tm-npsm-trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await trigger.ClickAsync();

        var menu = page.Locator(".tm-npsm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });

        var historyItem = menu.Locator(".tm-npsm__item")
                             .Filter(new LocatorFilterOptions { HasText = "Page history" })
                             .First;
        await historyItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await historyItem.ClickAsync();

        var panel = page.Locator(".tm-nph").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        return panel;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Tests
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Opening Page History via the settings menu shows the history panel")]
    public async Task PageHistory_OpenPanel_ShowsVersionList()
    {
        var page  = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);

        Assert.IsTrue(await panel.IsVisibleAsync(), "History panel should be visible after opening");

        // Version list sidebar should be present
        var sidebar = panel.Locator(".tm-nph__sidebar").First;
        Assert.IsTrue(await sidebar.IsVisibleAsync(), "Version list sidebar should be visible");

        await TakeScreenshotAsync(page, "history_panel_open");
    }

    [TestMethod]
    [Description("The version list contains at least one entry with a date and author name")]
    public async Task PageHistory_ListHasVersions()
    {
        var page  = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);

        // Wait for versions to load (spinner disappears, items appear)
        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var count = await versionItems.CountAsync();
        Assert.IsTrue(count >= 1, $"Version list should have at least 1 version, got {count}");

        // Verify first item has time and author
        var firstItem  = versionItems.First;
        var timeEl     = firstItem.Locator(".tm-nph__version-time").First;
        var authorEl   = firstItem.Locator(".tm-nph__version-author-name").First;

        Assert.IsTrue(await timeEl.IsVisibleAsync(),   "First version should display a timestamp");
        Assert.IsTrue(await authorEl.IsVisibleAsync(),  "First version should display an author name");

        var authorText = await authorEl.InnerTextAsync();
        Assert.IsFalse(string.IsNullOrWhiteSpace(authorText), "Author name should not be empty");

        await TakeScreenshotAsync(page, "history_version_list");
    }

    [TestMethod]
    [Description("Clicking a version in the list shows a preview of its block snapshot")]
    public async Task PageHistory_ClickVersion_ShowsPreview()
    {
        var page  = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);

        // Wait for version items
        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Click the second version (index 1 = "Bob Smith", non-current)
        var secondItem = versionItems.Nth(1);
        await secondItem.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await secondItem.ClickAsync();

        // Preview content area should appear
        var preview = panel.Locator(".tm-nph__preview").First;
        await preview.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await preview.IsVisibleAsync(), "Preview area should appear after selecting a version");

        // Preview should contain block elements
        var previewBlocks = preview.Locator(".tm-nph__preview-block");
        await previewBlocks.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        var blockCount = await previewBlocks.CountAsync();
        Assert.IsTrue(blockCount >= 1, $"Preview should show at least 1 block, got {blockCount}");

        // Restore button should be visible in toolbar
        var restoreBtn = panel.Locator(".tm-nph__toolbar-btn--primary").First;
        Assert.IsTrue(await restoreBtn.IsVisibleAsync(), "Restore button should be visible after selecting a version");

        await TakeScreenshotAsync(page, "history_version_preview");
    }

    [TestMethod]
    [Description("Clicking Restore on a non-current version shows confirm dialog then closes the panel")]
    public async Task PageHistory_RestoreVersion_AddsAsNewHead()
    {
        var page  = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);

        // Select the second version (non-current — Restore is enabled for it)
        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await versionItems.Nth(1).ClickAsync();

        // Wait for the preview toolbar
        var restoreBtn = panel.Locator(".tm-nph__toolbar-btn--primary").First;
        await restoreBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        // Click Restore → confirm dialog appears
        await restoreBtn.ClickAsync();
        var confirmDialog = page.Locator(".tm-nph-confirm").First;
        await confirmDialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        Assert.IsTrue(await confirmDialog.IsVisibleAsync(), "Restore confirm dialog should appear");

        await TakeScreenshotAsync(page, "history_restore_confirm");

        // Confirm the restore — panel should close
        var okBtn = confirmDialog.Locator(".tm-nph-confirm__ok").First;
        await okBtn.ClickAsync();
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        Assert.IsFalse(await panel.IsVisibleAsync(), "History panel should close after confirming restore");

        await TakeScreenshotAsync(page, "history_restored");
    }

    [TestMethod]
    [Description("Clicking the X button in the history panel closes it")]
    public async Task PageHistory_ClosePanel()
    {
        var page  = await OpenNotionEditorAsync();
        var panel = await OpenPageHistoryAsync(page);

        var closeBtn = panel.Locator(".tm-nph__close-btn").First;
        await closeBtn.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await closeBtn.ClickAsync();

        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });
        Assert.IsFalse(await panel.IsVisibleAsync(), "History panel should be closed after clicking X");

        await TakeScreenshotAsync(page, "history_closed");
    }
}

/// <summary>
/// Screenshot and UX recovery coverage for EB13 page history states.
/// </summary>
[TestClass]
public class NotionPageHistoryRecoveryE2ETests : NotionE2ETestBase
{
    [TestMethod]
    [Description("EB13 captures the empty page history panel loaded from the HTTPS Demo API")]
    public async Task EB13_EmptyHistoryPanel_IsCaptured()
    {
        var page = await OpenNotionEditorAsync(1280, 900);
        await SeedHistoryEmptyPageAsync();

        var panel = await OpenPageHistoryPanelAsync(page);

        await Assertions.Expect(panel.Locator(".tm-nph__version-item")).ToHaveCountAsync(0);
        await Assertions.Expect(panel.Locator(".tm-nph__status").First).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".tm-nph__empty-state").First).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByText("No history yet")).ToBeVisibleAsync();
        await Assertions.Expect(panel.GetByText("Select a version")).ToHaveCountAsync(0);

        await AssertNoHorizontalOverflowAsync(panel, "EB13 empty history panel");
        await CaptureBaselineAsync("page-history", "empty-history-panel", panel);
    }

    [TestMethod]
    [Description("EB13 captures many page history versions with readable preview and scrolled list state")]
    public async Task EB13_ManyVersionsPreviewAndScrolled_AreCaptured()
    {
        var page = await OpenNotionEditorAsync(1280, 900);
        await SeedHistoryManyPageAsync();

        var panel = await OpenPageHistoryPanelAsync(page);
        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        Assert.AreEqual(20, await versionItems.CountAsync(), "The first history page should use the production page size and show 20 versions.");

        await versionItems.Nth(1).ClickAsync();
        await Assertions.Expect(panel.Locator(".tm-nph__preview").First).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".tm-nph__preview-block").First).ToBeVisibleAsync();
        await Assertions.Expect(panel.Locator(".tm-nph__toolbar-btn--primary").First).ToBeEnabledAsync();
        await Assertions.Expect(panel.GetByText("EB13 History Version 45")).ToBeVisibleAsync();

        await AssertNoHorizontalOverflowAsync(panel, "EB13 many versions preview");
        await CaptureBaselineAsync("page-history", "many-versions-preview", panel);

        await panel.Locator(".tm-nph__page-btn").Nth(1).ClickAsync();
        await Assertions.Expect(panel.Locator(".tm-nph__page-info").First).ToContainTextAsync("2");
        await panel.Locator(".tm-nph__version-list").EvaluateAsync("el => { el.scrollTop = el.scrollHeight; }");
        await page.WaitForTimeoutAsync(300);

        await AssertNoHorizontalOverflowAsync(panel, "EB13 many versions scrolled");
        await CaptureBaselineAsync("page-history", "many-versions-scrolled", panel);
    }

    [TestMethod]
    [Description("EB13 captures restore confirmation and verifies restored page content through the HTTPS Demo API")]
    public async Task EB13_RestoreConfirmAndRestoredPageContent_AreCaptured()
    {
        var page = await OpenNotionEditorAsync(1280, 900);
        await SeedHistoryManyPageAsync();

        var panel = await OpenPageHistoryPanelAsync(page);
        var versionItems = panel.Locator(".tm-nph__version-item");
        await versionItems.First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        await versionItems.Nth(1).ClickAsync();
        var restoreButton = panel.Locator(".tm-nph__toolbar-btn--primary").First;
        await restoreButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await restoreButton.ClickAsync();

        var confirmDialog = page.Locator(".tm-nph-confirm").First;
        await confirmDialog.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await AssertNoHorizontalOverflowAsync(confirmDialog, "EB13 restore confirmation dialog");
        await CaptureBaselineAsync("page-history", "restore-confirm", confirmDialog);

        await confirmDialog.Locator(".tm-nph-confirm__ok").First.ClickAsync();
        await panel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 15000
        });

        var restoredHeading = page.GetByText("EB13 History Version 45").First;
        await restoredHeading.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });
        await Assertions.Expect(page.GetByText("Restorable body snapshot for version 45")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Checkpoint 45 keeps a deterministic preview")).ToBeVisibleAsync();

        var restoredPage = page.Locator(".tm-notion-page").First;
        await AssertNoHorizontalOverflowAsync(restoredPage, "EB13 restored page content");
        await CaptureBaselineAsync("page-history", "restored-page-content", restoredPage);
    }

    private static async Task<ILocator> OpenPageHistoryPanelAsync(IPage page)
    {
        var trigger = page.Locator(".tm-npsm-trigger").First;
        await trigger.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await trigger.ClickAsync();

        var menu = page.Locator(".tm-npsm").First;
        await menu.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });

        var historyItem = menu.Locator(".tm-npsm__item")
            .Filter(new LocatorFilterOptions { HasText = "Page history" })
            .First;
        await historyItem.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await historyItem.ClickAsync();

        var panel = page.Locator(".tm-nph").First;
        await panel.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        return panel;
    }

    private static async Task AssertNoHorizontalOverflowAsync(ILocator locator, string label)
    {
        var hasOverflow = await locator.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth + 1");
        Assert.IsFalse(hasOverflow, $"{label} should not have unintended horizontal overflow.");
    }
}
