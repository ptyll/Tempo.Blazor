using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 3 (DE-1.1): the editor lays itself out from its OWN container. At 1440 / 1024 (touch) /
/// 390 / 320 nothing overflows horizontally, the regions follow the container (not the viewport), a narrow host on a wide
/// viewport switches mode, and F6 cycles the five landmarks. The screenshots are
/// <see cref="DocumentEditorRedesignPhase3CaptureE2ETests"/> (a gated generator); this class writes no files.
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[TestCategory("DocumentEditor:ResponsiveShell")]
[DoNotParallelize]
public sealed class DocumentEditorResponsiveShellE2ETests : WasmTestBase
{
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("At 1440 / 1024 touch / 390 / 320 the editor never scrolls horizontally and its regions follow the container width")]
    public async Task Shell_NeverOverflowsHorizontally_AndRegionsFollowTheContainer_AtTheFourWidths()
    {
        foreach (var (name, width, height, touch) in SurfaceViewports.Append(("1920x1080", 1920, 1080, false)))
        {
            var (page, errors) = await OpenAsync(HistoryDocumentId, width, height, touch);

            var probe = await ReadShellAsync(page);
            // A page wider than the doc region must stay reachable: either it fits, or the canvas host scrolls horizontally.
            var scrolls = probe.HostOverflowX is "auto" or "scroll" && probe.HostScrollWidth > probe.HostClientWidth;
            Assert.IsTrue(probe.PageRight <= probe.DocRight + 1 || scrolls,
                $"{name}: the page (right {probe.PageRight}) either fits the doc region (right {probe.DocRight}) or the host scrolls (overflow-x {probe.HostOverflowX}, {probe.HostScrollWidth} > {probe.HostClientWidth})");
            Assert.IsFalse(probe.PageOverflowsX, $"{name}: the document must not scroll horizontally (scrollWidth {probe.ScrollWidth} > {probe.InnerWidth})");
            Assert.IsFalse(probe.EditorOverflowsX, $"{name}: the editor root must not overflow its own box ({probe.EditorScrollWidth} > {probe.EditorClientWidth}); widest: {string.Join(" | ", probe.Offenders)}");

            var expected = probe.EditorClientWidth < 640 ? "mobile" : probe.EditorClientWidth < 1024 ? "tablet" : "desktop";
            Assert.AreEqual(expected, probe.Layout, $"{name}: data-layout follows the editor's own width ({probe.EditorClientWidth}px), not the viewport");
            // Phase 8: a tablet's inspector is a side sheet in the document's own cell (its region stays, closed it is the edge toggle), a phone has
            // no inspector region at all - its panels are sheets.
            var expectedRegions = expected == "desktop" ? "nav,doc,inspector" : expected == "tablet" ? "doc,inspector" : "doc";
            Assert.AreEqual(expectedRegions, string.Join(",", probe.VisibleRegions), $"{name}: the visible regions");
            Assert.IsTrue(probe.HeaderVisible, $"{name}: the header renders at every width");
            Assert.AreEqual(1, probe.SaveCount, $"{name}: Save renders exactly once");

            // The regions are laid out side by side exactly when the container is wide enough, and never overlap.
            if (expected == "desktop")
            {
                Assert.IsTrue(probe.NavRight <= probe.DocLeft + 1 && probe.DocRight <= probe.InspectorLeft + 1,
                    $"{name}: navigation | document | inspector sit in one row ({probe.NavRight}/{probe.DocLeft}/{probe.DocRight}/{probe.InspectorLeft})");
            }
            else if (expected == "tablet")
            {
                // The side sheet OVERLAYS the document: it never narrows it (the closed sheet is only an edge toggle at the document's end).
                Assert.IsTrue(probe.InspectorLeft >= probe.DocLeft - 1 && probe.InspectorLeft <= probe.DocRight + 1, $"{name}: the inspector region shares the document's cell");
            }
            else
            {
                Assert.AreEqual(0, probe.InspectorLeft, $"{name}: a phone has no inspector region");
            }

            AssertNoClientErrors(errors, name);
            await page.Context.CloseAsync();
        }
    }

    [TestMethod]
    [Description("A narrow host on a wide viewport switches the editor's mode by container width, both ways")]
    public async Task NarrowHost_OnAWideViewport_SwitchesModeByContainer()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);

        // The editor host is the TmLayoutObserver root; pin its width the way a host application would by sizing a parent.
        foreach (var (hostWidth, expectedLayout, expectedRegions) in new[]
        {
            (420, "mobile", "doc"),
            (800, "tablet", "doc,inspector"),
            (1280, "desktop", "nav,doc,inspector"),
            (560, "mobile", "doc"),
        })
        {
            await page.EvaluateAsync("w => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = w + 'px'; host.style.maxWidth = w + 'px'; }", hostWidth);
            await WaitForPredicateAsync(page, $"() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === '{expectedLayout}'", 10_000);
            await page.WaitForTimeoutAsync(250);

            var probe = await ReadShellAsync(page);
            Assert.AreEqual(1440, probe.InnerWidth, "the viewport itself never changed");
            Assert.AreEqual(expectedRegions, string.Join(",", probe.VisibleRegions), $"host {hostWidth}px: the visible regions");
            Assert.IsFalse(probe.EditorOverflowsX, $"host {hostWidth}px: the editor does not overflow its box");
            Assert.IsTrue(probe.EditorClientWidth <= hostWidth + 1, $"host {hostWidth}px: the editor fits its host ({probe.EditorClientWidth})");
        }

        AssertNoClientErrors(errors, "narrow host resize");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A host-width change re-fits the canvas pages with no console error and the document stays editable")]
    public async Task HostWidthChange_RefitsTheCanvas_WithoutErrors()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);

        var before = await page.EvaluateAsync<double>("() => document.querySelector('[data-region=\"doc\"]').getBoundingClientRect().width");
        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '700px'; host.style.maxWidth = '700px'; }");
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === 'tablet'", 10_000);
        await page.WaitForTimeoutAsync(500);
        var after = await page.EvaluateAsync<double>("() => document.querySelector('[data-region=\"doc\"]').getBoundingClientRect().width");
        // The desktop doc region shared the row with the navigation and the inspector; a tablet's sheet overlays it, so the region is the host's width.
        Assert.IsTrue(after <= 700 + 1, $"the doc region follows the (smaller) host ({before} -> {after})");
        // The canvas host fills exactly its region (the page inside scrolls in the host, never the page).
        var hostWidth = await page.EvaluateAsync<double>("() => document.querySelector('[data-testid=\"document-canvas-engine-host\"]').getBoundingClientRect().width");
        Assert.IsTrue(hostWidth <= after + 1, $"the canvas host stays inside the doc region ({hostWidth} > {after})");

        // Still alive: a character typed into the canvas lands in the document.
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.TypeAsync("Z");
        await WaitForPredicateAsync(page, "() => (document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.textContent || document.body.innerText).includes('Z')", 5_000);

        AssertNoClientErrors(errors, "host width change");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("F6 / Shift+F6 cycle header, ribbon, navigation, document, inspector; the ribbon keyboard mode stays off")]
    public async Task F6_CyclesTheFiveRegions_AndLeavesRibbonKeyboardModeAlone()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);
        Assert.AreEqual("desktop", (await ReadShellAsync(page)).Layout, "this spec needs the desktop regions");

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        Assert.AreEqual("doc", await ActiveRegionAsync(page), "the click puts the caret in the document");

        var forward = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            await page.Keyboard.PressAsync("F6");
            forward.Add(await ActiveRegionAsync(page));
        }

        CollectionAssert.AreEqual(new[] { "inspector", "header", "ribbon", "nav", "doc" }, forward, "F6 walks the landmarks in order and wraps");

        await page.Keyboard.PressAsync("Shift+F6");
        Assert.AreEqual("nav", await ActiveRegionAsync(page), "Shift+F6 goes back");
        await page.Keyboard.PressAsync("Shift+F6");
        Assert.AreEqual("ribbon", await ActiveRegionAsync(page));

        Assert.AreEqual("false", await page.Locator(".tm-document-editor").GetAttributeAsync("data-ribbon-keyboard-mode"),
            "F6 only moves focus; the Alt / F10 ribbon keyboard mode is unchanged");
        Assert.IsFalse(await page.EvaluateAsync<bool>("() => document.activeElement === document.body"), "the focus never falls to the page");

        // Alt still activates the ribbon keyboard mode, exactly as before.
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("F10");
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-ribbon-keyboard-mode') === 'true'", 5_000);

        AssertNoClientErrors(errors, "F6 cycle");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("On a phone container the cycle is header, ribbon, document - the inspector region is gone, its panels are sheets")]
    public async Task F6_OnAPhoneContainer_HasNoInspectorRegion()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 390, 844, touch: true);
        Assert.AreEqual("mobile", (await ReadShellAsync(page)).Layout);

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        var visited = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            await page.Keyboard.PressAsync("F6");
            visited.Add(await ActiveRegionAsync(page));
        }

        CollectionAssert.AreEqual(new[] { "header", "ribbon", "doc" }, visited);
        AssertNoClientErrors(errors, "F6 phone");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("On a tablet container the cycle skips the (absent) navigation column")]
    public async Task F6_OnATabletContainer_SkipsTheNavigationColumn()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1180, 820, touch: true);
        var layout = (await ReadShellAsync(page)).Layout;
        Assert.AreEqual("tablet", layout, "the editor next to the demo sidebar at 1180 px is a tablet container");

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        var visited = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            await page.Keyboard.PressAsync("F6");
            visited.Add(await ActiveRegionAsync(page));
        }

        CollectionAssert.AreEqual(new[] { "inspector", "header", "ribbon", "doc" }, visited);
        AssertNoClientErrors(errors, "F6 tablet");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The header owns Save / Undo / Redo exactly once; Search opens the find panel; the title and the save state live in the header")]
    public async Task Header_OwnsTheDocumentActions_ExactlyOnce()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);

        foreach (var id in new[] { "document-save", "document-undo", "document-redo" })
        {
            Assert.AreEqual(1, await page.GetByTestId(id).CountAsync(), $"{id} renders once");
            Assert.AreEqual(1, await page.Locator($"[data-testid='document-header'] [data-testid='{id}']").CountAsync(), $"{id} lives in the header");
        }

        Assert.AreEqual(0, await page.Locator(".tm-document-editor__ribbon-group--quick-access").CountAsync(), "the ribbon quick-access group is gone while the header is shown");
        Assert.AreEqual(0, await page.Locator(".tm-document-editor__page-header").CountAsync(), "the in-surface page header is gone");
        Assert.AreNotEqual(string.Empty, (await page.Locator("[data-testid='document-header'] .tm-document-editor__document-title").InnerTextAsync()).Trim(), "the title is shown");

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.TypeAsync("Q");
        await Assertions.Expect(page.GetByTestId("document-header-status")).ToContainTextAsync("Unsaved", new() { Timeout = 10_000 });
        await page.GetByTestId("document-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-header-status")).ToContainTextAsync("Last saved", new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator("[data-testid='document-status-bar'] [data-testid='document-last-saved']").CountAsync(), "the status bar no longer repeats the save time");

        await page.GetByTestId("document-header-search").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        AssertNoClientErrors(errors, "header");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Desktop: Outline and Pages live in the left column; a heading navigates; the column folds into a rail and back")]
    public async Task NavigationColumn_ShowsOutlineAndPages_AndFolds()
    {
        // The demo caps the editor at ~1280 px, below the width that leaves the page its room next to both columns, so the column
        // starts folded; unfold it like a reader would.
        var (page, errors) = await OpenAsync("contract-demo", 1920, 1080, touch: false, "/document-editor");
        Assert.AreEqual("desktop", (await ReadShellAsync(page)).Layout);
        await page.GetByTestId("document-navigation-expand").ClickAsync();

        Assert.AreEqual(0, await page.GetByTestId("document-side-panel-tab-outline").CountAsync(), "Outline is not an inspector tab on a desktop container");
        Assert.AreEqual(0, await page.GetByTestId("document-side-panel-tab-pages").CountAsync());
        await Assertions.Expect(page.Locator("[data-region='nav'] [data-testid='document-outline-item']").First).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await Assertions.Expect(page.Locator("[data-region='nav'] [data-testid='document-page-navigator-item']").First).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var tabs = await page.Locator("[data-region='inspector'] [role='tab']").AllInnerTextsAsync();
        CollectionAssert.AreEqual(new[] { "Properties", "Comments", "Revisions", "Versions" }, tabs.Select(text => text.Split('\n')[0].Trim()).ToArray(), "the inspector shows the four text tabs");

        var navWidth = (await page.Locator("[data-region='nav']").BoundingBoxAsync())!.Width;
        await page.GetByTestId("document-navigation-collapse").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-navigation-expand")).ToBeVisibleAsync();
        Assert.IsTrue((await page.Locator("[data-region='nav']").BoundingBoxAsync())!.Width < navWidth / 2, "the folded column is a narrow rail");
        await page.GetByTestId("document-navigation-expand").ClickAsync();
        await Assertions.Expect(page.Locator("[data-region='nav'] [data-testid='document-outline-item']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await page.GetByTestId("document-navigation-search").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        AssertNoClientErrors(errors, "navigation column");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A desktop container narrower than the wide step folds the navigation column into its rail until the reader unfolds it (and follows the width both ways)")]
    public async Task NavigationColumn_StartsFolded_InANarrowDesktopContainer_AndTheReaderWins()
    {
        var (page, errors) = await OpenAsync("contract-demo", 1440, 900, touch: false, "/document-editor");
        var probe = await ReadShellAsync(page);
        Assert.AreEqual("desktop", probe.Layout);
        Assert.IsTrue(probe.EditorClientWidth < 1546, $"the demo's container at a 1440 px viewport cannot hold column + A4 page + inspector ({probe.EditorClientWidth})");

        await Assertions.Expect(page.GetByTestId("document-navigation-expand")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        Assert.AreEqual("true", await page.Locator("[data-region='nav']").GetAttributeAsync("data-collapsed"));
        Assert.IsTrue((await page.Locator("[data-region='nav']").BoundingBoxAsync())!.Width < 80, "the folded column is a narrow rail");

        // The reader's choice sticks, whatever the width does afterwards.
        await page.GetByTestId("document-navigation-expand").ClickAsync();
        await Assertions.Expect(page.Locator("[data-region='nav'] [data-testid='document-outline-item']").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '1100px'; host.style.maxWidth = '1100px'; }");
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual("false", await page.Locator("[data-region='nav']").GetAttributeAsync("data-collapsed"), "an unfolded column stays unfolded");

        AssertNoClientErrors(errors, "narrow desktop navigation");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("ShowHeader=false brings the ribbon quick-access group back (the pre-redesign look minus the page header)")]
    public async Task ShowHeaderFalse_KeepsTheQuickAccessGroupInTheRibbon()
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, touch: false));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/canvas-engine-host?documentId={HistoryDocumentId}&showToolbar=true&preferLocalDraft=false&showHeader=false", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-ribbon-tab-review\"]')", 45_000);

        // The demo page only forwards the flag when it supports it; the contract under test is the markup either way.
        var headerCount = await page.GetByTestId("document-header").CountAsync();
        var quickAccess = await page.Locator(".tm-document-editor__ribbon-group--quick-access").CountAsync();
        Assert.AreEqual(1, headerCount + quickAccess, "exactly one of the header and the ribbon quick-access group owns Save / Undo / Redo");
        Assert.AreEqual(1, await page.GetByTestId("document-save").CountAsync());
        await context.CloseAsync();
    }

    [TestMethod]
    [Description("A forced LayoutMode wins over the container: Desktop in an 800 px host shows the column, Mobile in a 1280 px host is one column with no empty track")]
    public async Task ForcedLayoutMode_FollowsDataLayout_NotTheContainerWidth()
    {
        var (page, errors) = await OpenAsync("contract-demo", 1440, 900, touch: false, "/document-editor");

        await page.GetByTestId("document-editor-host-width").SelectOptionAsync("800px");
        await page.GetByTestId("document-editor-layout-mode").SelectOptionAsync("Desktop");
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout-forced') === 'true' && document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === 'desktop'", 15_000);
        await WaitForPredicateAsync(page, "() => document.querySelector(\"[data-region='nav']\")", 15_000);
        await page.WaitForTimeoutAsync(400);
        var forcedDesktop = await ReadShellAsync(page);
        Assert.AreEqual("nav,doc,inspector", string.Join(",", forcedDesktop.VisibleRegions), "a forced Desktop shows all three regions even in an 800 px host");
        Assert.IsTrue(forcedDesktop.NavRight <= forcedDesktop.DocLeft + 1 && forcedDesktop.DocRight <= forcedDesktop.InspectorLeft + 1, "the three regions sit in one row");

        await page.GetByTestId("document-editor-host-width").SelectOptionAsync("1280px");
        await page.GetByTestId("document-editor-layout-mode").SelectOptionAsync("Mobile");
        await WaitForPredicateAsync(page, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === 'mobile'", 15_000);
        await page.WaitForTimeoutAsync(400);
        var forcedMobile = await ReadShellAsync(page);
        Assert.AreEqual("doc", string.Join(",", forcedMobile.VisibleRegions), "a forced Mobile has the document only - its panels are sheets");
        var columns = await page.EvaluateAsync<int>("() => getComputedStyle(document.querySelector('.tm-document-editor__workspace')).gridTemplateColumns.split(' ').length");
        Assert.AreEqual(1, columns, "a forced Mobile has exactly one grid track (no empty column)");

        AssertNoClientErrors(errors, "forced modes");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("F6 lands on the SELECTED ribbon tab, shows a visible ring and keeps the focused control inside the viewport and its region")]
    public async Task F6_IntoTheRibbon_FocusesTheSelectedTab_WithAVisibleRing()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();

        // doc -> inspector -> header -> ribbon
        for (var i = 0; i < 3; i++)
        {
            await page.Keyboard.PressAsync("F6");
        }

        Assert.AreEqual("ribbon", await ActiveRegionAsync(page));
        Assert.AreEqual("document-ribbon-tab-review", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid')"), "the selected ribbon tab holds the focus");
        var ring = await page.EvaluateAsync<string[]>(
            """
            () => {
                const region = document.activeElement.closest('[data-focus-region]');
                const active = document.activeElement.getBoundingClientRect();
                const box = region.getBoundingClientRect();
                return [region.hasAttribute('data-region-focus') ? 'true' : 'false', getComputedStyle(region).outlineStyle,
                    String(active.top >= 0 && active.bottom <= innerHeight), String(active.left >= box.left - 1 && active.right <= box.right + 1)];
            }
            """);
        Assert.AreEqual("true", ring[0], "the entered region is marked");
        Assert.AreNotEqual("none", ring[1], "the marked region shows an outline");
        Assert.AreEqual("true", ring[2], "the focused control is inside the viewport");
        Assert.AreEqual("true", ring[3], "the focused control is inside its region");

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await WaitForPredicateAsync(page, "() => !document.querySelector('[data-region-focus]')", 5_000);

        // the inspector selects its active tab too
        await page.GetByTestId("document-side-panel-tab-comments").ClickAsync();
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("F6");
        Assert.AreEqual("document-side-panel-tab-comments", await page.EvaluateAsync<string>("() => document.activeElement?.getAttribute('data-testid')"));

        AssertNoClientErrors(errors, "F6 selected tab");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The active inspector tab is always inside the tab row at 1440 with the demo sidebar")]
    public async Task InspectorTabs_ActiveTabIsInsideTheTabRow()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);

        foreach (var tab in new[] { "properties", "comments", "revisions", "versions" })
        {
            var locator = page.GetByTestId($"document-side-panel-tab-{tab}");
            if (await locator.CountAsync() == 0) continue;
            await locator.ClickAsync();
            var inside = await page.EvaluateAsync<bool>(
                """
                () => {
                    const active = document.querySelector('.tm-document-side-panel__tab--active').getBoundingClientRect();
                    const row = document.querySelector('.tm-document-side-panel__tabs').getBoundingClientRect();
                    return active.left >= row.left - 1 && active.right <= row.right + 1 && active.top >= row.top - 1 && active.bottom <= row.bottom + 1;
                }
                """);
            Assert.IsTrue(inside, $"the active '{tab}' tab is fully inside the tab row");
        }

        AssertNoClientErrors(errors, "inspector tabs");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Tablet icon tabs sit in one row, and the close button sits in that row too: every inspector tab has the same top at 1180 touch")]
    public async Task TabletInspectorIconTabs_SitInOneRow()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1180, 820, touch: true);
        Assert.AreEqual("tablet", (await ReadShellAsync(page)).Layout);
        await page.GetByTestId("document-side-panel-edge-toggle").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-side-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var tops = await page.EvaluateAsync<double[]>("() => [...document.querySelectorAll(\"[data-region='inspector'] [role='tab']\")].map(t => Math.round(t.getBoundingClientRect().top))");
        Assert.IsTrue(tops.Length >= 4, $"the inspector has its tabs ({tops.Length})");
        Assert.AreEqual(1, tops.Distinct().Count(), $"all inspector tabs share one top: {string.Join(",", tops)}");
        var closeTop = await page.EvaluateAsync<double>("() => Math.round(document.querySelector(\"[data-testid='document-side-panel-close']\").getBoundingClientRect().top)");
        Assert.IsTrue(Math.Abs(closeTop - tops[0]) <= 8, $"the close button shares the tabs' row ({closeTop} vs {tops[0]})");

        AssertNoClientErrors(errors, "tablet icon tabs");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The ribbon height is published as --tm-document-ribbon-block-size and the sticky navigation parks below it")]
    public async Task StickyNavigation_ParksBelowTheRibbon()
    {
        var (page, errors) = await OpenAsync("contract-demo", 1440, 900, touch: false, "/document-editor");
        await page.GetByTestId("document-navigation-expand").ClickAsync();

        var probe = await page.EvaluateAsync<double[]>(
            """
            () => {
                const root = document.querySelector('.tm-document-editor');
                const published = parseFloat(getComputedStyle(root).getPropertyValue('--tm-document-ribbon-block-size'));
                const ribbon = document.querySelector('[data-focus-region="ribbon"]').getBoundingClientRect().height;
                const nav = document.querySelector('.tm-document-navigation');
                return [published, ribbon, parseFloat(getComputedStyle(nav).top)];
            }
            """);
        Assert.IsTrue(probe[0] > 40, $"the ribbon height is published ({probe[0]})");
        Assert.AreEqual(Math.Round(probe[1]), probe[0], 1.0, "the published value is the ribbon's height");
        Assert.IsTrue(probe[2] >= probe[0], $"the sticky column's top ({probe[2]}) clears the ribbon ({probe[0]})");

        AssertNoClientErrors(errors, "sticky navigation");
        await page.Context.CloseAsync();
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed record ShellProbe(
        string Layout, bool HeaderVisible, int SaveCount, string[] VisibleRegions,
        double InnerWidth, double ScrollWidth, bool PageOverflowsX,
        double EditorClientWidth, double EditorScrollWidth, bool EditorOverflowsX,
        double NavRight, double DocLeft, double DocRight, double DocBottom, double InspectorLeft, double InspectorTop, string[] Offenders,
        string HostOverflowX, double HostScrollWidth, double HostClientWidth, double PageRight);

    private static async Task<ShellProbe> ReadShellAsync(IPage page)
    {
        var json = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const root = document.querySelector('.tm-document-editor');
                const box = selector => { const el = document.querySelector(selector); return el && el.getClientRects().length ? el.getBoundingClientRect() : null; };
                const nav = box("[data-region='nav']"), doc = box("[data-region='doc']"), inspector = box("[data-region='inspector']");
                const regions = ['nav', 'doc', 'inspector'].filter(name => box(`[data-region='${name}']`));
                const docEl = document.documentElement;
                return {
                    layout: root.getAttribute('data-layout'),
                    headerVisible: !!box("[data-testid='document-header']"),
                    saveCount: document.querySelectorAll("[data-testid='document-save']").length,
                    visibleRegions: regions,
                    innerWidth: window.innerWidth, scrollWidth: docEl.scrollWidth,
                    pageOverflowsX: docEl.scrollWidth > window.innerWidth + 1,
                    editorClientWidth: root.clientWidth, editorScrollWidth: root.scrollWidth,
                    editorOverflowsX: root.scrollWidth > root.clientWidth + 1,
                    navRight: nav ? nav.right : 0, docLeft: doc ? doc.left : 0, docRight: doc ? doc.right : 0, docBottom: doc ? doc.bottom : 0,
                    inspectorLeft: inspector ? inspector.left : 0, inspectorTop: inspector ? inspector.top : 0,
                    hostOverflowX: (() => { const h = document.querySelector('[data-testid="document-canvas-engine-host"]'); return h ? getComputedStyle(h).overflowX : 'none'; })(),
                    hostScrollWidth: document.querySelector('[data-testid="document-canvas-engine-host"]')?.scrollWidth ?? 0,
                    hostClientWidth: document.querySelector('[data-testid="document-canvas-engine-host"]')?.clientWidth ?? 0,
                    pageRight: document.querySelector('[data-testid="document-canvas-page"]')?.getBoundingClientRect().right ?? 0,
                    offenders: [...root.querySelectorAll('*')]
                        .filter(el => {
                            if (!el.getClientRects().length || el.getBoundingClientRect().right <= root.getBoundingClientRect().right + 1) return false;
                            // clipped or scrolled by an ancestor inside the editor: not a page-level overflow
                            for (let a = el.parentElement; a && a !== root; a = a.parentElement) {
                                if (/(auto|scroll|hidden|clip)/.test(getComputedStyle(a).overflowX)) return false;
                            }
                            return true;
                        })
                        .map(el => ({ el, right: el.getBoundingClientRect().right }))
                        .sort((a, b) => b.right - a.right).slice(0, 6)
                        .map(o => `${o.el.tagName.toLowerCase()}.${String(o.el.className).split(' ')[0]}[${o.el.getAttribute('data-testid') ?? ''}]@${Math.round(o.right)}`),
                };
            }
            """);
        return new ShellProbe(
            json.GetProperty("layout").GetString()!,
            json.GetProperty("headerVisible").GetBoolean(),
            json.GetProperty("saveCount").GetInt32(),
            json.GetProperty("visibleRegions").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            json.GetProperty("innerWidth").GetDouble(),
            json.GetProperty("scrollWidth").GetDouble(),
            json.GetProperty("pageOverflowsX").GetBoolean(),
            json.GetProperty("editorClientWidth").GetDouble(),
            json.GetProperty("editorScrollWidth").GetDouble(),
            json.GetProperty("editorOverflowsX").GetBoolean(),
            json.GetProperty("navRight").GetDouble(),
            json.GetProperty("docLeft").GetDouble(),
            json.GetProperty("docRight").GetDouble(),
            json.GetProperty("docBottom").GetDouble(),
            json.GetProperty("inspectorLeft").GetDouble(),
            json.GetProperty("inspectorTop").GetDouble(),
            json.GetProperty("offenders").EnumerateArray().Select(e => e.GetString()!).ToArray(),
            json.GetProperty("hostOverflowX").GetString()!,
            json.GetProperty("hostScrollWidth").GetDouble(),
            json.GetProperty("hostClientWidth").GetDouble(),
            json.GetProperty("pageRight").GetDouble());
    }

    /// <summary>The F6 landmark that currently holds the keyboard focus ("none" when the focus sits outside every landmark).</summary>
    private static async Task<string> ActiveRegionAsync(IPage page)
        => await page.EvaluateAsync<string>("() => document.activeElement?.closest('[data-focus-region]')?.getAttribute('data-focus-region') ?? 'none'");

    private static void AssertNoClientErrors(List<string> errors, string context)
        => Assert.AreEqual(0, errors.Count, $"{context}: no console or page error expected, got: {string.Join(" | ", errors.Take(5))}");

    private async Task<(IPage Page, List<string> Errors)> OpenAsync(
        string documentId, int width, int height, bool touch, string route = "/canvas-engine-host")
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
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

        var url = route == "/document-editor" && documentId == "contract-demo"
            ? $"{BaseUrl}{route}?documentId={documentId}"
            : OpenUrl(BaseUrl, documentId, route);
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(500);
        return (page, errors);
    }
}

/// <summary>
/// Phase-3 "after" screenshots for the UX review: the shell at 1440 (three regions + header), 1024 touch (document +
/// inspector), 390 and 320 (one column) in light and dark. Gated by <c>TM_WRITE_BASELINES</c> like every generator; the PNGs
/// land in <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-3/</c> (override the folder name with
/// <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase3CaptureE2ETests : BaselineGeneratorTestBase
{
    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-3" : target.Trim();
            var directory = Path.Combine(
                FindRepositoryRootDirectory(),
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redesign", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("The shell at the four widths in light and dark, plus the narrow-host case and the folded navigation column")]
    public async Task CaptureShell()
    {
        foreach (var (name, width, height, touch) in SurfaceViewports)
        {
            var page = await OpenAsync("contract-demo", width, height, touch);
            await ApplyThemeAsync(page, dark: false, indigo: false);
            await Shot(page, $"shell-light-{name}.png");
            await HeaderShot(page, $"header-light-{name}.png");
            await ApplyThemeAsync(page, dark: true, indigo: false);
            await Shot(page, $"shell-dark-{name}.png");
            await HeaderShot(page, $"header-dark-{name}.png");
            if (width < 640)
            {
                // The dirty state on a phone: the save status stays on the first line next to the title.
                await ApplyThemeAsync(page, dark: false, indigo: false);
                await page.GetByTestId("document-canvas-engine-host").ClickAsync();
                await page.Keyboard.TypeAsync("x");
                await Assertions.Expect(page.GetByTestId("document-header-status")).ToContainTextAsync("Unsaved", new() { Timeout = 10_000 });
                await HeaderShot(page, $"header-dirty-light-{name}.png");
            }

            await page.Context.CloseAsync();
        }

        var narrow = await OpenAsync("contract-demo", 1440, 900, touch: false);
        await ApplyThemeAsync(narrow, dark: false, indigo: false);
        await narrow.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = '420px'; host.style.maxWidth = '420px'; }");
        await WaitForPredicateAsync(narrow, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === 'mobile'", 10_000);
        await Shot(narrow, "shell-narrow-host-420-on-1440.png");
        await narrow.EvaluateAsync("() => { const host = document.querySelector('.tm-document-editor-host'); host.style.width = ''; host.style.maxWidth = ''; }");
        await WaitForPredicateAsync(narrow, "() => document.querySelector('.tm-document-editor')?.getAttribute('data-layout') === 'desktop'", 10_000);
        // 1440 px leaves the demo's editor below the wide step: the column starts folded into its rail; unfold it for the second shot.
        await Shot(narrow, "shell-navigation-folded-1440.png");
        await narrow.GetByTestId("document-navigation-expand").ClickAsync();
        await narrow.WaitForTimeoutAsync(300);
        await Shot(narrow, "shell-navigation-open-1440.png");
        await narrow.Context.CloseAsync();

        // A wide container shows the three regions open by themselves.
        var wide = await OpenAsync("contract-demo", 1920, 1080, touch: false);
        await ApplyThemeAsync(wide, dark: false, indigo: false);
        await Shot(wide, "shell-light-1920x1080.png");
        await ApplyThemeAsync(wide, dark: true, indigo: false);
        await Shot(wide, "shell-dark-1920x1080.png");
        await wide.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync(string documentId, int width, int height, bool touch)
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId={documentId}", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(500);
        return page;
    }

    /// <summary>
    /// The top of the editor (header, ribbon, the regions) as the reader sees it: the demo's own sticky / fixed bars are hidden so
    /// they cannot cover the header, and the frame is clipped to the first ~900 px of the host (the page below is a long canvas).
    /// </summary>
    private static async Task Shot(IPage page, string fileName)
    {
        var host = page.GetByTestId("document-editor-host");
        await host.ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync(
            """
            () => {
                const host = document.querySelector('[data-testid="document-editor-host"]');
                window.scrollTo(0, window.scrollY + host.getBoundingClientRect().top - 8);
                for (const el of document.querySelectorAll('body *')) {
                    if (host.contains(el) || el.contains(host)) continue;
                    const position = getComputedStyle(el).position;
                    if (position === 'fixed' || position === 'sticky') el.style.visibility = 'hidden';
                }
            }
            """);
        await page.WaitForTimeoutAsync(400);
        var box = (await host.BoundingBoxAsync())!;
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
            Clip = new Clip { X = (float)box.X, Y = (float)box.Y, Width = (float)box.Width, Height = (float)Math.Min(box.Height, 900) }
        });
    }

    private static async Task HeaderShot(IPage page, string fileName)
    {
        await page.GetByTestId("document-header").ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(300);
        await page.GetByTestId("document-header").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png
        });
    }
}
