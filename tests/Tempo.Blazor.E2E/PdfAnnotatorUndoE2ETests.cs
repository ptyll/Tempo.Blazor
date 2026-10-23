using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// FZE-15: the undo/redo E2E lane of <see cref="PdfAnnotatorE2ETests"/> — the toolbar history
/// group (stamp → undo → redo), the delete→undo thread restore, the keyboard shortcuts, the
/// native text undo in the composer, the two-user conflict, the Continuous anchor scroll, the
/// reload stack clear and the AllowUndo=false presentation. Split from PdfAnnotatorE2ETests.cs
/// to keep both under the 1200-line ceiling. Screenshots land in <c>__screenshots__/pdf-annotator/</c>.
/// </summary>
public partial class PdfAnnotatorE2ETests
{
    // ── FZE-15: undo/redo ────────────────────────────────────────────────────

    private static ILocator UndoCard(IPage page)
        => page.Locator("[data-testid='pdf-annotator-undo-card']");

    /// <summary>Deletes one comment of the undo card's selected thread (the item's "⋯" menu).</summary>
    private static async Task DeleteCommentAsync(ILocator card, int commentIndex)
    {
        var item = card.Locator("[data-testid='pdf-annotator-comment']").Nth(commentIndex);
        await item.Locator(".tm-pdf-comment-item__menu .tm-dropdown-trigger")
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await card.Locator("[data-testid='pdf-annotator-delete']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await card.Page.WaitForTimeoutAsync(300);
    }

    /// <summary>Saves a screenshot clipped to a card's box (a full-page capture resizes the
    /// viewport, which can re-resolve the Auto layout mid-test — the phase 12 flake).</summary>
    private static async Task SaveCardScreenshotAsync(IPage page, string fileName, ILocator card)
    {
        const float margin = 8;
        var box = await card.BoundingBoxAsync()
            ?? throw new InvalidOperationException("The captured card has no bounding box.");
        await SaveScreenshotAsync(page, fileName, new Clip
        {
            X = Math.Max(0, box.X - margin),
            Y = Math.Max(0, box.Y - margin),
            Width = box.Width + margin * 2,
            Height = box.Height + margin * 2,
        });
    }

    /// <summary>
    /// FZE-15: the toolbar history group — disabled until the first mutation, then the stamp
    /// is undone (gone) and redone (back) through the buttons. Captures
    /// <c>history-group-desktop.png</c> for the UX review.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_StampThenRedo()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = UndoCard(page);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        var undo = card.Locator("[data-testid='pdf-annotator-undo']").First;
        var redo = card.Locator("[data-testid='pdf-annotator-redo']").First;
        await undo.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        StringAssert.Contains(await undo.GetAttributeAsync("aria-disabled") ?? "", "true",
            "nothing to undo before the first mutation");
        StringAssert.Contains(await redo.GetAttributeAsync("aria-disabled") ?? "", "true");

        await ClickToolAsync(card, "pdf-annotator-mode-stamp", "Place stamp");
        await card.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 260, Y = 100 } });
        var stamp = card.Locator("[data-testid='pdf-annotator-stamp']").First;
        await stamp.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await SaveCardScreenshotAsync(page, "history-group-desktop", card);

        await undo.ClickAsync();
        await Assertions.Expect(card.Locator("[data-testid='pdf-annotator-stamp']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        await redo.ClickAsync();
        await card.Locator("[data-testid='pdf-annotator-stamp']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    /// <summary>
    /// FZE-15: deleting every comment of a thread removes it — one undo restores the whole
    /// thread (marker, both comments) from the pre-delete snapshot.
    /// Captures <c>undo-restored-thread.png</c>.
    /// </summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_DeleteThread_RestoresMarkerAndReplies()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = UndoCard(page);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        await ClickToolAsync(card, "pdf-annotator-mode-comment", "Add comment");
        await card.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 120 } });
        var composer = card.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Root note for the undo restore");
        await EnsurePanelVisibleAsync(card);
        await card.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();

        var thread = card.Locator("[data-testid='pdf-annotator-thread']").First;
        await thread.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        // Creating the thread already selects it (its detail is open) - clicking the head
        // again would toggle the detail closed, so open it only when needed.
        if (await card.Locator("[data-testid='pdf-annotator-detail']").CountAsync() == 0)
        {
            await thread.ClickAsync();
        }

        var replyInput = card.Locator("[data-testid='pdf-annotator-reply-input']");
        await replyInput.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await replyInput.FillAsync("First reply");
        await card.Locator("[data-testid='pdf-annotator-reply-submit']").ClickAsync();
        await card.Locator("[data-testid='pdf-annotator-comment']").Nth(1)
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Delete the reply, then the root comment — the thread disappears with its marker.
        await DeleteCommentAsync(card, 1);
        await DeleteCommentAsync(card, 0);
        await Assertions.Expect(card.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await Assertions.Expect(card.Locator("[data-testid='pdf-annotator-marker']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // Two undos (LIFO): the first re-creates the thread from the root-delete snapshot,
        // the second recognizes the replayed reply instead of duplicating it.
        await ClickToolAsync(card, "pdf-annotator-undo", "Undo");
        await card.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await ClickToolAsync(card, "pdf-annotator-undo", "Undo");
        await card.Locator("[data-testid='pdf-annotator-marker']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.AreEqual(2, await card.Locator("[data-testid='pdf-annotator-comment']").CountAsync(),
            "the restored thread replays the root comment and the reply");

        await SaveCardScreenshotAsync(page, "undo-restored-thread", card);
        AssertNoBlazorErrors(handle);
    }

    /// <summary>FZE-15: Ctrl+Z / Ctrl+Shift+Z work from the annotator surface focus and the
    /// aria-live announcer names the undone step.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_KeyboardShortcuts()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = UndoCard(page);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        await ClickToolAsync(card, "pdf-annotator-mode-stamp", "Place stamp");
        var surface = card.Locator("[data-testid='pdf-annotator-surface']").First;
        await surface.ClickAsync(new LocatorClickOptions { Position = new Position { X = 260, Y = 100 } });
        await card.Locator("[data-testid='pdf-annotator-stamp']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        await page.Keyboard.PressAsync("Control+z");
        await Assertions.Expect(card.Locator("[data-testid='pdf-annotator-stamp']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains(
            await card.Locator("[data-testid='pdf-annotator-announcer']").InnerTextAsync(),
            "Undone", "the live region announces the undone step");

        await page.Keyboard.PressAsync("Control+Shift+Z");
        await card.Locator("[data-testid='pdf-annotator-stamp']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // The redo tail is spent again; Ctrl+Z undoes it once more.
        await page.Keyboard.PressAsync("Control+z");
        await Assertions.Expect(card.Locator("[data-testid='pdf-annotator-stamp']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        await page.Keyboard.PressAsync("Control+y");
        await card.Locator("[data-testid='pdf-annotator-stamp']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    /// <summary>FZE-15 edge: inside the composer textarea Ctrl+Z is the NATIVE text undo —
    /// the typed text shrinks and no annotation step runs.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_InTextareaIsNativeTextUndo_Edge()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = UndoCard(page);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        // A stamp first, so the history HAS a step that the textarea undo must not consume.
        await ClickToolAsync(card, "pdf-annotator-mode-stamp", "Place stamp");
        await card.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 260, Y = 100 } });
        await card.Locator("[data-testid='pdf-annotator-stamp']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Open the draft composer and type into the textarea.
        await ClickToolAsync(card, "pdf-annotator-mode-comment", "Add comment");
        await card.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 300 } });
        var composer = card.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.ClickAsync();
        await page.Keyboard.TypeAsync("Hello world");

        await page.Keyboard.PressAsync("Control+z");

        var value = await composer.InputValueAsync();
        Assert.IsTrue(value.Length < "Hello world".Length,
            $"the native text undo must shrink the typed text, got '{value}'");
        Assert.AreEqual(1, await card.Locator("[data-testid='pdf-annotator-stamp']").CountAsync(),
            "the textarea undo must not consume an annotation history step");
        AssertNoBlazorErrors(handle);
    }

    /// <summary>FZE-15 edge (two users): Bob deletes the thread Alice created — Alice's undo
    /// is skipped with the localized conflict hint and the thread stays deleted.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_AfterOtherUserDeleted_Edge()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var alice = page.Locator("[data-testid='pdf-annotator-pane-alice']");
        var bob = page.Locator("[data-testid='pdf-annotator-pane-bob']");
        await alice.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(alice);
        await WaitForSurfaceReadyAsync(bob);

        await ClickToolAsync(alice, "pdf-annotator-mode-comment", "Add comment");
        await alice.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 150, Y = 120 } });
        var composer = alice.Locator("[data-testid='pdf-annotator-new-input']");
        await composer.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await composer.FillAsync("Thread Bob will delete");
        await EnsurePanelVisibleAsync(alice);
        await alice.Locator("[data-testid='pdf-annotator-new-submit']").ClickAsync();
        await alice.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await bob.Locator("[data-testid='pdf-annotator-thread']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });

        // Bob deletes the thread's comment — it disappears from both panes.
        await EnsurePanelVisibleAsync(bob);
        await bob.Locator("[data-testid='pdf-annotator-thread']").First.ClickAsync();
        await bob.Locator("[data-testid='pdf-annotator-comment']").First
            .Locator(".tm-pdf-comment-item__menu .tm-dropdown-trigger")
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await bob.Locator("[data-testid='pdf-annotator-delete']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        // Alice's undo of her create is now moot: skipped with the conflict hint.
        await alice.Locator("[data-testid='pdf-annotator-undo']").First
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });
        StringAssert.Contains(
            await alice.Locator("[data-testid='pdf-annotator-announcer']").InnerTextAsync(),
            "changed in the meantime",
            "the conflict hint names the concurrent change");
        await Assertions.Expect(alice.Locator("[data-testid='pdf-annotator-thread']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        AssertNoBlazorErrors(handle);
    }

    /// <summary>FZE-15: undo/redo works on the Continuous per-page surfaces — a stamp placed
    /// through the page-2 slot disappears on undo and comes back on redo, and the redo's
    /// anchor scroll lands the marker INSIDE the wrap viewport with the page input showing
    /// the anchor page (the F13/F14 fix: the scroll never lands above the anchor page, so
    /// the scroll tracker reports the anchor page instead of fighting the scroll).</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_Continuous_ScrollsToAnchor()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = page.Locator("[data-testid='pdf-annotator-continuous-card']");
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        // To page 2 by scrolling the continuous wrap directly (the page input's blur/Enter
        // commit lifecycle would race the redo's programmatic page set below).
        var wrap = card.Locator(".tm-pdf-viewer__continuous-wrap").First;
        var page2Top = await card.Locator(".tm-pdf-viewer__page[data-page='2']").First
            .EvaluateAsync<double>("el => el.offsetTop");
        await wrap.EvaluateAsync("(el, top) => { el.scrollTop = top; }", page2Top);
        await page.WaitForTimeoutAsync(900);

        await ClickToolAsync(card, "pdf-annotator-mode-stamp", "Place stamp");
        await card.Locator(".tm-pdf-viewer__page-overlay-slot[data-page='2'] [data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 200, Y = 100 }, Timeout = 15000 });
        var stamp = card.Locator(".tm-pdf-viewer__page-overlay-slot[data-page='2'] [data-testid='pdf-annotator-stamp']");
        await stamp.First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });

        // The buttons may overflow into the merged toolbar's More menu (ClickToolAsync
        // handles both paths).
        await ClickToolAsync(card, "pdf-annotator-undo", "Undo");
        await Assertions.Expect(card.Locator("[data-testid='pdf-annotator-stamp']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        StringAssert.Contains(
            await card.Locator("[data-testid='pdf-annotator-announcer']").InnerTextAsync(),
            "Undone", "the live region announces the undone step");

        // The redo restores the thread and scrolls its anchor into view.
        await ClickToolAsync(card, "pdf-annotator-redo", "Redo");
        StringAssert.Contains(
            await card.Locator("[data-testid='pdf-annotator-announcer']").InnerTextAsync(),
            "Redone", "the live region announces the redone step");
        var restoredStamp = card.Locator(".tm-pdf-viewer__page-overlay-slot[data-page='2'] [data-testid='pdf-annotator-stamp']").First;
        await restoredStamp.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000, State = WaitForSelectorState.Attached });

        // FZE-15 review: the anchor page is the tracked page — the toolbar page input shows it.
        var pageInput = card.Locator(".tm-pdf-viewer__page-input").First;
        await Assertions.Expect(pageInput).ToHaveValueAsync("2", new LocatorAssertionsToHaveValueOptions { Timeout = 15000 });

        // …and the marker sits INSIDE the wrap viewport (the scroll never landed above
        // the anchor page, where the previous page would have owned the viewport).
        var wrapBox = await wrap.BoundingBoxAsync()
            ?? throw new InvalidOperationException("the continuous wrap has no bounding box");
        var stampBox = await restoredStamp.BoundingBoxAsync()
            ?? throw new InvalidOperationException("the restored stamp has no bounding box");
        Assert.IsTrue(stampBox.Y >= wrapBox.Y - 1 && stampBox.Y + stampBox.Height <= wrapBox.Y + wrapBox.Height + 1,
            $"the anchor marker must sit inside the wrap viewport (wrap y {wrapBox.Y}..{wrapBox.Y + wrapBox.Height}, " +
            $"stamp y {stampBox.Y}..{stampBox.Y + stampBox.Height})");
        AssertNoBlazorErrors(handle);
    }

    /// <summary>FZE-15 edge: the history is per-session — a reload clears the stack (the undo
    /// button comes back disabled) even though the document may keep its annotations.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_PersistsThroughReload_StackCleared_Edge()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = UndoCard(page);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        await ClickToolAsync(card, "pdf-annotator-mode-stamp", "Place stamp");
        await card.Locator("[data-testid='pdf-annotator-surface']")
            .ClickAsync(new LocatorClickOptions { Position = new Position { X = 260, Y = 100 } });
        await card.Locator("[data-testid='pdf-annotator-stamp']").First
            .WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        Assert.IsNull(await card.Locator("[data-testid='pdf-annotator-undo']").First.GetAttributeAsync("disabled"),
            "the step is undoable before the reload");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        var reloaded = UndoCard(page);
        var undo = reloaded.Locator("[data-testid='pdf-annotator-undo']").First;
        await undo.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        StringAssert.Contains(await undo.GetAttributeAsync("aria-disabled") ?? "", "true",
            "the reload cleared the per-session stack");
        AssertNoBlazorErrors(handle);
    }

    /// <summary>FZE-15: <c>AllowUndo=false</c> (the demo toggle) removes the history group and
    /// the shortcuts keep doing nothing. Captures <c>history-disabled.png</c>.</summary>
    [TestMethod]
    [TestCategory("WASM")]
    public async Task PdfAnnotator_Undo_AllowUndoFalse_HidesGroup_Edge()
    {
        var handle = await OpenAnnotatorPageAsync();
        var page = handle.Page;
        var card = UndoCard(page);
        await card.ScrollIntoViewIfNeededAsync();
        await WaitForSurfaceReadyAsync(card);

        await card.Locator("[data-testid='pdf-annotator-allow-undo'] .tm-toggle-track")
            .ClickAsync(new LocatorClickOptions { Timeout = 15000 });

        await Assertions.Expect(card.Locator("[data-tm-group='history']"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });

        await SaveCardScreenshotAsync(page, "history-disabled", card);
        AssertNoBlazorErrors(handle);
    }
}
