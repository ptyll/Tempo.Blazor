using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-7 screenshot coverage for share, restrictions, history, media, AI and the Tempo edit
/// modals (plan c8d4bcd2, N8 + N9 + N11 + N12). One test per surface. Screenshots land in
/// tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-7/.
/// </summary>
[TestClass]
public sealed class NotionPhase7SurfacesE2ETests : WasmTestBase
{
    private static readonly (int Width, int Height, bool Touch, string Band)[] Bands =
    [
        (1440, 900, false, "1440"),
        (1024, 768, true, "1024"),
        (390, 844, true, "390"),
        (320, 700, true, "320"),
    ];

    [TestMethod]
    [Description("Phase 7: an active share stays locked, copy announces Copied, and revoke asks first")]
    public async Task Share_ActiveLink_IsLockedAndRevokeConfirms()
    {
        var page = await OpenAsync(1440, 900, false, "seedPhase7Surfaces");
        await OpenSettingsItemAsync(page, "Public share");
        var dialog = page.GetByTestId("notion-share-dialog");
        await dialog.WaitForAsync(Visible());

        await Assertions.Expect(dialog.GetByTestId("notion-share-locked-hint")).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByTestId("notion-share-allow-comments").Locator("input")).ToBeDisabledAsync();
        var describedBy = await dialog.GetByTestId("notion-share-allow-comments").Locator("input").GetAttributeAsync("aria-describedby");
        Assert.AreEqual("tm-npsd-locked-hint", describedBy);

        await dialog.Locator(".tm-copy-button").ClickAsync();
        await Assertions.Expect(dialog.GetByTestId("notion-share-copied")).ToContainTextAsync("Copied");
        await ShotAsync(page, "notion-share", "share-active-locked-light", dialog);

