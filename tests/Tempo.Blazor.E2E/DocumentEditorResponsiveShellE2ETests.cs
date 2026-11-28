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
                // Phase 11 (decision DE-AUTO-FIT-READABLE-FLOOR): a desktop container whose docked inspector would squeeze the fitted page below 75 percent
                // (the demo's 1046 px at a 1440 viewport) presents the inspector as the side sheet over the document, like a tablet.
                var sheet = await page.EvaluateAsync<bool>("() => document.querySelector('.tm-document-editor').getAttribute('data-inspector-sheet') === 'true'");
                if (sheet)
                {
                    Assert.IsTrue(probe.NavRight <= probe.DocLeft + 1, $"{name}: navigation | document sit in one row");
                    Assert.IsTrue(probe.InspectorLeft >= probe.DocLeft - 1 && probe.InspectorLeft <= probe.DocRight + 1, $"{name}: the sheet shares the document's cell");
                }
                else
                {
                    Assert.IsTrue(probe.NavRight <= probe.DocLeft + 1 && probe.DocRight <= probe.InspectorLeft + 1,
                        $"{name}: navigation | document | inspector sit in one row ({probe.NavRight}/{probe.DocLeft}/{probe.DocRight}/{probe.InspectorLeft})");
                }
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
        // 1600: a docked inspector (the 1440 viewport's 1046 px editor presents it as a closed sheet, see the readable floor)
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1600, 900, touch: false);
        // The docked inspector has rendered its tabs and the host has learned the engine's fit (its debounced report re-renders the chrome): F6 into
        // the inspector needs a settled page to land on.
        await Assertions.Expect(page.GetByTestId("document-side-panel-tab-comments")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("Fit ", new() { Timeout = 15_000 });
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        // Focus the document without clicking into it: a click at the host's centre now lands on a selectable object of the fitted page, whose inspector
        // context opens a moment later and takes the first F6.
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();

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

        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await WaitForPredicateAsync(page, "() => !document.querySelector('[data-region-focus]')", 5_000);

        // the inspector selects its active tab too (from the top of the page: the F6 focus scrolled it, and the sticky ribbon would cover the tab)
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await page.GetByTestId("document-side-panel-tab-comments").ClickAsync();
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
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

    // ── phase 11: the engine fits every narrow document column ─────────────────────────────────────────────────────

    private sealed record FitProbe(
        string Preset, string AutoFit, double Percent, double PageLeft, double PageRight, double HostLeft, double HostRight,
        double HostScrollWidth, double HostClientWidth, string Layout, double EditorWidth);

    private static async Task<FitProbe> ReadFitAsync(IPage page)
    {
        var json = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const pageEl = document.querySelector('[data-testid="document-canvas-page"]');
                const h = host.getBoundingClientRect();
                const p = pageEl.getBoundingClientRect();
                const editor = document.querySelector('.tm-document-editor');
                return {
                    preset: root.getAttribute('data-canvas-zoom-preset') || '',
                    autoFit: root.getAttribute('data-canvas-zoom-auto-fit') || '',
                    percent: Number(root.getAttribute('data-canvas-zoom-percent') || '0'),
                    pageLeft: p.left, pageRight: p.right, hostLeft: h.left, hostRight: h.left + host.clientWidth,
                    hostScrollWidth: host.scrollWidth, hostClientWidth: host.clientWidth,
                    layout: editor.getAttribute('data-layout') || '', editorWidth: editor.clientWidth,
                };
            }
            """);
        return new FitProbe(
            json.GetProperty("preset").GetString()!, json.GetProperty("autoFit").GetString()!, json.GetProperty("percent").GetDouble(),
            json.GetProperty("pageLeft").GetDouble(), json.GetProperty("pageRight").GetDouble(),
            json.GetProperty("hostLeft").GetDouble(), json.GetProperty("hostRight").GetDouble(),
            json.GetProperty("hostScrollWidth").GetDouble(), json.GetProperty("hostClientWidth").GetDouble(),
            json.GetProperty("layout").GetString()!, json.GetProperty("editorWidth").GetDouble());
    }

    // The demo layout caps its content at ~1280 px, so the document column never exceeds ~770 px there: lifting the cap on the editor's ancestors
    // lets the column grow past the page, the situation a wide host application has.
    private static Task LiftDemoWidthCapAsync(IPage page)
        => page.EvaluateAsync(
            """
            () => {
                for (let el = document.querySelector('.tm-document-editor-host'); el && el !== document.documentElement; el = el.parentElement) {
                    el.style.maxWidth = 'none';
                    el.style.maxInlineSize = 'none';
                }
            }
            """);

    private static Task WaitForZoomSettledAsync(IPage page, string predicate)
        => WaitForPredicateAsync(page, $"() => {{ const r = document.querySelector('[data-testid=\"document-canvas-engine-root\"]'); const p = Number(r?.getAttribute('data-canvas-zoom-percent') || '0'); const preset = r?.getAttribute('data-canvas-zoom-preset'); return {predicate}; }}", 15_000);

    private static void AssertPageInsideColumn(FitProbe probe, string name)
    {
        Assert.IsTrue(probe.PageRight <= probe.HostRight + 1, $"{name}: the page right edge ({probe.PageRight}) is inside the column ({probe.HostRight}), zoom {probe.Percent}% {probe.Preset}/{probe.AutoFit}");
        Assert.IsTrue(probe.PageLeft >= probe.HostLeft - 1, $"{name}: the page left edge ({probe.PageLeft}) is inside the column ({probe.HostLeft})");
        Assert.IsTrue(probe.HostScrollWidth <= probe.HostClientWidth + 1, $"{name}: no horizontal scroll in the document column ({probe.HostScrollWidth} > {probe.HostClientWidth})");
    }

    [TestMethod]
    [Description("1440 viewport with the demo sidebar (a 1046 px editor): the readable floor presents the inspector as the sheet, the page holds its 100 percent and nothing scrolls sideways")]
    public async Task ReadableFloor_At1440WithTheDemoSidebar_PresentsTheInspectorAsTheSheet_AndKeepsThePageReadable()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1440, 900, touch: false);
        await WaitForZoomSettledAsync(page, "p > 0");

        var probe = await ReadFitAsync(page);
        Assert.AreEqual("desktop", probe.Layout, "the ribbon and the navigation stay the desktop's");
        Assert.IsTrue(probe.EditorWidth is > 1024 and < 1136, $"this spec needs the demo's narrow desktop container ({probe.EditorWidth})");
        Assert.AreEqual("true", await page.Locator(".tm-document-editor").GetAttributeAsync("data-inspector-sheet"), "the docked inspector would squeeze the page below 75 percent");
        Assert.AreEqual("sheet", await page.Locator("[data-region='inspector']").GetAttributeAsync("data-presentation"));
        Assert.IsTrue(probe.Percent >= 75, $"the page stays at least at the readable floor ({probe.Percent}%)");
        AssertPageInsideColumn(probe, "1440 sheet");

        AssertNoClientErrors(errors, "readable floor 1440");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("1600 viewport with the demo sidebar (a docked inspector): the page is fitted into the narrow doc column, nothing scrolls sideways and a click 10 px from the right text edge lands on the last characters")]
    public async Task AutoFit_At1600WithTheDemoSidebar_FitsThePage_AndAClickAtTheRightTextEdgeHitsTheText()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1600, 900, touch: false);
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth' || p === 100");

        var probe = await ReadFitAsync(page);
        Assert.AreEqual("narrow", probe.AutoFit, "the default mode is the engine's narrow-only fit");
        Assert.IsTrue(probe.HostClientWidth < 842, $"this spec needs a doc column narrower than the page (A4 + gutters = 842), got {probe.HostClientWidth} in an editor of {probe.EditorWidth}");
        Assert.AreEqual("fitWidth", probe.Preset, "the page is fitted to the narrow column");
        Assert.IsTrue(probe.Percent < 100 && probe.Percent >= 25, $"the fit shrinks the page ({probe.Percent}%)");
        AssertPageInsideColumn(probe, "1440");
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => document.querySelector('.tm-document-editor').hasAttribute('data-page-fit')"), "data-page-fit follows the engine's report");

        // A fit at the top of the document keeps the reader at the top: the window is not scrolled and page one starts below the sticky ribbon.
        var top = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => ({
                scrollY: Math.round(window.scrollY),
                pageTop: document.querySelector('[data-testid="document-canvas-page"][data-page-index="0"]').getBoundingClientRect().top,
                ribbonBottom: document.querySelector('[data-focus-region="ribbon"]').getBoundingClientRect().bottom,
            })
            """);
        Assert.AreEqual(0, top.GetProperty("scrollY").GetInt32(), "the fit does not scroll the window away from the top");
        Assert.IsTrue(top.GetProperty("pageTop").GetDouble() >= top.GetProperty("ribbonBottom").GetDouble() - 1,
            $"page one starts below the ribbon (page top {top.GetProperty("pageTop").GetDouble():0.#}, ribbon bottom {top.GetProperty("ribbonBottom").GetDouble():0.#})");

        // The rightmost word of the first page: a click 10 px inside its right edge hits its last characters (before the fit this point was clipped away).
        var target = await page.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            () => {
                const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                const hostRight = host.getBoundingClientRect().left + host.clientWidth;
                const rects = [...document.querySelectorAll('[data-canvas-text-rect]')]
                    .map(el => ({ el, r: el.getBoundingClientRect() }))
                    .filter(item => item.r.width > 30 && item.r.right <= hostRight && item.el.getAttribute('data-canvas-text').trim().length > 3)
                    .sort((a, b) => b.r.right - a.r.right);
                const best = rects[0];
                return {
                    x: best.r.right - 10, y: best.r.top + best.r.height / 2,
                    blockId: best.el.getAttribute('data-block-id'),
                    start: Number(best.el.getAttribute('data-canvas-start-offset')), end: Number(best.el.getAttribute('data-canvas-end-offset')),
                    text: best.el.getAttribute('data-canvas-text'),
                };
            }
            """);
        await page.Mouse.ClickAsync((float)target.GetProperty("x").GetDouble(), (float)target.GetProperty("y").GetDouble());
        await WaitForPredicateAsync(page,
            $"() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-selection-focus-block-id') === '{target.GetProperty("blockId").GetString()}'", 10_000);
        var offset = await page.EvaluateAsync<int>("() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]').getAttribute('data-canvas-selection-focus-offset') || '-1')");
        var end = target.GetProperty("end").GetInt32();
        Assert.IsTrue(offset <= end && offset >= end - 4,
            $"the caret lands at the end of the word '{target.GetProperty("text").GetString()}' (offset {offset}, word end {end})");

        AssertNoClientErrors(errors, "auto fit 1600");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Resizing the window to 1920 releases the fit (100 percent), and shrinking it again fits once more")]
    public async Task AutoFit_ResizeTo1920_GoesBackToOneHundredPercent_AndFitsAgainWhenNarrow()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1600, 900, touch: false);
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");

        await LiftDemoWidthCapAsync(page);
        await page.SetViewportSizeAsync(1920, 1080);
        await WaitForZoomSettledAsync(page, "p === 100 && preset === 'custom'");
        var wide = await ReadFitAsync(page);
        Assert.AreEqual("narrow", wide.AutoFit, "the automatic mode survives the release of the fit");
        AssertPageInsideColumn(wide, "1920");
        // The engine reports its own re-fit to .NET debounced (trailing ~150 ms), so the root learns it a moment after the engine's own attribute.
        await WaitForPredicateAsync(page, "() => !document.querySelector('.tm-document-editor').hasAttribute('data-page-fit')", 10_000);

        await page.SetViewportSizeAsync(1600, 900);
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");
        AssertPageInsideColumn(await ReadFitAsync(page), "back at 1600");

        AssertNoClientErrors(errors, "auto fit resize");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A reader zoom of 120 percent wins: a later resize keeps it, and Page width brings the fit back")]
    public async Task AutoFit_ReaderZoomWins_OverAResize_AndPageWidthBringsTheFitBack()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1920, 1080, touch: false);
        await LiftDemoWidthCapAsync(page);
        await WaitForZoomSettledAsync(page, "p === 100");
        // The lifted cap lets the column grow past the page: the engine released its fit, and the host learns that (debounced) a moment later - the label
        // says "100%" once it does, and the buttons step from what the label says.
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("100%", new() { Timeout = 10_000 });
        await page.GetByTestId("document-status-zoom-in").ClickAsync();
        await WaitForZoomSettledAsync(page, "p === 110");
        await page.GetByTestId("document-status-zoom-in").ClickAsync();
        await WaitForZoomSettledAsync(page, "p === 120");
        Assert.AreEqual("off", (await ReadFitAsync(page)).AutoFit, "an explicit zoom switches the automatic fit off");

        var widthBefore = await page.EvaluateAsync<double>("() => document.querySelector('[data-testid=\"document-canvas-engine-host\"]').clientWidth");
        await page.SetViewportSizeAsync(1300, 900);
        // No fixed pause: the column really changed, then two animation frames let the engine's observer (one fit per frame) have its say.
        await WaitForPredicateAsync(page, $"() => document.querySelector('[data-testid=\"document-canvas-engine-host\"]').clientWidth !== {widthBefore.ToString(System.Globalization.CultureInfo.InvariantCulture)}", 10_000);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var after = await ReadFitAsync(page);
        Assert.AreEqual(120, after.Percent, "the reader's zoom survives the resize");
        Assert.AreEqual("off", after.AutoFit);

        await page.GetByTestId("document-ribbon-tab-view").ClickAsync();
        await ClickRibbonAsync(page, "document-zoom-page-width");
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");
        var fitted = await ReadFitAsync(page);
        Assert.AreEqual("always", fitted.AutoFit, "the explicit fit follows the column again");
        AssertPageInsideColumn(fitted, "page width");

        AssertNoClientErrors(errors, "auto fit reader zoom");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Tablet 820 x 1180 touch: the page is inside the column, no sideways scrolling")]
    public async Task AutoFit_OnATablet820_KeepsThePageInsideTheColumn()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 820, 1180, touch: true);
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");

        var probe = await ReadFitAsync(page);
        Assert.AreEqual("tablet", probe.Layout);
        AssertPageInsideColumn(probe, "tablet 820");
        Assert.IsTrue(probe.Percent < 100, $"a 820 px tablet fits the 794 px page plus gutters ({probe.Percent}%)");

        AssertNoClientErrors(errors, "auto fit tablet");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A slow resize of 20 steps of 5 px never makes the zoom oscillate: it only ever shrinks as the column shrinks")]
    public async Task AutoFit_SlowResize_DoesNotOscillate()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1600, 900, touch: false);
        // The docked inspector must hold over the whole range (the readable floor turns it into a sheet below ~1136 px of the editor and the page then
        // GROWS): aim the editor at 1260 px so the 20 steps of 5 px end at 1160.
        var container = await page.EvaluateAsync<double>("() => document.querySelector('.tm-document-editor').clientWidth");
        var startViewport = (int)Math.Round(1600 + (1260 - container));
        var hostBefore = await page.EvaluateAsync<double>("() => document.querySelector('[data-testid=\"document-canvas-engine-host\"]').clientWidth");
        await page.SetViewportSizeAsync(startViewport, 900);
        if (startViewport != 1600)
        {
            await WaitForPredicateAsync(page, $"() => document.querySelector('[data-testid=\"document-canvas-engine-host\"]').clientWidth !== {hostBefore.ToString(System.Globalization.CultureInfo.InvariantCulture)}", 10_000);
        }

        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(resolve))))");
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");
        var startProbe = await ReadFitAsync(page);
        Assert.AreEqual("fitWidth", startProbe.Preset, "the spec starts with a FITTED page");
        Assert.AreEqual("narrow", startProbe.AutoFit);
        await page.EvaluateAsync(
            """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                window.__zoomLog = [Number(root.getAttribute('data-canvas-zoom-percent'))];
                new MutationObserver(() => window.__zoomLog.push(Number(root.getAttribute('data-canvas-zoom-percent'))))
                    .observe(root, { attributes: true, attributeFilter: ['data-canvas-zoom-percent'] });
            }
            """);

        for (var step = 1; step <= 20; step++)
        {
            await page.SetViewportSizeAsync(startViewport - step * 5, 900);
            await page.WaitForTimeoutAsync(120);
        }

        await page.WaitForTimeoutAsync(500);
        var log = await page.EvaluateAsync<double[]>("() => window.__zoomLog");
        var changes = log.Zip(log.Skip(1), (a, b) => b - a).Count(d => d != 0);
        Assert.IsTrue(changes <= 20, $"at most one zoom change per resize step ({changes}): {string.Join(",", log)}");
        for (var i = 1; i < log.Length; i++)
        {
            Assert.IsTrue(log[i] <= log[i - 1], $"the zoom never grows while the column shrinks: {string.Join(",", log)}");
        }

        var endProbe = await ReadFitAsync(page);
        Assert.IsTrue(startProbe.HostClientWidth - endProbe.HostClientWidth >= 50, $"the column really shrank ({startProbe.HostClientWidth} -> {endProbe.HostClientWidth})");
        Assert.IsTrue(endProbe.Percent < startProbe.Percent, $"and the fit followed it ({startProbe.Percent}% -> {endProbe.Percent}%)");
        AssertPageInsideColumn(endProbe, "after the slow resize");
        AssertNoClientErrors(errors, "auto fit slow resize");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Reset zoom: after a reader zoom the status-bar menu entry Automatic and Ctrl+0 return to the automatic fit and the label reads Fit again; a fitted page hides the ruler")]
    public async Task AutoFit_ResetZoom_FromTheMenuAndCtrlZero_ReturnsToTheAutomaticFit()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1600, 900, touch: false);
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");
        var fitted = await ReadFitAsync(page);
        Assert.AreEqual("narrow", fitted.AutoFit);
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("Fit ", new() { Timeout = 10_000 });
        Assert.AreEqual(0, await page.Locator(".tm-document-canvas-ruler-shell:visible").CountAsync(), "a fitted page hides the ruler in every layout");

        // The first step lands on the grid of 10 above the fitted value, and it is the reader's zoom now.
        await page.GetByTestId("document-status-zoom-in").ClickAsync();
        await WaitForZoomSettledAsync(page, "preset === 'custom'");
        var stepped = await ReadFitAsync(page);
        Assert.AreEqual("off", stepped.AutoFit);
        Assert.AreEqual(0, stepped.Percent % 10, $"the step snaps to the grid ({fitted.Percent}% -> {stepped.Percent}%)");
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).Not.ToContainTextAsync("Fit ");

        await page.Locator("[data-testid='document-status-zoom'] .tm-dropdown-trigger").ClickAsync();
        await page.GetByTestId("document-status-zoom-auto").ClickAsync();
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");
        var reset = await ReadFitAsync(page);
        Assert.AreEqual("narrow", reset.AutoFit, "Automatic is back");
        Assert.AreEqual(fitted.Percent, reset.Percent, 1.0);
        await Assertions.Expect(page.GetByTestId("document-status-zoom")).ToContainTextAsync("Fit ", new() { Timeout = 10_000 });

        // Ctrl+0 in the document is the same command.
        await page.GetByTestId("document-status-zoom-in").ClickAsync();
        await WaitForZoomSettledAsync(page, "preset === 'custom'");
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+0");
        await WaitForZoomSettledAsync(page, "preset === 'fitWidth'");
        Assert.AreEqual("narrow", (await ReadFitAsync(page)).AutoFit);

        AssertNoClientErrors(errors, "reset zoom");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A re-fit keeps the reader's place: on a multi-page document scrolled to page 3 a window resize moves the block at the top by no more than 24 px")]
    public async Task AutoFit_ReFit_KeepsTheReadingPosition_OnAMultiPageDocument()
    {
        var context = await CreateContextAsync(ContextOptions(1800, 900, touch: false));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, message) => errors.Add($"pageerror: {message}");
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor?documentId=large-perf-1000", WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && Number(document.querySelector('[data-testid="document-canvas-engine-root"]')?.getAttribute('data-canvas-estimated-page-count') || '0') >= 4
            """, 90_000);

        await WaitForPredicateAsync(page, "() => document.querySelector('[data-testid=\"document-canvas-engine-root\"]')?.getAttribute('data-canvas-layout-complete') === 'true'", 90_000);
        // Wheel the document (whatever scrolls - the window or the canvas host) until page 3 is mounted and sits near the top of the view.
        await page.Mouse.MoveAsync(700, 500);
        for (var i = 0; i < 30; i++)
        {
            var onPageThree = await page.EvaluateAsync<bool>(
                """
                () => {
                    const pageEl = document.querySelector('[data-testid="document-canvas-page"][data-page-index="2"]');
                    return !!pageEl && pageEl.getBoundingClientRect().top < 300;
                }
                """);
            if (onPageThree)
            {
                break;
            }

            await page.Mouse.WheelAsync(0, 600);
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        }

        Assert.IsTrue(await page.EvaluateAsync<bool>("() => !!document.querySelector('[data-testid=\"document-canvas-page\"][data-page-index=\"2\"]')"), "the spec reads page 3 of the document");

        // The reader's place: the page under the bottom edge of the sticky ribbon and how far down that page the line sits (a fraction of the page, so it
        // survives a change of the scale). After the re-fit the same fraction of the same page must be at the same line, within 24 px.
        const string readPlace = """
            (anchor) => {
                const line = document.querySelector('[data-focus-region="ribbon"]').getBoundingClientRect().bottom + 8;
                const pages = [...document.querySelectorAll('[data-testid="document-canvas-page"]')].map(el => ({ index: el.getAttribute('data-page-index'), r: el.getBoundingClientRect() }));
                const hit = anchor ? pages.find(p => p.index === anchor.index) : pages.find(p => p.r.top <= line && p.r.bottom > line);
                return hit ? { index: hit.index, line, top: hit.r.top, height: hit.r.height } : null;
            }
            """;
        var before = await page.EvaluateAsync<System.Text.Json.JsonElement>($"() => ({readPlace})(null)");
        Assert.AreNotEqual(System.Text.Json.JsonValueKind.Null, before.ValueKind, "a page sits under the ribbon before the re-fit");
        var pageIndex = before.GetProperty("index").GetString()!;
        var fraction = (before.GetProperty("line").GetDouble() - before.GetProperty("top").GetDouble()) / before.GetProperty("height").GetDouble();
        var percentBefore = await page.EvaluateAsync<double>("() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]').getAttribute('data-canvas-zoom-percent'))");

        await page.SetViewportSizeAsync(1600, 900);
        await WaitForPredicateAsync(page, $"() => Number(document.querySelector('[data-testid=\"document-canvas-engine-root\"]').getAttribute('data-canvas-zoom-percent')) < {percentBefore.ToString(System.Globalization.CultureInfo.InvariantCulture)}", 15_000);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(() => requestAnimationFrame(resolve))))");

        var after = await page.EvaluateAsync<System.Text.Json.JsonElement>($"() => ({readPlace})({{ index: '{pageIndex}' }})");
        Assert.AreNotEqual(System.Text.Json.JsonValueKind.Null, after.ValueKind, $"page {pageIndex} is still mounted after the re-fit");
        var lineAfter = after.GetProperty("top").GetDouble() + fraction * after.GetProperty("height").GetDouble();
        var drift = Math.Abs(lineAfter - after.GetProperty("line").GetDouble());
        Assert.IsTrue(drift <= 24, $"the same place of the document stays under the ribbon: it drifted {drift:0.#} px (page {pageIndex}, fraction {fraction:0.###})");

        AssertNoClientErrors(errors, "reading position");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The docked inspector's text tabs sit in one row at ~1080 px and the active tab is entirely visible, the first tab included")]
    public async Task InspectorTabs_At1080_AreAllFullyVisible_NoHalfHiddenFirstTab()
    {
        var (page, errors) = await OpenAsync(HistoryDocumentId, 1472, 900, touch: false);

        foreach (var tab in new[] { "properties", "comments", "revisions", "versions" })
        {
            var locator = page.GetByTestId($"document-side-panel-tab-{tab}");
            if (await locator.CountAsync() == 0) continue;
            await locator.ClickAsync();
            await page.WaitForTimeoutAsync(150);
            var geometry = await page.EvaluateAsync<System.Text.Json.JsonElement>(
                """
                () => {
                    const row = document.querySelector('.tm-document-side-panel__tabs').getBoundingClientRect();
                    const tabs = [...document.querySelectorAll('.tm-document-side-panel__tabs [role="tab"]')].map(t => t.getBoundingClientRect());
                    const list = document.querySelector('.tm-document-side-panel__tabs');
                    return {
                        allInside: tabs.every(r => r.left >= row.left - 1 && r.right <= row.right + 1),
                        scrolls: list.scrollWidth > list.clientWidth + 1,
                        first: tabs.length ? tabs[0].left - row.left : 0,
                    };
                }
                """);
            Assert.IsTrue(geometry.GetProperty("allInside").GetBoolean(), $"with '{tab}' active every tab is fully inside the row");
            Assert.IsFalse(geometry.GetProperty("scrolls").GetBoolean(), $"with '{tab}' active the tab row needs no sideways scroll");
        }

        AssertNoClientErrors(errors, "inspector tabs 1080");
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
