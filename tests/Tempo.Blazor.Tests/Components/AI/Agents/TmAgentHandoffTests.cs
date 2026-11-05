using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 5 — <c>TmAgentHandoff</c> (Tempo.Blazor.AI): a run handed from agent to agent as ONE chain, with
/// what each step passed on, what it lost and why, its acceptance state and a chain-wide loss summary.
/// <para>
/// The tests read behaviour, not markup: the chain is an ordered list whose steps are labelled sections, every state and
/// every item kind is an icon AND a localized word AND a tone (never colour alone), the two item lists live in a detail
/// region a native button opens and closes (<c>aria-expanded</c> + <c>aria-controls</c>), the loss summary names the
/// pieces that were dropped across the whole chain, the table view carries the same facts without the geometry, and a
/// step the host removes while it holds the focus hands the focus to the next one and says so once.
/// </para>
/// </summary>
public class TmAgentHandoffTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private static readonly DateTimeOffset Ten = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    private IReadOnlyList<string>? _expanded;
    private AgentHandoffViewMode? _viewMode;
    private int _expandedChangeCount;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static HandoffItem Item(string label, HandoffItemKind kind = HandoffItemKind.Context, string? reason = null)
        => new(label, kind) { Reason = reason };

    private static HandoffStep Step(
        string id,
        HandoffStatus status = HandoffStatus.Accepted,
        string summary = "The invoice run moves to the matcher",
        IReadOnlyList<HandoffItem>? passed = null,
        IReadOnlyList<HandoffItem>? lost = null,
        bool withAgents = true)
        => new(id, summary)
        {
            FromAgent = withAgents ? new AgentRef("Fetcher", "f-" + id) { ColorIndex = 1 } : null,
            ToAgent = withAgents ? new AgentRef("Matcher", "t-" + id) { ColorIndex = 4 } : null,
            FromAgentId = withAgents ? null : "w-from",
            ToAgentId = withAgents ? null : "w-to",
            At = Ten.AddMinutes(int.Parse(id.TrimStart('s'), CultureInfo.InvariantCulture)),
            Status = status,
            PassedItems = passed ?? [Item("the task statement"), Item("invoice schema", HandoffItemKind.Artifact)],
            LostItems = lost ?? [],
        };

    /// <summary>Three steps: completed with one loss, accepted with nothing lost, pending with two losses.</summary>
    private static IReadOnlyList<HandoffStep> Steps() =>
    [
        Step("s1", HandoffStatus.Completed,
            passed: [Item("the task statement"), Item("invoice schema", HandoffItemKind.Artifact)],
            lost: [Item("conversation history", reason: "the context window was full")]),
        Step("s2", HandoffStatus.Accepted),
        Step("s3", HandoffStatus.Pending,
            passed: [],
            lost:
            [
                Item("the buyer's deadline", HandoffItemKind.Memory, "it was never recorded"),
                Item("the write-off rule", HandoffItemKind.Instruction, "the instruction was truncated"),
            ]),
    ];

    private IRenderedComponent<TmAgentHandoff> RenderHandoff(
        IReadOnlyList<HandoffStep>? steps = null,
        HandoffOrientation orientation = HandoffOrientation.Vertical,
        AgentHandoffViewMode viewMode = AgentHandoffViewMode.Chain,
        bool showTitle = true,
        bool showSummary = true,
        int headingLevel = 3,
        string? title = null,
        IReadOnlyList<string>? expanded = null,
        CultureInfo? culture = null)
    {
        _expanded = null;
        _viewMode = null;
        _expandedChangeCount = 0;

        return Render<TmAgentHandoff>(parameters =>
        {
            parameters.Add(component => component.Steps, steps ?? Steps());
            parameters.Add(component => component.Orientation, orientation);
            parameters.Add(component => component.ViewMode, viewMode);
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.ShowSummary, showSummary);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.Culture, culture ?? CultureInfo.GetCultureInfo("en-US"));
            parameters.Add(component => component.ViewModeChanged, (AgentHandoffViewMode mode) => _viewMode = mode);
            parameters.Add(component => component.ExpandedStepIdsChanged, (IReadOnlyList<string>? ids) =>
            {
                _expanded = ids;
                _expandedChangeCount++;
            });

            if (title is not null)
            {
                parameters.Add(component => component.Title, title);
            }

            if (expanded is not null)
            {
                parameters.Add(component => component.ExpandedStepIds, expanded);
            }
        });
    }

    private static Task UpdateAsync(
        IRenderedComponent<TmAgentHandoff> cut,
        params (string Name, object? Value)[] values)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            values.ToDictionary(value => value.Name, value => value.Value))));

    private static AngleSharp.Dom.IElement StepEl(IRenderedComponent<TmAgentHandoff> cut, string id)
        => cut.Find($"[data-testid='agent-handoff-step'][data-step-id='{id}']");

    private static string TextOf(IRenderedComponent<TmAgentHandoff> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static string Announcement(IRenderedComponent<TmAgentHandoff> cut)
        => cut.Find("[data-testid='agent-handoff-announcement']").TextContent;

    // ── Header ───────────────────────────────────────────────────────────────

    [Fact]
    public void TheHeader_RendersTheHeadingAtTheRequestedLevel()
    {
        var cut = RenderHandoff(title: "Invoice run", headingLevel: 2);

        var heading = cut.Find("[data-testid='agent-handoff-title']");
        heading.TagName.Should().Be("H2");
        heading.TextContent.Should().Be("Invoice run");
    }

    [Fact]
    public void TheHeadingLevel_IsClamped_AndTheDefaultTitleIsLocalized()
    {
        var cut = RenderHandoff(headingLevel: 1);

        var heading = cut.Find("[data-testid='agent-handoff-title']");
        heading.TagName.Should().Be("H2", "a section heading never starts the page outline");
        heading.TextContent.Should().Be("Agent handoffs");
    }

    [Fact]
    public void TheHeadingCanBeHidden_WithoutLosingTheChainLabel()
    {
        var cut = RenderHandoff(showTitle: false);

        cut.FindAll("[data-testid='agent-handoff-title']").Should().BeEmpty();
        cut.Find("[data-testid='agent-handoff-chain']").GetAttribute("aria-label")
            .Should().Be("Handoff chain", "the list keeps a name of its own when the heading is gone");
    }

    // ── Summary ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheSummary_CountsTheSettledSteps_AndSumsTheItems()
    {
        var cut = RenderHandoff();

        TextOf(cut, "agent-handoff-summary-steps").Should().Be("2/3",
            "two of the three handoffs were answered (completed and accepted)");
        TextOf(cut, "agent-handoff-summary-passed").Should().Be("4");
        TextOf(cut, "agent-handoff-summary-lost").Should().Be("3");
        TextOf(cut, "agent-handoff-summary-rate").Should().Be("43%");
        TextOf(cut, "agent-handoff-summary-state").Should().Be("1 still pending");
    }

    [Fact]
    public void TheSummary_SaysWhenEverythingIsSettled()
    {
        var cut = RenderHandoff(
        [
            Step("s1", HandoffStatus.Completed, lost: []),
            Step("s2", HandoffStatus.Accepted),
        ]);

        TextOf(cut, "agent-handoff-summary-state").Should().Be("Every handoff is settled");
        TextOf(cut, "agent-handoff-summary-steps").Should().Be("2/2");
        TextOf(cut, "agent-handoff-summary-rate").Should().Be("0%");
    }

    [Fact]
    public void TheSummaryCanBeHidden()
    {
        var cut = RenderHandoff(showSummary: false);

        cut.FindAll("[data-testid='agent-handoff-summary']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-handoff-step']").Should().HaveCount(3);
    }

    // ── The chain ────────────────────────────────────────────────────────────

    [Fact]
    public void TheChain_IsAnOrderedListOfIdentifiedSteps()
    {
        var cut = RenderHandoff();

        var chain = cut.Find("[data-testid='agent-handoff-chain']");
        chain.TagName.Should().Be("OL");
        chain.GetAttribute("role").Should().Be("list");
        chain.GetAttribute("aria-describedby").Should().NotBeNullOrEmpty(
            "the keyboard instructions describe the chain");

        var steps = cut.FindAll("[data-testid='agent-handoff-step']");
        steps.Should().HaveCount(3);
        steps.Select(step => step.GetAttribute("data-step-id")).Should().Equal("s1", "s2", "s3");
        steps.Select(step => step.GetAttribute("data-status")).Should()
            .Equal("completed", "accepted", "pending");
        steps.Should().OnlyContain(step => step.TagName == "LI");
        steps.Should().OnlyContain(step => step.GetAttribute("role") == "listitem");
    }

    [Fact]
    public void EachStep_NamesItsRouteThroughTheSharedAgentIdentity()
    {
        var cut = RenderHandoff();

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-from']")!.TextContent
            .Should().Contain("Fetcher");
        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-to']")!.TextContent
            .Should().Contain("Matcher");
        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-summary']")!.TextContent
            .Should().Contain("The invoice run moves to the matcher");
    }

    [Fact]
    public void AStepWithoutAnAgentRef_PrintsTheRawIds_AndAFallbackWhenItHasNone()
    {
        var cut = RenderHandoff(
        [
            Step("s1", withAgents: false),
            new HandoffStep("s2", "  "),
        ]);

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-from']")!.TextContent
            .Should().Contain("w-from");
        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-to']")!.TextContent
            .Should().Contain("w-to");

        StepEl(cut, "s2").QuerySelector("[data-testid='agent-handoff-step-from']")!.TextContent
            .Should().Contain("Unknown agent");
        StepEl(cut, "s2").QuerySelector("[data-testid='agent-handoff-step-summary']")!.TextContent
            .Should().Contain("Untitled handoff");
    }

    [Fact]
    public void TheAgentsArePaintedThroughTheSharedBadge_AndARawIdIsNot()
    {
        var withAgents = RenderHandoff();

        withAgents.FindComponents<TmAgentBadge>().Should().HaveCount(6,
            "every step paints both identities through the shared badge, so the palette and the initials agree with "
            + "every other agent surface");

        var rawIds = RenderHandoff([Step("s1", withAgents: false)]);

        rawIds.FindComponents<TmAgentBadge>().Should().BeEmpty(
            "a raw agent id carries no identity to paint");
    }

    [Theory]
    [InlineData(HandoffStatus.Pending, "pending", "Pending")]
    [InlineData(HandoffStatus.Accepted, "accepted", "Accepted")]
    [InlineData(HandoffStatus.Rejected, "rejected", "Rejected")]
    [InlineData(HandoffStatus.Completed, "completed", "Completed")]
    public void AStatus_IsAnIconAWordAndATone(HandoffStatus status, string tone, string word)
    {
        var cut = RenderHandoff([Step("s1", status)]);

        var chip = StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-status']")!;
        chip.GetAttribute("data-status").Should().Be(tone);
        chip.QuerySelector("svg").Should().NotBeNull("the status is never carried by colour alone");
        chip.TextContent.Trim().Should().Be(word);
    }

    [Fact]
    public void AnUnknownStatus_FallsBackToPending_InsteadOfThrowing()
    {
        var cut = RenderHandoff([Step("s1", (HandoffStatus)17)]);

        StepEl(cut, "s1").GetAttribute("data-status").Should().Be("pending");
        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-status']")!.TextContent.Trim()
            .Should().Be("Pending");
    }

    [Fact]
    public void TheConnectorBetweenSteps_IsDecorative()
    {
        var cut = RenderHandoff();

        var connector = cut.FindAll("[data-testid='agent-handoff-connector']");
        connector.Should().HaveCount(2, "three steps are joined by two arrows and the last step ends the chain");
        connector.Should().OnlyContain(element => element.GetAttribute("aria-hidden") == "true");
        connector.Should().OnlyContain(element => element.QuerySelector("svg") != null);
    }

    // ── The detail, the passed and the lost lists ────────────────────────────

    [Fact]
    public void TheItemsAreOpenByDefault_AndEachOneIsAKindAWordAndATone()
    {
        var cut = RenderHandoff();

        var passed = StepEl(cut, "s1").QuerySelectorAll("[data-testid='agent-handoff-passed-item']");
        passed.Should().HaveCount(2);
        passed[1].GetAttribute("data-kind").Should().Be("artifact");
        passed[1].TextContent.Should().Contain("invoice schema");
        passed[1].TextContent.Should().Contain("Artifact");
        passed[1].QuerySelector("svg").Should().NotBeNull("a kind is an icon and a word, never a colour");
    }

    [Fact]
    public void ALostItem_CarriesItsReason()
    {
        var cut = RenderHandoff();

        var lost = StepEl(cut, "s1").QuerySelectorAll("[data-testid='agent-handoff-lost-item']");
        lost.Should().HaveCount(1);
        lost[0].TextContent.Should().Contain("conversation history");
        lost[0].TextContent.Should().Contain("Context");
        lost[0].QuerySelector("[data-testid='agent-handoff-lost-reason']")!.TextContent
            .Should().Contain("the context window was full");
    }

    [Fact]
    public void ALostItemWithoutAReason_PrintsNoReasonLine()
    {
        var cut = RenderHandoff([Step("s1", lost: [Item("tool catalogue", HandoffItemKind.Tool)])]);

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-lost-item']")!.TextContent
            .Should().Contain("tool catalogue");
        StepEl(cut, "s1").QuerySelectorAll("[data-testid='agent-handoff-lost-reason']").Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyList_SaysSoInsteadOfRenderingNothing()
    {
        var cut = RenderHandoff([Step("s1", passed: [], lost: [])]);

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-passed-empty']")!.TextContent
            .Should().Contain("Nothing was passed on");
        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-lost-empty']")!.TextContent
            .Should().Contain("Nothing was lost");
    }

    [Fact]
    public void TheDetailToggle_IsANativeButton_ThatOpensAndClosesItsOwnRegion()
    {
        var cut = RenderHandoff();

        var toggle = StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-toggle']")!;
        toggle.TagName.Should().Be("BUTTON");
        toggle.GetAttribute("type").Should().Be("button");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        toggle.GetAttribute("aria-label").Should().Be("Handoff 1 details");

        var detailId = toggle.GetAttribute("aria-controls");
        detailId.Should().NotBeNullOrEmpty();
        StepEl(cut, "s1").QuerySelector($"[data-testid='agent-handoff-step-detail']")!.GetAttribute("id")
            .Should().Be(detailId);

        toggle.Click();

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-toggle']")!
            .GetAttribute("aria-expanded").Should().Be("false");
        StepEl(cut, "s1").QuerySelectorAll("[data-testid='agent-handoff-step-detail']").Should().BeEmpty();
        _expandedChangeCount.Should().Be(1);
        _expanded.Should().BeEquivalentTo(new[] { "s2", "s3" },
            "the collapsed step is the only one removed from the expanded set");

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-toggle']")!.Click();

        StepEl(cut, "s1").QuerySelectorAll("[data-testid='agent-handoff-step-detail']").Should().HaveCount(1);
        _expanded.Should().BeEquivalentTo("s1", "s2", "s3");
    }

    [Fact]
    public void TheToggleDoesNotEmulateKeys_SoTheBrowserActivatesItExactlyOnce()
    {
        var cut = RenderHandoff();
        var toggle = StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-toggle']")!;

        toggle.TagName.Should().Be("BUTTON");
        toggle.HasAttribute("onkeydown").Should().BeFalse();

        Action pressingEnter = () => toggle.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        pressingEnter.Should().Throw<MissingEventHandlerException>(
            "a native button activates itself on Enter and Space; a key handler here would toggle it twice");
        _expandedChangeCount.Should().Be(0);

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-toggle']")!.Click();

        _expandedChangeCount.Should().Be(1, "one click is one toggle");
    }

    [Fact]
    public void TheHostCanPinWhichStepsAreOpen()
    {
        var cut = RenderHandoff(expanded: ["s2"]);

        StepEl(cut, "s1").QuerySelectorAll("[data-testid='agent-handoff-step-detail']").Should().BeEmpty();
        StepEl(cut, "s2").QuerySelectorAll("[data-testid='agent-handoff-step-detail']").Should().HaveCount(1);
        StepEl(cut, "s3").QuerySelectorAll("[data-testid='agent-handoff-step-detail']").Should().BeEmpty();

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-toggle']")!.Click();

        _expanded.Should().BeEquivalentTo(new[] { "s1", "s2" },
            "the reader opened another step without closing the pinned one");
    }

    [Fact]
    public void AnEmptyExpandedSet_ClosesEveryStep()
    {
        var cut = RenderHandoff(expanded: []);

        cut.FindAll("[data-testid='agent-handoff-step-detail']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-handoff-step-toggle']")
            .Should().OnlyContain(toggle => toggle.GetAttribute("aria-expanded") == "false");
    }

    // ── The loss summary ─────────────────────────────────────────────────────

    [Fact]
    public void AStepWithoutLosses_DoesNotWearTheLossWash()
    {
        var cut = RenderHandoff([Step("s1", lost: []), Step("s2", lost: [Item("the deadline")])]);

        StepEl(cut, "s1").GetAttribute("data-has-losses").Should().Be("false",
            "tinting 'nothing was lost' in the danger colour would be a colour-only verdict on an empty list");
        StepEl(cut, "s2").GetAttribute("data-has-losses").Should().Be("true");
    }

    [Fact]
    public void TheLossSummary_NamesWhatWasDroppedAcrossTheWholeChain()
    {
        var cut = RenderHandoff();

        TextOf(cut, "agent-handoff-lost-summary").Should()
            .Be("Lost across the chain: conversation history, the buyer's deadline, the write-off rule");
    }

    [Fact]
    public void AChainWithoutLosses_SaysSo()
    {
        var cut = RenderHandoff([Step("s1", lost: []), Step("s2", lost: [])], showSummary: false);

        TextOf(cut, "agent-handoff-lost-summary").Should().Be("Nothing was lost across the chain.");
    }

    // ── Orientation ──────────────────────────────────────────────────────────

    [Fact]
    public void TheOrientation_IsAHookOnTheRoot_NotASecondMarkupShape()
    {
        var vertical = RenderHandoff(orientation: HandoffOrientation.Vertical);
        vertical.Find("[data-testid='agent-handoff']").GetAttribute("data-orientation").Should().Be("vertical");
        vertical.Find("[data-testid='agent-handoff']").ClassList.Should().Contain("tm-ai-handoff--vertical");

        var horizontal = RenderHandoff(orientation: HandoffOrientation.Horizontal);
        horizontal.Find("[data-testid='agent-handoff']").GetAttribute("data-orientation").Should().Be("horizontal");
        horizontal.Find("[data-testid='agent-handoff']").ClassList.Should().Contain("tm-ai-handoff--horizontal");
        horizontal.FindAll("[data-testid='agent-handoff-step']").Should().HaveCount(3,
            "the horizontal chain reads the same steps in the same order");
    }

    // ── The table alternative ────────────────────────────────────────────────

    [Fact]
    public void TheTableView_ReplacesTheChain_AndCarriesTheSameFacts()
    {
        var cut = RenderHandoff(viewMode: AgentHandoffViewMode.Table);

        cut.FindAll("[data-testid='agent-handoff-chain']").Should().BeEmpty("the table replaces the drawing");
        var table = cut.Find("[data-testid='agent-handoff-table']");
        table.QuerySelector("caption")!.TextContent.Trim().Should().Be("Handoff steps");

        var rows = cut.FindAll("[data-testid='agent-handoff-table-row']");
        rows.Should().HaveCount(3);
        rows[0].TextContent.Should().Contain("Fetcher");
        rows[0].TextContent.Should().Contain("Matcher");
        rows[0].TextContent.Should().Contain("Completed");
        rows[0].TextContent.Should().Contain("the task statement",
            "the passed items are carried as text, not only as a count");
        rows[0].TextContent.Should().Contain("the context window was full",
            "a lost item's reason is part of the text alternative");
    }

    [Fact]
    public void TheTableRows_AreIdentifiedByStep()
    {
        var cut = RenderHandoff(viewMode: AgentHandoffViewMode.Table);

        cut.FindAll("[data-testid='agent-handoff-table-row']")
            .Select(row => row.GetAttribute("data-step-id")).Should().Equal("s1", "s2", "s3");
    }

    [Fact]
    public void TheViewSwitcher_ReportsTheNewPresentation_AndTheHostCanPinIt()
    {
        var cut = RenderHandoff();

        var switcher = cut.FindComponents<TmSegmentedControl<AgentHandoffViewMode>>().Single();
        switcher.Instance.Options.Select(option => option.Value).Should()
            .Equal(AgentHandoffViewMode.Chain, AgentHandoffViewMode.Table);

        cut.FindAll("[data-testid='agent-handoff-view'] button")[1].Click();

        _viewMode.Should().Be(AgentHandoffViewMode.Table);

        cut.FindAll("[data-testid='agent-handoff-table']").Should().NotBeEmpty(
            "the reader's pick paints the table before the host echoes it back");
        cut.FindAll("[data-testid='agent-handoff-chain']").Should().BeEmpty();
    }

    [Fact]
    public void TheViewModeParameter_MovesThePresentation()
    {
        var cut = RenderHandoff(viewMode: AgentHandoffViewMode.Table);

        cut.FindAll("[data-testid='agent-handoff-table']").Should().NotBeEmpty();

        UpdateAsync(cut, (nameof(TmAgentHandoff.ViewMode), AgentHandoffViewMode.Chain));

        cut.FindAll("[data-testid='agent-handoff-chain']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='agent-handoff-table']").Should().BeEmpty();
    }

    // ── Empty, focus and hooks ───────────────────────────────────────────────

    [Fact]
    public void AnEmptyChain_RendersTheLocalizedEmptyState()
    {
        var cut = RenderHandoff(steps: []);

        TextOf(cut, "agent-handoff-empty").Should().Be("No handoffs to show yet.");
        cut.FindAll("[data-testid='agent-handoff-step']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-handoff-chain']").Should().BeEmpty();
        cut.FindAll("[data-testid='agent-handoff-summary']").Should().BeEmpty(
            "there is nothing to summarise");
    }

    [Fact]
    public void NullSteps_ReadAsAnEmptyChain()
    {
        var cut = Render<TmAgentHandoff>(parameters =>
            parameters.Add(component => component.Steps, (IReadOnlyList<HandoffStep>?)null));

        cut.FindAll("[data-testid='agent-handoff-empty']").Should().NotBeEmpty();
    }

    [Fact]
    public void AStepRemovedWhileItHeldTheFocus_HandsTheFocusOn_AndSaysWhyOnce()
    {
        var cut = RenderHandoff();

        StepEl(cut, "s2").QuerySelector("[data-testid='agent-handoff-step-toggle']")!.Focus();
        Announcement(cut).Should().BeEmpty();

        UpdateAsync(cut, (nameof(TmAgentHandoff.Steps), new[] { Steps()[0], Steps()[2] }));

        cut.WaitForAssertion(() => Announcement(cut).Should().Contain("no longer shown"));
        JSInterop.Invocations[FocusInvocation].Should().NotBeEmpty(
            "the focus was moved off the body to the step that took the removed one's place");
    }

    [Fact]
    public void RemovingAStepThatDidNotHoldTheFocus_AnnouncesNothing()
    {
        var cut = RenderHandoff();

        UpdateAsync(cut, (nameof(TmAgentHandoff.Steps), new[] { Steps()[0], Steps()[1] }));

        Announcement(cut).Should().BeEmpty();
        JSInterop.Invocations[FocusInvocation].Should().BeEmpty();
    }

    [Fact]
    public void TheTimeIsCultureFormatted()
    {
        var culture = CultureInfo.GetCultureInfo("cs-CZ");
        var cut = RenderHandoff(culture: culture);

        var expected = Ten.AddMinutes(1).ToLocalTime().ToString("t", culture);

        StepEl(cut, "s1").QuerySelector("[data-testid='agent-handoff-step-at']")!.TextContent
            .Should().Contain(expected, "the clock follows the culture the host bound, not the machine's format");
    }

    [Fact]
    public void TheRootCarriesTheHostHooks()
    {
        var cut = Render<TmAgentHandoff>(parameters =>
        {
            parameters.Add(component => component.Steps, Steps());
            parameters.Add(component => component.Class, "my-handoff");
            parameters.Add(component => component.DataTestId, "handoff-under-test");
            parameters.Add(component => component.TestIdPrefix, "carrier");
            parameters.AddUnmatched("data-scenario", "review");
        });

        var root = cut.Find("[data-testid='handoff-under-test']");
        root.ClassList.Should().Contain("tm-ai-handoff");
        root.ClassList.Should().Contain("my-handoff");
        root.GetAttribute("data-scenario").Should().Be("review");
        cut.FindAll("[data-testid='carrier-agent-handoff-step']").Should().HaveCount(3);
    }

    [Fact]
    public void TheSurfaceHasOnePoliteLiveRegion()
    {
        var cut = RenderHandoff();

        cut.FindAll("[aria-live]").Should().HaveCount(1);
        cut.Find("[aria-live]").GetAttribute("aria-live").Should().Be("polite");
        cut.Find("[aria-live]").GetAttribute("role").Should().Be("status");
    }
}
