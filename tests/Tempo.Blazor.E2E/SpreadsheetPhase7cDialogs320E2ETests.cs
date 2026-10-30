using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7c of the Spreadsheet redesign — the 320 px dialog pass, the Sort phone toolbar and the shared
/// <c>TmModal</c> header polish.
/// <para>
/// The three content-heavy dialogs are opened on the DESKTOP chrome (that is where the demo reaches
/// them: mobile is G2, so the Name Manager and the custom Sort have no phone entry) and then the
/// VIEWPORT is narrowed to 390 / 320. The dialog's mobile presentation follows the app-level viewport
/// scope, which is exactly the rotated-phone case; every assertion is taken AFTER the overlay reports
/// <c>data-layout="mobile"</c>, so nothing is measured mid-transition.
/// </para>
/// <para>
/// What is asserted: no horizontal overflow inside <c>.tm-modal-body</c> or the page, the footer is on
/// screen, no control is clipped out of the viewport, the reference's Sort two-column toolbar at 390,
/// the 320 stacking of its options, the Name Manager's wrapping "Refers to" line (+ single-line
/// ellipsised name, chip above it), Format Cells' category select above the options, and the shared
/// frame's × centred on the title's first line box.
/// </para>
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as
/// <c>phase7c-{name-manager,sort,format-cells}--mobile-{320,390}.png</c> (+ the long-names edge case and
/// the modal-header alignment shot) and are reviewed against
/// output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet-name-manager-dialog--mobile.png,
/// spreadsheet-sort-dialog--mobile.png and spreadsheet-format-cells-dialog--mobile.png.
/// </para>
/// </summary>
[TestClass]
public sealed class SpreadsheetPhase7cDialogs320E2ETests : WasmTestBase
{
    private const int Phase7cPhoneWidth = 320;
    private const int Phase7cReferencePhoneWidth = 390;

    // ── harness ─────────────────────────────────────────────────────────────