        await dialog.GetByTestId("notion-share-revoke").ClickAsync();
        var confirm = page.GetByTestId("notion-share-revoke-confirm");
        await confirm.WaitForAsync(Visible());
        await Assertions.Expect(confirm).ToContainTextAsync("Revoke this link?");
        await ShotAsync(page, "notion-share", "share-revoke-confirm-light", confirm);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 7: restrictions list entries, and a child page shows the inheritance banner")]
    public async Task Restrictions_ShowsEntriesAndInheritedBanner()
    {
        var page = await OpenAsync(1440, 900, false, "seedRestrictionsPage");
        await OpenSettingsItemAsync(page, "Page restrictions");
        var dialog = page.GetByTestId("notion-restrictions-dialog");
        await dialog.WaitForAsync(Visible());
        await Assertions.Expect(dialog.GetByTestId("notion-restrictions-entry").First).ToBeVisibleAsync();
        Assert.IsTrue(await dialog.GetByTestId("notion-restrictions-entry").CountAsync() >= 2);
        await ShotAsync(page, "notion-restrictions", "restrictions-entries-1440-light", dialog);
        await page.Keyboard.PressAsync("Escape");

        var child = page.Locator(".tm-npt-title", new PageLocatorOptions { HasTextString = "CF20 Child Inherits Restrictions" }).First;
        if (await child.CountAsync() == 0)
        {
            await page.Locator(".tm-npt-item", new PageLocatorOptions { HasTextString = "CF20 Restricted Workspace" })
                .Locator(".tm-npt-toggle").First.ClickAsync();
            await child.WaitForAsync(Visible());
        }
        await child.ClickAsync();
        await page.Locator(".tm-notion-page[data-page-id='22222222-2222-2222-2222-222222222222']")
            .WaitForAsync(Visible());
        await OpenSettingsItemAsync(page, "Page restrictions");
        await dialog.WaitForAsync(Visible());
        await Assertions.Expect(page.GetByTestId("notion-restrictions-inherited")).ToBeVisibleAsync();
        await ShotAsync(page, "notion-restrictions", "restrictions-inherited-light", dialog);

        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(dialog.Locator(".tm-nprd[data-layout='mobile']")).ToBeVisibleAsync();
        await ShotAsync(page, "notion-restrictions", "restrictions-cards-390-light", dialog);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 7: history selects, compares, confirms restore, and drills in on a phone")]
    public async Task History_SelectCompareRestoreAndMobilePreview()
    {
        var page = await OpenAsync(1440, 900, false, "seedPhase7Surfaces");
        await OpenSettingsItemAsync(page, "Page history");
        var host = page.Locator(".tm-nph-host");
        await host.WaitForAsync(Visible());
        await page.Locator(".tm-nph__version-item").First.WaitForAsync(Visible());
        await page.Locator(".tm-nph__version-item").Nth(1).ClickAsync();
        await Assertions.Expect(page.Locator(".tm-nph__version-item--selected")).ToHaveCountAsync(1);
        await ShotAsync(page, "notion-history", "history-selected-1440-light", host);

        await page.GetByTestId("notion-history-pick").Nth(0).CheckAsync();
        await page.GetByTestId("notion-history-pick").Nth(1).CheckAsync();
        await page.GetByTestId("notion-history-compare-selected").ClickAsync();
        await Assertions.Expect(page.GetByTestId("notion-history-change-count")).ToBeVisibleAsync();
        await ShotAsync(page, "notion-history", "history-compare-1440-light", host);

        await page.GetByTestId("notion-history-restore").ClickAsync();
        var confirm = page.GetByTestId("notion-history-restore-confirm");
        await confirm.WaitForAsync(Visible());
        await Assertions.Expect(confirm).ToContainTextAsync("Restore this version?");
        await ShotAsync(page, "notion-history", "history-restore-confirm-light", confirm);
        await page.Keyboard.PressAsync("Escape");
        await page.Context.CloseAsync();

        var phone = await OpenAsync(320, 700, true, "seedPhase7Surfaces");
        await OpenPageSettingsFromMoreAsync(phone);
        await phone.Locator(".tm-npsm__item", new PageLocatorOptions { HasTextString = "Page history" }).First
            .ClickAsync(new LocatorClickOptions { Force = true });
        await phone.Locator(".tm-nph").WaitForAsync(Visible());
        var footerBottom = await phone.Locator(".tm-nph__footer").EvaluateAsync<double>("el => el.getBoundingClientRect().bottom");
        var viewport = phone.ViewportSize!.Height;
        Assert.IsTrue(footerBottom <= viewport + 1, $"History footer sits below the viewport ({footerBottom} > {viewport}).");
        await phone.Locator(".tm-nph__version-item").First.ClickAsync();
        await Assertions.Expect(phone.Locator(".tm-nph[data-mobile-preview='true']")).ToBeVisibleAsync();
        await Assertions.Expect(phone.GetByTestId("notion-history-back")).ToBeVisibleAsync();
        await ShotAsync(phone, "notion-history", "history-mobile-preview-320-light", phone.Locator(".tm-nph-host"));
        await phone.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 7: the media chooser is a coarse target, in light and dark")]
    public async Task Media_CoarseChooseFile_IsATouchTarget()
    {
        var page = await OpenAsync(390, 844, true, "seedPhase7Surfaces");
        await page.Locator(".tm-notion-media-upload-zone--image").First.ClickAsync(new LocatorClickOptions { Force = true });
        var dialog = page.Locator(".tm-media-dialog");
        await dialog.WaitForAsync(Visible());
        var height = await dialog.Locator(".tm-media-dialog__choose-file").EvaluateAsync<double>("el => el.getBoundingClientRect().height");
        Assert.IsTrue(height >= 44, $"Choose file is {height}px, expected at least 44.");
        await ShotAsync(page, "notion-media", "media-coarse-390-light", dialog);
        await SetThemeAsync(page, "dark");
        await ShotAsync(page, "notion-media", "media-coarse-390-dark", dialog);
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 7: the AI sheet fills the phone viewport")]
    public async Task AiSheet_FillsThePhoneViewport()
    {
        foreach (var width in new[] { 390, 320 })
        {
            var page = await OpenAsync(width, 844, true, "seedPhase7Surfaces");
            await TypeSlashAsync(page);
            await page.Locator("[data-testid='notion-ai-slash-item']").First.ClickAsync(new LocatorClickOptions { Force = true });
            var menu = page.Locator("[data-testid='notion-ai-menu']");
            await menu.WaitForAsync(Visible());
            var panelWidth = await menu.EvaluateAsync<double>("el => el.getBoundingClientRect().width");
            var sheetWidth = await page.Locator(".tm-overlay-panel-sheet").First
                .EvaluateAsync<double>("el => el.getBoundingClientRect().width");
            Assert.AreEqual(page.ViewportSize!.Width, sheetWidth, 2, $"AI sheet at {width} is not the viewport.");
            Assert.AreEqual(sheetWidth, panelWidth, 2, $"AI panel at {width} is narrower than its sheet.");
            await ShotAsync(page, "notion-ai", $"ai-sheet-{width}-light", page.Locator(".tm-overlay-panel-sheet").First);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("Phase 7: the diagram edit modal is XLarge from 1280, compact below 1024, and confirms a dirty discard")]
    public async Task DiagramEditModal_BandsAndDirtyDiscard()
    {
        var desktop = await OpenDiagramModalAsync(1440, 900, false);
        await desktop.Locator("[data-testid='notion-diagram-edit-modal'][data-editor-layout='desktop']").WaitForAsync(Visible());
        await Assertions.Expect(desktop.Locator(".tm-modal.tm-modal--xl")).ToBeVisibleAsync();
        await Assertions.Expect(desktop.GetByTestId("notion-tempo-modal-maximize")).ToBeVisibleAsync();
        await ShotAsync(desktop, "notion-edit-modals", "diagram-1440-light", desktop.GetByTestId("notion-diagram-edit-modal"));

        var toolboxToggle = desktop.GetByTestId("diagram-editor-toggle-toolbox");
        if (await toolboxToggle.CountAsync() > 0 && await toolboxToggle.GetAttributeAsync("aria-pressed") != "true")
            await toolboxToggle.ClickAsync();
        var toolbox = desktop.Locator("[data-testid='diagram-toolbox']");
        var stencil = toolbox.First.Locator("[data-stencil-id]").First;
        await stencil.WaitForAsync(Visible());
        await stencil.ClickAsync();
        await desktop.GetByTestId("notion-tempo-modal-discard").ClickAsync();
        var confirm = desktop.GetByTestId("notion-tempo-modal-discard-confirm");
        await confirm.WaitForAsync(Visible());
        await Assertions.Expect(confirm).ToContainTextAsync("Discard changes?");
        await ShotAsync(desktop, "notion-edit-modals", "diagram-dirty-discard-light", confirm);
        await desktop.Context.CloseAsync();

        var tablet = await OpenDiagramModalAsync(1024, 768, true);
        await tablet.Locator("[data-testid='notion-diagram-edit-modal'][data-editor-layout='desktop']").WaitForAsync(Visible());
        Assert.AreEqual(0, await tablet.GetByTestId("notion-tempo-modal-maximize").CountAsync());
        await ShotAsync(tablet, "notion-edit-modals", "diagram-1024-light", tablet.GetByTestId("notion-diagram-edit-modal"));
        await tablet.Context.CloseAsync();

        var phone = await OpenDiagramModalAsync(390, 844, true);
        await phone.Locator("[data-testid='notion-diagram-edit-modal'][data-editor-layout='mobile']").WaitForAsync(Visible());
        Assert.AreEqual(0, await phone.GetByTestId("notion-tempo-modal-maximize").CountAsync());
        await ShotAsync(phone, "notion-edit-modals", "diagram-390-light", phone.GetByTestId("notion-diagram-edit-modal"));
        await phone.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Phase 7: the template button config panel opens")]
    public async Task TemplateButton_ConfigOpens()
    {
        var page = await OpenAsync(1440, 900, false, "seedPhase7Surfaces");
        await page.Locator(".tm-template-btn__config-toggle").First.ClickAsync();
        var config = page.Locator(".tm-template-btn__config");
        await config.WaitForAsync(Visible());
        await Assertions.Expect(config.Locator(".tm-template-btn__label-input")).ToHaveValueAsync("Insert status update");
        await ShotAsync(page, "notion-template", "template-config-light", config);
        await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenDiagramModalAsync(int width, int height, bool touch)
    {
        var page = await OpenAsync(width, height, touch, "seedPhase7Surfaces");
        await page.Locator(".tm-notion-media-upload-zone--diagram").First.ClickAsync(new LocatorClickOptions { Force = true });
        await page.GetByTestId("notion-diagram-edit-modal").WaitForAsync(Visible());
        return page;
    }

    private static async Task OpenPageSettingsFromMoreAsync(IPage page)
    {
        await page.Locator(".tm-notion-editor[data-editor-layout]").First.WaitForAsync(Visible());
        var entry = page.Locator("[data-testid='notion-page-settings']:visible, [data-menu-testid='notion-page-settings']:visible");
        if (await entry.CountAsync() == 0)
            await page.Locator(".tm-mobile-action-bar__more, .tm-notion-topbar .tm-toolbar-more").First.ClickAsync();
        await entry.First.ClickAsync();
        await page.Locator(".tm-npsm:visible, .tm-overlay-panel-sheet .tm-notion-floating-menu__body").First.WaitForAsync(Visible());
    }

    private static async Task OpenSettingsItemAsync(IPage page, string label)
    {
        await page.Locator(".tm-notion-editor[data-editor-layout]").First.WaitForAsync(Visible());
        var trigger = page.Locator(".tm-npsm-trigger").First;
        if (page.ViewportSize!.Width < 1024)
        {
            var more = page.Locator(".tm-toolbar-more, .tm-mobile-action-bar__more").First;
            await more.ClickAsync();
            trigger = page.Locator(".tm-npsm-trigger:visible").First;
        }
        else
        {
            await page.Locator(".tm-notion-page").First.HoverAsync();
        }
        await trigger.WaitForAsync(Visible());
        await trigger.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator(".tm-npsm:visible, .tm-overlay-panel-sheet .tm-notion-floating-menu__body").First.WaitForAsync(Visible());
        await page.Locator(".tm-npsm__item", new PageLocatorOptions { HasTextString = label }).First
            .ClickAsync(new LocatorClickOptions { Force = true });
    }

    private static LocatorWaitForOptions Visible()
        => new() { State = WaitForSelectorState.Visible, Timeout = 15000 };

    private async Task<IPage> OpenAsync(int width, int height, bool touch, string seed)
    {
        using var http = DemoApiTestHttp.CreateClient();
        using var seeded = await http.PostAsync($"https://localhost:5100/api/notion/e2e/seed/{seed}", null);
        seeded.EnsureSuccessStatusCode();

        var context = await CreateContextAsync(hasTouch: touch);
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync($"{BaseUrl}/notion-editor", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-notion-page", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000,
        });
        return page;
    }

    private static async Task TypeSlashAsync(IPage page)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(Visible());
        await para.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("/");
        await page.WaitForSelectorAsync(".tm-notion-slash", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000,
        });
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
        await page.WaitForTimeoutAsync(150);
    }

    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-7");
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

    private static async Task ShotAsync(IPage page, string area, string name, ILocator? region = null)
    {
        await page.WaitForTimeoutAsync(200);
        var path = Path.Combine(ShotDir(area), name + ".png");
        if (region is not null && await region.CountAsync() > 0 && await region.IsVisibleAsync())
            await region.ScreenshotAsync(new LocatorScreenshotOptions { Path = path, Timeout = 8000 });
        else
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = path });
    }
}
