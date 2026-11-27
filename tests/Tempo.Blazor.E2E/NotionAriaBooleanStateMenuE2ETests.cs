using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan b75365fb Fáze 4j — the boolean ARIA states of the NotionEditor MENUS and SEARCH render as
/// STRINGS in both positions: the selected tab/option carries <c>aria-selected="true"</c>, every
/// inactive one <c>"false"</c> — never the invalid empty value a bool produced, never a missing
/// attribute. The touched surfaces are exercised in a real browser on <c>/notion-editor</c>: the
/// @@ mention menu (People/Pages/Date tabs + option lists with arrow navigation), the block
/// comment mention dropdown, the page-icon emoji picker category tabs, the <c>{{</c> token
/// dropdown, the block type switcher (the demo card — the component is not wired into the editor
/// chrome) and the Ctrl+P page search results (pages + blocks across the section boundary). A
/// scoped axe scan (aria-valid-attr-value) runs in both themes and screenshots land in
/// <c>__screenshots__/aria-boolean-states/</c> for the UX review — the visual state must be
/// unchanged: exactly ONE option is highlighted, because every surface styles the state through
/// BEM classes, never through attribute presence (the key presence-selector risk).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class NotionAriaBooleanStateMenuE2ETests : NotionE2ETestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";
    private const string MentionMenu = ".tm-nmm";
    private const string CommentDropdown = ".tm-comment-mention-dropdown";
    private const string EmojiPicker = ".tm-notion-emoji-picker__panel";
    private const string TokenDropdown = ".tm-notion-token-dropdown";
    private const string TypeSwitcher = ".tm-notion-type-switcher";
    private const string PageSearch = ".tm-nps";

    private readonly List<string> _clientErrors = [];

    [TestCleanup]
    public void Cleanup() => AssertNoClientErrors();

    // ── @@ mention menu — tabs + option lists ─────────────────────────────────────────────

    [TestMethod]
    [Description("The mention menu tabs and option lists announce aria-selected as a string; arrows move the single selected option")]
    public async Task MentionMenu_TabsAndOptions_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await TypeTriggerAsync(page, "@");

        var menu = page.Locator(MentionMenu).First;
        await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Tabs: People starts active — the string "true"; Pages/Date keep the attribute.
        var tabs = menu.Locator(".tm-nmm__tab");
        Assert.AreEqual(3, await tabs.CountAsync(), "People, Pages and Date");
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "the active tab announces the string \"true\"");
        Assert.AreEqual("false", await tabs.Nth(1).GetAttributeAsync("aria-selected"),
            "an inactive tab keeps the attribute — the string \"false\"");
        Assert.AreEqual("false", await tabs.Nth(2).GetAttributeAsync("aria-selected"));

        // People options load from the seeded provider: exactly one carries "true".
        await menu.Locator(".tm-nmm__item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await AssertExactlyOneSelectedAsync(menu.Locator(".tm-nmm__item"), "mention people options");

        // ArrowDown moves the string "true" to the next option.
        var before = await SelectedIndexAsync(menu.Locator(".tm-nmm__item"));
        await menu.Locator(".tm-nmm__search-input").First.PressAsync("ArrowDown");
        await Assertions.Expect(menu.Locator(".tm-nmm__item").Nth((before + 1) % await menu.Locator(".tm-nmm__item").CountAsync()))
            .ToHaveAttributeAsync("aria-selected", "true");
        await AssertExactlyOneSelectedAsync(menu.Locator(".tm-nmm__item"), "mention people options after ArrowDown");

        // The Pages tab flips the tab state and lists pages with the same string contract.
        await tabs.Nth(1).ClickAsync();
        await Assertions.Expect(menu.Locator(".tm-nmm__tab").Nth(1))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(menu.Locator(".tm-nmm__tab").Nth(0))
            .ToHaveAttributeAsync("aria-selected", "false");
        await menu.Locator(".tm-nmm__item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await AssertExactlyOneSelectedAsync(menu.Locator(".tm-nmm__item"), "mention page options");

        // The Date tab has three fixed options with arrow navigation.
        await menu.Locator(".tm-nmm__tab").Nth(2).ClickAsync();
        await Assertions.Expect(menu.Locator(".tm-nmm__tab").Nth(2))
            .ToHaveAttributeAsync("aria-selected", "true");
        Assert.AreEqual(3, await menu.Locator(".tm-nmm__item").CountAsync(), "Today, Tomorrow and Yesterday");
        await AssertExactlyOneSelectedAsync(menu.Locator(".tm-nmm__item"), "mention date options");
        await menu.Locator(".tm-nmm__search-input").First.PressAsync("ArrowDown");
        await Assertions.Expect(menu.Locator(".tm-nmm__item").Nth(1))
            .ToHaveAttributeAsync("aria-selected", "true");

        await AssertNoAxeViolationsAsync(page, MentionMenu, "mention menu");
        await CaptureScreenAsync(page, MentionMenu, "fze-4j-notion-mention-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, MentionMenu);
        await AssertNoAxeViolationsAsync(page, MentionMenu, "mention menu (dark)");
        await CaptureScreenAsync(page, MentionMenu, "fze-4j-notion-mention-dark-1600.png");
    }

    // ── Block comment mention dropdown ─────────────────────────────────────────────────────

    [TestMethod]
    [Description("The comment mention dropdown options announce aria-selected as a string; arrows move the single selected option")]
    public async Task CommentMention_Options_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);

        // Open the block comment panel through the block handle menu.
        var firstBlock = page.Locator("[data-notion-block]").First;
        await firstBlock.HoverAsync();
        await firstBlock.Locator(".tm-notion-handle__btn").Last.ClickAsync();
        await page.Locator(".tm-notion-ctx__item:has-text('Comment')").First.ClickAsync();
        await page.Locator(".tm-nbcp").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var textarea = page.Locator(".tm-nbcp__reply-input").First;
        // "@a" matches several seeded users, so the arrow walk moves between real options.
        await textarea.FillAsync("Hi @a");
        var dropdown = page.Locator(CommentDropdown).First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await dropdown.Locator(".tm-comment-mention-dropdown__item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        await AssertExactlyOneSelectedAsync(dropdown.Locator(".tm-comment-mention-dropdown__item"),
            "comment mention options");

        var before = await SelectedIndexAsync(dropdown.Locator(".tm-comment-mention-dropdown__item"));
        await textarea.PressAsync("ArrowDown");
        await Assertions.Expect(
                dropdown.Locator(".tm-comment-mention-dropdown__item")
                    .Nth((before + 1) % await dropdown.Locator(".tm-comment-mention-dropdown__item").CountAsync()))
            .ToHaveAttributeAsync("aria-selected", "true");
        await AssertExactlyOneSelectedAsync(dropdown.Locator(".tm-comment-mention-dropdown__item"),
            "comment mention options after ArrowDown");

        await AssertNoAxeViolationsAsync(page, CommentDropdown, "comment mention dropdown");
        await CaptureScreenAsync(page, CommentDropdown, "fze-4j-notion-comment-mention-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, CommentDropdown);
        await AssertNoAxeViolationsAsync(page, CommentDropdown, "comment mention dropdown (dark)");
        await CaptureScreenAsync(page, CommentDropdown, "fze-4j-notion-comment-mention-dark-1600.png");
    }

    // ── Emoji picker category tabs ─────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The emoji picker category tabs announce aria-selected as a string in both positions")]
    public async Task EmojiPicker_CategoryTabs_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);

        // A page with no icon has no .tm-notion-header-icon — the opener is the "Add icon"
        // control, which only appears while the header is hovered.
        var icon = page.Locator(".tm-notion-header-icon");
        if (await icon.CountAsync() == 0)
        {
            await page.Locator(".tm-notion-header-meta").First.HoverAsync();
            await page.Locator(".tm-notion-header-meta__add-btn").First.ClickAsync();
        }
        else
        {
            await icon.First.ClickAsync();
        }

        var picker = page.Locator(EmojiPicker).First;
        await picker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var tabs = picker.Locator(".tm-notion-emoji-picker__tab");
        Assert.AreEqual(9, await tabs.CountAsync(), "the nine emoji categories");
        await AssertExactlyOneSelectedAsync(tabs, "emoji category tabs");
        Assert.AreEqual("true", await tabs.Nth(0).GetAttributeAsync("aria-selected"),
            "Smileys starts active — the string \"true\"");

        await tabs.Nth(2).ClickAsync(); // Animals
        await Assertions.Expect(picker.Locator(".tm-notion-emoji-picker__tab").Nth(2))
            .ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(picker.Locator(".tm-notion-emoji-picker__tab").Nth(0))
            .ToHaveAttributeAsync("aria-selected", "false");
        await AssertExactlyOneSelectedAsync(picker.Locator(".tm-notion-emoji-picker__tab"),
            "emoji category tabs after the switch");

        await AssertNoAxeViolationsAsync(page, EmojiPicker, "emoji picker");
        await CaptureScreenAsync(page, EmojiPicker, "fze-4j-notion-emoji-picker-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, EmojiPicker);
        await AssertNoAxeViolationsAsync(page, EmojiPicker, "emoji picker (dark)");
        await CaptureScreenAsync(page, EmojiPicker, "fze-4j-notion-emoji-picker-dark-1600.png");
    }

    // ── Token dropdown ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The token dropdown options announce aria-selected as a string; arrows move the single selected option")]
    public async Task TokenDropdown_Options_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        await TypeTriggerAsync(page, "{{");

        var dropdown = page.Locator(TokenDropdown).First;
        await dropdown.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await dropdown.Locator(".tm-notion-token-dropdown__item").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        await AssertExactlyOneSelectedAsync(dropdown.Locator(".tm-notion-token-dropdown__item"),
            "token options");

        var before = await SelectedIndexAsync(dropdown.Locator(".tm-notion-token-dropdown__item"));
        await dropdown.Locator(".tm-notion-token-dropdown__input").First.PressAsync("ArrowDown");
        await Assertions.Expect(
                dropdown.Locator(".tm-notion-token-dropdown__item")
                    .Nth((before + 1) % await dropdown.Locator(".tm-notion-token-dropdown__item").CountAsync()))
            .ToHaveAttributeAsync("aria-selected", "true");
        await AssertExactlyOneSelectedAsync(dropdown.Locator(".tm-notion-token-dropdown__item"),
            "token options after ArrowDown");

        await AssertNoAxeViolationsAsync(page, TokenDropdown, "token dropdown");
        await CaptureScreenAsync(page, TokenDropdown, "fze-4j-notion-token-dropdown-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, TokenDropdown);
        await AssertNoAxeViolationsAsync(page, TokenDropdown, "token dropdown (dark)");
        await CaptureScreenAsync(page, TokenDropdown, "fze-4j-notion-token-dropdown-dark-1600.png");
    }

    // ── Block type switcher (demo card) ────────────────────────────────────────────────────

    [TestMethod]
    [Description("The block type switcher options announce aria-selected as a string — exactly the current type carries \"true\"")]
    public async Task BlockTypeSwitcher_Options_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);

        await page.GetByTestId("notion-type-switcher-open").ScrollIntoViewIfNeededAsync();
        await page.GetByTestId("notion-type-switcher-open").ClickAsync();
        var switcher = page.Locator(TypeSwitcher).First;
        await switcher.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });

        var options = switcher.Locator("[data-type-idx]");
        Assert.IsTrue(await options.CountAsync() > 3, "the switcher lists the block types");
        await AssertExactlyOneSelectedAsync(options, "block type options");
        Assert.AreEqual("true",
            await switcher.Locator(".tm-notion-type-switcher__item--current").GetAttributeAsync("aria-selected"),
            "the current type announces the string \"true\"");

        // Pick another type: the demo host binds it back, so reopening marks the NEW current type.
        var target = switcher.Locator("[data-type-idx='2']");
        await target.ClickAsync();
        // The demo card echoes the picked BlockType enum name (idx 2 = Heading2).
        await Assertions.Expect(page.GetByTestId("notion-type-switcher-current"))
            .ToHaveTextAsync("Heading2", new LocatorAssertionsToHaveTextOptions { Timeout = 8000 });

        await page.GetByTestId("notion-type-switcher-open").ClickAsync();
        await switcher.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await Assertions.Expect(switcher.Locator("[data-type-idx='2']"))
            .ToHaveAttributeAsync("aria-selected", "true");
        await AssertExactlyOneSelectedAsync(switcher.Locator("[data-type-idx]"),
            "block type options after the change");

        await AssertNoAxeViolationsAsync(page, TypeSwitcher, "block type switcher");
        await CaptureScreenAsync(page, TypeSwitcher, "fze-4j-notion-type-switcher-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, TypeSwitcher);
        await AssertNoAxeViolationsAsync(page, TypeSwitcher, "block type switcher (dark)");
        await CaptureScreenAsync(page, TypeSwitcher, "fze-4j-notion-type-switcher-dark-1600.png");
    }

    // ── Page search ────────────────────────────────────────────────────────────────────────

    [TestMethod]
    [Description("The page search results announce aria-selected as a string; arrows move the single selected option across the section boundary")]
    public async Task PageSearch_Results_AnnounceTheStringState()
    {
        var page = await OpenNotionEditorAsync();
        HookClientErrors(page);
        // The CF22 search seed returns BOTH a page ("CF22 Escalation Notes" — the word sits in
        // its description/labels) and block results for "customer", so the arrow walk crosses
        // the section boundary.
        await SeedSearchPageAsync();

        // Ctrl/Cmd+P from a focused block opens the page search modal.
        await page.Locator(".tm-notion-paragraph[contenteditable='true']").First.FocusAsync();
        await page.Keyboard.PressAsync("Control+p");
        var search = page.Locator(PageSearch).First;
        await search.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await search.Locator(".tm-nps__search-input").First.FillAsync("customer");
        // Wait for the DEBOUNCED "customer" results (the highlighted snippet only exists in
        // them) — the initial empty-query results are replaced underneath, so reading right
        // after Fill races the re-render.
        await search.Locator(".tm-nps__item-snippet mark").First
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        // Atomic JS snapshot — the list re-renders as a whole, so per-locator reads could straddle
        // two renders.
        var states = await ReadSelectedStatesAsync(page, ".tm-nps__item");
        Assert.IsTrue(states.Length >= 2, "the customer query returns pages AND blocks");
        AssertStringStatesExactlyOneTrue(states, "page search results");

        // ArrowDown walks the flat result list (pages first, then blocks) — the string "true"
        // moves with it, across the section boundary when both kinds are present.
        var before = Array.IndexOf(states, "true");
        await search.Locator(".tm-nps__search-input").First.PressAsync("ArrowDown");
        var expected = (before + 1) % states.Length;
        await page.WaitForFunctionAsync(
            """
            ([selector, index]) => document.querySelectorAll(selector)[index]
                ?.getAttribute('aria-selected') === 'true'
            """,
            new object[] { ".tm-nps__item", expected },
            new PageWaitForFunctionOptions { Timeout = 5000 });
        AssertStringStatesExactlyOneTrue(await ReadSelectedStatesAsync(page, ".tm-nps__item"),
            "page search results after ArrowDown");

        await AssertNoAxeViolationsAsync(page, PageSearch, "page search");
        await CaptureScreenAsync(page, PageSearch, "fze-4j-notion-page-search-light-1600.png");

        await EnsureDemoThemeAsync(page, "dark");
        await WaitForLayoutToSettleAsync(page, PageSearch);
        await AssertNoAxeViolationsAsync(page, PageSearch, "page search (dark)");
        await CaptureScreenAsync(page, PageSearch, "fze-4j-notion-page-search-dark-1600.png");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────

    /// <summary>Focuses the first editable paragraph, places the cursor at its end, presses Enter
    /// for a fresh block and types the trigger (the mention/token menus open on the keystroke).</summary>
    private static async Task TypeTriggerAsync(IPage page, string trigger)
    {
        var firstPara = page.Locator(".tm-notion-paragraph[contenteditable='true']").First;
        await firstPara.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await page.EvaluateAsync(@"() => {
            const el = document.querySelector('.tm-notion-paragraph[contenteditable=""true""]');
            if (!el) return;
            el.focus();
            const range = document.createRange();
            range.selectNodeContents(el);
            range.collapse(false);
            const sel = window.getSelection();
            sel.removeAllRanges();
            sel.addRange(range);
        }");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(1200);
        await page.Keyboard.TypeAsync(trigger);
    }

    /// <summary>The APG combobox contract: every option carries the attribute and exactly one
    /// carries "true" — the presence-selector risk is that a stale rule would highlight more.</summary>
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

    /// <summary>One atomic JS read of every option's aria-selected — immune to the list
    /// re-rendering between per-locator reads.</summary>
    private static Task<string?[]> ReadSelectedStatesAsync(IPage page, string selector)
        => page.EvaluateAsync<string?[]>(
            $"() => Array.from(document.querySelectorAll('{selector}')).map(el => el.getAttribute('aria-selected'))");

    private static void AssertStringStatesExactlyOneTrue(string?[] states, string label)
    {
        Assert.IsTrue(states.Length > 0, $"{label}: at least one option must render");
        foreach (var value in states)
        {
            Assert.IsTrue(value is "true" or "false",
                $"{label}: every option must carry the string state, got '{value ?? "<missing>"}'");
        }

        Assert.AreEqual(1, states.Count(value => value == "true"),
            $"{label}: exactly one option carries aria-selected=\"true\"");
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
