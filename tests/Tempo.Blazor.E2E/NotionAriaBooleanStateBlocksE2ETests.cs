using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4k — the boolean ARIA states of the NotionEditor BLOCKS and DATABASE render
/// as STRINGS in both positions: the open disclosure carries <c>aria-expanded="true"</c>, the
/// closed one <c>"false"</c>; the current tab/option/mode carries <c>"true"</c>, every inactive
/// one <c>"false"</c> — never the invalid empty value a bool produced, never a missing attribute.
/// The touched surfaces are exercised in a real browser on <c>/notion-editor</c>: the toggle
/// block arrow, the database view tabs, the group bar field listbox, the table group toggle, the
/// import/export dialog tabs, the field editor option color dots and the work item block
/// display-mode buttons. A scoped axe scan (aria-valid-attr-value) runs in both themes plus a
/// phone pass, and screenshots land in <c>__screenshots__/aria-boolean-states/</c> for the UX
/// review — the visual state must be unchanged: every surface styles the state through BEM
/// classes, never through attribute presence (the key presence-selector risk; the CSS/JS sweep
/// found no <c>[aria-expanded]</c>-style presence selector and no <c>hasAttribute('aria-…')</c>
/// read in the package).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class NotionAriaBooleanStateBlocksE2ETests : NotionE2ETestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string ToggleWithChildrenBlockId = "eb200000-0000-0000-0000-000000000010";
    private const string WorkItemCardBlockId = "cf500000-0000-0000-0000-000000000010";

    private readonly List<string> _clientErrors = [];

    [TestCleanup]
    public void Cleanup() => AssertNoClientErrors();

    // ── Toggle block — the header arrow ────────────────────────────────────────────────────

    [TestMethod]
    [Description("The toggle block arrow announces aria-expanded as a string in both states")]
    public async Task ToggleBlock_Arrow_AnnouncesTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await SeedListTodoPageAsync();

        var toggle = page.Locator($"[data-block-id='{ToggleWithChildrenBlockId}'] .tm-notion-toggle").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await toggle.ScrollIntoViewIfNeededAsync();

        var arrow = toggle.Locator(".tm-notion-toggle__arrow").First;
        await Assertions.Expect(arrow).ToHaveAttributeAsync("aria-expanded", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        await arrow.ClickAsync();
        await Assertions.Expect(arrow).ToHaveAttributeAsync("aria-expanded", "false",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        Assert.AreEqual(0, await toggle.Locator(".tm-notion-toggle__children").CountAsync(),
            "the collapsed toggle renders no children container");

        await arrow.ClickAsync();
        await Assertions.Expect(arrow).ToHaveAttributeAsync("aria-expanded", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        await toggle.Locator(".tm-notion-toggle__children").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var scope = $"[data-block-id='{ToggleWithChildrenBlockId}']";
        await AssertNoAxeViolationsAsync(page, scope, "toggle block");
        await CaptureScreenAsync(page, scope, "fze-4k-notion-toggle-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, scope);
        await AssertNoAxeViolationsAsync(page, scope, "toggle block (dark)");
        await CaptureScreenAsync(page, scope, "fze-4k-notion-toggle-dark-1600.png");
    }

    // ── Database block — view tabs, group bar, group toggle, import/export, field editor ───

    [TestMethod]
    [Description("The database view tabs announce aria-selected as a string and the state moves with the active view")]
    public async Task DatabaseBlock_ViewTabs_AnnounceTheStringState()
    {
        var page = await OpenDatabasePageAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        var tabs = db.Locator("[data-testid='notion-db-view-tab']");
        Assert.IsTrue(await tabs.CountAsync() >= 2, "the seeded database has at least two view tabs");
        await AssertExactlyOneSelectedAsync(tabs, "database view tabs");

        var active = await SelectedIndexAsync(tabs);
        var target = (active + 1) % await tabs.CountAsync();
        await tabs.Nth(target).ClickAsync();
        await Assertions.Expect(db.Locator("[data-testid='notion-db-view-tab']").Nth(target))
            .ToHaveAttributeAsync("aria-selected", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        await AssertExactlyOneSelectedAsync(db.Locator("[data-testid='notion-db-view-tab']"),
            "database view tabs after the switch");

        await AssertNoAxeViolationsAsync(page, ".tm-db", "database block");
        await CaptureScreenAsync(page, ".tm-db", "fze-4k-notion-db-view-tabs-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, ".tm-db");
        await AssertNoAxeViolationsAsync(page, ".tm-db", "database block (dark)");
        await CaptureScreenAsync(page, ".tm-db", "fze-4k-notion-db-view-tabs-dark-1600.png");
    }

    [TestMethod]
    [Description("The group bar options and the table group toggles announce their string state; collapsing a group flips aria-expanded to \"false\"")]
    public async Task DatabaseBlock_GroupBarAndGroupToggle_AnnounceTheStringState()
    {
        var page = await OpenDatabasePageAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Open the Group panel and pick the Status field.
        await ClickDatabaseToolbarButtonAsync(db, "Group");
        var groupBar = db.Locator(".tm-dbgb").First;
        await groupBar.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var options = groupBar.Locator(".tm-dbgb__field-item");
        Assert.IsTrue(await options.CountAsync() >= 2, "No grouping plus the groupable fields");
        await AssertExactlyOneSelectedAsync(options, "group bar options");
        Assert.AreEqual("true", await options.Nth(0).GetAttributeAsync("aria-selected"),
            "No grouping starts selected — the string \"true\"");

        await options.Filter(new LocatorFilterOptions { HasText = "Status" }).First.ClickAsync();
        await Assertions.Expect(
                groupBar.Locator(".tm-dbgb__field-item").Filter(new LocatorFilterOptions { HasText = "Status" }).First)
            .ToHaveAttributeAsync("aria-selected", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        await AssertExactlyOneSelectedAsync(groupBar.Locator(".tm-dbgb__field-item"),
            "group bar options after picking Status");

        await AssertNoAxeViolationsAsync(page, ".tm-dbgb", "group bar");
        await CaptureScreenAsync(page, ".tm-dbgb", "fze-4k-notion-db-group-bar-light-1600.png");

        // The table now renders one toggle per group — all start expanded.
        var groupToggles = db.Locator("[data-testid='notion-db-group-toggle']");
        await groupToggles.First.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.IsTrue(await groupToggles.CountAsync() >= 1, "grouping renders at least one group row");
        for (var i = 0; i < await groupToggles.CountAsync(); i++)
        {
            Assert.AreEqual("true", await groupToggles.Nth(i).GetAttributeAsync("aria-expanded"),
                $"group #{i} starts expanded — the string \"true\"");
        }

        await groupToggles.First.ClickAsync();
        await Assertions.Expect(db.Locator("[data-testid='notion-db-group-toggle']").First)
            .ToHaveAttributeAsync("aria-expanded", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        await db.Locator("[data-testid='notion-db-group-toggle']").First.ClickAsync();
        await Assertions.Expect(db.Locator("[data-testid='notion-db-group-toggle']").First)
            .ToHaveAttributeAsync("aria-expanded", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        await AssertNoAxeViolationsAsync(page, ".tm-db", "grouped table");
        await CaptureScreenAsync(page, ".tm-db", "fze-4k-notion-db-grouped-table-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, ".tm-db");
        await AssertNoAxeViolationsAsync(page, ".tm-db", "grouped table (dark)");
        await CaptureScreenAsync(page, ".tm-db", "fze-4k-notion-db-grouped-table-dark-1600.png");
    }

    [TestMethod]
    [Description("The import/export dialog tabs announce aria-selected as a string in both positions")]
    public async Task DatabaseBlock_ImportExportTabs_AnnounceTheStringState()
    {
        var page = await OpenDatabasePageAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        await ClickDatabaseToolbarButtonAsync(db, "Import / Export", "notion-db-import-export");
        var dialog = page.Locator(".tm-dbie").First;
        await dialog.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var tabs = dialog.Locator(".tm-dbie__tab");
        Assert.AreEqual(2, await tabs.CountAsync(), "Import and Export");
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the Import tab starts active — the string \"true\"");
        Assert.AreEqual("false", await tabs.Nth(1).GetAttributeAsync("aria-selected"),
            "the Export tab keeps the attribute — the string \"false\"");

        await tabs.Nth(1).ClickAsync();
        await Assertions.Expect(dialog.Locator(".tm-dbie__tab").Nth(1))
            .ToHaveAttributeAsync("aria-selected", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        await Assertions.Expect(dialog.Locator(".tm-dbie__tab").Nth(0))
            .ToHaveAttributeAsync("aria-selected", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        await AssertNoAxeViolationsAsync(page, ".tm-dbie", "import/export dialog");
        await CaptureScreenAsync(page, ".tm-dbie", "fze-4k-notion-db-import-export-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, ".tm-dbie");
        await AssertNoAxeViolationsAsync(page, ".tm-dbie", "import/export dialog (dark)");
        await CaptureScreenAsync(page, ".tm-dbie", "fze-4k-notion-db-import-export-dark-1600.png");
    }

    [TestMethod]
    [Description("The field editor option color dots form a radiogroup and announce aria-checked as a string — exactly the option's color carries \"true\"")]
    public async Task DatabaseBlock_FieldEditorColorDots_AnnounceTheStringState()
    {
        var page = await OpenDatabasePageAsync();
        var db = await NavigateToDatabaseBlockAsync(page);

        // Priority is the seeded SELECT field — its editor shows the option color dots.
        await db.Locator(".tm-dbt__th-name").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await db.Locator(".tm-dbt__th-inner").Filter(new LocatorFilterOptions { HasText = "Priority" })
            .First.ClickAsync();
        var editor = page.Locator(".tm-dbfe").First;
        await editor.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var optionRow = editor.Locator(".tm-dbfe__opt-row").First;
        await optionRow.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var dots = optionRow.Locator(".tm-dbfe__color-dot");
        var dotCount = await dots.CountAsync();
        Assert.IsTrue(dotCount > 1, "the option row offers the color palette");

        // The seeded Priority options carry HEX colors, so no palette dot starts checked — every
        // dot must still carry the STRING state. Picking a dot checks exactly that one.
        for (var i = 0; i < dotCount; i++)
        {
            var value = await dots.Nth(i).GetAttributeAsync("aria-checked");
            Assert.IsTrue(value is "true" or "false",
                $"color dot #{i} must carry the string state, got '{value ?? "<missing>"}'");
        }

        await dots.Nth(1).ClickAsync();
        await Assertions.Expect(optionRow.Locator(".tm-dbfe__color-dot").Nth(1))
            .ToHaveAttributeAsync("aria-checked", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        var states = new string?[dotCount];
        for (var i = 0; i < dotCount; i++)
        {
            states[i] = await optionRow.Locator(".tm-dbfe__color-dot").Nth(i).GetAttributeAsync("aria-checked");
        }

        AssertStringStatesExactlyOneTrue(states, "field editor color dots after the pick");

        // Picking another dot MOVES the single "true" — it never multiplies.
        await optionRow.Locator(".tm-dbfe__color-dot").Nth(2).ClickAsync();
        await Assertions.Expect(optionRow.Locator(".tm-dbfe__color-dot").Nth(2))
            .ToHaveAttributeAsync("aria-checked", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        await Assertions.Expect(optionRow.Locator(".tm-dbfe__color-dot").Nth(1))
            .ToHaveAttributeAsync("aria-checked", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        await AssertNoAxeViolationsAsync(page, ".tm-dbfe", "field editor");
        await CaptureScreenAsync(page, ".tm-dbfe", "fze-4k-notion-db-field-editor-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, ".tm-dbfe");
        await AssertNoAxeViolationsAsync(page, ".tm-dbfe", "field editor (dark)");
        await CaptureScreenAsync(page, ".tm-dbfe", "fze-4k-notion-db-field-editor-dark-1600.png");
    }

    // ── Work item block — display-mode buttons ─────────────────────────────────────────────

    [TestMethod]
    [Description("The work item display-mode buttons announce aria-pressed as a string and the state moves with the picked mode")]
    public async Task WorkItemBlock_DisplayModes_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await SeedWorkItemsPageAsync();

        var block = page.Locator($"[data-block-id='{WorkItemCardBlockId}']").First;
        await block.ScrollIntoViewIfNeededAsync();
        var modes = block.Locator(".tm-work-item__mode");
        Assert.AreEqual(3, await modes.CountAsync(), "Card, List and Inline");
        Assert.AreEqual("true", await modes.Nth(0).GetAttributeAsync("aria-pressed"),
            "the Card mode starts active — the string \"true\"");
        Assert.AreEqual("false", await modes.Nth(1).GetAttributeAsync("aria-pressed"),
            "an inactive mode keeps the attribute — the string \"false\"");
        Assert.AreEqual("false", await modes.Nth(2).GetAttributeAsync("aria-pressed"));

        await modes.Nth(1).ClickAsync(); // List
        await block.Locator(".tm-work-item--list").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await Assertions.Expect(block.Locator(".tm-work-item__mode").Nth(1))
            .ToHaveAttributeAsync("aria-pressed", "true",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });
        await Assertions.Expect(block.Locator(".tm-work-item__mode").Nth(0))
            .ToHaveAttributeAsync("aria-pressed", "false",
                new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        var scope = $"[data-block-id='{WorkItemCardBlockId}']";
        await AssertNoAxeViolationsAsync(page, scope, "work item block");
        await CaptureScreenAsync(page, scope, "fze-4k-notion-work-item-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, scope);
        await AssertNoAxeViolationsAsync(page, scope, "work item block (dark)");
        await CaptureScreenAsync(page, scope, "fze-4k-notion-work-item-dark-1600.png");
    }

    // ── Phone pass — the toggle block on a 390 px viewport ─────────────────────────────────

    [TestMethod]
    [Description("Phone pass: the toggle block arrow keeps the string aria-expanded state and the surface is axe-clean on a 390 px viewport")]
    public async Task ToggleBlock_Phone_AnnouncesTheStringState()
    {
        var page = await OpenNotionEditorAsync(390, 844);
        HookClientErrors(page);
        await SeedListTodoPageAsync();

        var toggle = page.Locator($"[data-block-id='{ToggleWithChildrenBlockId}'] .tm-notion-toggle").First;
        await toggle.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await toggle.ScrollIntoViewIfNeededAsync();

        var arrow = toggle.Locator(".tm-notion-toggle__arrow").First;
        await Assertions.Expect(arrow).ToHaveAttributeAsync("aria-expanded", "true",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        await arrow.ClickAsync();
        await Assertions.Expect(arrow).ToHaveAttributeAsync("aria-expanded", "false",
            new LocatorAssertionsToHaveAttributeOptions { Timeout = 8000 });

        var scope = $"[data-block-id='{ToggleWithChildrenBlockId}']";
        await AssertNoAxeViolationsAsync(page, scope, "toggle block (phone)");
        await CaptureScreenAsync(page, scope, "fze-4k-notion-toggle-phone-390.png");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds the default demo database BEFORE the page opens (the reset leaves the
    /// database store alone, so the block renders the seeded fields/views on load).</summary>
    private async Task<IPage> OpenDatabasePageAsync()
    {
        await SeedDatabaseAsync("default");
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        return page;
    }

    private static async Task<ILocator> NavigateToDatabaseBlockAsync(IPage page)
    {
        var db = page.Locator(".tm-db").First;
        await db.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await db.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);
        await page.WaitForSelectorAsync(".tm-db__skeleton",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Hidden, Timeout = 10000 });
        await page.WaitForTimeoutAsync(300);
        return db;
    }

    /// <summary>Clicks a database toolbar button by its visible label, falling back to the
    /// toolbar "More" menu when the bar collapsed the entry.</summary>
    private static async Task ClickDatabaseToolbarButtonAsync(ILocator db, string buttonText, string? testId = null)
    {
        if (testId is not null)
        {
            var byId = db.GetByTestId(testId);
            if (await byId.CountAsync() > 0 && await byId.First.IsVisibleAsync())
            {
                await byId.First.ClickAsync();
                await Task.Delay(600);
                return;
            }

            await db.Locator(".tm-toolbar-more").ClickAsync();
            var menuItem = db.Page.Locator($"[data-menu-testid='{testId}'], [data-testid='{testId}']").Last;
            await menuItem.ClickAsync(new LocatorClickOptions { Force = true });
            await Task.Delay(600);
            return;
        }

        var btn = db.Locator(".tm-db__tool-btn, [data-tool]").Filter(new LocatorFilterOptions { HasText = buttonText });
        if (await btn.CountAsync() == 0 || !await btn.First.IsVisibleAsync())
        {
            await db.Locator(".tm-toolbar-more").ClickAsync();
            await db.Page.Locator("[role='menuitem']").Filter(new LocatorFilterOptions { HasText = buttonText })
                .First.ClickAsync(new LocatorClickOptions { Force = true });
            return;
        }

        await btn.WaitForAsync(new LocatorWaitForOptions { Timeout = 5000 });
        await btn.ClickAsync();
        await Task.Delay(600);
    }

    /// <summary>Every option carries the attribute and exactly one carries "true" — the
    /// presence-selector risk is that a stale rule would highlight more.</summary>
    private static async Task AssertExactlyOneSelectedAsync(ILocator options, string label)
    {
        var count = await options.CountAsync();
        Assert.IsTrue(count > 0, $"{label}: at least one option must render");
        var trueCount = 0;
        for (var i = 0; i < count; i++)
        {
            var value = await options.Nth(i).GetAttributeAsync("aria-selected");
            Assert.IsTrue(value is "true" or "false",
                $"{label}: option #{i} must carry the string state, got '{value ?? "<missing>"}'");
            if (value == "true") trueCount++;
        }

        Assert.AreEqual(1, trueCount, $"{label}: exactly one option carries aria-selected=\"true\"");
    }

    private static async Task<int> SelectedIndexAsync(ILocator options)
    {
        var count = await options.CountAsync();
        for (var i = 0; i < count; i++)
        {
            if (await options.Nth(i).GetAttributeAsync("aria-selected") == "true") return i;
        }

        Assert.Fail("no option carries aria-selected=\"true\"");
        return -1;
    }

    private static void AssertStringStatesExactlyOneTrue(string?[] states, string label)
    {
        Assert.IsTrue(states.Length > 0, $"{label}: at least one option must render");
        foreach (var value in states)
        {
            Assert.IsTrue(value is "true" or "false",
                $"{label}: every option must carry the string state, got '{value ?? "<missing>"}'");
        }

        Assert.AreEqual(1, states.Count(value => value == "true"),
            $"{label}: exactly one option carries the string \"true\"");
    }

    private void HookClientErrors(IPage page)
        => page.PageError += (_, error) => { lock (_clientErrors) _clientErrors.Add("PAGEERROR: " + error); };

    private void AssertNoClientErrors()
    {
        lock (_clientErrors)
        {
            Assert.IsTrue(_clientErrors.Count == 0,
                "Unhandled client-side errors occurred:\n" + string.Join("\n", _clientErrors));
        }
    }

    /// <summary>The demo shell's sticky header would cover the top of the element in a screenshot;
    /// hide it for the capture and restore it right after.</summary>
    private static Task SetStickyHeaderVisibleAsync(IPage page, bool visible)
        => page.EvaluateAsync(
            """
            (visible) => {
                const header = document.querySelector('[data-demo-shell="main"] > .sticky');
                if (header) {
                    header.style.visibility = visible ? '' : 'hidden';
                }
            }
            """, visible);

    private async Task CaptureScreenAsync(IPage page, string selector, string fileName)
    {
        var directory = Path.Combine(
            FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "aria-boolean-states");
        Directory.CreateDirectory(directory);
        await WaitForLayoutToSettleAsync(page, selector);
        await AssertNoBlazorErrorUiAsync(page);
        await SetStickyHeaderVisibleAsync(page, false);
        try
        {
            var path = Path.Combine(directory, fileName);
            await page.Locator(selector).First.ScreenshotAsync(new LocatorScreenshotOptions { Path = path });
            TestContext.AddResultFile(path);
        }
        finally
        {
            await SetStickyHeaderVisibleAsync(page, true);
        }
    }

    private static async Task AssertNoAxeViolationsAsync(IPage page, string selector, string label)
    {
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });
        var violations = await page.EvaluateAsync<string[]>(
            """
            async (selector) => {
                const host = document.querySelector(selector) || document.body;
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'rule', values: ['aria-valid-attr-value'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """,
            selector);
        Assert.AreEqual(0, violations.Length,
            $"axe critical/serious violations on the {label}:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static DirectoryInfo FindRepoRoot()
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

        throw new InvalidOperationException("repo root not found");
    }
}
