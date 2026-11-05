using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 6 — <c>TmAutonomyDial</c> (Tempo.Blazor.AI): how much an agent may do on its own, as a real
/// slider (<c>role="slider"</c>) or as a segmented radio group, with the stops the host declared, the ceiling a plan
/// imposes and the limits somebody else enforces.
/// <para>
/// The facts read behaviour: the keys move one stop per press and never wrap, Home and End reach the ends of what is
/// ALLOWED, the slider's value is exposed as an invariant number plus a localized value text, a change at or above
/// <c>ConfirmAbove</c> waits for a confirmation (Escape cancels and the focus comes back), a ceiling above the current
/// level disables the higher stops WITH an explanation, a lock names its action, its source and its reason, and exactly
/// one polite live region announces a committed change.
/// </para>
/// </summary>
public class TmAutonomyDialTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private int? _reportedValue;
    private int _changeCount;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static IReadOnlyList<AutonomyLevel> Levels() =>
    [
        new(0) { Label = "Ask first", Description = "Every action waits for your approval." },
        new(1) { Label = "Ask when risky", Description = "Risky actions wait for approval." },
        new(2) { Label = "Act freely", Description = "The agent acts without asking." },
    ];

    private static IReadOnlyList<AutonomyLock> Locks() =>
    [
        new("Send external email") { Source = "Tenant policy", Reason = "Outbound mail needs a human sign-off." },
        new("Delete a file"),
    ];

    private IRenderedComponent<TmAutonomyDial> RenderDial(
        IReadOnlyList<AutonomyLevel>? levels = null,
        int value = 1,
        AutonomyDialPresentation presentation = AutonomyDialPresentation.Dial,
        IReadOnlyList<AutonomyLock>? locks = null,
        int? maxAllowed = null,
        string? maxAllowedReason = null,
        int? confirmAbove = null,
        bool showTitle = true,
        string? title = null,
        int headingLevel = 3)
    {
        _reportedValue = null;
        _changeCount = 0;

        return Render<TmAutonomyDial>(parameters =>
        {
            parameters.Add(component => component.Levels, levels ?? Levels());
            parameters.Add(component => component.Value, value);
            parameters.Add(component => component.Presentation, presentation);
            parameters.Add(component => component.Locks, locks);
            parameters.Add(component => component.MaxAllowed, maxAllowed);
            parameters.Add(component => component.MaxAllowedReason, maxAllowedReason);
            parameters.Add(component => component.ConfirmAbove, confirmAbove);
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.ValueChanged, (int next) =>
            {
                _reportedValue = next;
                _changeCount++;
            });

            if (title is not null)
            {
                parameters.Add(component => component.Title, title);
            }
        });
    }

    /// <summary>A sequential key press on the slider, exactly as a keyboard reader sends it.</summary>
    private static void Press(IRenderedComponent<TmAutonomyDial> cut, string key)
        => cut.Find("[data-testid='autonomy-dial-track']")
            .KeyDown(new KeyboardEventArgs { Key = key });

    private static string ValueNow(IRenderedComponent<TmAutonomyDial> cut)
        => cut.Find("[data-testid='autonomy-dial-track']").GetAttribute("aria-valuenow") ?? string.Empty;

    private static string ValueText(IRenderedComponent<TmAutonomyDial> cut)
        => cut.Find("[data-testid='autonomy-dial-track']").GetAttribute("aria-valuetext") ?? string.Empty;

    // ── The stops ────────────────────────────────────────────────────────────

    [Fact]
    public void WithNoLevels_TheThreeLocalizedDefaultStopsAreUsed()
    {
        var cut = RenderDial(levels: [], value: 0);

        cut.FindAll("[data-testid='autonomy-dial-stop']").Should().HaveCount(3,
            "a dial with no host stops still explains the three grades of autonomy");
        cut.FindAll("[data-testid='autonomy-dial-stop']")[1]
            .QuerySelector(".tm-ai-autonomy__stop-label")!.TextContent.Should().Be("Ask when risky");
        cut.Find("[data-testid='autonomy-current']").TextContent.Should().Be("Ask first");
        cut.Find("[data-testid='autonomy-current-description']").TextContent
            .Should().Be("Every action waits for your approval.");
    }

    [Fact]
    public void TheHostsLevels_AreRenderedInAscendingValueOrder()
    {
        var cut = RenderDial(levels:
        [
            new(2) { Label = "Act freely" },
            new(0) { Label = "Ask first" },
            new(1) { Label = "Ask when risky" },
        ], value: 1);

        cut.FindAll("[data-testid='autonomy-dial-stop']")
            .Select(stop => stop.QuerySelector(".tm-ai-autonomy__stop-label")!.TextContent)
            .Should().Equal("Ask first", "Ask when risky", "Act freely");
        cut.Find("[data-testid='autonomy-current-position']").TextContent.Should().Be("Level 2 of 3");
    }

    [Fact]
    public void ADuplicateLevelValue_IsKeptOnce_SoTheSliderHasOneStopPerValue()
    {
        var cut = RenderDial(levels: [new(0) { Label = "Ask first" }, new(1) { Label = "A" }, new(1) { Label = "B" }]);

        cut.FindAll("[data-testid='autonomy-dial-stop']").Should().HaveCount(2);
    }

    // ── The dial presentation ────────────────────────────────────────────────

    [Fact]
    public void TheDial_IsASlider_ThatNamesItsValueInWords()
    {
        var cut = RenderDial(value: 1);

        var slider = cut.Find("[data-testid='autonomy-dial-track']");
        slider.GetAttribute("role").Should().Be("slider");
        slider.GetAttribute("tabindex").Should().Be("0");
        slider.GetAttribute("aria-valuemin").Should().Be("0");
        slider.GetAttribute("aria-valuemax").Should().Be("2");
        slider.GetAttribute("aria-valuenow").Should().Be("1");
        slider.GetAttribute("aria-valuetext").Should().Be("Ask when risky",
            "the value is a grade of autonomy, not the number 1");

        var titleId = cut.Find("[data-testid='autonomy-title']").GetAttribute("id");
        slider.GetAttribute("aria-labelledby").Should().Be(titleId,
            "the heading names the slider, so the name a reader hears is the one they see");
        slider.GetAttribute("aria-describedby").Should().NotBeNullOrEmpty();
        cut.Find("#" + slider.GetAttribute("aria-describedby")).TextContent
            .Should().Be("Risky actions wait for approval.");
    }

    [Fact]
    public void WithoutATitle_TheSliderCarriesItsOwnLocalizedName()
    {
        var cut = RenderDial(showTitle: false, title: null);

        cut.FindAll("[data-testid='autonomy-title']").Should().BeEmpty();
        cut.Find("[data-testid='autonomy-dial-track']").GetAttribute("aria-label").Should().Be("Autonomy level");
    }

    [Fact]
    public void TheArrowKeys_MoveExactlyOneStopPerPress()
    {
        var cut = RenderDial(value: 0);

        Press(cut, "ArrowRight");

        _reportedValue.Should().Be(1);
        _changeCount.Should().Be(1, "one press is one change, never two");
        ValueNow(cut).Should().Be("1");
        ValueText(cut).Should().Be("Ask when risky");

        Press(cut, "ArrowLeft");

        _reportedValue.Should().Be(0);
        _changeCount.Should().Be(2);
        ValueNow(cut).Should().Be("0");
    }

    [Fact]
    public void TheUpAndDownArrows_AreTheSameMove()
    {
        var cut = RenderDial(value: 1);

        Press(cut, "ArrowUp");
        _reportedValue.Should().Be(2);
        Press(cut, "ArrowDown");
        _reportedValue.Should().Be(1);
    }

    [Fact]
    public void HomeAndEnd_ReachTheFirstAndLastLevel()
    {
        var cut = RenderDial(value: 1);

        Press(cut, "End");
        ValueNow(cut).Should().Be("2");
        _reportedValue.Should().Be(2);

        Press(cut, "Home");
        ValueNow(cut).Should().Be("0");
        _reportedValue.Should().Be(0);
    }

    [Fact]
    public void AKeyPastTheEnd_ChangesNothing_AndAnnouncesNothing()
    {
        var cut = RenderDial(value: 2);

        Press(cut, "ArrowRight");
        Press(cut, "End");

        _changeCount.Should().Be(0);
        cut.Find("[data-testid='autonomy-announcement']").TextContent.Should().BeEmpty(
            "there was nothing to announce");
    }

    [Fact]
    public void AnUnknownKey_IsIgnored()
    {
        var cut = RenderDial(value: 1);

        Press(cut, "a");
        Press(cut, "Tab");

        _changeCount.Should().Be(0);
    }

    [Fact]
    public void TheStepButtons_AreTheTapAlternative_AndStopAtTheEnds()
    {
        var cut = RenderDial(value: 0);

        var lower = cut.Find("[data-testid='autonomy-dial-lower']");
        var higher = cut.Find("[data-testid='autonomy-dial-higher']");

        lower.GetAttribute("aria-disabled").Should().Be("true", "the first stop has nothing below it");
        lower.HasAttribute("disabled").Should().BeFalse("a natively disabled button loses the focus mid-step");
        higher.HasAttribute("aria-disabled").Should().BeFalse();

        higher.Click();
        _reportedValue.Should().Be(1);

        higher.Click();
        _reportedValue.Should().Be(2);
        ValueNow(cut).Should().Be("2");

        var exhausted = cut.Find("[data-testid='autonomy-dial-higher']");
        exhausted.GetAttribute("aria-disabled").Should().Be("true");
        exhausted.HasAttribute("disabled").Should().BeFalse(
            "the button the reader pressed is still the focused element, so it must stay focusable (WCAG 2.4.3)");

        lower.Click();
        _reportedValue.Should().Be(1);
    }

    [Fact]
    public void AStepButtonAtTheTopOfTheRange_StaysFocusable_SoTheFocusIsNeverDropped()
    {
        // The ceiling moves the top allowed stop, so the button the keyboard reader stands on must not disable itself
        // under them: pressing Higher leaves the focus where it was and simply does nothing.
        var cut = RenderDial(value: 1, maxAllowed: 1);

        var higher = cut.Find("[data-testid='autonomy-dial-higher']");
        higher.GetAttribute("aria-disabled").Should().Be("true");
        higher.HasAttribute("disabled").Should().BeFalse("a disabled button drops the focus to <body> (WCAG 2.4.3)");
        higher.HasAttribute("tabindex").Should().BeFalse("the button keeps its place in the tab order");

        higher.Click();

        _changeCount.Should().Be(0, "the button is inert at the ceiling, not gone");
        ValueNow(cut).Should().Be("1");
    }

    [Fact]
    public void AStepButtonAtTheBottomOfTheRange_StaysFocusable_Too()
    {
        var cut = RenderDial(value: 0);

        var lower = cut.Find("[data-testid='autonomy-dial-lower']");
        lower.GetAttribute("aria-disabled").Should().Be("true");
        lower.HasAttribute("disabled").Should().BeFalse("the lowest stop must not take the focus away");

        lower.Click();

        _changeCount.Should().Be(0);
        ValueNow(cut).Should().Be("0");
    }

    [Fact]
    public void AValueOutsideTheStops_ClampsToTheNearestOne()
    {
        var high = RenderDial(value: 9);
        ValueNow(high).Should().Be("2");

        var low = RenderDial(value: -4);
        ValueNow(low).Should().Be("0");
    }

    // ── The segmented presentation ───────────────────────────────────────────

    [Fact]
    public void TheSegmentedPresentation_IsARadioGroupOfTheSameStops()
    {
        var cut = RenderDial(presentation: AutonomyDialPresentation.Segmented, value: 1);

        cut.FindAll("[data-testid='autonomy-dial-track']").Should().BeEmpty("the slider is replaced, not duplicated");

        var group = cut.Find("[data-testid='autonomy-segmented'] [role='radiogroup']");
        group.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty(
            "the visible label names the group");

        var radios = cut.FindAll("[data-testid='autonomy-segmented'] [role='radio']");
        radios.Should().HaveCount(3);
        radios.Select(radio => radio.GetAttribute("aria-checked")).Should().Equal("false", "true", "false");
        radios.Select(radio => radio.TextContent.Trim()).Should().Equal("Ask first", "Ask when risky", "Act freely");
    }

    [Fact]
    public void PickingASegment_ReportsItsLevel()
    {
        var cut = RenderDial(presentation: AutonomyDialPresentation.Segmented, value: 1);

        cut.Find("[data-testid='autonomy-segmented'] [role='radio'][data-value='2']").Click();

        _reportedValue.Should().Be(2);
        _changeCount.Should().Be(1);
        cut.Find("[data-testid='autonomy-current']").TextContent.Should().Be("Act freely");
    }

    [Fact]
    public void AnUnknownPresentation_FallsBackToTheDial()
    {
        var cut = RenderDial(presentation: (AutonomyDialPresentation)42);

        cut.Find("[data-testid='autonomy-dial']").GetAttribute("data-presentation").Should().Be("dial");
        cut.FindAll("[data-testid='autonomy-dial-track']").Should().HaveCount(1);
    }

    // ── The ceiling ──────────────────────────────────────────────────────────

    [Fact]
    public void ACeiling_DisablesTheHigherStops_AndSaysWhy()
    {
        var cut = RenderDial(value: 1, maxAllowed: 1);

        cut.Find("[data-testid='autonomy-dial-track']").GetAttribute("aria-valuemax").Should().Be("1");
        cut.Find("[data-testid='autonomy-dial-higher']").GetAttribute("aria-disabled").Should().Be("true");
        cut.FindAll("[data-testid='autonomy-dial-stop']")[2].GetAttribute("data-available").Should().Be("false");
        cut.Find("[data-testid='autonomy-limit']").TextContent
            .Should().Be("Levels above Ask when risky are not available.");
        cut.Find("[data-testid='autonomy-limit-reason']").TextContent
            .Should().Be("Your plan does not allow a higher level.");

        Press(cut, "End");
        _changeCount.Should().Be(0, "End stops at the ceiling instead of jumping past it");
        ValueNow(cut).Should().Be("1");
    }

    [Fact]
    public void TheHostCanExplainItsOwnCeiling()
    {
        var cut = RenderDial(maxAllowed: 1, maxAllowedReason: "The compliance profile caps autonomy at this level.");

        cut.Find("[data-testid='autonomy-limit-reason']").TextContent
            .Should().Be("The compliance profile caps autonomy at this level.");
    }

    [Fact]
    public void ACeilingBelowTheCurrentValue_RendersTheHighestAllowedStop()
    {
        var cut = RenderDial(value: 2, maxAllowed: 1);

        ValueNow(cut).Should().Be("1", "a level the host no longer allows is not the level the surface shows");
        cut.Find("[data-testid='autonomy-current']").TextContent.Should().Be("Ask when risky");
    }

    [Fact]
    public void ACeilingBelowEveryStop_KeepsTheDialUsable()
    {
        var cut = RenderDial(value: 1, maxAllowed: -1);

        cut.Find("[data-testid='autonomy-dial-track']").GetAttribute("aria-valuemax")
            .Should().Be("2", "a ceiling no stop can satisfy is a host mistake, not a locked control");

        Press(cut, "End");

        _reportedValue.Should().Be(2, "the reader can still move the dial");
        ValueNow(cut).Should().Be("2");
    }

    [Fact]
    public void WithNoCeiling_NoLimitLineIsRendered()
    {
        var cut = RenderDial(value: 1);

        cut.FindAll("[data-testid='autonomy-limit']").Should().BeEmpty();
    }

    [Fact]
    public void InTheSegmentedPresentation_TheDisallowedSegments_AreDisabledAndSkipped()
    {
        var cut = RenderDial(presentation: AutonomyDialPresentation.Segmented, value: 1, maxAllowed: 1);

        var third = cut.Find("[data-testid='autonomy-segmented'] [role='radio'][data-value='2']");
        third.HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='autonomy-limit']").TextContent.Should().Contain("Ask when risky");
    }

    // ── Locks ────────────────────────────────────────────────────────────────

    [Fact]
    public void ALock_ShowsTheAction_TheSourceAndTheReason_WithALockIcon()
    {
        var cut = RenderDial(locks: Locks());

        var locks = cut.FindAll("[data-testid='autonomy-lock']");
        locks.Should().HaveCount(2);

        var first = locks[0];
        first.GetAttribute("data-source").Should().Be("Tenant policy");
        first.QuerySelector("[data-testid='autonomy-lock-action']")!.TextContent
            .Should().Be("Send external email");
        first.QuerySelector("[data-testid='autonomy-lock-source']")!.TextContent
            .Should().Be("Enforced by Tenant policy");
        first.QuerySelector("[data-testid='autonomy-lock-reason']")!.TextContent
            .Should().Be("Outbound mail needs a human sign-off.");

        first.QuerySelector("[data-testid='autonomy-lock-icon']")!.GetAttribute("aria-hidden")
            .Should().Be("true", "the lock glyph repeats the source sentence, it never carries it alone");
    }

    [Fact]
    public void ALockWithoutASource_PrintsNoEnforcedByLine()
    {
        var cut = RenderDial(locks: Locks());

        var second = cut.FindAll("[data-testid='autonomy-lock']")[1];
        second.QuerySelector("[data-testid='autonomy-lock-action']")!.TextContent.Should().Be("Delete a file");
        second.QuerySelector("[data-testid='autonomy-lock-source']").Should().BeNull();
        second.QuerySelector("[data-testid='autonomy-lock-reason']").Should().BeNull();
    }

    [Fact]
    public void TheLocksList_IsNamed_AndIsNotAFocusStop()
    {
        var cut = RenderDial(locks: Locks());

        var list = cut.Find("[data-testid='autonomy-locks']");
        list.GetAttribute("aria-label").Should().Be("Enforced limits");
        list.QuerySelectorAll("[tabindex]").Should().BeEmpty("a lock is a fact, not a control");
    }

    [Fact]
    public void NoLocks_RenderNoList()
    {
        var cut = RenderDial();

        cut.FindAll("[data-testid='autonomy-locks']").Should().BeEmpty();
    }

    // ── Confirmation ─────────────────────────────────────────────────────────

    [Fact]
    public void AChangeAtOrAboveTheThreshold_WaitsForConfirmation()
    {
        var cut = RenderDial(value: 1, confirmAbove: 2);

        Press(cut, "ArrowRight");

        _changeCount.Should().Be(0, "the value is not committed before the reader confirms");
        ValueNow(cut).Should().Be("1");
        cut.Find("[data-testid='autonomy-confirm']").Should().NotBeNull();
        cut.Find("[data-testid='autonomy-confirm-message']").TextContent
            .Should().Contain("Act freely", "the confirmation names the level it is about to commit");
    }

    [Fact]
    public void Confirming_CommitsTheChange_AndClosesTheConfirmation()
    {
        var cut = RenderDial(value: 1, confirmAbove: 2);

        Press(cut, "ArrowRight");
        cut.Find("[data-testid='autonomy-confirm-accept']").Click();

        _reportedValue.Should().Be(2);
        ValueNow(cut).Should().Be("2");
        cut.FindAll("[data-testid='autonomy-confirm']").Should().BeEmpty();
        cut.Find("[data-testid='autonomy-announcement']").TextContent.Should().Be("Autonomy level: Act freely");
    }

    [Fact]
    public void Cancelling_KeepsTheOldLevel_AndHandsTheFocusBackToTheSlider()
    {
        var cut = RenderDial(value: 1, confirmAbove: 2);

        Press(cut, "ArrowRight");
        var focusBefore = JSInterop.Invocations[FocusInvocation].Count;

        cut.Find("[data-testid='autonomy-confirm-cancel']").Click();

        _changeCount.Should().Be(0);
        ValueNow(cut).Should().Be("1");
        cut.FindAll("[data-testid='autonomy-confirm']").Should().BeEmpty();
        JSInterop.Invocations[FocusInvocation].Count.Should().Be(focusBefore + 1,
            "a control that disappears must not drop the reader's focus onto the document");
    }

    [Fact]
    public void EscapeCancelsTheConfirmation()
    {
        var cut = RenderDial(value: 1, confirmAbove: 2);

        Press(cut, "ArrowRight");
        cut.Find("[data-testid='autonomy-confirm']")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        _changeCount.Should().Be(0);
        cut.FindAll("[data-testid='autonomy-confirm']").Should().BeEmpty();
        ValueNow(cut).Should().Be("1");
    }

    [Fact]
    public void AChangeBelowTheThreshold_IsCommittedImmediately()
    {
        var cut = RenderDial(value: 0, confirmAbove: 2);

        Press(cut, "ArrowRight");

        _reportedValue.Should().Be(1);
        cut.FindAll("[data-testid='autonomy-confirm']").Should().BeEmpty();
    }

    [Fact]
    public void WithNoThreshold_EveryChangeIsImmediate()
    {
        var cut = RenderDial(value: 1, confirmAbove: null);

        Press(cut, "ArrowRight");

        _reportedValue.Should().Be(2);
        cut.FindAll("[data-testid='autonomy-confirm']").Should().BeEmpty();
    }

    [Fact]
    public void TheConfirmationIsAnnouncedByTheMessageItself_NotByASecondLiveRegion()
    {
        var cut = RenderDial(value: 1, confirmAbove: 2);

        Press(cut, "ArrowRight");

        cut.FindAll("[role='status']").Should().HaveCount(1, "the surface owns exactly one live region");
        cut.Find("[data-testid='autonomy-confirm']").GetAttribute("aria-live").Should().BeNull();
    }

    // ── The single live region and the read-out ──────────────────────────────

    [Fact]
    public void ACommittedChange_IsAnnouncedOnce()
    {
        var cut = RenderDial(value: 0);

        Press(cut, "ArrowRight");
        cut.Find("[data-testid='autonomy-announcement']").TextContent
            .Should().Be("Autonomy level: Ask when risky");
        cut.FindAll("[data-testid='autonomy-announcement']").Should().HaveCount(1);
        cut.Find("[data-testid='autonomy-announcement']").GetAttribute("aria-live").Should().Be("polite");

        Press(cut, "ArrowRight");
        cut.Find("[data-testid='autonomy-announcement']").TextContent
            .Should().Be("Autonomy level: Act freely");
    }

    [Fact]
    public void TheReadOut_FollowsTheValue()
    {
        var cut = RenderDial(value: 0);

        cut.Find("[data-testid='autonomy-current']").TextContent.Should().Be("Ask first");
        cut.Find("[data-testid='autonomy-current-description']").TextContent
            .Should().Be("Every action waits for your approval.");

        Press(cut, "End");

        cut.Find("[data-testid='autonomy-current']").TextContent.Should().Be("Act freely");
        cut.Find("[data-testid='autonomy-current-description']").TextContent
            .Should().Be("The agent acts without asking.");
        cut.Find("[data-testid='autonomy-current-position']").TextContent.Should().Be("Level 3 of 3");
    }

    [Fact]
    public void TheTitle_UsesTheRequestedHeadingLevel_AndTheHostsWords()
    {
        var cut = RenderDial(headingLevel: 4, title: "Agent autonomy");

        var title = cut.Find("[data-testid='autonomy-title']");
        title.TagName.Should().Be("H4");
        title.TextContent.Should().Be("Agent autonomy");
    }

    [Fact]
    public void TheHeadingLevel_IsClampedToASectioningLevel()
    {
        var cut = RenderDial(headingLevel: 9);

        cut.Find("[data-testid='autonomy-title']").TagName.Should().Be("H6");
    }

    [Fact]
    public void EveryUserFacingString_ComesFromTheLocalizer()
    {
        var cut = RenderDial(locks: Locks(), maxAllowed: 1);

        Press(cut, "End");

        cut.Markup.Should().NotMatchRegex("\\[Tm[A-Za-z0-9_]+\\]",
            "a bracket-wrapped key is an unresolved string, never a label a reader should see");
    }

    /// <summary>One extra press on the slider re-announces the SAME sentence (a live region announces a mutation).</summary>
    [Fact]
    public void ARepeatedAnnouncement_IsStillAnnounced()
    {
        var cut = RenderDial(value: 0);

        Press(cut, "ArrowRight");
        var first = cut.Find("[data-testid='autonomy-announcement']").TextContent;

        Press(cut, "ArrowRight");
        Press(cut, "ArrowLeft");

        cut.Find("[data-testid='autonomy-announcement']").TextContent.Replace("\u200B", string.Empty)
            .Should().Be(first, "the same sentence is re-announced through an invisible marker");
    }

    /// <summary>
    /// The presentation rules stay INSIDE the package: every sibling <c>*Presentation</c> helper in
    /// <c>Tempo.Blazor.AI</c> is <c>internal</c>, and a public static class would turn shapes like <c>LevelTone</c> into a
    /// 2.10.0 API promise no host asked for. The enum a host must name stays public.
    /// </summary>
    [Fact]
    public void ThePresentationHelper_IsInternal_WhileThePresentationEnumStaysPublic()
    {
        var helper = typeof(TmAutonomyDial).Assembly
            .GetType("Tempo.Blazor.Components.AI.AutonomyPresentation", throwOnError: false);

        helper.Should().NotBeNull("AutonomyPresentation still backs the dial");
        helper!.IsPublic.Should().BeFalse("it is a presentation detail, not a public API promise");
        typeof(AutonomyDialPresentation).IsPublic.Should().BeTrue("a host names the presentation it wants");
    }
}
