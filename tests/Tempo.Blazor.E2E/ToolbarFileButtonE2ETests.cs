using System.Text;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Decision TOOLBAR-FILE-BUTTON (plan 643bba06, phase 4): <c>TmToolbarFileButton</c> on the REAL components. The demo page
/// <c>/toolbar-forms</c> hosts the bars; the Wireframe editor proves the migration (Import JSON became a Secondary file button).
/// The checks are browser facts, never markup alone: the OS file chooser really opens (Playwright's <c>filechooser</c> event) from
/// the bar button AND from the "More" entry, the hidden input keeps its DOM node across a collapse / expand, the toolbar settles
/// (no "element is not stable" on the More trigger), and the edge cases behave: a cancelled chooser imports nothing, invalid
/// JSON is reported, a disabled entry opens nothing, the same file twice raises a change twice.
/// Screenshots go to the git-ignored <c>TestResults/toolbar-file-button</c> folder; review them against the mockups.
/// </summary>
[TestClass]
public sealed class ToolbarFileButtonE2ETests : WasmTestBase
{
    private const string DemoUrl = "/toolbar-forms";
    private const string EditorUrl = "/wireframe-editor";

    private static string ShotDir => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "toolbar-file-button");

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    private static FilePayload Json(string name, string body) => new() { Name = name, MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(body) };

    // ── Demo bars ────────────────────────────────────────────────────────────

    private async Task<IPage> OpenDemoAsync(int width, int height, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            IsMobile = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}{DemoUrl}");
        await WaitForAppReadyAsync(page);
        await page.Locator("[data-testid='toolbar-file-button-card']").WaitForAsync();
        return page;
    }

    private static ILocator Bar(IPage page, string testId) => page.Locator($"[data-testid='{testId}']");

    /// <summary>Waits until the bar's fit measurement settled (two frames after the interop-driven re-render).</summary>
    private static async Task SettleAsync(IPage page, string testId)
    {
        await Bar(page, testId).WaitForAsync();
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(r, 250))))");
    }

    private static async Task ShootAsync(IPage page, ILocator subject, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await subject.ScrollIntoViewIfNeededAsync();
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await subject.ScreenshotAsync(new LocatorScreenshotOptions { Path = Path.Combine(ShotDir, name + ".png"), Timeout = 60_000 });
    }

    /// <summary>Viewport capture for a state with an open popover/menu (a popover paints outside the subject element).</summary>
    private static async Task ShootPageAsync(IPage page, string name)
    {
        Directory.CreateDirectory(ShotDir);
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, name + ".png"), FullPage = false });
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Wide bar: the Secondary file button stays on the bar; clicking it opens the native chooser; the chosen JSON is read")]
    public async Task Wide_BarButton_OpensTheChooser_AndTheChosenFileIsRead()
    {
        var page = await OpenDemoAsync(1440, 900);
        await SettleAsync(page, "toolbar-file-button-520");
        var bar = Bar(page, "toolbar-file-button-520");

        var button = bar.Locator("button[data-testid='toolbar-file-button-import-520']");
        Assert.AreEqual(0, await bar.Locator(".tm-toolbar-item--collapsed").CountAsync(), "520px holds all four buttons");
        await Assertions.Expect(button).ToBeVisibleAsync();

        var chooser = await page.RunAndWaitForFileChooserAsync(() => button.ClickAsync());
        Assert.IsFalse(chooser.IsMultiple);
        await chooser.SetFilesAsync(Json("doc.json", "{\"a\":1}"));

        await Assertions.Expect(page.Locator("[data-testid='toolbar-file-button-result']")).ToContainTextAsync("Loaded doc.json: a JSON Object");
        await ShootAsync(page, Bar(page, "toolbar-file-button-host-520"), "demo-1440-bar");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Keyboard: Enter and Space on the focused bar button each open the chooser exactly once (native button, no emulation)")]
    public async Task Keyboard_EnterAndSpace_OpenTheChooserOnce()
    {
        var page = await OpenDemoAsync(1440, 900);
        await SettleAsync(page, "toolbar-file-button-520");
        var button = Bar(page, "toolbar-file-button-520").Locator("button[data-testid='toolbar-file-button-import-520']");

        foreach (var key in new[] { "Enter", "Space" })
        {
            await button.FocusAsync();
            var opened = 0;
            void OnChooser(object? _, IFileChooser c) => opened++;
            page.FileChooser += OnChooser;
            await page.Keyboard.PressAsync(key);
            await page.WaitForTimeoutAsync(400);
            page.FileChooser -= OnChooser;
            Assert.AreEqual(1, opened, $"{key} opens the chooser exactly once");
        }
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Narrow bar: the file button collapses into More; the More entry opens the SAME chooser; the toolbar settles and the trigger is stable")]
    public async Task Narrow_FileButtonCollapsesIntoMore_AndTheMenuEntryOpensTheChooser()
    {
        var page = await OpenDemoAsync(1024, 768);
        await SettleAsync(page, "toolbar-file-button-300");
        var bar = Bar(page, "toolbar-file-button-300");

        var collapsed = bar.Locator("button.tm-toolbar-item--collapsed[data-testid='toolbar-file-button-import-300']");
        Assert.AreEqual(1, await collapsed.CountAsync(), "the Secondary file button collapsed at 300px");
        Assert.IsTrue(await collapsed.EvaluateAsync<bool>("el => el.inert"), "the collapsed bar copy is inert");

        // The hidden input is NOT inside the collapsed wrapper.
        var input = bar.Locator("input[type='file'][data-testid='toolbar-file-button-import-300-input']");
        Assert.AreEqual(1, await input.CountAsync());
        Assert.IsFalse(await input.EvaluateAsync<bool>("el => !!el.closest('.tm-toolbar-item--collapsed')"));

        // The More trigger must be stable: this is the Playwright actionability check that failed before.
        var more = bar.Locator("button.tm-toolbar-more");
        await more.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
        var entry = page.Locator("[role='menu'] [role='menuitem']", new() { HasTextString = "Import JSON" });
        await Assertions.Expect(entry).ToBeVisibleAsync();
        await Bar(page, "toolbar-file-button-host-300").ScrollIntoViewIfNeededAsync();
        await ShootPageAsync(page, "demo-narrow-menu-open");

        var chooser = await page.RunAndWaitForFileChooserAsync(() => entry.ClickAsync());
        await chooser.SetFilesAsync(Json("narrow.json", "[1,2,3]"));

        await Assertions.Expect(page.Locator("[data-testid='toolbar-file-button-result']")).ToContainTextAsync("Loaded narrow.json: a JSON Array");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Collapse / expand never remounts the hidden input: a marker set on the DOM node survives a resize round-trip")]
    public async Task CollapseAndExpand_KeepsTheSameInputNode()
    {
        var page = await OpenDemoAsync(1440, 900);
        var host = Bar(page, "toolbar-file-button-host-520");
        await SettleAsync(page, "toolbar-file-button-520");
        var input = host.Locator("input[type='file']");
        await input.EvaluateAsync("el => { el.__marker = 'same-node'; }");

        // Narrow the host (collapse), then widen it again (expand).
        await host.EvaluateAsync("el => { el.style.width = '260px'; }");
        await page.WaitForFunctionAsync("() => document.querySelector(\"[data-testid='toolbar-file-button-520'] .tm-toolbar-item--collapsed\") !== null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });
        await host.EvaluateAsync("el => { el.style.width = '520px'; }");
        await page.WaitForFunctionAsync("() => document.querySelector(\"[data-testid='toolbar-file-button-520'] .tm-toolbar-item--collapsed\") === null",
            null, new PageWaitForFunctionOptions { Timeout = 10_000 });

        Assert.AreEqual("same-node", await input.EvaluateAsync<string?>("el => el.__marker"),
            "the InputFile survived collapse + expand - it was never re-created");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: invalid JSON is reported, a cancelled chooser imports nothing, the same file twice raises a change twice")]
    public async Task Edges_InvalidJson_Cancel_AndTheSameFileTwice()
    {
        var page = await OpenDemoAsync(1440, 900);
        await SettleAsync(page, "toolbar-file-button-520");
        var button = Bar(page, "toolbar-file-button-520").Locator("button[data-testid='toolbar-file-button-import-520']");
        var result = page.Locator("[data-testid='toolbar-file-button-result']");

        // Invalid JSON.
        var chooser = await page.RunAndWaitForFileChooserAsync(() => button.ClickAsync());
        await chooser.SetFilesAsync(Json("bad.json", "{not json"));
        await Assertions.Expect(result).ToContainTextAsync("bad.json is not valid JSON.");

        // Cancelled chooser: nothing changes.
        var before = await result.TextContentAsync();
        chooser = await page.RunAndWaitForFileChooserAsync(() => button.ClickAsync());
        await chooser.SetFilesAsync(Array.Empty<FilePayload>());
        await page.WaitForTimeoutAsync(500);
        Assert.AreEqual(before, await result.TextContentAsync(), "a cancelled chooser is not an import");

        // The same file twice: a change each time (the input is cleared before every open). The page counts the changes it receives.
        var count = page.Locator("[data-testid='toolbar-file-button-count']");
        await Assertions.Expect(count).ToHaveTextAsync("Files received: 1", new() { Timeout = 5_000 });   // only bad.json: the cancelled chooser raised nothing
        var same = Json("same.json", "{\"k\":true}");
        chooser = await page.RunAndWaitForFileChooserAsync(() => button.ClickAsync());
        await chooser.SetFilesAsync(same);
        await Assertions.Expect(count).ToHaveTextAsync("Files received: 2");
        chooser = await page.RunAndWaitForFileChooserAsync(() => button.ClickAsync());
        await chooser.SetFilesAsync(same);
        await Assertions.Expect(count).ToHaveTextAsync("Files received: 3", new() { Timeout = 5_000 });
    }
    [TestMethod]
    [TestCategory("WASM")]
    [Description("Edge: a Disabled file button offers a disabled More entry; the More trigger is disabled and no chooser opens")]
    public async Task Disabled_InMore_OpensNothing()
    {
        var page = await OpenDemoAsync(1440, 900);
        await SettleAsync(page, "toolbar-file-button-disabled");
        var bar = Bar(page, "toolbar-file-button-disabled");

        var input = bar.Locator("input[type='file']");
        Assert.IsTrue(await input.IsDisabledAsync(), "the hidden input of a disabled file button is disabled");

        var more = bar.Locator("button.tm-toolbar-more");
        await more.ClickAsync();
        var entry = page.Locator("[role='menu'] [role='menuitem']", new() { HasTextString = "Import JSON" });
        await Assertions.Expect(entry).ToBeDisabledAsync();

        var opened = false;
        page.FileChooser += (_, _) => opened = true;
        await entry.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(500);
        Assert.IsFalse(opened, "a disabled More entry opens no chooser");
        await ShootPageAsync(page, "demo-disabled-menu-open");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Phone (390, touch): the More bottom sheet carries the file entry as a 44px item; tapping it opens the chooser")]
    public async Task Phone_390_Touch_MoreSheetOpensTheChooser()
    {
        var page = await OpenDemoAsync(390, 844, touch: true);
        await SettleAsync(page, "toolbar-file-button-300");
        var bar = Bar(page, "toolbar-file-button-300");

        await bar.Locator("button.tm-toolbar-more").TapAsync();
        var sheet = page.Locator(".tm-overlay-panel-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        var entry = sheet.Locator("[role='menuitem']", new() { HasTextString = "Import JSON" });
        await Assertions.Expect(entry).ToBeVisibleAsync();
        var box = await entry.BoundingBoxAsync();
        Assert.IsTrue(box!.Height >= 43.5, $"a sheet item is a finger target, measured {box.Height}px");
        await page.Mouse.MoveAsync(0, 0);
        Directory.CreateDirectory(ShotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(ShotDir, "demo-390-sheet.png") });

        var chooser = await page.RunAndWaitForFileChooserAsync(() => entry.TapAsync());
        await chooser.SetFilesAsync(Json("phone.json", "{}"));
        await Assertions.Expect(page.Locator("[data-testid='toolbar-file-button-result']")).ToContainTextAsync("Loaded phone.json");
    }

    // ── The Wireframe editor: Import JSON as a Secondary file button ─────────

    private async Task<(IPage Page, ILocator Editor)> OpenEditorAsync(int width, int height, bool touch = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            HasTouch = touch,
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        // The layout resolves from the editor CONTAINER: drop the demo sidebar margin before the app boots.
        await page.AddInitScriptAsync("document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; document.head.appendChild(s); });");
        await GotoAppReadyAsync(page, $"{BaseUrl}{EditorUrl}");
        await page.WaitForSelectorAsync(".tm-wd-editor .tm-wd-canvas__svg", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 30_000 });
        var editor = page.Locator(".tm-wd-editor").First;
        await editor.Locator(".tm-wd-editor__toolbar").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(() => setTimeout(r, 600))))");
        return (page, editor);
    }

    private static string ValidWireframe(string title) =>
        "{\"title\":\"" + title + "\",\"width\":1200,\"height\":800,\"pages\":[{\"id\":\"p1\",\"name\":\"Imported\",\"width\":1200,\"height\":800}],\"activePageId\":\"p1\",\"elements\":[]}";

    /// <summary>Opens the (single, desktop) More menu of the editor and returns the Import JSON entry.</summary>
    private static async Task<ILocator> ImportEntryInMoreAsync(IPage page, ILocator editor)
    {
        // The More trigger takes a click within a short actionability budget: this is the check that failed before ("element is not stable").
        await editor.Locator("button.tm-toolbar-more").First.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
        var entry = page.Locator("[role='menu'] [role='menuitem']", new() { HasTextString = "Import JSON" });
        await Assertions.Expect(entry).ToBeVisibleAsync();
        return entry;
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Wireframe @1440: Import JSON is Secondary (data-testid wd-import kept); it collapses into More BEFORE the arrange actions; the More entry opens the chooser and a valid document is imported")]
    public async Task Wireframe_1440_ImportJson_IsSecondary_CollapsesBeforeArrange_AndImportsFromMore()
    {
        var (page, editor) = await OpenEditorAsync(1440, 900);
        Assert.AreEqual("desktop", await editor.GetAttributeAsync("data-editor-layout"));

        var import = editor.Locator("button[data-testid='wd-import']");
        Assert.AreEqual(1, await import.CountAsync());
        Assert.AreEqual("auto", await import.GetAttributeAsync("data-pin"), "Import JSON is Secondary now, not Pinned");

        // It is the LAST Secondary item, so it is the first to leave the bar - every arrange button is still on the bar.
        var state = await editor.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            e => {
                const collapsed = el => !!el && el.classList.contains('tm-toolbar-item--collapsed');
                const arrange = Array.from(e.querySelectorAll('[data-tm-group="arrange"] button.tm-toolbar-btn'));
                return { importCollapsed: collapsed(e.querySelector("button[data-testid='wd-import']")), arrange: arrange.map(collapsed) };
            }
            """);
        Assert.IsTrue(state.GetProperty("importCollapsed").GetBoolean(), "at 1440 the full command row has no room for Import JSON: it is in More");
        Assert.AreEqual(3, state.GetProperty("arrange").GetArrayLength());
        foreach (var arrangeCollapsed in state.GetProperty("arrange").EnumerateArray())
            Assert.IsFalse(arrangeCollapsed.GetBoolean(), "Import JSON leaves before any arrange action does");

        var entry = await ImportEntryInMoreAsync(page, editor);
        await ShootPageAsync(page, "wireframe-1440-menu-open");
        var chooser = await page.RunAndWaitForFileChooserAsync(() => entry.ClickAsync());
        await chooser.SetFilesAsync(Json("imported.wireframe.json", ValidWireframe("Imported from E2E")));

        await Assertions.Expect(editor.Locator("[data-testid='wd-title']")).ToHaveValueAsync("Imported from E2E");
        await Assertions.Expect(page.Locator("[role='menu']")).ToHaveCountAsync(0);
        await ShootAsync(page, editor.Locator(".tm-wd-editor__toolbar"), "wireframe-1440-toolbar");
    }


    [TestMethod]
    [TestCategory("WASM")]
    [Description("Stability: while the command bar is squeezed through nine widths, the More trigger settles after ONE layout change and never moves again for 2.5 s (the Playwright 'element is not stable' regression)")]
    public async Task Wireframe_MoreTrigger_SettlesAtEveryWidth()
    {
        var (page, _) = await OpenEditorAsync(1920, 1080);
        foreach (var width in new[] { 1262, 1200, 1100, 1000, 900, 800, 700, 600, 560 })
        {
            await page.EvaluateAsync("w => { let s = document.getElementById('e2e-squeeze'); if (!s) { s = document.createElement('style'); s.id = 'e2e-squeeze'; document.head.appendChild(s); } s.textContent = '.tm-wd-editor__commandbar { max-width: ' + w + 'px !important; }'; }", width);
            var samples = await page.EvaluateAsync<string[]>(
                """
                () => new Promise(resolve => {
                    const out = [];
                    const bar = document.querySelector('.tm-wd-editor .tm-wd-editor__commandbar');
                    const t0 = performance.now();
                    const tick = () => {
                        const more = bar.querySelector('button.tm-toolbar-more');
                        const r = more && more.getBoundingClientRect();
                        out.push((r ? Math.round(r.left) + '/' + Math.round(r.top) : 'none') + ':c' + bar.querySelectorAll('.tm-toolbar-item--collapsed').length);
                        if (performance.now() - t0 < 2500) setTimeout(tick, 50); else resolve(out);
                    };
                    tick();
                })
                """);
            var changes = 0;
            for (var i = 1; i < samples.Length; i++) if (samples[i] != samples[i - 1]) changes++;
            Assert.IsTrue(changes <= 1, $"width {width}: the More trigger changed state {changes} times (oscillation): {string.Join(" ", samples.Distinct())}");
            Assert.AreEqual(samples[^1], samples[^20], $"width {width}: the trigger moved during the last second");
        }
    }
    [TestMethod]
    [TestCategory("WASM")]
    [Description("Wireframe edge: invalid JSON from the More entry shows the import error banner and leaves the document untouched")]
    public async Task Wireframe_InvalidJson_ShowsTheErrorBanner()
    {
        var (page, editor) = await OpenEditorAsync(1440, 900);
        var title = await editor.Locator("[data-testid='wd-title']").InputValueAsync();

        var entry = await ImportEntryInMoreAsync(page, editor);
        var chooser = await page.RunAndWaitForFileChooserAsync(() => entry.ClickAsync());
        await chooser.SetFilesAsync(Json("broken.json", "this is not json"));

        await Assertions.Expect(editor.Locator(".tm-wd-editor__error-banner")).ToBeVisibleAsync();
        Assert.AreEqual(title, await editor.Locator("[data-testid='wd-title']").InputValueAsync(), "the document is untouched");
        await ShootAsync(page, editor, "wireframe-invalid-json");
    }

    [TestMethod]
    [TestCategory("WASM")]
    [Description("Wireframe edge: a second import after the first still works (the chooser is cleared and re-armed before every open)")]
    public async Task Wireframe_ImportTwice_BothDocumentsLoad()
    {
        var (page, editor) = await OpenEditorAsync(1440, 900);
        foreach (var name in new[] { "First import", "Second import" })
        {
            var entry = await ImportEntryInMoreAsync(page, editor);
            var chooser = await page.RunAndWaitForFileChooserAsync(() => entry.ClickAsync());
            await chooser.SetFilesAsync(Json("again.json", ValidWireframe(name)));
            await Assertions.Expect(editor.Locator("[data-testid='wd-title']")).ToHaveValueAsync(name);
        }
    }
    [TestMethod]
    [TestCategory("WASM")]
    [Description("Wireframe tablet (1024, touch): the file button lives in the second toolbar row; the toolbar stays unclipped and the chooser opens")]
    public async Task Wireframe_Tablet_ImportReachableAndUnclipped()
    {
        var (page, editor) = await OpenEditorAsync(1024, 768, touch: true);
        Assert.AreEqual("tablet", await editor.GetAttributeAsync("data-editor-layout"));

        var metrics = await editor.EvaluateAsync<System.Text.Json.JsonElement>(
            """
            e => ({
                rows: Array.from(e.querySelectorAll('[data-testid^="wd-toolbar-row-"]')).map(r => r.scrollWidth - r.clientWidth),
                editor: e.scrollWidth - e.clientWidth,
            })
            """);
        foreach (var overflow in metrics.GetProperty("rows").EnumerateArray())
            Assert.IsTrue(overflow.GetInt32() <= 0, $"a toolbar row is clipped by {overflow.GetInt32()}px");
        Assert.IsTrue(metrics.GetProperty("editor").GetInt32() <= 0);

        var import = editor.Locator("button[data-testid='wd-import']").First;
        if (await import.EvaluateAsync<bool>("el => el.classList.contains('tm-toolbar-item--collapsed')"))
        {
            await editor.Locator("[data-testid='wd-toolbar-row-2'] button.tm-toolbar-more").ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
            import = page.Locator("[role='menu'] [role='menuitem']", new() { HasTextString = "Import JSON" });
        }

        var chooser = await page.RunAndWaitForFileChooserAsync(() => import.TapAsync());
        await chooser.SetFilesAsync(Json("tablet.json", ValidWireframe("Tablet import")));
        await Assertions.Expect(editor.Locator("[data-testid='wd-title']")).ToHaveValueAsync("Tablet import");
        await ShootAsync(page, editor, "wireframe-tablet-1024");
    }
}
