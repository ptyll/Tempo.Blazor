using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Minimal browser repro for the panel rail toggles of the CORE <c>TmEditorShell</c>, on the shell's own
/// demo page (<c>/editor-shell</c>) with no email editor involved. The shell's tablet layout renders the
/// left panel as a rail; expanding it must dock the panel and rail the other one.
/// <para>
/// bUnit covers the same interaction (TmEmailTemplateEditorShellTests and the shell's own tests) and the
/// handler chain works there; this spec answers whether the REAL browser does the same, so a failure
/// localises the defect to the core primitive instead of the editor.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EditorShellRailE2ETests : WasmTestBase
{
    private async Task<IPage> OpenShellAsync(int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/editor-shell", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.Load,
            Timeout = 60000,
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout]", new() { Timeout = 60000 });

        // The demo app's own 18 rem sidebar would leave the shell a narrower container than the viewport;
        // the shell decides from its CONTAINER, so give it the full width.
        await page.AddStyleTagAsync(new()
        {
            Content = "aside.fixed { display: none !important; } "
                + "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }",
        });
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    [TestMethod]
    public async Task Rail_expand_docks_the_left_panel_on_a_tablet()
    {
        var page = await OpenShellAsync(1024, 800);
        page.Console += (_, message) => Console.WriteLine($"SHELL console[{message.Type}] {message.Text}");
        page.PageError += (_, error) => Console.WriteLine("SHELL pageerror " + error);

        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='tablet']", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-region='rail-left']", new() { Timeout = 15000 });

        var toggle = page.Locator("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").First;
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(700);

        Console.WriteLine("SHELL afterExpand asides=" + await page.Locator("aside.tm-editor-shell__panel").CountAsync()
            + " rails=" + await page.Locator("[data-region^='rail-']").CountAsync());

        (await page.Locator("aside.tm-editor-shell__panel--left").CountAsync())
            .Should().Be(1, "expanding the rail must dock the left panel in a real browser too");
    }

    [TestMethod]
    public async Task Collapse_toggle_folds_the_docked_left_panel_on_a_desktop()
    {
        var page = await OpenShellAsync(1440, 900);
        await page.WaitForSelectorAsync(".tm-editor-shell[data-layout='desktop']", new() { Timeout = 30000 });

        var toggle = page.Locator("button.tm-editor-shell__panel-toggle--left").First;
        await toggle.ClickAsync();
        await page.WaitForTimeoutAsync(700);

        Console.WriteLine("SHELL afterCollapse asides=" + await page.Locator("aside.tm-editor-shell__panel").CountAsync()
            + " rails=" + await page.Locator("[data-region^='rail-']").CountAsync());

        (await page.Locator("[data-region='rail-left']").CountAsync())
            .Should().Be(1, "collapsing the docked panel must rail it in a real browser too");
    }
}
