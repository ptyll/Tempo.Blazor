using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Layout guard for TmTabs' HeaderLeading/HeaderTrailing slots.
/// <para>
/// The DOM contract of the slots is covered by <c>TmTabsHeaderSlotsTests</c> (bUnit). What bUnit
/// cannot reach is LAYOUT: it renders a DOM, never a box tree, so "the slot sits on the first row"
/// and "the header no longer paints an orphan rule" are invisible to it no matter how the assertion
/// is phrased. Both defects this file pins were found by measuring boxes in a real browser, and both
/// were green in bUnit at the time.
/// </para>
/// <para>
/// Deliberately screenshot-free: every assertion reads <see cref="ILocator.BoundingBoxAsync"/> (plus
/// one computed border width), so the nightly baseline lane neither feeds nor gates this file and a
/// stale <c>.png</c> can never make it pass or fail.
/// </para>
/// <para>
/// It is also the pin for <c>--tm-tab-row-height</c> in <c>_tabs.css</c>. That token has to equal the
/// real height of one tab row; it is now DERIVED with <c>calc()</c> from
/// <c>--tm-tab-padding-y</c>, <c>--tm-tab-line-box</c> and <c>--tm-tab-row-chrome</c>, which the
/// variants re-declare. (It was four per-variant literals while the CSS bundler stripped the
/// whitespace around <c>+</c> and shipped an invalid <c>calc(a+b)</c>; that is fixed in
/// <c>Tempo.Blazor.csproj</c> and guarded by <c>CssBundleCalcWhitespaceTests</c>. This file measures
/// the un-minified stylesheet — the demo links the source <c>tempo-blazor.css</c> — so it can say
/// nothing about the bundle either way.)
/// </para>
/// <para>
/// All three variants are measured on both sides of the 640px breakpoint, which is what pins BOTH
/// the arithmetic and the cascade the inputs arrive through: Line 42px above / 38px below, Pill 44px
/// and Enclosed 37px unchanged across it. Nothing here compares against a literal — every expected
/// value is the LIVE centre of the first tab row of the very strip under test, so an input reaching
/// the calc() from the wrong declaration puts the slot off-centre and turns this file red.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTabsHeaderSlotGeometryE2ETests : WasmTestBase
{
    private const string WrapSlotTabs = "[data-testid='tabs-wrap-slots']";
    private const string PillWrapSlotTabs = "[data-testid='tabs-pill-wrap-slots']";
    private const string EnclosedWrapSlotTabs = "[data-testid='tabs-enclosed-wrap-slots']";
    private const string PillSlotTabs = "[data-testid='tabs-pill-slots']";
    private const string PillPlainTabs = "[data-testid='tabs-pill-plain']";

    /// <summary>Layout rounding only; every quantity below is an integer number of CSS pixels.</summary>
    private const float Tolerance = 1.0f;

    /// <summary>
    /// Tighter than <see cref="Tolerance"/>, and deliberately so. Flex centring is exact arithmetic —
    /// every correct delta measured here is exactly 0.00px — while the gap between two candidate
    /// values of <c>--tm-tab-row-height</c> can be as small as 1px (enclosed 37 vs the &lt;640px
    /// media value 38), which a centred slot box halves into a 0.5px displacement. The comparison in
    /// <see cref="AssertClose"/> is inclusive, so a 0.5px band would ADMIT that error: the enclosed
    /// pair measures +0.50 at both widths and the line pair −0.50, i.e. those arms would not
    /// discriminate the token substitution at all. 0.25 is below the smallest error to catch and
    /// still far above the layout noise floor (Chromium LayoutUnit = 1/64px).
    /// </summary>
    private const float AlignmentTolerance = 0.25f;

    [TestMethod]
    [DataRow(430)] // below the 640px breakpoint that shrinks the tab: one row is 38px
    [DataRow(760)] // above it, still inside the narrow layouts the slots ship into: one row is 42px
    public async Task LineWrapWithSlots_PaintsNoRowRule_AndKeepsTheSlotsOnTheFirstRow(int width)
    {
        var page = await OpenFeedbackAsync(width);

        var root = page.Locator(WrapSlotTabs);
        var row = root.Locator(".tm-tabs__header-row");
        var strip = root.Locator(".tm-tabs__header");

        var firstRow = await MeasureFirstRowAsync(root, width);

        // ── (a) the band must not get the header's single bottom border back ──
        // In wrap mode every row already carries its own 2px baseline; adding the row's 1px rule
        // under the WHOLE band leaves it orphaned far below the active indicator (measured before
        // the fix: 41.0px of gap) and stacks baseline + rule under the last row.
        var rowBorder = await row.EvaluateAsync<string>("e => getComputedStyle(e).borderBottomWidth");
        Assert.AreEqual(
            "0px",
            rowBorder,
            $"at {width}px .tm-tabs__header-row must drop its bottom border in wrap mode, exactly as "
            + ".tm-tabs__header does — otherwise the band ends in a rule that belongs to no row");

        var rowBox = await BoxAsync(row);
        var stripBox = await BoxAsync(strip);

        TestContext.WriteLine(
            $"[line @{width}px] rows={firstRow.RowCount} band={stripBox.Height} row={rowBox.Height} "
            + $"rowBorderBottom={rowBorder} firstRow={firstRow.Top}..{firstRow.Bottom} centre={firstRow.CentreY}");

        AssertClose(
            rowBox.Y + rowBox.Height,
            stripBox.Y + stripBox.Height,
            $"at {width}px the row must end where the band ends; any extra height is the orphan rule");

        // ── (b) the slots belong beside the FIRST row, not the middle of the band ──
        await AssertSlotsSitOnTheFirstRowAsync(root, firstRow, $"line @{width}px");
    }

    /// <summary>
    /// The same first-row alignment for the two variants that carry their OWN value of
    /// <c>--tm-tab-row-height</c>. Without this the token was pinned for Line only: the Line case
    /// above measures the media-query value at 430px and the container default at 760px, and neither
    /// touches the pill (44px) or enclosed (37px) declaration. Both are measured on both sides of
    /// the breakpoint, which is also what pins the claim that those two do NOT change across it.
    /// </summary>
    [TestMethod]
    [DataRow(PillWrapSlotTabs, 430)]
    [DataRow(PillWrapSlotTabs, 760)]
    [DataRow(EnclosedWrapSlotTabs, 430)]
    [DataRow(EnclosedWrapSlotTabs, 760)]
    public async Task WrapWithSlots_KeepsTheSlotsOnTheFirstRow_InPillAndEnclosed(string testId, int width)
    {
        var page = await OpenFeedbackAsync(width);

        var root = page.Locator(testId);
        var firstRow = await MeasureFirstRowAsync(root, width);

        await AssertSlotsSitOnTheFirstRowAsync(root, firstRow, $"{testId} @{width}px");
    }

    [TestMethod]
    public async Task PillWithATallSlot_LeavesTheTrayAndThePillsAtTheirOwnHeight()
    {
        var page = await OpenFeedbackAsync(1366);

        // Reference comes off the page itself (the slot-less Pill demo), not from a constant, so a
        // future change to the pill metrics moves both sides together instead of turning this red.
        var referenceTray = await BoxAsync(page.Locator(PillPlainTabs).Locator(".tm-tabs__header"));
        var referencePill = await BoxAsync(page.Locator(PillPlainTabs).Locator(".tm-tab").First);

        // The defect only shows when the slot is TALLER than the tray, so the probe forces it there
        // — the same shape in which the tray was measured growing from 44px to 60px and the pills
        // from 36px to 52px. The height it actually reaches is asserted below rather than assumed.
        await page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = PillSlotTabs + " .tm-tabs__header-trailing > * { min-height: 60px; }"
        });

        var slotContent = await BoxAsync(page.Locator(PillSlotTabs).Locator(".tm-tabs__header-trailing > *").First);
        Assert.IsTrue(
            slotContent.Height > referenceTray.Height + Tolerance,
            $"the probe slot ({slotContent.Height}px) must be taller than the tray "
            + $"({referenceTray.Height}px), otherwise this test cannot tell stretch from centring");

        var tray = await BoxAsync(page.Locator(PillSlotTabs).Locator(".tm-tabs__header"));
        var pill = await BoxAsync(page.Locator(PillSlotTabs).Locator(".tm-tab").First);

        TestContext.WriteLine(
            $"probeSlot={slotContent.Height} tray={tray.Height} (reference {referenceTray.Height}) "
            + $"pill={pill.Height} (reference {referencePill.Height})");

        AssertClose(
            tray.Height,
            referenceTray.Height,
            $"a {slotContent.Height}px slot must not stretch the pill tray: Pill draws no underline "
            + "for the strip to meet, so the row has to centre its children instead of stretching them");

        AssertClose(
            pill.Height,
            referencePill.Height,
            $"a {slotContent.Height}px slot must not stretch the pills inside the tray either");
    }

    // ── Shared measurement ─────────────────────────────────

    /// <summary>The first row of a wrapped band, measured from the real tab boxes.</summary>
    private readonly record struct FirstRowBox(int RowCount, float Top, float Bottom)
    {
        public float CentreY => (Top + Bottom) / 2;
    }

    private async Task<FirstRowBox> MeasureFirstRowAsync(ILocator root, int width)
    {
        var tabs = await BoxesAsync(root.Locator(".tm-tab"));
        var top = tabs.Min(t => t.Y);
        var bottom = tabs.Where(t => Math.Abs(t.Y - top) < Tolerance).Max(t => t.Y + t.Height);

        // Precondition, asserted rather than assumed: without a wrapped band there is nothing to
        // align to, and the alignment assertions would pass vacuously on a single-row strip.
        var rowCount = tabs.Select(t => (int)Math.Round(t.Y)).Distinct().Count();
        Assert.IsTrue(
            rowCount >= 2,
            $"at {width}px the demo strip must wrap onto at least two rows, measured {rowCount} — "
            + "this test says nothing about a single-row strip");

        return new FirstRowBox(rowCount, top, bottom);
    }

    /// <summary>
    /// Both slots must be centred on the FIRST row of the band, which is the whole point of sizing
    /// them with <c>--tm-tab-row-height</c>. Measured on the slot CONTENT, not on the slot box: the
    /// fix works by sizing the box to one row, so asserting on the box would only restate the
    /// mechanism instead of the outcome the token is chosen for.
    /// </summary>
    private async Task AssertSlotsSitOnTheFirstRowAsync(ILocator root, FirstRowBox firstRow, string label)
    {
        foreach (var slot in new[] { "leading", "trailing" })
        {
            var box = await BoxAsync(root.Locator($".tm-tabs__header-{slot}"));
            var content = await BoxAsync(root.Locator($".tm-tabs__header-{slot} > *").First);
            var centreY = content.Y + (content.Height / 2);

            TestContext.WriteLine(
                $"[{label}] {slot}: slotBox={box.Height} content={content.Height} centreY={centreY} "
                + $"firstRow={firstRow.Top}..{firstRow.Bottom} centre={firstRow.CentreY} "
                + $"delta={centreY - firstRow.CentreY} rows={firstRow.RowCount}");

            // Vacuity guard for the token: only a slot box TALLER than its content is sized by
            // `min-height: var(--tm-tab-row-height)`. If the content were the taller of the two, the
            // box would follow the content and the assertion below would say nothing about the token.
            Assert.IsTrue(
                box.Height > content.Height + Tolerance,
                $"[{label}] the {slot} slot box ({box.Height}px) must be taller than its content "
                + $"({content.Height}px), otherwise its height comes from the content and this "
                + "assertion measures nothing about --tm-tab-row-height");

            AssertClose(
                centreY,
                firstRow.CentreY,
                $"[{label}] Header{(slot == "leading" ? "Leading" : "Trailing")} must be centred on "
                + $"the FIRST row of the band (first row {firstRow.Top}..{firstRow.Bottom}); centred "
                + "over the whole band it lands on the first row's underline and reads as belonging "
                + "to no row at all",
                AlignmentTolerance);

            // On the CONTENT again, not the box: Pill's slot box is deliberately taller than the
            // pill row (it matches the padded tray), so a box-level assertion would be false there
            // for a correct layout.
            Assert.IsTrue(
                content.Y + content.Height <= firstRow.Bottom + Tolerance,
                $"[{label}] the {slot} slot content must stay above the first row's baseline: content "
                + $"bottom {content.Y + content.Height}, first row bottom {firstRow.Bottom}");
        }
    }

    private async Task<IPage> OpenFeedbackAsync(int width)
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync($"{WrapSlotTabs} .tm-tabs__header-row", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });
        await WaitForLayoutToSettleAsync(page);
        return page;
    }

    /// <summary>
    /// The page is still MOVING when the app reports ready: the demo's Tailwind Play CDN generates the CSS for
    /// freshly rendered classes after the interactive render, and elements carrying <c>transition-all</c>
    /// then animate from their unstyled box to the styled one (measured: ~40 running CSS transitions, among
    /// them <c>padding-bottom</c>, right after <c>data-blazor-ready</c>, pushing the tab strips down by 176px
    /// on an ease-out curve over ~250ms). Every assertion here compares boxes read by SEPARATE
    /// <see cref="ILocator.BoundingBoxAsync"/> calls, so reading them during that window mixes positions
    /// from different frames (a two-row strip then "has" four rows, and the slot is off by however far the
    /// band moved in between). Wait for the state the assertions assume: fonts loaded, no CSS transition
    /// running, and the measured strips' geometry unchanged across consecutive frames.
    /// </summary>
    private static async Task WaitForLayoutToSettleAsync(IPage page)
    {
        var settled = await page.EvaluateAsync<bool>(
            """
            async () => {
                const frame = () => new Promise(resolve => requestAnimationFrame(() => resolve()));
                const signature = () => [...document.querySelectorAll('.tm-tabs')]
                    .flatMap(tabs => [tabs, ...tabs.querySelectorAll('.tm-tab, .tm-tabs__header-leading > *, .tm-tabs__header-trailing > *')])
                    .map(node => {
                        const r = node.getBoundingClientRect();
                        return `${r.left},${r.top + window.scrollY},${r.width},${r.height}`;
                    })
                    .join('|');
                const transitionRunning = () => document.getAnimations()
                    .some(animation => animation instanceof CSSTransition && animation.playState === 'running');
                const deadline = performance.now() + 15000;
                let previous = null;
                let stableFrames = 0;
                while (performance.now() < deadline) {
                    await document.fonts.ready;
                    await frame();
                    const current = signature();
                    const quiet = document.fonts.status === 'loaded' && !transitionRunning();
                    stableFrames = quiet && current === previous ? stableFrames + 1 : 0;
                    if (stableFrames >= 3) {
                        return true;
                    }

                    previous = current;
                }

                return false;
            }
            """);
        Assert.IsTrue(settled, "the feedback page never settled (fonts loading, a CSS transition running, or the tab strips still moving after 15s) — the geometry below would be read mid-reflow");
    }

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, "element has no layout box — it is not rendered");
        return box;
    }

    private static async Task<List<LocatorBoundingBoxResult>> BoxesAsync(ILocator locator)
    {
        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var element in await locator.AllAsync())
        {
            boxes.Add(await BoxAsync(element));
        }

        return boxes;
    }

    private static void AssertClose(float actual, float expected, string because, float tolerance = Tolerance)
        => Assert.IsTrue(
            Math.Abs(actual - expected) <= tolerance,
            $"{because}. Measured {actual}px, expected {expected}px (tolerance {tolerance}px).");
}
