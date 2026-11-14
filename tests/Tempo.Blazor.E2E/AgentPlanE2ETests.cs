using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 2 — <c>TmAgentPlan</c> in a real browser. The assertions read behaviour rather than
/// markup: the plan is a group of numbered rows each naming its state in words, the inline rename saves on
/// Enter and cancels on Escape, a step can be inserted below a row or at the end, a removal is confirmed
/// through its Undo strip, the rows reorder by keyboard, by the row menu and by a pointer drag on the grip,
/// and the running surface is read-only with a live clock and Retry on the failed step. A running plan never
/// leaks an edit path; a locked review keeps its rows and its Approve action but no edit action; the phone
/// width keeps every action at the 44 px touch target and the page never scrolls sideways; a scoped axe scan
/// of the three sections finds no critical or serious violation. Reference screenshots (light, dark, phone)
/// land in <c>TestResults/agent-plan</c>.
/// </summary>
public abstract class AgentPlanE2EBase : PlaywrightTestBase
{
    private const string ReviewSection = "[data-testid='agent-plan-review-section']";
    private const string RunningSection = "[data-testid='agent-plan-running-section']";
    private const string LockedSection = "[data-testid='agent-plan-readonly-section']";

    private string ShotDir
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "agent-plan");

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

    private static ILocator Section(IPage page, string selector) => page.Locator(selector);

    private static ILocator Review(IPage page) => page.Locator("[data-testid='agent-plan-review']");

    private static ILocator Running(IPage page) => page.Locator("[data-testid='agent-plan-running']");

    private static ILocator Locked(IPage page) => page.Locator("[data-testid='agent-plan-locked']");

    private static ILocator ReviewRows(IPage page) => Review(page).Locator("[data-testid='agent-plan-step']");

    /// <summary>
    /// One title per row, taken from the title element or — for the row that is being renamed — from the
    /// inline editor's value, so the list always has one entry per row.
    /// </summary>
    private static Task<string[]> ReviewTitlesAsync(IPage page)
        => Review(page).Locator("[data-testid='agent-plan-step']").EvaluateAllAsync<string[]>(
            """
            rows => rows.map(row => {
                const title = row.querySelector("[data-testid='agent-plan-step-title']");
                if (title) return title.textContent.trim();
                const input = row.querySelector("[data-testid='agent-plan-step-rename']");
                return input ? input.value : '';
            })
            """);

    private static ILocator LastEdit(IPage page) => page.Locator("[data-testid='agent-plan-last-edit']");

    private async Task<IPage> OpenAsync(int width = 1440, int height = 1000, bool reduceMotion = false,
        bool coarsePointer = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            ReducedMotion = reduceMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IgnoreHTTPSErrors = true,
            HasTouch = coarsePointer,
        });
        await context.AddInitScriptAsync(
            "localStorage.setItem('tm-demo-culture', 'en'); document.cookie = 'tm-demo-culture=en;path=/;max-age=31536000;samesite=lax';");
        RegisterContext(context);

        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/agent-plan");
        await page.WaitForSelectorAsync(
            $"{ReviewSection} [data-testid='agent-plan-review'] [data-testid='agent-plan-step']",
            new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    [TestMethod]
    public async Task TheReviewPlanExposesItsStructure()
    {
        var page = await OpenAsync();

        Assert.AreEqual(4, await ReviewRows(page).CountAsync());
        CollectionAssert.AreEqual(
            new[] { "Draft the outline", "Confirm the sources", "Review with the team", "Publish the summary" },
            await ReviewTitlesAsync(page));

        var statuses = await Review(page).Locator("[data-testid='agent-plan-step-status']").AllTextContentsAsync();
        CollectionAssert.AreEqual(new[] { "Pending", "Pending", "Pending", "Pending" },
            statuses.Select(status => status.Trim()).ToArray());

        var summary = (await Review(page).Locator("[data-testid='agent-plan-summary']").TextContentAsync())?.Trim();
        Assert.AreEqual("0 of 4 done", summary);

        // The plan is named by the heading a reader sees, and described by the hidden instructions.
        var group = Review(page);
        Assert.AreEqual("group", await group.GetAttributeAsync("role"));
        var labelledBy = await group.GetAttributeAsync("aria-labelledby");
        Assert.IsFalse(string.IsNullOrEmpty(labelledBy));
        Assert.AreEqual("Release checklist",
            (await page.Locator($"#{labelledBy}").TextContentAsync())?.Trim());

        // One polite region for the plan's own events, and the list keeps its own keyboard model.
        Assert.AreEqual("polite",
            await Review(page).Locator("[data-testid='agent-plan-announcement']").GetAttributeAsync("aria-live"));
        Assert.AreEqual("Agent plan",
            await Review(page).Locator("[data-testid='agent-plan-list'] [role='list']").GetAttributeAsync("aria-label"));
    }

    [TestMethod]
    public async Task RenamingAStepInline_IsSavedByTheHostPage()
    {
        var page = await OpenAsync();

        await Review(page).Locator("[data-testid='agent-plan-step-title']").Nth(1).ClickAsync();
        var input = Review(page).Locator("[data-testid='agent-plan-step-rename']");
        await input.WaitForAsync();
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("Confirm the sources and the numbers");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual("Confirm the sources and the numbers", (await ReviewTitlesAsync(page))[1]);
        Assert.AreEqual("Rename", await LastEdit(page).GetAttributeAsync("data-kind"));
        Assert.AreEqual("1", await LastEdit(page).GetAttributeAsync("data-index"));
        StringAssert.Contains(await LastEdit(page).TextContentAsync() ?? string.Empty,
            "Confirm the sources and the numbers");

        // The plan announced the rename exactly once.
        StringAssert.Contains(
            await Review(page).Locator("[data-testid='agent-plan-announcement']").TextContentAsync() ?? string.Empty,
            "Step 2 renamed to");
    }

    [TestMethod]
    public async Task EscapeCancelsTheRename_AndLeavesTheTitleAlone()
    {
        var page = await OpenAsync();

        await Review(page).Locator("[data-testid='agent-plan-step-title']").First.FocusAsync();
        await page.Keyboard.PressAsync("F2");
        var input = Review(page).Locator("[data-testid='agent-plan-step-rename']");
        await input.WaitForAsync();
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("Something else");
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForTimeoutAsync(250);

        Assert.AreEqual(0, await Review(page).Locator("[data-testid='agent-plan-step-rename']").CountAsync());
        Assert.AreEqual("Draft the outline", (await ReviewTitlesAsync(page))[0]);
        Assert.AreEqual("none", await LastEdit(page).GetAttributeAsync("data-kind"));
        StringAssert.Contains(
            await Review(page).Locator("[data-testid='agent-plan-announcement']").TextContentAsync() ?? string.Empty,
            "Renaming cancelled");
    }

    [TestMethod]
    public async Task ARefusedEmptyTitle_IsShownOnTheField_NotOnlyAnnounced()
    {
        var page = await OpenAsync();

        await Review(page).Locator("[data-testid='agent-plan-step-title']").First.ClickAsync();
        var input = Review(page).Locator("[data-testid='agent-plan-step-rename']");
        await input.WaitForAsync();
        await input.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.PressAsync("Backspace");
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);

        // The refusal keeps the editor open, and it is VISIBLE: the field is invalid and the reason is
        // rendered under it, not only spoken by the plan's live region (WCAG 3.3.1).
        Assert.AreEqual(1, await Review(page).Locator("[data-testid='agent-plan-step-rename']").CountAsync(),
            "the editor stays open so the reader can fix the title");
        var refused = Review(page).Locator("[data-testid='agent-plan-step-rename']");
        Assert.AreEqual("true", await refused.GetAttributeAsync("aria-invalid"));
        var describedBy = await refused.GetAttributeAsync("aria-describedby");
        Assert.IsFalse(string.IsNullOrEmpty(describedBy), "the invalid field points at its reason");
        var message = page.Locator($"#{describedBy}");
        Assert.AreEqual("A step needs a title.", (await message.TextContentAsync())?.Trim());
        Assert.AreEqual("none", await LastEdit(page).GetAttributeAsync("data-kind"), "no edit left the page");
        StringAssert.Contains(
            await Review(page).Locator("[data-testid='agent-plan-announcement']").TextContentAsync() ?? string.Empty,
            "A step needs a title");

        await ShootAsync(page, "agent-plan-review-rename-error-1440");

        // Typing is the fix the error asks for, so the state clears as soon as the reader types.
        await refused.ClickAsync();
        await page.Keyboard.PressAsync("Control+A");
        await page.Keyboard.TypeAsync("Draft the outline, revised");
        await page.WaitForTimeoutAsync(200);
        Assert.IsNull(await Review(page).Locator("[data-testid='agent-plan-step-rename']")
            .GetAttributeAsync("aria-invalid"));
        Assert.AreEqual(0, await Review(page).Locator(".tm-ai-agent-plan__rename-error").CountAsync());

        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(300);
        Assert.AreEqual("Draft the outline, revised", (await ReviewTitlesAsync(page))[0]);
    }

    [TestMethod]
    public async Task AStepCanBeInsertedBelowARow_AndRemovedThroughTheUndoStrip()
    {
        var page = await OpenAsync();

        await Review(page).Locator("[data-testid='agent-plan-step-add']").First.ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(5, await ReviewRows(page).CountAsync());
        Assert.AreEqual("Add", await LastEdit(page).GetAttributeAsync("data-kind"));
        Assert.AreEqual(1, await Review(page).Locator("[data-testid='agent-plan-step-rename']").CountAsync(),
            "the inserted row opens in its editor so the reader names it");

        await Review(page).Locator("[data-testid='agent-plan-step-rename-cancel']").ClickAsync();
        await page.WaitForTimeoutAsync(200);

        await Review(page).Locator("[data-testid='agent-plan-step-remove']").Nth(2).ClickAsync();
        await page.WaitForTimeoutAsync(300);

        Assert.AreEqual(4, await ReviewRows(page).CountAsync());
        Assert.AreEqual("Remove", await LastEdit(page).GetAttributeAsync("data-kind"));
        var undo = Review(page).Locator("[data-testid='agent-plan-undo']");
        Assert.AreEqual(1, await undo.CountAsync());

        await Review(page).Locator("[data-testid='agent-plan-undo-button']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        // The inserted step survives the removal, so the restored plan holds five rows with the removed one
        // back at the position it left.
        CollectionAssert.AreEqual(
            new[]
            {
                "Draft the outline", "New step", "Confirm the sources", "Review with the team",
                "Publish the summary",
            },
            await ReviewTitlesAsync(page));
        Assert.AreEqual(5, await ReviewRows(page).CountAsync());
        Assert.AreEqual(0, await Review(page).Locator("[data-testid='agent-plan-undo']").CountAsync(),
            "the strip is spent");
    }

    [TestMethod]
    public async Task TheRowsReorderByKeyboard_AndFromTheRowMenu()
    {
        var page = await OpenAsync();

        var row = Review(page).Locator(".tm-sortable-list__item").First;
        await row.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("ArrowDown");
        await page.WaitForTimeoutAsync(150);
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForTimeoutAsync(350);

        Assert.AreEqual("Confirm the sources", (await ReviewTitlesAsync(page))[0]);
        Assert.AreEqual("Reorder", await LastEdit(page).GetAttributeAsync("data-kind"));
        Assert.AreEqual("Agent plan",
            await Review(page).Locator("[data-testid='agent-plan-list'] [role='list']").GetAttributeAsync("aria-label"),
            "the nested list keeps its own name after the move");

        await Review(page).Locator(".tm-context-menu__trigger").First.ClickAsync();
        await page.WaitForSelectorAsync("[role='menu'] [role='menuitem']");
        await page.Locator("[role='menu'] [role='menuitem']", new PageLocatorOptions { HasTextString = "Move to bottom" })
            .ClickAsync();
        await page.WaitForTimeoutAsync(350);

        Assert.AreEqual("Reorder", await LastEdit(page).GetAttributeAsync("data-kind"));
        Assert.AreEqual("Draft the outline", (await ReviewTitlesAsync(page))[0],
            "the menu moved the row it was opened on to the end");
        Assert.AreEqual("Confirm the sources", (await ReviewTitlesAsync(page))[3]);
    }

    [TestMethod]
    public async Task AStepDragsByItsGrip()
    {
        var page = await OpenAsync();

        var handleBox = await Review(page).Locator(".tm-sortable-list__handle").First.BoundingBoxAsync();
        var targetRow = await Review(page).Locator(".tm-sortable-list__item").Nth(2).BoundingBoxAsync();
        Assert.IsNotNull(handleBox);
        Assert.IsNotNull(targetRow);

        var startX = handleBox!.X + handleBox.Width / 2;
        var startY = handleBox.Y + handleBox.Height / 2;
        var endY = targetRow!.Y + targetRow.Height * 0.75;

        await page.Mouse.MoveAsync(startX, startY);
        await page.Mouse.DownAsync();
        for (var step = 1; step <= 8; step++)
        {
            await page.Mouse.MoveAsync(startX, (float)(startY + (endY - startY) * step / 8.0));
            await page.WaitForTimeoutAsync(30);
        }

        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);

        Assert.AreEqual("Draft the outline", (await ReviewTitlesAsync(page))[2],
            "the dragged row landed where the pointer dropped it");
        Assert.AreEqual("Reorder", await LastEdit(page).GetAttributeAsync("data-kind"));
    }

    [TestMethod]
    public async Task TheRunningPlanIsReadOnly_TicksAndRetries()
    {
        var page = await OpenAsync();

        await Section(page, RunningSection).ScrollIntoViewIfNeededAsync();

        Assert.AreEqual("running", await Running(page).GetAttributeAsync("data-mode"));
        Assert.AreEqual(0, await Running(page).Locator("[data-testid='agent-plan-step-remove']").CountAsync());
        Assert.AreEqual(0, await Running(page).Locator("[data-testid='agent-plan-step-add']").CountAsync());
        Assert.AreEqual(0, await Running(page).Locator("[data-testid='agent-plan-add']").CountAsync());
        Assert.AreEqual(0, await Running(page).Locator("[data-testid='agent-plan-approve']").CountAsync());

        var statuses = await Running(page).Locator("[data-testid='agent-plan-step']")
            .EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.status)");
        CollectionAssert.AreEqual(new[] { "done", "done", "done", "done", "running", "failed", "pending" }, statuses);

        var summary = (await Running(page).Locator("[data-testid='agent-plan-summary']").TextContentAsync())?.Trim();
        StringAssert.StartsWith(summary ?? string.Empty, "4 of 7 done · ");

        // The running step counts live: the clock must move between two reads.
        var time = Running(page).Locator("[data-testid='agent-plan-step-time']");
        var before = await time.Nth(4).TextContentAsync();
        await page.WaitForTimeoutAsync(2300);
        var after = await time.Nth(4).TextContentAsync();
        Assert.AreNotEqual(before, after, "the running step counts its live elapsed time");

        await Running(page).Locator("[data-testid='agent-plan-step-retry']").ClickAsync();
        await page.WaitForTimeoutAsync(300);

        StringAssert.Contains(await page.Locator("[data-testid='agent-plan-retried']").TextContentAsync() ?? string.Empty,
            "Notify the finance team");
        Assert.AreEqual("running",
            await Running(page).Locator("[data-testid='agent-plan-step']").Nth(5).GetAttributeAsync("data-status"),
            "the retried step runs again");
    }

    [TestMethod]
    public async Task TheLockedReviewKeepsItsRowsAndApproveButNoEditPath()
    {
        var page = await OpenAsync();

        await Section(page, LockedSection).ScrollIntoViewIfNeededAsync();

        Assert.AreEqual("false", await Locked(page).GetAttributeAsync("data-editable"));
        Assert.AreEqual(3, await Locked(page).Locator("[data-testid='agent-plan-step']").CountAsync());
        Assert.AreEqual(0, await Locked(page).Locator("button[data-testid='agent-plan-step-title']").CountAsync());
        Assert.AreEqual(0, await Locked(page).Locator("[data-testid='agent-plan-add']").CountAsync());
        Assert.AreEqual(1, await Locked(page).Locator("[data-testid='agent-plan-approve']").CountAsync());
        Assert.AreEqual("Locked review plan", await Locked(page).GetAttributeAsync("aria-label"));
    }

    [TestMethod]
    public async Task ThePhoneLayoutKeepsTouchTargetsAndNeverScrollsSideways()
    {
        var page = await OpenAsync(375, 812, coarsePointer: true);

        var scrollWidth = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth");
        Assert.IsTrue(scrollWidth <= 376, $"the page does not scroll sideways (scrollWidth {scrollWidth})");

        foreach (var action in new[] { "agent-plan-step-remove", "agent-plan-add" })
        {
            var box = await Review(page).Locator($"[data-testid='{action}']").First.BoundingBoxAsync();
            Assert.IsNotNull(box, $"{action} is rendered on a phone");
            Assert.IsTrue(box!.Height >= 43.5, $"{action} reaches the 44 px touch target (height {box.Height})");
        }

        // The narrow band must not squeeze the title into the index column: the title owns the body band.
        var titleBox = await Review(page).Locator("[data-testid='agent-plan-step-title']").First.BoundingBoxAsync();
        Assert.IsNotNull(titleBox);
        Assert.IsTrue(titleBox!.Width >= 140,
            $"the step title keeps a readable width on a phone (width {titleBox.Width})");

        var reviewBox = await Review(page).BoundingBoxAsync();
        Assert.IsNotNull(reviewBox);
        Assert.IsTrue(reviewBox!.X >= 0 && reviewBox.Width <= 375,
            $"the plan stays inside a 375 px phone (x {reviewBox.X}, width {reviewBox.Width})");
    }

    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js"
        });

        foreach (var testId in new[]
                 {
                     "agent-plan-review-section",
                     "agent-plan-running-section",
                     "agent-plan-readonly-section",
                 })
        {
            var violations = await page.EvaluateAsync<string[]>(
                """
                async (selector) => {
                    const host = document.querySelector(selector);
                    // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                    // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                    // like the skeleton shimmer, never finish and are not waited for).
                    await Promise.all(document.getAnimations()
                        .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                        .map(a => a.finished.catch(() => { })));
                    await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                    const result = await axe.run(host, {
                        runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                        resultTypes: ['violations']
                    });
                    return result.violations
                        .filter(v => v.impact === 'critical' || v.impact === 'serious')
                        .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).slice(0, 4).join('; ')})`);
                }
                """,
                $"[data-testid='{testId}']");
            Assert.AreEqual(0, violations.Length, $"axe [{testId}]: " + string.Join(" | ", violations));
        }
    }

    [TestMethod]
    public async Task VisualSweep_LightAndDark_DesktopAndPhone()
    {
        var desktop = await OpenAsync(1440, 1000);

        await Section(desktop, ReviewSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "agent-plan-review-light-1440");

        await Section(desktop, RunningSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "agent-plan-running-light-1440");

        await ToggleThemeAsync(desktop);
        await Section(desktop, RunningSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "agent-plan-running-dark-1440");
        await Section(desktop, ReviewSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(desktop, "agent-plan-review-dark-1440");

        var phone = await OpenAsync(375, 812, coarsePointer: true);
        await Section(phone, ReviewSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "agent-plan-review-light-375");

        await ToggleThemeAsync(phone);
        await Section(phone, RunningSection).ScrollIntoViewIfNeededAsync();
        await ShootAsync(phone, "agent-plan-running-dark-375");
    }

    /// <summary>Switches the demo's own theme (the header toggle), so the whole frame repaints.</summary>
    private static async Task ToggleThemeAsync(IPage page)
    {
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// Frames the section that is being reviewed, in the viewport: a full-page shot of a long demo page
    /// simply frames the top of the page and proves nothing about this component.
    /// </summary>
    private async Task ShootAsync(IPage page, string name)
    {
        await page.WaitForTimeoutAsync(300);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync(
            "() => document.activeElement instanceof HTMLElement && document.activeElement.blur()");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(ShotDir, $"{name}.png"),
            FullPage = false,
        });
    }
}

/// <summary>The WASM lane (the demo default).</summary>
[TestClass]
public class AgentPlanE2ETests : AgentPlanE2EBase
{
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
