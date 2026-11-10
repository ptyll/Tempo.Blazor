using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan 7ba95ed6, Fáze 5 — the <c>TmMemoryPanel</c> follow-ups: the filter presentation that answers a narrow container
/// (a real select instead of a clipped tray), the undo QUEUE with a window per pending removal, and the field-aware
/// editor that reports only what actually moved.
/// <para>
/// The class is shared with <see cref="TmMemoryPanelTests"/> (one fixture per file, both partial) so the suite stays
/// inside the repository's 1200-line source ceiling.
/// </para>
/// </summary>
public partial class TmMemoryPanelTests
{
    // ── The filter presentation on a narrow container (plan 7ba95ed6, Fáze 5) ──

    /// <summary>
    /// Five segments do not fit a 375 px card: the core <c>TmSegmentedControl</c> answers a narrow container by
    /// scrolling its own tray, which reads as clipped. The panel therefore renders BOTH surfaces and a container query
    /// — never the viewport — decides which one is visible, so the phone gets a real <c>&lt;select&gt;</c>.
    /// </summary>
    [Fact]
    public void TheAutoPresentation_RendersBothSurfaces_AndTheContainerQueryPicksOne()
    {
        var cut = RenderPanel();

        var root = cut.Find("[data-testid='memory-panel']");
        root.GetAttribute("data-filter").Should().Be("auto", "the container query is gated on the mode the host asked for");
        cut.FindAll("[data-testid='memory-status-filter-segmented']").Should().ContainSingle();
        cut.FindAll("[data-testid='memory-status-filter-select']").Should().ContainSingle();
    }

    [Fact]
    public void TheSegmentedPresentation_RendersTheTrayOnly()
    {
        var cut = RenderPanel(filterPresentation: MemoryFilterPresentation.Segmented);

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-filter").Should().Be("segmented");
        cut.FindAll("[data-testid='memory-status-filter-segmented']").Should().ContainSingle();
        cut.FindAll("[data-testid='memory-status-filter-select']").Should().BeEmpty();
    }

    [Fact]
    public void TheSelectPresentation_RendersARealSelect_ThatStillDrivesTheFilter()
    {
        var cut = RenderPanel(filterPresentation: MemoryFilterPresentation.Select);

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-filter").Should().Be("select");
        cut.FindAll("[data-testid='memory-status-filter-segmented']").Should().BeEmpty();

        var select = cut.Find("[data-testid='memory-status-filter-select'] select");
        select.GetAttribute("aria-label").Should().Be("Status");
        cut.FindAll("[data-testid='memory-status-filter-select'] option")
            .Select(option => option.GetAttribute("value"))
            .Should().Equal("all", "active", "paused", "pending-review", "forgotten");

        select.Change("paused");

        Rows(cut).Select(row => row.GetAttribute("data-item-id")).Should().Equal("m-2");
        _statusFilter.Should().Be(MemoryStatusFilter.Paused, "the select is the same VIEW as the segmented tray");
    }

    /// <summary>The select shows the value the reader picked, not the host's original one.</summary>
    [Fact]
    public void TheSelect_ShowsTheCurrentFilter()
    {
        var cut = RenderPanel(statusFilter: MemoryStatusFilter.PendingReview,
            filterPresentation: MemoryFilterPresentation.Select);

        cut.Find("[data-testid='memory-status-filter-select'] select")
            .GetAttribute("value").Should().Be("pending-review");
    }

    // ── A queue of pending removals (plan 7ba95ed6, Fáze 5) ──────────────────

