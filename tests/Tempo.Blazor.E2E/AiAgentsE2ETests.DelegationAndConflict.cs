using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan d67b33af phases 7 and 8 — the delegation surfaces of the <c>/ai-agents</c> page: the board that hands work to
/// agents and the week of scheduled runs (phase 7) and the branch conflict radar with the presence overlay over the
/// page's own document (phase 8).
/// <para>
/// The specs live in their own partial file so the main scenario file stays inside the repository's 1200-line source
/// ceiling (N270/F13) — the class, its base and every test name are unchanged.
/// </para>
/// </summary>
public partial class AiAgentsE2ETests
{
    [TestMethod]
    public async Task TheDelegateBoardAssignsAndRefuses_AndThePageOwnsTheTasks()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, DelegateSection);

        var board = page.Locator("[data-testid='ai-agents-delegate']");
        Assert.AreEqual(4, await board.Locator(".tm-ai-delegate__column").CountAsync());
        Assert.AreEqual("Nothing received yet.", await TextAsync(page, "ai-agents-delegate-log"));

        const string card = "[data-testid='delegate-task'][data-task-id='task-summary']";
        await board.Locator(card).Locator(".tm-context-menu__trigger").ClickAsync();
        await page.Locator("[data-testid='ai-agents-delegate'] .tm-context-menu__item").First.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-delegate'] [data-column='planner'] "
            + "[data-task-id='task-summary']\") !== null",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(await TextAsync(page, "ai-agents-delegate-log"), "assigned to Planner");

        // A full column refuses the drop: the visible notice and the one live region say the same sentence.
        var reviewer = board.Locator("[data-column='reviewer']");
        Assert.AreEqual("true", await reviewer.GetAttributeAsync("data-full"));
        StringAssert.Contains(await reviewer.InnerTextAsync(), "Full");

        // The automatic pass hands what is left to the free agents.
        // The switch's checkbox is visually carried by its track, so the label is what a reader (and Playwright) clicks.
        await board.Locator("[data-testid='delegate-auto'] .tm-toggle-label").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-delegate'] [data-column='backlog'] "
            + "[data-testid='delegate-task']\").length < 2",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // ONE polite live region for the whole board: the transfer-only columns contribute none.
        Assert.AreEqual(1, await board.Locator("[aria-live]").CountAsync());

        await ShootAsync(page, "ai-agents-delegate-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-delegate-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, DelegateSection);
        await ShootAsync(page, "ai-agents-delegate-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-delegate-section");

        // A phone keeps the board inside 375 px and the Assign target at the 44 px touch size.
        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await ScrollToSection(phone, DelegateSection);
        var box375 = await phone.Locator("[data-testid='ai-agents-delegate']").BoundingBoxAsync();
        Assert.IsNotNull(box375);
        Assert.IsTrue(box375!.Width <= 375, $"the delegate board fits a 375 px phone (width {box375.Width})");
        var assignBox = await phone.Locator(
            "[data-testid='ai-agents-delegate'] [data-task-id='task-summary'] .tm-context-menu__trigger")
            .First.BoundingBoxAsync();
        Assert.IsNotNull(assignBox);
        Assert.IsTrue(assignBox!.Height >= 44, $"the Assign target keeps a touch target (height {assignBox.Height})");
        await ShootAsync(phone, "ai-agents-delegate-phone-375");
    }

    [TestMethod]
    public async Task TheScheduledRunsPlaceRunsAndReportAMove()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, RunsSection);

        var runs = page.Locator("[data-testid='ai-agents-runs']");
        Assert.AreEqual(4, await runs.Locator("[data-testid='runs-window']").CountAsync());
        StringAssert.Contains(await runs.InnerTextAsync(), "Part 1 of 2");
        Assert.AreEqual("true", await runs.Locator("[data-window-id='win-tue-pm']").GetAttributeAsync("data-overloaded"));
        StringAssert.Contains(
            await runs.Locator("[data-testid='runs-window-overload']").First.InnerTextAsync(), "Overloaded by");

        // The table is the text alternative (WCAG 1.3.1), not a lesser view.
        await runs.Locator("[data-testid='runs-view'] [data-testid='segmented-option'][data-value='Table']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs']\")?.dataset.view === 'table'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        Assert.IsTrue(await runs.Locator("[data-testid='runs-table-row']").CountAsync() >= 4);
        await ShootAsync(page, "ai-agents-runs-table-light-1440");
        await runs.Locator("[data-testid='runs-view'] [data-testid='segmented-option'][data-value='Week']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs']\")?.dataset.view === 'week'",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // Moving the long run to the second window is reported to the page and the run stays there.
        await runs.Locator("[data-window-id='win-mon-am'] [data-run-id='run-export'] .tm-context-menu__trigger")
            .First.ClickAsync();
        await page.Locator("[data-testid='ai-agents-runs'] .tm-context-menu__item").Nth(1).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-runs-log']\")?.textContent?.includes('moved to')",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // ONE polite live region: the transfer-only window lists contribute none.
        Assert.AreEqual(1, await runs.Locator("[aria-live]").CountAsync());

        await ShootAsync(page, "ai-agents-runs-light-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-runs-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, RunsSection);
        await ShootAsync(page, "ai-agents-runs-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-runs-section");
    }

    [TestMethod]
    public async Task TheConflictRadarSettlesEveryConflictByKeyboard_AndMerges()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, ConflictSection);

        var radar = page.Locator("[data-testid='ai-agents-conflict']");
        Assert.AreEqual(3, await radar.Locator("[data-testid='radar-agent-header']").CountAsync());
        Assert.AreEqual(3, await radar.Locator("[data-testid='radar-row'][data-conflict='true']").CountAsync());
        Assert.IsNotNull(await radar.Locator("[data-testid='radar-merge']").GetAttributeAsync("disabled"),
            "the merge is unavailable until every conflict is settled");
        StringAssert.Contains(await radar.Locator("[data-testid='radar-merge-hint']").InnerTextAsync(), "unresolved");

        // The surface keeps the NATIVE table semantics: a role="grid" without a roving tabindex would make the
        // reader's arrow keys do nothing AND suppress the table navigation commands.
        var table = radar.Locator("[data-testid='conflict-radar-grid']");
        Assert.AreEqual("TABLE", await table.EvaluateAsync<string>("el => el.tagName"));
        Assert.IsNull(await table.GetAttributeAsync("role"), "the radar must not claim role=grid");
        Assert.AreEqual(
            0,
            await table.Locator("[role='row'], [role='rowgroup'], [role='gridcell'], [role='columnheader'], [role='rowheader']").CountAsync(),
            "a native table already carries these roles");
        Assert.AreEqual(
            3,
            await radar.Locator(".tm-ai-radar__resolution-cell[headers]").CountAsync(),
            "each side choice names the file row header it belongs to");

        await ShootAsync(page, "ai-agents-conflict-light-1440");

        // Every conflict is settled from the keyboard: focus the first radio, walk to it with the arrows, press Space.
        var groups = radar.Locator("[role='radiogroup']");
        for (var index = 0; index < 3; index++)
        {
            var first = groups.Nth(index).Locator("input[type='radio']").First;
            await first.FocusAsync();
            await page.Keyboard.PressAsync("ArrowRight");
            await first.PressAsync("Space");
        }

        await page.WaitForFunctionAsync(
            "() => !document.querySelector(\"[data-testid='ai-agents-conflict'] [data-testid='radar-merge']\")?.hasAttribute('disabled')",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        StringAssert.Contains(
            await page.Locator("[data-testid='ai-agents-conflict-log']").InnerTextAsync(), "settled");

        // The conflicts-only filter and a directory collapse are switches and buttons, never pointer-only.
        var filter = radar.Locator("[data-testid='radar-conflicts-only'] .tm-toggle-input");
        await filter.FocusAsync();
        await page.Keyboard.PressAsync("Space");
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-conflict'] [data-testid='radar-row']\")?.dataset.path === 'CHANGELOG.md'",
            new PageWaitForFunctionOptions { Timeout = 10000 });
        await filter.PressAsync("Space");

        var toggle = radar.Locator("[data-testid='radar-directory-toggle']").Nth(1);
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.AreEqual("false", await toggle.GetAttributeAsync("aria-expanded"));
        await page.Keyboard.PressAsync("Enter");
        Assert.AreEqual("true", await toggle.GetAttributeAsync("aria-expanded"));

        await radar.Locator("[data-testid='radar-merge']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-agents-conflict-log']\")?.textContent?.includes('Merge requested')",
            new PageWaitForFunctionOptions { Timeout = 10000 });

        // ONE polite live region, and the decisions survive the host echoing them back.
        Assert.AreEqual(1, await radar.Locator("[aria-live]").CountAsync());
        Assert.AreEqual(3, await radar.Locator("[role='radiogroup'] input[type='radio']:checked").CountAsync());

        await ScrollToSection(page, ConflictSection);
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-conflict-section");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, ConflictSection);
        await ShootAsync(page, "ai-agents-conflict-dark-1440");
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-conflict-section");
    }

    [TestMethod]
    public async Task ThePresenceOverlayMeasuresItsAnchors_AndTheRosterNamesEveryone()
    {
        var page = await OpenAsync();
        await ScrollToSection(page, PresenceSection);

        var presence = page.Locator("[data-testid='ai-agents-presence']");
        Assert.AreEqual(3, await presence.Locator("[data-presence-marker]").CountAsync());
        Assert.AreEqual("true", await presence.Locator("[data-testid='presence-layer']").GetAttributeAsync("aria-hidden"));

        // The module placed every anchored marker: a real pixel offset, not the default fraction.
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll(\"[data-testid='ai-agents-presence'] [data-presence-marker][data-presence-placed='true']\").length === 3",
            new PageWaitForFunctionOptions { Timeout = 15000 });
        var left = await presence.Locator("[data-presence-marker]").First
            .EvaluateAsync<string>("marker => marker.style.getPropertyValue('--tm-presence-x')");
        StringAssert.EndsWith(left, "px");

        StringAssert.Contains(await presence.Locator("[data-presence-marker]").First.InnerTextAsync(), "Planner");
        Assert.AreEqual(3, await presence.Locator("[data-testid='presence-roster-item']").CountAsync());
        Assert.AreEqual(1, await presence.Locator("[aria-live]").CountAsync());

        // The layer is a picture: a press at its centre reaches the document underneath.
        var layer = presence.Locator("[data-testid='presence-layer']");
        Assert.AreEqual("none", await layer.EvaluateAsync<string>(
            "layer => getComputedStyle(layer).pointerEvents"));

        await ScrollToSection(page, PresenceSection);
        await AssertNoSeriousAxeViolationsAsync(page, "ai-agents-presence-section");
        await ShootAsync(page, "ai-agents-presence-light-1440");

        await ToggleThemeAsync(page);
        await ScrollToSection(page, PresenceSection);
        await ShootAsync(page, "ai-agents-presence-dark-1440");

        // A reader who asked for less motion gets the finished position at once.
        var reduced = await OpenAsync(reduceMotion: true);
        await ScrollToSection(reduced, PresenceSection);
        var duration = await reduced.Locator("[data-testid='ai-agents-presence'] [data-presence-marker]").First
            .EvaluateAsync<string>("marker => getComputedStyle(marker).transitionDuration");
        Assert.AreEqual("0s", duration, "prefers-reduced-motion turns the travelling cursor into a jump");
    }
}
