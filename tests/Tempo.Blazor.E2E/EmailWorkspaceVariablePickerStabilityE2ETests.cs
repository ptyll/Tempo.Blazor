using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Fáze 11 — the F7A_6 carry-forward, pinned as its own regression spec: the variable picker must not
/// REMOUNT while the editor works around it.
/// <para>
/// The historical flake was "fill the filter, then 1.5 s later the box is empty and the list shows every
/// variable again", which made <c>F7A_6</c> read the wrong row roughly half the time. A remount (a new DOM
/// node) is the only way the picker can lose its own <c>_search</c>, so this spec marks the CURRENT node and
/// then watches it across a full auto-save cycle: the marker has to survive, the filter has to stay applied
/// and inserting a token has to keep working. That turns a rare, misleading downstream failure into a precise
/// one, and it is the diagnostic to reach for if the flake ever comes back.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailWorkspaceVariablePickerStabilityE2ETests : EmailTemplateE2ETestBase
{
    [TestMethod]
    public async Task VariablePicker_KeepsItsFilter_AcrossAnAutoSaveCycle()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });

        // Dirty the document first: an auto-save (3 s interval) then re-renders the whole editor while the
        // picker is open, which is exactly the situation the flake needed.
        var subject = page.Locator("[data-tm-prop='Subject'] input");
        await subject.FillAsync("Ahoj ");
        await subject.EvaluateAsync("el => { el.focus(); el.setSelectionRange(5, 5); }");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-email-editor]')?.getAttribute('data-tm-dirty') === 'true'",
            null, new() { Timeout = 15000 });

        var trigger = page.Locator("[data-tm-variable-trigger='Subject'] .tm-popover__trigger");
        await trigger.ClickAsync();
        await page.WaitForSelectorAsync("[data-tm-variable-picker] input[type='search']",
            new() { State = WaitForSelectorState.Visible, Timeout = 20000 });

        // The identity marker: it survives a re-render of the same node and disappears with a remount.
        await page.EvaluateAsync(
            "() => document.querySelector('[data-tm-variable-picker]').setAttribute('data-picker-identity', 'stable')");

        var search = page.Locator("[data-tm-variable-picker] input[type='search']");
        await search.FillAsync("total");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-variable]').length === 1 "
            + "&& document.querySelector(\"[data-tm-variable='order.total']\") !== null",
            null, new() { Timeout = 10000 });

        // Long enough for at least one auto-save round trip (interval 3 s + the API call).
        for (var i = 0; i < 10; i++)
        {
            await page.WaitForTimeoutAsync(600);
            var identity = await page.EvaluateAsync<string?>(
                "() => document.querySelector('[data-tm-variable-picker]')?.getAttribute('data-picker-identity')");
            identity.Should().Be("stable",
                $"the picker must keep its DOM node while the editor auto-saves (checked after {(i + 1) * 600} ms)");
            (await search.InputValueAsync()).Should().Be("total", "the filter text survives the auto-save");
            (await page.Locator("[data-tm-variable]").CountAsync()).Should().Be(1,
                "the FILTERED list survives the auto-save");
        }

        // The lane captures VIEWPORT frames of the editor (F8 convention) — a full-page capture changes the
        // page's metrics, and the editor then re-resolves its layout, which re-creates the overlay panel and
        // with it the picker (a fresh picker starts with an empty filter). This is why the spec measures the
        // filtered state BEFORE any screenshot and why F7A_6 takes a viewport frame for the picker.
        await SaveNamedViewportScreenshotAsync(page, "58b-variable-picker-stable.png");
        (await page.EvaluateAsync<string?>(
            "() => document.querySelector('[data-tm-variable-picker]')?.getAttribute('data-picker-identity')"))
            .Should().Be("stable", "a viewport frame does not re-create the picker");
        (await search.InputValueAsync()).Should().Be("total", "the filter survives the capture");

        // And it still inserts the token it was filtered to.
        await page.Locator("[data-tm-variable]").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => (document.querySelector(\"[data-tm-prop='Subject'] input\")?.value ?? '').includes('{{ order.total }}')",
            null, new() { Timeout = 15000 });
    }

    /// <summary>
    /// Phase 7 (EMAIL-VARIABLE-PICKER-FILTER): a desktop &lt;-&gt; tablet flip re-creates the inspector, and the
    /// editor-owned inspector state brings the OPEN picker and its filter back. Before, the flip closed the
    /// popover (and a fresh picker always started with an empty filter). Closing the picker, on the other hand,
    /// forgets the filter. Frames: viewport PNGs only (a full-page capture would itself re-resolve the layout).
    /// </summary>
    [TestMethod]
    public async Task VariablePicker_KeepsItsFilter_AcrossADesktopTabletLayoutFlip()
    {
        var page = await OpenAsync($"/email-templates/edit/{WelcomeTemplateId}");
        await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 30000 });

        // The document settings (no block selected) are the inspector content the Subject field lives in.
        var trigger = page.Locator("[data-tm-variable-trigger='Subject'] .tm-popover__trigger");
        await trigger.ClickAsync();
        var search = page.Locator("[data-tm-variable-picker] input[type='search']");
        await search.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
        await search.FillAsync("total");
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('[data-tm-variable]').length === 1", null, new() { Timeout = 10000 });
        await SaveNamedViewportScreenshotAsync(page, "58c-variable-picker-filter-desktop.png");

        // Desktop -> Tablet: the inspector is re-created inside the tablet layout's panel.
        await ForceEditorLayoutAsync(page, "Tablet");
        await ExpandInspectorIfRailedAsync(page);

        var tabletSearch = page.Locator("[data-tm-variable-picker] input[type='search']");
        await tabletSearch.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
        (await tabletSearch.InputValueAsync()).Should().Be("total",
            "the filter belongs to the editor's inspector state, not to the picker that the flip unmounted");
        (await page.Locator("[data-tm-variable]").CountAsync()).Should().Be(1, "the filtered list came back too");
        await SaveNamedViewportScreenshotAsync(page, "58d-variable-picker-filter-tablet.png");

        // Tablet -> Desktop: and back again.
        await ForceEditorLayoutAsync(page, "Desktop");
        var desktopSearch = page.Locator("[data-tm-variable-picker] input[type='search']");
        await desktopSearch.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
        (await desktopSearch.InputValueAsync()).Should().Be("total");

        // Closing the picker resets the filter (it is the close, not the flip, that forgets it).
        await page.Locator("[data-tm-variable-trigger='Subject'] .tm-popover__trigger").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('[data-tm-variable-picker]') === null", null, new() { Timeout = 10000 });
        await page.Locator("[data-tm-variable-trigger='Subject'] .tm-popover__trigger").ClickAsync();
        var reopened = page.Locator("[data-tm-variable-picker] input[type='search']");
        await reopened.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 20000 });
        (await reopened.InputValueAsync()).Should().BeEmpty("a closed picker forgets what was typed");
        (await page.Locator("[data-tm-variable]").CountAsync()).Should().BeGreaterThan(1);
    }

    /// <summary>The tablet layout shows ONE panel; expand the inspector from its rail when the palette won.</summary>
    private static async Task ExpandInspectorIfRailedAsync(IPage page)
    {
        var rail = page.Locator("[data-tm-expand-properties]");
        if (await rail.CountAsync() > 0)
        {
            await rail.First.ClickAsync();
        }
    }
}