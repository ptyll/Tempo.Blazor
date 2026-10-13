using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-21 lane: the TmDataTable column resize on the shared pointer-drag helper
/// (data-table.js → pointer-drag.js). A real mouse drag follows the pointer live and commits once;
/// Escape mid-drag restores the start width and persists nothing; a real CDP touch (coarse pointer)
/// resizes through the widened handle; a tap on the handle must not sort the column; and the
/// focused separator resizes with the arrow keys (the WCAG 2.5.7 non-drag alternative).
/// </summary>
[TestClass]
public class DataTableColumnResizeE2ETests : WasmTestBase
{
    private const string Section = "[data-testid='dt-ergonomics-section']";

    private async Task<IPage> OpenDataTableAsync(bool touch, int width, int height)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 640,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/data-table", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var table = page.Locator($"{Section} .tm-data-table");
        await table.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await page.EvaluateAsync("() => document.querySelector(\"[data-testid='dt-ergonomics-section'] .tm-data-table\").scrollIntoView({ block: 'center', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(300);
        return page;
    }

    private static ILocator Handle(IPage page, string column) => page.Locator($"{Section} [data-testid='resize-{column}']");

    private static ILocator NameHeader(IPage page) => page.Locator($"{Section} thead th").Filter(new LocatorFilterOptions { HasText = "Name" }).First;

    private static async Task<double> NameWidthAsync(IPage page)
        => await page.EvaluateAsync<double>(@"() => {
            const section = document.querySelector(""[data-testid='dt-ergonomics-section']"");
            const th = [...section.querySelectorAll('thead th')].find(t => t.textContent.includes('Name'));
            return th.getBoundingClientRect().width;
        }");

    private static async Task<double> ScoreWidthAsync(IPage page)
        => await page.EvaluateAsync<double>(@"() => {
            const section = document.querySelector(""[data-testid='dt-ergonomics-section']"");
            const th = [...section.querySelectorAll('thead th')].find(t => t.textContent.includes('Score'));
            return th.getBoundingClientRect().width;
        }");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_Mouse_DragFollowsThePointer_AndPersistsTheWidth()
    {
        var page = await OpenDataTableAsync(touch: false, 1440, 900);
        var before = await NameWidthAsync(page);

        var box = (await Handle(page, "Name").BoundingBoxAsync())!;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 50, y, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        var after = await NameWidthAsync(page);
        Assert.AreEqual(before + 50, after, 4.0, "the width follows the pointer and is re-rendered by Blazor");
        Assert.AreEqual((before + 50).ToString("0"), await Handle(page, "Name").GetAttributeAsync("aria-valuenow"),
            "the separator announces the committed width");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_Mouse_EscapeMidDrag_RestoresTheStartWidth_AndPersistsNothing()
    {
        var page = await OpenDataTableAsync(touch: false, 1440, 900);
        var before = await NameWidthAsync(page);
        var valueBefore = await Handle(page, "Name").GetAttributeAsync("aria-valuenow");

        var box = (await Handle(page, "Name").BoundingBoxAsync())!;
        var y = box.Y + box.Height / 2;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, y);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2 + 60, y, new MouseMoveOptions { Steps = 8 });
        Assert.IsTrue(await NameWidthAsync(page) > before + 30, "precondition: the drag widened the column");
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();

        Assert.AreEqual(before, await NameWidthAsync(page), 2.0, "Escape restores the start width");
        Assert.AreEqual(valueBefore, await Handle(page, "Name").GetAttributeAsync("aria-valuenow"),
            "the separator value is restored — a cancelled drag persists nothing");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_Touch_At768_ResizesThroughTheWidenedHandle()
    {
        var page = await OpenDataTableAsync(touch: true, 768, 900);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"), "precondition: coarse pointer emulation");

        var handleBox = (await Handle(page, "Name").BoundingBoxAsync())!;
        Assert.IsTrue(handleBox.Width >= 24, $"the handle widens on a coarse pointer ({handleBox.Width}px)");
        Assert.AreEqual("none", await Handle(page, "Name").EvaluateAsync<string>("e => getComputedStyle(e).touchAction"),
            "the handle starts the drag immediately; touch-action belongs to it alone");

        var before = await NameWidthAsync(page);
        var session = await page.Context.NewCDPSessionAsync(page);
        var x = handleBox.X + handleBox.Width / 2;
        var y = handleBox.Y + handleBox.Height / 2;
        await TouchAsync(session, "touchStart", x, y);
        for (var i = 1; i <= 6; i++)
        {
            await TouchAsync(session, "touchMove", x + 48 * i / 6.0, y);
        }
        await TouchAsync(session, "touchEnd", x + 48, y);

        Assert.AreEqual(before + 48, await NameWidthAsync(page), 4.0, "the finger resizes the column");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_Touch_TapOnTheHandle_DoesNotSort()
    {
        var page = await OpenDataTableAsync(touch: true, 768, 900);
        var sortBefore = await NameHeader(page).GetAttributeAsync("aria-sort");

        var handleBox = (await Handle(page, "Role").BoundingBoxAsync())!;
        var roleHeader = page.Locator($"{Section} thead th").Filter(new LocatorFilterOptions { HasText = "Role" }).First;
        var roleSortBefore = await roleHeader.GetAttributeAsync("aria-sort");

        var session = await page.Context.NewCDPSessionAsync(page);
        var x = handleBox.X + handleBox.Width / 2;
        var y = handleBox.Y + handleBox.Height / 2;
        await TouchAsync(session, "touchStart", x, y);
        await TouchAsync(session, "touchEnd", x, y);
        await page.WaitForTimeoutAsync(200);

        Assert.AreEqual(roleSortBefore, await roleHeader.GetAttributeAsync("aria-sort"),
            "a tap on the resize handle is not a sort");
        Assert.AreEqual(sortBefore, await NameHeader(page).GetAttributeAsync("aria-sort"));
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task DataTable_Keyboard_FocusedSeparator_ArrowKeysResize_By8()
    {
        var page = await OpenDataTableAsync(touch: false, 1440, 900);
        var handle = Handle(page, "Score");
        var scoreHeader = page.Locator($"{Section} thead th").Filter(new LocatorFilterOptions { HasText = "Score" }).First;
        await scoreHeader.GetAttributeAsync("aria-sort"); // wait for the table to be settled

        await handle.FocusAsync();
        // The Score column has no preset width: the first step starts from the REAL rendered
        // <th> width, measured through tmDataTable.measureColumn (never the 160 default).
        var before = Math.Round(await ScoreWidthAsync(page));

        for (var i = 0; i < 3; i++)
        {
            await page.Keyboard.PressAsync("ArrowRight");
            await page.WaitForTimeoutAsync(120);
        }

        Assert.AreEqual(before + 24, int.Parse((await handle.GetAttributeAsync("aria-valuenow"))!),
            "three arrow steps of 8px resize the column from the measured width (aria-valuenow tracks it)");
        var headerStyle = await scoreHeader.GetAttributeAsync("style");
        StringAssert.Contains(headerStyle, $"width:{before + 24}px", "the committed width is rendered");
        Assert.AreEqual(before + 24, Math.Round(await ScoreWidthAsync(page)), 2.0,
            "the auto table layout honours the width through the rendered <th>");
    }

    private static Task TouchAsync(ICDPSession session, string type, double x, double y)
        => session.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd"
                ? Array.Empty<object>()
                : new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y, ["id"] = 1 } },
        });
}
