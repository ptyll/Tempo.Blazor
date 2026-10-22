using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// PdfAnnotator redesign phase 8 (FZE-8): the annotator and its panel on tablet and mobile. On a
/// coarse-pointer tablet (1024×768) the tools carry labels below the icons, every annotator-owned
/// control meets the 44px touch target and the thread panel is the shell's docked side region; on
/// a phone (390×844, touch) the G2 rules hold — highlight/stamp/draw are gone, the palette is a
/// horizontal tile row, the F5 action bar carries Export/Flatten/Print with the rest in "More",
/// the thread panel is a bottom sheet (which starts closed at load, phase 8 review 3) opened
/// from a thread marker, from "Comments" or from a comment draft at the half snap, where the
/// reply input and its Resolve secondary sit side by side, fully inside the sheet's visible
/// band and uncovered (and an edit of the own comment works through the ⋯ menu); 320px stays
/// free of horizontal overflow with both palette tiles fully visible. Screenshots land in
/// <c>__screenshots__/pdf-annotator/{tablet,mobile,mobile-320}-*.png</c> for the UX review
/// against <c>output/imagegen/tempo-reimagined/Tempo.Blazor.PdfViewer/pdf-annotator--tablet.png</c>
/// and friends.
/// </summary>
[TestClass]
public class PdfAnnotatorResponsiveE2ETests : WasmTestBase
{
    private const string AnnotatorPage = "/pdf-annotator";
    private const string TabletSectionTitle = "Tablet Layout";
    private const string MobileSectionTitle = "Mobile Layout (G2)";

