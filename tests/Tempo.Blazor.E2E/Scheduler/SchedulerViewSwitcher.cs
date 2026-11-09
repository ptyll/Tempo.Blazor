using Microsoft.Playwright;

namespace Tempo.Blazor.E2E.Scheduler;

/// <summary>
/// Switches the scheduler's view through whichever presentation its toolbar is currently showing.
/// </summary>
/// <remarks>
/// The full toolbar draws the pill tablist while its OWN container is at least 60rem wide, and swaps
/// it for the path's compact view dropdown below that (a <c>@container tm-toolbar</c> query, so the
/// date title keeps its room). The hidden pills stay in the DOM with <c>display:none</c>, so clicking
/// <c>[data-view]</c> blindly can wait forever on a 0x0 tab. This helper clicks the pill when it is
/// visible and otherwise opens the dropdown and picks the view, which also covers the phone's
/// compact row (a dropdown in every layout).
/// </remarks>
internal static class SchedulerViewSwitcher
{
    /// <summary>
    /// Switches <paramref name="scheduler"/> (the scheduler root, or any element containing exactly
    /// one scheduler) to <paramref name="view"/> — an English view label such as <c>Week</c>.
    /// </summary>
    public static async Task SwitchViewAsync(ILocator scheduler, string view)
    {
        var pill = scheduler.Locator($"[data-tm-group='view'] [role='tab'][data-view='{view}']");
        if (await pill.IsVisibleAsync())
        {
            await pill.ClickAsync();
            return;
        }

        // The narrow bar (and the compact row) renders the same view dropdown: one trigger and one
        // option per view. Both carry stable ids; the pills stay in the DOM but outside this region.
        var menu = scheduler.Locator("[data-testid='scheduler-view-menu']").First;
        var trigger = menu.Locator("button[aria-haspopup='true']").First;
        await trigger.ClickAsync();

        var option = menu.Locator($"[data-view='{view}']").First;
        await option.ClickAsync();
        // A selection closes the menu (on a mobile layout it is a modal sheet, whose closing backdrop
        // would swallow the test's next touch) and the trigger's label then follows the committed view.
        await option.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
        await Assertions.Expect(trigger).ToContainTextAsync(
            view, new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
    }
}
