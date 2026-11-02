using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models.Diffs;
using Tempo.Blazor.Components.Approvals;
using Tempo.Blazor.Diff;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Phase 4 of plan 10b3966f — <c>TmInlineDiff</c>, the inline comparison of two texts. Every change
/// is rendered as a real <c>&lt;ins&gt;</c> / <c>&lt;del&gt;</c> pair preceded by a visually hidden
/// label (so the change is never carried by colour alone), and Review mode adds one decision per
/// change: a marker button that opens a non-modal popover with Accept / Reject, the A / R shortcuts
/// inside that popover, bulk Accept all / Reject all, one level of Undo, and a two-way
/// <c>AcceptedHunkIds</c> whose result is raised through <c>ResultChanged</c> as the exact
/// <see cref="TextDiffResult.Apply"/> output. A pathological input degrades to the engine's fallback
/// and is reported, never thrown.
/// </summary>
public class TmInlineDiffTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    /// <summary>
    /// Two changes over three words: a replacement ("beta" → "BETA") and an insertion ("delta").
    /// The fixture is deliberately small so the expected <see cref="TextDiffResult.Apply"/> outputs
    /// can be written out by hand instead of recomputed in the test.
    /// </summary>
    private const string MixedOld = "alpha beta gamma";

    private const string MixedNew = "alpha BETA gamma delta";

    private IRenderedComponent<TmInlineDiff> RenderDiff(
        Action<ComponentParameterCollectionBuilder<TmInlineDiff>>? configure = null)
        => RenderDiff(MixedOld, MixedNew, configure);

    private IRenderedComponent<TmInlineDiff> RenderDiff(
        string oldText,
        string newText,
        Action<ComponentParameterCollectionBuilder<TmInlineDiff>>? configure = null)
        => Render<TmInlineDiff>(parameters =>
        {
            parameters
                .Add(diff => diff.OldText, oldText)
                .Add(diff => diff.NewText, newText);
            configure?.Invoke(parameters);
        });

    private static IReadOnlyList<string> ChangeIds(IRenderedComponent<TmInlineDiff> cut)
        => cut.FindAll("[data-testid='inline-diff-change']")
            .Select(change => change.GetAttribute("data-hunk-id")!)
            .ToList();

    private static IReadOnlyList<IElement> Markers(IRenderedComponent<TmInlineDiff> cut)
        => cut.FindAll("[data-testid='inline-diff-marker']").ToList();

    private static IElement MarkerFor(IRenderedComponent<TmInlineDiff> cut, int index)
        => Markers(cut)[index];

    private static IElement ChangeFor(IRenderedComponent<TmInlineDiff> cut, int index)
        => cut.FindAll("[data-testid='inline-diff-change']")[index];

    private static IReadOnlyList<string> LiveRegions(IRenderedComponent<TmInlineDiff> cut)
        => cut.FindAll("[role='status'], [aria-live]")
            .Select(element => element.GetAttribute("data-testid") ?? element.ClassName ?? element.TagName)
            .ToList();

    // ── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void AnInsertionIsARealInsElementWithAVisuallyHiddenLabel()
    {
        var cut = RenderDiff(
            oldText: "The quick fox",
            newText: "The quick brown fox");

        var insert = cut.Find("ins");
        insert.TextContent.Should().Be("brown ",
            "the inserted token keeps the whitespace that separates it from the next hunk");
        insert.ClassList.Should().Contain("tm-inline-diff__ins");
        insert.GetAttribute("data-testid").Should().Be("inline-diff-ins");

        topLevelLabels(cut).Should().Equal(
            new[] { "Inserted:" },
            "a <ins> element is not announced as an insertion by every screen reader, so the change "
            + "carries a visually hidden label (WCAG 1.3.1 / 1.4.1)");
    }

    [Fact]
    public void ADeletionIsARealDelElementWithAVisuallyHiddenLabel()
    {
        var cut = RenderDiff(
            oldText: "The quick brown fox",
            newText: "The quick fox");

        var delete = cut.Find("del");
        delete.TextContent.Should().Be("brown ");
        delete.ClassList.Should().Contain("tm-inline-diff__del");
        delete.GetAttribute("data-testid").Should().Be("inline-diff-del");

        topLevelLabels(cut).Should().Equal(new[] { "Removed:" });
    }

    [Fact]
    public void AReplacementRendersBothHalvesInsideOneReviewableChange()
    {
        var cut = RenderDiff(
            oldText: "red car",
            newText: "blue car",
            configure: parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        var change = cut.FindAll("[data-testid='inline-diff-change']").Single();
        change.GetAttribute("data-kind").Should().Be("replace");
        change.QuerySelector("del")!.TextContent.Should().Be("red ");
        change.QuerySelector("ins")!.TextContent.Should().Be("blue ");
        Markers(cut).Should().ContainSingle(
            "a replacement is ONE decision even though it covers both sides");
        topLevelLabels(cut).Should().Equal("Removed:", "Inserted:");
    }

    [Fact]
    public void TheEqualTextSurroundingAChangeIsRenderedVerbatim()
    {
        var cut = RenderDiff();

        var text = string.Concat(cut.FindAll(".tm-inline-diff__equal")
            .Select(element => element.TextContent));

        text.Should().Be("alpha gamma ",
            "the context between and around the changes is rendered exactly, so a re-flowed "
            + "paragraph is not presented as changed text");
    }

    [Fact]
    public void EveryChangeHasAMarkerNamedWithItsPositionAndItsKind()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        Markers(cut).Should().HaveCount(2);
        Markers(cut)[0].GetAttribute("aria-label").Should()
            .Be("Change 1 of 2: replaced text, not reviewed. Review the change.");
        Markers(cut)[1].GetAttribute("aria-label").Should()
            .Be("Change 2 of 2: inserted text, not reviewed. Review the change.");
        Markers(cut).Should().OnlyContain(marker => marker.GetAttribute("aria-haspopup") == "true");
        Markers(cut).Should().OnlyContain(marker => marker.GetAttribute("aria-expanded") == "false");
    }

    [Fact]
    public void TheRootIsAGroupNamedByALocalizedLabel()
    {
        var cut = RenderDiff();

        var root = cut.Find(".tm-inline-diff");
        root.GetAttribute("role").Should().Be("group");
        root.GetAttribute("aria-label").Should().Be("Inline diff");
        root.GetAttribute("data-mode").Should().Be("display");
        root.GetAttribute("data-granularity").Should().Be("words");
        root.GetAttribute("data-testid").Should().Be("inline-diff");

        var named = RenderDiff(parameters => parameters
            .Add(diff => diff.AriaLabel, "Proposed wording"));
        named.Find(".tm-inline-diff").GetAttribute("aria-label").Should().Be("Proposed wording");
    }

    [Fact]
    public void AnIdenticalPairRendersTheTextWithoutAnyChangeUi()
    {
        var cut = RenderDiff(
            oldText: "No change here",
            newText: "No change here",
            configure: parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        cut.FindAll("[data-testid='inline-diff-change']").Should().BeEmpty();
        Markers(cut).Should().BeEmpty();
        cut.Find("[data-testid='inline-diff-content']").TextContent.Trim()
            .Should().Be("No change here");
        cut.Instance.ResultText.Should().Be("No change here");
        cut.Find("[data-testid='inline-diff-no-changes']").TextContent.Trim()
            .Should().Be("No changes.", "an equal pair still says so instead of an empty region");
    }

    // ── Modes ────────────────────────────────────────────────────────────────

    [Fact]
    public void DisplayModeRendersTheChangesWithoutAnyAction()
    {
        var cut = RenderDiff();

        cut.Find(".tm-inline-diff").GetAttribute("data-mode").Should().Be("display");
        Markers(cut).Should().BeEmpty("Display mode is the read-only presentation");
        cut.FindAll("[data-testid='inline-diff-toolbar']").Should().BeEmpty();
        cut.FindAll("ins, del").Should().HaveCount(3,
            "the replacement contributes both sides and the insertion one: the changes themselves are "
            + "still presented");
    }

    [Fact]
    public void ReviewModeRendersTheToolbarAndOneMarkerPerChange()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        cut.Find(".tm-inline-diff").GetAttribute("data-mode").Should().Be("review");
        cut.Find("[data-testid='inline-diff-toolbar']").Should().NotBeNull();
        Markers(cut).Should().HaveCount(2);
        cut.Find("[data-testid='inline-diff-accept-all']").TextContent.Trim().Should().Be("Accept all");
        cut.Find("[data-testid='inline-diff-reject-all']").TextContent.Trim().Should().Be("Reject all");
        cut.Find("[data-testid='inline-diff-count']").TextContent.Trim().Should().Be("Changes: 2");
    }

    [Fact]
    public void ReadOnlyRemovesEveryActionEvenInReviewMode()
    {
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ReadOnly, true));

        cut.Find(".tm-inline-diff").GetAttribute("data-readonly").Should().Be("true");
        Markers(cut).Should().BeEmpty();
        cut.FindAll("[data-testid='inline-diff-toolbar']").Should().BeEmpty();
        cut.Instance.IsReview.Should().BeFalse("ReadOnly is the effective switch, not Mode");
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptingAChangeRaisesTheAcceptedIdsAndTheAppliedResult()
    {
        IReadOnlyList<string>? accepted = null;
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIdsChanged, ids => accepted = ids)
            .Add(diff => diff.ResultChanged, text => result = text));

        await OpenPopoverAsync(cut, 0);
        await cut.Find("[data-testid='inline-diff-accept']").ClickAsync(new MouseEventArgs());

        var expectedId = ChangeIds(cut)[0];
        accepted.Should().NotBeNull();
        accepted.Should().Equal(expectedId);
        result.Should().Be("alpha BETA gamma ",
            "the raised text is the exact TextDiffResult.Apply output for the accepted set");
        cut.Instance.ResultText.Should().Be("alpha BETA gamma ");

        var change = ChangeFor(cut, 0);
        change.GetAttribute("data-state").Should().Be("accepted");
        change.ClassList.Should().Contain("tm-inline-diff__change--accepted");
    }

    [Fact]
    public async Task AcceptingEveryChangeProducesTheNewTextCharacterForCharacter()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ResultChanged, text => result = text));

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());

        result.Should().Be(MixedNew);
        cut.Instance.AcceptedIds.Should().BeEquivalentTo(TextDiff.Words(MixedOld, MixedNew).Changes.Select(h => h.Id));
        cut.FindAll("[data-testid='inline-diff-change']")
            .Should().OnlyContain(change => change.GetAttribute("data-state") == "accepted");
    }

    [Fact]
    public async Task RejectingASingleChangeKeepsItsOldWording()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIds, TextDiff.Words(MixedOld, MixedNew).Changes.Select(h => h.Id).ToList())
            .Add(diff => diff.ResultChanged, text => result = text));

        cut.Instance.ResultText.Should().Be(MixedNew, "the host pre-accepted every change");

        await OpenPopoverAsync(cut, 0);
        await cut.Find("[data-testid='inline-diff-reject']").ClickAsync(new MouseEventArgs());

        result.Should().Be("alpha beta gamma delta",
            "rejecting the replacement restores its old wording while the still-accepted insertion "
            + "keeps contributing its new text");
        ChangeFor(cut, 0).GetAttribute("data-state").Should().Be("rejected");
    }

    [Fact]
    public async Task RejectingEverythingProducesTheOldTextCharacterForCharacter()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ResultChanged, text => result = text));

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-testid='inline-diff-reject-all']").ClickAsync(new MouseEventArgs());

        result.Should().Be(MixedOld);
        cut.Instance.ResultText.Should().Be(MixedOld);
    }

    [Fact]
    public async Task TheDecisionCallbackReceivesTheChangeIdentityAndTheResultingText()
    {
        var decisions = new List<InlineDiffDecision>();
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.OnDecision, decision =>
            {
                decisions.Add(decision);
                return Task.CompletedTask;
            }));

        await OpenPopoverAsync(cut, 1);
        await cut.Find("[data-testid='inline-diff-accept']").ClickAsync(new MouseEventArgs());

        decisions.Should().ContainSingle();
        decisions[0].HunkId.Should().Be(ChangeIds(cut)[1]);
        decisions[0].Accepted.Should().BeTrue();
        decisions[0].Result.Should().Be("alpha beta gamma delta");

        decisions.Clear();
        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        decisions.Should().HaveCount(1,
            "only the change that is not accepted yet needs a decision on a bulk accept");
        decisions[0].HunkId.Should().Be(ChangeIds(cut)[0]);
    }

    [Fact]
    public async Task AFailingDecisionKeepsTheChangeUndecidedAndAnnouncesTheError()
    {
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.OnDecision, _ => throw new InvalidOperationException("save failed")));

        await OpenPopoverAsync(cut, 0);
        await cut.Find("[data-testid='inline-diff-accept']").ClickAsync(new MouseEventArgs());

        ChangeFor(cut, 0).GetAttribute("data-state").Should().Be("pending",
            "a failed save must never look like a decision");
        cut.Instance.AcceptedIds.Should().BeEmpty();
        cut.Find("[data-testid='inline-diff-error']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
    }

    [Fact]
    public async Task AHunkCanBeDecidedThroughThePublicApi()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ResultChanged, text => result = text));

        var ids = ChangeIds(cut);
        await cut.InvokeAsync(() => cut.Instance.AcceptHunkAsync(ids[0]));
        result.Should().Be("alpha BETA gamma ");

        await cut.InvokeAsync(() => cut.Instance.RejectHunkAsync(ids[0]));
        result.Should().Be(MixedOld);
    }

    [Fact]
    public async Task ADisplayOnlyDiffIgnoresADecisionRequest()
    {
        var cut = RenderDiff();

        await cut.InvokeAsync(() => cut.Instance.AcceptHunkAsync(ChangeIds(cut)[0]));

        cut.Instance.AcceptedIds.Should().BeEmpty();
        cut.Instance.ResultText.Should().Be(MixedOld);
    }

    // ── Popover: focus, keys, ARIA ─────────────────────────────────────────────

    [Fact]
    public async Task ThePopoverIsAWiredNonModalGroupThatTakesFocusOnOpen()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        var marker = MarkerFor(cut, 0);
        var markerId = marker.GetAttribute("blazor:elementReference");

        await marker.ClickAsync(new MouseEventArgs());

        var popover = cut.Find("[data-testid='inline-diff-popover']");
        popover.GetAttribute("role").Should().Be("group");
        popover.GetAttribute("aria-label").Should().Be("Review change 1 of 2");

        var expanded = MarkerFor(cut, 0);
        expanded.GetAttribute("aria-expanded").Should().Be("true");
        expanded.GetAttribute("aria-controls").Should().Be(popover.Id,
            "the trigger points at the popover it owns");

        var accept = cut.Find("[data-testid='inline-diff-accept']");
        var acceptId = accept.GetAttribute("blazor:elementReference");
        acceptId.Should().NotBeNullOrEmpty();

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(acceptId,
            "opening the popover moves focus into it, so a keyboard user is not stranded on the trigger");

        markerId.Should().NotBeNullOrEmpty("the marker is the focus restore target");
    }

    [Fact]
    public async Task EscapeClosesThePopoverAndRestoresFocusToTheMarker()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        var markerId = MarkerFor(cut, 0).GetAttribute("blazor:elementReference");
        markerId.Should().NotBeNullOrEmpty();

        await MarkerFor(cut, 0).ClickAsync(new MouseEventArgs());
        var beforeEscape = JSInterop.Invocations.Count;

        await cut.Find("[data-testid='inline-diff-popover']").KeyDownAsync(
            new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[data-testid='inline-diff-popover']").Should().BeEmpty(
            "Escape closes the popover instead of leaving an orphaned focus stop behind");
        MarkerFor(cut, 0).GetAttribute("aria-expanded").Should().Be("false");

        var focus = JSInterop.Invocations.Skip(beforeEscape)
            .Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(markerId,
            "focus comes back to the marker that opened the popover");
    }

    [Fact]
    public async Task OnlyOnePopoverIsOpenAtATime()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        await MarkerFor(cut, 0).ClickAsync(new MouseEventArgs());
        await MarkerFor(cut, 1).ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-testid='inline-diff-popover']").Should().ContainSingle();
        MarkerFor(cut, 1).GetAttribute("aria-expanded").Should().Be("true");
        MarkerFor(cut, 0).GetAttribute("aria-expanded").Should().Be("false");
        cut.Find("[data-testid='inline-diff-popover']").GetAttribute("aria-label")
            .Should().Be("Review change 2 of 2");
    }

    [Fact]
    public async Task TheMarkerTogglesThePopoverClosedOnASecondActivation()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        await MarkerFor(cut, 0).ClickAsync(new MouseEventArgs());
        await MarkerFor(cut, 0).ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-testid='inline-diff-popover']").Should().BeEmpty();
    }

    [Fact]
    public async Task TabbingPastTheLastPopoverStopClosesItWithoutPullingFocusBack()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        await OpenPopoverAsync(cut, 0);
        var beforeTab = JSInterop.Invocations.Count;

        // Accept -> Reject is an inner move: the second focus stop must not take the popover down.
        await cut.Find("[data-testid='inline-diff-accept']").KeyDownAsync(
            new KeyboardEventArgs { Key = "Tab" });

        cut.FindAll("[data-testid='inline-diff-popover']").Should().ContainSingle(
            "Tab from the first stop moves to the second one, it does not leave the popover");

        await cut.Find("[data-testid='inline-diff-reject']").KeyDownAsync(
            new KeyboardEventArgs { Key = "Tab" });

        cut.FindAll("[data-testid='inline-diff-popover']").Should().BeEmpty(
            "a non-modal popover that survives Tab-out leaves a stale decision surface over the text");
        MarkerFor(cut, 0).GetAttribute("aria-expanded").Should().Be("false");

        JSInterop.Invocations.Skip(beforeTab)
            .Should().NotContain(invocation => invocation.Identifier == FocusInvocation,
                "the browser is already moving focus on — closing must not drag it back");
    }

    [Fact]
    public async Task ShiftTabbingPastTheFirstPopoverStopClosesIt()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        await OpenPopoverAsync(cut, 0);

        // Reject <- Accept backwards is an inner move.
        await cut.Find("[data-testid='inline-diff-reject']").KeyDownAsync(
            new KeyboardEventArgs { Key = "Tab", ShiftKey = true });
        cut.FindAll("[data-testid='inline-diff-popover']").Should().ContainSingle();

        await cut.Find("[data-testid='inline-diff-accept']").KeyDownAsync(
            new KeyboardEventArgs { Key = "Tab", ShiftKey = true });
        cut.FindAll("[data-testid='inline-diff-popover']").Should().BeEmpty(
            "Shift+Tab on the first stop leaves the popover backwards");
    }

    [Fact]
    public async Task ClickingTheDiffTextDismissesAnOpenPopover()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        await OpenPopoverAsync(cut, 0);
        await cut.Find("[data-testid='inline-diff-content']").ClickAsync(new MouseEventArgs());

        cut.FindAll("[data-testid='inline-diff-popover']").Should().BeEmpty(
            "a click on the text around the decision closes the decision surface");

        await OpenPopoverAsync(cut, 1);
        cut.FindAll("[data-testid='inline-diff-popover']").Should().ContainSingle(
            "the marker's own click opens (the content click handler is fenced out of it)");
    }

    [Fact]
    public async Task TheKeyboardShortcutsDecideInsideThePopover()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ResultChanged, text => result = text));

        await OpenPopoverAsync(cut, 0);
        cut.Find("[data-testid='inline-diff-accept']").GetAttribute("aria-keyshortcuts")
            .Should().Be("A");
        cut.Find("[data-testid='inline-diff-reject']").GetAttribute("aria-keyshortcuts")
            .Should().Be("R");

        await cut.Find("[data-testid='inline-diff-popover']").KeyDownAsync(
            new KeyboardEventArgs { Key = "A" });

        result.Should().Be("alpha BETA gamma ");
        cut.FindAll("[data-testid='inline-diff-popover']").Should().BeEmpty(
            "a decision closes the popover — there is nothing left to decide");

        await OpenPopoverAsync(cut, 0);
        await cut.Find("[data-testid='inline-diff-popover']").KeyDownAsync(
            new KeyboardEventArgs { Key = "r" });

        result.Should().Be(MixedOld, "R rejects the change after the undo-less toggle back");
    }

    [Fact]
    public async Task TheShortcutsCanBeDisabled()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.EnableShortcuts, false)
            .Add(diff => diff.ResultChanged, text => result = text));

        await OpenPopoverAsync(cut, 0);
        cut.Find("[data-testid='inline-diff-accept']").HasAttribute("aria-keyshortcuts")
            .Should().BeFalse("a disabled shortcut must not be advertised");

        await cut.Find("[data-testid='inline-diff-popover']").KeyDownAsync(
            new KeyboardEventArgs { Key = "A" });

        result.Should().BeNull();
        ChangeFor(cut, 0).GetAttribute("data-state").Should().Be("pending");
    }

    [Fact]
    public async Task ShortcutsIgnoreAutoRepeatAndModifierCombinations()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ResultChanged, text => result = text));

        await OpenPopoverAsync(cut, 0);
        var popover = cut.Find("[data-testid='inline-diff-popover']");

        await popover.KeyDownAsync(new KeyboardEventArgs { Key = "a", Repeat = true });
        await popover.KeyDownAsync(new KeyboardEventArgs { Key = "a", CtrlKey = true });
        await popover.KeyDownAsync(new KeyboardEventArgs { Key = "a", AltKey = true });

        result.Should().BeNull("a held or modified key is not the announced single-key shortcut");
        ChangeFor(cut, 0).GetAttribute("data-state").Should().Be("pending");
    }

    [Fact]
    public async Task AnInFlightDecisionMarksTheDiffBusyAndDisablesTheActions()
    {
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.OnDecision, async _ => await Task.Delay(500)));

        var click = cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());

        cut.WaitForState(() => cut.Instance.IsBusy, TimeSpan.FromSeconds(2));
        cut.Find(".tm-inline-diff").GetAttribute("aria-busy").Should().Be("true");
        cut.Find("[data-testid='inline-diff-accept-all']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='inline-diff-reject-all']").HasAttribute("disabled").Should().BeTrue();

        await click;

        cut.Find(".tm-inline-diff").GetAttribute("aria-busy").Should().BeNull();
        cut.Instance.IsBusy.Should().BeFalse();
        cut.Instance.AcceptedIds.Should().BeEquivalentTo(ChangeIds(cut));
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public void UndoIsNotOfferedBeforeAnyDecision()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        cut.Instance.CanUndo.Should().BeFalse();
        cut.FindAll("[data-testid='inline-diff-undo']").Should().BeEmpty();
    }

    [Fact]
    public async Task UndoRestoresThePreviousAcceptedSet()
    {
        string? result = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.ResultChanged, text => result = text));

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        result.Should().Be(MixedNew);
        cut.Instance.CanUndo.Should().BeTrue();

        await cut.Find("[data-testid='inline-diff-undo']").ClickAsync(new MouseEventArgs());

        result.Should().Be(MixedOld, "Undo returns to the accepted set before the last decision");
        cut.Instance.CanUndo.Should().BeFalse("there is exactly one level of undo");
        cut.FindAll("[data-testid='inline-diff-change']")
            .Should().OnlyContain(change => change.GetAttribute("data-state") == "pending");
        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim()
            .Should().Be("The last decision was undone.");
    }

    // ── Announcements ────────────────────────────────────────────────────────

    [Fact]
    public async Task ExactlyOnePoliteLiveRegionCarriesEveryAnnouncement()
    {
        var cut = RenderDiff(parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        LiveRegions(cut).Should().ContainSingle(
            "a diff with a popover, bulk actions and a live result must not announce the same "
            + "decision twice");

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim()
            .Should().Be("All 2 changes accepted.");

        await cut.Find("[data-testid='inline-diff-reject-all']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim()
            .Should().Be("All 2 changes rejected.");
    }

    [Fact]
    public async Task ABulkDecisionOnASingleChangeAnnouncesTheSingularForm()
    {
        // "All 1 changes accepted" is what a single template plus a number produces; the announcement
        // goes through the plural family (`_One` / `_Other`) so it reads as a sentence.
        var cut = RenderDiff(
            oldText: "red car",
            newText: "blue car",
            configure: parameters => parameters.Add(diff => diff.Mode, InlineDiffMode.Review));

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='inline-diff-announcements']").TextContent.Trim()
            .Should().Be("1 change accepted.");
    }

    // ── Line granularity and degraded input ──────────────────────────────────

    [Fact]
    public void LineGranularityComparesWholeLines()
    {
        var cut = RenderDiff(
            oldText: "first line\nsecond line\n",
            newText: "first line\nchanged line\n",
            configure: parameters => parameters
                .Add(diff => diff.Mode, InlineDiffMode.Review)
                .Add(diff => diff.Granularity, TextDiffGranularity.Lines));

        cut.Find(".tm-inline-diff").GetAttribute("data-granularity").Should().Be("lines");
        cut.Find(".tm-inline-diff").ClassList.Should().Contain("tm-inline-diff--lines");

        var change = cut.FindAll("[data-testid='inline-diff-change']").Single();
        change.QuerySelector("del")!.TextContent.Should().Be("second line\n",
            "a line change covers the whole line including its line break");
        change.QuerySelector("ins")!.TextContent.Should().Be("changed line\n");
    }

    [Fact]
    public void AnInputAboveTheConfiguredLimitRendersTheFallbackWithoutThrowing()
    {
        var oldText = "first old line\nsecond old line";
        var newText = "first new line\nsecond new line";

        var cut = RenderDiff(
            oldText,
            newText,
            parameters => parameters
                .Add(diff => diff.Mode, InlineDiffMode.Review)
                .Add(diff => diff.Granularity, TextDiffGranularity.Lines)
                .Add(diff => diff.Options, new TextDiffOptions { MaxTokens = 1 }));

        cut.Find(".tm-inline-diff").GetAttribute("data-fallback").Should().Be("single-change");
        cut.Find("[data-testid='inline-diff-fallback']").TextContent.Trim()
            .Should().Be("The comparison is too large, so the whole text is presented as a single change.");
        cut.FindAll("ins, del").Should().HaveCount(2,
            "the degraded comparison is still ONE reviewable change with both sides");

        cut.Instance.ResultText.Should().Be(oldText);
    }

    [Fact]
    public void AWordDiffThatDegradesToLinesSaysSo()
    {
        var oldText = string.Join(' ', Enumerable.Range(0, 40).Select(index => $"old{index}"));
        var newText = string.Join(' ', Enumerable.Range(0, 40).Select(index => $"new{index}"));

        var cut = RenderDiff(
            oldText,
            newText,
            parameters => parameters
                .Add(diff => diff.Options, new TextDiffOptions { MaxTokens = 8, MaxCells = 100_000 }));

        cut.Find(".tm-inline-diff").GetAttribute("data-fallback").Should().Be("line-level");
        cut.Find("[data-testid='inline-diff-fallback']").TextContent.Trim()
            .Should().Be("The comparison is too large, so whole lines are compared instead of words.");
        cut.Find(".tm-inline-diff").GetAttribute("data-granularity").Should().Be("lines",
            "the rendered granularity is the one the engine actually used");
    }

    // ── Host contract ────────────────────────────────────────────────────────

    [Fact]
    public void AnIncomingAcceptedSetIsRenderedAndNotTreatedAsAChange()
    {
        var ids = TextDiff.Words(MixedOld, MixedNew).Changes.Select(hunk => hunk.Id).ToList();

        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIds, ids));

        cut.Instance.AcceptedIds.Should().BeEquivalentTo(ids);
        cut.Instance.ResultText.Should().Be(MixedNew);
        cut.FindAll("[data-testid='inline-diff-change']")
            .Should().OnlyContain(change => change.GetAttribute("data-state") == "accepted");
    }

    [Fact]
    public async Task AHostEchoOfTheRaisedSetDoesNotResetTheUndoStep()
    {
        IReadOnlyList<string>? echo = null;
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIds, echo)
            .Add(diff => diff.AcceptedHunkIdsChanged, value => echo = value));

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        echo.Should().NotBeNull();
        cut.Instance.CanUndo.Should().BeTrue();

        cut.Render(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIds, echo));

        cut.Instance.AcceptedIds.Should().BeEquivalentTo(echo,
            "a two-way host echoes the set the component itself raised");
        cut.Instance.CanUndo.Should().BeTrue(
            "an echo of the component's own value is not a new host decision, so the undo step stays");
    }

    [Fact]
    public async Task ANewHostValueReplacesTheDecisionAndDropsTheUndoStep()
    {
        IReadOnlyList<string>? value = [];
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIds, value));

        await cut.Find("[data-testid='inline-diff-accept-all']").ClickAsync(new MouseEventArgs());
        cut.Instance.CanUndo.Should().BeTrue();

        var ids = ChangeIds(cut);
        cut.Render(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.AcceptedHunkIds, new List<string> { ids[1] }));

        cut.Instance.AcceptedIds.Should().Equal(ids[1]);
        cut.Instance.CanUndo.Should().BeFalse("the host replaced the decision, so there is nothing to undo");
    }

    [Fact]
    public void AdditionalAttributesAndClassReachTheRoot()
    {
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Class, "my-diff")
            .AddUnmatched("data-host", "x"));

        var root = cut.Find(".tm-inline-diff");
        root.ClassList.Should().Contain("my-diff");
        root.GetAttribute("data-host").Should().Be("x");
    }

    [Fact]
    public void ATestIdPrefixNamespacesEveryInternalTestId()
    {
        var cut = RenderDiff(parameters => parameters
            .Add(diff => diff.Mode, InlineDiffMode.Review)
            .Add(diff => diff.TestIdPrefix, "wording"));

        cut.Find("[data-testid='wording-inline-diff']").Should().NotBeNull();
        cut.Find("[data-testid='wording-inline-diff-accept-all']").Should().NotBeNull();
        cut.Find("[data-testid='wording-inline-diff-marker']").Should().NotBeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> topLevelLabels(IRenderedComponent<TmInlineDiff> cut)
        => cut.FindAll(".tm-inline-diff__change .tm-sr-only")
            .Select(label => label.TextContent.Trim())
            .ToList();

    private static async Task OpenPopoverAsync(IRenderedComponent<TmInlineDiff> cut, int index)
    {
        await MarkerFor(cut, index).ClickAsync(new MouseEventArgs());
        cut.Find("[data-testid='inline-diff-popover']").Should().NotBeNull();
    }
}
