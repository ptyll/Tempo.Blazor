using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Chrome-aware opening helpers shared by the spreadsheet specs.
/// <para>
/// Since the redesign's phase 6 the spreadsheet chrome follows the width of its own CONTAINER:
/// <c>desktop</c> renders the tabbed ribbon, while <c>tablet</c> renders two compact rows and
/// <c>mobile</c> a single compact row plus the <c>TmMobileActionBar</c>. The compact rows have NO tab
/// strip, so the pre-phase-6 <c>ClickTabButton</c> helper (tab → <c>button[title]</c>) times out there
/// even though the command is reachable. Every command carries the frozen <c>data-command-id</c> and
/// every group its <c>data-tm-group</c>; a command that does not fit a row (or is
/// <c>OverflowOnly</c>) lives in that row's "More" menu.
/// </para>
/// <para>
/// MOBILE is G2 — viewing plus basic editing — so the advanced commands (Data validation, Name
/// Manager, custom Sort, Text to columns, Remove duplicates, Paste special) deliberately have NO
/// phone entry at all. A spec that needs one of those to produce a PHONE presentation therefore opens
/// it on the desktop chrome and narrows the viewport afterwards (the phase-7c "rotated phone"
/// pattern), exactly like the 320 px dialog pass does.
/// </para>
/// </summary>
public partial class SpreadsheetE2ETests
{
    /// <summary>The chrome the spreadsheet's own container resolved (<c>desktop|tablet|mobile</c>).</summary>
    private static async Task<string> ResolvedChromeAsync(IPage page)
        => await DemoComponent(page).Locator(".tm-spreadsheet").First.GetAttributeAsync("data-layout") ?? "desktop";

    /// <summary>
    /// Opens one toolbar command in whatever chrome is live: the desktop ribbon selects its TAB first,
    /// the compact chrome clicks the command on a row or reaches it through that row's "More" menu.
    /// Returns <c>false</c> when the compact chrome offers no such command at all (G2 keeps the
    /// advanced commands desktop-only on a phone).
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="tab">The desktop ribbon tab that owns the command ("Data", "Insert", …).</param>
    /// <param name="commandId">The frozen <c>data-command-id</c> of the command.</param>
    /// <param name="title">The command's localized label (the desktop button's <c>title</c>, and the text of the "More" entry).</param>
    internal static async Task<bool> TryOpenToolbarCommandAsync(IPage page, string tab, string commandId, string title)
    {
        var layout = await ResolvedChromeAsync(page);
        var grid = DemoGrid(page);
        await grid.FocusAsync();

        if (layout == "desktop")
        {
            await DemoComponent(page).Locator(".tm-spreadsheet-toolbar__tab", new() { HasTextString = tab })
                .First.ClickAsync();
            await DemoComponent(page).Locator($"button[title='{title}']").First.ClickAsync();
            await page.WaitForTimeoutAsync(250);
            return true;
        }

        var toolbar = DemoComponent(page).Locator(".tm-spreadsheet-toolbar");

        // One tap: the command is on a row and visible. A real click (not a synthetic dispatch) so the
        // pointer/focus path is the one a user takes.
        var onBar = toolbar.Locator($"[data-command-id='{commandId}']");
        for (var i = 0; i < await onBar.CountAsync(); i++)
        {
            if (await onBar.Nth(i).IsVisibleAsync())
            {
                await onBar.Nth(i).ClickAsync();
                await page.WaitForTimeoutAsync(250);
                return true;
            }
        }

        // Two taps: the row's "More". One row open at a time; the panel is mounted only while open.
        var more = toolbar.Locator(".tm-toolbar-more");
        for (var i = 0; i < await more.CountAsync(); i++)
        {
            var trigger = more.Nth(i);
            if (!await trigger.IsVisibleAsync())
            {
                continue;
            }

            await ClickMoreTriggerAsync(page, trigger);
            var menu = page.Locator(".tm-toolbar-more__menu").First;
            await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60_000 });

            var entry = await FindMenuEntryAsync(menu, commandId, title);
            if (entry is not null)
            {
                await entry.ClickAsync();
                await page.WaitForTimeoutAsync(300);
                return true;
            }

