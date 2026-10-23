using System.Net.Http.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 3 of plan 643bba06: <c>TmDocumentOpenDialog</c> over the live Demo.Api library after the explicit query scope
/// (DOCLIB-QUERY-SCOPE) - the Recent node and the search now reach the WHOLE library (<c>Scope=Library</c>), the tree keeps one
/// <c>aria-selected</c> row when an ancestor of the active folder is collapsed, rapid switching never shows a stale answer, an empty
/// folder and a deeply nested path behave, and at 1440 the toolbar's "New folder" and the footer hint never wrap. Named screenshots go
/// to the git-ignored TestResults folder unless <c>TM_E2E_UPDATE_BASELINE=1</c> rewrites the committed set under
/// <c>__screenshots__/document-library/scope/</c>.
/// </summary>
[TestClass]
public class DocumentLibraryScopeE2ETests : WasmTestBase
{
    private const string DialogPage = "/document-open-dialog";
    private const string Api = "https://localhost:5100/api/document-library/wireframe";

    private static bool UpdateBaseline =>
        string.Equals(Environment.GetEnvironmentVariable("TM_E2E_UPDATE_BASELINE"), "1", StringComparison.Ordinal);

    private static string ScreenshotDirectory => UpdateBaseline
        ? Path.Combine(RepoRoot(), "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-library", "scope")
        : Path.Combine(RepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "document-library-scope");

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private async Task<IPage> OpenPageAsync(int width, int height, bool touch = false, string culture = "en", bool showRecent = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch && width < 700,
            Locale = culture == "en" ? "en-US" : culture,
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync($"localStorage.setItem('tm-demo-culture', '{culture}'); localStorage.removeItem('tm-demo-color-theme');");
        await page.GotoAsync($"{BaseUrl}{DialogPage}", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);
        if (showRecent)
        {
            await page.CheckAsync("[data-testid=show-recent]");
        }

        return page;
    }