    private async Task<IPage> OpenDemoAsync(int width = 1440, int height = 960)
    {
        var page = await CreatePageAsync(hasTouch: true);
        await page.SetViewportSizeAsync(width, height);
        await GotoAppReadyAsync(page, $"{BaseUrl}/spreadsheet");
        var grid = DemoGrid(page);
        await grid.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync(
            @"() => !!window.tmSpreadsheetCanvas
                && !!document.querySelector(""[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid"")?.__tmSpreadsheetCanvas?.model",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });
        return page;
    }

    private static ILocator DemoGrid(IPage page)
        => page.Locator("[data-testid='spreadsheet-demo'] .tm-spreadsheet-canvas-grid");

    private static string ShotPath(string name)
    {
        var directory = Path.Combine(RepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "spreadsheet");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name + ".png");
    }

    private static DirectoryInfo RepoRoot()
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

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    /// <summary>Opens one dialog from the DESKTOP ribbon (its only entry: mobile is G2 for these).</summary>
    private static async Task OpenDataDialogAsync(IPage page, string entryTitle)
    {
        await DemoGrid(page).FocusAsync();
        await page.Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = "Data" }).First.ClickAsync();
        await page.Locator($"button[title='{entryTitle}']").First.ClickAsync();
        await page.WaitForTimeoutAsync(250);
    }

    /// <summary>Narrows the viewport and waits until the dialog reports its MOBILE presentation.</summary>
    private static async Task ResizeToPhoneAsync(IPage page, string dialogRootClass, int width)
    {
        await page.SetViewportSizeAsync(width, 800);
        var selector = $".tm-modal-overlay.{dialogRootClass}";
        await page.WaitForFunctionAsync(
            @"([sel, want]) => document.querySelector(sel)?.getAttribute('data-layout') === want",
            new object[] { selector, "mobile" },
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        // One paint past the layout flip (the presentation class and the CSS transition are done).
        await page.WaitForTimeoutAsync(400);
    }

    private sealed class Phase7cBox
    {
        public double DocumentOverflow { get; set; }

        public double BodyOverflow { get; set; }

        public double PanelWidth { get; set; }

        public double PanelLeft { get; set; }

        public double PanelRight { get; set; }

        public double ViewportWidth { get; set; }

        public bool FooterVisible { get; set; }

        public int OffscreenControls { get; set; }

        public int ControlCount { get; set; }
    }

    /// <summary>
    /// The 320 px contract: no horizontal overflow (page + dialog body), the footer is visible and no
    /// interactive control's box leaves the viewport. Measured from the live DOM, never from CSS.
    /// </summary>
    private static async Task AssertFitsThePhoneAsync(IPage page, string dialogRootClass)
    {
        var raw = await page.EvaluateAsync<string>(
            @"(sel) => {
                const overlay = document.querySelector(sel);
                if (!overlay) throw new Error('no dialog at ' + sel);
                const body = overlay.querySelector('.tm-modal-body');
                const panel = overlay.querySelector('.tm-modal-container');
                const footer = overlay.querySelector('.tm-modal-footer');
                const toRect = (el) => { const r = el.getBoundingClientRect(); return { l: r.left, t: r.top, r: r.right, b: r.bottom, w: r.width, h: r.height }; };
                const panelRect = toRect(panel);
                const footerRect = footer ? toRect(footer) : null;
                const controls = [...overlay.querySelectorAll('button, input, select, textarea, [role=""option""], [role=""tab""]')]
                    .filter(el => el.offsetParent !== null)
                    .map(toRect)
                    .filter(r => r.w > 0 && r.h > 0);
                return JSON.stringify({
                    documentOverflow: document.documentElement.scrollWidth - document.documentElement.clientWidth,
                    bodyOverflow: body.scrollWidth - body.clientWidth,
                    panelWidth: panelRect.w,
                    panelLeft: panelRect.l,
                    panelRight: panelRect.r,
                    viewportWidth: window.innerWidth,
                    footerVisible: !!footerRect && footerRect.h > 0
                        && footerRect.t >= -0.5 && footerRect.b <= window.innerHeight + 0.5,
                    offscreenControls: controls.filter(r => r.l < -0.5 || r.r > window.innerWidth + 0.5).length,
                    controlCount: controls.length
                });
            }",
            $".tm-modal-overlay.{dialogRootClass}");

        var box = JsonSerializer.Deserialize<Phase7cBox>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsTrue(box.ControlCount > 0, $"{dialogRootClass}: the probe must have measured the dialog's controls");
        Assert.AreEqual(0.0, box.DocumentOverflow,
            $"{dialogRootClass} at {box.ViewportWidth}px: the PAGE must not scroll horizontally");
        Assert.AreEqual(0.0, box.BodyOverflow,
            $"{dialogRootClass}: the dialog body must not scroll horizontally (scrollWidth - clientWidth)");
        Assert.IsTrue(box.PanelLeft >= -0.5 && box.PanelRight <= box.ViewportWidth + 0.5,
            $"{dialogRootClass}: the panel ({box.PanelLeft}-{box.PanelRight}) must stay inside the {box.ViewportWidth}px viewport");
        Assert.IsTrue(box.FooterVisible, $"{dialogRootClass}: the footer must be on screen at {box.ViewportWidth}px");
        Assert.AreEqual(0, box.OffscreenControls,
            $"{dialogRootClass}: every control's box must stay inside the viewport");
    }

    private async Task ScreenShotAsync(IPage page, string name)
    {
        var path = ShotPath(name);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = false, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    // ── Name Manager ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Phase7c_NameManager_Mobile320_FitsWithoutHorizontalOverflow()
    {
        var page = await OpenDemoAsync();
        await SeedNamedRangeAsync(page);

        await OpenDataDialogAsync(page, "Name Manager");
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        await ResizeToPhoneAsync(page, "tm-spreadsheet-name-manager", Phase7cPhoneWidth);

        var cards = page.Locator(".tm-spreadsheet-name-manager__card");
        await cards.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        Assert.IsTrue(await cards.CountAsync() > 0, "the phone Name Manager renders cards, not the five-column table");
        Assert.AreEqual(0, await page.Locator(".tm-spreadsheet-name-manager__table").CountAsync(),
            "no table at 320 (the phone card layout)");

        await AssertFitsThePhoneAsync(page, "tm-spreadsheet-name-manager");
        await ScreenShotAsync(page, "phase7c-name-manager--mobile-320");
        await page.CloseAsync();
    }

    /// <summary>
    /// Edge case: a name far longer than the card and a long sheet-qualified reference. The NAME keeps one
    /// ellipsised line, the REFERS TO line wraps and clamps to two lines with the full value in `title`,
    /// and the scope chip sits ABOVE the reference (under the name) on the narrowest phone.
    /// </summary>
    [TestMethod]
    public async Task Phase7c_NameManager_Mobile320_LongNamesWrapTheRefersToLine()
    {
        var page = await OpenDemoAsync();
        const string longName = "ConsolidatedRevenueForecast_2026_FullYear";
        const string longRefersTo = "=Sheet1!$B$2:$B$16,Sheet1!$D$2:$D$16,Sheet1!$F$2:$F$16";
        await SeedNamedRangeAsync(page, longName, longRefersTo);

        await OpenDataDialogAsync(page, "Name Manager");
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        await ResizeToPhoneAsync(page, "tm-spreadsheet-name-manager", Phase7cPhoneWidth);

        var card = page.Locator(".tm-spreadsheet-name-manager__card").First;
        await card.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        var raw = await page.EvaluateAsync<string>(
            @"() => {
                const card = document.querySelector('.tm-spreadsheet-name-manager__card');
                const name = card.querySelector('.tm-spreadsheet-name-manager__card-name');
                const refers = card.querySelector('.tm-spreadsheet-name-manager__card-refers');
                const chip = card.querySelector('.tm-spreadsheet-name-manager__chip');
                const toRect = (el) => { const r = el.getBoundingClientRect(); return { t: r.top, b: r.bottom, w: r.width, h: r.height }; };
                const nameStyle = getComputedStyle(name);
                const refersStyle = getComputedStyle(refers);
                return JSON.stringify({
                    nameText: name.textContent.trim(),
                    nameTitle: name.getAttribute('title'),
                    nameTextOverflow: nameStyle.textOverflow,
                    nameWhiteSpace: nameStyle.whiteSpace,
                    nameScrollWidth: name.scrollWidth,
                    nameClientWidth: name.clientWidth,
                    refersText: refers.textContent.trim(),
                    refersTitle: refers.getAttribute('title'),
                    refersAriaLabel: refers.getAttribute('aria-label'),
                    refersScrollWidth: refers.scrollWidth,
                    refersClientWidth: refers.clientWidth,
                    refersHeight: toRect(refers).h,
                    refersLineHeight: parseFloat(refersStyle.lineHeight),
                    refersOverflowWrap: refersStyle.overflowWrap || refersStyle.wordWrap,
                    chipTop: toRect(chip).t,
                    refersTop: toRect(refers).t,
                    nameRect: toRect(name)
                });
            }");

        var sample = JsonSerializer.Deserialize<NameManagerEdgeSample>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.AreEqual(longName, sample.NameText);
        Assert.AreEqual(longName, sample.NameTitle, "the full name stays readable from the title");
        Assert.AreEqual("ellipsis", sample.NameTextOverflow, "the name is ONE ellipsised line");
        Assert.AreEqual("nowrap", sample.NameWhiteSpace);
        Assert.IsTrue(sample.NameScrollWidth > sample.NameClientWidth,
            $"the name really is truncated at 320 (scroll {sample.NameScrollWidth} vs client {sample.NameClientWidth})");

        Assert.AreEqual(longRefersTo, sample.RefersText);
        Assert.AreEqual(longRefersTo, sample.RefersTitle, "the clamped reference keeps the full formula in the title");
        Assert.AreEqual("anywhere", sample.RefersOverflowWrap, "the reference breaks anywhere instead of overflowing");
        Assert.IsTrue(string.IsNullOrEmpty(sample.RefersAriaLabel),
            "a generic div must not carry aria-label (the visible text + title is the contract)");
        Assert.IsTrue(sample.RefersHeight <= sample.RefersLineHeight * 2 + 1.5,
            $"the reference clamps to TWO lines (height {sample.RefersHeight} vs line {sample.RefersLineHeight})");
        Assert.IsTrue(sample.RefersHeight >= sample.RefersLineHeight * 1.8,
            $"the edge-case formula really WRAPS onto a second line (height {sample.RefersHeight} vs line {sample.RefersLineHeight})");

        Assert.IsTrue(sample.ChipTop < sample.RefersTop,
            $"the scope chip moves UP under the name at 320 (chip {sample.ChipTop} vs refers {sample.RefersTop})");
        Assert.IsTrue(sample.ChipTop >= sample.NameRect.Top,
            "the chip stays below the name's top edge");

        await AssertFitsThePhoneAsync(page, "tm-spreadsheet-name-manager");
        await ScreenShotAsync(page, "phase7c-name-manager--mobile-320-long-names");
        await page.CloseAsync();
    }

    private sealed class NameManagerEdgeSample
    {
        public string NameText { get; set; } = string.Empty;

        public string NameTitle { get; set; } = string.Empty;

        public string NameTextOverflow { get; set; } = string.Empty;

        public string NameWhiteSpace { get; set; } = string.Empty;

        public double NameScrollWidth { get; set; }

        public double NameClientWidth { get; set; }

        public string RefersText { get; set; } = string.Empty;

        public string RefersTitle { get; set; } = string.Empty;

        // aria-label is PROHIBITED on a generic div (ARIA in HTML) — the title is the readable fallback,
        // so a regression that re-adds an aria-label must fail here too.
        public string? RefersAriaLabel { get; set; }

        public double RefersScrollWidth { get; set; }

        public double RefersClientWidth { get; set; }

        public double RefersHeight { get; set; }

        public double RefersLineHeight { get; set; }

        public string RefersOverflowWrap { get; set; } = string.Empty;

        public double ChipTop { get; set; }

        public double RefersTop { get; set; }

        public Phase7cTopLeft NameRect { get; set; } = new();
    }

    private sealed class Phase7cTopLeft
    {
        public double Top { get; set; }

        public double Left { get; set; }
    }

    /// <summary>Creates one named range through the Name Manager's own New dialog.</summary>
    private static async Task SeedNamedRangeAsync(IPage page, string name = "Sales", string refersTo = "=Sheet1!$B$2:$B$6")
    {
        var alreadyOpen = await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager").CountAsync() > 0;
        if (!alreadyOpen)
        {
            await OpenDataDialogAsync(page, "Name Manager");
            await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        }

        await page.Locator(".tm-spreadsheet-name-manager__toolbar button").First.ClickAsync();
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.FillAsync("#nr-name", name);
        await page.FillAsync("#nr-refers", refersTo);
        await page.Locator(".tm-spreadsheet-named-range-edit__btn--ok").ClickAsync();
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(250);

        // Close the manager again: every spec opens it through the real Data ribbon entry, on the
        // DESKTOP chrome (the phone layout is G2 and offers no Name Manager at all).
        await page.Locator(".tm-spreadsheet-name-manager__btn--close").ClickAsync();
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 20_000 });
        await page.WaitForTimeoutAsync(200);
    }

    // ── Sort ────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Phase7c_Sort_Mobile390_OptionsColumnSitsBesideAddLevel()
    {
        var page = await OpenDemoAsync();
        await OpenSortAsync(page);
        await ResizeToPhoneAsync(page, "tm-spreadsheet-sort", Phase7cReferencePhoneWidth);

        var layout = await ReadSortToolbarAsync(page);
        Assert.AreEqual(2, layout.ToolbarColumns,
            $"390: the phone toolbar is TWO columns (grid-template-columns: {layout.GridTemplateColumns})");
        Assert.AreEqual("column", layout.TogglesDirection,
            "390: the two option checkboxes are STACKED in the right column (the reference)");
        Assert.IsTrue(layout.ButtonRight <= layout.TogglesLeft + 1.0,
            $"390: Add level is the LEFT column and the options the right one (button ends {layout.ButtonRight}, options start {layout.TogglesLeft})");
        Assert.IsTrue(layout.TogglesHeight > layout.ToggleHeight,
            "390: both checkboxes fit in the right column (stacked, not side by side)");
        Assert.AreEqual(layout.ColumnWidths[0], layout.ButtonWidth, 1.0,
            $"390: the add-level button fills its grid column ({layout.ButtonWidth} vs {layout.ColumnWidths[0]}) — "
            + "its class is rendered by TmButton, so the scoped rule needs ::deep");
        Assert.IsTrue(layout.ButtonHeight >= 43.5,
            $"390: the add-level button keeps the 44px coarse touch target (height {layout.ButtonHeight})");

        await AssertFitsThePhoneAsync(page, "tm-spreadsheet-sort");
        await ScreenShotAsync(page, "phase7c-sort--mobile-390");
        await page.CloseAsync();
    }

    [TestMethod]
    public async Task Phase7c_Sort_Mobile320_OptionsStackUnderTheButton()
    {
        var page = await OpenDemoAsync();
        await OpenSortAsync(page);
        await ResizeToPhoneAsync(page, "tm-spreadsheet-sort", Phase7cPhoneWidth);

        var layout = await ReadSortToolbarAsync(page);
        Assert.AreEqual(1, layout.ToolbarColumns,
            $"320: the options stack UNDER the button — one column (grid-template-columns: {layout.GridTemplateColumns})");
        Assert.IsTrue(layout.TogglesTop >= layout.ButtonBottom - 1.0,
            $"320: the options begin below the Add-level button (button ends {layout.ButtonBottom}, options start {layout.TogglesTop})");
        Assert.AreEqual(layout.ColumnWidths[0], layout.ButtonWidth, 1.0,
            $"320: the add-level button fills the single grid column ({layout.ButtonWidth} vs {layout.ColumnWidths[0]})");
        Assert.IsTrue(layout.ButtonHeight >= 43.5,
            $"320: the add-level button keeps the 44px coarse touch target (height {layout.ButtonHeight})");

        await AssertFitsThePhoneAsync(page, "tm-spreadsheet-sort");
        await ScreenShotAsync(page, "phase7c-sort--mobile-320");
        await page.CloseAsync();
    }

    private static async Task OpenSortAsync(IPage page)
    {
        await OpenDataDialogAsync(page, "Custom sort…");
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-sort")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
    }

    private sealed class SortToolbarSample
    {
        public int ToolbarColumns { get; set; }

        public string GridTemplateColumns { get; set; } = string.Empty;

        public string TogglesDirection { get; set; } = string.Empty;

        public double ButtonRight { get; set; }

        public double ButtonBottom { get; set; }

        public double TogglesLeft { get; set; }

        public double TogglesTop { get; set; }

        public double ButtonWidth { get; set; }

        public double ButtonHeight { get; set; }

        public List<double> ColumnWidths { get; set; } = [];

        public double TogglesHeight { get; set; }

        public double ToggleHeight { get; set; }
    }

    private static async Task<SortToolbarSample> ReadSortToolbarAsync(IPage page)
    {
        var raw = await page.EvaluateAsync<string>(
            @"() => {
                const overlay = document.querySelector('.tm-modal-overlay.tm-spreadsheet-sort');
                const body = overlay.querySelector('.tm-spreadsheet-sort__body');
                const toolbar = overlay.querySelector('.tm-spreadsheet-sort__toolbar');
                const toggles = overlay.querySelector('.tm-spreadsheet-sort__toggles');
                const button = overlay.querySelector('.tm-spreadsheet-sort__tool');
                const tracks = getComputedStyle(toolbar).gridTemplateColumns.split(' ').filter(Boolean);
                const columnWidths = tracks.map(track => parseFloat(track));
                const toRect = (el) => { const r = el.getBoundingClientRect(); return { t: r.top, b: r.bottom, h: r.height, w: r.width, l: r.left, r: r.right }; };
                const checkboxes = [...toggles.querySelectorAll('.tm-spreadsheet-sort__header-toggle')].map(toRect);
                return JSON.stringify({
                    toolbarColumns: tracks.length,
                    gridTemplateColumns: getComputedStyle(toolbar).gridTemplateColumns,
                    togglesDirection: getComputedStyle(toggles).flexDirection,
                    buttonRight: toRect(button).r,
                    buttonBottom: toRect(button).b,
                    buttonWidth: toRect(button).w,
                    buttonHeight: toRect(button).h,
                    columnWidths,
                    togglesLeft: toRect(toggles).l,
                    togglesTop: toRect(toggles).t,
                    togglesHeight: toRect(toggles).h,
                    toggleHeight: checkboxes.length ? Math.max(...checkboxes.map(c => c.h)) : 0,
                    bodyWidth: toRect(body).w
                });
            }");

        return JsonSerializer.Deserialize<SortToolbarSample>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    // ── Format Cells ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Phase7c_FormatCells_Mobile320_CategorySelectAboveOptions()
    {
        var page = await OpenDemoAsync();

        // The desktop entry is the cell context menu (the phone action bar's Format opens the same dialog).
        var grid = DemoGrid(page);
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Button = MouseButton.Right, Position = new() { X = 120, Y = 60 } });
        await page.Locator(".tm-spreadsheet-context-menu__item", new() { HasTextString = "Format Cells" }).First.ClickAsync();
        await page.Locator(".tm-modal-overlay.tm-fcd")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        await ResizeToPhoneAsync(page, "tm-fcd", Phase7cPhoneWidth);
        await page.Locator(".tm-fcd__category-select")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        var raw = await page.EvaluateAsync<string>(
            @"() => {
                const overlay = document.querySelector('.tm-modal-overlay.tm-fcd');
                const select = overlay.querySelector('.tm-fcd__category-select');
                const options = overlay.querySelector('.tm-fcd__number-options');
                const listbox = overlay.querySelector('.tm-fcd__listbox[role=""listbox""]');
                const toRect = (el) => { const r = el.getBoundingClientRect(); return { t: r.top, b: r.bottom, w: r.width }; };
                return JSON.stringify({
                    selectVisible: !!select && toRect(select).w > 0,
                    selectWidth: select ? toRect(select).w : 0,
                    selectBottom: select ? toRect(select).b : -1,
                    optionsTop: options ? toRect(options).t : -1,
                    hasCategoryListbox: !!listbox,
                    tabsWrap: getComputedStyle(overlay.querySelector('.tm-fcd__tabs')).flexWrap
                });
            }");
        var sample = JsonSerializer.Deserialize<FormatCellsSample>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.IsTrue(sample.SelectVisible, "320: the category is the phone SELECT, not the 140px listbox column");
        Assert.IsFalse(sample.HasCategoryListbox, "320: the desktop category listbox is not rendered");
        Assert.IsTrue(sample.SelectBottom <= sample.OptionsTop + 0.5,
            $"320: the category select sits ABOVE the options (select ends {sample.SelectBottom}, options start {sample.OptionsTop})");
        Assert.AreEqual("wrap", sample.TabsWrap, "320: the five tabs wrap instead of overflowing the dialog");

        await AssertFitsThePhoneAsync(page, "tm-fcd");
        await ScreenShotAsync(page, "phase7c-format-cells--mobile-320");
        await page.CloseAsync();
    }

    private sealed class FormatCellsSample
    {
        public bool SelectVisible { get; set; }

        public double SelectWidth { get; set; }

        public double SelectBottom { get; set; }

        public double OptionsTop { get; set; }

        public bool HasCategoryListbox { get; set; }

        public string TabsWrap { get; set; } = string.Empty;
    }

    // ── Shared frame: the × shares the title's first line box ───────────────

    [TestMethod]
    public async Task Phase7c_TmModal_CloseButton_IsCentredOnTheTitleLine()
    {
        // (1) a Spreadsheet dialog (title only, no subtitle, no icon)
        var page = await OpenDemoAsync();
        await OpenDataDialogAsync(page, "Name Manager");
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });

        var spreadsheet = await ReadModalHeaderAlignmentAsync(page, ".tm-modal-overlay.tm-spreadsheet-name-manager");
        Assert.IsTrue(Math.Abs(spreadsheet.Delta) <= 1.0,
            $"the Name Manager × must share the title line box (× centre {spreadsheet.CloseCentre}, title line centre {spreadsheet.TitleLineCentre}, "
            + $"delta {spreadsheet.Delta:F2}px; close top {spreadsheet.CloseTop}, title top {spreadsheet.TitleTop}, "
            + $"font {spreadsheet.TitleFontSize}, line {spreadsheet.TitleLineHeight}, margin {spreadsheet.CloseMarginBlockStart}, "
            + $"align {spreadsheet.HeaderAlignItems})");
        Assert.IsTrue(spreadsheet.CloseWidth >= 32.0,
            $"the click/touch target keeps at least its 2rem size (44 on the coarse pointer this page emulates, got {spreadsheet.CloseWidth})");
        Assert.IsTrue(spreadsheet.CloseHeight >= 32.0);
        await ScreenShotAsync(page, "phase7c-modal-header--spreadsheet-dialog");
        await page.CloseAsync();

        // (2) the core demo's modals: with a subtitle, and with a header icon
        foreach (var (triggerTestId, name) in new[]
                 {
                     ("open-basic-modal", "core-basic"),
                     ("open-icon-modal", "core-icon"),
                 })
        {
            var modalPage = await CreatePageAsync();
            await modalPage.SetViewportSizeAsync(1440, 900);
            await GotoAppReadyAsync(modalPage, $"{BaseUrl}/modal-dialog");
            await modalPage.Locator($"[data-testid='{triggerTestId}']").First.ClickAsync();
            await modalPage.Locator(".tm-modal-overlay").First
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
            await modalPage.WaitForTimeoutAsync(300);

            var sample = await ReadModalHeaderAlignmentAsync(modalPage, ".tm-modal-overlay:has(.tm-modal-title)");
            Assert.IsTrue(Math.Abs(sample.Delta) <= 1.0,
                $"{name}: the × must share the title's first line box (delta {sample.Delta:F2}px)");
            Assert.AreEqual(32.0, sample.CloseWidth, $"{name}: the target keeps its 2rem size");
            Assert.IsTrue(sample.HasHeaderIcon == (name == "core-icon"),
                $"{name}: the header icon presence is what the shot is meant to cover");
            await ScreenShotAsync(modalPage, $"phase7c-modal-header--{name}");
            await modalPage.CloseAsync();
        }
    }

    private sealed class HeaderAlignmentSample
    {
        public double Delta { get; set; }

        public double CloseWidth { get; set; }

        public double CloseHeight { get; set; }

        public double CloseCentre { get; set; }

        public double TitleLineCentre { get; set; }

        public bool HasHeaderIcon { get; set; }

        public double CloseTop { get; set; }

        public double TitleTop { get; set; }

        public string TitleFontSize { get; set; } = string.Empty;

        public string TitleLineHeight { get; set; } = string.Empty;

        public string CloseMarginBlockStart { get; set; } = string.Empty;

        public string CloseFontSize { get; set; } = string.Empty;

        public string HeaderAlignItems { get; set; } = string.Empty;
    }

    /// <summary>
    /// Compares the CENTRE of the 2rem close box with the centre of the title's FIRST LINE BOX (top +
    /// line-height / 2). The tolerance is a single pixel: the fix is a half-line-box offset, not a
    /// re-layout, so anything above 1 px means the optical misalignment came back.
    /// </summary>
    private static async Task<HeaderAlignmentSample> ReadModalHeaderAlignmentAsync(IPage page, string overlaySelector)
    {
        var raw = await page.EvaluateAsync<string>(
            @"(sel) => {
                const overlay = document.querySelector(sel);
                if (!overlay) throw new Error('no modal overlay for ' + sel);
                const header = overlay.querySelector('.tm-modal-header');
                const close = header.querySelector('.tm-modal-close');
                const title = header.querySelector('.tm-modal-title');
                if (!header || !close || !title) throw new Error('the header is missing its close button or title');
                const closeRect = close.getBoundingClientRect();
                const titleRect = title.getBoundingClientRect();
                const lineHeight = parseFloat(getComputedStyle(title).lineHeight);
                const closeStyle = getComputedStyle(close);
                const titleStyle = getComputedStyle(title);
                return JSON.stringify({
                    delta: (closeRect.top + closeRect.height / 2) - (titleRect.top + lineHeight / 2),
                    closeWidth: closeRect.width,
                    closeHeight: closeRect.height,
                    closeCentre: closeRect.top + closeRect.height / 2,
                    titleLineCentre: titleRect.top + lineHeight / 2,
                    hasHeaderIcon: !!header.querySelector('.tm-modal-header-icon'),
                    closeTop: closeRect.top,
                    titleTop: titleRect.top,
                    titleFontSize: titleStyle.fontSize,
                    titleLineHeight: titleStyle.lineHeight,
                    closeMarginBlockStart: closeStyle.marginBlockStart,
                    closeFontSize: closeStyle.fontSize,
                    headerAlignItems: getComputedStyle(header).alignItems
                });
            }",
            overlaySelector);

        return JsonSerializer.Deserialize<HeaderAlignmentSample>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
