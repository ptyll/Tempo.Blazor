using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 4 batch B functional checks for the page-header dialogs (plan c8d4bcd2, N1): the cover
/// dialog and the emoji picker ride the core TmModal — centred and titled at 1440, initial focus
/// on the URL input, Escape close with focus return, disabled primary until a value exists. The
/// page-info panel shares the same contract and its metric cards anchor values on one baseline.
/// </summary>
[TestClass]
public sealed class NotionPageHeaderDialogsE2ETests : NotionE2ETestBase
{
    private static async Task<string> ActiveElementDescriptionAsync(IPage page)
        => await page.EvaluateAsync<string>(
            """
            () => {
                const el = document.activeElement;
                if (!el) return '<none>';
                const id = el.id ? '#' + el.id : '';
                const cls = typeof el.className === 'string' && el.className
                    ? '.' + el.className.trim().split(/\s+/).slice(0, 3).join('.')
                    : '';
                return `${el.tagName.toLowerCase()}${id}${cls}`;
            }
            """);

    private static async Task AssertCentredAsync(IPage page, ILocator modal, int viewportWidth, string what)
    {
        var box = await modal.BoundingBoxAsync()
            ?? throw new AssertFailedException($"{what}: no bounding box");
        var centerX = box.X + box.Width / 2;
        Assert.IsTrue(Math.Abs(centerX - viewportWidth / 2.0) < 60,
            $"{what} must be horizontally centred (center {centerX:F0}, viewport {viewportWidth})");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Cover dialog at 1440: centred core TmModal, visible title, initial focus on the URL input, disabled primary.")]
    public async Task CoverDialog_At1440_CentredTitledModal_FocusesUrlInput()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await OpenCoverDialogAsync(page);

        var overlay = page.Locator(".tm-modal-overlay").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.AreEqual("Change cover", await page.Locator(".tm-modal-title").First.InnerTextAsync());
        await AssertCentredAsync(page, page.Locator(".tm-modal").First, 1440, "cover dialog");

        // Initial focus lands on the URL field — not the ×, not a preset (batch B UX fix).
        var active = await ActiveElementDescriptionAsync(page);
        StringAssert.Contains(active, "input#tm-notion-cover-url",
            "opening the cover dialog must focus the URL input");

        Assert.IsTrue(await page.Locator(".tm-modal-footer .tm-btn-primary").First.IsDisabledAsync(),
            "Set cover stays disabled while the URL input is empty");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Typing a URL enables Set cover; applying it renders the header cover. The preset selected-state evidence lives in the phase-4 screenshot suite.")]
    public async Task CoverDialog_TypingUrl_EnablesSetCover_AndApplies()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await OpenCoverDialogAsync(page);
        var primary = page.Locator(".tm-modal-footer .tm-btn-primary").First;
        Assert.IsTrue(await primary.IsDisabledAsync());

        await page.Locator("#tm-notion-cover-url").FillAsync("https://example.com/cover.png");
        await page.WaitForTimeoutAsync(200);
        Assert.IsFalse(await primary.IsDisabledAsync(), "Set cover enables once a URL is present");

        await primary.ClickAsync();
        await page.Locator(".tm-notion-header-cover").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
        Assert.AreEqual(0, await page.Locator(".tm-notion-cover-dialog__input").CountAsync(),
            "the dialog closes after Set cover applies");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Escape closes the cover dialog and returns focus to the Add cover trigger.")]
    public async Task CoverDialog_Escape_ClosesAndReturnsFocusToTrigger()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        // A REAL click — the focus scope remembers the actual opener, so focus can return to it.
        var addCover = page.Locator(".tm-notion-header-meta__add-btn", new PageLocatorOptions { HasText = "Add cover" }).First;
        await addCover.ClickAsync(new LocatorClickOptions { Force = true });
        await page.Locator("#tm-notion-cover-url").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
        StringAssert.Contains(await ActiveElementDescriptionAsync(page), "input#tm-notion-cover-url");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-notion-cover-dialog__input").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });

        var active = await ActiveElementDescriptionAsync(page);
        TestContext.WriteLine($"focus after Escape: {active}");
        StringAssert.Contains(active, "tm-notion-header-meta__add-btn",
            "focus must return to the Add cover trigger");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Emoji picker at 1440: titled core modal; Escape closes it and returns focus to the page icon.")]
    public async Task EmojiPicker_At1440_TitledModal_EscapeReturnsFocus()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        var icon = page.Locator(".tm-notion-header-icon").First;
        await icon.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        await icon.ClickAsync(new LocatorClickOptions { Force = true });

        var overlay = page.Locator(".tm-modal-overlay").First;
        await overlay.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        Assert.AreEqual("Choose icon", await page.Locator(".tm-modal-title").First.InnerTextAsync());
        await page.Locator(".tm-notion-emoji-picker__search").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-notion-emoji-picker__search").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden,
            Timeout = 5000
        });

        var active = await ActiveElementDescriptionAsync(page);
        TestContext.WriteLine($"focus after Escape: {active}");
        StringAssert.Contains(active, "tm-notion-header-icon",
            "focus must return to the page icon trigger");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Page info at 1440: centred core modal and metric card values anchored on one baseline (align-self: end).")]
    public async Task PageInfoDialog_At1440_Centred_AndMetricCardsShareBaseline()
    {
        var page = await OpenNotionEditorAsync(1440, 900);
        await SeedPageInfoPageAsync();
        await OpenPageInfoAsync(page);

        var panel = page.Locator("[data-page-info-panel]").First;
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        await AssertCentredAsync(page, panel, 1440, "page info panel");

        var cards = page.Locator(".tm-page-info__metric");
        Assert.IsTrue(await cards.CountAsync() >= 2, "word count and reading time cards render");

        var alignSelf = await page.Locator(".tm-page-info__metric-value").First.EvaluateAsync<string>(
            "el => getComputedStyle(el).alignSelf");
        Assert.AreEqual("end", alignSelf, "the metric value anchors to the card bottom row");
    }

    [TestMethod]
    [TestCategory("NotionUxBaseline")]
    [Description("Page info at 390 presents as a core bottom sheet docked to the viewport bottom edge.")]
    public async Task PageInfoDialog_At390_PresentsAsBottomSheet()
    {
        var page = await OpenNotionEditorAsync(390, 844);
        await SeedPageInfoPageAsync();
        await page.Locator(".tm-notion-editor").First.ScrollIntoViewIfNeededAsync();
        await OpenPageInfoAsync(page);

        var sheet = page.Locator(".tm-modal--sheet").First;
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 8000 });
        var box = await sheet.BoundingBoxAsync() ?? throw new AssertFailedException("sheet has no box");
        Assert.IsTrue(Math.Abs(box.Y + box.Height - 844) < 2,
            $"sheet bottom must sit at the viewport bottom edge (bottom {box.Y + box.Height:F0})");
    }

    private async Task OpenCoverDialogAsync(IPage page)
    {
        var addCover = page.Locator(".tm-notion-header-meta__add-btn",
            new PageLocatorOptions { HasText = "Add cover" }).First;
        if (await addCover.CountAsync() == 0)
        {
            addCover = page.Locator(".tm-notion-header-cover__btn",
                new PageLocatorOptions { HasText = "Change cover" }).First;
        }
        await addCover.EvaluateAsync("el => el.click()");
        await page.Locator("#tm-notion-cover-url").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
    }

    private static async Task OpenPageInfoAsync(IPage page)
    {
        await page.Locator(".tm-npsm-trigger").First.EvaluateAsync("el => el.click()");
        await page.Locator(".tm-npsm__item").Filter(new LocatorFilterOptions { HasText = "Page info" })
            .First.ClickAsync();
    }
}
