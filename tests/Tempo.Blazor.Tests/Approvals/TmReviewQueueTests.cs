using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Models.Approvals;
using Tempo.Blazor.Abstractions.Models.Reviews;
using Tempo.Blazor.Components.Approvals;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Phase 3 of plan 10b3966f — <c>TmReviewQueue&lt;TItem&gt;</c>, the queue of items a human has to
/// decide on. The queue sorts by risk then priority, shows every item as a selectable row
/// (<c>listbox</c>, or a <c>grid</c> once the rows carry buttons) or one at a time in Focus mode with
/// a localized "Item 3 of 4" position, raises A/E/R/S through buttons and single-key shortcuts, and
/// resolves an item only when the host's asynchronous <c>OnAction</c> completes without throwing.
/// Exactly one polite live region announces the result and the remaining count; a handled item
/// collapses into a result row (CSS height transition, no JS) and leaves the pending set.
/// <para>
/// The item type is a small record so the tests can assert on identity, risk and priority without a
/// domain model.
/// </para>
/// </summary>
public class TmReviewQueueTests : LocalizationTestBase
{
    private static readonly string FocusInvocation = "Blazor._internal.domWrapper.focus";

    /// <summary>One reviewable item: identity, heading, risk, priority and a subtitle.</summary>
    private sealed record ReviewItem(
        string Id,
        string Title,
        ApprovalRisk Risk = ApprovalRisk.Low,
        int Priority = 0,
        string? Subtitle = null);

    /// <summary>
    /// Four items whose risk and priority orders DISAGREE: risk-first puts "Invoice #2" on top,
    /// priority-first puts "Invoice #3" (priority 5) first. A test that passes on an unsorted list
    /// would prove nothing, so the input order is deliberately different from both.
    /// </summary>
    private static ReviewItem[] DefaultItems() =>
    [
        new("a", "Invoice #1", ApprovalRisk.Low, 0, "Finance"),
        new("b", "Invoice #2", ApprovalRisk.Critical, 0, "Finance"),
        new("c", "Invoice #3", ApprovalRisk.Medium, 5, "Legal"),
        new("d", "Invoice #4", ApprovalRisk.High, 1, "Legal"),
    ];

    private static IReadOnlyList<string> RowTitles(IRenderedComponent<TmReviewQueue<ReviewItem>> cut)
        => cut.FindAll("[data-testid='review-item']")
            .Select(row => row.QuerySelector("[data-testid='review-item-title']")!.TextContent.Trim())
            .ToList();

    private static IReadOnlyList<string> RowIds(IRenderedComponent<TmReviewQueue<ReviewItem>> cut)
        => cut.FindAll("[data-testid='review-item']")
            .Select(row => row.GetAttribute("data-item-id")!)
            .ToList();

    private static IElement ActiveRow(IRenderedComponent<TmReviewQueue<ReviewItem>> cut)
        => cut.FindAll("[data-testid='review-item']").Single(row => row.GetAttribute("aria-selected") == "true");

    private static IReadOnlyList<IElement> RowsMarkedTabStop(IRenderedComponent<TmReviewQueue<ReviewItem>> cut)
        => cut.FindAll("[data-testid='review-item']").Where(row => row.GetAttribute("tabindex") == "0").ToList();

    /// <summary>
    /// The element that owns the queue's navigation keys: the list body or, in Focus mode, the
    /// Focus view. The queue root is NOT one — the tab strip lives there and keeps its own keys.
    /// </summary>
    private static IElement KeyboardSurface(IRenderedComponent<TmReviewQueue<ReviewItem>> cut)
        => cut.FindAll("[data-testid='review-list'], [data-testid='review-focus']").Single();

    /// <summary>Whether the element sits in an <c>inert</c> half of a collapsing row.</summary>
    private static bool IsHidden(IElement element)
    {
        for (var node = element; node is not null; node = node.ParentElement)
        {
            if (node.HasAttribute("inert"))
            {
                return true;
            }
        }

        return false;
    }

    private static IElement FindVisible(IRenderedComponent<TmReviewQueue<ReviewItem>> cut, string testId)
        => cut.FindAll($"[data-testid='{testId}']").Single(element => !IsHidden(element));

