using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 3 of the Spreadsheet redesign — every dialog rides the ONE core dialog frame (G6).
/// <para>
/// The tests open each dialog in a real browser and assert the migration's contract: one
/// <c>.tm-modal-overlay</c> per dialog (no package backdrop), the dialog role plus <c>aria-modal</c> on the
/// frame's scope, the shared header/footer, Escape closing the surface, the focus returning to the grid,
/// the mobile presentation (a bottom sheet for the short forms, a fullscreen panel for the tall ones) and
/// the stacked case (two modals, Escape closes only the top one and the page dims once).
/// </para>
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as
/// <c>phase3-&lt;dialog&gt;--&lt;desktop|tablet|mobile|mobile-320&gt;</c> and are reviewed against
/// output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet-*-dialog--{desktop,tablet,mobile}.png,
/// spreadsheet-insert-image--*.png, spreadsheet-validation-error--*.png and spreadsheet-validation-warning--*.png.
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    private sealed record Phase3Dialog(string Name, string RootClass, bool TallForm, Func<IPage, Task> Open, bool Stacked = false);


    /// <summary>The dialogs of the package and how a user reaches each one in the demo.</summary>
    private Phase3Dialog[] Phase3Dialogs() =>
    [
        new("sort", "tm-spreadsheet-sort", TallForm: false, Open: OpenSortAsync),
        new("custom-filter", "tm-spreadsheet-custom-filter", TallForm: false, Open: OpenCustomFilterAsync),
        new("remove-duplicates", "tm-spreadsheet-dedup", TallForm: false, Open: page => ClickDataTabButtonAsync(page, "Remove duplicates")),
        new("special-paste", "tm-spreadsheet-pastespecial", TallForm: false, Open: OpenPasteSpecialAsync),
        new("text-to-columns", "tm-spreadsheet-t2c", TallForm: true, Open: page => ClickDataTabButtonAsync(page, "Text to columns")),
        new("hyperlink", "tm-spreadsheet-hyperlink", TallForm: false, Open: page => ClickInsertTabButtonAsync(page, "Insert link")),
        new("name-manager", "tm-spreadsheet-name-manager", TallForm: true, Open: page => ClickDataTabButtonAsync(page, "Name Manager")),
        new("named-range-edit", "tm-spreadsheet-named-range-edit", TallForm: false, Open: OpenNamedRangeEditAsync, Stacked: true),
        new("data-validation", "tm-dvd", TallForm: false, Open: page => ClickDataTabButtonAsync(page, "Data validation...")),
        new("format-cells", "tm-fcd", TallForm: true, Open: OpenFormatCellsAsync),
        new("insert-image", "tm-spreadsheet-insert-image", TallForm: false, Open: page => ClickInsertTabButtonAsync(page, "Insert image")),
    ];

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_Dialogs_Desktop1440_UseTheSharedFrame_AndRestoreFocusToTheGrid()
    {
        foreach (var dialog in Phase3Dialogs())
        {
            var page = await OpenPhase3DialogDemoAsync();
            await dialog.Open(page);

            var root = page.Locator($".tm-modal-overlay.{dialog.RootClass}");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForLayoutAsync(page, dialog.RootClass, "desktop", "tm-modal--dialog");

            // ONE surface, the core frame's: no package backdrop survives.
            (await page.Locator(".tm-spreadsheet-dialog-backdrop").CountAsync()).Should().Be(0,
                $"{dialog.Name}: the package no longer paints a dialog backdrop");
            (await page.Locator($".tm-modal-overlay.{dialog.RootClass}").CountAsync()).Should().Be(1,
                $"{dialog.Name}: exactly one surface of this dialog is open");

            var scope = root.Locator("[role='dialog'], [role='alertdialog']").First;
            Assert.AreEqual("true", await scope.GetAttributeAsync("aria-modal"), $"{dialog.Name}: aria-modal");

            var labelledBy = await scope.GetAttributeAsync("aria-labelledby");
            Assert.IsFalse(string.IsNullOrWhiteSpace(labelledBy), $"{dialog.Name}: the dialog needs a name");
            Assert.IsFalse(string.IsNullOrWhiteSpace(await page.Locator($"[id='{labelledBy}']").TextContentAsync()),
                $"{dialog.Name}: the name points at a non-empty title");

            // The shared chrome, not a per-dialog header/footer.
            Assert.AreEqual(1, await root.Locator(".tm-modal-header").CountAsync(), $"{dialog.Name}: one shared header");
            Assert.AreEqual(1, await root.Locator(".tm-modal-footer").CountAsync(), $"{dialog.Name}: one shared footer");
            Assert.AreEqual(1, await root.Locator(".tm-modal-close").CountAsync(), $"{dialog.Name}: one shared close affordance");

            // The desktop layout is the centred dialog, never a sheet or a fullscreen panel.
            StringAssert.Contains(await root.GetAttributeAsync("class") ?? string.Empty, "tm-modal--dialog",
                $"{dialog.Name}: a desktop viewport renders the centred dialog");
            Assert.AreEqual("desktop", await root.GetAttributeAsync("data-layout"), $"{dialog.Name}: data-layout");

            await ShotPhase2Async(page, $"phase3-{dialog.Name}--desktop");

            // Escape closes the surface. The editor opened OVER the manager returns the focus to its
            // opener (the manager is still modal); every other dialog restores it to the grid.
            await page.Keyboard.PressAsync("Escape");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await page.WaitForTimeoutAsync(250);
            if (dialog.Stacked)
            {
                Assert.IsTrue(await page.EvaluateAsync<bool>(
                        "() => !!document.activeElement?.closest('.tm-modal-overlay.tm-spreadsheet-name-manager')"),
                    $"{dialog.Name}: closing returns the focus to the Name Manager underneath");
                await page.Keyboard.PressAsync("Escape");
                await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
                    .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
                await page.WaitForTimeoutAsync(250);
            }

            Assert.IsTrue(await page.EvaluateAsync<bool>(
                    "() => !!document.activeElement?.closest('.tm-spreadsheet-canvas-grid')"),
                $"{dialog.Name}: closing restores focus to the grid");

            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_Dialogs_Tablet1024Touch_StayDialogs_WithTouchSizedTargets()
    {
        foreach (var dialog in Phase3Dialogs())
        {
            var page = await OpenPhase2DemoAsync(1024, 768, touch: true);
            await dialog.Open(page);

            var root = page.Locator($".tm-modal-overlay.{dialog.RootClass}");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForPresentationAsync(page, dialog.RootClass, "tm-modal--dialog");

            StringAssert.Contains(await root.GetAttributeAsync("class") ?? string.Empty, "tm-modal--dialog",
                $"{dialog.Name}: a 1024px viewport stays the centred dialog (no sheet, no fullscreen panel)");
            Assert.AreNotEqual("mobile", await root.GetAttributeAsync("data-layout"),
                $"{dialog.Name}: a 1024px viewport is not the mobile layout");

            // The frame's footer never leaves the viewport, and its primary action is thumb sized
            // (--tm-touch-target on a coarse pointer).
            var footerButton = root.Locator(".tm-modal-footer .tm-btn").Last;
            var box = await footerButton.BoundingBoxAsync();
            Assert.IsNotNull(box, $"{dialog.Name}: the footer action is rendered");
            var viewport = page.ViewportSize!;
            Assert.IsTrue(box!.Y >= 0 && box.Y + box.Height <= viewport.Height,
                $"{dialog.Name}: the footer stays inside the viewport ({box.Y}..{box.Y + box.Height} of {viewport.Height})");
            Assert.IsTrue(box.Height >= 40,
                $"{dialog.Name}: a coarse pointer gives the footer action a thumb-sized target, was {box.Height}px");

            await ShotPhase2Async(page, $"phase3-{dialog.Name}--tablet");

            await page.Keyboard.PressAsync("Escape");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_Dialogs_Mobile390_AreSheetsOrFullscreen_AndKeepTheFooterVisible()
    {
        foreach (var dialog in Phase3Dialogs())
        {
            var page = await OpenPhase2DemoAsync(390, 844, touch: true);
            await dialog.Open(page);

            var root = page.Locator($".tm-modal-overlay.{dialog.RootClass}");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForLayoutAsync(page, dialog.RootClass, "mobile",
                dialog.TallForm ? "tm-modal--fullscreen-presentation" : "tm-modal--sheet");

            var classes = await root.GetAttributeAsync("class") ?? string.Empty;
            Assert.AreEqual("mobile", await root.GetAttributeAsync("data-layout"), $"{dialog.Name}: data-layout");
            if (dialog.TallForm)
            {
                StringAssert.Contains(classes, "tm-modal--fullscreen-presentation",
                    $"{dialog.Name} is a tall surface: a phone gets the fullscreen panel");
            }
            else
            {
                StringAssert.Contains(classes, "tm-modal--sheet",
                    $"{dialog.Name} is a short form: a phone gets the bottom sheet");
                Assert.AreEqual(1, await root.Locator(".tm-sheet__panel").CountAsync(),
                    $"{dialog.Name}: the sheet panel is the trapped surface");
            }

            // The footer is reachable: this is the "keyboard open" contract of a sheet, and the
            // fullscreen panel keeps its footer pinned for the same reason.
            var footerButton = root.Locator(".tm-modal-footer .tm-btn").Last;
            var box = await footerButton.BoundingBoxAsync();
            Assert.IsNotNull(box, $"{dialog.Name}: the footer action is rendered");
            var viewport = page.ViewportSize!;
            Assert.IsTrue(box!.Y >= 0 && box.Y + box.Height <= viewport.Height,
                $"{dialog.Name}: the footer must stay on screen ({box.Y}..{box.Y + box.Height} of {viewport.Height})");

            // Nothing overflows the phone horizontally.
            var rootBox = await root.Locator(".tm-modal").First.BoundingBoxAsync();
            Assert.IsNotNull(rootBox);
            Assert.IsTrue(rootBox!.X >= -1 && rootBox.X + rootBox.Width <= viewport.Width + 1,
                $"{dialog.Name}: the surface fits the phone width ({rootBox.X}..{rootBox.X + rootBox.Width} of {viewport.Width})");

            await ShotPhase2Async(page, $"phase3-{dialog.Name}--mobile");

            await page.Keyboard.PressAsync("Escape");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_TallDialogs_Mobile320_KeepTheWholeFormReachable()
    {
        // Edge case: the three 320px references — a fullscreen panel must still scroll its body and
        // keep the footer visible at the narrowest supported width.
        foreach (var name in new[] { "format-cells", "text-to-columns", "name-manager" })
        {
            var dialog = Phase3Dialogs().Single(d => d.Name == name);
            var page = await OpenPhase2DemoAsync(320, 640, touch: true);
            await dialog.Open(page);

            var root = page.Locator($".tm-modal-overlay.{dialog.RootClass}");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForLayoutAsync(page, dialog.RootClass, "mobile", "tm-modal--fullscreen-presentation");
            StringAssert.Contains(await root.GetAttributeAsync("class") ?? string.Empty,
                "tm-modal--fullscreen-presentation", $"{name} at 320 is a fullscreen panel");

            var body = root.Locator(".tm-modal-body");
            Assert.IsTrue(await body.EvaluateAsync<bool>("el => el.scrollHeight > 0"),
                $"{name}: the panel body still has content at 320px");
            var footerButton = root.Locator(".tm-modal-footer .tm-btn").Last;
            var box = await footerButton.BoundingBoxAsync();
            Assert.IsNotNull(box);
            Assert.IsTrue(box!.Y + box.Height <= 641,
                $"{name}: the footer stays inside a 640px-tall viewport, was {box.Y + box.Height}");

            await ShotPhase2Async(page, $"phase3-{name}--mobile-320");

            await page.Keyboard.PressAsync("Escape");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_Stacked_NamedRangeEditOverNameManager_DimsOnce_AndEscapeClosesOnlyTheTop()
    {
        var page = await OpenPhase3DialogDemoAsync();
        await ClickDataTabButtonAsync(page, "Name Manager");

        var manager = page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager");
        await manager.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        // Edge case: a long name list in a short viewport — seed 20 names through the editor.
        for (var i = 1; i <= 20; i++)
        {
            await manager.Locator(".tm-spreadsheet-name-manager__toolbar button").First.ClickAsync();
            var editor = page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit");
            await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

            // Two modals are open at once: each paints its own overlay…
            Assert.AreEqual(2, await page.Locator(".tm-modal-overlay").CountAsync(),
                "the editor is a second modal over the manager");

            if (i == 1)
            {
                // …but the page is NOT dimmed twice: the first overlay paints the full scrim, the
                // stacked one only the light --tm-modal-stacked-scrim, so the lower dialog keeps a
                // depth cue without the page turning black.
                var scrimAlpha =
                    "el => { const c = getComputedStyle(el).backgroundColor;"
                    + " const rgba = c.match(/rgba?\\([^)]*,\\s*([\\d.]+)\\)/);"
                    + " if (rgba) return parseFloat(rgba[1]);"
                    + " const slash = c.match(/\\/\\s*([\\d.]+)\\)/);"
                    + " if (slash) return parseFloat(slash[1]);"
                    + " return c === 'transparent' ? 0 : 1; }";
                var managerAlpha = await manager.EvaluateAsync<double>(scrimAlpha);
                var editorAlpha = await editor.EvaluateAsync<double>(scrimAlpha);
                Assert.IsTrue(managerAlpha > 0.3,
                    $"the first overlay paints the page scrim, alpha was {managerAlpha}");
                Assert.IsTrue(editorAlpha > 0.0,
                    "a stacked overlay still paints the light stacked scrim (depth cue for the dialog below)");
                Assert.IsTrue(editorAlpha < managerAlpha - 0.15,
                    $"the stacked scrim must be clearly lighter than the page one, was {editorAlpha} vs {managerAlpha}");
            }

            // … and Escape closes ONLY the top one.
            if (i == 1)
            {
                await page.FillAsync("#nr-name", "Range_01");
                await page.FillAsync("#nr-refers", "=Sheet1!A2");
                await ShotPhase2Async(page, "phase3-stacked--named-range-edit-over-name-manager");

                await page.Keyboard.PressAsync("Escape");
                await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
                Assert.IsTrue(await manager.IsVisibleAsync(),
                    "Escape closed only the top dialog; the manager underneath stays open");
                Assert.AreEqual(1, await page.Locator(".tm-modal-overlay").CountAsync(),
                    "one overlay is left, so the page is not dimmed twice");
                continue;
            }

            await page.FillAsync("#nr-name", $"Range_{i:00}");
            await page.FillAsync("#nr-refers", $"=Sheet1!A{i + 1}");
            await editor.Locator(".tm-modal-footer .tm-btn").Last.ClickAsync();
            await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        }

        // The long list keeps the frame's footer inside the viewport (the modal body scrolls).
        var closeButton = manager.Locator(".tm-modal-footer .tm-btn").Last;
        var box = await closeButton.BoundingBoxAsync();
        var viewport = page.ViewportSize!;
        Assert.IsNotNull(box);
        Assert.IsTrue(box!.Y >= 0 && box.Y + box.Height <= viewport.Height,
            $"the manager footer stays on screen with 20 names, was {box.Y}..{box.Y + box.Height} of {viewport.Height}");
        var scrollRange = await manager.Locator(".tm-modal-body").EvaluateAsync<double>(
            "el => el.scrollHeight - el.clientHeight");
        Assert.IsTrue(scrollRange > 0, "20 names overflow the modal body, so it scrolls");

        await ShotPhase2Async(page, "phase3-name-manager--long-list-desktop");

        await page.Keyboard.PressAsync("Escape");
        await manager.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_ValidationAlert_IsAnAlertDialog_WithSeverityIcon_AndTheGridBehindIt()
    {
        var page = await OpenPhase3DialogDemoAsync();
        var grid = await OpenPhase3GridAsync(page);

        // Stop severity: a hard block with the single acknowledge action.
        await SetWholeNumberValidationAsync(page, grid, "H6", "1", "10");
        await TypeIntoCanvasCellAsync(page, grid, "H6", "99");

        var alert = page.Locator(".tm-modal-overlay.tm-spreadsheet-alert");
        await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        StringAssert.Contains(await alert.GetAttributeAsync("class") ?? string.Empty, "tm-spreadsheet-alert--stop");
        Assert.AreEqual(1, await alert.Locator("[role='alertdialog']").CountAsync());
        Assert.AreEqual(1, await alert.Locator(".tm-spreadsheet-alert__badge").CountAsync(), "Stop paints the danger icon");
        Assert.AreEqual(1, await alert.Locator(".tm-spreadsheet-alert__actions button").CountAsync(),
            "a Stop alert offers exactly one way out: Got it");
        Assert.AreEqual(0, await alert.Locator(".tm-modal-close").CountAsync(),
            "an alert never grows a header close path");

        await ShotPhase2Async(page, "phase3-validation-error--desktop");

        // A backdrop click does nothing (the value stays blocked).
        await page.Mouse.ClickAsync(10, 10);
        await page.WaitForTimeoutAsync(200);
        Assert.IsTrue(await alert.IsVisibleAsync(), "a Stop alert ignores a backdrop click");

        await page.Keyboard.PressAsync("Escape");
        await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Warning severity: its own rule on its own cell (the Stop rule would block the value first).
        await SetWholeNumberWarningValidationAsync(page, grid, "H7", "1", "10");
        await TypeIntoCanvasCellAsync(page, grid, "H7", "150");

        var warning = page.Locator(".tm-modal-overlay.tm-spreadsheet-alert");
        await warning.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        StringAssert.Contains(await warning.GetAttributeAsync("class") ?? string.Empty,
            "tm-spreadsheet-alert--warning");
        StringAssert.Contains(await warning.InnerTextAsync(), "Use anyway",
            "a Warning offers the keep-the-value action");

        await ShotPhase2Async(page, "phase3-validation-warning--desktop");
        await page.CloseAsync();

        // The alert is a frame surface like the others: the centred dialog on a tablet, the bottom
        // sheet on a phone, with its single primary action inside the viewport.
        // The alert resolves its presentation from the same core scope as every other surface, so the
        // assertion pins the PRESENTATION (dialog on a 1024 tablet, sheet on a 390 phone) rather than
        // the scope's own breakpoint label.
        foreach (var (width, height, classToken, name) in new[]
                 {
                     (1024, 768, "tm-modal--dialog", "tablet"),
                     (390, 844, "tm-modal--sheet", "mobile")
                 })
        {
            var sized = await OpenPhase2DemoAsync(width, height, touch: true);
            var sizedGrid = await OpenPhase3GridAsync(sized);
            await SetWholeNumberValidationAsync(sized, sizedGrid, "H6", "1", "10", viaNameBox: true);
            await TypeIntoCanvasCellAsync(sized, sizedGrid, "H6", "99");

            var sizedAlert = sized.Locator(".tm-modal-overlay.tm-spreadsheet-alert");
            await sizedAlert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForPresentationAsync(sized, "tm-spreadsheet-alert", classToken);
            // The sheet slides up (core 0.3s transition); measuring the footer mid-animation would
            // read it ~20px below its resting place.
            await sized.WaitForTimeoutAsync(450);
            Assert.AreEqual(1, await sizedAlert.Locator("[role='alertdialog']").CountAsync());
            var sizedBox = await sizedAlert.Locator(".tm-modal-footer .tm-btn").Last.BoundingBoxAsync();
            Assert.IsNotNull(sizedBox, $"{name}: the acknowledge action is rendered");
            var sizedViewport = sized.ViewportSize!;
            Assert.IsTrue(sizedBox!.Y >= 0 && sizedBox.Y + sizedBox.Height <= sizedViewport.Height + 1,
                $"{name}: the alert's footer stays inside the viewport, was "
                + $"{sizedBox.Y:N1}..{sizedBox.Y + sizedBox.Height:N1} of {sizedViewport.Height}");
            await ShotPhase2Async(sized, $"phase3-validation-error--{name}");
            await sized.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_FindReplace_StaysNonModal_AndBecomesANonModalSheetOnMobile()
    {
        // Desktop: the floating panel, no modal, the grid still usable.
        var desktop = await OpenPhase3DialogDemoAsync();
        var desktopGrid = await OpenPhase3GridAsync(desktop);
        await desktopGrid.FocusAsync();
        await desktop.Keyboard.PressAsync("Control+f");

        var panel = desktop.Locator(".tm-spreadsheet-find");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual(0, await desktop.Locator(".tm-modal-overlay").CountAsync(),
            "Find/Replace is deliberately NOT a modal: the matched cells must stay visible");
        Assert.AreEqual(0, await desktop.Locator(".tm-drawer__panel").CountAsync(),
            "the desktop layout keeps the floating panel");
        await ShotPhase2Async(desktop, "phase3-find-replace--desktop");
        await desktop.Keyboard.PressAsync("Escape");
        await desktop.CloseAsync();

        // Mobile: the same panel inside a NON-modal bottom sheet (no backdrop, grid operable).
        var mobile = await OpenPhase2DemoAsync(390, 844, touch: true);
        var mobileGrid = await OpenPhase3GridAsync(mobile);
        await mobileGrid.FocusAsync();
        await mobile.Keyboard.PressAsync("Control+f");

        var sheet = mobile.Locator(".tm-drawer__panel");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual(0, await mobile.Locator(".tm-drawer__overlay").CountAsync(),
            "the sheet is non-modal: there is no backdrop, so the grid stays operable");
        Assert.AreEqual(0, await mobile.Locator(".tm-modal-overlay").CountAsync());
        // The panel inside the sheet is the SHEET's content: static, full width, no floating-card
        // chrome. The scoped `.tm-spreadsheet-find` rule is more specific than a plain descendant
        // selector, so the in-sheet override has to out-specify it.
        var panelBox = await mobile.Locator(".tm-spreadsheet-find").First.BoundingBoxAsync();
        // The panel spans the CONTENT box of whatever holds it inside the sheet (the drawer keeps its
        // own padding), so the expectation is read from the DOM instead of hardcoded.
        var containerContentWidth = await mobile.Locator(".tm-spreadsheet-find").First.EvaluateAsync<double>(
            "el => { const p = el.parentElement; const cs = getComputedStyle(p);"
            + " return p.clientWidth - parseFloat(cs.paddingLeft || 0) - parseFloat(cs.paddingRight || 0); }");
        Assert.IsNotNull(panelBox);
        Assert.AreEqual("static", await mobile.Locator(".tm-spreadsheet-find").First
            .EvaluateAsync<string>("el => getComputedStyle(el).position"),
            "the mobile panel is in-flow inside the sheet, not a position:absolute floating card");
        Assert.AreEqual("none", await mobile.Locator(".tm-spreadsheet-find").First
            .EvaluateAsync<string>("el => getComputedStyle(el).boxShadow"),
            "the sheet owns the surface, so the panel paints no card shadow of its own");
        // The sheet's own horizontal padding stays; the panel spans the rest of its content box.
        Assert.IsTrue(panelBox!.Width >= containerContentWidth - 2,
            $"the panel fills the sheet's content box, was {panelBox.Width} of {containerContentWidth}");
        await mobile.WaitForTimeoutAsync(300);
        await ShotPhase2Async(mobile, "phase3-find-replace--mobile");
        await mobile.CloseAsync();

        // Tablet: still the floating panel (the sheet is the phone presentation only).
        var tablet = await OpenPhase2DemoAsync(1024, 768, touch: true);
        var tabletGrid = await OpenPhase3GridAsync(tablet);
        await tabletGrid.FocusAsync();
        await tablet.Keyboard.PressAsync("Control+f");
        await tablet.Locator(".tm-spreadsheet-find").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        Assert.AreEqual("absolute", await tablet.Locator(".tm-spreadsheet-find").First
            .EvaluateAsync<string>("el => getComputedStyle(el).position"),
            "a 1024px viewport keeps the floating panel");
        await tablet.WaitForTimeoutAsync(300);
        await ShotPhase2Async(tablet, "phase3-find-replace--tablet");
        await tablet.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase3_ResizeDialogs_OwnTheirKeyboard_AndRestoreFocusToTheGrid()
    {
        // The column-width / row-height dialogs render INSIDE the grid root, whose capturing keydown
        // (and wheel) listener used to answer their keys: Enter on Apply reached the grid as well and
        // Escape was handled twice. Both are guards in spreadsheet-canvas.js now.
        var page = await OpenPhase3DialogDemoAsync();

        await OpenResizeDialogAsync(page, "Set column width");
        var dialog = page.Locator(".tm-modal-overlay.tm-spreadsheet-resize-dialog");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var input = dialog.Locator(".tm-spreadsheet-resize-dialog__input");
        await input.FillAsync("150");

        // Tab walks the dialog's own order (input → Cancel → Apply), never the grid behind it.
        for (var i = 0; i < 6; i++)
        {
            if (await page.EvaluateAsync<bool>(
                    "() => document.activeElement?.classList.contains('tm-spreadsheet-resize-dialog__btn--ok') === true"))
            {
                break;
            }

            await page.Keyboard.PressAsync("Tab");
        }

        Assert.IsTrue(await page.EvaluateAsync<bool>(
                "() => document.activeElement?.classList.contains('tm-spreadsheet-resize-dialog__btn--ok') === true"),
            "Tab must reach the dialog's Apply button");

        await page.Keyboard.PressAsync("Enter");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // The commit is visible where a user sees it: reopen the same column's dialog and read the
        // MODEL width the grid stored (the canvas is a bitmap, so there is no DOM cell to measure).
        await OpenResizeDialogAsync(page, "Set column width");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        var replayed = await dialog.Locator(".tm-spreadsheet-resize-dialog__input").InputValueAsync();
        Assert.AreEqual("150", replayed,
            "Enter on Apply commits the width (a reopened dialog shows the stored 150)");
        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });

        // Open the row-height one and Escape: one dismissal (the frame's), and the focus is back in
        // the grid.
        await OpenResizeDialogAsync(page, "Set row height");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.Keyboard.PressAsync("Escape");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(250);
        Assert.IsTrue(await page.EvaluateAsync<bool>(
                "() => !!document.activeElement?.closest('.tm-spreadsheet-canvas-grid')"),
            "closing the resize dialog returns the focus to the grid");
        await page.CloseAsync();
    }

    /// <summary>
    /// Waits until the frame renders the expected presentation class. The presentation is the phase's
    /// contract; the exact data-layout value belongs to the frame's own resolution.
    /// </summary>
    private static async Task WaitForPresentationAsync(IPage page, string rootClass, string classToken)
    {
        var selector = $".tm-modal-overlay.{rootClass}";
        try
        {
            await page.WaitForFunctionAsync(
                "([sel, token]) => (document.querySelector(sel)?.className || '').includes(token)",
                new object[] { selector, classToken },
                new PageWaitForFunctionOptions { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            var seen = await page.Locator(selector).First.EvaluateAsync<string>(
                "el => (el.getAttribute('data-layout') || '?') + ' | ' + (el.className || '')");
            Assert.Fail($"{selector} never rendered '{classToken}'; it rendered '{seen}'");
        }
    }

    /// <summary>
    /// Waits for the frame to finish resolving its layout: <c>data-layout</c> starts on the first-frame
    /// assumption (desktop) and the probe / the body observer corrects it one interop round trip later,
    /// so an assertion made right after "visible" would read the initial value.
    /// </summary>
    private static async Task WaitForLayoutAsync(IPage page, string rootClass, string expected, string? classToken = null)
    {
        var selector = $".tm-modal-overlay.{rootClass}";
        var predicate = classToken is null
            ? "([sel, want]) => document.querySelector(sel)?.getAttribute('data-layout') === want"
            : "([sel, want, token]) => (document.querySelector(sel)?.getAttribute('data-layout') === want) && (document.querySelector(sel)?.className || '').includes(token)";
        var args = classToken is null
            ? new object[] { selector, expected }
            : new object[] { selector, expected, classToken };
        try
        {
            await page.WaitForFunctionAsync(predicate, args, new PageWaitForFunctionOptions { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            var seen = await page.Locator(selector).First.EvaluateAsync<string>(
                "el => (el.getAttribute('data-layout') || '?') + ' | ' + (el.className || '')");
            Assert.Fail($"{selector} never resolved to '{expected}'; it rendered '{seen}'");
            throw;
        }
    }

    // ── Opening helpers ──────────────────────────────────────────────────────

    private async Task<IPage> OpenPhase3DialogDemoAsync()
    {
        var page = await OpenPhase2DemoAsync(1440, 900, touch: false);
        await OpenPhase3GridAsync(page);
        return page;
    }

    private static async Task<ILocator> OpenPhase3GridAsync(IPage page)
    {
        var grid = DemoGrid(page);
        await WaitForCanvasGridReadyAsync(page, grid);
        return grid;
    }

    private static Task ClickDataTabButtonAsync(IPage page, string title)
        => ClickTabButtonAsync(page, "Data", title);

    private static Task ClickInsertTabButtonAsync(IPage page, string title)
        => ClickTabButtonAsync(page, "Insert", title);

    private static async Task ClickTabButtonAsync(IPage page, string tab, string title)
    {
        await DemoGrid(page).FocusAsync();
        await page.Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = tab }).First.ClickAsync();
        await page.Locator($"button[title='{title}']").First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>
    /// Paste Special only opens with something on the clipboard: copy the active cell first, the way a
    /// user reaches it.
    /// </summary>
    private static async Task OpenPasteSpecialAsync(IPage page)
    {
        var grid = DemoGrid(page);
        await grid.FocusAsync();
        await page.Keyboard.PressAsync("Control+c");
        await page.WaitForTimeoutAsync(200);
        await ClickDataTabButtonAsync(page, "Paste special");
    }

    private static async Task OpenSortAsync(IPage page)
        => await ClickDataTabButtonAsync(page, "Custom sort…");

    private static async Task OpenFormatCellsAsync(IPage page)
    {
        var grid = DemoGrid(page);
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Button = MouseButton.Right,
            Position = new() { X = 120, Y = 60 }
        });
        await page.Locator(".tm-spreadsheet-context-menu__item", new() { HasTextString = "Format Cells" })
            .First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>Opens one of the canvas-grid resize dialogs from the cell context menu.</summary>
    private static async Task OpenResizeDialogAsync(IPage page, string title)
    {
        var grid = DemoGrid(page);
        await grid.ClickAsync(new LocatorClickOptions
        {
            Force = true,
            Button = MouseButton.Right,
            Position = new() { X = 120, Y = 60 }
        });
        await page.Locator(".tm-spreadsheet-context-menu__item", new() { HasTextString = title })
            .First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    private static async Task OpenNamedRangeEditAsync(IPage page)
    {
        await ClickDataTabButtonAsync(page, "Name Manager");
        await page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager")
            .Locator(".tm-spreadsheet-name-manager__toolbar button").First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>
    /// Reaches the custom filter through the real path: seed a region, enable the auto filter, click the
    /// header filter button and pick the custom-filter entry.
    /// </summary>
    private async Task OpenCustomFilterAsync(IPage page)
    {
        var grid = DemoGrid(page);
        await SeedFilterRegionAsync(page, grid);
        await EnableFilterAsync(page, grid);
        await ClickFilterButtonAsync(page, grid, col: 3);

        var dropdown = page.Locator(".tm-spreadsheet-filter-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        // The entry is named after the column kind ("Text filters" / "Number filters" / "Date filters");
        // it opens the custom-filter dialog.
        await dropdown.Locator(".tm-spreadsheet-filter-dropdown__item", new() { HasTextString = "filters" })
            .First.ClickAsync();
        await page.WaitForTimeoutAsync(200);
    }

    /// <summary>
    /// Seeds a whole-number rule whose error style is Warning, through the Data validation dialog
    /// (the Error alert tab's Style select). Stop is the dialog's default, hence the extra steps.
    /// </summary>
    private static async Task SetWholeNumberWarningValidationAsync(
        IPage page,
        ILocator grid,
        string cellRef,
        string min,
        string max)
    {
        var pt = await GetCanvasCellCenterAsync(grid, cellRef);
        await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = pt.X, Y = pt.Y } });
        await WaitForCanvasActiveRefAsync(grid, cellRef);

        await ClickDataTabButtonAsync(page, "Data validation...");
        var dialog = page.Locator(".tm-dvd");
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await dialog.Locator(".tm-dvd__select select").First.SelectOptionAsync("Whole");
        await page.WaitForTimeoutAsync(200);
        await FillDialogInputAsync(page, dialog.Locator(".tm-dvd__input").First, min);
        await FillDialogInputAsync(page, dialog.Locator(".tm-dvd__input").Last, max);

        await dialog.Locator(".tm-dvd__tab", new() { HasTextString = "Error alert" }).ClickAsync();
        await page.WaitForTimeoutAsync(200);
        await dialog.Locator(".tm-dvd__select select").First.SelectOptionAsync("Warning");
        await page.WaitForTimeoutAsync(200);

        await dialog.Locator(".tm-dvd__actions button").Last.ClickAsync();
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
    }

    private static async Task TypeIntoCanvasCellAsync(IPage page, ILocator grid, string cellRef, string value)
    {
        await page.FillAsync(".tm-spreadsheet-formula-bar__ref", cellRef);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(200);
        await page.Keyboard.TypeAsync(value);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(200);
    }
}
