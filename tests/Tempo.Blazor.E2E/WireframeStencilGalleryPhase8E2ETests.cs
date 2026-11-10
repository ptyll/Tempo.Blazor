using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6 phase 8 — the new and changed wireframe stencils looked at IN THE RUNNING EDITOR,
/// not merely validated as a parsed SVG string.
/// <para>
/// The bUnit/unit layer already pins the schema and pack parity of <c>TmAppShell</c>, <c>TmUserMenu</c>,
/// <c>TmBottomNavigation</c>, <c>TmSidePanel</c> and the open-combobox state of <c>TmMultiSelect</c>
/// (<c>TmAppShellStencilTests</c>, <c>TmUserMenuStencilTests</c>, <c>TmMultiSelectStencilTests</c>,
/// <c>StencilPackRegistryParityTests</c>). What that layer cannot answer is whether a designer can
/// actually FIND the entry in the palette and whether the sketch READS as the component it stands in
/// for — so this spec drives the real editor: it filters the toolbox, inserts each stencil through the
/// canvas drop handler, flips the opt-in props through the properties panel, and captures the palette
/// entry, the default render and the conditional states into
/// <c>TestResults/wireframe-stencils-phase8</c>.
/// </para>
/// <para>
/// The captures are the artifact a UX review reads. Every assertion here is about reachability and
/// state, never about pixels: the palette entry must exist and be clickable, the inserted element must
/// land on the canvas, and the conditional drawing must change the rendered SVG when its prop is set.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Wireframe")]
public class WireframeStencilGalleryPhase8E2ETests : WasmTestBase
{
    private const string WireframeEditorUrl = "/wireframe-editor";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-stencils-phase8");

