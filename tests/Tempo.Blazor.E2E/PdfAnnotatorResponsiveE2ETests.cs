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

    // ── FZE-14: the annotator in Continuous mode (per-page tools) ────────────

    private const string ContinuousSectionTitle = "Continuous Mode";

    /// <summary>The annotator's interaction surface inside one continuous page slot.</summary>
    private static ILocator PageSurface(ILocator section, int page)
        => section.Locator($".tm-pdf-viewer__page-overlay-slot[data-page='{page}'] [data-testid='pdf-annotator-surface']");

    private static ILocator PageSlot(ILocator section, int page)
        => section.Locator($".tm-pdf-viewer__page-overlay-slot[data-page='{page}']");

    /// <summary>Waits for the continuous column's per-page surfaces (the JS geometry report
    /// mounted the band) and returns the section locator. The section is centered first, so
    /// the annotator's 600px stage is inside the viewport for the mouse-driven flows.</summary>
    private static async Task<ILocator> OpenContinuousSectionAsync(IPage page)
    {
        var section = Section(page, ContinuousSectionTitle);
        await section.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await WaitForLoadedAnnotatorAsync(section);
        await section.Locator(".tm-pdf-viewer__page-overlay-slot[data-page] [data-testid='pdf-annotator-surface']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000, State = WaitForSelectorState.Attached });
        await section.EvaluateAsync("el => el.scrollIntoView({ block: 'center', behavior: 'instant' })");
        await page.WaitForTimeoutAsync(300);
        return section;
    }

    /// <summary>A point on a page slot at relative coordinates — the slot box IS the page
    /// geometry (the FZE-13 report), so its bounding box is exactly the annotator surface.</summary>
    private static async Task<(double X, double Y)> SlotPointAsync(ILocator slot, double fx, double fy)
    {
        var box = await slot.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The page slot has no bounding box.");
        return (box.X + box.Width * fx, box.Y + box.Height * fy);
    }

    /// <summary>
    /// The visible intersection of a page slot and the continuous wrap: the slot's layout
    /// rect spans the whole page (taller than the wrap's window), so a fixed-ratio point can
    /// fall OUTSIDE the wrap's clip — the pointer would land on the chrome above the stage
    /// and never reach the surface. The intersection is the receivable area.
    /// </summary>
    private static async Task<(double X, double Y, double W, double H)> VisibleSlotBoxAsync(
        ILocator section, int pageNumber)
    {
        var slot = await PageSlot(section, pageNumber).BoundingBoxAsync()
            ?? throw new InvalidOperationException("The page slot has no bounding box.");
        var wrap = await section.Locator(".tm-pdf-viewer__continuous-wrap").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The continuous wrap has no bounding box.");
        var left = Math.Max(slot.X, wrap.X);
        var right = Math.Min(slot.X + slot.Width, wrap.X + wrap.Width);
        var top = Math.Max(slot.Y, wrap.Y);
        var bottom = Math.Min(slot.Y + slot.Height, wrap.Y + wrap.Height);
        Assert.IsTrue(right > left && bottom > top,
            "the page slot has no visible intersection with the continuous wrap");
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Scrolls the section's continuous WRAP to a page and waits for that page's slot
    /// surface AND its painted canvas: the mount band is the wrap's ±50% scroll band, so a
    /// slot far from the current band appears only after the column scrolls there — and a
    /// slot-less element could never be scroll-targeted by an element locator.
    /// </summary>
    private static async Task<ILocator> GoToPageAsync(IPage page, ILocator section, int pageNumber)
    {
        await section.EvaluateAsync(
            @"(section, target) => {
                const wrap = section.querySelector('.tm-pdf-viewer__continuous-wrap');
                wrap?.querySelector('.tm-pdf-viewer__page[data-page=""' + target + '""]')
                    ?.scrollIntoView({ block: 'start', behavior: 'instant' });
            }", pageNumber);
        var slot = PageSlot(section, pageNumber);
        await PageSurface(section, pageNumber)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000, State = WaitForSelectorState.Attached });
        await page.WaitForFunctionAsync($@"() => {{
            const section = [...document.querySelectorAll('section')]
                .find(s => s.querySelector('h2')?.textContent?.includes('{ContinuousSectionTitle}'));
            const wrap = section?.querySelector('.tm-pdf-viewer__continuous-wrap');
            const pageWrap = wrap?.querySelector('.tm-pdf-viewer__page[data-page=""{pageNumber}""]');
            return !!pageWrap?.querySelector('canvas[data-rendered]');
        }}", null, new PageWaitForFunctionOptions { Timeout = 60000 });
        return slot;
    }

    /// <summary>
    /// The first viewport-visible text span of a page's layer (the same guarantee the
    /// viewer's continuous selection E2E uses): a fixed-ratio drag could fall between lines,
    /// but a picked span always selects text.
    /// </summary>
    private static async Task<(double X, double Y, double W, double H)> VisibleSpanAsync(
        IPage page, ILocator section, int pageNumber)
    {
        var json = await section.EvaluateAsync<string>(@"
(section, target) => {
    const wrap = section.querySelector('.tm-pdf-viewer__continuous-wrap');
    const wb = wrap.getBoundingClientRect();
    const spans = [...wrap.querySelectorAll('.tm-pdf-viewer__page[data-page=""' + target + '""] .tm-pdf-viewer__text-layer span')]
        .map(s => { const b = s.getBoundingClientRect(); return { x: b.x, y: b.y, w: b.width, h: b.height }; })
        .filter(b => b.w > 30 && b.h > 5 && b.y > wb.y + 8 && b.y + b.h < wb.y + wb.height - 8
            && b.x > wb.x && b.x + b.w < wb.x + wb.width);
    return spans.length ? JSON.stringify(spans[0]) : 'none';
}", pageNumber);
        Assert.AreNotEqual("none", json, $"the page-{pageNumber} layer must have a visible text span");
        var span = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json!);
        return (span.GetProperty("x").GetSingle(), span.GetProperty("y").GetSingle(),
                span.GetProperty("w").GetSingle(), span.GetProperty("h").GetSingle());
    }

    /// <summary>Picks an annotator tool through the merged toolbar (or its "More" overflow on
    /// a narrow row — the overflow items carry the button's toolbar item id).</summary>
    private static async Task ClickToolAsync(ILocator section, string testId)
    {
        var tool = section.Locator($"[data-testid='{testId}']").First;
        if (await tool.IsVisibleAsync() && await tool.IsEnabledAsync())
        {
            await tool.ClickAsync();
            return;
        }

        var menuId = await tool.GetAttributeAsync("data-tm-toolbar-item");
        Assert.IsNotNull(menuId, $"the overflowed tool {testId} keeps its toolbar item id");
        await section.Locator("button.tm-toolbar-more").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await section.Locator($"[data-action-id='{menuId}']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Continuous_HighlightOnPage2()
    {
        var page = await OpenAsync(1600, 1000, touch: false);
        var section = await OpenContinuousSectionAsync(page);

        // The mode toggle rides the merged toolbar (FZE-14): the segmented control is there
        // and Continuous is the checked segment.
        Assert.IsTrue(await section.Locator(".tm-pdf-viewer__view-mode input[type='radio']").Last
            .IsVisibleAsync(), "the single/continuous segment rides the merged toolbar");
        Assert.IsTrue(await section.Locator(".tm-pdf-viewer__view-mode input[type='radio']").First
            .EvaluateAsync<bool>("el => !el.checked"), "Continuous is the checked segment");

        // Highlight text on page 2: the selection is reported with the page it was made on.
        await ClickToolAsync(section, "pdf-annotator-mode-highlight");
        await GoToPageAsync(page, section, 2);
        var (sx, sy, sw, sh) = await VisibleSpanAsync(page, section, 2);
        var dragY = (float)(sy + sh / 2);
        await page.Mouse.MoveAsync((float)sx + 4, dragY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(sx + sw * 0.8), dragY, new MouseMoveOptions { Steps = 12 });
        await page.Mouse.UpAsync();

        var composer = section.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Continuous highlight on page 2");
        await section.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        // The draft marker lives in the PAGE-2 slot — never on the annotator's current page.
        await PageSlot(section, 2).Locator("[data-testid='pdf-annotator-highlight']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "continuous-desktop");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Continuous_StampOnPage4_PersistsAfterReload()
    {
        var page = await OpenAsync(1600, 1000, touch: false);
        var section = await OpenContinuousSectionAsync(page);

        await ClickToolAsync(section, "pdf-annotator-mode-stamp");
        var stampSelect = section.Locator("[data-testid='pdf-annotator-stamp-select']");
        await stampSelect.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await stampSelect.SelectOptionAsync(new SelectOptionValue { Index = 1 });

        // With Stamp active the segmented view-mode toggle is still a SINGLE row: its height
        // stays within 1.2× the touch target (a wrapped second row would roughly double it —
        // the toolbar's narrow-overflow path handles the space, never a stacked segment).
        var toggleReport = await section.EvaluateAsync<string>(@"el => {
            const toggle = el.querySelector('.tm-pdf-viewer__view-mode');
            if (!toggle) return JSON.stringify({ missing: true });
            const probe = document.createElement('div');
            probe.style.height = 'var(--tm-touch-target)';
            toggle.appendChild(probe);
            const target = probe.getBoundingClientRect().height;
            probe.remove();
            return JSON.stringify({ height: toggle.getBoundingClientRect().height, limit: target * 1.2 });
        }");
        var report = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(toggleReport!);
        Assert.IsFalse(report.TryGetProperty("missing", out _), "the view-mode segment rides the merged toolbar");
        var toggleHeight = report.GetProperty("height").GetSingle();
        var toggleLimit = report.GetProperty("limit").GetSingle();
        Assert.IsTrue(toggleHeight <= toggleLimit,
            $"the view-mode toggle is a single row ({toggleHeight}px ≤ {toggleLimit}px) — it never wraps");

        // The click anchors on the SLOT's page — page 4, not the annotator's current page.
        await GoToPageAsync(page, section, 4);
        await PageSurface(section, 4).ClickAsync(new LocatorClickOptions { Position = new Position { X = 120, Y = 60 } });

        var stamp = PageSlot(section, 4).Locator("[data-testid='pdf-annotator-stamp']").First;
        await stamp.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "continuous-stamp-page4");

        // Reload: the localStorage provider keeps the section's document — the stamp is
        // still anchored on page 4 (the per-page slot mounts it after the geometry report).
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var reloaded = await OpenContinuousSectionAsync(page);
        await GoToPageAsync(page, reloaded, 4);
        await PageSlot(reloaded, 4).Locator("[data-testid='pdf-annotator-stamp']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 60000 });
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Continuous_DrawStrokeAcrossPageBoundary_Edge()
    {
        var page = await OpenAsync(1600, 1000, touch: false);
        var section = await OpenContinuousSectionAsync(page);

        await ClickToolAsync(section, "pdf-annotator-mode-draw");

        // The page 3/4 gap sits MID-VIEWPORT: scrollTop = slot3.offsetTop + slot3.offsetHeight
        // - wrap.clientHeight/2 puts the slot-3 bottom edge at half the wrap's client height,
        // so the drag starts inside page 3, crosses the gap and ends 40px INSIDE page 4. The
        // stroke is clipped at the boundary — every saved point stays normalized to page 3
        // (Y ≤ 1) and nothing lands on page 4.
        await GoToPageAsync(page, section, 3);
        await section.EvaluateAsync(@"el => {
            const wrap = el.querySelector('.tm-pdf-viewer__continuous-wrap');
            const slot3 = wrap.querySelector('.tm-pdf-viewer__page-overlay-slot[data-page=""3""]');
            const slotTop = slot3.getBoundingClientRect().top - wrap.getBoundingClientRect().top + wrap.scrollTop;
            wrap.scrollTop = slotTop + slot3.offsetHeight - wrap.clientHeight / 2;
        }");
        await PageSurface(section, 4)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 30000, State = WaitForSelectorState.Attached });
        await page.WaitForTimeoutAsync(300);

        var slot3Box = await PageSlot(section, 3).BoundingBoxAsync()
            ?? throw new InvalidOperationException("The page-3 slot has no bounding box.");
        var startX = slot3Box.X + slot3Box.Width * 0.4;
        var startY = slot3Box.Y + slot3Box.Height - 30;
        var endY = slot3Box.Y + slot3Box.Height + 40;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(startX + 40), (float)(startY + 10), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(startX + 60), (float)endY, new MouseMoveOptions { Steps = 8 });
        await page.Mouse.UpAsync();

        var composer = section.Locator("[data-testid='pdf-annotator-new']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await section.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        // The persisted stroke: every point stays inside page 3 (Y ≤ 1) and the anchor is
        // page 3 — the crossing contact never re-anchored the draft onto page 4.
        await page.WaitForFunctionAsync(@"() => {
            const threads = JSON.parse(localStorage.getItem('tm-demo-pdf-annotations:demo-annotator-continuous') ?? '[]');
            const strokes = threads.filter(t => t.kind === 3 && t.anchor?.pageNumber === 3)
                .flatMap(t => (t.inkStrokes ?? []).map(s => s.points ?? []));
            return strokes.length > 0 && strokes.every(pts => pts.every(p => p.y <= 1));
        }", null, new PageWaitForFunctionOptions { Timeout = 15000 });
        await PageSlot(section, 3).Locator("[data-testid='pdf-annotator-ink']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
        Assert.AreEqual(0, await PageSlot(section, 4).Locator("[data-testid='pdf-annotator-ink']").CountAsync(),
            "the stroke is clipped at the page boundary — nothing lands on page 4");
        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "continuous-stroke-boundary-edge");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Continuous_TwoUsersSharedThread()
    {
        // The two-user flow rides the section's localStorage provider: the first "session"
        // creates a comment on page 2, the reload is the second session — the thread is
        // there and a reply lands in the same thread, all inside Continuous.
        var page = await OpenAsync(1600, 1000, touch: false);
        var section = await OpenContinuousSectionAsync(page);

        await ClickToolAsync(section, "pdf-annotator-mode-comment");
        await GoToPageAsync(page, section, 2);
        await PageSurface(section, 2).ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 90 } });

        var composer = section.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Shared across sessions");
        await section.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        await PageSlot(section, 2).Locator("[data-testid='pdf-annotator-marker']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        var second = await OpenContinuousSectionAsync(page);

        // The second session opens the thread from its page-2 marker and replies — the mode
        // stays Continuous (the anchor scroll, not a mode switch).
        await GoToPageAsync(page, second, 2);
        var marker = PageSlot(second, 2).Locator("[data-testid='pdf-annotator-marker']").First;
        await marker.ClickAsync();
        var reply = second.Locator("[data-testid='pdf-annotator-reply-input']").First;
        await reply.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await reply.FillAsync("Second user reply");
        await second.Locator("[data-testid='pdf-annotator-reply-submit']").First.ClickAsync();

        await second.Locator("[data-testid='pdf-annotator-comment']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsTrue(await second.Locator("[data-testid='pdf-annotator-comment']").CountAsync() >= 2,
            "the reply landed in the shared thread");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Continuous_TabletPenDraw()
    {
        // The tablet layout (coarse pointer) offers the full tool set; the pen's priority is
        // covered by the bUnit suite — here the stroke on the per-page surface is the
        // evidence (a stylus dispatches the same pointer events as the emulated pointer).
        var page = await OpenAsync(1180, 820, touch: true);
        var section = await OpenContinuousSectionAsync(page);

        await ClickToolAsync(section, "pdf-annotator-mode-draw");
        await GoToPageAsync(page, section, 2);
        var (vx, vy, vw, vh) = await VisibleSlotBoxAsync(section, 2);
        await page.Mouse.MoveAsync((float)(vx + vw * 0.3), (float)(vy + vh * 0.35));
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(vx + vw * 0.6), (float)(vy + vh * 0.55), new MouseMoveOptions { Steps = 10 });
        await page.Mouse.UpAsync();

        var composer = section.Locator("[data-testid='pdf-annotator-new']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await section.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        await PageSlot(section, 2).Locator("[data-testid='pdf-annotator-ink']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });
        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "continuous-draw-tablet");
    }

    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Continuous_MobileComment()
    {
        // G2 in Continuous: exactly Browse + Add comment tiles; a comment drop opens the
        // bottom sheet composer and the marker lands on the tapped page's slot.
        var page = await OpenAsync(390, 844, touch: true);
        var section = await OpenContinuousSectionAsync(page);

        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-annotator-mode-highlight']").CountAsync(),
            "G2: the editing tools do not exist on a phone, in Continuous too");
        Assert.AreEqual(0, await section.Locator("[data-testid='pdf-annotator-mode-draw']").CountAsync(),
            "G2: draw is a tablet+ tool");

        await section.Locator("[data-testid='pdf-annotator-mode-comment']").First.TapAsync();
        await GoToPageAsync(page, section, 3);
        // Tap inside the slot∩wrap intersection: the tap position is relative to the
        // surface's own top-left, which may sit above the wrap's visible clip.
        var (vx, vy, vw, vh) = await VisibleSlotBoxAsync(section, 3);
        var surfaceBox = await PageSurface(section, 3).BoundingBoxAsync()
            ?? throw new InvalidOperationException("The page-3 surface has no bounding box.");
        var tapX = (int)((vx + vw * 0.3) - surfaceBox.X);
        var tapY = (int)((vy + vh * 0.4) - surfaceBox.Y);
        await PageSurface(section, 3).TapAsync(new LocatorTapOptions { Position = new Position { X = tapX, Y = tapY } });

        var composer = section.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Mobile comment on page 3");
        await section.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        await PageSlot(section, 3).Locator("[data-testid='pdf-annotator-marker']")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await ParkAndShootAsync(page, section, ShotDir("pdf-annotator"), "continuous-mobile-comment");
    }
}
