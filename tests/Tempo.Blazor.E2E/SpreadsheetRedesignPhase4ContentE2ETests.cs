using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 4 of the Spreadsheet redesign — the CONTENT and hierarchy of every dialog against the
/// reference mockups: visible labels on Find/Replace, the match counter, two option columns, the
/// primary Replace all, a labelled wizard stepper, cards instead of cramped tables on a phone,
/// segmented link type / And-Or, the selected-column count, shared icons instead of Unicode glyphs,
/// and the mobile category select above the format options.
/// <para>
/// Screenshots land in <c>__screenshots__/spreadsheet/</c> as <c>phase4-&lt;dialog&gt;--&lt;desktop|tablet|mobile&gt;</c>
/// (plus <c>--step-2</c>/<c>--step-3</c> for the Text to Columns wizard and the two edge cases) and are
/// reviewed against output/imagegen/tempo-reimagined/Tempo.Blazor.Spreadsheet/spreadsheet-*-dialog--*.png
/// and spreadsheet-filter-dropdown--desktop.png.
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    /// <summary>The dialogs this phase restyles, and how a user reaches each one in the demo.</summary>
    private Phase3Dialog[] Phase4Dialogs() => Phase3Dialogs()
        .Where(d => d.Name is "sort" or "custom-filter" or "remove-duplicates" or "special-paste"
            or "text-to-columns" or "hyperlink" or "name-manager" or "data-validation" or "format-cells")
        .ToArray();

    private static async Task<int> CountAsync(ILocator locator) => await locator.CountAsync();

    private static async Task<string> TextAsync(ILocator locator) => (await locator.First.TextContentAsync() ?? string.Empty).Trim();

    /// <summary>Asserts the reference's content contract for one dialog in the current viewport.</summary>
    private static async Task AssertPhase4ContentAsync(IPage page, ILocator root, string name, bool mobile)
    {
        switch (name)
        {
            case "sort":
                if (mobile)
                {
                    (await CountAsync(root.Locator(".tm-spreadsheet-sort__table"))).Should().Be(0,
                        "a phone cannot show the five-column level table");
                    (await CountAsync(root.Locator(".tm-spreadsheet-sort__card"))).Should().BeGreaterThanOrEqualTo(1);
                    (await CountAsync(root.Locator(".tm-spreadsheet-sort__card-field"))).Should().Be(3,
                        "each field keeps its label above it in the card");
                }
                else
                {
                    (await CountAsync(root.Locator(".tm-spreadsheet-sort__table"))).Should().Be(1);
                    (await CountAsync(root.Locator(".tm-spreadsheet-sort__card"))).Should().Be(0);
                }

                StringAssert.Contains(await root.Locator(".tm-spreadsheet-sort__tool").First.GetAttributeAsync("class") ?? string.Empty,
                    "tm-btn-outline", "Add level is the outlined tonal action, not the filled primary");

                // Regression (phase-4 review): switching Desc -> Asc must paint "A → Z", not a blank
                // select — a non-nullable enum at its default (Ascending) now pins its value attribute.
                var order = root.Locator(".tm-spreadsheet-sort__select select").Last;
                await order.SelectOptionAsync("Descending");
                await order.SelectOptionAsync("Ascending");
                Assert.AreEqual("A → Z",
                    await order.EvaluateAsync<string>("el => el.selectedOptions[0].textContent"),
                    "the Ascending choice stays visible after switching back");
                (await CountAsync(root.Locator(".tm-spreadsheet-sort__select select"))).Should().BeGreaterThanOrEqualTo(3,
                    "the level fields are core selects");
                break;

            case "custom-filter":
                (await CountAsync(root.Locator(".tm-spreadsheet-custom-filter__join .tm-radio-segment"))).Should().Be(2,
                    "And/Or is a segmented control");
                // The demo opens the TEXT filter ("Text filters"), whose operand keeps the text
                // keyboard; the numeric/date kind's inputmode="decimal" is pinned by the bUnit test.
                Assert.AreEqual("text", await root.Locator(".tm-spreadsheet-custom-filter__value").First.GetAttributeAsync("inputmode"),
                    "a text operand keeps the text keyboard");
                if (!mobile)
                {
                    var operand = await root.Locator(".tm-spreadsheet-custom-filter__value").First.BoundingBoxAsync();
                    Assert.IsNotNull(operand);
                    Assert.IsTrue(operand!.Width >= 120,
                        $"the operand shares the row with its operator instead of collapsing, was {operand.Width:N1}px");
                }
                break;

            case "remove-duplicates":
                StringAssert.StartsWith(await TextAsync(root.Locator(".tm-spreadsheet-dedup__count")), "Selected ",
                    "the dialog states how many columns are selected");
                (await CountAsync(root.Locator(".tm-spreadsheet-dedup__column input[type=checkbox]"))).Should().BeGreaterThanOrEqualTo(1,
                    "the columns are core checkboxes");
                var columnBox = await root.Locator(".tm-spreadsheet-dedup__column").First.BoundingBoxAsync();
                Assert.IsNotNull(columnBox);
                Assert.IsTrue(columnBox!.Height >= 43,
                    $"the column row keeps the touch target on a coarse pointer, was {columnBox.Height:N1}px");
                break;

            case "special-paste":
                (await CountAsync(root.Locator(".tm-radio-group"))).Should().Be(2,
                    "Paste and Operation are two radio columns");
                (await CountAsync(root.Locator("input[name='ps-operation']"))).Should().Be(5,
                    "the operation is a radio choice, not a select");
                var pasteMarkup = await root.InnerHTMLAsync();
                foreach (var glyph in new[] { "▲", "▼", "⚙", "✕", "›", "→" })
                {
                    Assert.IsFalse(pasteMarkup.Contains(glyph), $"'{glyph}' is a glyph, not the shared icon set");
                }

                break;

            case "text-to-columns":
                if (mobile)
                {
                    Assert.IsTrue(await root.Locator(".tm-spreadsheet-t2c__step-compact").First.IsVisibleAsync(),
                        "the phone shows the compact 'Step N of 3' line");
                    (await CountAsync(root.Locator(".tm-stepper"))).Should().Be(0);
                }
                else
                {
                    (await CountAsync(root.Locator(".tm-stepper-item"))).Should().Be(3, "the wizard has three labelled steps");
                    var stepText = await TextAsync(root.Locator(".tm-stepper"));
                    StringAssert.Contains(stepText, "Data type");
                    StringAssert.Contains(stepText, "Delimiters");
                    StringAssert.Contains(stepText, "Column format");
                    Assert.AreEqual("step", await root.Locator(".tm-stepper-step").First.GetAttributeAsync("aria-current"));
                }

                break;

            case "hyperlink":
                (await CountAsync(root.Locator("#hl-type .tm-radio-segment"))).Should().Be(4,
                    "the link type is a segmented control with all four kinds visible");
                Assert.AreEqual("url", await root.Locator("#hl-target").First.GetAttributeAsync("inputmode"));
                await root.Locator("#hl-type .tm-radio-segment").Nth(1).ClickAsync();
                await page.WaitForTimeoutAsync(150);
                Assert.AreEqual("email", await root.Locator("#hl-target").First.GetAttributeAsync("inputmode"),
                    "an e-mail link takes the e-mail keyboard");
                break;

            case "name-manager":
                StringAssert.Contains(await root.Locator(".tm-spreadsheet-name-manager__tool").First.GetAttributeAsync("class") ?? string.Empty,
                    "tm-btn-primary", "New is the primary toolbar action");
                Assert.AreEqual("search", await root.Locator(".tm-spreadsheet-name-manager__filter input").First.GetAttributeAsync("type"),
                    "the filter is the core search input");
                if (mobile)
                {
                    (await CountAsync(root.Locator(".tm-spreadsheet-name-manager__card"))).Should().BeGreaterThanOrEqualTo(1,
                        "the phone lists the ranges as cards");
                    (await CountAsync(root.Locator(".tm-spreadsheet-name-manager__table"))).Should().Be(0);
                }
                else
                {
                    (await CountAsync(root.Locator(".tm-spreadsheet-name-manager__table"))).Should().Be(1);
                    (await CountAsync(root.Locator(".tm-spreadsheet-name-manager__card"))).Should().Be(0);
                }

                break;

            case "data-validation":
                var fieldDisplay = await root.Locator(".tm-dvd__field").First.EvaluateAsync<string>(
                    "el => getComputedStyle(el).display");
                if (mobile)
                {
                    Assert.AreEqual("flex", fieldDisplay, "a phone stacks label over field");
                }
                else
                {
                    Assert.AreEqual("grid", fieldDisplay, "from a 640px surface the form is label | field");
                    var tracks = await root.Locator(".tm-dvd__field").First.EvaluateAsync<string>(
                        "el => getComputedStyle(el).gridTemplateColumns");
                    tracks.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should().Be(2,
                        $"label and field are two columns, got '{tracks}'");
                }

                break;

            case "format-cells":
                if (mobile)
                {
                    Assert.IsTrue(await root.Locator(".tm-fcd__category-select").First.IsVisibleAsync(),
                        "the phone puts the category select above the options");
                    (await CountAsync(root.Locator(".tm-fcd__listbox[role='listbox']"))).Should().Be(0);
                }
                else
                {
                    (await CountAsync(root.Locator(".tm-fcd__listbox[role='listbox']"))).Should().Be(1);
                    (await CountAsync(root.Locator(".tm-fcd__category-select"))).Should().Be(0);
                    var activeTab = root.Locator(".tm-fcd__tab--active").First;
                    var borderColor = await activeTab.EvaluateAsync<string>("el => getComputedStyle(el).borderBottomColor");
                    var primary = await root.EvaluateAsync<string>(
                        "el => getComputedStyle(el.querySelector('.tm-fcd__tab--active')).getPropertyValue('--tm-color-primary').trim()");
                    Assert.AreNotEqual("rgba(0, 0, 0, 0)", borderColor, "the active tab is underlined");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(primary), "the underline is the primary token");
                }

                break;
        }
    }

    /// <summary>
    /// Waits until the frame resolved its presentation for this viewport. The core frame treats a
    /// 1024px touch viewport as the DESKTOP layout (its mobile breakpoint is narrower), so the phone
    /// is the only case that may assert data-layout="mobile".
    /// </summary>
    private static async Task WaitForResolvedLayoutAsync(IPage page, string rootClass, bool expectMobile)
    {
        var selector = $".tm-modal-overlay.{rootClass}";
        try
        {
            await page.WaitForFunctionAsync(
                "([sel, wantMobile]) => { const el = document.querySelector(sel); if (!el) return false;"
                + " const l = el.getAttribute('data-layout');"
                + " return wantMobile ? l === 'mobile' : (l === 'desktop' || l === 'tablet'); }",
                new object[] { selector, expectMobile },
                new PageWaitForFunctionOptions { Timeout = 15000 });
        }
        catch (TimeoutException)
        {
            var seen = await page.Locator(selector).First.EvaluateAsync<string>(
                "el => (el.getAttribute('data-layout') || '?') + ' | ' + (el.className || '')");
            Assert.Fail($"{selector} never resolved its layout (mobile={expectMobile}); it rendered '{seen}'");
        }
    }

    /// <summary>Gives the Name Manager one range, so the card/table layout is actually exercised.</summary>
    private static async Task SeedNamedRangeAsync(IPage page)
    {
        await ClickDataTabButtonAsync(page, "Name Manager");
        var manager = page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager");
        await manager.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        await manager.Locator(".tm-spreadsheet-name-manager__toolbar button").First.ClickAsync();
        var editor = page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit");
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.FillAsync("#nr-name", "Celkem");
        await page.FillAsync("#nr-refers", "=Sheet1!A1:A3");
        await editor.Locator(".tm-modal-footer .tm-btn").Last.ClickAsync();
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.Keyboard.PressAsync("Escape");
        await manager.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
    }

    private async Task RunPhase4DialogsAsync(int width, int height, bool touch, string layout)
    {
        foreach (var dialog in Phase4Dialogs())
        {
            var page = await OpenPhase2DemoAsync(width, height, touch);
            await OpenPhase3GridAsync(page);
            if (dialog.Name == "name-manager")
            {
                await SeedNamedRangeAsync(page);
            }

            await dialog.Open(page);

            var root = page.Locator($".tm-modal-overlay.{dialog.RootClass}");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForResolvedLayoutAsync(page, dialog.RootClass, expectMobile: layout == "mobile");

            await AssertPhase4ContentAsync(page, root, dialog.Name, mobile: layout == "mobile");
            await page.WaitForTimeoutAsync(300);
            await ShotPhase2Async(page, $"phase4-{dialog.Name}--{layout}");

            await page.Keyboard.PressAsync("Escape");
            await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_Dialogs_Desktop1440_ShowTheReferenceContent()
        => await RunPhase4DialogsAsync(1440, 900, touch: false, "desktop");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_Dialogs_Tablet1024Touch_ShowTheReferenceContent()
        => await RunPhase4DialogsAsync(1024, 768, touch: true, "tablet");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_Dialogs_Mobile390_ShowTheReferenceContent()
        => await RunPhase4DialogsAsync(390, 844, touch: true, "mobile");

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_FindReplace_LabelsCounterAndPrimaryReplaceAll_AllSizes()
    {
        foreach (var (width, height, touch, layout) in new[]
                 {
                     (1440, 900, false, "desktop"),
                     (1024, 768, true, "tablet"),
                     (390, 844, true, "mobile")
                 })
        {
            var page = await OpenPhase2DemoAsync(width, height, touch);
            var grid = await OpenPhase3GridAsync(page);
            await grid.FocusAsync();
            await page.Keyboard.PressAsync("Control+f");

            var panel = page.Locator(".tm-spreadsheet-find");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

            await page.Locator(".tm-spreadsheet-find__query").First.FillAsync("Canvas");
            await page.WaitForTimeoutAsync(300);
            Assert.AreEqual(2, await CountAsync(page.Locator(".tm-spreadsheet-find__label")),
                $"{layout}: both fields carry a visible label");
            Assert.IsTrue(await page.Locator(".tm-spreadsheet-find__counter").First.IsVisibleAsync(),
                $"{layout}: the match counter is visible");
            StringAssert.Contains(
                await page.Locator(".tm-spreadsheet-find__actions button", new() { HasTextString = "Replace all" })
                    .First.GetAttributeAsync("class") ?? string.Empty,
                "tm-btn-primary", $"{layout}: Replace all is the primary action");
            Assert.AreEqual(1, await CountAsync(page.Locator(".tm-spreadsheet-find__btn--nav").First.Locator("svg")),
                $"{layout}: the previous action is icon + text");
            (await TextAsync(page.Locator(".tm-spreadsheet-find__btn--nav").First)).Should().Contain("Find previous");

            await page.WaitForTimeoutAsync(200);
            if (layout == "mobile")
            {
                // The phone sheet is taller than the viewport: capture the panel itself, not a folded page.
                await page.WaitForTimeoutAsync(500); // the sheet slide-in is a 0.3s core transition
                var path = ShotPath($"phase4-find-replace--{layout}");
                // The panel lives in-flow inside the sheet; the SHEET is the sized surface (the panel's
                // own box is clipped by the drawer body), so capture the sheet with the form in it.
                await page.Locator(".tm-drawer__panel").First.ScreenshotAsync(new LocatorScreenshotOptions
                {
                    Path = path,
                    Type = ScreenshotType.Png
                });
                TestContext.AddResultFile(path);
            }
            else
            {
                await ShotPhase2Async(page, $"phase4-find-replace--{layout}");
            }
            await page.Keyboard.PressAsync("Escape");
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_FilterDropdown_Desktop_UsesIconsAndTitle()
    {
        var page = await OpenPhase3DialogDemoAsync();
        var grid = DemoGrid(page);
        await SeedFilterRegionAsync(page, grid);
        await EnableFilterAsync(page, grid);
        await ClickFilterButtonAsync(page, grid, col: 3);

        var dropdown = page.Locator(".tm-spreadsheet-filter-dropdown");
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("Filter and sort", await TextAsync(dropdown.Locator(".tm-spreadsheet-filter-dropdown__title")));
        (await CountAsync(dropdown.Locator(".tm-spreadsheet-filter-dropdown__item svg"))).Should().BeGreaterThanOrEqualTo(4,
            "every row paints a shared icon");
        var markup = await dropdown.InnerHTMLAsync();
        foreach (var glyph in new[] { "▲", "▼", "⚙", "✕", "›" })
        {
            Assert.IsFalse(markup.Contains(glyph), $"'{glyph}' is a glyph, not the shared icon set");
        }

        await page.WaitForTimeoutAsync(200);
        await ShotPhase2Async(page, "phase4-filter-dropdown--desktop");
        await page.CloseAsync();
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_TextToColumns_Steps1to3_AllSizes()
    {
        foreach (var (width, height, touch, layout) in new[]
                 {
                     (1440, 900, false, "desktop"),
                     (1024, 768, true, "tablet"),
                     (390, 844, true, "mobile")
                 })
        {
            var page = await OpenPhase2DemoAsync(width, height, touch);
            var grid = await OpenPhase3GridAsync(page);
            await EditCanvasCellAsync(page, grid, "D1", "Jan;Novak;Praha");
            await EditCanvasCellAsync(page, grid, "D2", "Petr;Svoboda;Brno");
            var topLeft = await GetCanvasCellCenterAsync(grid, "D1");
            await grid.ClickAsync(new LocatorClickOptions { Force = true, Position = new() { X = topLeft.X, Y = topLeft.Y } });
            await WaitForCanvasActiveRefAsync(grid, "D1");
            await page.Keyboard.PressAsync("Shift+ArrowDown");

            await ClickDataTabButtonAsync(page, "Text to columns");
            var dialog = page.Locator(".tm-spreadsheet-t2c");
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForResolvedLayoutAsync(page, "tm-spreadsheet-t2c", expectMobile: layout == "mobile");

            await page.WaitForTimeoutAsync(250);
            await ShotPhase2Async(page, $"phase4-text-to-columns--{layout}");

            await dialog.Locator(".tm-spreadsheet-t2c__btn--ok").ClickAsync(); // → step 2
            await page.WaitForTimeoutAsync(250);
            if (layout == "mobile")
            {
                StringAssert.Contains(await TextAsync(dialog.Locator(".tm-spreadsheet-t2c__step-compact")), "Step 2 of 3");
            }
            else
            {
                StringAssert.Contains(await TextAsync(dialog.Locator(".tm-stepper-step").Nth(1)), "Delimiters");
                Assert.AreEqual("step", await dialog.Locator(".tm-stepper-step").Nth(1).GetAttributeAsync("aria-current"));
            }

            await dialog.Locator(".tm-spreadsheet-t2c__delim", new() { HasTextString = "Semicolon" }).ClickAsync(); // semicolon
            await page.WaitForTimeoutAsync(300);
            await ShotPhase2Async(page, $"phase4-text-to-columns--step-2--{layout}");

            await dialog.Locator(".tm-spreadsheet-t2c__btn--ok").ClickAsync(); // → step 3
            await page.WaitForTimeoutAsync(300);
            await ShotPhase2Async(page, $"phase4-text-to-columns--step-3--{layout}");
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_Sort_SixLevels_EdgeCase()
    {
        foreach (var (width, height, touch, layout) in new[] { (1440, 900, false, "desktop"), (390, 844, true, "mobile") })
        {
            var page = await OpenPhase2DemoAsync(width, height, touch);
            await OpenPhase3GridAsync(page);
            await OpenSortAsync(page);

            var dialog = page.Locator(".tm-spreadsheet-sort");
            await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForResolvedLayoutAsync(page, "tm-spreadsheet-sort", expectMobile: layout == "mobile");

            for (var i = 0; i < 5; i++)
            {
                await dialog.Locator(".tm-spreadsheet-sort__tool").First.ClickAsync();
                await page.WaitForTimeoutAsync(120);
            }

            Assert.AreEqual(6, await CountAsync(dialog.Locator(layout == "mobile"
                    ? ".tm-spreadsheet-sort__card"
                    : ".tm-spreadsheet-sort__table tbody tr")),
                $"{layout}: six levels are reachable");

            await page.WaitForTimeoutAsync(200);
            await ShotPhase2Async(page, $"phase4-sort-six-levels--{layout}");
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_NameManager_LongNames_EdgeCase()
    {
        foreach (var (width, height, touch, layout) in new[] { (1440, 900, false, "desktop"), (390, 844, true, "mobile") })
        {
            var page = await OpenPhase2DemoAsync(width, height, touch);
            await OpenPhase3GridAsync(page);
            await ClickDataTabButtonAsync(page, "Name Manager");

            var manager = page.Locator(".tm-modal-overlay.tm-spreadsheet-name-manager");
            await manager.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

            var names = new[]
            {
                "Rozpocet_Projektu_Aurora_Celkem_Bez_DPH",
                "Skutecnost_Kategorie_Infrastruktura_Q1_Q4",
                "Rezerva_Na_Rizika_A_Nepredvidane_Naklady"
            };
            for (var i = 0; i < names.Length; i++)
            {
                await manager.Locator(".tm-spreadsheet-name-manager__toolbar button").First.ClickAsync();
                var editor = page.Locator(".tm-modal-overlay.tm-spreadsheet-named-range-edit");
                await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
                await page.FillAsync("#nr-name", names[i]);
                await page.FillAsync("#nr-refers", $"=Sheet1!A{i + 2}:C{i + 4}");
                await editor.Locator(".tm-modal-footer .tm-btn").Last.ClickAsync();
                await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            }

            await page.WaitForTimeoutAsync(250);
            await ShotPhase2Async(page, $"phase4-name-manager-long-names--{layout}");

            // No horizontal overflow: the long names wrap/ellipsize inside the surface.
            var overflow = await manager.Locator(".tm-modal-body").EvaluateAsync<double>(
                "el => el.scrollWidth - el.clientWidth");
            Assert.IsTrue(overflow <= 2, $"{layout}: long names must not overflow the dialog, was {overflow}px");
            await page.CloseAsync();
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task Phase4_ValidationAlert_MatchesTheReferenceCard()
    {
        foreach (var (width, height, touch, layout) in new[] { (1440, 900, false, "desktop"), (390, 844, true, "mobile") })
        {
            var page = await OpenPhase2DemoAsync(width, height, touch);
            var grid = await OpenPhase3GridAsync(page);

            await SetWholeNumberValidationAsync(page, grid, "H6", "1", "10", viaNameBox: true);
            await TypeIntoCanvasCellAsync(page, grid, "H6", "99");

            var alert = page.Locator(".tm-modal-overlay.tm-spreadsheet-alert");
            await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await WaitForPresentationAsync(page, "tm-spreadsheet-alert", layout == "mobile" ? "tm-modal--sheet" : "tm-modal--dialog");
            await page.WaitForTimeoutAsync(450);

            Assert.AreEqual(1, await CountAsync(alert.Locator(".tm-spreadsheet-alert__badge")),
                $"{layout}: the severity is one large filled badge");
            // The badge sits on the message's leading edge, left of the text.
            var badgeBox = await alert.Locator(".tm-spreadsheet-alert__badge").BoundingBoxAsync();
            var messageBox = await alert.Locator(".tm-spreadsheet-alert__message").BoundingBoxAsync();
            Assert.IsNotNull(badgeBox);
            Assert.IsNotNull(messageBox);
            Assert.IsTrue(badgeBox!.X + badgeBox.Width <= messageBox!.X + 1,
                $"{layout}: the badge is LEFT of the message, badge ends {badgeBox.X + badgeBox.Width} vs message {messageBox.X}");
            // The reference card has no divider under the title.
            var headerBorder = await alert.Locator(".tm-modal-header").EvaluateAsync<string>(
                "el => getComputedStyle(el).borderBottomWidth");
            Assert.AreEqual("0px", headerBorder, $"{layout}: no divider under the alert title");

            await ShotPhase2Async(page, $"phase4-validation-error--{layout}");
            await page.Keyboard.PressAsync("Escape");
            await alert.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
            await page.CloseAsync();
        }
    }
}