    private static async Task OpenDialogAsync(IPage page)
    {
        await page.Locator("[data-testid=open-dialog-btn]").DispatchEventAsync("click");
        await page.WaitForSelectorAsync(".tm-document-open-dialog",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        await page.WaitForSelectorAsync(".tm-dod-tree-node, .tm-dod-folder-row", new PageWaitForSelectorOptions { Timeout = 20_000 });
        await SettleAsync(page);
    }

    private static async Task SettleAsync(IPage page) =>
        await page.EvaluateAsync("async () => { await Promise.all(document.getAnimations().map(a => a.finished.catch(() => { }))); }");

    private static ILocator Node(IPage page, string text) =>
        page.Locator(".tm-dod-tree-node", new() { HasTextString = text }).First;

    private async Task ShotAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ScreenshotDirectory);
        await SettleAsync(page);
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ScreenshotDirectory, name),
            Type = ScreenshotType.Png,
            Timeout = 60_000,
        });
    }

    private static async Task<string[]> RowNamesAsync(IPage page) =>
        await page.Locator(".tm-dod-row").EvaluateAllAsync<string[]>("rows => rows.map(r => r.dataset.name)");

    private static async Task<Guid> CreateAsync(HttpClient http, string name, string folder)
    {
        var resp = await http.PostAsJsonAsync($"{Api}/documents", new { name, folderPath = folder, payloadJson = "{}", author = "E2E" });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task DeleteAsync(IEnumerable<Guid> ids)
    {
        // The API store is shared by every document-library spec: leave it as found.
        using var http = DemoApiTestHttp.CreateClient();
        await http.PostAsJsonAsync($"{Api}/documents/delete", new { ids = ids.ToArray() });
    }

    // ── Scope=Library: Recent and the search span every folder ─────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Recent is library-wide now: a document that lives in a NESTED folder shows up in Recent, newest first (the demo store used to list the root only)")]
    public async Task Scope_Recent_ReachesDocumentsInNestedFolders()
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var nested = $"Scope-nested-{suffix}";
        var root = $"Scope-root-{suffix}";
        var created = new List<Guid>();
        using (var http = DemoApiTestHttp.CreateClient())
        {
            // Oldest first: the nested document is created BEFORE the root one, so the order below proves the sort, not the folder.
            created.Add(await CreateAsync(http, nested, "/Designs/Mobile"));
            await Task.Delay(30);
            created.Add(await CreateAsync(http, root, "/"));
        }

        try
        {
            var page = await OpenPageAsync(1440, 900, showRecent: true);
            await OpenDialogAsync(page);

            await page.Locator(".tm-dod-tree-node--recent").ClickAsync();
            await page.WaitForFunctionAsync(
                $"() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === '{nested}')",
                null, new PageWaitForFunctionOptions { Timeout = 15_000 });

            var names = await RowNamesAsync(page);
            Assert.IsTrue(Array.IndexOf(names, root) < Array.IndexOf(names, nested), $"newest first: {string.Join(", ", names)}");
            await ShotAsync(page, "desktop-1440-recent-library-scope.png");
        }
        finally
        {
            await DeleteAsync(created);
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Search from inside a folder finds documents of OTHER folders (Scope=Library); clearing it returns to the folder's own content")]
    public async Task Scope_Search_IsLibraryWide_AndClearReturnsToTheFolder()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);
        await Node(page, "Designs").ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-row");
        var folderRows = await RowNamesAsync(page);

        await page.FillAsync(".tm-dod-search", "Login");
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name.includes('Login')) && ![...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name === 'Home page')",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });

        await page.FillAsync(".tm-dod-search", string.Empty);
        await page.WaitForFunctionAsync(
            "n => [...document.querySelectorAll('.tm-dod-row')].length === n",
            folderRows.Length, new PageWaitForFunctionOptions { Timeout = 15_000 });
        CollectionAssert.AreEqual(folderRows, await RowNamesAsync(page), "clearing the search restores the folder's own rows");
    }

    // ── Collapsing an ancestor of the active folder ───────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Collapsing the parent of the active folder hands aria-selected to the nearest VISIBLE ancestor - never two or zero selected rows")]
    public async Task Scope_CollapsedAncestor_KeepsExactlyOneSelectedTreeRow()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);
        await Node(page, "Mobile").ClickAsync();
        await page.WaitForSelectorAsync(".tm-dod-crumb--current");

        Assert.AreEqual(1, await page.Locator(".tm-dod-tree-node[aria-selected='true']").CountAsync());
        await Node(page, "Designs").Locator(".tm-dod-tree-toggle").ClickAsync();
        await page.WaitForFunctionAsync("() => ![...document.querySelectorAll('.tm-dod-tree-node')].some(n => n.textContent.includes('Mobile'))");

        var selected = page.Locator(".tm-dod-tree-node[aria-selected='true']");
        Assert.AreEqual(1, await selected.CountAsync(), "exactly one selected row");
        StringAssert.Contains(await selected.InnerTextAsync(), "Designs", "the collapsed parent stands in for the hidden active folder");
        Assert.AreEqual("0", await selected.GetAttributeAsync("tabindex"), "the selected row is the tree's single tab stop");
        await ShotAsync(page, "desktop-1440-collapsed-ancestor-selected.png");
    }

    // ── Edge cases ───────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: an empty folder shows the empty state, keeps the footer usable and Open disabled")]
    public async Task Scope_EmptyFolder_ShowsTheEmptyState()
    {
        var folder = "ScopeEmpty" + Guid.NewGuid().ToString("N")[..6];
        using (var http = DemoApiTestHttp.CreateClient())
        {
            (await http.PostAsJsonAsync($"{Api}/folders", new { parentPath = "/", name = folder })).EnsureSuccessStatusCode();
        }

        try
        {
            var page = await OpenPageAsync(1440, 900);
            await OpenDialogAsync(page);
            await Node(page, folder).ClickAsync();

            await page.WaitForSelectorAsync(".tm-dod-empty", new PageWaitForSelectorOptions { Timeout = 15_000 });
            Assert.AreEqual(0, await page.Locator(".tm-dod-row").CountAsync());
            Assert.IsTrue(await page.Locator(".tm-dod-open").IsDisabledAsync(), "nothing to open");
            Assert.IsFalse(await page.Locator(".tm-dod-new-folder").IsDisabledAsync(), "a folder can still be created in an empty folder");
            await ShotAsync(page, "desktop-1440-empty-folder.png");
        }
        finally
        {
            using var http = DemoApiTestHttp.CreateClient();
            await http.DeleteAsync($"{Api}/folders?folderPath={Uri.EscapeDataString("/" + folder)}");
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a deeply nested folder keeps the tree readable (indent clamps inside the pane), the breadcrumb reaches the leaf and collapsing the top ancestor leaves one selected row")]
    public async Task Scope_DeepNesting_StaysInsideThePane()
    {
        var top = "ScopeDeep" + Guid.NewGuid().ToString("N")[..6];
        var leafPath = "/" + top;
        using (var http = DemoApiTestHttp.CreateClient())
        {
            var parent = "/";
            foreach (var level in Enumerable.Range(1, 8))
            {
                var name = level == 1 ? top : $"Level{level}";
                (await http.PostAsJsonAsync($"{Api}/folders", new { parentPath = parent, name })).EnsureSuccessStatusCode();
                parent = parent == "/" ? "/" + name : parent + "/" + name;
            }

            leafPath = parent;
        }

        try
        {
            var page = await OpenPageAsync(1440, 900);
            await OpenDialogAsync(page);
            await page.Locator($".tm-dod-tree-node[data-path='{leafPath}']").ClickAsync();
            await page.WaitForSelectorAsync(".tm-dod-crumb--current");

            StringAssert.Contains(await page.Locator(".tm-dod-crumb--current").InnerTextAsync(), "Level8");
            var overflow = await page.EvaluateAsync<double>("() => { const t = document.querySelector('.tm-dod-tree'); return t.scrollWidth - t.clientWidth; }");
            Assert.IsTrue(overflow <= 200, $"the tree pane may scroll but must not blow the layout (overflow {overflow}px)");
            var dialog = await page.Locator(".tm-modal").BoundingBoxAsync();
            Assert.IsTrue(dialog!.X >= 0 && dialog.X + dialog.Width <= 1440.5, "the dialog stays inside the viewport");
            Assert.AreEqual(1, await page.Locator(".tm-dod-tree-node[aria-selected='true']").CountAsync());
            await ShotAsync(page, "desktop-1440-deep-nesting.png");

            await Node(page, top).Locator(".tm-dod-tree-toggle").ClickAsync();
            var selected = page.Locator(".tm-dod-tree-node[aria-selected='true']");
            Assert.AreEqual(1, await selected.CountAsync());
            StringAssert.Contains(await selected.GetAttributeAsync("data-path") ?? string.Empty, top);
        }
        finally
        {
            using var http = DemoApiTestHttp.CreateClient();
            await http.DeleteAsync($"{Api}/folders?folderPath={Uri.EscapeDataString("/" + top)}");
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: rapid switching between folders and a search ends on the LAST request's content (no stale overwrite, no error banner)")]
    public async Task Scope_RapidSwitching_EndsOnTheLastRequest()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);

        for (var i = 0; i < 4; i++)
        {
            await Node(page, "Designs").DispatchEventAsync("click");
            await Node(page, "Archive").DispatchEventAsync("click");
        }

        await Node(page, "Designs").DispatchEventAsync("click");
        await page.FillAsync(".tm-dod-search", "Home");
        await Node(page, "Designs").DispatchEventAsync("click");

        await page.WaitForFunctionAsync(
            "() => document.querySelector('.tm-dod-crumb--current')?.textContent.includes('Designs') && document.querySelectorAll('.tm-dod-row').length > 0",
            null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(500); // let any straggling stale response land - it must not change the outcome
        Assert.AreEqual(0, await page.Locator(".tm-dod-error").CountAsync(), "no error banner");
        Assert.AreEqual("", await page.InputValueAsync(".tm-dod-search"), "opening a folder cleared the search box");
        var names = await RowNamesAsync(page);
        CollectionAssert.Contains(names, "Home page");
        Assert.AreEqual(1, await page.Locator(".tm-dod-tree-node[aria-selected='true']").CountAsync());
        StringAssert.Contains(await Node(page, "Designs").GetAttributeAsync("aria-selected") ?? string.Empty, "true");
    }

    // ── Desktop 1440: nothing wraps ──────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Desktop 1440: 'New folder' and the footer hint never wrap - the row heights of the toolbar buttons and of the mode cards stay at one line")]
    public async Task Desktop1440_NewFolderAndFooterHint_DoNotWrap()
    {
        var page = await OpenPageAsync(1440, 900);
        await OpenDialogAsync(page);
        await ShotAsync(page, "desktop-1440-toolbar-and-footer-nowrap.png");

        var metrics = await page.EvaluateAsync<double[]>(
            @"() => {
                const lineCount = el => {
                    const range = document.createRange();
                    range.selectNodeContents(el);
                    const tops = new Set([...range.getClientRects()].map(r => Math.round(r.top)));
                    return tops.size;
                };
                const label = document.querySelector('.tm-dod-new-folder > span');
                const button = document.querySelector('.tm-dod-new-folder');
                const hints = [...document.querySelectorAll('.tm-dod-mode-hint')];
                const titles = [...document.querySelectorAll('.tm-dod-mode-title')];
                const lineHeight = el => parseFloat(getComputedStyle(el).lineHeight) || parseFloat(getComputedStyle(el).fontSize) * 1.4;
                return [
                    lineCount(label),
                    button.getBoundingClientRect().height,
                    Math.max(...hints.map(h => h.getBoundingClientRect().height / lineHeight(h))),
                    Math.max(...titles.map(h => h.getBoundingClientRect().height)),
                    hints.length,
                ];
            }");

        Assert.AreEqual(1, metrics[0], "the 'New folder' label is ONE line");
        Assert.IsTrue(metrics[1] <= 44, $"the toolbar button keeps a single-line height ({metrics[1]}px)");
        Assert.IsTrue(metrics[2] <= 1.5, $"the footer hint fits one line at 1440 ({metrics[2]:0.0} lines)");
        Assert.IsTrue(metrics[3] <= 28, $"the card titles are one line ({metrics[3]:0.0}px tall)");
        Assert.AreEqual(2, metrics[4], "both cards carry a hint");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Desktop 1440 in Czech (longer labels): 'Nová složka' still renders on one line")]
    public async Task Desktop1440_Czech_NewFolderDoesNotWrap()
    {
        var page = await OpenPageAsync(1440, 900, culture: "cs");
        await OpenDialogAsync(page);

        var height = (await page.Locator(".tm-dod-new-folder").BoundingBoxAsync())!.Height;
        var single = (await page.Locator(".tm-dod-search").BoundingBoxAsync())!.Height;
        Assert.IsTrue(height <= Math.Max(44, single + 4), $"'Nová složka' stays on one line ({height}px vs search {single}px)");
        await ShotAsync(page, "desktop-1440-cs-toolbar.png");
    }

    // ── Tablet / mobile: the same query semantics ────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Tablet 1024 touch: Recent (library scope) lists the newest documents and a tap on a folder leaves it")]
    public async Task Tablet_Recent_UsesTheLibraryScope()
    {
        var page = await OpenPageAsync(1024, 768, touch: true, showRecent: true);
        await OpenDialogAsync(page);

        await page.Locator(".tm-dod-tree-node--recent").TapAsync();
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.tm-dod-row').length > 0", null, new PageWaitForFunctionOptions { Timeout = 15_000 });
        Assert.AreEqual("true", await page.Locator(".tm-dod-tree-node--recent").GetAttributeAsync("aria-selected"));
        Assert.IsTrue(await page.Locator(".tm-dod-col-name").IsDisabledAsync(), "Recent fixes the order");
        await ShotAsync(page, "tablet-1024-recent.png");

        await Node(page, "Designs").TapAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('.tm-dod-col-name')?.disabled", null, new PageWaitForFunctionOptions { Timeout = 10_000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [DataRow(390, 844)]
    [DataRow(320, 640)]
    [Description("Mobile: the library-wide search from a drilled-in folder finds other folders' documents, and the row-menu sheet title wraps a long document name inside the sheet")]
    public async Task Mobile_Search_IsLibraryWide_AndTheRowMenuTitleStaysInsideTheSheet(int width, int height)
    {
        var longName = "A-very-long-document-name-without-any-break-opportunity-" + Guid.NewGuid().ToString("N");
        Guid id;
        using (var http = DemoApiTestHttp.CreateClient())
        {
            id = await CreateAsync(http, longName, "/Designs");
        }

        try
        {
            var page = await OpenPageAsync(width, height, touch: true);
            await OpenDialogAsync(page);
            await page.Locator(".tm-dod-folder-row[data-path$='/Designs']").First.TapAsync();
            await page.WaitForSelectorAsync($".tm-dod-row[data-name='{longName}']");

            await page.FillAsync(".tm-dod-search", "Login");
            await page.WaitForFunctionAsync(
                "() => [...document.querySelectorAll('.tm-dod-row')].some(r => r.dataset.name.includes('Login'))",
                null, new PageWaitForFunctionOptions { Timeout = 15_000 });

            await page.FillAsync(".tm-dod-search", string.Empty);
            await page.WaitForSelectorAsync($".tm-dod-row[data-name='{longName}']");
            await page.Locator($".tm-dod-row[data-name='{longName}'] .tm-dod-row-menu button.tm-dropdown-trigger").TapAsync();
            var title = page.Locator(".tm-overlay-panel-sheet__title");
            await title.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });
            await SettleAsync(page);

            var done = await page.Locator(".tm-overlay-panel-sheet__done").BoundingBoxAsync();
            var titleBox = await title.BoundingBoxAsync();
            Assert.IsNotNull(done);
            Assert.IsTrue(done!.X >= 0 && done.X + done.Width <= width + 0.5, $"the Done action stays on screen (x={done.X}, w={done.Width})");
            Assert.IsTrue(titleBox!.X + titleBox.Width <= width + 0.5, $"the sheet title stays inside the viewport ({titleBox.X + titleBox.Width}px)");
            await ShotAsync(page, $"mobile-{width}-row-menu-long-title.png");
        }
        finally
        {
            await DeleteAsync([id]);
        }
    }
}
