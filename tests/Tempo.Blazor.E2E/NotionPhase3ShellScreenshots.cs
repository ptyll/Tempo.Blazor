using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase-3 evidence for the responsive editor shell, overlay sheets and the public page
/// (plan c8d4bcd2, N0/N5/N4). Light and dark, desktop 1440 / tablet 1024 / mobile 390.
/// Lands in tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-3/. Evidence only.
/// </summary>
[TestClass]
[TestCategory("NotionUxBaseline")]
public sealed class NotionPhase3ShellScreenshots : NotionE2ETestBase
{
    private static string ShotDir(string area)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E",
            "__screenshots__", area, "phase-3");
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

    private static async Task ShotAsync(IPage page, string area, string name)
    {
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir(area), name + ".png"),
            FullPage = false,
        });
    }

    private static async Task DismissAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(150);
    }

    // The mobile drawer is a NON-modal sheet: it owns Escape only while focus is inside. After a
    // Force click the focus sits on the toggle, so Escape may leave it open — close it through the
    // toggle when it is still visible before the next capture.
    private static async Task CloseDrawerAsync(IPage page)
    {
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(200);
        if (await page.Locator(".tm-notion-sidebar--visible").CountAsync() > 0)
        {
            await page.Locator(".tm-notion-sidebar-toggle").First.EvaluateAsync("el => el.click()");
            await page.WaitForTimeoutAsync(300);
        }
    }

    [TestMethod]
    [Description("Editor shell at 1440, 1024 and 390 (drawer closed and open), light and dark.")]
    public async Task Phase3_EditorShell()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            await ShotAsync(page, "notion-editor", $"shell-desktop-1440-{theme}");

            await page.SetViewportSizeAsync(1024, 768);
            await page.WaitForTimeoutAsync(300);
            await ShotAsync(page, "notion-editor", $"shell-tablet-1024-{theme}");

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            // The editor sits below the demo page header — bring it into view so the mobile
            // drawer/sheet captures actually show it.
            await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
            await page.WaitForTimeoutAsync(200);
            await ShotAsync(page, "notion-editor", $"shell-mobile-390-drawer-closed-{theme}");

            await page.Locator(".tm-notion-sidebar-toggle").First.EvaluateAsync("el => el.click()");
            await page.Locator(".tm-notion-sidebar--visible").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 5000,
            });
            await page.WaitForTimeoutAsync(400);
            await ShotAsync(page, "notion-editor", $"shell-mobile-390-drawer-open-{theme}");
            await CloseDrawerAsync(page);
        }
    }

    [TestMethod]
    [Description("Escape on the page-header emoji picker search closes it and returns focus to the icon trigger.")]
    public async Task Phase3_PageHeaderEmojiPicker_Escape_ClosesAndRestoresFocus()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var icon = page.Locator(".tm-notion-header-icon").First;
        await icon.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await icon.ClickAsync();

        var search = page.Locator(".tm-notion-emoji-picker__search");
        await search.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        await search.ClickAsync();
        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.Locator(".tm-notion-emoji-picker")).ToHaveCountAsync(0);
        var focused = await page.EvaluateAsync<bool>(
            "() => document.activeElement?.classList.contains('tm-notion-header-icon') === true");
        Assert.IsTrue(focused, "Escape returns focus to the icon trigger");
    }

    [TestMethod]
    [Description("Slash, emoji, color, token and status overlays on desktop and as a mobile sheet.")]
    public async Task Phase3_Overlays()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });

        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);
            await page.SetViewportSizeAsync(1440, 900);
            await CaptureOverlaysAsync(page, theme, "desktop");
            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await CaptureOverlaysAsync(page, theme, "mobile");
        }
    }

    private static async Task CaptureOverlaysAsync(IPage page, string theme, string band)
    {
        var para = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await para.ScrollIntoViewIfNeededAsync();
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync("/");
        var slash = page.Locator(".tm-notion-slash, .tm-overlay-sheet").First;
        try
        {
            await slash.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        }
        catch (TimeoutException)
        {
            await para.ClickAsync();
            await page.Keyboard.TypeAsync("/");
            await slash.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        }
        await ShotAsync(page, "notion-menus", $"slash-{band}-{theme}");
        await DismissAsync(page);

        var icon = page.Locator(".tm-notion-header-icon").First;
        if (await icon.CountAsync() > 0)
        {
            await icon.ClickAsync();
            await page.WaitForTimeoutAsync(300);
        }
        // The consumer class rides the popover root only — the core overlay never forwards Class
        // onto the sheet content, so on the mobile band the core sheet class is the surface hook.
        await ShotIfAsync(page, ".tm-notion-emoji-picker, .tm-notion-emoji-picker__panel, .tm-overlay-panel-sheet", "notion-menus", $"emoji-{band}-{theme}");
        await DismissAsync(page);
        if (await page.Locator(".tm-notion-emoji-picker").CountAsync() > 0)
        {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(200);
        }

        await SelectParagraphAsync(page, para);
        var colorBtn = page.Locator("button[title='Color'], .tm-notion-inline-toolbar__btn--color").First;
        try
        {
            await colorBtn.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
            await colorBtn.ClickAsync();
            await page.Locator(".tm-notion-inline-toolbar__color-panel, .tm-notion-color-picker, .tm-overlay-panel-sheet")
                .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        }
        catch (TimeoutException)
        {
            // Evidence only — a missing menu must not fail the capture of the others.
        }
        await ShotIfAsync(page, ".tm-notion-inline-toolbar__color-panel, .tm-notion-color-picker, .tm-overlay-panel-sheet", "notion-menus", $"color-{band}-{theme}");
        await DismissAsync(page);

        await TypeTriggerAsync(page, para, "{{");
        try
        {
            await page.Locator(".tm-notion-token-dropdown, .tm-overlay-panel-sheet").First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        }
        catch (TimeoutException) { }
        await ShotIfAsync(page, ".tm-notion-token-dropdown, .tm-overlay-panel-sheet", "notion-menus", $"token-{band}-{theme}");
        await DismissAsync(page);

        await OpenStatusAsync(page, para);
        await ShotIfAsync(page, ".tm-notion-status-picker, .tm-overlay-panel-sheet", "notion-menus", $"status-{band}-{theme}");
        await DismissAsync(page);
    }

    private static async Task SelectParagraphAsync(IPage page, ILocator para)
    {
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync("Color sample");
        await page.Keyboard.PressAsync("Shift+Home");
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task OpenStatusAsync(IPage page, ILocator para)
    {
        await TypeTriggerAsync(page, para, "/");
        var slash = page.Locator(".tm-notion-slash").First;
        try
        {
            await slash.WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
            await slash.Locator(".tm-notion-slash__input").First.FillAsync("status");
            var item = slash.Locator(".tm-notion-slash__item", new LocatorLocatorOptions { HasTextString = "Status" }).First;
            await item.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
            await item.EvaluateAsync("el => el.click()");
            await page.Locator(".tm-notion-status-picker, .tm-overlay-sheet").First
                .WaitForAsync(new LocatorWaitForOptions { Timeout = 8000 });
        }
        catch (TimeoutException) { }
    }

    private static async Task TypeTriggerAsync(IPage page, ILocator para, string text)
    {
        await para.ClickAsync();
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.TypeAsync(text);
        await page.WaitForTimeoutAsync(500);
    }

    private static async Task ShotIfAsync(IPage page, string selector, string area, string name)
    {
        var surface = page.Locator(selector).First;
        if (await surface.CountAsync() == 0)
            return;
        await surface.ScrollIntoViewIfNeededAsync();
        await ShotAsync(page, area, name);
    }

    [TestMethod]
    [Description("Public page at desktop 1440 and mobile 390, light and dark.")]
    public async Task Phase3_PublicPage()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedPublicSharePageAsync();
        await page.Locator(".tm-npsm-trigger").First.ClickAsync();
        await page.Locator(".tm-npsm").GetByTestId("notion-share-open").ClickAsync();
        var dialog = page.GetByTestId("notion-share-dialog");
        await dialog.GetByTestId("notion-share-create").ClickAsync();
        var publicUrl = await dialog.GetByTestId("notion-share-url").InputValueAsync();
        await DismissAsync(page);

        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var publicPage = await context.NewPageAsync();
        await publicPage.SetViewportSizeAsync(1440, 900);
        await publicPage.GotoAsync(publicUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(publicPage);
        await publicPage.GetByTestId("notion-public-page").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(publicPage, theme);
            await publicPage.SetViewportSizeAsync(1440, 900);
            await ShotAsync(publicPage, "notion-page", $"public-desktop-1440-{theme}");
            await publicPage.SetViewportSizeAsync(390, 844);
            await publicPage.WaitForTimeoutAsync(300);
            await ShotAsync(publicPage, "notion-page", $"public-mobile-390-{theme}");
        }

        // Review fixes: Minimal chrome renders no empty shell header band, and the page content
        // is never clipped at the narrow bands — the last block stays reachable.
        Assert.AreEqual(0, await publicPage.Locator(".tm-editor-shell__header").CountAsync(),
            "Chrome=Minimal must not render the empty shell header band.");
        foreach (var (width, height) in new[] { (Width: 390, Height: 844), (Width: 320, Height: 700) })
        {
            await publicPage.SetViewportSizeAsync(width, height);
            await publicPage.WaitForTimeoutAsync(300);
            var lastBlock = publicPage.Locator(".tm-notion-page [data-block-id]").Last;
            await lastBlock.ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(lastBlock).ToBeInViewportAsync();
        }
    }

    [TestMethod]
    [Description("Mobile topbar (390/320), the More sheet at 390, and the tablet band at 768 — light and dark.")]
    public async Task Phase3_ResponsiveShellBands()
    {
        var page = await OpenNotionEditorAsync(390, 844);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);

            // The topbar alone: title hidden (page H1 carries it), toggle + tools readable.
            await page.Locator(".tm-notion-topbar").First.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 30000,
            });
            await page.Locator(".tm-notion-topbar").First.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(ShotDir("notion-editor"), $"shell-mobile-390-bar-{theme}.png"),
            });

            // The More sheet: at 390 the overflowed tools live in the bottom sheet.
            await page.Locator(".tm-notion-topbar .tm-toolbar-more").First.ClickAsync(new LocatorClickOptions { Force = true });
            await page.WaitForTimeoutAsync(400);
            await ShotAsync(page, "notion-editor", $"shell-mobile-390-more-sheet-{theme}");
            await DismissAsync(page);

            await page.SetViewportSizeAsync(320, 700);
            await page.WaitForTimeoutAsync(300);
            await ShotAsync(page, "notion-editor", $"shell-mobile-320-{theme}");

            await page.SetViewportSizeAsync(768, 900);
            await page.WaitForTimeoutAsync(300);
            await ShotAsync(page, "notion-editor", $"shell-tablet-768-{theme}");

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
        }
    }

    [TestMethod]
    [Description("My Tasks hosted: side sheet on the tablet band (1024), full-height sheet on mobile (390) — light and dark.")]
    public async Task Phase3_MyTasksHosted()
    {
        var page = await OpenNotionEditorAsync(1024, 768);
        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(page, theme);

            await ClickNotionCommandAsync(page, "notion-tasks");
            var panel = page.Locator(".tm-notion-shell-panel--overlay, .tm-notion-tasks-panel").First;
            await panel.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8000,
            });
            await ShotAsync(page, "notion-panels", $"my-tasks-tablet-1024-{theme}");
            await DismissAsync(page);
            await page.WaitForTimeoutAsync(300);

            await page.SetViewportSizeAsync(390, 844);
            await page.WaitForTimeoutAsync(300);
            await ClickNotionCommandAsync(page, "notion-tasks");
            await panel.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8000,
            });
            await ShotAsync(page, "notion-panels", $"my-tasks-mobile-390-{theme}");
            await DismissAsync(page);
            await page.WaitForTimeoutAsync(300);

            await page.SetViewportSizeAsync(1024, 768);
            await page.WaitForTimeoutAsync(300);
        }
    }

    [TestMethod]
    [Description("Public page at the tablet band (768) and 320 — light and dark.")]
    public async Task Phase3_PublicPage_NarrowBands()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedPublicSharePageAsync();
        await page.Locator(".tm-npsm-trigger").First.ClickAsync();
        await page.Locator(".tm-npsm").GetByTestId("notion-share-open").ClickAsync();
        var dialog = page.GetByTestId("notion-share-dialog");
        await dialog.GetByTestId("notion-share-create").ClickAsync();
        var publicUrl = await dialog.GetByTestId("notion-share-url").InputValueAsync();
        await DismissAsync(page);

        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("window.localStorage.setItem('tm-demo-culture', 'en');");
        var publicPage = await context.NewPageAsync();
        await publicPage.SetViewportSizeAsync(768, 900);
        await publicPage.GotoAsync(publicUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(publicPage);
        await publicPage.GetByTestId("notion-public-page").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        foreach (var theme in new[] { "light", "dark" })
        {
            await SetThemeAsync(publicPage, theme);
            await publicPage.SetViewportSizeAsync(768, 900);
            await publicPage.WaitForTimeoutAsync(300);
            await ShotAsync(publicPage, "notion-page", $"public-tablet-768-{theme}");
            await publicPage.SetViewportSizeAsync(320, 700);
            await publicPage.WaitForTimeoutAsync(300);
            // Bring the page H1 into view so the narrow-band capture shows the wrapped title.
            await publicPage.Locator(".tm-notion-header-title").First.ScrollIntoViewIfNeededAsync();
            await publicPage.WaitForTimeoutAsync(200);
            await ShotAsync(publicPage, "notion-page", $"public-320-{theme}");
        }
    }
}
