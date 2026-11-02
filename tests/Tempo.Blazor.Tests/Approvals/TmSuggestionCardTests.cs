using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Approvals;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Phase 4 of plan 10b3966f — <c>TmSuggestionCard</c>, one machine suggestion a human accepts or
/// dismisses. The card shows the suggested text, a localized confidence label (with an optional
/// percentage), the rationale behind a native disclosure and, when the host offers more than one
/// wording, a <c>radiogroup</c> of alternatives. Accept / Dismiss are asynchronous callbacks — the
/// card leaves the pending state only when the callback completes without throwing — and, with an
/// <c>UndoWindow</c>, the decided row offers Undo until the window elapses. Exactly one polite live
/// region carries every announcement and focus follows the collapse (and returns on undo).
/// </summary>
public class TmSuggestionCardTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    private static Task DrainAsync<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => cut.InvokeAsync(() => { });

    /// <summary>Whether the element sits in the collapsed half of the card (the inactive half is
    /// <c>inert</c>, so it is out of the tab order and out of the accessibility tree).</summary>
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

    private static IElement FindVisible(IRenderedComponent<TmSuggestionCard> cut, string testId)
        => cut.FindAll($"[data-testid='{testId}']").Single(element => !IsHidden(element));

    private static bool HasVisible(IRenderedComponent<TmSuggestionCard> cut, string testId)
        => cut.FindAll($"[data-testid='{testId}']").Any(element => !IsHidden(element));

    private static IReadOnlyList<string> Alternatives()
        =>
        [
            "Use the compact layout",
            "Keep the detailed layout",
            "Ask the reader",
        ];

    private IRenderedComponent<TmSuggestionCard> RenderCard(
        Action<ComponentParameterCollectionBuilder<TmSuggestionCard>>? configure = null)
        => Render<TmSuggestionCard>(parameters =>
        {
            parameters
                .Add(card => card.Text, "Move the summary above the fold.")
                .Add(card => card.Title, "Layout suggestion");
            configure?.Invoke(parameters);
        });

    // ── Content ──────────────────────────────────────────────────────────────

    [Fact]
    public void RendersTheTextTitleAndALocalizedGroupName()
    {
        var cut = RenderCard();

        var root = cut.Find(".tm-suggestion-card");
        root.GetAttribute("role").Should().Be("group");
        root.GetAttribute("aria-label").Should().Be("Suggestion");
        root.GetAttribute("data-state").Should().Be("pending");
        root.GetAttribute("data-testid").Should().Be("suggestion-card");

        cut.Find("[data-testid='suggestion-text']").TextContent.Trim()
            .Should().Be("Move the summary above the fold.");
        cut.Find("[data-testid='suggestion-title']").TextContent.Trim().Should().Be("Layout suggestion");
    }

    [Fact]
    public void ABriefWithoutTitleStillHasANameAndNoHeading()
    {
        var cut = Render<TmSuggestionCard>(parameters => parameters
            .Add(card => card.Text, "Move the summary above the fold.")
            .Add(card => card.AriaLabel, "Wording proposal"));

        cut.FindAll("[data-testid='suggestion-title']").Should().BeEmpty();
        cut.Find(".tm-suggestion-card").GetAttribute("aria-label").Should().Be("Wording proposal");
    }

    // ── Confidence ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.92, "High confidence", "tm-suggestion-card--confidence-high", "high")]
    [InlineData(0.6, "Medium confidence", "tm-suggestion-card--confidence-medium", "medium")]
    [InlineData(0.2, "Low confidence", "tm-suggestion-card--confidence-low", "low")]
    public void TheConfidenceIsALocalizedLabelWithItsOwnModifier(
        double confidence, string expectedLabel, string expectedClass, string dataValue)
    {
        var cut = RenderCard(parameters => parameters.Add(card => card.Confidence, confidence));

        var chip = cut.Find("[data-testid='suggestion-confidence']");
        chip.TextContent.Trim().Should().Be(expectedLabel,
            "the confidence must not be expressed by the number alone (WCAG 1.4.1)");
        cut.Find(".tm-suggestion-card").ClassList.Should().Contain(expectedClass);
        cut.Find(".tm-suggestion-card").GetAttribute("data-confidence").Should().Be(dataValue);
    }

    [Fact]
    public void WithoutAConfidenceThereIsNoChip()
    {
        var cut = RenderCard();

        cut.FindAll("[data-testid='suggestion-confidence']").Should().BeEmpty();
        cut.Find(".tm-suggestion-card").GetAttribute("data-confidence").Should().Be("none");
    }

    [Fact]
    public void ThePercentageIsOptIn()
    {
        var withPercent = RenderCard(parameters => parameters
            .Add(card => card.Confidence, 0.87)
            .Add(card => card.ShowConfidencePercent, true));

        withPercent.Find("[data-testid='suggestion-confidence-percent']").TextContent.Trim()
            .Should().Be("87 %");
        withPercent.Find("[data-testid='suggestion-confidence']").TextContent.Trim()
            .Should().Contain("High confidence", "the label stays even when the number is shown");

        var withoutPercent = RenderCard(parameters => parameters.Add(card => card.Confidence, 0.87));
        withoutPercent.FindAll("[data-testid='suggestion-confidence-percent']").Should().BeEmpty();
    }

    // ── Rationale disclosure ─────────────────────────────────────────────────

    [Fact]
    public void TheRationaleIsANativeDisclosureCollapsedByDefault()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Rationale, "The summary is the first thing a reader scans."));

        var disclosure = cut.Find("[data-testid='suggestion-rationale']");
        disclosure.TagName.Should().Be("DETAILS",
            "a native <details> keeps the disclosure keyboard-operable without a JS handler");
        disclosure.HasAttribute("open").Should().BeFalse();
        disclosure.QuerySelector("summary")!.TextContent.Trim().Should().Be("Why this suggestion");
        cut.Find("[data-testid='suggestion-rationale-text']").TextContent.Trim()
            .Should().Be("The summary is the first thing a reader scans.");
    }

    [Fact]
    public void TheRationaleCanStartExpandedAndIsAbsentWhenBlank()
    {
        var expanded = RenderCard(parameters => parameters
            .Add(card => card.Rationale, "Because it is faster.")
            .Add(card => card.RationaleDefaultExpanded, true));
        expanded.Find("[data-testid='suggestion-rationale']").HasAttribute("open").Should().BeTrue();

        var blank = RenderCard(parameters => parameters.Add(card => card.Rationale, "   "));
        blank.FindAll("[data-testid='suggestion-rationale']").Should().BeEmpty(
            "an empty disclosure is a dead focus stop");
    }

    // ── Alternatives ─────────────────────────────────────────────────────────

    [Fact]
    public void AlternativesFormALocalizedRadiogroupWithoutAPreselection()
    {
        var cut = RenderCard(parameters => parameters.Add(card => card.Alternatives, Alternatives()));

        var group = cut.Find("[data-testid='suggestion-alternatives']");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.GetAttribute("aria-label").Should().Be("Alternatives");

        var options = cut.FindAll("[data-testid='suggestion-alternative']");
        options.Should().HaveCount(3);
        options.Should().OnlyContain(option => option.GetAttribute("type") == "radio");
        options.Should().OnlyContain(option => option.GetAttribute("name") != null
            && option.GetAttribute("name") == options[0].GetAttribute("name"));
        cut.FindAll("[data-testid='suggestion-alternative']:checked").Should().BeEmpty(
            "no alternative is the suggestion itself, so the card starts without a selection");
    }

    [Fact]
    public void SelectingAnAlternativeRaisesTheTwoWayValue()
    {
        string? selected = null;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Alternatives, Alternatives())
            .Add(card => card.SelectedAlternativeChanged, value => selected = value));

        var options = cut.FindAll("[data-testid='suggestion-alternative']");
        options[1].Change(true);

        selected.Should().Be("Keep the detailed layout");
        cut.Instance.CurrentAlternative.Should().Be("Keep the detailed layout");
        cut.FindAll("[data-testid='suggestion-alternative']:checked").Should().ContainSingle();
        cut.FindAll("[data-testid='suggestion-alternative']")
            .Single(option => option.HasAttribute("checked"))
            .GetAttribute("value").Should().Be("Keep the detailed layout");
    }

    [Fact]
    public void AnIncomingSelectedAlternativeIsRenderedAsTheCheckedRadio()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Alternatives, Alternatives())
            .Add(card => card.SelectedAlternative, "Ask the reader"));

        cut.FindAll("[data-testid='suggestion-alternative']:checked").Should().ContainSingle()
            .Which.GetAttribute("value").Should().Be("Ask the reader");
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptPassesTheSelectedAlternativeToTheCallbacks()
    {
        string? accepted = null;
        string? eventText = null;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Alternatives, Alternatives())
            .Add(card => card.OnAccept, text =>
            {
                accepted = text;
                return Task.CompletedTask;
            })
            .Add(card => card.Accepted, text => eventText = text));

        cut.FindAll("[data-testid='suggestion-alternative']")[2].Change(true);
        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        accepted.Should().Be("Ask the reader");
        eventText.Should().Be("Ask the reader");
    }

    [Fact]
    public async Task AcceptWithoutASelectionUsesTheText()
    {
        string? accepted = null;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Alternatives, Alternatives())
            .Add(card => card.OnAccept, text =>
            {
                accepted = text;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        accepted.Should().Be("Move the summary above the fold.");
    }

    [Fact]
    public async Task AcceptingCollapsesTheCardIntoAResultRow()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnAccept, _ => Task.CompletedTask));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("accepted");
        FindVisible(cut, "suggestion-summary").Should().NotBeNull();
        FindVisible(cut, "suggestion-summary-text").TextContent.Trim()
            .Should().Be("Accepted: Move the summary above the fold.");
        HasVisible(cut, "suggestion-accept").Should().BeFalse("the decided action collapsed away");
        cut.Find("[data-testid='suggestion-announcements']").TextContent.Trim()
            .Should().Be("Suggestion accepted.");
    }

    [Fact]
    public async Task TheSummaryIconAppearsOnlyOnceTheCardIsDecided()
    {
        // The result row's glyph marks the DECISION (check vs cross). A pending card has none: falling
        // back to the accepted icon would put a "check" next to an undecided suggestion as soon as the
        // inert half is read by anything that ignores inert (a print stylesheet, a text extraction).
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnAccept, _ => Task.CompletedTask));

        cut.FindAll(".tm-suggestion-card__summary-icon").Should().BeEmpty(
            "nothing has been decided yet");

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        cut.FindAll(".tm-suggestion-card__summary-icon").Should().ContainSingle();
    }

    [Fact]
    public async Task DismissingCollapsesTheCardAndRaisesTheDismissCallback()
    {
        var dismissed = 0;
        var raiseEvent = 0;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDismiss, () =>
            {
                dismissed++;
                return Task.CompletedTask;
            })
            .Add(card => card.Dismissed, () => raiseEvent++));

        await cut.Find("[data-testid='suggestion-dismiss']").ClickAsync(new MouseEventArgs());

        dismissed.Should().Be(1);
        raiseEvent.Should().Be(1);
        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("dismissed");
        FindVisible(cut, "suggestion-summary-text").TextContent.Trim().Should().Be("Dismissed");
        cut.Find("[data-testid='suggestion-announcements']").TextContent.Trim()
            .Should().Be("Suggestion dismissed.");
    }

    [Fact]
    public async Task AFailingAcceptShowsTheLocalizedErrorAndKeepsTheSuggestionPending()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnAccept, _ => throw new InvalidOperationException("save failed")));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("pending",
            "a failed save must never look like an accepted suggestion");
        cut.Find("[data-testid='suggestion-error']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
        cut.Find("[data-testid='suggestion-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
        cut.Instance.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task AnInFlightDecisionDisablesTheActionsAndMarksTheCardBusy()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnAccept, async _ => await Task.Delay(500)));

        var click = cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        cut.WaitForState(() => cut.Instance.IsBusy, TimeSpan.FromSeconds(2));
        cut.Find(".tm-suggestion-card").GetAttribute("aria-busy").Should().Be("true");
        cut.Find("[data-testid='suggestion-accept']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='suggestion-dismiss']").HasAttribute("disabled").Should().BeTrue();

        await click;

        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("accepted");
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AUndoLabelContainingBracesIsShownLiterallyInsteadOfThrowing()
    {
        // The undo button's aria-label carries no count, so it must not go through string.Format: a
        // translation with a literal brace ("Undo the {decision}") would otherwise throw FormatException
        // from the render and take the whole card down.
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmSuggestionCard_UndoLabel"] = "Undo the {decision",
        });

        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnAccept, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => Task.CompletedTask));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='suggestion-undo']").GetAttribute("aria-label")
            .Should().Be("Undo the {decision");
    }

    [Fact]
    public async Task UndoIsOfferedInsideTheWindowAndRestoresThePendingCard()
    {
        UseFakeClock();
        var undone = 0;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnAccept, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () =>
            {
                undone++;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        FindVisible(cut, "suggestion-undo").Should().NotBeNull("the undo window is open");
        await cut.Find("[data-testid='suggestion-undo']").ClickAsync(new MouseEventArgs());

        undone.Should().Be(1);
        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("pending");
        HasVisible(cut, "suggestion-accept").Should().BeTrue();
        cut.Find("[data-testid='suggestion-announcements']").TextContent.Trim()
            .Should().Be("Decision undone. The suggestion is pending again.");
    }

    [Fact]
    public async Task UndoDisappearsWhenTheWindowElapses()
    {
        var time = UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnAccept, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => Task.CompletedTask));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());
        HasVisible(cut, "suggestion-undo").Should().BeTrue();

        time.Advance(TimeSpan.FromSeconds(31));
        await DrainAsync(cut);

        HasVisible(cut, "suggestion-undo").Should().BeFalse("the undo window has elapsed");
        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("accepted");
    }

    [Fact]
    public async Task WithoutAnUndoWindowOrCallbackNoUndoIsOffered()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnAccept, _ => Task.CompletedTask));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        HasVisible(cut, "suggestion-undo").Should().BeFalse();
    }

    [Fact]
    public async Task AFailingUndoKeepsTheDecisionAndAnnouncesTheError()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnAccept, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => throw new InvalidOperationException("save failed")));

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());
        await cut.Find("[data-testid='suggestion-undo']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-suggestion-card").GetAttribute("data-state").Should().Be("accepted",
            "the decision stands when the undo cannot be persisted");
        cut.Find("[data-testid='suggestion-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
    }

    // ── Focus ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcceptingWithAnUndoWindowMovesFocusToUndo()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnAccept, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => Task.CompletedTask));

        var acceptId = cut.Find("[data-testid='suggestion-accept']").GetAttribute("blazor:elementReference");
        acceptId.Should().NotBeNullOrEmpty();

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        var undoId = cut.Find("[data-testid='suggestion-undo']").GetAttribute("blazor:elementReference");
        undoId.Should().NotBeNullOrEmpty();
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(undoId,
            "the focused accept button collapsed away, so focus lands on the surviving action");

        var beforeUndo = JSInterop.Invocations.Count;
        await cut.Find("[data-testid='suggestion-undo']").ClickAsync(new MouseEventArgs());

        focus = JSInterop.Invocations.Skip(beforeUndo)
            .Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(acceptId,
            "undo re-expands the card, so focus comes back to the primary action");
    }

    [Fact]
    public async Task AcceptingWithoutUndoFocusesTheCardItself()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnAccept, _ => Task.CompletedTask));

        var rootId = cut.Find(".tm-suggestion-card").GetAttribute("blazor:elementReference");
        rootId.Should().NotBeNullOrEmpty("the card root is the focus fallback (tabindex=-1)");

        await cut.Find("[data-testid='suggestion-accept']").ClickAsync(new MouseEventArgs());

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(rootId);
    }

    // ── Live region / host contract ──────────────────────────────────────────

    [Fact]
    public void ExactlyOnePoliteLiveRegionExists()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Confidence, 0.9)
            .Add(card => card.Rationale, "Because.")
            .Add(card => card.Alternatives, Alternatives()));

        var regions = cut.FindAll("[role='status'], [aria-live]");
        regions.Should().ContainSingle(
            "the confidence, the disclosure and the radiogroup must not each announce themselves");
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].GetAttribute("data-testid").Should().Be("suggestion-announcements");
    }

    [Fact]
    public void AdditionalAttributesClassAndTestIdPrefixReachTheComponent()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Class, "my-suggestion")
            .Add(card => card.TestIdPrefix, "wording")
            .AddUnmatched("data-host", "x"));

        var root = cut.Find(".tm-suggestion-card");
        root.ClassList.Should().Contain("my-suggestion");
        root.GetAttribute("data-host").Should().Be("x");
        cut.Find("[data-testid='wording-suggestion-accept']").Should().NotBeNull();
        cut.Find("[data-testid='wording-suggestion-announcements']").Should().NotBeNull();
    }
}
