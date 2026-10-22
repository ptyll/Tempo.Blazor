using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 7A — the editor's working surface in a real browser: the blocks palette (search, wrapping tiles,
/// collapse), the inspector (Content/Style tabs, localized labels, typed editors, header actions), the
/// variable picker (host description/group, caret insertion through <c>tm-email-variable-insert.js</c>) and
/// the new recommending validation level.
/// <para>
/// Screenshots land in <c>__screenshots__/email-templates/</c> and are the UX review material for this
/// phase, including the edge cases the reference does not show: a filtered-empty palette, a collapsed
/// palette in a narrow viewport, a picker with many variables and the panels in dark mode.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailWorkspaceF7AE2ETests : EmailTemplateE2ETestBase
{
    private async Task<IPage> OpenEditorAsync(bool czech = false)
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });
        if (czech) await SwitchLanguageAsync(page, "cs");
        await page.WaitForSelectorAsync("[data-tm-email-editor]", new() { Timeout = 30000 });
        return page;
    }

    private static async Task SelectFirstTextBlockAsync(IPage page)
    {
        await page.Locator("[data-tm-block-id]").First.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-prop-target='block']", new() { Timeout = 15000 });
    }

    /// <summary>
    /// Opens the picker next to <paramref name="field"/>. The click is retried: on a freshly booted WASM
    /// app the first click can land before Blazor has wired the handler, which would look like "the
    /// popover never opens".
    /// </summary>
    private static async Task OpenVariablePickerAsync(IPage page, string field)
    {
        var trigger = page.Locator($"[data-tm-variable-trigger='{field}'] .tm-popover__trigger");
        await trigger.WaitForAsync(new() { Timeout = 15000 });

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (await page.Locator("[data-tm-variable-picker]").CountAsync() > 0)
            {
                // A picker left OPEN by the previous step would make this click CLOSE it — and the node
                // survives the close animation, so a "did it open?" probe would lie and the next Fill would
                // land in a panel that is being removed (measured: the search box stayed EMPTY while the
                // test read an unfiltered row). Close it, wait it out, then really open it.
                await trigger.ClickAsync();
                await WaitForPickerGoneAsync(page);
            }

            await trigger.ClickAsync();
            try
            {
                // VISIBLE and interactive, not merely present: the popover animates in.
                await page.WaitForSelectorAsync("[data-tm-variable-picker] input[type='search']",
                    new() { State = WaitForSelectorState.Visible, Timeout = 5000 });
                return;
            }
            catch (TimeoutException)
            {
                // Blazor had not attached the trigger yet — wait the stale node out and click again.
                await WaitForPickerGoneAsync(page);
            }
        }

        throw new TimeoutException($"The variable picker for '{field}' did not open.");
    }

    /// <summary>Best effort: waits until no variable picker is mounted (it may never have opened).</summary>
    private static async Task WaitForPickerGoneAsync(IPage page)
    {
        try
        {
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-tm-variable-picker]') === null",
                null, new() { Timeout = 3000 });
        }
        catch (TimeoutException)
        {
        }
    }

    /// <summary>
    /// Whether the element's painted text colour is red-dominant — the measured proof that a "danger" action
    /// is actually tinted by the theme (a stylesheet can declare it and never match the child's root).
    /// </summary>
    private static async Task<bool> IsReddishAsync(IPage page, string selector)
        => await page.Locator(selector).First.EvaluateAsync<bool>(
            "el => { const c = getComputedStyle(el).color.match(/\\d+/g).map(Number); " +
            "return c[0] > 120 && c[0] > c[1] + 40 && c[0] > c[2] + 40; }");

    // ── Blocks palette ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7A_1_Toolbox_Search_FiltersByLocalizedName_ClearsOnEscape_AndShowsTheEmptyState()
    {
        var page = await OpenEditorAsync(czech: true);

        var all = await page.Locator("[data-tm-toolbox-list] [data-tm-block]").CountAsync();
        all.Should().Be(14, "the palette shows the whole registry");

        // The editor body is the shell's main region (Fáze 8 replaced the local TmSplitter), a CHILD
        // component's element, so its background rule is alive only through ::deep. A transparent region is
        // the fingerprint of a rule that never matched.
        var shellBackground = await page.Locator(".tm-email-editor__shell").First
            .EvaluateAsync<string>("el => getComputedStyle(el).backgroundColor");
        shellBackground.Should().NotBe("rgba(0, 0, 0, 0)",
            "the workspace surface comes from the shell root through the editor's ::deep rule (Fáze 8: the "
            + "shell owns the background, the regions are transparent)");

        // The desktop editor keeps BOTH docked panels: the palette is a column, not a rail (Fáze 8 moved the
        // collapse control onto the shell, whose rail is the collapsed state).
        (await page.Locator("[data-region='left'] [data-tm-toolbox-list]").CountAsync()).Should().Be(1);
        (await page.Locator("[data-region='rail-left']").CountAsync()).Should().Be(0);

        // Diacritics-insensitive: a Czech user types without them.
        await page.Locator("[data-tm-toolbox-search] input").FillAsync("tlacitko");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-toolbox-list] [data-tm-block]').length === 1",
            null, new() { Timeout = 10000 });
        (await page.Locator("[data-tm-toolbox-list] [data-tm-block]").First.GetAttributeAsync("data-tm-block"))
            .Should().Be("button");
        (await page.Locator("[data-tm-toolbox-list] [data-tm-block='button']").GetAttributeAsync("title"))
            .Should().Be("Tlačítko", "the full name is discoverable even when the label wraps");
        await SaveNamedScreenshotAsync(page, "50-toolbox-search-cs.png");

        // Empty state is a localized sentence that names the term, not a key.
        await page.Locator("[data-tm-toolbox-search] input").FillAsync("xyz");
        await page.WaitForSelectorAsync("[data-tm-toolbox-empty]", new() { Timeout = 10000 });
        var empty = (await page.Locator("[data-tm-toolbox-empty]").InnerTextAsync()).Trim();
        empty.Should().Contain("xyz").And.NotContain("EmailEditor_");
        await SaveNamedScreenshotAsync(page, "51-toolbox-empty-cs.png");

        // Escape clears the field and restores every tile.
        await page.Locator("[data-tm-toolbox-search] input").PressAsync("Escape");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-toolbox-list] [data-tm-block]').length === 14",
            null, new() { Timeout = 10000 });
        (await page.Locator("[data-tm-toolbox-search] input").InputValueAsync()).Should().BeEmpty();

        // Fáze 8: the palette no longer draws its own collapse arrow — the shell's panel toggle is the ONE
        // control for that state (ShowCollapseToggle=false), so the same state cannot be changed twice.
        (await page.Locator("[data-tm-toolbox-collapse]").CountAsync()).Should().Be(0,
            "the shell's panel toggle owns the collapse state");
        (await page.Locator("[data-tm-toolbox-search]").First.IsVisibleAsync()).Should().BeTrue();
        (await page.Locator(".tm-email-editor__panel--toolbox .tm-email-editor__panel-header").CountAsync())
            .Should().Be(0, "the palette owns its header");
    }

    [TestMethod]
    public async Task F7A_2_Toolbox_Collapse_FoldsThePalette_AndBringsItBack()
    {
        // Fáze 8: collapsing the palette is the SHELL's panel rail. On a 1024 px editor (tablet) at most one
        // panel is expanded and the inspector wins by default, so the palette starts railed; expanding it
        // rails the inspector instead. Collapsing it again comes back to the rail.
        var page = await OpenEditorAsync();
        await page.SetViewportSizeAsync(1024, 800);
        await ForceEditorLayoutAsync(page, "Tablet");

        await page.WaitForSelectorAsync("[data-region='rail-left'] [data-tm-rail-block]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-toolbox-list]").CountAsync()).Should().Be(0,
            "a collapsed panel is a rail, not a folded list inside a full-width panel");
        await SaveNamedScreenshotAsync(page, "52-panels-narrow-1024.png");

        await page.Locator("[data-region='rail-left'] button.tm-editor-shell__panel-toggle").ClickAsync();
        await page.WaitForSelectorAsync("[data-region='left'] [data-tm-toolbox-list]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-toolbox-list] [data-tm-block]").First.IsVisibleAsync())
            .Should().BeTrue("the palette is docked again");
        (await page.Locator("aside.tm-editor-shell__panel--right").CountAsync()).Should().Be(0,
            "expanding the palette rails the inspector (one expanded panel on a tablet)");
        await SaveNamedScreenshotAsync(page, "53-toolbox-expanded-1024.png");

        await page.Locator("button.tm-editor-shell__panel-toggle--left").ClickAsync();
        await page.WaitForSelectorAsync("[data-region='rail-left'] [data-tm-rail-block]", new() { Timeout = 15000 });
        (await page.Locator("[data-tm-toolbox-list]").CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task F7A_3_Palette_Tiles_InsertABlock_AndTheLabelNeverCrops()
    {
        var page = await OpenEditorAsync();

        // Two tiles per row, as in the reference: at a 1440 viewport the palette is wide enough for the
        // grid to place the second tile beside the first (a single column made the palette four screens
        // tall and pushed the media/layout groups out of sight).
        var tiles = page.Locator("[data-tm-toolbox-list] [data-tm-block]");
        var first = await tiles.Nth(0).BoundingBoxAsync();
        var second = await tiles.Nth(1).BoundingBoxAsync();
        second!.X.Should().BeGreaterThan(first!.X, "the palette is a two-column grid");
        second.Y.Should().Be(first.Y, "the first two tiles share a row");

        var tile = page.Locator("[data-tm-block='carousel']").First;
        var tileBox = await tile.BoundingBoxAsync();
        var labelBox = await tile.Locator(".tm-email-toolbox__label").BoundingBoxAsync();
        labelBox!.Width.Should().BeLessThanOrEqualTo(tileBox!.Width, "the label wraps inside the tile");
        var labelText = (await tile.Locator(".tm-email-toolbox__label").InnerTextAsync()).Trim();
        (await tile.GetAttributeAsync("title")).Should().Be(labelText);

        var before = await page.Locator("[data-tm-block-id]").CountAsync();
        await tile.ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before + 1}",
            null, new() { Timeout = 15000 });
    }

    // ── Inspector ───────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7A_4_Inspector_SplitsContentAndStyle_AndLabelsFieldsInCzech()
    {
        var page = await OpenEditorAsync(czech: true);

        // No selection → the document inspector, where the subject must read Czech.
        var subjectLabel = (await page.Locator("[data-tm-prop='Subject'] .tm-input-label").InnerTextAsync()).Trim();
        subjectLabel.Should().Be("Předmět", "the inspector labels come from the JSON tables, not from Humanize()");
        (await page.Locator("#tab-content").InnerTextAsync()).Trim().Should().Be("Obsah");
        (await page.Locator("#tab-style").InnerTextAsync()).Trim().Should().Be("Styl");
        await SaveNamedScreenshotAsync(page, "54-inspector-document-cs.png");

        await SelectFirstTextBlockAsync(page);
        (await page.Locator("#tab-content").GetAttributeAsync("aria-selected")).Should().Be("true");
        (await page.Locator("[data-tm-prop='Content']").CountAsync())
            .Should().Be(0, "the text block's content is the rich editor, not a plain field");
        (await page.Locator("[data-tm-rich-content]").CountAsync()).Should().Be(1);

        await page.Locator("#tab-style").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-prop='Color']", new() { Timeout = 15000 });

        // Typed editors: colour picker with the hex, alignment segments, length with a unit.
        (await page.Locator("[data-tm-prop='Color'] .tm-color-picker").CountAsync()).Should().Be(1);
        (await page.Locator("[data-tm-prop='Color']").InnerTextAsync()).Should().Contain("#");
        (await page.Locator("[data-tm-prop='Align'] [data-tm-align-option]").CountAsync()).Should().Be(4);
        (await page.Locator("[data-tm-prop='Align'] [data-tm-align-option][aria-pressed='true']").CountAsync())
            .Should().Be(1, "exactly one alignment segment is pressed");
        (await page.Locator("[data-tm-prop='FontSize'] [data-tm-length-unit] select").CountAsync()).Should().Be(1);
        (await page.Locator("[data-tm-prop-group-toggle='padding']").GetAttributeAsync("aria-expanded"))
            .Should().Be("true");

        // The fields are built with RenderTreeBuilder, so their rules only apply through ::deep. Measured:
        // the number and the unit share a row (a rule that never matched stacked them).
        var numberTop = await page.Locator("[data-tm-prop='FontSize'] [data-tm-length-number]").First
            .EvaluateAsync<double>("el => el.getBoundingClientRect().top");
        var unitTop = await page.Locator("[data-tm-prop='FontSize'] [data-tm-length-unit] select").First
            .EvaluateAsync<double>("el => el.getBoundingClientRect().top");
        Math.Abs(numberTop - unitTop).Should().BeLessThan(4,
            $"the number and its unit sit on one line (number {numberTop}, unit {unitTop})");

        await SaveNamedScreenshotAsync(page, "55-inspector-style-cs.png");

        // An invalid entry shows the localized error, painted in the danger colour (measured, not grepped).
        var fontSize = page.Locator("[data-tm-prop='FontSize'] input[data-tm-length-number]");
        await fontSize.FillAsync("abc");
        await page.WaitForSelectorAsync("[data-tm-field-error='FontSize']", new() { Timeout = 10000 });
        (await IsReddishAsync(page, "[data-tm-field-error='FontSize']")).Should().BeTrue(
            "the field error is tinted danger, so it reads as an error and not as help text");
        await SaveNamedScreenshotAsync(page, "55b-inspector-field-error.png");
        await fontSize.FillAsync("13");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-field-error=\\'FontSize\\']') === null",
            null, new() { Timeout = 10000 });

        // The header actions are reachable without hover.
        (await page.Locator("[data-tm-prop-duplicate]").IsVisibleAsync()).Should().BeTrue(
            "the header actions are reachable without hover");
        (await page.Locator("[data-tm-prop-delete]").IsVisibleAsync()).Should().BeTrue();

        // Delete reads as DANGER, not as a second ghost button. The class sits on the TmButton's own root,
        // so this only holds when the rule is scoped with ::deep — measured, because a stylesheet assertion
        // says nothing about what the browser paints.
        var deleteColor = await page.Locator("[data-tm-prop-delete]").First
            .EvaluateAsync<string>("el => getComputedStyle(el).color");
        var duplicateColor = await page.Locator("[data-tm-prop-duplicate]").First
            .EvaluateAsync<string>("el => getComputedStyle(el).color");
        (await IsReddishAsync(page, "[data-tm-prop-delete]")).Should().BeTrue(
            $"the Delete action is tinted danger (measured {deleteColor} vs the duplicate's {duplicateColor})");

        // Changing an alignment segment writes the model (the canvas re-renders with the new alignment).
        await page.Locator("[data-tm-prop='Align'] [data-tm-align-option='right']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-prop=\\'Align\\'] [data-tm-align-option=\\'right\\']')?.getAttribute('aria-pressed') === 'true'",
            null, new() { Timeout = 10000 });
        (await page.Locator("[data-tm-email-editor]").GetAttributeAsync("data-tm-dirty")).Should().Be("true");

        // Collapsing the spacing group is remembered PER BLOCK TYPE: the same type keeps it folded, a
        // different type starts expanded.
        await page.Locator("[data-tm-prop-group-toggle='padding']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-prop-group-body=\\'padding\\']')?.hasAttribute('hidden') === true",
            null, new() { Timeout = 10000 });

        // The inspector remembers a folded group PER BLOCK TYPE, so the second block must be a DIFFERENT type.
        // (The accessible name is unusable for this since the F9A UX review: it carries the brief content too,
        // so two text blocks have different names while sharing one remembered fold state.)
        var blockKinds = await page.Locator("[data-tm-canvas] [data-tm-block-id]")
            .EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.getAttribute('data-tm-block-kind'))");
        var firstTitle = blockKinds[0];
        var otherIndex = Array.FindIndex(blockKinds, kind => kind != firstTitle);
        if (otherIndex > 0)
        {
            await page.Locator("[data-tm-canvas] [data-tm-block-id]").Nth(otherIndex).ClickAsync();
            await page.Locator("#tab-style").ClickAsync();
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-tm-prop-group-body=\\'padding\\']')?.hasAttribute('hidden') === false",
                null, new() { Timeout = 10000 });

            await page.Locator("[data-tm-canvas] [data-tm-block-id]").First.ClickAsync();
            await page.Locator("#tab-style").ClickAsync();
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-tm-prop-group-body=\\'padding\\']')?.hasAttribute('hidden') === true",
                null, new() { Timeout = 10000 });
        }
    }

    [TestMethod]
    public async Task F7A_5_Inspector_DuplicateAndDelete_AreOneUndoStepEach()
    {
        var page = await OpenEditorAsync();
        await SelectFirstTextBlockAsync(page);

        var before = await page.Locator("[data-tm-block-id]").CountAsync();
        await page.Locator("[data-tm-prop-duplicate]").ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before + 1}",
            null, new() { Timeout = 15000 });

        await page.Locator("[data-tm-undo]").ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before}",
            null, new() { Timeout = 15000 });

        await page.Locator("[data-tm-block-id]").First.ClickAsync();
        await page.Locator("[data-tm-prop-delete]").ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before - 1}",
            null, new() { Timeout = 15000 });

        // The polite live region names the outcome (the row is simply gone otherwise), and focus did not
        // fall to <body> with the removed header: it is on a block or on the canvas root.
        var announced = (await page.Locator("[data-tm-save-announcer]").InnerTextAsync()).Trim();
        announced.Should().NotBeEmpty("a delete is announced with the undo chord");
        var focusInsideCanvas = await page.EvaluateAsync<bool>(
            "() => { const a = document.activeElement; " +
            "return !!a && a.closest('[data-tm-canvas]') !== null; }");
        focusInsideCanvas.Should().BeTrue("focus is handed back into the canvas");
        await SaveNamedScreenshotAsync(page, "55c-delete-announced.png");

        await page.Locator("[data-tm-undo]").ClickAsync();
        await page.WaitForFunctionAsync(
            $"() => document.querySelectorAll('[data-tm-block-id]').length === {before}",
            null, new() { Timeout = 15000 });
    }

    // ── Variable picker ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7A_6_VariablePicker_ShowsHostDescription_AndInsertsAtTheCaret()
    {
        var page = await OpenEditorAsync();

        var subject = page.Locator("[data-tm-prop='Subject'] input");
        await subject.FillAsync("Ahoj ");
        await subject.EvaluateAsync("el => { el.focus(); el.setSelectionRange(5, 5); }");

        await OpenVariablePickerAsync(page, "Subject");

        var row = page.Locator("[data-tm-variable='user.first_name']");
        (await row.InnerTextAsync()).Should().Contain("{{ user.first_name }}")
            .And.Contain("First name of the user", "the host's description rides next to the token");
        (await page.Locator("[data-tm-variable-group]").AllInnerTextsAsync())
            .Should().NotBeEmpty("host groups head the rows");
        await SaveNamedScreenshotAsync(page, "56-variable-picker.png");

        await row.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-prop=\\'Subject\\'] input')?.value === 'Ahoj {{ user.first_name }}'",
            null, new() { Timeout = 15000 });

        // Searching by the DESCRIPTION must find the token the user cannot name, and clicking that row must
        // insert it (the search box must never become the insertion target).
        await OpenVariablePickerAsync(page, "Subject");
        await page.Locator("[data-tm-variable-picker] input[type='search']").FillAsync("e-mail address");
        // Wait for the FILTERED row the search must produce, not for a global row count: a leftover
        // unfiltered/unmounted picker can satisfy a count while the real list is still rendering.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-variable]').length === 1 "
            + "&& document.querySelector(\"[data-tm-variable='user.email']\") !== null",
            null, new() { Timeout = 10000 });
        var emailRow = page.Locator("[data-tm-variable]").First;
        (await emailRow.GetAttributeAsync("data-tm-variable")).Should().Be("user.email");
        await emailRow.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-tm-prop=\\'Subject\\'] input')?.value ?? '').includes('{{ user.email }}')",
            null, new() { Timeout = 15000 });

        // The popover animates in (opacity 0 → 1): a screenshot taken too early shows a half-transparent
        // panel and a click can land on a moving target.
        await OpenVariablePickerAsync(page, "Subject");
        var totalSearch = page.Locator("[data-tm-variable-picker] input[type='search']");
        await totalSearch.FillAsync("total");
        // The fill has to LAND (the picker could have been remounted under it) and the list has to be the
        // filtered one before the row is read.
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-tm-variable-picker] input[type='search']\")?.value === 'total'",
            null, new() { Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-variable]').length === 1 "
            + "&& document.querySelector(\"[data-tm-variable='order.total']\") !== null",
            null, new() { Timeout = 10000 });
        await page.WaitForFunctionAsync(
            "() => { const panel = document.querySelector('[data-tm-variable-picker]')?.closest('.tm-popover__body'); " +
            "return !!panel && getComputedStyle(panel).opacity === '1'; }",
            null, new() { Timeout = 10000 });

        // Read the filtered row BEFORE the screenshot, and take the VIEWPORT frame rather than a full-page
        // one: a full-page capture changes the page's captured metrics, the editor re-resolves its layout and
        // the overlay panel's content is re-created — the picker then legitimately starts from an empty filter
        // and this assertion reads the first unfiltered row (the F7A_6 flake, reproduced in Fáze 11 by
        // EmailWorkspaceVariablePickerStabilityE2ETests).
        var totalRow = page.Locator("[data-tm-variable]").First;
        (await totalRow.GetAttributeAsync("data-tm-variable")).Should().Be("order.total");
        await SaveNamedViewportScreenshotAsync(page, "57-variable-picker-search.png");

        await totalRow.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => (document.querySelector('[data-tm-prop=\\'Subject\\'] input')?.value ?? '').includes('{{ order.total }}')",
            null, new() { Timeout = 15000 });
    }

    [TestMethod]
    public async Task F7A_7_Validation_ReportsTheLongPreheaderAsInfo()
    {
        var page = await OpenEditorAsync();

        var preheader = page.Locator("[data-tm-prop='Preheader'] input");
        await preheader.FillAsync(new string('x', 120));
        await preheader.BlurAsync();

        await page.Locator("[data-tm-validate-btn]").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-validation-severity='Info']", new() { Timeout = 15000 });
        var info = page.Locator("[data-tm-validation-severity='Info']").First;
        (await info.InnerTextAsync()).Should().Contain("100", "the rule explains its threshold");
        (await info.Locator("[data-tm-validation-icon]").CountAsync()).Should().Be(1,
            "the severity is not signalled by colour alone");
        await SaveNamedScreenshotAsync(page, "58-validation-info.png");

        // Findings are ordered Error → Warning → Info, never by the enum's numeric value.
        var severities = await page.Locator("[data-tm-validation-message]")
            .EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.getAttribute('data-tm-validation-severity'))");
        var ranked = severities.Select(s => s switch { "Error" => 0, "Warning" => 1, _ => 2 }).ToList();
        ranked.Should().BeInAscendingOrder();
    }

    // ── Edge cases for the UX review ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task F7A_8_Panels_InDarkMode_AndAtANarrowViewport()
    {
        var page = await OpenEditorAsync();

        await SelectFirstTextBlockAsync(page);
        await page.Locator("#tab-style").ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-prop='Color']", new() { Timeout = 15000 });

        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !!document.querySelector('[data-theme=\\'dark\\']')",
            null, new() { Timeout = 10000 });
        await SaveNamedScreenshotAsync(page, "59-panels-dark.png");

        await page.SetViewportSizeAsync(1024, 800);
        await page.WaitForTimeoutAsync(300);
        await SaveNamedScreenshotAsync(page, "60-panels-dark-1024.png");
    }
}
