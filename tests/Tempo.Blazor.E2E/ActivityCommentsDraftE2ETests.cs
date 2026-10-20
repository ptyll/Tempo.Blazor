using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-6 lane: the consumer-facing draft-guard capability of <c>TmActivityComments</c> on the
/// /activity demo page. The demo mirrors <c>OnDraftChanged</c> into a visible draft-state panel
/// (<c>data-testid="activity-comments-draft-state"</c>) and exposes the consumer path
/// (<c>DiscardDraftAsync</c>) through a demo button — so the panel flags are EXACTLY what a host
/// page's dirty-guard sees through the public API.
/// <para>
/// Covered: opening the new-comment editor reports "editor open, nothing unsaved"; typing flips
/// it to an unsaved draft (the dedup contract — further typing must keep the SAME dirty state
/// without re-reporting — is guarded in the bUnit suite); editing an EXISTING comment without
/// touching the text must NOT report an unsaved draft (the differs-from-original semantics — the
/// easiest branch to implement wrong); the demo discard button closes the editor and clears the
/// state; submitting the comment leaves no dirty state behind.
/// </para>
/// <para>
/// Screenshots follow the repo's single route (<see cref="BaselineOutput"/>): with
/// <c>TM_WRITE_BASELINES</c> they land under <c>artifacts/baseline/activity</c> (the baseline
/// root — never the tracked <c>__baseline__</c> folder); otherwise in the durable non-opt-in
/// scratch root of <see cref="BaselineOutput.DirectoryFor"/> under the OS temp folder —
/// deliberately NOT the run's deployment directory, which the MSTest runner deletes when the run
/// ends (captures written there vanished with a green run — recorded learning).
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class ActivityCommentsDraftE2ETests : WasmTestBase
{
    private const string SectionSelector = "[data-testid='activity-comments-section']";
    private const string DraftOpenFlag = "[data-testid='activity-comments-draft-open']";
    private const string DraftEditingFlag = "[data-testid='activity-comments-draft-editing']";
    private const string DraftUnsavedFlag = "[data-testid='activity-comments-draft-unsaved']";
    private const string DiscardButtonSelector = "[data-testid='activity-comments-discard-draft']";
    private const string AddButtonSelector = $"{SectionSelector} .tm-comments-add-btn";
    private const string SectionEditorSelector = $"{SectionSelector} .tm-rte-editor-content";
    private const string ThemeToggleSelector = "[data-testid='theme-toggle']";

    private const string ExistingCommentAuthor = "Alice Nguyen";
    private const string ExistingCommentId = "demo-c1";

    /// <summary>A text a real user would type — more than one character, so more than one
    /// input event fires and the dedup contract is exercised on the way up.</summary>
    private const string DraftText = "E2E draft-guard probe: this text must trigger the unsaved flag.";

    private sealed record DraftFlags(bool EditorOpen, string Editing, bool Unsaved)
    {
        public override string ToString() => $"open={EditorOpen}, editing={Editing}, unsaved={Unsaved}";
    }

    private async Task<IPage> OpenActivityAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{BaseUrl}/activity",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        // The standalone comments section works on local data — it renders even when the demo
        // API is unreachable, so the wait below is not masked by an API outage.
        await Assertions.Expect(page.Locator(SectionSelector)).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-comment-item").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        return page;
    }

    private static async Task<DraftFlags> ReadDraftFlagsAsync(IPage page)
    {
        var open = (await page.Locator(DraftOpenFlag).TextContentAsync())?.Trim();
        var editing = (await page.Locator(DraftEditingFlag).TextContentAsync())?.Trim();
        var unsaved = (await page.Locator(DraftUnsavedFlag).TextContentAsync())?.Trim();
        return new DraftFlags(
            EditorOpen: string.Equals(open, "yes", StringComparison.Ordinal),
            Editing: editing ?? string.Empty,
            Unsaved: string.Equals(unsaved, "yes", StringComparison.Ordinal));
    }

    /// <summary>Asserts (web-first) that the panel reports the expected flags — the Blazor render
    /// lands a few frames after the input event, so the assertions retry internally instead of
    /// sleeping between polls — and returns the observed flags. <paramref name="editing"/> may be
    /// null when the edited comment does not matter.</summary>
    private static async Task<DraftFlags> WaitForDraftFlagsAsync(IPage page, bool open, string? editing, bool unsaved)
    {
        var options = new LocatorAssertionsToHaveTextOptions { Timeout = 15000 };
        await Assertions.Expect(page.Locator(DraftOpenFlag)).ToHaveTextAsync(open ? "yes" : "no", options);
        await Assertions.Expect(page.Locator(DraftUnsavedFlag)).ToHaveTextAsync(unsaved ? "yes" : "no", options);
        if (editing is not null)
        {
            await Assertions.Expect(page.Locator(DraftEditingFlag)).ToHaveTextAsync(editing, options);
        }

        return await ReadDraftFlagsAsync(page);
    }

    private static async Task TypeIntoSectionEditorAsync(IPage page, string text)
    {
        var editor = page.Locator(SectionEditorSelector).First;
        await editor.ClickAsync();
        await editor.PressSequentiallyAsync(text, new LocatorPressSequentiallyOptions { Delay = 15 });
    }

    private async Task CaptureSectionAsync(IPage page, string name)
    {
        // The demo host keeps moving after ready (Tailwind Play CDN + Google Fonts); settle the
        // section boxes before capturing so the PNGs are not mid-reflow (see e2e-test-lanes.md).
        await WaitForLayoutToSettleAsync(page, $"{SectionSelector}, {DraftOpenFlag}, {SectionEditorSelector}");
        var directory = BaselineOutput.DirectoryFor(TestContext, "activity");
        var path = Path.Combine(directory, $"{name}.png");
        await page.Locator(SectionSelector).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = path,
            Timeout = 60_000
        });
        Assert.IsTrue(File.Exists(path), $"the screenshot was not written: {path}");
        TestContext.AddResultFile(path);
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task NewCommentEditor_ReportsOpenThenUnsaved_AsTheUserTypes()
    {
        var page = await OpenActivityAsync();

        // Closed editor: clean state, Add visible, discard disabled.
        var initial = await ReadDraftFlagsAsync(page);
        Assert.IsFalse(initial.EditorOpen, $"initial {initial}");
        Assert.AreEqual("none", initial.Editing, $"initial {initial}");
        Assert.IsFalse(initial.Unsaved, $"initial {initial}");
        await Assertions.Expect(page.Locator(AddButtonSelector)).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(DiscardButtonSelector)).ToBeDisabledAsync();
        await CaptureSectionAsync(page, "closed-editor");

        // Open the new-comment editor: editor-open without unsaved content.
        await page.Locator(AddButtonSelector).ClickAsync();
        await Assertions.Expect(page.Locator(SectionEditorSelector).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await WaitForDraftFlagsAsync(page, open: true, editing: "none", unsaved: false);
        await Assertions.Expect(page.Locator(AddButtonSelector)).ToHaveCountAsync(0);
        await CaptureSectionAsync(page, "editor-open-empty");

        // Typing flips the flag exactly once — the dedup contract (further typing keeps the SAME
        // dirty state and must not re-report anything) is guarded in the bUnit suite.
        await TypeIntoSectionEditorAsync(page, DraftText);
        var dirty = await WaitForDraftFlagsAsync(page, open: true, editing: "none", unsaved: true);
        Assert.IsTrue(dirty.Unsaved, $"after typing {dirty}");
        await CaptureSectionAsync(page, "editor-open-dirty");

        // Dark mode evidence: no new CSS shipped with this capability, so the panel must look
        // and read identically under the dark tokens.
        await page.Locator($"{ThemeToggleSelector}:visible").First.ClickAsync();
        await Assertions.Expect(page.Locator(".demo-shell"))
            .ToHaveAttributeAsync("data-theme", "dark", new LocatorAssertionsToHaveAttributeOptions { Timeout = 15000 });
        await WaitForDraftFlagsAsync(page, open: true, editing: "none", unsaved: true);
        await CaptureSectionAsync(page, "editor-open-dirty-dark");
    }

    [TestMethod]
    [TestCategory("Smoke")]
    public async Task EditingExistingComment_WithoutTouchingText_IsNotAnUnsavedDraft()
    {
        var page = await OpenActivityAsync();

        var editButton = page.Locator($"{SectionSelector} .tm-comment-item", new PageLocatorOptions { HasText = ExistingCommentAuthor })
            .Locator(".tm-comment-edit-btn");
        await Assertions.Expect(editButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await editButton.ClickAsync();

        await Assertions.Expect(page.Locator(SectionEditorSelector).First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        var flags = await WaitForDraftFlagsAsync(page, open: true, editing: ExistingCommentId, unsaved: false);
        Assert.IsFalse(flags.Unsaved,
            $"opening an edit must NOT report an unsaved draft before the content changes; observed {flags}");
        await Assertions.Expect(page.Locator(AddButtonSelector)).ToHaveCountAsync(0);

        // The prefilled body is really in the editor (the comparison baseline is the original).
        var content = await page.Locator(SectionEditorSelector).First.TextContentAsync();
        StringAssert.Contains(content, "outage window", "the edit editor should open pre-filled with the comment body");

        await CaptureSectionAsync(page, "edit-existing-untouched");

        // Typing under the edit semantics (differs-from-original) flips the flag.
        await TypeIntoSectionEditorAsync(page, " Correction: window moved to 23:45.");
        await WaitForDraftFlagsAsync(page, open: true, editing: ExistingCommentId, unsaved: true);
    }

    [TestMethod]
    public async Task DiscardDraftButton_ClosesEditor_AndClearsTheDraftState()
    {
        var page = await OpenActivityAsync();

        await page.Locator(AddButtonSelector).ClickAsync();
        await TypeIntoSectionEditorAsync(page, DraftText);
        await WaitForDraftFlagsAsync(page, open: true, editing: "none", unsaved: true);

        await Assertions.Expect(page.Locator(DiscardButtonSelector)).ToBeEnabledAsync(new LocatorAssertionsToBeEnabledOptions { Timeout = 15000 });
        await page.Locator(DiscardButtonSelector).ClickAsync();

        // The editor disappears and the panel returns to the clean state.
        await Assertions.Expect(page.Locator(SectionEditorSelector)).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await WaitForDraftFlagsAsync(page, open: false, editing: "none", unsaved: false);
        await Assertions.Expect(page.Locator(DiscardButtonSelector)).ToBeDisabledAsync();
        await Assertions.Expect(page.Locator(AddButtonSelector)).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task SubmittingComment_ClearsDraftState_AndAppendsTheComment()
    {
        var page = await OpenActivityAsync();
        var commentsBefore = await page.Locator($"{SectionSelector} .tm-comment-item").CountAsync();
        Assert.IsTrue(commentsBefore > 0, "the demo seeds standalone comments");

        await page.Locator(AddButtonSelector).ClickAsync();
        await TypeIntoSectionEditorAsync(page, "Submitted through the E2E lane: no dirty flag may survive the submit.");
        await WaitForDraftFlagsAsync(page, open: true, editing: "none", unsaved: true);

        await page.Locator($"{SectionSelector} .tm-comment-submit-btn").ClickAsync();

        await Assertions.Expect(page.Locator(SectionEditorSelector)).ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await WaitForDraftFlagsAsync(page, open: false, editing: "none", unsaved: false);
        await Assertions.Expect(page.Locator($"{SectionSelector} .tm-comment-item"))
            .ToHaveCountAsync(commentsBefore + 1, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator(SectionSelector))
            .ToContainTextAsync("no dirty flag may survive the submit", new LocatorAssertionsToContainTextOptions { Timeout = 15000 });
        await Assertions.Expect(page.Locator(AddButtonSelector)).ToBeVisibleAsync();
    }
}