            await page.Keyboard.PressAsync("Escape");
            await menu.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached, Timeout = 30_000 });
        }

        return false;
    }

    /// <summary>
    /// Clicks one row's "More" trigger. A real click first — the menu is a normal button with a pointer
    /// path worth exercising. The synthetic dispatch is the documented FALLBACK for the one case where
    /// Playwright's actionability check loops: on a phone the sticky <c>TmMobileActionBar</c> repositions
    /// while Playwright scrolls the trigger into view, so a real click can wait forever on moving
    /// coordinates (phase-6 learning). The fallback is scoped to exactly that situation.
    /// </summary>
    private static async Task ClickMoreTriggerAsync(IPage page, ILocator trigger)
    {
        try
        {
            await trigger.ClickAsync(new LocatorClickOptions { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            await trigger.DispatchEventAsync("click");
        }

        await page.WaitForTimeoutAsync(100);
    }

    /// <summary>
    /// The "More" entry of one command. The stable identity is the frozen <c>data-command-id</c>, and the
    /// core overflow menu renders <c>data-action-id</c> (a per-instance id, not the command id), so the
    /// entry is matched BY EXACT LABEL when the id is not carried over — never by substring: "Sort
    /// ascending" must not match "Sort ascending (A→Z)" or a "Custom sort…" entry.
    /// </summary>
    private static async Task<ILocator?> FindMenuEntryAsync(ILocator menu, string commandId, string title)
    {
        var byId = menu.Locator($"[data-command-id='{commandId}']");
        if (await byId.CountAsync() > 0)
        {
            return byId.First;
        }

        var byLabel = menu.Locator("button").Filter(new() { HasTextRegex = ExactText(title) });
        return await byLabel.CountAsync() > 0 ? byLabel.First : null;
    }

    /// <summary>An anchored, whitespace-tolerant match of exactly <paramref name="text"/>.</summary>
    private static Regex ExactText(string text) => new($"^\\s*{Regex.Escape(text)}\\s*$");

    /// <summary>The strict form of <see cref="TryOpenToolbarCommandAsync"/>: fails when the command is unreachable.</summary>
    internal static async Task OpenToolbarCommandAsync(IPage page, string tab, string commandId, string title)
    {
        if (!await TryOpenToolbarCommandAsync(page, tab, commandId, title))
        {
            Assert.Fail($"the '{commandId}' command ('{title}') is unreachable in the "
                + $"{await ResolvedChromeAsync(page)} chrome — a phone has no entry for the advanced commands (G2)");
        }
    }

    /// <summary>
    /// Opens one phase-3 dialog on the DESKTOP chrome and then narrows the viewport to the target size.
    /// The core dialog frame resolves its presentation from the app-level viewport scope (a sheet / a
    /// fullscreen panel on a phone), so the assertions after this call still see the MOBILE surface —
    /// while the command itself stays reachable through the ribbon tab that only the desktop chrome has.
    /// </summary>
    private async Task<IPage> OpenPhase3DialogThenResizeAsync(
        Phase3Dialog dialog, int width, int height, bool touch)
    {
        var page = await OpenPhase2DemoAsync(1440, 900, touch);
        await OpenPhase3GridAsync(page);
        if (dialog.Name == "name-manager")
        {
            await SeedNamedRangeAsync(page);
        }

        await dialog.Open(page);
        var root = page.Locator($".tm-modal-overlay.{dialog.RootClass}");
        await root.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });

        if (width != 1440 || height != 900)
        {
            await page.SetViewportSizeAsync(width, height);
            // The frame re-resolves the presentation on the resize; a sheet also slides in (0.3s), and
            // measuring a footer mid-transition reads it ~20px below its resting place.
            await page.WaitForTimeoutAsync(600);
        }

        return page;
    }

    /// <summary>
    /// Opens the demo on the DESKTOP chrome, runs <paramref name="open"/> (a dialog, a validation rule,
    /// a selection …), then narrows the viewport to the target size. The mobile/tablet presentation the
    /// caller asserts afterwards follows the app-level viewport scope, so it is unchanged by the order
    /// in which the command was reached.
    /// </summary>
    private async Task<IPage> OpenOnDesktopThenResizeAsync(
        Func<IPage, Task> open, int width, int height, bool touch)
    {
        var page = await OpenPhase2DemoAsync(1440, 900, touch);
        await OpenPhase3GridAsync(page);
        await open(page);

        if (width != 1440 || height != 900)
        {
            await page.SetViewportSizeAsync(width, height);
            await page.WaitForTimeoutAsync(600);
        }

        return page;
    }

    /// <summary>
    /// Resizes an already-open page to the target viewport and waits for the dialog frame to report the
    /// new layout — the second half of <see cref="OpenPhase3DialogThenResizeAsync"/>, for specs that set
    /// something up first (a validation rule, a selection) through the desktop chrome.
    /// </summary>
    private static async Task ResizeToAsync(IPage page, int width, int height, string rootClass, string expectedLayout)
    {
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForFunctionAsync(
            "([sel, want]) => document.querySelector(sel)?.getAttribute('data-layout') === want",
            new object[] { $".tm-modal-overlay.{rootClass}", expectedLayout },
            new PageWaitForFunctionOptions { Timeout = 30_000 });
        await page.WaitForTimeoutAsync(500);
    }
}