    /// <summary>
    /// Two removals inside one window are two independent promises: each keeps its own undo row and its own countdown,
    /// and the FIRST one to expire is the first the host hears about.
    /// </summary>
    [Fact]
    public void TwoForgetsInQueueMode_KeepTwoUndoRows_AndExpireInOrder()
    {
        var cut = RenderPanel(undoMode: MemoryUndoMode.Queue);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-forget");

        cut.FindAll("[data-testid='memory-undo-row']").Should().HaveCount(2,
            "a queue keeps one undo row per pending removal instead of reporting the previous one");
        _forgetCount.Should().Be(0, "both removals are still undoable");

        cut.FindAll("[data-testid='memory-undo-row']")
            .Select(row => row.GetAttribute("data-item-id"))
            .Should().Equal("m-1", "m-4");
        cut.FindAll("[data-testid='memory-undo-timer']").Select(timer => timer.TextContent)
            .Should().Equal(["3 s", "5 s"], "each row counts down its own window");

        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(3)));

        _forgetCount.Should().Be(1, "the first window closed first");
        _forgottenItems.Select(item => item.Id).Should().Equal("m-1");
        cut.FindAll("[data-testid='memory-undo-row']").Should().ContainSingle();

        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(2)));

        _forgetCount.Should().Be(2);
        _forgottenItems.Select(item => item.Id).Should().Equal("m-1", "m-4");
        cut.FindAll("[data-testid='memory-undo']").Should().BeEmpty();
    }

    /// <summary>Undo reaches the row the reader pressed, not merely the newest pending removal.</summary>
    [Fact]
    public void UndoOnAMiddleRow_RestoresThatMemory_AndLeavesTheOtherWindowAlone()
    {
        var cut = RenderPanel(undoMode: MemoryUndoMode.Queue);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-forget");

        ClickIn(cut, "[data-testid='memory-undo-row'][data-item-id='m-1']", "memory-undo-action");

        cut.Find("[data-testid='memory-item'][data-item-id='m-1']").Should().NotBeNull("the restored row is back");
        cut.FindAll("[data-testid='memory-undo-row']").Select(row => row.GetAttribute("data-item-id"))
            .Should().Equal("m-4");
        _forgetCount.Should().Be(0);

        cut.InvokeAsync(() => _clock.Advance(TimeSpan.FromSeconds(5)));

        string.Join("|", _forgottenItems.Select(item => item.Id)).Should().Be("m-4",
            "the restored memory is never reported, and the other window still closes on its own");
    }

    /// <summary>
    /// The undo row's button leaves the DOM with its row, so the focus would otherwise drop to <c>&lt;body&gt;</c>. The
    /// restored row takes it instead — a keyboard reader with several queued rows never loses their place (WCAG 2.4.3).
    /// </summary>
    [Fact]
    public void UndoMovesTheFocusToTheRestoredRow_InsteadOfDroppingIt()
    {
        var cut = RenderPanel(undoMode: MemoryUndoMode.Queue);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-forget");
        var focusBefore = JSInterop.Invocations[FocusInvocation].Count;

        ClickIn(cut, "[data-testid='memory-undo-row'][data-item-id='m-1']", "memory-undo-action");

        // The button the reader pressed left the DOM with its undo row, so exactly ONE focus call has to reach a real
        // element (never an empty reference, which a browser drops onto <body>).
        var calls = JSInterop.Invocations[FocusInvocation];
        calls.Should().HaveCount(focusBefore + 1, "the control the reader was on no longer exists");
        ((ElementReference)calls[^1].Arguments[0]).Id.Should().NotBeNullOrEmpty(
            "the focus lands on an element rather than dropping to <body>");
        cut.Find("[data-testid='memory-item'][data-item-id='m-1']").GetAttribute("tabindex").Should().Be("-1",
            "the restored row is the component's focus target (the row, not a button a key sequence could re-activate)");
        cut.FindAll("[data-testid='memory-undo-row']").Select(row => row.GetAttribute("data-item-id"))
            .Should().Equal(["m-4"], "only the row the reader pressed left the stack");
    }

    /// <summary>
    /// A stack cannot grow without a bound: the panel shows at most five undo rows, and a sixth removal reports the
    /// OLDEST pending one immediately instead of dropping it off the screen.
    /// </summary>
    [Fact]
    public void AQueueBeyondTheVisibleLimit_ReportsTheOldestRemovalImmediately()
    {
        var many = Enumerable.Range(1, 6)
            .Select(index => new MemoryItem($"q-{index}", "Queue", $"Fact number {index}.") { CreatedAt = Ten })
            .ToArray();
        var cut = RenderPanel(items: many, undoMode: MemoryUndoMode.Queue);

        for (var index = 1; index <= 6; index++)
        {
            ClickIn(cut, $"[data-testid='memory-item'][data-item-id='q-{index}']", "memory-item-forget");
        }

        _forgetCount.Should().Be(1, "the oldest pending removal cannot stay undoable once its row is gone");
        _forgottenItems[0].Id.Should().Be("q-1");
        cut.FindAll("[data-testid='memory-undo-row']").Should().HaveCount(TmMemoryPanel.MaxVisibleUndoRows,
            "the stack is bounded by the number of rows a reader can still act on");
        cut.FindAll("[data-testid='memory-undo-row']").Select(row => row.GetAttribute("data-item-id"))
            .Should().Equal("q-2", "q-3", "q-4", "q-5", "q-6");
    }

    /// <summary>Disposing the panel reports EVERY pending removal, not only the newest one.</summary>
    [Fact]
    public async Task DisposingAQueue_ReportsEveryPendingRemoval()
    {
        var cut = RenderPanel(undoMode: MemoryUndoMode.Queue);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-forget");

        await DisposeComponentsAsync();

        string.Join("|", _forgottenItems.Select(item => item.Id)).Should().Be("m-1|m-4",
            "the page that carried the undo rows is gone, so every confirmed removal must reach the host");
    }

    /// <summary>The default is still ONE window: a second Forget in <c>Single</c> mode reports the first removal.</summary>
    [Fact]
    public void TheSingleUndoMode_KeepsReportingThePreviousRemoval()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-undo-mode").Should().Be("single");

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-forget");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-4']", "memory-item-forget");

        _forgottenItems.Select(item => item.Id).Should().Equal("m-1");
        cut.FindAll("[data-testid='memory-undo-row']").Should().ContainSingle();
    }

    // ── Editing more than the text (plan 7ba95ed6, Fáze 5) ───────────────────

    /// <summary>The default is today's behaviour: the editor edits the TEXT and reports the legacy <c>OnEdit</c>.</summary>
    [Fact]
    public void TheDefaultEditableFields_EditTheTextOnly()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-editable").Should().Be("text");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");

        cut.Find("[data-testid='memory-item-edit-box']").Should().NotBeNull();
        cut.FindAll("[data-testid='memory-item-edit-category']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item-edit-source']").Should().BeEmpty();
    }

    /// <summary>
    /// A host that allows the CATEGORY gets a field beside the text, and the commit reports ONE record naming the fields
    /// that changed — never a bare item a host would have to diff itself.
    /// </summary>
    [Fact]
    public void AnEditableCategory_IsReportedThroughOnEditItem()
    {
        var cut = RenderPanel(editableFields: MemoryEditableFields.Text | MemoryEditableFields.Category);

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-editable").Should().Be("text category");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");

        var category = cut.Find("[data-testid='memory-item-edit-category']");
        category.GetAttribute("aria-label").Should().BeNull(
            "the wrapping <label> is the single source of the field's accessible name");
        category.ParentElement!.TextContent.Trim().Should().Be("Category");
        category.Input("People");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-save");

        _editItem.Should().NotBeNull("a field-aware host is told through OnEditItem");
        _editItem!.Id.Should().Be("m-1");
        _editItem.Text.Should().Be("Prefers metric units and 24-hour time.");
        _editItem.Category.Should().Be("People");
        _editItem.Fields.Should().Be(MemoryEditableFields.Category,
            "only the category moved, so a host can apply just that change");
        _edited.Should().BeNull("OnEdit stays the text-only callback and is not fired for a category edit");

        cut.Find("[data-testid='memory-group'][data-category='People'] [data-testid='memory-item'][data-item-id='m-1']")
            .Should().NotBeNull("the panel regroups the row from its own copy");
    }

    /// <summary>Text and source in one commit are one record with both flags set.</summary>
    [Fact]
    public void ATextAndSourceEdit_ReportsBothFields()
    {
        var cut = RenderPanel(editableFields: MemoryEditableFields.Text | MemoryEditableFields.Source);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        cut.Find("[data-testid='memory-item-edit-box']").Input("Prefers metric units.");
        cut.Find("[data-testid='memory-item-edit-source']").Input("Call transcript");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-save");

        _editItem!.Text.Should().Be("Prefers metric units.");
        _editItem.Source.Should().Be("Call transcript");
        _editItem.Fields.Should().Be(MemoryEditableFields.Text | MemoryEditableFields.Source);
        cut.Find("[data-testid='memory-item'][data-item-id='m-1'] [data-testid='memory-item-source']").TextContent
            .Should().Be("From: Call transcript");
    }

    /// <summary>Enter commits from the single-line field too, and Escape rolls the whole draft back.</summary>
    [Fact]
    public void TheFieldEditors_CommitOnEnter_AndRollBackOnEscape()
    {
        var cut = RenderPanel(editableFields: MemoryEditableFields.Category);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        var category = cut.Find("[data-testid='memory-item-edit-category']");
        category.Input("Secret");
        category.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[data-testid='memory-item-edit-category']").Should().BeEmpty("Escape closes the editor");
        _editItem.Should().BeNull();
        cut.Find("[data-testid='memory-item'][data-item-id='m-1']").GetAttribute("data-category")
            .Should().Be("Preferences", "a rolled-back draft never reaches the row");

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        var again = cut.Find("[data-testid='memory-item-edit-category']");
        again.Input("People");
        again.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        _editItem!.Category.Should().Be("People");
        _editItem.Fields.Should().Be(MemoryEditableFields.Category);
    }

    /// <summary>With no editable field there is nothing to edit, so no Edit button is offered.</summary>
    [Fact]
    public void NoEditableField_OffersNoEditButton()
    {
        var cut = RenderPanel(editableFields: MemoryEditableFields.None);

        cut.Find("[data-testid='memory-panel']").GetAttribute("data-editable").Should().Be("none");
        cut.FindAll("[data-testid='memory-item-edit']").Should().BeEmpty();
        cut.FindAll("[data-testid='memory-item-forget']").Should().NotBeEmpty("the other actions stay");
    }

    /// <summary>An unchanged field is not reported: a commit that moved nothing closes without a callback.</summary>
    [Fact]
    public void ACommitThatChangedNothing_ReportsNothing()
    {
        var cut = RenderPanel(editableFields: MemoryEditableFields.Category);

        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-edit");
        ClickIn(cut, "[data-testid='memory-item'][data-item-id='m-1']", "memory-item-save");

        _editItem.Should().BeNull();
        _edited.Should().BeNull();
        cut.FindAll("[data-testid='memory-item-edit-box']").Should().BeEmpty();
    }
}
