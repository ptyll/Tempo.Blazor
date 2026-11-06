using System.Net.Http;
using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-4 evidence for the dialog unification (plan c8d4bcd2, N1/N3/DB-3/P3): the page-header
/// dialogs, the page-info panel, the sidebar bulk picker and trash, the database dialogs and the
/// block/text comment presentations. Light and dark, desktop 1440 / tablet 1024 / mobile 390.
/// Lands in tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-4/. Evidence only.
/// </summary>
[TestClass]
[TestCategory("NotionUxBaseline")]
public sealed class NotionPhase4DialogScreenshots : NotionE2ETestBase
{
    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-4");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir ?? throw new DirectoryNotFoundException("TempoBlazor.slnx");
    }

    private static async Task SetThemeAsync(IPage page, string theme)
    {
        await page.EvaluateAsync(
            """
            theme => {
                document.documentElement.setAttribute('data-theme', theme);
                const shell = document.querySelector('.demo-shell');
                if (shell) shell.setAttribute('data-theme', theme);
            }
            """,
            theme);
        await page.WaitForTimeoutAsync(200);
    }

    private async Task ShotRegionAsync(IPage page, string area, string name, ILocator region)
    {
        var path = Path.Combine(ShotDir(area), name + ".png");
        await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
        TestContext.WriteLine($"phase-4 shot: {path}");
    }

    /// <summary>
    /// Clip-based capture for surfaces Playwright's actionability probe reports hidden even
    /// though they paint (anchored popovers in the browser top layer): the rect is measured in
    /// the page and the viewport screenshot is clipped to it.
    /// </summary>
    private async Task ShotRegionClipAsync(IPage page, string area, string name, ILocator region)
    {
        var box = await region.EvaluateAsync<JsonElement>(
            """
            el => {
                const r = el.getBoundingClientRect();
                return { x: r.x, y: r.y, width: r.width, height: r.height,
                         display: getComputedStyle(el).display,
                         popoverOpen: el.matches ? el.matches(':popover-open') : null };
            }
            """);
        TestContext.WriteLine($"phase-4 clip shot {name}: {box}");
        var path = Path.Combine(ShotDir(area), name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            Clip = new Clip
            {
                X = box.GetProperty("x").GetSingle(),
                Y = box.GetProperty("y").GetSingle(),
                Width = Math.Max(1, box.GetProperty("width").GetSingle()),
                Height = Math.Max(1, box.GetProperty("height").GetSingle()),
            },
        });
        TestContext.WriteLine($"phase-4 shot: {path}");
    }

    private async Task OpenCoverDialogAsync(IPage page)
    {
        var addCover = page.Locator(".tm-notion-header-meta__add-btn",
            new PageLocatorOptions { HasText = "Add cover" }).First;
        if (await addCover.CountAsync() == 0)
        {
            addCover = page.Locator(".tm-notion-header-cover__btn",
                new PageLocatorOptions { HasText = "Change cover" }).First;
        }
        await addCover.EvaluateAsync("el => el.click()");
        await page.Locator(".tm-notion-cover-dialog__input").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
    }

    [TestMethod]
    [Description("Cover dialog: centred core TmModal at 1440, dialog ~80% at 1024-touch, bottom sheet at 390. Light and dark.")]
    public async Task Phase4_PageHeader_CoverDialog()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            await OpenCoverDialogAsync(page);
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-header--cover-dialog-title--desktop-{theme}",
                page.Locator(".tm-modal-overlay").First);
            await page.Keyboard.PressAsync("Escape");
            await page.Locator(".tm-notion-cover-dialog__input").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden, Timeout = 5000
            });

            await page.SetViewportSizeAsync(1024, 768);
            await page.WaitForTimeoutAsync(300);
            await OpenCoverDialogAsync(page);
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-header--cover-dialog-title--tablet-{theme}",
                page.Locator(".tm-modal-overlay").First);
            await page.Keyboard.PressAsync("Escape");
            await page.Locator(".tm-notion-cover-dialog__input").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden, Timeout = 5000
            });

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
            await OpenCoverDialogAsync(page);
            var sheet = page.Locator(".tm-modal--sheet").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-header--cover-dialog-title--mobile-{theme}", sheet);
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("Emoji picker: titled core TmModal at 1440 and its sheet at 390. Light and dark.")]
    public async Task Phase4_PageHeader_EmojiPicker()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            var icon = page.Locator(".tm-notion-header-icon").First;
            await icon.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
            await icon.ClickAsync();
            await page.Locator(".tm-notion-emoji-picker__search").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible, Timeout = 8000
            });
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-header--emoji-picker-title--desktop-{theme}",
                page.Locator(".tm-modal-overlay").First);
            await page.Keyboard.PressAsync("Escape");
            await page.Locator(".tm-notion-emoji-picker__search").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden, Timeout = 5000
            });

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
            await icon.EvaluateAsync("el => el.click()");
            var sheet = page.Locator(".tm-modal--sheet").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-header--emoji-picker-title--mobile-{theme}", sheet);
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("Page info panel: core TmModal at 1440, sheet at 390. Light and dark.")]
    public async Task Phase4_PageInfoPanel()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedPageInfoPageAsync();
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            await page.WaitForTimeoutAsync(300);
            // The 390 pass scrolls the editor into view, which can leave the trigger above the
            // viewport; an anchored popover parks (visibility:hidden) while its anchor is off-screen.
            await page.Locator(".tm-npsm-trigger").First.ScrollIntoViewIfNeededAsync();
            await page.Locator(".tm-npsm-trigger").First.EvaluateAsync("el => el.click()");
            var pageInfo = page.Locator(".tm-npsm__item").Filter(new LocatorFilterOptions { HasText = "Page info" }).First;
            await pageInfo.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await pageInfo.ClickAsync();
            var panel = page.Locator("[data-page-info-panel]").First;
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-info-panel--page-info_title--desktop-{theme}", panel);
            await page.Keyboard.PressAsync("Escape");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
            await page.Locator(".tm-npsm-trigger").First.EvaluateAsync("el => el.click()");
            var pageInfoSheet = page.Locator(".tm-npsm__item").Filter(new LocatorFilterOptions { HasText = "Page info" }).First;
            await pageInfoSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await pageInfoSheet.ClickAsync();
            var sheet = page.Locator(".tm-modal--sheet, [data-page-info-panel]").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-info-panel--page-info_title--mobile-{theme}", sheet);
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("Block comment: Auto keeps the anchored popover at 1440 and opens the core sheet at 390 (composer above the sheet edge). Light and dark.")]
    public async Task Phase4_BlockCommentPresentation()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedCommentsPageAsync();
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            await OpenFirstBlockCommentAsync(page);
            var panel = page.Locator(".tm-nbcp").First;
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            Assert.IsNull(await panel.GetAttributeAsync("aria-modal"),
                "the anchored popover must not claim aria-modal");
            // Batch B: capture the WHOLE overlay panel (chrome + arrow + positioned bubble), not
            // just the card — the overlay owns the geometry after overlay.js positions it.
            await page.WaitForTimeoutAsync(300);
            await ShotRegionAsync(page, "notion-panels",
                $"notion-block-comment-panel--desktop-{theme}",
                page.Locator(".tm-overlay-panel:has(.tm-nbcp)").First);
            // The anchored popover owns no document-level Escape (no backdrop, no trap) — close
            // it with its ×; the mobile sheet below IS a core TmModal and closes on Escape.
            await panel.Locator(".tm-nbcp__close-btn").First.ClickAsync();
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
            await OpenFirstBlockCommentAsync(page);
            var sheet = page.Locator(".tm-modal--sheet .tm-nbcp").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-panels",
                $"notion-block-comment-panel--mobile-{theme}", page.Locator(".tm-modal--sheet").First);
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("Text comment: anchored popover at 1440, core sheet at 390 with the whole quote. Light and dark.")]
    public async Task Phase4_TextCommentPresentation()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedCommentsPageAsync();
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            await OpenTextCommentAsync(page);
            var panel = page.Locator(".tm-ntcp").First;
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            // Batch B: whole overlay panel after positioning (see the block capture).
            await page.WaitForTimeoutAsync(300);
            await ShotRegionAsync(page, "notion-panels",
                $"notion-text-comment-panel--desktop-{theme}",
                page.Locator(".tm-overlay-panel:has(.tm-ntcp)").First);
            // Same contract as the block popover: × closes the anchored bubble (no own Escape).
            await panel.Locator(".tm-ntcp__close-btn").First.ClickAsync();
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 5000 });

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
            await OpenTextCommentAsync(page);
            var sheet = page.Locator(".tm-modal--sheet .tm-ntcp").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-panels",
                $"notion-text-comment-panel--mobile-{theme}", page.Locator(".tm-modal--sheet").First);
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("Trash at 390 in the mobile drawer: card rows with the parent path and the inline confirm.")]
    public async Task Phase4_SidebarTrash_MobileDrawer()
    {
        var page = await OpenNotionEditorAsync(390, 844);
        await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
        // The sidebar is the mobile drawer — open it before seeding so the trash badge renders.
        await page.Locator(".tm-notion-sidebar-toggle").First.EvaluateAsync("el => el.click()");
        await page.Locator(".tm-notion-sidebar--visible").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        // The demo's seed refreshes the client through the desktop sidebar path; reload so the
        // mobile drawer picks the seeded trash up on its own load, then reopen the drawer.
        await InvokeSeedAsync("seedSidebarTrashPage");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = 60000 });
        await page.Locator(".tm-notion-editor").First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
        await page.Locator(".tm-notion-sidebar-toggle").First.EvaluateAsync("el => el.click()");
        await page.Locator(".tm-notion-sidebar--visible").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 5000
        });
        await page.Locator(".tm-ns-trash__btn").First.ClickAsync();
        var trash = page.Locator(".tm-nst").First;
        await trash.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await trash.Locator(".tm-nst-item, .tm-nst-empty").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
        await page.WaitForTimeoutAsync(300);
        await ShotRegionAsync(page, "notion-sidebar", "notion-sidebar-trash--mobile-light", trash);

        var item = page.Locator(".tm-nst-item").First;
        await item.Locator(".tm-nst-item__btn--delete").First.EvaluateAsync("el => el.click()");
        var confirm = item.Locator(".tm-nst-item__confirm").First;
        await confirm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
        await ShotRegionAsync(page, "notion-sidebar", "notion-sidebar-trash--delete--mobile-light", item);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Batch B evidence (UX findings follow-up)
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("Cover dialog with a preset selected: apply a preset, reopen Change cover — the current swatch shows ✓ + aria-pressed. Light and dark.")]
    public async Task Phase4_PageHeader_CoverDialog_PresetSelected()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);

            // Picking a preset applies it and closes the dialog (apply-on-select), so the
            // selected-state evidence is the REOPENED dialog marking the current cover pressed.
            await OpenCoverDialogAsync(page);
            await page.Locator(".tm-notion-cover-dialog__preset").Nth(2)
                .EvaluateAsync("el => el.click()");
            await page.Locator(".tm-notion-header-cover").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8000
            });

            await OpenCoverDialogAsync(page);
            var pressed = page.Locator(".tm-notion-cover-dialog__preset[aria-pressed='true']");
            await pressed.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 8000 });
            Assert.AreEqual(1, await pressed.CountAsync(), "exactly the applied preset is pressed");
            Assert.AreEqual(1, await page.Locator(".tm-notion-cover-dialog__preset-check").CountAsync(),
                "the pressed preset carries the visible check");
            await page.WaitForTimeoutAsync(250);
            await ShotRegionAsync(page, "notion-page-dialogs",
                $"notion-page-header--cover-dialog-preset--desktop-{theme}",
                page.Locator(".tm-modal-overlay").First);
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("Comment entry delete confirm (core TmDialog) in the 1440 comment dialog, light and dark.")]
    public async Task Phase4_CommentDeleteConfirm()
    {
        var page = await OpenNotionEditorAsync(1440, 900, "?comments=dialog");
        await SeedCommentsPageAsync();
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await OpenFirstBlockCommentAsync(page);
            var card = page.Locator(".tm-modal-overlay .tm-nbcp").First;
            await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

            // The seed only creates blocks — write a thread so the detail has an entry to delete.
            var composer = card.Locator(".tm-nbcp__reply-input").First;
            await composer.FillAsync($"Phase-4 delete-confirm evidence ({theme}).");
            await card.Locator(".tm-nbcp__reply-send").First.ClickAsync();
            await page.WaitForTimeoutAsync(500);

            await page.Locator(".tm-nbcp__entry").First.HoverAsync();
            // Delete lives in the ⋯ entry menu (core TmDropdown), not as a standalone hover button.
            var deleteItem = await NotionCommentsE2ETests.OpenCommentEntryMenuAsync(page, "tm-nbcp", "delete");
            await deleteItem.EvaluateAsync("el => el.click()");
            var dialog = page.Locator(".tm-dialog").First;
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 5000 });
            await ShotRegionAsync(page, "notion-panels",
                $"notion-comment-delete-confirm--desktop-{theme}", dialog);
            await page.Locator(".tm-dialog-btn-cancel").First.ClickAsync();
            await page.WaitForTimeoutAsync(250);
            await page.Locator(".tm-modal-overlay .tm-nbcp .tm-nbcp__close-btn").First
                .EvaluateAsync("el => el.click()");
            await page.Locator(".tm-modal-overlay").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Hidden,
                Timeout = 5000,
            });
        }
    }

    [TestMethod]
    [Description("Trash with a deleted child page: the row shows the parent path breadcrumb and the file-text icon fallback (no icon), 1440 light/dark.")]
    public async Task Phase4_SidebarTrash_ChildPagePath()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await InvokeSeedAsync("seedSidebarTrashChildPage");
        await page.Locator(".tm-ns-trash__btn").First.EvaluateAsync("el => el.click()");
        var trash = page.Locator(".tm-nst").First;
        await trash.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var child = trash.Locator(".tm-nst-item", new LocatorLocatorOptions { HasText = "EB11 Orphaned Child Page" })
            .First;
        await child.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var path = await child.Locator(".tm-nst-item__path").InnerTextAsync();
        Assert.IsTrue(path.Contains("EB11 Active Reference"), $"child row shows the parent path (got '{path}')");
        Assert.AreEqual(1, await child.Locator(".tm-nst-item__icon svg").CountAsync(),
            "the no-icon row falls back to the lucide file-text glyph");

        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await ShotRegionAsync(page, "notion-sidebar",
                $"notion-sidebar-trash--child-path--desktop-{theme}", trash);
        }
    }

    [TestMethod]
    [Description("Bulk move/copy target picker and the bulk delete confirm in the sidebar, 1440 light.")]
    public async Task Phase4_SidebarBulkTargetAndDelete()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedBulkPagesAsync();

        // The bulk bar renders only with an active selection — tick the Source Root checkbox.
        var sourceRootCheckbox = page.Locator("[data-testid='notion-page-select-11111111-1111-1111-1111-111111111111']").First;
        await sourceRootCheckbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await sourceRootCheckbox.CheckAsync(new LocatorCheckOptions { Force = true });
        await page.Locator("[data-testid='notion-page-bulk-toolbar']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });

        // Move target picker.
        await page.Locator("[data-testid='notion-page-bulk-move']").First.EvaluateAsync("el => el.click()");
        var target = page.Locator("[data-testid='notion-page-bulk-target-search']").First;
        await target.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await target.FillAsync("Target Space");
        await page.WaitForTimeoutAsync(400);
        await ShotRegionAsync(page, "notion-sidebar",
            "notion-sidebar-bulk--target--desktop-light",
            page.Locator(".tm-modal-overlay").First);
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(300);
        await page.Locator("[data-testid='notion-page-bulk-clear']").First.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(300);

        // Bulk delete confirm: select the two delete candidates via their row checkboxes.
        foreach (var pageId in new[] { "55555555-5555-5555-5555-555555555555", "66666666-6666-6666-6666-666666666666" })
        {
            var checkbox = page.Locator($"[data-testid='notion-page-select-{pageId}']").First;
            await checkbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
            await checkbox.CheckAsync(new LocatorCheckOptions { Force = true });
        }
        await page.Locator("[data-testid='notion-page-bulk-toolbar']").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 10000
        });
        await page.Locator("[data-testid='notion-page-bulk-delete']").First.EvaluateAsync("el => el.click()");
        var confirm = page.Locator("[data-testid='notion-page-bulk-delete-confirm'] .tm-dialog").First;
        await confirm.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await ShotRegionAsync(page, "notion-sidebar",
            "notion-sidebar-bulk--delete--desktop-light", confirm);
        await page.Locator("[data-testid='notion-page-bulk-delete-confirm'] .tm-dialog-btn-cancel").First
            .EvaluateAsync("el => el.click()");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Database dialogs (DB-3): 1440 / 1024 touch / 390 touch / 320 touch, light and dark
    // ══════════════════════════════════════════════════════════════════════════

    [TestMethod]
    [Description("DB properties panel: anchored popover at 1440, bottom sheet at 390/320 touch. Light and dark.")]
    public async Task Phase4_DbPropertiesPopoverAndSheet()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var db = await NavigateToDbAsync(page);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await EvaluateClickInViewAsync(db.Locator("[data-testid='notion-db-properties-open']").First);
            // The popover lives in the browser top layer — Playwright's visibility probe is flaky
            // there (batch-A advanced suites already wait for Attached), so wait for Attached.
            var popover = page.Locator(".tm-overlay-panel:has(.tm-db__panel--fields)").First;
            await popover.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 8000 });
            await page.WaitForTimeoutAsync(300);
            await ShotRegionClipAsync(page, "notion-db-dialogs",
                $"notion-db-properties--desktop-{theme}", popover);
            await popover.Locator(".tm-db__panel-done").First.EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(250);
        }

        var touch = await OpenDbTouchEditorAsync(390, 844);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(touch, theme);
            var tdb = await NavigateToDbAsync(touch);
            await EvaluateClickInViewAsync(tdb.Locator("[data-testid='notion-db-properties-open']").First);
            var sheet = touch.Locator(".tm-overlay-panel-sheet:has(.tm-db__panel--fields)").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                $"notion-db-properties--mobile-{theme}", sheet);
            await sheet.Locator(".tm-overlay-panel-sheet__done").First.EvaluateAsync("el => el.click()");
            await touch.WaitForTimeoutAsync(250);
        }

        await touch.SetViewportSizeAsync(320, 700);
        var db320 = await NavigateToDbAsync(touch);
        await EvaluateClickInViewAsync(db320.Locator("[data-testid='notion-db-properties-open']").First);
        var sheet320 = touch.Locator(".tm-overlay-panel-sheet:has(.tm-db__panel--fields)").First;
        await sheet320.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await ShotRegionAsync(touch, "notion-db-dialogs", "notion-db-properties--mobile-320-light", sheet320);
    }

    [TestMethod]
    [Description("DB field editor: centred dialog at 1440, sheet at 1024 touch and 390 touch. Light and dark.")]
    public async Task Phase4_DbFieldEditor()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var db = await NavigateToDbAsync(page);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await db.Locator(".tm-dbt__th-inner").Nth(1).EvaluateAsync("el => el.click()");
            var overlay = page.Locator(".tm-modal-overlay:has(.tm-dbfe)").First;
            await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-db-dialogs",
                $"notion-db-field-editor--desktop-{theme}", overlay);
            await page.Locator(".tm-dbfe__btn-close").First.EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(250);
        }

        var touch = await OpenDbTouchEditorAsync(1024, 768);
        foreach (var (width, height, label) in new[] { (1024, 768, "tablet"), (390, 844, "mobile") })
        {
            await touch.SetViewportSizeAsync(width, height);
            await touch.WaitForTimeoutAsync(300);
            var tdb = await NavigateToDbAsync(touch);
            await tdb.Locator(".tm-dbt__th-inner").Nth(1).EvaluateAsync("el => el.click()");
            var sheet = touch.Locator(".tm-modal-overlay:has(.tm-dbfe)").First;
            await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                $"notion-db-field-editor--{label}-light", sheet);
            await touch.Locator(".tm-dbfe__btn-close").First.EvaluateAsync("el => el.click()");
            await touch.WaitForTimeoutAsync(250);
        }
    }

    [TestMethod]
    [Description("DB import/export: dialog at 1440; at 390 touch the sheet shows the mapping rows as cards. Light and dark.")]
    public async Task Phase4_DbImportExport()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var db = await NavigateToDbAsync(page);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await OpenImportExportAsync(db);
            var overlay = page.Locator(".tm-modal-overlay:has(.tm-dbie)").First;
            await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-db-dialogs",
                $"notion-db-import-export--desktop-{theme}", overlay);
            await page.Locator(".tm-modal-overlay:has(.tm-dbie) .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(250);
        }

        var touch = await OpenDbTouchEditorAsync(390, 844);
        var tdb = await NavigateToDbAsync(touch);
        await OpenImportExportAsync(tdb);
        var sheet = touch.Locator(".tm-modal-overlay:has(.tm-dbie)").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await ShotRegionAsync(touch, "notion-db-dialogs",
            "notion-db-import-export--mobile-light", sheet);

        // Upload a CSV and capture the mapping cards (rows stack as cards under 600px).
        var csvPath = Path.Combine(Path.GetTempPath(), $"tm-phase4-import-{Guid.NewGuid():n}.csv");
        await File.WriteAllTextAsync(csvPath, "Name,Status,Priority\nAlpha,Active,1\nBeta,Done,2\nGamma,Active,3\n");
        try
        {
            await touch.Locator(".tm-dbie__file-input").SetInputFilesAsync(csvPath);
            var mapRow = touch.Locator(".tm-dbie__map-table tbody tr").First;
            await mapRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                "notion-db-import-mapping-cards--mobile-light",
                touch.Locator(".tm-modal-overlay:has(.tm-dbie)").First);
        }
        finally
        {
            File.Delete(csvPath);
        }

        foreach (var theme in new[] { "dark" })
        {
            // Close the mapping capture's dialog first — the toolbar sits behind it.
            await touch.Locator(".tm-modal-overlay:has(.tm-dbie) .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await touch.WaitForTimeoutAsync(300);
            await SetThemeAsync(touch, theme);
            await OpenImportExportAsync(await NavigateToDbAsync(touch));
            var darkSheet = touch.Locator(".tm-modal-overlay:has(.tm-dbie)").First;
            await darkSheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                $"notion-db-import-export--mobile-{theme}", darkSheet);
        }
    }

    [TestMethod]
    [Description("DB record detail: centred dialog at 1440, AutoFullscreen sheet at 390/320 touch. Light and dark.")]
    public async Task Phase4_DbRecordDetail()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var db = await NavigateToDbAsync(page);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await OpenFirstRecordDetailAsync(page, db);
            var overlay = page.Locator(".tm-modal-overlay:has(.tm-dbrd)").First;
            await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-db-dialogs",
                $"notion-db-record-detail--desktop-{theme}", overlay);
            await page.Locator(".tm-modal-overlay:has(.tm-dbrd) .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(250);
        }

        var touch = await OpenDbTouchEditorAsync(390, 844);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(touch, theme);
            var tdb = await NavigateToDbAsync(touch);
            await OpenFirstRecordDetailAsync(touch, tdb);
            var overlay = touch.Locator(".tm-modal-overlay:has(.tm-dbrd)").First;
            await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                $"notion-db-record-detail--mobile-{theme}", overlay);
            await touch.Locator(".tm-modal-overlay:has(.tm-dbrd) .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await touch.WaitForTimeoutAsync(250);
        }

        await touch.SetViewportSizeAsync(320, 700);
        var db320 = await NavigateToDbAsync(touch);
        await OpenFirstRecordDetailAsync(touch, db320);
        var overlay320 = touch.Locator(".tm-modal-overlay:has(.tm-dbrd)").First;
        await overlay320.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await ShotRegionAsync(touch, "notion-db-dialogs", "notion-db-record-detail--mobile-320-light", overlay320);
    }

    [TestMethod]
    [Description("DB template menu (popover/sheet) and the template editor (dialog/fullscreen). Light and dark.")]
    public async Task Phase4_DbTemplateMenuAndEditor()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var db = await NavigateToDbAsync(page);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await EvaluateClickInViewAsync(db.Locator(".tm-db__new-chevron").First);
            var menu = page.Locator(".tm-overlay-panel:has(.tm-dbtm)").First;
            await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 8000 });
            await page.WaitForTimeoutAsync(300);
            await ShotRegionClipAsync(page, "notion-db-dialogs",
                $"notion-db-template-menu--desktop-{theme}", menu);
            await page.Locator(".tm-dbtm__new-btn").First.EvaluateAsync("el => el.click()");
            var editor = page.Locator(".tm-modal-overlay:has(.tm-dbte)").First;
            await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(page, "notion-db-dialogs",
                $"notion-db-template-editor--desktop-{theme}", editor);
            await page.Locator(".tm-modal-overlay:has(.tm-dbte) .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(300);
        }

        var touch = await OpenDbTouchEditorAsync(390, 844);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(touch, theme);
            var tdb = await NavigateToDbAsync(touch);
            await EvaluateClickInViewAsync(tdb.Locator(".tm-db__new-chevron").First);
            var menu = touch.Locator(".tm-overlay-panel-sheet:has(.tm-dbtm)").First;
            await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                $"notion-db-template-menu--mobile-{theme}", menu);
            await touch.Locator(".tm-dbtm__new-btn").First.EvaluateAsync("el => el.click()");
            var editor = touch.Locator(".tm-modal-overlay:has(.tm-dbte)").First;
            await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
            await ShotRegionAsync(touch, "notion-db-dialogs",
                $"notion-db-template-editor--mobile-{theme}", editor);
            await touch.Locator(".tm-modal-overlay:has(.tm-dbte) .tm-modal-close").First
                .EvaluateAsync("el => el.click()");
            await touch.WaitForTimeoutAsync(300);
        }
    }

    // ── Batch B helpers ─────────────────────────────────────────────────────────

    private async Task SelectTreePageAsync(IPage page, string title)
    {
        var row = page.Locator(".tm-npt-title").Filter(new LocatorFilterOptions { HasText = title }).First;
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await row.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task OpenImportExportAsync(ILocator db)
        => await EvaluateClickInViewAsync(db.Locator(".tm-db__tool-btn[title*='Import']").First);

    /// <summary>
    /// Scrolls a trigger into view RIGHT BEFORE clicking it: the table re-renders after the
    /// skeleton disappears and resets the earlier scroll, and an off-screen anchor parks the
    /// anchored popover invisible (overlay.js anchor-visibility rule).
    /// </summary>
    private static async Task EvaluateClickInViewAsync(ILocator trigger)
    {
        await trigger.EvaluateAsync("el => el.scrollIntoView({ block: 'center', inline: 'nearest' })");
        await trigger.EvaluateAsync("el => el.click()");
    }

    private static async Task<ILocator> NavigateToDbAsync(IPage page)
    {
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        // Center (not just minimum-scroll): an anchor sitting a few px above the viewport parks
        // the anchored popover invisible (overlay.js anchor-visibility rule).
        await db.EvaluateAsync("el => el.scrollIntoView({ block: 'center' })");
        await page.WaitForSelectorAsync(".tm-db__skeleton",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(300);
        return db;
    }

    /// <summary>Touch context for the DB mobile captures; the caller owns disposal.</summary>
    private async Task<IPage> OpenDbTouchEditorAsync(int width, int height)
    {
        using var http = DemoApiTestHttp.CreateClient();
        using var notionResponse = await http.PostAsync("/api/notion/reset", null);
        notionResponse.EnsureSuccessStatusCode();
        using var databaseResponse = await http.PostAsync("/api/notion/databases/e2e/seed/default", null);
        databaseResponse.EnsureSuccessStatusCode();

        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = true,
            IsMobile = true,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/notion-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-editor", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    private static async Task OpenFirstRecordDetailAsync(IPage page, ILocator db)
    {
        var firstRow = db.Locator(".tm-dbt__row").First;
        await firstRow.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        var spacer = firstRow.Locator(".tm-dbt__cell--spacer");
        await spacer.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 8000 });
        await spacer.EvaluateAsync("el => el.click()");
        await page.WaitForTimeoutAsync(800);
    }

    private async Task OpenFirstBlockCommentAsync(IPage page)
    {
        var firstBlock = page.Locator("[data-notion-block]").First;
        await firstBlock.ScrollIntoViewIfNeededAsync();
        await firstBlock.HoverAsync();
        await firstBlock.Locator(".tm-notion-handle__btn").Last.ClickAsync();
        await page.Locator(".tm-notion-ctx__item:has-text('Comment')").First.ClickAsync();
    }

    private async Task OpenTextCommentAsync(IPage page)
    {
        var firstEditable = page.Locator(".tm-notion-paragraph.tm-notion-editable").First;
        await firstEditable.ScrollIntoViewIfNeededAsync();
        await firstEditable.ClickAsync();
        await firstEditable.EvaluateAsync("el => { el.focus(); document.execCommand('selectAll', false, null); }");
        var toolbar = page.Locator(".tm-notion-inline-toolbar").First;
        await toolbar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await toolbar.Locator("button[title*='Comment'], button[title*='Komentář']").First.ClickAsync();
    }
}