    private IRenderedComponent<TmReviewQueue<ReviewItem>> RenderQueue(
        ReviewQueueMode mode = ReviewQueueMode.List,
        IReadOnlyList<ReviewItem>? items = null,
        Action<ComponentParameterCollectionBuilder<TmReviewQueue<ReviewItem>>>? configure = null,
        TmLayoutMode layout = TmLayoutMode.Desktop)
        => Render<TmReviewQueue<ReviewItem>>(parameters =>
        {
            parameters
                .Add(queue => queue.Items, items ?? DefaultItems())
                .Add(queue => queue.IdSelector, item => item.Id)
                .Add(queue => queue.TitleSelector, item => item.Title)
                .Add(queue => queue.SubtitleSelector, item => item.Subtitle)
                .Add(queue => queue.RiskSelector, item => item.Risk)
                .Add(queue => queue.PrioritySelector, item => item.Priority)
                .Add(queue => queue.Mode, mode)
                .Add(queue => queue.LayoutMode, layout);
            configure?.Invoke(parameters);
        });

    // ── Structure / ARIA pattern ─────────────────────────────────────────────

    [Fact]
    public void RowsWithoutButtonsFormAListboxWithOneSelectedOption()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.Actions, ReviewQueueActions.None));

        var list = cut.Find("[data-testid='review-list']");
        list.GetAttribute("role").Should().Be("listbox",
            "APG: options must not contain interactive controls, so a queue whose rows carry no "
            + "buttons is a listbox");
        list.GetAttribute("aria-label").Should().Be("Items to review");

        var rows = cut.FindAll("[data-testid='review-item']");
        rows.Should().HaveCount(4);
        rows.Should().OnlyContain(row => row.GetAttribute("role") == "option");
        cut.FindAll("[aria-selected='true']").Should().ContainSingle();
        RowsMarkedTabStop(cut).Should().ContainSingle("a listbox is one tab stop with a roving tabindex");
    }

    [Fact]
    public void RowsWithButtonsFormAGridBecauseAnOptionMayNotContainButtons()
    {
        var cut = RenderQueue();

        var list = cut.Find("[data-testid='review-list']");
        list.GetAttribute("role").Should().Be("grid",
            "APG: a row of action buttons is not allowed inside a role=option, so the same queue "
            + "switches to the grid pattern instead of shipping invalid ARIA");

        var rows = cut.FindAll("[data-testid='review-item']");
        rows.Should().OnlyContain(row => row.GetAttribute("role") == "row");
        rows.Should().OnlyContain(row => row.QuerySelector("[role='gridcell']") != null);
    }

    [Fact]
    public void RowsShowTheTitleTheSubtitleAndTheRiskAsWords()
    {
        var cut = RenderQueue();

        RowIds(cut).Should().Equal(["b", "d", "c", "a"], "risk first: Critical, High, Medium, Low");
        var first = cut.FindAll("[data-testid='review-item']")[0];
        first.QuerySelector("[data-testid='review-item-title']")!.TextContent.Trim()
            .Should().Be("Invoice #2");
        first.QuerySelector("[data-testid='review-item-subtitle']")!.TextContent.Trim()
            .Should().Be("Finance");
        first.QuerySelector("[data-testid='review-item-risk']")!.TextContent.Trim()
            .Should().Be("Critical risk", "the risk is never colour alone (WCAG 1.4.1)");
        first.QuerySelector("[data-testid='review-item-risk'] .tm-badge").Should().NotBeNull();
    }

    [Fact]
    public void TheItemTemplateRendersInsideTheRow()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.ItemTemplate, item => builder =>
                builder.AddMarkupContent(0, $"<span class='review-custom'>{item.Title} preview</span>")));

        cut.FindAll("[data-testid='review-item-content']").Should().HaveCount(4);
        cut.Find("[data-testid='review-item-content']").TextContent.Trim()
            .Should().Be("Invoice #2 preview");
    }

    // ── Sorting ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultOrderIsRiskThenPriority()
    {
        var cut = RenderQueue();

        RowTitles(cut).Should().Equal("Invoice #2", "Invoice #4", "Invoice #3", "Invoice #1");
    }

    [Theory]
    [InlineData(ReviewQueueSort.PriorityThenRisk, new[] { "Invoice #3", "Invoice #4", "Invoice #2", "Invoice #1" })]
    [InlineData(ReviewQueueSort.InputOrder, new[] { "Invoice #1", "Invoice #2", "Invoice #3", "Invoice #4" })]
    public void TheSortModeDecidesTheOrder(ReviewQueueSort sort, string[] expected)
    {
        var cut = RenderQueue(configure: parameters => parameters.Add(queue => queue.SortMode, sort));

        RowTitles(cut).Should().Equal(expected);
    }

    // ── Selection / navigation ───────────────────────────────────────────────

    [Fact]
    public async Task ClickingARowSelectsItAndRaisesTheSelection()
    {
        string? selected = null;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.ActiveItemIdChanged, id => selected = id));

        await cut.FindAll("[data-testid='review-item']")[2].ClickAsync(new MouseEventArgs());

        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("c");
        selected.Should().Be("c");
        RowsMarkedTabStop(cut).Should().ContainSingle()
            .Which.GetAttribute("data-item-id").Should().Be("c");
    }

    [Fact]
    public async Task ArrowDownMovesTheSelectionAndFocusFollowsIt()
    {
        var cut = RenderQueue();
        var expectedFocus = cut.FindAll("[data-testid='review-item']")[1].GetAttribute("blazor:elementReference");

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });

        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("d");
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(expectedFocus,
            "a roving tabindex that leaves the real DOM focus behind is only half a roving tabindex");
    }

    [Fact]
    public async Task FocusFollowsTheSelectionIntoView()
    {
        var cut = RenderQueue();

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "End" });

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        focus.Arguments[1].Should().Be(false,
            "preventScroll:true moves focus to a row that can sit outside the viewport (WCAG 2.4.7) — "
            + "a focus ring nobody can see is not a focus move");
    }

    [Theory]
    [InlineData("j", "c")]
    [InlineData("k", "b")]
    public async Task TheLetterNavigationKeysMoveTheSelection(string key, string expectedId)
    {
        var cut = RenderQueue();

        // Put the selection on the second row first, so K has somewhere to go.
        await cut.FindAll("[data-testid='review-item']")[1].ClickAsync(new MouseEventArgs());
        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = key });

        ActiveRow(cut).GetAttribute("data-item-id").Should().Be(expectedId);
    }

    [Fact]
    public async Task NavigationStopsAtTheEndsInsteadOfWrapping()
    {
        var cut = RenderQueue();

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("b", "the first row has nowhere above it");

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("a");

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("a", "the last row has nowhere below it");

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("b");
    }

    // ── Tabs ─────────────────────────────────────────────────────────────────

    private static ReviewQueueTab[] Tabs() =>
    [
        new() { Id = "open", Title = "Open", Count = 4 },
        new() { Id = "mine", Title = "Mine", Count = 0 },
    ];

    [Fact]
    public void TabsRenderWithTheirCounts()
    {
        var cut = RenderQueue(configure: parameters => parameters.Add(queue => queue.Tabs, Tabs()));

        var tabButtons = cut.FindAll(".tm-tab");
        tabButtons.Select(tab => tab.QuerySelector(".tm-tab__label")!.TextContent.Trim())
            .Should().Equal("Open", "Mine");
        tabButtons.Select(tab => tab.QuerySelector(".tm-tab__badge")!.TextContent.Trim())
            .Should().Equal("4", "0");
        tabButtons[0].GetAttribute("aria-selected").Should().Be("true",
            "the first tab is the fallback selection");
        cut.Find("[role='tablist']").GetAttribute("aria-label").Should().Be("Review tabs");
    }

    [Fact]
    public async Task ClickingATabRaisesTheTabIdAndTheHostOwnsTheFiltering()
    {
        string? raised = null;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.Tabs, Tabs())
            .Add(queue => queue.ActiveTabId, "open")
            .Add(queue => queue.ActiveTabIdChanged, id => raised = id));

        await cut.FindAll(".tm-tab")[1].ClickAsync(new MouseEventArgs());

        raised.Should().Be("mine");
        RowIds(cut).Should().HaveCount(4,
            "the queue never guesses the host's predicate — it keeps rendering Items until the host "
            + "passes a filtered list back");
    }

    [Fact]
    public void TheBoundActiveTabIsTheSelectedTab()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.Tabs, Tabs())
            .Add(queue => queue.ActiveTabId, "mine"));

        cut.FindAll(".tm-tab")[1].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public async Task KeysPressedOnTheTabStripNeverMoveTheQueue()
    {
        var calls = 0;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.Tabs, Tabs())
            .Add(queue => queue.OnAction, _ =>
            {
                calls++;
                return Task.CompletedTask;
            }));

        // The tab strip is TmTabs' own keyboard surface (its tablist owns Home/End); key presses
        // bubble from the tab button to the queue root, which must not act on them.
        await cut.FindAll("[role='tab']")[0].KeyDownAsync(new KeyboardEventArgs { Key = "End" });
        await cut.FindAll("[role='tab']")[0].KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        cut.Instance.ActiveItemKey.Should().Be("b",
            "End belonged to the tab strip — jumping the queue to its last item is WCAG 2.1.1 noise");
        calls.Should().Be(0,
            "'a' on a focused tab must not approve the active item the reviewer never focused");
        cut.FindAll(".tm-tab")[1].GetAttribute("aria-selected").Should().Be("true",
            "the tab strip DID handle the key, which is what proves the two handlers are separated");
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    private static ReviewQueueActions SingleAction(ReviewQueueAction action) => action switch
    {
        ReviewQueueAction.Approve => ReviewQueueActions.Approve,
        ReviewQueueAction.Edit => ReviewQueueActions.Edit,
        ReviewQueueAction.Reject => ReviewQueueActions.Reject,
        _ => ReviewQueueActions.Skip,
    };

    [Theory]
    [InlineData(ReviewQueueAction.Approve, "review-approve", "a")]
    [InlineData(ReviewQueueAction.Edit, "review-edit", "e")]
    [InlineData(ReviewQueueAction.Reject, "review-reject", "r")]
    [InlineData(ReviewQueueAction.Skip, "review-skip", "s")]
    public async Task EveryBuiltInActionButtonRaisesItsVerb(
        ReviewQueueAction action, string testId, string shortcut)
    {
        ReviewQueueActionArgs<ReviewItem>? raised = null;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.Actions, SingleAction(action))
            .Add(queue => queue.OnAction, args =>
            {
                raised = args;
                return Task.CompletedTask;
            }));

        var row = cut.FindAll("[data-testid='review-item']")[0];
        var button = row.QuerySelector($"[data-testid='{testId}']")!;
        button.GetAttribute("aria-keyshortcuts").Should().Be(shortcut.ToUpperInvariant());
        await button.ClickAsync(new MouseEventArgs());

        raised.Should().NotBeNull();
        raised!.Action.Should().Be(action);
        raised.Item.Id.Should().Be("b", "the row's own button acts on the row's own item");
        raised.AvailableActions.Should().Be(SingleAction(action));
    }

    [Theory]
    [InlineData(ReviewQueueActions.None, 0)]
    [InlineData(ReviewQueueActions.Approve, 1)]
    [InlineData(ReviewQueueActions.Decisions, 2)]
    [InlineData(ReviewQueueActions.All, 4)]
    public void TheActionFlagsDecideWhichButtonsExist(ReviewQueueActions actions, int expectedCount)
    {
        var cut = RenderQueue(configure: parameters => parameters.Add(queue => queue.Actions, actions));

        // Quick-actions are absent, so the only buttons in a row are the built-in verbs.
        cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelectorAll("[data-testid^='review-approve'], [data-testid^='review-edit'], "
                + "[data-testid^='review-reject'], [data-testid^='review-skip']")
            .Should().HaveCount(expectedCount);
    }

    [Fact]
    public void RepeatedRowButtonsCarryTheItemInTheirAccessibleName()
    {
        var cut = RenderQueue();

        var approve = cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-approve']")!;

        approve.GetAttribute("aria-label").Should().Be("Approve: Invoice #2",
            "a screen reader's button list would otherwise read four identical 'Approve' entries "
            + "(WCAG 2.4.6)");
        approve.TextContent.Trim().Should().Be("Approve", "the visible verb stays inside the name (2.5.3)");
    }

    [Theory]
    [InlineData("a", ReviewQueueAction.Approve)]
    [InlineData("e", ReviewQueueAction.Edit)]
    [InlineData("r", ReviewQueueAction.Reject)]
    [InlineData("s", ReviewQueueAction.Skip)]
    public async Task TheSingleKeyShortcutsRaiseTheActions(string key, ReviewQueueAction expected)
    {
        ReviewQueueAction? raised = null;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, args =>
            {
                raised = args.Action;
                return Task.CompletedTask;
            }));

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = key });

        raised.Should().Be(expected);
    }

    [Fact]
    public async Task TheShortcutsCanBeTurnedOff()
    {
        var calls = 0;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.EnableShortcuts, false)
            .Add(queue => queue.OnAction, _ =>
            {
                calls++;
                return Task.CompletedTask;
            }));

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        calls.Should().Be(0, "WCAG 2.1.4: single-character shortcuts must be switchable off");
        cut.FindAll("[aria-keyshortcuts]").Should().BeEmpty();
        cut.FindAll("[data-testid='review-hint']").Should().BeEmpty();

        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("d",
            "arrow navigation is the widget's own keyboard contract, not a shortcut");
    }

    [Fact]
    public async Task ATextEntryInsideTheDetailNeverFiresAShortcut()
    {
        var calls = 0;
        var cut = RenderQueue(ReviewQueueMode.Focus, configure: parameters => parameters
            .Add(queue => queue.DetailTemplate, item => builder =>
            {
                // A host template may fence its own keys with @onkeydown:stopPropagation (the same
                // rule as TmDataTable's CellTemplate). The queue fences the DETAIL slot too, so this
                // is defence in depth rather than the only line of defence.
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "data-testid", "detail-note");
                builder.AddAttribute(2, "onkeydown",
                    EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));
                builder.AddEventStopPropagationAttribute(3, "onkeydown", true);
                builder.CloseElement();
            })
            .Add(queue => queue.OnAction, _ =>
            {
                calls++;
                return Task.CompletedTask;
            }));

        var input = cut.Find("[data-testid='detail-note']");

        // The field handles its own keydown and the fence keeps it from bubbling: without
        // @onkeydown:stopPropagation the same key would reach the queue and approve the item.
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        calls.Should().Be(0, "typing 'a' into a field must never approve the review");

        // The queue's own keys still work from its chrome — the fence is on the field, not the queue.
        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "a" });
        calls.Should().Be(1);
    }

    [Fact]
    public async Task ArrowKeysInsideADetailTextFieldNeverMoveTheSelection()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus, configure: parameters => parameters
            .Add(queue => queue.DetailTemplate, _ => builder =>
            {
                // Deliberately UNFENCED by the host: Home/End/arrows must still belong to the field
                // because the queue fences the detail slot itself. The field carries its own key
                // handler (as a real editor field does) so bUnit can dispatch to it at all — bUnit
                // refuses to trigger an event on an element with no handler of its own.
                builder.OpenElement(0, "textarea");
                builder.AddAttribute(1, "data-testid", "detail-notes");
                builder.AddAttribute(2, "onkeydown",
                    EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));
                builder.CloseElement();
            }));

        await cut.Find("[data-testid='detail-notes']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await cut.Find("[data-testid='detail-notes']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "End" });

        cut.Instance.ActiveItemKey.Should().Be("b",
            "a textarea's Home/End/arrows move the caret; the queue must not consume them");

        // The Focus card itself is still a keyboard surface.
        await cut.Find("[data-testid='review-focus']")
            .KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Instance.ActiveItemKey.Should().Be("d");
    }

    [Fact]
    public async Task ShortcutsAndArrowsInsideAnItemTemplateFieldNeverReachTheQueue()
    {
        var calls = 0;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.ItemTemplate, _ => builder =>
            {
                builder.OpenElement(0, "input");
                builder.AddAttribute(1, "data-testid", "item-note");
                builder.AddAttribute(2, "onkeydown",
                    EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }));
                builder.CloseElement();
            })
            .Add(queue => queue.OnAction, _ =>
            {
                calls++;
                return Task.CompletedTask;
            }));

        var input = cut.Find("[data-testid='item-note']");
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "ArrowDown" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "Home" });
        await input.KeyDownAsync(new KeyboardEventArgs { Key = "a" });

        cut.Instance.ActiveItemKey.Should().Be("b");
        calls.Should().Be(0);
    }

    [Fact]
    public async Task WhileAnActionRunsTheButtonsAreDisabledAndTheQueueIsBusy()
    {
        // A real asynchronous gap (not an awaited-from-the-test gate): bUnit flushes the busy render
        // while the handler is suspended, which is exactly the frame the user sees mid-save. The gap
        // is generous because the busy frame must outlive the assertions below on a loaded machine —
        // with 150 ms the save could finish between WaitForState and the disabled check (the buttons
        // are re-enabled by the completion render and the test failed intermittently).
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, async _ => await Task.Delay(1500)));

        var click = cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-approve']")!
            .ClickAsync(new MouseEventArgs());

        cut.WaitForState(
            () => cut.Find(".tm-review-queue").GetAttribute("aria-busy") == "true",
            TimeSpan.FromSeconds(5));

        cut.Instance.IsBusy.Should().BeTrue();
        cut.FindAll("[data-testid='review-item'] [data-testid='review-approve']")
            .Should().OnlyContain(button => button.HasAttribute("disabled"),
                "a second click while the save runs would raise the decision twice");

        await click;

        cut.Find(".tm-review-queue").GetAttribute("aria-busy").Should().BeNull("the save finished");
        cut.FindAll("[data-testid='review-item']")[0].GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public async Task AFailingActionShowsTheErrorAndLeavesTheItemPending()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => throw new InvalidOperationException("boom")));

        await cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-approve']")!
            .ClickAsync(new MouseEventArgs());

        cut.Find(".tm-review-queue").GetAttribute("aria-busy").Should().BeNull();
        cut.Find("[data-testid='review-error']").TextContent.Trim()
            .Should().Be("The action could not be saved. Try again.");
        cut.Find("[data-testid='review-error'] [role='group']").Should().NotBeNull(
            "the alert renders as a group, never as a second live region");
        cut.Find("[data-testid='review-announcements']").TextContent.Trim()
            .Should().Be("The action could not be saved. Try again.");
        RowIds(cut).Should().HaveCount(4, "a failed save changes nothing");
        cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-approve']")!.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task SkipMovesOnWithoutResolvingTheItem()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-skip']")!
            .ClickAsync(new MouseEventArgs());

        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("d", "skip advances to the next item");
        cut.FindAll("[data-testid='review-item']").Should().HaveCount(4, "a skipped item stays in the queue");
        RowIds(cut).Should().Equal(["b", "d", "c", "a"]);
        cut.FindAll("[data-testid='review-item-title']").Where(title => !IsHidden(title))
            .Should().HaveCount(4, "nothing collapsed");
    }

    [Fact]
    public async Task AutoAdvanceMovesTheSelectionToTheNextPendingItem()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-approve']")!
            .ClickAsync(new MouseEventArgs());

        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("d");
        cut.FindAll("[data-testid='review-item']").Should().HaveCount(4,
            "a handled item collapses into a result row instead of disappearing");
        cut.FindAll("[data-testid='review-item']")[0].GetAttribute("aria-disabled").Should().Be("true");
        cut.FindAll("[data-testid='review-item']")[0].GetAttribute("aria-selected").Should().Be("false");
    }

    [Fact]
    public async Task WithoutAutoAdvanceTheHandledItemStaysSelectedUntilTheUserMovesOn()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus, configure: parameters => parameters
            .Add(queue => queue.AutoAdvance, false)
            .Add(queue => queue.DetailTemplate, item => builder =>
                builder.AddMarkupContent(0, $"<p>{item.Title} detail</p>"))
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.Find("[data-testid='review-approve']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #2",
            "without auto-advance the reviewer decides when to move on");
        cut.Find("[data-testid='review-handled']").TextContent.Trim().Should().Be("Approved");
        cut.FindAll("[data-testid='review-approve']").Should().BeEmpty("a handled item has no actions");

        await cut.Find("[data-testid='review-next-item']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #4");
        cut.FindAll("[data-testid='review-handled']").Should().BeEmpty("the next item is still pending");
    }

    [Fact]
    public async Task TheAnnouncementNamesTheResultAndTheRemainingCount()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-reject']")!
            .ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='review-announcements']").TextContent.Trim()
            .Should().Be("Rejected: Invoice #2. Remaining: 3.");
    }

    [Fact]
    public void TheQueueHasExactlyOneLiveRegion()
    {
        var cut = RenderQueue();

        cut.FindAll("[aria-live]").Should().ContainSingle()
            .Which.GetAttribute("data-testid").Should().Be("review-announcements");
        cut.Find("[data-testid='review-announcements']").GetAttribute("role").Should().Be("status");
        cut.Find("[data-testid='review-announcements']").GetAttribute("aria-live").Should().Be("polite");
    }

    [Fact]
    public async Task AHandledRowCollapsesIntoAResultLabelAndLeavesTheNavigation()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-edit']")!
            .ClickAsync(new MouseEventArgs());

        var handled = cut.FindAll("[data-testid='review-item']")[0];
        handled.QuerySelector("[data-testid='review-item-result-label']")!.TextContent.Trim()
            .Should().Be("Edited");
        IsHidden(handled.QuerySelector("[data-testid='review-item-title']")!).Should().BeTrue(
            "the handled half is inert, so it leaves the tab order and the accessibility tree");
        handled.QuerySelector("[data-testid='review-item-result-label']")!.HasAttribute("inert")
            .Should().BeFalse();

        // Navigation skips the handled row: from the new selection, ArrowUp goes to nothing because
        // the selection already sits on the first PENDING row.
        await KeyboardSurface(cut).KeyDownAsync(new KeyboardEventArgs { Key = "ArrowUp" });
        ActiveRow(cut).GetAttribute("data-item-id").Should().Be("d");
    }

    [Fact]
    public async Task ClearHandledRestoresEveryItem()
    {
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.FindAll("[data-testid='review-item']")[0]
            .QuerySelector("[data-testid='review-approve']")!
            .ClickAsync(new MouseEventArgs());

        cut.Instance.HandledItemIds.Should().BeEquivalentTo(["b"]);

        await cut.InvokeAsync(() => cut.Instance.ClearHandledAsync());

        cut.Instance.HandledItemIds.Should().BeEmpty();
        cut.FindAll("[data-testid='review-item']").Should().OnlyContain(row => row.GetAttribute("aria-disabled") == null);
    }

    // ── Quick actions (decision inbox) ───────────────────────────────────────

    private static ReviewQueueQuickAction[] QuickActions() =>
    [
        new() { Id = "file", Label = "Approve & file", Variant = ReviewQueueQuickActionVariant.Primary },
        new() { Id = "return", Label = "Send back", Variant = ReviewQueueQuickActionVariant.Danger },
    ];

    [Fact]
    public async Task QuickActionsRenderHostButtonsAndRaiseTheirOwnId()
    {
        ReviewQueueActionArgs<ReviewItem>? raised = null;
        var cut = RenderQueue(configure: parameters => parameters
            .Add(queue => queue.Actions, ReviewQueueActions.None)
            .Add(queue => queue.QuickActions, QuickActions())
            .Add(queue => queue.OnAction, args =>
            {
                raised = args;
                return Task.CompletedTask;
            }));

        cut.Find("[data-testid='review-list']").GetAttribute("role").Should().Be("grid",
            "custom row buttons are still buttons — the row pattern follows the content");

        var row = cut.FindAll("[data-testid='review-item']")[0];
        var buttons = row.QuerySelectorAll("[data-testid^='review-quick-action']");
        buttons.Select(button => button.TextContent.Trim())
            .Should().Equal("Approve & file", "Send back");

        await buttons[0].ClickAsync(new MouseEventArgs());

        raised.Should().NotBeNull();
        raised!.Action.Should().Be(ReviewQueueAction.QuickAction);
        raised.QuickActionId.Should().Be("file");
        raised.Item.Id.Should().Be("b");
        cut.Find("[data-testid='review-announcements']").TextContent.Trim()
            .Should().Be("Approve & file: Invoice #2. Remaining: 3.");
        cut.FindAll("[data-testid='review-item']")[0].GetAttribute("aria-disabled").Should().Be("true",
            "a quick action resolves the item like any other decision");
    }

    // ── Focus mode ───────────────────────────────────────────────────────────

    [Fact]
    public void FocusModeShowsTheDetailThePositionAndTheNavigation()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus, configure: parameters => parameters
            .Add(queue => queue.DetailTemplate, item => builder =>
                builder.AddMarkupContent(0, $"<p data-testid='custom-detail'>{item.Title} body</p>")));

        cut.Find("[data-testid='review-position']").TextContent.Trim().Should().Be("Item 1 of 4");
        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #2");
        cut.Find("[data-testid='review-detail'] [data-testid='custom-detail']").TextContent.Trim()
            .Should().Be("Invoice #2 body");
        cut.Find("[data-testid='review-prev']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='review-next']").HasAttribute("disabled").Should().BeFalse();
        cut.FindAll("[data-testid='review-item']").Should().BeEmpty("focus mode shows one item, not a list");
    }

    [Fact]
    public async Task FocusModeNavigationMovesBetweenPendingItems()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus);

        await cut.Find("[data-testid='review-next']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='review-position']").TextContent.Trim().Should().Be("Item 2 of 4");
        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #4");

        await cut.Find("[data-testid='review-prev']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #2");

        await cut.Find("[data-testid='review-next']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-testid='review-next']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-testid='review-next']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='review-position']").TextContent.Trim().Should().Be("Item 4 of 4");
        cut.Find("[data-testid='review-next']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public async Task FocusModeAnnouncesAndAdvancesAfterAnApproval()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus, configure: parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        var detailId = cut.Find("[data-testid='review-focus']").GetAttribute("blazor:elementReference");
        await cut.Find("[data-testid='review-approve']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='review-position']").TextContent.Trim().Should().Be("Item 1 of 3",
            "the handled item leaves the pending sequence");
        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #4");
        cut.Find("[data-testid='review-announcements']").TextContent.Trim()
            .Should().Be("Approved: Invoice #2. Remaining: 3.");

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(detailId,
            "focus moves into the detail of the next item instead of falling back to <body>");
        focus.Arguments[1].Should().Be(false,
            "the Focus view is scrolled into view: a detail focused off screen is WCAG 2.4.7");
    }

    [Fact]
    public void TheNavigationButtonsAdvertiseTheirShortcuts()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus);

        cut.Find("[data-testid='review-prev']").GetAttribute("aria-keyshortcuts").Should().Be("K",
            "the A/E/R/S buttons advertise theirs; J/K must be consistent (and discoverable)");
        cut.Find("[data-testid='review-next']").GetAttribute("aria-keyshortcuts").Should().Be("J");

        var quiet = RenderQueue(ReviewQueueMode.Focus, configure: parameters => parameters
            .Add(queue => queue.EnableShortcuts, false));

        quiet.Find("[data-testid='review-prev']").GetAttribute("aria-keyshortcuts").Should().BeNull();
        quiet.Find("[data-testid='review-next']").GetAttribute("aria-keyshortcuts").Should().BeNull();
    }

    [Fact]
    public async Task FocusModeShowsTheAllReviewedStateWhenNothingIsPending()
    {
        ReviewItem[] items = [new("only", "Invoice #9", ApprovalRisk.High)];
        var cut = RenderQueue(ReviewQueueMode.Focus, items, parameters => parameters
            .Add(queue => queue.OnAction, _ => Task.CompletedTask));

        await cut.Find("[data-testid='review-approve']").ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-testid='review-focus']").Should().BeEmpty();
        cut.Find("[data-testid='review-empty']").TextContent.Should().Contain("Everything is reviewed");
        cut.Find("[data-testid='review-announcements']").TextContent.Trim()
            .Should().Be("Approved: Invoice #9. Remaining: 0.");
    }

    // ── Loading / empty ──────────────────────────────────────────────────────

    [Fact]
    public void LoadingRendersSkeletonRowsAndReportsTheQueueBusy()
    {
        var cut = RenderQueue(configure: parameters => parameters.Add(queue => queue.IsLoading, true));

        cut.Find(".tm-review-queue").GetAttribute("aria-busy").Should().Be("true");
        cut.Find("[data-testid='review-loading']").GetAttribute("aria-label").Should().Be("Loading items");
        cut.FindAll("[data-testid='review-skeleton']").Should().HaveCount(3);
        cut.FindAll("[data-testid='review-item']").Should().BeEmpty();
        cut.FindAll("[data-testid='review-empty']").Should().BeEmpty();
        cut.FindAll("[role='status']").Should().ContainSingle(
            "the skeleton is not announced as a second live region");
    }

    [Fact]
    public void AnEmptyQueueShowsTheLocalizedEmptyState()
    {
        var cut = RenderQueue(items: []);

        cut.Find("[data-testid='review-empty']").TextContent.Should()
            .Contain("Nothing to review")
            .And.Contain("New items will appear here");
        cut.FindAll("[data-testid='review-item']").Should().BeEmpty();
    }

    [Fact]
    public void TheEmptyContentSlotReplacesTheBuiltInEmptyState()
    {
        var cut = RenderQueue(items: [], configure: parameters => parameters
            .Add(queue => queue.EmptyContent, builder =>
                builder.AddMarkupContent(0, "<p data-testid='custom-empty'>All clear</p>")));

        cut.Find("[data-testid='review-empty'] [data-testid='custom-empty']").TextContent.Trim()
            .Should().Be("All clear");
    }

    // ── Mobile ───────────────────────────────────────────────────────────────

    [Fact]
    public void FocusModeOnAMobileLayoutMovesTheActionsIntoTheActionBar()
    {
        var cut = RenderQueue(ReviewQueueMode.Focus, layout: TmLayoutMode.Mobile);

        cut.Find(".tm-mobile-action-bar").Should().NotBeNull();
        cut.FindAll("[data-testid='review-actions']").Should().BeEmpty(
            "two action groups in the DOM means two sets of tab stops for the same verbs");
        cut.FindAll(".tm-mobile-action-bar [data-action-id='approve']").Should().ContainSingle();
    }

    [Fact]
    public async Task TheMobileActionBarRaisesTheDecision()
    {
        ReviewQueueAction? raised = null;
        var cut = RenderQueue(ReviewQueueMode.Focus, layout: TmLayoutMode.Mobile, configure: parameters => parameters
            .Add(queue => queue.OnAction, args =>
            {
                raised = args.Action;
                return Task.CompletedTask;
            }));

        await cut.Find(".tm-mobile-action-bar [data-action-id='approve']").ClickAsync(new MouseEventArgs());

        raised.Should().Be(ReviewQueueAction.Approve);
        cut.Find("[data-testid='review-focus-title']").TextContent.Trim().Should().Be("Invoice #4");
    }

    [Fact]
    public void ListModeOnAMobileLayoutKeepsTheRowButtons()
    {
        var cut = RenderQueue(layout: TmLayoutMode.Mobile);

        cut.FindAll(".tm-mobile-action-bar").Should().BeEmpty(
            "the list has its own per-row controls; a bar would duplicate them");
        cut.FindAll("[data-testid='review-item'] [data-testid='review-approve']").Should().HaveCount(4);
    }

    // ── Hidden summary is not focusable / a11y details ───────────────────────

    [Fact]
    public void TheShortcutHintIsVisibleAndLocalized()
    {
        var cut = RenderQueue();

        cut.Find("[data-testid='review-hint']").TextContent.Trim()
            .Should().Be("Keyboard: A approve, E edit, R reject, S skip, J/K navigate.");
    }

    [Fact]
    public void TheRowActionsGroupIsNamedByTheItem()
    {
        var cut = RenderQueue();

        cut.FindAll("[data-testid='review-item-actions']")[0].GetAttribute("aria-label")
            .Should().Be("Actions for Invoice #2");
    }
}