    private static string ShotDir(string sub)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", sub);
            if (Directory.Exists(Path.Combine(current.FullName, "tests", "Tempo.Blazor.E2E")))
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }

            current = current.Parent!;
        }

        throw new InvalidOperationException("Could not locate the E2E screenshot directory.");
    }

    private async Task<IPage> OpenAsync(int width, int height, bool touch)
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
        await page.GotoAsync($"{BaseUrl}{AnnotatorPage}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        return page;
    }

    private static ILocator Section(IPage page, string title)
        => page.Locator($"section:has(h2:has-text('{title}'))");

    /// <summary>Waits until the section's annotator has loaded its document (page input present).</summary>
    private static async Task WaitForLoadedAnnotatorAsync(ILocator section)
    {
        var input = section.Locator(".tm-pdf-viewer__page-input").First;
        await input.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
    }

    private async Task ParkAndShootAsync(IPage page, ILocator section, string directory, string name)
    {
        await section.EvaluateAsync("el => el.scrollIntoView({ block: 'start', behavior: 'instant' })");
        // The sheet is anchored to the stage's bottom edge: bring the stage fully into view so
        // the pinned composer is on screen in the shot (no-op when it already fits; the stage
        // element only exists on the mobile layout).
        await StageIntoViewAsync(page, section);
        await page.WaitForTimeoutAsync(400);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        var path = Path.Combine(directory, name + ".png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, FullPage = false, Timeout = 60_000 });
        TestContext.AddResultFile(path);
    }

    /// <summary>Scrolls the annotator stage fully into view: the mobile sheet is anchored to
    /// the stage's bottom edge, so when the stage runs past the viewport fold the pinned
    /// composer is rendered off-screen and every "in view" measurement would race the scroll
    /// position (phase 8 review 12). <c>nearest</c> is a no-op when the stage already fits.</summary>
    private static async Task StageIntoViewAsync(IPage page, ILocator section)
    {
        var stage = section.Locator(".tm-editor-shell__stage");
        if (await stage.CountAsync() > 0)
        {
            await stage.First.EvaluateAsync("el => el.scrollIntoView({ block: 'nearest', behavior: 'instant' })");
            await page.WaitForTimeoutAsync(300);
        }
    }

    // ── Tablet: 44px targets and labeled tools ───────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Tablet_TouchTargets44_ToolsLabeled()
    {
        // 1180×820 (iPad Air landscape): the forced-tablet section is the touch evidence and,
        // since the phase 10 review, renders full-bleed in the demo column — its container is
        // a real tablet width (≥768px), not the squeezed desktop column.
        var page = await OpenAsync(1180, 820, touch: true);
        Assert.IsTrue(await page.EvaluateAsync<bool>("() => matchMedia('(pointer: coarse)').matches"),
            "precondition: the tablet context emulates a coarse pointer");

        // The forced-tablet section is the touch evidence: its layout does not depend on how
        // the demo column measures at this viewport.
        var section = Section(page, TabletSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);

        // Phase 10 review: the evidence screenshot must come from a ≥768px container — the
        // full-bleed demo section promises a real tablet width.
        var rootWidth = await section.EvaluateAsync<double>(
            "el => { const a = el.querySelector('.tm-pdf-annotator'); return a ? a.getBoundingClientRect().width : 0; }");
        Assert.IsTrue(rootWidth >= 768,
            $"the tablet evidence must render in a container ≥768px wide (measured {rootWidth:0.#}px)");

        // Every visible annotator-owned control in the merged toolbar meets the 44px touch
        // target on coarse: the tool pills, the export/print buttons and the panel toggle.
        var sizes = await section.EvaluateAsync<string>(@"
el => {
    const toolbar = el.querySelector('.tm-pdf-viewer__toolbar');
    if (!toolbar) return 'missing-toolbar';
    const small = [...toolbar.querySelectorAll('button, input, [role=button], a')]
        .filter(b => b.offsetParent !== null)
        .filter(b => (b.getBoundingClientRect().width < 43 || b.getBoundingClientRect().height < 43))
        .map(b => (b.getAttribute('data-testid') ?? b.getAttribute('aria-label') ?? b.className) +
                  ':' + Math.round(b.getBoundingClientRect().width) + 'x' + Math.round(b.getBoundingClientRect().height));
    return small.length === 0 ? 'ok' : small.join(', ');
    }");
        Assert.AreEqual("ok", sizes,
            "on a coarse pointer every annotator toolbar control must meet the --tm-touch-target minimum");

        // The phase 8 review extends the coarse check to the thread panel: the header ✕, the
        // "Show resolved" label (the checkbox's real target) and the card toggles. The native
        // checkbox input itself is skipped — its LABEL is the enlarged touch target.
        var panelSizes = await section.EvaluateAsync<string>(@"
el => {
    const panel = el.querySelector(""[data-testid='pdf-annotator-panel']"");
    if (!panel) return 'missing-panel';
    const small = [...panel.querySelectorAll('button, [role=button], a, label')]
        .filter(b => b.offsetParent !== null)
        .filter(b => (b.getBoundingClientRect().width < 43 || b.getBoundingClientRect().height < 43))
        .map(b => (b.getAttribute('data-testid') ?? b.getAttribute('aria-label') ?? b.className) +
                  ':' + Math.round(b.getBoundingClientRect().width) + 'x' + Math.round(b.getBoundingClientRect().height));
    return small.length === 0 ? 'ok' : small.join(', ');
    }");
        Assert.AreEqual("ok", panelSizes,
            "on a coarse pointer every annotator panel control must meet the --tm-touch-target minimum");

        // The annotator's tools show their labels UNDER the icons (the tablet ribbon style).
        var labeledTools = await section.Locator(
            "[data-testid='pdf-annotator-tools'] .tm-toolbar-btn--label-below").CountAsync();
        Assert.IsTrue(labeledTools >= 5,
            $"the five annotator tools render labels below the icons, found {labeledTools}");

        // Phase 10 review: the tablet ribbon is ONE scrollable row at every container width —
        // the pills keep their labels (never collapsing to icons), the color label stays
        // VISIBLE and the panel toggle stays in the row. A narrow pane scrolls the row
        // horizontally instead of wrapping it to a second line.
        var rowReport = await section.EvaluateAsync<string>(@"
el => {
    const row = el.querySelector('[data-testid=""pdf-annotator-toolbar""]');
    if (!row) return 'missing-row';
    const rr = row.getBoundingClientRect();
    const bad = [];
    let firstTop = null;
    for (const id of ['browse', 'highlight', 'comment', 'stamp', 'draw']) {
        const btn = el.querySelector('[data-testid=""pdf-annotator-mode-' + id + '""]');
        if (!btn) { bad.push(id + ':missing'); continue; }
        const b = btn.getBoundingClientRect();
        if (b.height <= 0) { bad.push(id + ':hidden'); continue; }
        if (b.top < rr.top - 0.5 || b.bottom > rr.bottom + 0.5)
            bad.push(id + ':outside-row ' + Math.round(b.top) + '..' + Math.round(b.bottom) +
                ' vs row ' + Math.round(rr.top) + '..' + Math.round(rr.bottom));
        if (firstTop === null) firstTop = Math.round(b.top);
        else if (Math.abs(firstTop - Math.round(b.top)) > 1) bad.push(id + ':second-line');
        const text = btn.querySelector('.tm-toolbar-btn-text');
        if (!text || text.getClientRects().length === 0) bad.push(id + ':label-hidden');
    }
    const chip = el.querySelector('.tm-pdf-annotator__color-chip');
    const label = el.querySelector('.tm-pdf-annotator__color-label');
    if (!chip || !label) bad.push('missing-color');
    else {
        const c = chip.getBoundingClientRect(), l = label.getBoundingClientRect();
        if (l.width < 8 || l.height < 8)
            bad.push('color-label-hidden:' + Math.round(l.width) + 'x' + Math.round(l.height));
        else {
            const overlap = Math.min(c.bottom, l.bottom) - Math.max(c.top, l.top);
            if (overlap < c.height / 2) bad.push('chip-label-not-on-one-line:' + Math.round(overlap) + 'px overlap');
        }
    }
    const toggle = el.querySelector('[data-testid=""pdf-annotator-panel-toggle""]');
    if (!toggle) bad.push('missing-panel-toggle');
    else {
        const t = toggle.getBoundingClientRect();
        if (t.height <= 0) bad.push('panel-toggle-hidden');
        else if (t.top < rr.top - 0.5 || t.bottom > rr.bottom + 0.5) bad.push('panel-toggle-wrapped');
    }
    return bad.length === 0 ? 'ok' : bad.join(', ');
    }");
        Assert.AreEqual("ok", rowReport,
            $"the tablet ribbon must keep the tool labels, the color label and the panel toggle on one scrollable row (never wrapping, never dropping a label); got '{rowReport}'");

        // The thread panel is the shell's side region (docked), not an inline column squeeze.
        var sidePanel = section.Locator("[data-region='right'] [data-testid='pdf-annotator-panel']").First;
        await sidePanel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsTrue(await sidePanel.IsVisibleAsync(), "the thread panel is the docked side sheet");

        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "tablet-tools-labeled");
    }

    // ── Mobile: the bottom sheet reply/resolve flow ──────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Mobile_ReplyAndResolveInSheet()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var section = Section(page, MobileSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);

        // Park the section at the viewport top first: the sheet is anchored to the annotator
        // stage, so the composer pinned to the sheet bottom is only inside the fold when the
        // stage's bottom edge is on screen — the marker's own auto-scroll can otherwise leave
        // it below the fold and the in-view assertions would race the page scroll position.
        await section.EvaluateAsync("el => el.scrollIntoView({ block: 'start', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(400);

        // G2: the editing tools do not exist on a phone.
        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-annotator-mode-highlight']").CountAsync());
        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-annotator-mode-stamp']").CountAsync());
        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-annotator-mode-draw']").CountAsync());

        // G2 (phase 8 review 3): the sheet starts CLOSED — the page is reachable at load and
        // the marker opens it on the thread.
        await section.Locator(".tm-drawer--bottom").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

        // Tapping a thread marker opens the panel bottom sheet on that thread, at the HALF
        // snap, with the selected card scrolled into view.
        var marker = section.Locator("[data-testid='pdf-annotator-marker']").First;
        await marker.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await marker.ClickAsync();

        var sheet = section.Locator(".tm-drawer--bottom").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var panel = section.Locator("[data-testid='pdf-annotator-panel']").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // The sheet opened ON the tapped thread (selected = expanded detail with the composer).
        var expanded = panel.Locator("[data-testid='pdf-annotator-thread'][aria-expanded='true']").First;
        await expanded.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // The sheet is anchored to the stage's bottom edge — when the stage runs past the
        // viewport fold the pinned composer is rendered off-screen, so bring the stage fully
        // into view before the in-view assertions (phase 8 review 12).
        await StageIntoViewAsync(page, section);

        // Phase 8 review 12: the reply input and the Resolve ghost secondary sit fully inside
        // the drawer body's visible rect (the sheet's scrollport clipped to the viewport), and
        // nothing covers them — elementFromPoint at each center must hit the element itself or
        // one of its descendants.
        var composerReport = await section.EvaluateAsync<string>(@"
el => {
    const body = el.querySelector('.tm-drawer__body');
    if (!body) return 'missing-body';
    const br = body.getBoundingClientRect();
    const visible = {
        left: Math.max(br.left, 0), top: Math.max(br.top, 0),
        right: Math.min(br.right, window.innerWidth),
        bottom: Math.min(br.bottom, window.innerHeight)
    };
    const bad = [];
    const targets = [
        ['reply-input', [...el.querySelectorAll('[data-testid=""pdf-annotator-panel""] textarea')].pop()],
        ['resolve', el.querySelector('[data-testid=""pdf-annotator-resolve""]')]
    ];
    for (const [name, target] of targets) {
        if (!target) { bad.push(name + ':missing'); continue; }
        const b = target.getBoundingClientRect();
        if (b.left < visible.left - 0.5 || b.right > visible.right + 0.5 ||
            b.top < visible.top - 0.5 || b.bottom > visible.bottom + 0.5)
            bad.push(name + ':' + Math.round(b.left) + ',' + Math.round(b.top) + '..' +
                Math.round(b.right) + ',' + Math.round(b.bottom) + ' vs visible ' +
                Math.round(visible.left) + ',' + Math.round(visible.top) + '..' +
                Math.round(visible.right) + ',' + Math.round(visible.bottom));
        const cx = b.left + b.width / 2, cy = b.top + b.height / 2;
        const hit = document.elementFromPoint(cx, cy);
        if (!hit || (hit !== target && !target.contains(hit)))
            bad.push(name + '-covered-by:' + (hit ? hit.tagName + '.' + hit.className : 'null'));
    }
    return bad.length === 0 ? 'ok' : bad.join(', ');
    }");
        Assert.AreEqual("ok", composerReport,
            $"the reply composer and Resolve must sit fully inside the sheet's visible band, uncovered; got '{composerReport}'");

        var replyInput = panel.Locator("textarea").Last;
        var resolve = panel.Locator("[data-testid='pdf-annotator-resolve']").First;
        await replyInput.FillAsync("Mobile reply from the sheet");
        await replyInput.PressAsync("Enter");
        await page.WaitForTimeoutAsync(500);
        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "mobile-reply-sheet");

        // Resolve through the composer's ghost secondary, then verify the resolved chip under
        // the "All" filter (a resolved thread leaves the default Unresolved view; phase 12
        // replaced the "Show resolved" checkbox with the segmented filter).
        await resolve.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        await panel.Locator(".tm-pdf-thread-filter__segment:has(input[data-testid='pdf-annotator-filter-all'])").ClickAsync();
        var chip = panel.Locator(".tm-pdf-thread-card__chip--success").First;
        await chip.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
    }

    /// <summary>Asserts an element is rendered inside the layout viewport (the E2E form of
    /// "in view"): its bounding box intersects the viewport rect with a positive area.</summary>
    private static async Task AssertInViewAsync(IPage page, ILocator locator, string label)
    {
        var box = await locator.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"{label} has no bounding box.");
        var viewport = page.ViewportSize ?? throw new InvalidOperationException("No viewport.");
        var inView = box.X + box.Width > 0 && box.Y + box.Height > 0
            && box.X < viewport.Width && box.Y < viewport.Height;
        Assert.IsTrue(inView,
            $"{label} ({box.Width}x{box.Height} at {box.X},{box.Y}) must be inside the " +
            $"{viewport.Width}x{viewport.Height} viewport.");
    }

    // ── Mobile: the default state (phase 8 review 3) ─────────────────────────

    /// <summary>
    /// The mobile presentation's load state: the sheet starts closed, so the page canvas, the
    /// G2 tool palette and the F5 action bar are all visible without any interaction. Screenshot
    /// <c>mobile-default.png</c> for the UX review.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Mobile_DefaultState_SheetClosed()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var section = Section(page, MobileSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);

        Assert.AreEqual(0, await section.Locator(".tm-drawer--bottom").CountAsync(),
            "the mobile sheet must start closed (phase 8 review 3)");

        // Bring the annotator root to the top of the viewport so the "in view" checks measure
        // the visible chrome, not an off-screen scroll position.
        await section.Locator(".tm-pdf-annotator").First
            .EvaluateAsync("el => el.scrollIntoView({ block: 'start', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(400);

        // The page canvas, the G2 palette and the F5 action bar are the visible chrome.
        var canvas = section.Locator(".tm-pdf-viewer__canvas-wrap").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await AssertInViewAsync(page, canvas, "the page canvas");
        var palette = section.Locator("[data-testid='pdf-annotator-palette']").First;
        await AssertInViewAsync(page, palette, "the G2 tool palette");
        var bar = section.Locator(".tm-mobile-action-bar__bar").First;
        await AssertInViewAsync(page, bar, "the F5 action bar");

        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "mobile-default");
    }

    // ── Mobile: light edit of the own comment ────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Mobile_EditOwnComment()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var section = Section(page, MobileSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);

        // The sheet starts closed (phase 8 review 3) — the palette and the page are reachable
        // at load without any close gesture.
        await section.Locator(".tm-drawer--bottom").WaitForAsync(
            new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 15000 });

        // Light edit #1: the Add comment tile arms the comment tool. The palette is a
        // horizontally scrollable track — slide the tile into view before the tap.
        var commentTile = section.Locator("[data-testid='pdf-annotator-mode-comment']").First;
        await commentTile.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await commentTile.EvaluateAsync("el => el.scrollIntoView({ block: 'nearest', inline: 'center' })");
        await page.WaitForTimeoutAsync(200);
        await commentTile.ClickAsync();
        Assert.AreEqual("true", await commentTile.GetAttributeAsync("aria-pressed"),
            "the palette tile mirrors the active tool");

        // Tapping the page creates the draft and opens the panel sheet with its composer.
        var surface = section.Locator("[data-testid='pdf-annotator-surface']").First;
        await surface.ClickAsync();

        var draftInput = section.Locator("[data-testid='pdf-annotator-new-input']").First;
        await draftInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await draftInput.FillAsync("Mobile note from the palette");
        await section.Locator("[data-testid='pdf-annotator-new-submit']").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        // Light edit #2: edit the own comment through the comment's ⋯ menu. The draft composer
        // is pinned to the sheet's bottom edge (the half snap it opened at), so the trigger may
        // sit under it — slide the handle up to the full snap (the keyboard gesture the slider
        // exposes) like a user would drag the sheet, then click.
        var handle = section.Locator(".tm-sheet__handle").First;
        await handle.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await handle.FocusAsync();
        await handle.PressAsync("ArrowUp");
        await handle.PressAsync("ArrowUp");
        await page.WaitForTimeoutAsync(400);

        var commentMenu = section.Locator(
            "[data-testid='pdf-annotator-panel'] .tm-pdf-comment-item__menu .tm-dropdown-trigger").First;
        await commentMenu.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await commentMenu.ClickAsync();
        var editItem = page.Locator("[data-testid='pdf-annotator-edit']").First;
        await editItem.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await editItem.ClickAsync();

        var editInput = section.Locator("[data-testid='pdf-annotator-edit-input']").First;
        await editInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await editInput.FillAsync("Mobile note, edited");
        await section.Locator("[data-testid='pdf-annotator-edit-save']").First.ClickAsync();
        await page.WaitForTimeoutAsync(500);

        var panel = section.Locator("[data-testid='pdf-annotator-panel']").First;
        Assert.IsTrue((await panel.InnerTextAsync()).Contains("Mobile note, edited"),
            "the edited body is visible in the sheet");

        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "mobile-edit-comment");
    }

    // ── 320: no horizontal overflow ──────────────────────────────────────────

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_320_NoHorizontalOverflow()
    {
        var page = await OpenAsync(320, 640, touch: true);
        var section = Section(page, MobileSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);
        await page.WaitForTimeoutAsync(500);

        var overflow = await section.EvaluateAsync<bool>(@"
() => {
    const root = document.querySelector('section .tm-pdf-annotator');
    if (!root) return true;
    return root.scrollWidth > root.clientWidth + 1;
    }");
        Assert.IsFalse(overflow, "the annotator must not introduce horizontal overflow at 320px");

        // Phase 8 review 10 (phase 12: the color tile joined the row): every palette tile is
        // reachable — inside the scrollable track's coordinate space (never clipped to the
        // left, never zero-width) — while the annotator ROOT itself never overflows. At 320px
        // the three tiles exceed the viewport, which is exactly what the scrollable track is
        // for (the hint yields, the track scrolls).
        var tileBoxes = await section.EvaluateAsync<string>(@"
el => {
    const track = el.querySelector('.tm-pdf-annotator__palette-track');
    if (!track) return 'missing-track';
    const tr = track.getBoundingClientRect();
    const tiles = [...el.querySelectorAll('.tm-pdf-annotator__tile')];
    return tiles.map(t => {
        const b = t.getBoundingClientRect();
        return (b.width > 0 && b.left >= tr.left - 1)
            ? 'ok' : (t.getAttribute('data-testid') ?? '') + ':' + Math.round(b.left) + '..' + Math.round(b.right);
    }).join(',');
    }");
        Assert.AreEqual("ok,ok,ok", tileBoxes,
            $"all mobile palette tiles (browse, comment, color) must stay reachable in the scrollable track, got '{tileBoxes}'");

        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "mobile-320-annotator");
    }

    // ── Phase 12: the color palette as the mobile bottom sheet ────────────────

    /// <summary>Phase 12 (G2): the color tile rides the Browse/Add-comment palette row and
    /// opens the palette as the F3 bottom sheet; a pick applies to new annotations and closes
    /// the sheet with the tile swatch updated. Ends with the
    /// <c>color-picker-mobile-sheet</c> screenshot.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_ColorPicker_MobileSheet()
    {
        var page = await OpenAsync(390, 844, touch: true);
        var section = Section(page, MobileSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);

        var tile = section.Locator("[data-testid='pdf-annotator-color-tile']").First;
        await tile.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Tap the tile: the palette opens as a bottom sheet (never a popover on a phone).
        await tile.TapAsync();
        var sheet = section.Locator(".tm-overlay-panel-sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        var palette = sheet.Locator(".tm-color-palette").First;
        await palette.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "color-picker-mobile-sheet");

        // The reset action rides the sheet too; then pick a swatch — the sheet closes and the
        // tile's swatch shows the picked color.
        Assert.IsNotNull(await sheet.Locator("[data-testid='pdf-annotator-color-reset']").First.ElementHandleAsync(),
            "the reset action is reachable inside the sheet");
        await sheet.Locator(".tm-color-palette-swatch[style*='#0e7490']").First.TapAsync();
        await Assertions.Expect(section.Locator(".tm-overlay-panel-sheet"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains((await section.Locator(".tm-pdf-annotator__tile-swatch").First
            .GetAttributeAsync("style")) ?? "", "#0e7490",
            "the tile swatch shows the picked color after the sheet closes");
    }
}