    [TestInitialize]
    public void EnsureShotDir() => Directory.CreateDirectory(ShotDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    /// <summary>The first (interactive) editor — the demo page hosts several.</summary>
    private static ILocator FirstEditor(IPage page) => page.Locator(".tm-wd-editor").First;

    private static ILocator Toolbox(IPage page) => FirstEditor(page).Locator(".tm-wd-toolbox");

    private static ILocator PaletteItem(IPage page, string type)
        => Toolbox(page).Locator($".tm-wd-toolbox__item[data-component-type='{type}']");

    /// <summary>Boots the demo page with the sidebar margin removed, so the editor resolves its DESKTOP layout.</summary>
    private async Task<IPage> PrepareAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync(
            "document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); " +
            "s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } " +
            "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; " +
            "document.head.appendChild(s); });");
        await GotoAppReadyAsync(page, $"{BaseUrl}{WireframeEditorUrl}");
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await FirstEditor(page).Locator(".tm-wd-editor__toolbar")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });
        await page.EvaluateAsync(
            "() => { const style = document.createElement('style'); " +
            "style.textContent = '.tm-wd-editor__minimap-wrap { display: none !important; }'; " +
            "document.head.appendChild(style); }");
        await page.WaitForTimeoutAsync(1500);
        return page;
    }

    /// <summary>Filters the palette to one stencil so the capture shows the entry a designer would click.</summary>
    private static async Task FilterPaletteAsync(IPage page, string term)
    {
        var search = Toolbox(page).Locator(".tm-wd-toolbox__search-input");
        await search.FillAsync(term);
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Inserts a stencil through the canvas drop handler and returns the DOM id of the element that is
    /// NEW. The id is DIFFED against the ids the canvas already had, never taken from the last node in
    /// DOM order: the demo document is pre-seeded and the paint order follows z-index, so "the last
    /// <c>g[data-el-id]</c>" is not necessarily the element just dropped.
    /// </summary>
    private static async Task<string> InsertAsync(IPage page, string type, int x = 300, int y = 200)
    {
        var canvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        var beforeIds = await FirstEditor(page).Locator("g[data-el-id]")
            .EvaluateAllAsync<string[]>("groups => groups.map(group => group.getAttribute('data-el-id'))");

        var result = await page.EvaluateAsync<string>(
            """
            async ([type, canvasId, x, y]) => {
                const inst = window.tmWireframeDesigner.instances.get(canvasId);
                if (!inst) return JSON.stringify({ error: 'no-inst' });
                try {
                    await inst.dotNetRef.invokeMethodAsync('OnElementDropped', type, x, y);
                    return JSON.stringify({ ok: true });
                } catch (err) {
                    return JSON.stringify({ error: err.message });
                }
            }
            """,
            new object[] { type, canvasId, x, y });

        Assert.IsTrue(result.Contains("\"ok\":true"), $"the {type} stencil must insert onto the canvas (was {result})");
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n",
            beforeIds.Length + 1,
            new PageWaitForFunctionOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(500);

        var added = await page.EvaluateAsync<string>(
            """
            (before) => {
                const groups = document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]');
                const id = [...groups]
                    .map(group => group.getAttribute('data-el-id'))
                    .find(candidate => !before.includes(candidate));
                return id ?? '';
            }
            """,
            beforeIds);

        Assert.IsFalse(string.IsNullOrWhiteSpace(added), $"the inserted {type} element is findable on the canvas");
        return added;
    }

    /// <summary>Selects the inserted element so the properties panel shows ITS surface props.</summary>
    private static async Task SelectAsync(IPage page, string elementId)
    {
        var group = FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First;
        await group.ScrollIntoViewIfNeededAsync();
        await group.ClickAsync(new LocatorClickOptions { Force = true });
        await page.WaitForTimeoutAsync(400);
    }

    /// <summary>
    /// Switches a boolean schema prop on through the properties panel's own toggle. The panel paints a
    /// designed switch over a visually hidden <c>&lt;input type="checkbox"&gt;</c> (0×0,
    /// <c>pointer-events: none</c>), so Playwright cannot click the input itself — the editor's other
    /// bool-prop specs dispatch the same DOM click the label would.
    /// </summary>
    private static async Task TogglePropAsync(IPage page, string prop)
    {
        var input = FirstEditor(page).Locator($"input[id$='-p-{prop}']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        if (await input.IsCheckedAsync())
            return;

        await input.EvaluateAsync("element => element.click()");
        await page.WaitForTimeoutAsync(600);

        Assert.IsTrue(await input.IsCheckedAsync(),
            $"the properties panel must switch '{prop}' on — Blazor's change handler has to receive the click");
    }

    /// <summary>
    /// Sets the element's HEIGHT through the properties panel's Layout section — the designer action that
    /// makes an overlay stencil (the user menu's open panel) part of its own box, so the canvas fits the
    /// whole state instead of clipping it at the component's collapsed height.
    /// </summary>
    private static async Task SetLayoutHeightAsync(IPage page, int height)
    {
        var input = FirstEditor(page).Locator("input[id$='-layout-h']");
        await input.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await input.FillAsync(height.ToString(CultureInfo.InvariantCulture));
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>Chooses a value of an enum schema prop through the properties panel's select.</summary>
    private static async Task SelectEnumPropAsync(IPage page, string prop, string value)
    {
        var select = FirstEditor(page).Locator($"select[id$='-p-{prop}']");
        await select.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
        await select.SelectOptionAsync(value);
        await page.WaitForTimeoutAsync(500);
    }

    /// <summary>Counts how many times a literal appears in the rendered markup.</summary>
    private static int Occurrences(string markup, string literal)
    {
        var count = 0;
        var index = 0;
        while ((index = markup.IndexOf(literal, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += literal.Length;
        }

        return count;
    }

    private static async Task<string> ElementSvgAsync(IPage page, string elementId)
        => await FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First.InnerHTMLAsync();

    /// <summary>
    /// Frames ONE element to fill the canvas (instead of the page-wide fit, where a 10 px stencil label
    /// lands 4 px tall), using the editor's own <c>getElementBounds</c> and the viewBox it pans/zooms
    /// with — no test-only hooks. The capture is then clipped to the CANVAS as well as the element, so
    /// no editor chrome (toolbar, panels, selection handles of other elements) leaks into the artifact.
    /// </summary>
    private static async Task FrameAsync(IPage page, string elementId, double padding = 48, double reserveBelow = 0)
    {
        var canvasId = await FirstEditor(page).Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        var framed = await page.EvaluateAsync<bool>(
            """
            ([canvasId, id, pad, reserve]) => {
                const designer = window.tmWireframeDesigner;
                const inst = designer.instances.get(canvasId);
                if (!inst) return false;
                const bounds = designer.getElementBounds(inst.svg, id);
                if (!bounds || !bounds.w || !bounds.h) return false;
                const box = inst.svg.getBoundingClientRect();
                // The element plus its margin BECOMES the viewBox: the element fills the canvas and the
                // canvas clips the rest (preserveAspectRatio=meet letterboxes it, never crops it). An
                // OVERLAY stencil paints past its own box, so `reserve` keeps that space INSIDE the
                // viewBox — content outside the viewBox is never painted at all.
                designer._setViewBox(inst,
                    bounds.x - pad,
                    bounds.y - pad,
                    bounds.w + 2 * pad,
                    bounds.h + 2 * pad + reserve);
                inst.scale = box.width / (bounds.w + 2 * pad);
                return true;
            }
            """,
            new object[] { canvasId, elementId, padding, reserveBelow });

        Assert.IsTrue(framed, $"the canvas framed {elementId} for its capture");
        await page.WaitForTimeoutAsync(400);

        // A blur (or the pointer parking in a corner) can make the editor re-render, and a re-render
        // re-fits the canvas — so the viewBox is applied LAST and the capture must not touch focus again
        // (`alreadyPrepared` in ShootAsync).
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.WaitForTimeoutAsync(300);
    }

    /// <summary>
    /// Captures the box of <paramref name="target"/> (plus a small margin) into the shot folder. With
    /// <paramref name="clipToCanvas"/> the clip is intersected with the canvas, whose SVG clips its own
    /// content — a wider clip would capture the surrounding editor chrome (palette shots sit NEXT to the
    /// canvas, so they keep their own box).
    /// </summary>
    private async Task ShootAsync(
        IPage page,
        ILocator target,
        string name,
        int pad = 8,
        bool clipToCanvas = false,
        int extraBottom = 0,
        bool alreadyPrepared = false)
    {
        await page.WaitForTimeoutAsync(250);
        if (!alreadyPrepared)
        {
            await page.Mouse.MoveAsync(0, 0);
            await page.EvaluateAsync(
                "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        }

        var box = await target.BoundingBoxAsync()
                  ?? throw new InvalidOperationException($"{name}: the target has no bounding box");
        var left = box.X - pad;
        var top = box.Y - pad;
        var right = box.X + box.Width + pad;
        // An OVERLAY stencil (the user menu's panel, the combobox's popup) paints past the element's own
        // box, and the canvas clips the group at its declared height — so a state capture extends the
        // clip downwards, otherwise the very state it documents would be cut off.
        var bottom = box.Y + box.Height + pad + extraBottom;

        if (clipToCanvas)
        {
            var canvas = await FirstEditor(page).Locator(".tm-wd-canvas__svg").BoundingBoxAsync()
                         ?? throw new InvalidOperationException($"{name}: the canvas has no bounding box");
            left = Math.Max(left, canvas.X);
            top = Math.Max(top, canvas.Y);
            right = Math.Min(right, canvas.X + canvas.Width);
            bottom = Math.Min(bottom, canvas.Y + canvas.Height);
        }
        Assert.IsTrue(right - left > 40 && bottom - top > 20,
            $"{name}: the element is visible on the canvas ({right - left}×{bottom - top} px)");

        var directory = ShotDir;
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(directory, $"{name}.png"),
            Clip = new Clip { X = left, Y = top, Width = right - left, Height = bottom - top },
        });
        Assert.IsTrue(File.Exists(Path.Combine(directory, $"{name}.png")), $"{name} lands on disk");
    }

    [TestMethod]
    public async Task Phase8Stencils_AreInThePalette_AndRenderOnTheCanvas()
    {
        var page = await PrepareAsync();

        // Every stencil this phase adds or changes is FINDABLE by the name a designer would type.
        foreach (var (type, searchTerm) in new[]
                 {
                     ("TmAppShell", "App Shell"),
                     ("TmUserMenu", "User Menu"),
                     ("TmBottomNavigation", "Bottom Navigation"),
                     ("TmSidePanel", "Side Panel"),
                     ("TmMultiSelect", "Multi Select"),
                 })
        {
            await FilterPaletteAsync(page, searchTerm);
            Assert.AreEqual(1, await PaletteItem(page, type).CountAsync(),
                $"the palette must offer {type} when the designer searches '{searchTerm}'");
            await ShootAsync(page, Toolbox(page), $"palette-{type}", pad: 4);
        }

        // …and each one draws on the canvas at its default size.
        await FilterPaletteAsync(page, "");
        foreach (var type in new[] { "TmAppShell", "TmUserMenu", "TmBottomNavigation", "TmSidePanel", "TmMultiSelect" })
        {
            var elementId = await InsertAsync(page, type);
            var svg = await ElementSvgAsync(page, elementId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(svg), $"{type} must draw something on the canvas");
            Assert.IsFalse(svg.Contains("Missing component", StringComparison.OrdinalIgnoreCase),
                $"{type} must resolve through the Tempo pack, not render a fallback");

            await FrameAsync(page, elementId);
            await ShootAsync(page, FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First, $"default-{type}",
                clipToCanvas: true);
        }
    }

    [TestMethod]
    public async Task AppShell_ConditionalStates_SketchTheWorkAreaAndTheBottomBar()
    {
        var page = await PrepareAsync();
        await FilterPaletteAsync(page, "");

        var elementId = await InsertAsync(page, "TmAppShell", x: 260, y: 180);
        await SelectAsync(page, elementId);

        var defaultSvg = await ElementSvgAsync(page, elementId);
        Assert.IsTrue(defaultSvg.Contains("Content"), "the shell sketches its content region");
        Assert.IsTrue(defaultSvg.Contains("Overview"),
            "a dropped shell carries the schema's navigation items, so its docked column sketches them");

        await TogglePropAsync(page, "showWorkArea");
        var withWorkArea = await ElementSvgAsync(page, elementId);
        Assert.IsTrue(withWorkArea.Contains("Work area"),
            "the work-area sheet is an opt-in region, so toggling showWorkArea must draw it");
        Assert.IsFalse(defaultSvg.Contains("Work area"), "the default sketch has no work-area sheet");
        await FrameAsync(page, elementId);
        await ShootAsync(page, FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First, "state-TmAppShell-workarea",
            clipToCanvas: true);

        await SelectEnumPropAsync(page, "mobileNav", "BottomNavigation");
        var withBottomBar = await ElementSvgAsync(page, elementId);
        Assert.AreNotEqual(withWorkArea, withBottomBar,
            "the phone presentation is a different sketch: the bottom bar replaces the hamburger");
        await FrameAsync(page, elementId);
        await ShootAsync(page, FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First, "state-TmAppShell-bottomnav",
            clipToCanvas: true);
    }

    [TestMethod]
    public async Task UserMenu_OpenPanel_SketchesTheProfileTheItemsAndTheThemeRow()
    {
        var page = await PrepareAsync();
        await FilterPaletteAsync(page, "");

        var elementId = await InsertAsync(page, "TmUserMenu");
        await SelectAsync(page, elementId);

        var closed = await ElementSvgAsync(page, elementId);
        Assert.IsFalse(closed.Contains(">Profile<"), "a closed account menu has no panel, so no host entries");
        Assert.AreEqual(1, Occurrences(closed, "Jane Cooper"),
            "the closed trigger states the account name exactly once");

        await TogglePropAsync(page, "open");
        var open = await ElementSvgAsync(page, elementId);
        Assert.IsTrue(open.Contains(">Profile<"), "the open panel carries the host's own entries");
        Assert.AreEqual(2, Occurrences(open, "Jane Cooper"),
            "the panel repeats the account name from the trigger — trigger plus profile card");

        await TogglePropAsync(page, "showThemeToggle");
        var withTheme = await ElementSvgAsync(page, elementId);
        Assert.IsTrue(withTheme.Contains(">Theme<"), "the theme row is opt-in and must appear when switched on");

        // The open panel paints below the trigger's 44 px; a designer sizes the element to hold it, which
        // also lets the canvas fit the whole state instead of clipping it.
        await SetLayoutHeightAsync(page, 250);
        await page.WaitForTimeoutAsync(400);
        var resized = await ElementSvgAsync(page, elementId);
        Assert.IsTrue(resized.Contains("Settings"),
            "the open panel lists the host entries from the items default (a browser-only miss means the " +
            "dropped element's string-list prop did not survive the size change)");
        await FrameAsync(page, elementId, padding: 24, reserveBelow: 40);
        await ShootAsync(page, FirstEditor(page).Locator($"g[data-el-id='{elementId}']").First, "state-TmUserMenu-open",
            clipToCanvas: true, extraBottom: 320, alreadyPrepared: true);
    }

    [TestMethod]
    public async Task MultiSelect_OpenCombobox_SketchesTheCreateRowAndTheMatchHighlight()
    {
        var page = await PrepareAsync();
        await FilterPaletteAsync(page, "");

        var elementId = await InsertAsync(page, "TmMultiSelect");
        await SelectAsync(page, elementId);

        var closed = await ElementSvgAsync(page, elementId);
        Assert.IsFalse(closed.Contains("Admin"), "a closed field draws no option list");

        var openId = elementId;
        await TogglePropAsync(page, "open");
        var open = await ElementSvgAsync(page, openId);
        Assert.IsTrue(open.Contains("Admin"), "the open combobox sketches its option rows");
        Assert.IsFalse(open.Contains("Create"), "the create row is still opt-in");

        await TogglePropAsync(page, "showCreateRow");
        await TogglePropAsync(page, "highlight");
        var full = await ElementSvgAsync(page, openId);
        Assert.IsTrue(full.Contains("Create"), "the create row is the LAST entry of the open list");
        Assert.IsTrue(full.Contains(">adm<"), "the typed term is marked inside the matching option");
        Assert.IsFalse(full.Contains(">Admin<"), "the mark splits the label into a marked run and the rest");
        await FrameAsync(page, openId, padding: 24, reserveBelow: 270);
        await ShootAsync(page, FirstEditor(page).Locator($"g[data-el-id='{openId}']").First, "state-TmMultiSelect-open",
            clipToCanvas: true, extraBottom: 280, alreadyPrepared: true);
    }
}
