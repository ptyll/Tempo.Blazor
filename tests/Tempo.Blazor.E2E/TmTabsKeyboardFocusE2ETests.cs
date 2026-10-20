using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The roving tabindex of <c>TmTabs</c>, measured where it actually lives: <c>document.activeElement</c>.
/// <para>
/// WHY THIS FILE EXISTS. TmTabs used to move only the SELECTION on Arrow keys — <c>aria-selected</c>
/// and the <c>tabindex</c> 0/-1 pair — and never moved DOM focus, so after ArrowRight the focus was
/// still sitting on the tab the user had just left, on an element that now carried
/// <c>tabindex="-1"</c>. Every test one could write over the rendered DOM was green throughout: the
/// selection really did move. A consumer downstream had to rewrite an entire assertion after
/// discovering that its "arrow keys must not reach the chevron" check would have passed on a
/// DELETED keyboard handler, because nothing it measured was focus.
/// </para>
/// <para>
/// So the assertions below are split deliberately: <see cref="AssertSelectionAsync"/> is the floor —
/// it fails on a page with nothing to walk — and <see cref="AssertFocusAsync"/> is the discriminator.
/// A regression that reinstates "selection without focus" leaves every selection assertion green and
/// turns only the focus ones red, which is exactly the failure this file is here to name.
/// </para>
/// <para>
/// Keys are sent with <see cref="IKeyboard"/>, i.e. to whatever currently holds focus, never to a
/// located element. That is load-bearing: a test that dispatched each key AT the tablist would keep
/// working on an implementation that never moved focus, because it would be supplying by hand the
/// very thing under test.
/// </para>
/// <para>
/// It is also where the two DELIBERATE departures from the APG pattern are bounded rather than
/// merely asserted in a comment: the veto arm pins that focus follows the CONFIRMED selection (so a
/// consumer that refuses the change keeps focus and selection together), and the scroll arm pins
/// that the un-suppressed default scroll of Home/End still leaves the focused tab in the viewport.
/// The third departure, RTL mirroring, is not measured here because the component layer has nothing
/// to set a direction with — see <c>TmTabs.HandleKeyDown</c>.
/// </para>
/// <para>
/// Screenshot-free by construction — nothing here reads or writes a <c>.png</c>.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class TmTabsKeyboardFocusE2ETests : WasmTestBase
{
    private const string LineTabs = "[data-testid='tabs-line-keyboard']";

    /// <summary>A strip whose consumer rejects every change — see the veto test below.</summary>
    private const string VetoTabs = "[data-testid='tabs-veto']";

    private const string VetoFirst = "tab-veto-first";
    private const string VetoSecond = "tab-veto-second";
    private const string VetoThird = "tab-veto-third";

    /// <summary>Tab ids of the demo strip, in document order. The fourth one is disabled.</summary>
    private const string First = "tab-overview";
    private const string Second = "tab-details";
    private const string Last = "tab-settings";
    private const string Disabled = "tab-disabled-tab";

    /// <summary>
    /// Focus lands one render AFTER the selection does (TmTabs defers it to <c>OnAfterRenderAsync</c>
    /// so the element handle belongs to the current render), so the two are awaited separately
    /// rather than assumed simultaneous.
    /// </summary>
    private const int FocusSettleMs = 3000;

    [TestMethod]
    public async Task ArrowRight_MovesTheDomFocus_NotJustTheSelection()
    {
        var page = await OpenStripAsync();

        await FocusTheActiveTabAsync(page);

        await page.Keyboard.PressAsync("ArrowRight");

        await AssertSelectionAsync(page, Second, "ArrowRight selects the next tab");
        await AssertFocusAsync(page, Second, "ArrowRight");
    }

    [TestMethod]
    public async Task ArrowLeft_WrapsBackwards_AndCarriesTheFocusWithIt()
    {
        var page = await OpenStripAsync();
        await FocusTheActiveTabAsync(page);

        // From the FIRST tab, so this also pins the wrap — and it wraps onto the last SELECTABLE tab,
        // stepping over the disabled one, which is asserted to exist in OpenStripAsync.
        await page.Keyboard.PressAsync("ArrowLeft");

        await AssertSelectionAsync(page, Last, "ArrowLeft from the first tab wraps to the last selectable one");
        await AssertFocusAsync(page, Last, "ArrowLeft");
    }

    [TestMethod]
    public async Task EndThenHome_AreHandled_AndBothMoveTheDomFocus()
    {
        var page = await OpenStripAsync();
        await FocusTheActiveTabAsync(page);

        await page.Keyboard.PressAsync("End");

        await AssertSelectionAsync(page, Last, "End jumps to the last selectable tab");
        await AssertFocusAsync(page, Last, "End");

        await page.Keyboard.PressAsync("Home");

        await AssertSelectionAsync(page, First, "Home jumps back to the first tab");
        await AssertFocusAsync(page, First, "Home");
    }

    /// <summary>
    /// Chained arrows, sent to nothing but the keyboard. On an implementation that moves selection
    /// only, the SECOND press is dispatched from the tab the first one left behind and the strip
    /// never gets past the second tab — so this arrives somewhere else entirely, which is the
    /// clearest statement of what "focus did not follow" costs a user.
    /// </summary>
    [TestMethod]
    public async Task TwoArrowsInARow_WalkTwoTabs_BecauseTheFocusTravelsWithTheSelection()
    {
        var page = await OpenStripAsync();
        await FocusTheActiveTabAsync(page);

        await page.Keyboard.PressAsync("ArrowRight");
        await AssertFocusAsync(page, Second, "the first ArrowRight");

        await page.Keyboard.PressAsync("ArrowRight");

        await AssertSelectionAsync(page, Last, "two ArrowRights walk two tabs");
        await AssertFocusAsync(page, Last, "the second ArrowRight");
    }

    /// <summary>
    /// The roving tabindex itself: exactly one tab may be in the sequential tab order, and it must be
    /// the focused one. Without this, "focus moved" and "tabindex moved" could drift apart and the
    /// strip would still look correct in both of the other files that measure them separately.
    /// </summary>
    [TestMethod]
    public async Task AfterAnArrowKey_ExactlyTheFocusedTabIsInTheTabOrder()
    {
        var page = await OpenStripAsync();
        await FocusTheActiveTabAsync(page);

        await page.Keyboard.PressAsync("ArrowRight");
        await AssertFocusAsync(page, Second, "ArrowRight");

        var reachable = await page.EvalOnSelectorAllAsync<string[]>(
            $"{LineTabs} [role='tab']",
            "els => els.filter(e => e.getAttribute('tabindex') === '0').map(e => e.id)");

        CollectionAssert.AreEqual(
            new[] { Second },
            reachable,
            "exactly the focused tab may carry tabindex=\"0\"; measured [" + string.Join(", ", reachable) + "]");
    }

    /// <summary>
    /// The MIRROR of the defect this file was written for, and it arrived with the fix rather than
    /// before it. <c>TmTabs</c> is a controlled component: the arrow key only ASKS, and a consumer
    /// that ignores <c>ActiveTabIdChanged</c> keeps the old selection. An implementation that
    /// focused the tab the key REQUESTED would then park the focus ring on a tab that is not
    /// <c>aria-selected</c> and carries <c>tabindex="-1"</c> — the same focus/selection divergence
    /// as before, only from the other side. Focus is resolved against <c>ActiveTabId</c> after the
    /// render precisely so that a veto degrades to re-focusing the tab that is still selected.
    /// <para>
    /// The vacuity risk here is the whole difficulty: "nothing moved" is also what a dead page
    /// looks like. So the accepting strip is driven FIRST with the same key from the same keyboard,
    /// and its move is asserted — that is the liveness floor, and it fails on an app that never
    /// went interactive, leaving "the veto strip did not move" as a statement about the veto.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AVetoedChange_LeavesTheFocusOnTheStillSelectedTab()
    {
        var page = await OpenStripAsync();

        // ── Liveness floor: the very same key visibly moves an ACCEPTING strip ──
        await FocusTheActiveTabAsync(page);
        await page.Keyboard.PressAsync("ArrowRight");
        await AssertSelectionAsync(page, Second, "the accepting strip proves the key is delivered");
        await AssertFocusAsync(page, Second, "ArrowRight on the accepting strip");

        // ── The veto strip ──
        var order = await page.EvalOnSelectorAllAsync<string[]>(
            $"{VetoTabs} [role='tab']",
            "els => els.map(e => e.id)");
        CollectionAssert.AreEqual(
            new[] { VetoFirst, VetoSecond, VetoThird },
            order,
            "the veto strip must offer somewhere to go, otherwise 'the selection did not move' is "
            + "true for the wrong reason; measured [" + string.Join(", ", order) + "]");

        await page.FocusAsync($"{VetoTabs} #{VetoFirst}");
        Assert.AreEqual(
            VetoFirst,
            await ActiveElementIdAsync(page),
            "precondition: the veto arm starts with the focus on the selected tab");

        await page.Keyboard.PressAsync("ArrowRight");

        // Give the strip at least as long to move as the accepting one was given, so "it stayed"
        // is a settled measurement rather than a race the assertion happened to win.
        await page.WaitForTimeoutAsync(FocusSettleMs);

        var selected = await SelectedTabIdAsync(page, VetoTabs);
        var focused = await ActiveElementIdAsync(page);
        var rovingZero = await page.EvalOnSelectorAllAsync<string[]>(
            $"{VetoTabs} [role='tab']",
            "els => els.filter(e => e.getAttribute('tabindex') === '0').map(e => e.id)");

        TestContext.WriteLine(
            $"veto strip after ArrowRight: selected=#{selected} activeElement=#{focused} "
            + $"tabindex0=[{string.Join(", ", rovingZero)}]");

        Assert.AreEqual(
            VetoFirst,
            selected,
            $"the veto strip's handler rejects every change, so the selection must stay on "
            + $"'{VetoFirst}'; measured '{selected}'. If this moved, the demo strip is no longer a "
            + "veto and the focus assertion below says nothing");

        Assert.AreEqual(
            VetoFirst,
            focused,
            $"after a REJECTED change the DOM focus must stay on the still-selected tab "
            + $"'{VetoFirst}' — measured '{focused}', while the selection is '{selected}'. Focusing "
            + "the tab the key merely ASKED for re-creates the focus/selection divergence this "
            + "whole change removes, only mirrored: the focus ring would sit on a tab that is not "
            + "aria-selected and carries tabindex=\"-1\"");

        CollectionAssert.AreEqual(
            new[] { VetoFirst },
            rovingZero,
            "and the focused tab must be the one in the sequential tab order; measured ["
            + string.Join(", ", rovingZero) + "]");
    }

    /// <summary>
    /// Pins the SECOND documented departure from the APG pattern, so it is a measured bound rather
    /// than a claim in a comment: <c>Home</c>/<c>End</c> do not call <c>preventDefault</c>, so the
    /// page scrolls as usual — but the focus call must bring the focused tab back INSIDE the
    /// viewport. That is the difference between transient jank (accepted) and a strip the user has
    /// to hunt for (not accepted), and it is the reason the scroll was left alone rather than
    /// suppressed with a render-time <c>@onkeydown:preventDefault</c> that would also swallow Tab.
    /// <para>
    /// NOT A FOCUS DISCRIMINATOR, and must not be counted as one: under a mutation that removes the
    /// focus move entirely this is the only method in the file that stays GREEN, because the
    /// viewport rectangle it asserts is reached by the browser's own scroll-into-view. Its
    /// <c>AssertSelectionAsync</c> calls do make it red when Home/End go unhandled, so it discriminates
    /// on the KEY-HANDLING axis and on the scroll bound — never on focus.
    /// </para>
    /// <para>
    /// The two scroll positions on the way (<c>before</c> and <c>peak</c>) vary between runs and are
    /// logged, never asserted; only the settled position and the focused tab's rectangle reproduce.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task HomeScrollsThePage_ButLeavesTheFocusedTabInsideTheViewport()
    {
        var page = await OpenStripAsync();
        await FocusTheActiveTabAsync(page);

        var before = await ScrollYAsync(page);

        await page.Keyboard.PressAsync("End");
        await AssertSelectionAsync(page, Last, "End jumps to the last selectable tab");
        var peak = await ScrollYAsync(page);

        await page.Keyboard.PressAsync("Home");
        await AssertSelectionAsync(page, First, "Home jumps back to the first tab");
        await AssertFocusAsync(page, First, "Home");

        // The demo host styles <html> with `scroll-behavior: smooth` (docs/e2e-test-lanes.md,
        // "Timing-masked races"), so the Home/End default scrolls AND the focus call's
        // scroll-into-view are ANIMATIONS. scrollY and the tab's rect read a frame later are
        // mid-flight samples (observed red: settled read at scrollY 647 with the focused tab
        // 1895 px below the viewport top, still flying). The invariant this method pins is about
        // the SETTLED page — the wait's frame signature includes window.scrollY, so an in-flight
        // smooth scroll can never settle it early.
        await WaitForLayoutToSettleAsync(page, $"{LineTabs} #{First}");

        var settled = await ScrollYAsync(page);
        var box = await BoxAsync(page.Locator($"{LineTabs} #{First}"));
        var viewport = page.ViewportSize!;

        TestContext.WriteLine(
            $"scrollY {before} -> peak {peak} -> settled {settled}; focused tab rect "
            + $"top={box.Y} bottom={box.Y + box.Height} viewportHeight={viewport.Height}");

        Assert.IsTrue(
            box.Y >= 0 && box.Y + box.Height <= viewport.Height,
            $"after Home the focused tab must be inside the viewport (measured top={box.Y}, "
            + $"bottom={box.Y + box.Height}, viewport height={viewport.Height}, scrollY {before} -> "
            + $"{settled}). The default scroll is deliberately NOT suppressed — "
            + "@onkeydown:preventDefault is evaluated at render time and could not be limited to "
            + "Home/End, so it would swallow Tab too — and this assertion is what bounds that "
            + "decision: jank is accepted, losing the strip is not");
    }

    /// <summary>
    /// The same veto, reached through the POINTER — and the arm that stops the invariant from
    /// quietly narrowing to "the focused tab is the selected tab, if you used the keyboard".
    /// <para>
    /// A click is the one path where the browser moves focus before any Blazor code runs, so a
    /// refused click leaves the native focus on the refused tab unless the component pulls it back.
    /// Measured on this very strip before <c>SelectTab</c> claimed a focus move:
    /// <c>activeElement=#tab-veto-second, selected=[tab-veto-first], tabindex0=[tab-veto-first]</c>
    /// — the focus ring on a tab that is neither selected nor in the tab order.
    /// </para>
    /// <para>
    /// Liveness floor, same shape as the keyboard veto arm: the accepting strip is clicked first
    /// and its move asserted, so "the veto strip did not move" cannot be satisfied by a page that
    /// never went interactive.
    /// </para>
    /// </summary>
    [TestMethod]
    public async Task AVetoedCLICK_PullsTheFocusBackToTheStillSelectedTab()
    {
        var page = await OpenStripAsync();

        // ── Liveness floor: a click on the ACCEPTING strip moves selection AND focus ──
        await page.ClickAsync($"{LineTabs} #{Second}");
        await AssertSelectionAsync(page, Second, "clicking an accepting strip selects the clicked tab");
        await AssertFocusAsync(page, Second, "a click on the accepting strip");

        // ── The refused click ──
        await page.ClickAsync($"{VetoTabs} #{VetoSecond}");
        await page.WaitForTimeoutAsync(FocusSettleMs);

        var selected = await SelectedTabIdAsync(page, VetoTabs);
        var focused = await ActiveElementIdAsync(page);
        var rovingZero = await page.EvalOnSelectorAllAsync<string[]>(
            $"{VetoTabs} [role='tab']",
            "els => els.filter(e => e.getAttribute('tabindex') === '0').map(e => e.id)");

        TestContext.WriteLine(
            $"veto strip after CLICK on #{VetoSecond}: selected=#{selected} activeElement=#{focused} "
            + $"tabindex0=[{string.Join(", ", rovingZero)}]");

        Assert.AreEqual(
            VetoFirst,
            selected,
            $"the veto strip rejects every change, so a click must not select '{VetoSecond}'; "
            + $"measured '{selected}'");

        Assert.AreEqual(
            VetoFirst,
            focused,
            $"after a REFUSED CLICK the focus must be pulled back to the still-selected tab "
            + $"'{VetoFirst}' — measured '{focused}'. The browser focuses the clicked button before "
            + "any component code runs, so without a focus claim on the click path the ring is left "
            + "on a tab that is not aria-selected and carries tabindex=\"-1\": the same divergence "
            + "the arrow keys were fixed for, arriving through the pointer");

        CollectionAssert.AreEqual(
            new[] { VetoFirst },
            rovingZero,
            "and the focused tab must be the one in the sequential tab order; measured ["
            + string.Join(", ", rovingZero) + "]");
    }

    // ── Measurement ────────────────────────────────────────

    /// <summary>
    /// The FLOOR. Every focus assertion in this file is preceded by one of these, so a strip that
    /// lost its tabs, or a demo page that stopped rendering it, fails here rather than passing a
    /// focus check vacuously.
    /// </summary>
    private static async Task AssertSelectionAsync(IPage page, string expectedTabId, string because)
    {
        try
        {
            await page.WaitForSelectorAsync(
                $"{LineTabs} #{expectedTabId}[aria-selected='true']",
                new PageWaitForSelectorOptions { Timeout = FocusSettleMs });
        }
        catch (TimeoutException)
        {
            // Deliberately swallowed so the failure below can NAME the tab that was selected
            // instead of the run ending on a bare "Timeout 3000ms exceeded" — a key that is not
            // handled at all fails here, and that red has to say which key and where it landed.
        }

        var selected = await SelectedTabIdAsync(page);
        Assert.AreEqual(
            expectedTabId,
            selected,
            $"{because}: exactly one tab must report aria-selected=true and it must be "
            + $"'{expectedTabId}', measured '{selected}'. A key the strip does not handle leaves the "
            + "selection where it was and fails exactly here");
    }

    /// <summary>
    /// The DISCRIMINATOR, and the whole reason this file is an E2E test and not a bUnit one: bUnit
    /// renders a DOM but owns no focused element, so this quantity does not exist there at all.
    /// </summary>
    private async Task AssertFocusAsync(IPage page, string expectedTabId, string afterKey)
    {
        var observed = await WaitForActiveElementAsync(page, expectedTabId);
        var selected = await SelectedTabIdAsync(page);

        TestContext.WriteLine(
            $"after {afterKey}: activeElement=#{observed} selected=#{selected} (expected #{expectedTabId})");

        Assert.AreEqual(
            expectedTabId,
            observed,
            $"after {afterKey} the DOM focus must sit on '{expectedTabId}' — measured '{observed}', "
            + $"while the selection reached '{selected}'. Selection moving without focus is the "
            + "half-implemented roving tabindex this file exists to catch: the focus ring and the "
            + "screen reader stay on the tab the user left, which now carries tabindex=\"-1\", and "
            + "the next key is dispatched from the wrong element");
    }

    /// <summary>
    /// Polls rather than waits once, and RETURNS what it last saw instead of throwing a Playwright
    /// timeout, so a red run names the element that really held focus.
    /// </summary>
    private static async Task<string> WaitForActiveElementAsync(IPage page, string expectedTabId)
    {
        string observed;
        var deadline = DateTime.UtcNow.AddMilliseconds(FocusSettleMs);

        do
        {
            observed = await ActiveElementIdAsync(page);
            if (observed == expectedTabId) return observed;
            await page.WaitForTimeoutAsync(50);
        }
        while (DateTime.UtcNow < deadline);

        return observed;
    }

    private static async Task<double> ScrollYAsync(IPage page)
        => await page.EvaluateAsync<double>("() => window.scrollY");

    private static async Task<LocatorBoundingBoxResult> BoxAsync(ILocator locator)
    {
        var box = await locator.BoundingBoxAsync();
        Assert.IsNotNull(box, "element has no layout box — it is not rendered");
        return box;
    }

    private static async Task<string> ActiveElementIdAsync(IPage page)
        => await page.EvaluateAsync<string>(
            "() => document.activeElement ? (document.activeElement.id || document.activeElement.tagName) : '<none>'");

    private static async Task<string> SelectedTabIdAsync(IPage page, string strip = LineTabs)
    {
        var ids = await page.EvalOnSelectorAllAsync<string[]>(
            $"{strip} [role='tab']",
            "els => els.filter(e => e.getAttribute('aria-selected') === 'true').map(e => e.id)");

        return ids.Length == 1 ? ids[0] : $"<{ids.Length} selected: {string.Join(",", ids)}>";
    }

    /// <summary>
    /// Puts the browser focus where a keyboard user would have it before pressing an arrow: on the
    /// tab that is currently selected. It is asserted, not assumed — every later measurement is a
    /// DELTA from this position, so a run that started with focus somewhere else would be measuring
    /// a different question.
    /// </summary>
    private static async Task FocusTheActiveTabAsync(IPage page)
    {
        await page.FocusAsync($"{LineTabs} #{First}");

        var observed = await ActiveElementIdAsync(page);
        Assert.AreEqual(
            First,
            observed,
            $"precondition: the run must start with the focus on the selected tab '{First}', "
            + $"measured '{observed}'");

        var selected = await SelectedTabIdAsync(page);
        Assert.AreEqual(
            First,
            selected,
            $"precondition: the demo strip must start with '{First}' selected, measured '{selected}'");
    }

    private async Task<IPage> OpenStripAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1366, 900);
        await page.GotoAsync($"{BaseUrl}/feedback", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync($"{LineTabs} [role='tab']", new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 60000
        });

        // Population, asserted rather than assumed: three selectable tabs (so ArrowRight twice has
        // somewhere to go and End differs from ArrowRight) plus one disabled tab AFTER the last
        // selectable one (so "End steps over disabled" and "ArrowLeft wraps past it" are real).
        var order = await page.EvalOnSelectorAllAsync<string[]>(
            $"{LineTabs} [role='tab']",
            "els => els.map(e => e.id + (e.getAttribute('aria-disabled') === 'true' ? ':disabled' : ''))");

        CollectionAssert.AreEqual(
            new[] { First, Second, Last, Disabled + ":disabled" },
            order,
            "the demo strip this file measures must be [" + string.Join(", ", new[] { First, Second, Last, Disabled + ":disabled" })
            + "]; measured [" + string.Join(", ", order) + "]. Every expectation below is written "
            + "against that exact shape, and a strip that lost its disabled tail would make the "
            + "End/wrap arms say nothing");

        TestContext.WriteLine(
            "strip: " + string.Join(", ", order)
            + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        return page;
    }
}
