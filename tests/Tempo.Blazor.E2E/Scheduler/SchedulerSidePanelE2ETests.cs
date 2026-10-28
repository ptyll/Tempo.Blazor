using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// Phase 8. The side panel is the core TmSidePanel: docked on the right at desktop, a side sheet on
/// tablet, a bottom sheet on mobile. The resource filter is the core TmMultiSelect and narrows the
/// timeline rows and the week columns. Screenshots are element-scoped.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class SchedulerSidePanelE2ETests : WasmTestBase
{
    private const string Panel = "[data-testid='scheduler-panel'] .tm-scheduler";

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        if (touch)
            await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.GotoAsync($"{BaseUrl}/scheduler",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    /// <summary>
    /// The panel starts closed, so a page load never covers the other cards with a sheet. A test that
    /// needs it opens it through the header toggle and closes it again before it returns.
    /// </summary>
    private static async Task<ILocator> OpenPanelAsync(IPage page)
    {
        var root = page.Locator(Panel);
        await page.Locator("[data-testid='scheduler-panel-toggle']").ClickAsync();
        return root;
    }

    private static async Task ClosePanelAsync(IPage page, ILocator root)
    {
        var close = root.Locator(".tm-side-panel__close, .tm-drawer__close").First;
        if (await close.CountAsync() > 0)
        {
            await close.ClickAsync();
        }

        await Assertions.Expect(page.Locator(".tm-drawer")).ToHaveCountAsync(0);
    }

    [TestMethod]
    public async Task Desktop_Panel_Is_Docked_On_The_Right()
    {
        var page = await OpenAsync(1440, 1100, touch: false);
        var root = await OpenPanelAsync(page);

        var docked = root.Locator("aside.tm-side-panel--docked[data-side='right']");
        await Assertions.Expect(docked).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator(".tm-drawer")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid='scheduler-panel-day']")).ToContainTextAsync("Selected day");
        var bodyBox = await root.Locator(".tm-scheduler-body").BoundingBoxAsync();
        var panelBox = await docked.BoundingBoxAsync();
        Assert.IsNotNull(bodyBox);
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox!.X >= bodyBox!.X + bodyBox.Width - 1,
            $"docked panel left {panelBox.X} is left of the body right {bodyBox.X + bodyBox.Width}");
        Assert.IsTrue(Math.Abs(panelBox.Y - bodyBox.Y) <= 1,
            $"docked panel top {panelBox.Y} differs from the body top {bodyBox.Y}");

        await docked.ScreenshotAsync(new LocatorScreenshotOptions { Path = Shot("desktop-docked") });
        await ClosePanelAsync(page, root);
    }

    [TestMethod]
    public async Task Tablet_Panel_Is_A_Side_Sheet()
    {
        var page = await OpenAsync(1024, 900, touch: true);
        var root = await OpenPanelAsync(page);

        var sheet = page.Locator(".tm-drawer--right");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await sheet.ScreenshotAsync(new LocatorScreenshotOptions { Path = Shot("tablet-sheet") });
        await ClosePanelAsync(page, root);
    }

    [TestMethod]
    public async Task Mobile_Panel_Is_A_Bottom_Sheet_And_Escape_Returns_Focus()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var root = await OpenPanelAsync(page);

        var sheet = page.Locator(".tm-drawer--bottom");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await sheet.ScreenshotAsync(new LocatorScreenshotOptions { Path = Shot("mobile-sheet") });

        // The host panel opens over the calendar. Close it with its own button (Escape also returns
        // focus, and that would become the opener the next sheet restores to) so the event click is
        // what opens the detail and what the closed sheet returns focus to.
        await sheet.Locator(".tm-drawer__close").ClickAsync();
        await Assertions.Expect(sheet).ToHaveCountAsync(0);

        var eventButton = root.Locator("[data-event-id]").First;
        await eventButton.ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        // Read the id once the sheet has rendered, so the click's focus move has settled.
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToHaveCountAsync(0);
        await Assertions.Expect(eventButton).ToBeFocusedAsync();
    }

    [TestMethod]
    public async Task Resource_Filter_Narrows_The_Timeline_And_The_Week()
    {
        var page = await OpenAsync(1440, 1100, touch: false);
        var root = await OpenPanelAsync(page);

        var filter = root.Locator("[data-testid='scheduler-resource-filter'] .tm-multiselect");
        await filter.ClickAsync();
        await page.Locator(".tm-multiselect__option", new PageLocatorOptions { HasText = "Conference Room A" }).ClickAsync();
        await page.Keyboard.PressAsync("Escape");

        // The month keeps only the selected resource's event.
        await Assertions.Expect(root.Locator("[data-event-id='p1']")).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator("[data-event-id='p2']")).ToHaveCountAsync(0);

        // The timeline drops the other rows.
        await root.Locator("[role='tab'][data-view='Timeline']").ClickAsync();
        await Assertions.Expect(root.Locator(".tm-scheduler-timeline-row[data-resource='room-a']")).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator(".tm-scheduler-timeline-row[data-resource='room-b']")).ToHaveCountAsync(0);

        // The week keeps only that resource's column.
        await root.Locator("[role='tab'][data-view='Week']").ClickAsync();
        await Assertions.Expect(root.Locator(".tm-scheduler-timegrid-resource-col[data-resource='room-a']").First).ToBeVisibleAsync();
        await Assertions.Expect(root.Locator(".tm-scheduler-timegrid-resource-col[data-resource='room-b']")).ToHaveCountAsync(0);

        await root.ScreenshotAsync(new LocatorScreenshotOptions { Path = Shot("filter-week") });
        await ClosePanelAsync(page, root);
    }

    [TestMethod]
    public async Task Desktop_Escape_Closes_The_Detail_And_Returns_Focus()
    {
        var page = await OpenAsync(1440, 1100, touch: false);
        var root = await OpenPanelAsync(page);

        var evt = root.Locator("[data-event-id='p1']");
        await evt.EvaluateAsync("node => node.click()");
        var detail = page.Locator("[data-testid='scheduler-panel-detail']");
        await Assertions.Expect(detail).ToBeVisibleAsync();
        await page.Locator("[data-testid='scheduler-close-detail']").EvaluateAsync("node => node.focus()");

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("[data-testid='scheduler-panel-detail']")).ToHaveCountAsync(0);
        await Assertions.Expect(root.Locator("aside.tm-side-panel--docked")).ToBeVisibleAsync();
        await Assertions.Expect(evt).ToBeFocusedAsync();
        await ClosePanelAsync(page, root);
    }

    [TestMethod]
    public async Task Panel_And_Filter_Popup_Have_No_Serious_Axe_Findings()
    {
        var desktop = await OpenAsync(1440, 1100, touch: false);
        var docked = await OpenPanelAsync(desktop);
        await Assertions.Expect(docked.Locator("aside.tm-side-panel--docked")).ToBeVisibleAsync();
        AssertNoSerious(await AxeAsync(desktop), "desktop docked panel");
        await docked.Locator("[data-testid='scheduler-resource-filter'] .tm-multiselect").ClickAsync();
        await Assertions.Expect(desktop.Locator(".tm-multiselect__popup")).ToBeVisibleAsync();
        AssertNoSerious(await AxeAsync(desktop), "desktop filter popup");
        await ClosePanelAsync(desktop, docked);

        var mobile = await OpenAsync(390, 844, touch: true);
        var sheet = await OpenPanelAsync(mobile);
        await Assertions.Expect(mobile.Locator(".tm-drawer--bottom")).ToBeVisibleAsync();
        AssertNoSerious(await AxeAsync(mobile), "mobile sheet");
        await ClosePanelAsync(mobile, sheet);
    }

    private static void AssertNoSerious(string[] violations, string where)
        => Assert.AreEqual(0, violations.Length,
            where + ":" + Environment.NewLine + string.Join(Environment.NewLine, violations));

    private static async Task<string[]> AxeAsync(IPage page)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });
        return await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('.tm-multiselect__popup')
                    || document.querySelector('.tm-drawer')
                    || document.querySelector("[data-testid='scheduler-panel'] aside.tm-side-panel--docked");
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                result.violations.forEach(v => {
                    v.nodes = (v.nodes || []).filter(n => {
                        const el = n.target && n.target[0] ? document.querySelector(n.target[0]) : null;
                        return !el || el.getClientRects().length > 0;
                    });
                });
                return result.violations
                    .filter(v => v.nodes.length > 0 && (v.impact === 'critical' || v.impact === 'serious'))
                    .map(v => `${v.impact}: ${v.id} - ${v.help} [${(v.nodes[0] && v.nodes[0].target) || ''}]`);
            }
            """);
    }

    private static string Shot(string name)
    {
        var dir = Path.Combine("TestResults", "scheduler-side-panel");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, name + ".png");
    }
}
