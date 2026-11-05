using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 4 — <c>TmParallelAttempts</c> (Tempo.Blazor.AI): several attempts at the SAME task side by
/// side, with their scores, their votes and a winner the reader picks and confirms.
/// <para>
/// The tests read behaviour, not markup: the attempts are columns of ONE labelled radiogroup (native radios, so the
/// arrows walk the choices for free), the best attempt is marked with an icon AND a word (the host's explicit pick
/// first, the highest score otherwise), picking a winner is a two-step action — choose, then confirm — that raises
/// <c>OnSelect</c> and hands the focus back to the radio, the vote rationales open per attempt, and the comparison
/// table carries exactly the same numbers as text.
/// </para>
/// </summary>
public class TmParallelAttemptsTests : LocalizationTestBase
{
    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private string? _selected;
    private string? _bound;
    private int _selectCount;

    // ── Fixtures ─────────────────────────────────────────────────────────────

    private static AttemptVote Vote(string voter, AttemptVoteOutcome outcome, double? score = null, string? rationale = null)
        => new(voter) { Outcome = outcome, Score = score, Rationale = rationale };

    private static ParallelAttempt Attempt(
        string id,
        string? label = null,
        double? score = null,
        AgentActivityStatus status = AgentActivityStatus.Succeeded,
        string? summary = null,
        params AttemptVote[] votes)
        => new(id, label ?? "Draft " + id)
        {
            Score = score,
            Status = status,
            Summary = summary,
            Votes = votes,
        };

    /// <summary>Three attempts: the second has the highest score and two votes with rationales.</summary>
    private static IReadOnlyList<ParallelAttempt> Attempts() =>
    [
        Attempt("a", "Draft A", 0.61,
            votes: [Vote("Reviewer", AttemptVoteOutcome.Approve, 0.6, "Clear, but misses the edge case")]),
        Attempt("b", "Draft B", 0.93,
            summary: "Covers the edge case and the empty list",
            votes:
            [
                Vote("Reviewer", AttemptVoteOutcome.Approve, 0.9, "Covers the edge case"),
                Vote("Grader", AttemptVoteOutcome.Approve, 0.96),
            ]),
        Attempt("c", "Draft C", 0.42, votes: [Vote("Reviewer", AttemptVoteOutcome.Reject, 0.4, "Drops the second half")]),
    ];

    private IRenderedComponent<TmParallelAttempts> RenderAttempts(
        IReadOnlyList<ParallelAttempt>? attempts = null,
        string? title = null,
        bool showTitle = true,
        int headingLevel = 3,
        string? bestAttemptId = null,
        string? selectedAttemptId = null,
        bool showComparison = true,
        bool confirmSelection = true,
        bool bindSelection = true,
        RenderFragment<ParallelAttempt>? contentTemplate = null)
    {
        _selected = null;
        _bound = null;
        _selectCount = 0;

        return Render<TmParallelAttempts>(parameters =>
        {
            parameters.Add(component => component.Attempts, attempts ?? Attempts());
            parameters.Add(component => component.Culture, CultureInfo.GetCultureInfo("en-US"));
            parameters.Add(component => component.ShowTitle, showTitle);
            parameters.Add(component => component.HeadingLevel, headingLevel);
            parameters.Add(component => component.ShowComparison, showComparison);
            parameters.Add(component => component.ConfirmSelection, confirmSelection);
            parameters.Add(component => component.OnSelect, (string id) =>
            {
                _selected = id;
                _selectCount++;
            });
            if (bindSelection)
            {
                parameters.Add(component => component.SelectedAttemptIdChanged, (string? id) => _bound = id);
            }

            if (title is not null)
            {
                parameters.Add(component => component.Title, title);
            }

            if (bestAttemptId is not null)
            {
                parameters.Add(component => component.BestAttemptId, bestAttemptId);
            }

            if (selectedAttemptId is not null)
            {
                parameters.Add(component => component.SelectedAttemptId, selectedAttemptId);
            }

            if (contentTemplate is not null)
            {
                parameters.Add(component => component.ContentTemplate, contentTemplate);
            }
        });
    }

    private static IReadOnlyList<AngleSharp.Dom.IElement> Columns(IRenderedComponent<TmParallelAttempts> cut)
        => cut.FindAll("[data-testid='parallel-attempt']");

    private static AngleSharp.Dom.IElement Column(IRenderedComponent<TmParallelAttempts> cut, string id)
        => cut.Find($"[data-testid='parallel-attempt'][data-attempt-id='{id}']");

    private static AngleSharp.Dom.IElement Radio(IRenderedComponent<TmParallelAttempts> cut, string id)
        => cut.Find($"[data-testid='parallel-attempt-radio'][data-attempt-id='{id}']");

    private static string Announcement(IRenderedComponent<TmParallelAttempts> cut)
        => cut.Find("[data-testid='parallel-attempts-announcement']").TextContent;

    private List<string?> FocusTargets()
        => JSInterop.Invocations
            .Where(invocation => invocation.Identifier == FocusInvocation)
            .Select(invocation => (invocation.Arguments[0] as ElementReference?)?.Id)
            .ToList();

    // ── The header ───────────────────────────────────────────────────────────

    [Fact]
    public void TheDefaultTitle_IsLocalized_AndSitsAtTheRequestedLevel()
    {
        var cut = RenderAttempts();

        cut.Find("[data-testid='parallel-attempts-title']").TextContent.Should().Be("Parallel attempts");
        cut.Find("[data-testid='parallel-attempts-title']").TagName.Should().Be("H3");
        RenderAttempts(headingLevel: 1).Find("[data-testid='parallel-attempts-title']").TagName.Should().Be("H2",
            "the level is clamped into the range a sectioning element allows");
        RenderAttempts(headingLevel: 9).Find("[data-testid='parallel-attempts-title']").TagName.Should().Be("H6");
    }

    [Fact]
    public void TheHostsTitleWins_AndShowTitleHidesIt()
    {
        RenderAttempts(title: "Three drafts").Find("[data-testid='parallel-attempts-title']").TextContent
            .Should().Be("Three drafts");
        RenderAttempts(showTitle: false).FindAll("[data-testid='parallel-attempts-title']").Should().BeEmpty();
    }

    [Fact]
    public void TheInstructions_NameTheAttemptsOnce()
    {
        var cut = RenderAttempts();

        var instructions = cut.Find("[data-testid='parallel-attempts-instructions']");
        instructions.ClassList.Should().Contain("tm-sr-only");
        cut.FindAll($"[aria-describedby='{instructions.Id}']").Should().ContainSingle(
            "the surface reads its instructions once, not once per nesting level");
    }

    [Fact]
    public void AnEmptyList_RendersTheLocalizedEmptyState()
    {
        var cut = RenderAttempts([]);

        cut.Find("[data-testid='parallel-attempts-empty']").TextContent.Should().Contain("No attempts");
        cut.FindAll("[data-testid='parallel-attempts-track']").Should().BeEmpty();
        cut.FindAll("[data-testid='parallel-attempts-table']").Should().BeEmpty();
    }

    // ── The columns ──────────────────────────────────────────────────────────

    [Fact]
    public void EveryAttemptIsAColumn_WithItsLabelStateScoreAndSummary()
    {
        var cut = RenderAttempts();

        Columns(cut).Select(column => column.GetAttribute("data-attempt-id")).Should().Equal(new[] { "a", "b", "c" });

        var second = Column(cut, "b");
        second.GetAttribute("data-status").Should().Be("succeeded");
        second.QuerySelector("[data-testid='parallel-attempt-label']")!.TextContent.Trim().Should().Be("Draft B");
        second.QuerySelector("[data-testid='parallel-attempt-score']")!.TextContent.Trim().Should().Be("0.93");
        second.QuerySelector("[data-testid='parallel-attempt-state']")!.TextContent.Should().Contain("Succeeded");
        second.QuerySelector("[data-testid='parallel-attempt-summary']")!.TextContent.Trim()
            .Should().Be("Covers the edge case and the empty list");
    }

    [Fact]
    public void AnUnscoredAttempt_SaysSoInsteadOfPrintingZero()
    {
        var cut = RenderAttempts([Attempt("a", "Draft A")]);

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-score']")!.TextContent.Trim()
            .Should().Be("No score");
    }

    [Fact]
    public void AnUnlabelledAttempt_GetsTheNumberedFallback()
    {
        var cut = RenderAttempts([Attempt("a", "  "), Attempt("b", "Draft B")]);

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-label']")!.TextContent.Trim()
            .Should().Be("Attempt 1", "the reader needs a handle for an attempt the host did not name");
        Radio(cut, "a").GetAttribute("aria-label").Should().Contain("Attempt 1");
    }

    [Fact]
    public void TheHostsContentSlot_RendersInsideItsOwnAttempt()
    {
        var cut = RenderAttempts(contentTemplate: attempt => builder =>
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "data-testid", "attempt-body");
            builder.AddContent(2, "body of " + attempt.Id);
            builder.CloseElement();
        });

        cut.FindAll("[data-testid='attempt-body']").Should().HaveCount(3);
        Column(cut, "b").QuerySelector("[data-testid='attempt-body']")!.TextContent.Should().Be("body of b");
    }

    // ── The best attempt ─────────────────────────────────────────────────────

    [Fact]
    public void TheHighestScore_IsMarkedBest_WithAnIconAndAWord()
    {
        var cut = RenderAttempts();

        Column(cut, "b").GetAttribute("data-best").Should().Be("true");
        Column(cut, "a").GetAttribute("data-best").Should().BeNull();

        var badge = Column(cut, "b").QuerySelector("[data-testid='parallel-attempt-best']")!;
        badge.TextContent.Trim().Should().Be("Best");
        badge.QuerySelector("svg").Should().NotBeNull("the mark is never a colour alone");
        Column(cut, "b").GetAttribute("aria-label").Should().Contain("Best attempt");
    }

    [Fact]
    public void TheHostsExplicitPick_OverridesTheScore()
    {
        var cut = RenderAttempts(bestAttemptId: "c");

        Column(cut, "c").GetAttribute("data-best").Should().Be("true");
        Column(cut, "b").GetAttribute("data-best").Should().BeNull("the host's pick is the host's pick");
    }

    [Fact]
    public void NothingIsBest_WhenNothingIsScored()
    {
        var cut = RenderAttempts([Attempt("a", "Draft A"), Attempt("b", "Draft B")]);

        cut.FindAll("[data-testid='parallel-attempt-best']").Should().BeEmpty(
            "an unscored attempt is not a zero-scored one");
    }

    // ── Choosing a winner ────────────────────────────────────────────────────

    [Fact]
    public void TheAttempts_AreOneLabelledGroup_OfNativeRadios()
    {
        var cut = RenderAttempts();

        var track = cut.Find("[data-testid='parallel-attempts-track']");
        track.GetAttribute("role").Should().Be("group",
            "the columns are groups, and a radiogroup may own only radios — a radiogroup here would put a "
            + "required-child violation in front of the columns");
        track.GetAttribute("aria-label").Should().Be("Choose the winning attempt");

        var radios = cut.FindAll("[data-testid='parallel-attempt-radio']");
        radios.Should().HaveCount(3);
        radios.Select(radio => radio.GetAttribute("type")).Should().OnlyContain(type => type == "radio");
        radios.Select(radio => radio.GetAttribute("name")).Distinct().Should().ContainSingle(
            "one name is what makes the arrow keys walk the choices natively");
        radios.Select(radio => radio.GetAttribute("aria-label")).Should()
            .OnlyContain(label => label!.StartsWith("Choose Draft", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSelectedAttempt_IsChecked_AndBadged()
    {
        var cut = RenderAttempts(selectedAttemptId: "a");

        Radio(cut, "a").HasAttribute("checked").Should().BeTrue();
        Radio(cut, "b").HasAttribute("checked").Should().BeFalse();
        Column(cut, "a").GetAttribute("data-selected").Should().Be("true");
        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-chosen']")!.TextContent.Trim()
            .Should().Be("Chosen");
    }

    [Fact]
    public void PickingARadio_AsksForConfirmation_AndRaisesNothingYet()
    {
        var cut = RenderAttempts();

        Radio(cut, "b").Change(true);

        var bar = cut.Find("[data-testid='parallel-attempts-confirm']");
        bar.TextContent.Should().Contain("Use Draft B as the result?");
        bar.GetAttribute("role").Should().Be("group");
        _selected.Should().BeNull("choosing is not confirming");
        _selectCount.Should().Be(0);
        Announcement(cut).Should().Be("Confirm to use Draft B as the result.");
    }

    [Fact]
    public void ConfirmingRaisesTheWinner_AndHandsTheFocusBackToTheRadio()
    {
        var cut = RenderAttempts();
        var radioReference = Radio(cut, "b").GetAttribute("blazor:elementreference");

        Radio(cut, "b").Change(true);
        cut.Find("[data-testid='parallel-attempts-confirm-yes']").Click();

        _selected.Should().Be("b");
        _bound.Should().Be("b", "the winner is two-way bound");
        _selectCount.Should().Be(1);
        cut.FindAll("[data-testid='parallel-attempts-confirm']").Should().BeEmpty("the question is answered");
        Column(cut, "b").GetAttribute("data-selected").Should().Be("true");
        Announcement(cut).Should().Be("Draft B is now the result.");
        FocusTargets()[^1].Should().Be(radioReference,
            "the question's buttons are gone, so the focus returns to the radio that opened it");
    }

    [Fact]
    public void CancellingLeavesTheWinnerAlone_AndRevertsTheRadio()
    {
        var cut = RenderAttempts(selectedAttemptId: "a");

        Radio(cut, "c").Change(true);
        cut.Find("[data-testid='parallel-attempts-confirm-no']").Click();

        _selected.Should().BeNull("nothing was confirmed");
        cut.FindAll("[data-testid='parallel-attempts-confirm']").Should().BeEmpty();
        Radio(cut, "a").HasAttribute("checked").Should().BeTrue("the previous winner is still the winner");
        Column(cut, "c").GetAttribute("data-selected").Should().BeNull();
        Announcement(cut).Should().Be("The selection was cancelled.");
        FocusTargets()[^1].Should().NotBeNullOrEmpty(
            "the focus returns to the confirmed attempt's radio — which the revert has just RE-MOUNTED, so its "
            + "element reference is a new one and only 'the focus moved back into the group' can be asserted here");
    }

    [Fact]
    public void RePickingTheConfirmedWinner_OpensNoQuestion()
    {
        var cut = RenderAttempts(selectedAttemptId: "a");

        // The arrow keys inside the radio group can land back on the confirmed radio, which raises a change.
        Radio(cut, "a").Change(true);

        cut.FindAll("[data-testid='parallel-attempts-confirm']").Should().BeEmpty(
            "there is nothing to confirm when the pick IS the result");
        _selected.Should().BeNull();
        Announcement(cut).Should().BeEmpty("nothing happened, so nothing is announced");
    }

    [Fact]
    public void WithoutConfirmation_ThePickIsTheWinnerImmediately()
    {
        var cut = RenderAttempts(confirmSelection: false);

        Radio(cut, "c").Change(true);

        cut.FindAll("[data-testid='parallel-attempts-confirm']").Should().BeEmpty(
            "a host that opted out of the confirmation gets the choice directly");
        _selected.Should().Be("c");
        _bound.Should().Be("c");
        Column(cut, "c").GetAttribute("data-selected").Should().Be("true");
        Announcement(cut).Should().Be("Draft C is now the result.");
    }

    [Fact]
    public void AHostBoundWinner_IsApplied_WithoutRaisingTheCallback()
    {
        var cut = RenderAttempts(selectedAttemptId: "a");

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmParallelAttempts.Attempts)] = Attempts(),
                [nameof(TmParallelAttempts.SelectedAttemptId)] = "c",
            })));

        Radio(cut, "c").HasAttribute("checked").Should().BeTrue();
        _selected.Should().BeNull("the host already knows what it bound");
    }

    [Fact]
    public void APinnedWinnerTheHostDoesNotBind_IsRestoredOnTheHostsNextRender()
    {
        var cut = RenderAttempts(selectedAttemptId: "a", bindSelection: false);

        Radio(cut, "b").Change(true);
        cut.Find("[data-testid='parallel-attempts-confirm-yes']").Click();

        _selected.Should().Be("b", "the command channel still reports the confirmed pick");
        Column(cut, "b").GetAttribute("data-selected").Should().Be("true",
            "the confirmation is shown until the host answers");

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmParallelAttempts.Attempts)] = Attempts(),
                [nameof(TmParallelAttempts.SelectedAttemptId)] = "a",
            })));

        Column(cut, "a").GetAttribute("data-selected").Should().Be("true",
            "a pinned value the host never binds stays authoritative — the host keeps control");
        Column(cut, "b").GetAttribute("data-selected").Should().BeNull();
        Radio(cut, "a").HasAttribute("checked").Should().BeTrue(
            "the re-mounted radio follows the host's pinned value, not the reader's last click");
        _bound.Should().BeNull("nothing is bound, so nothing was written back");
    }

    [Fact]
    public void AnUnpinnedWinner_KeepsTheReadersConfirmation_AcrossAHostRender()
    {
        var cut = RenderAttempts(bindSelection: false);

        Radio(cut, "b").Change(true);
        cut.Find("[data-testid='parallel-attempts-confirm-yes']").Click();

        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?>
            {
                [nameof(TmParallelAttempts.Attempts)] = Attempts(),
            })));

        Column(cut, "b").GetAttribute("data-selected").Should().Be("true",
            "a host that names no winner lets the reader's confirmation stand");
        Radio(cut, "b").HasAttribute("checked").Should().BeTrue();
    }

    // ── Votes ────────────────────────────────────────────────────────────────

    [Fact]
    public void TheVoteDisclosure_StartsCollapsed_AndOpensTheVoters()
    {
        var cut = RenderAttempts();

        var toggle = Column(cut, "b").QuerySelector("[data-testid='parallel-attempt-votes-toggle']")!;
        toggle.TextContent.Should().Contain("Votes (2)");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        Column(cut, "b").QuerySelectorAll("[data-testid='parallel-attempt-vote']").Should().BeEmpty();

        toggle.Click();

        var opened = Column(cut, "b").QuerySelector("[data-testid='parallel-attempt-votes-toggle']")!;
        opened.GetAttribute("aria-expanded").Should().Be("true");
        opened.GetAttribute("aria-controls").Should().Be(
            Column(cut, "b").QuerySelector("[data-testid='parallel-attempt-votes']")!.Id,
            "the toggle names the panel it opens");

        var votes = Column(cut, "b").QuerySelectorAll("[data-testid='parallel-attempt-vote']");
        votes.Should().HaveCount(2);
        votes[0].TextContent.Should().Contain("Reviewer").And.Contain("Approved").And.Contain("0.9");
        Column(cut, "b").QuerySelector("[data-testid='parallel-attempt-vote-rationale']")!.TextContent.Trim()
            .Should().Be("Covers the edge case");
    }

    [Fact]
    public void AVoteWithoutARationale_RendersNoEmptyLine()
    {
        var cut = RenderAttempts([Attempt("a", "Draft A", 0.5, votes: [Vote("Grader", AttemptVoteOutcome.Abstain)])]);

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-votes-toggle']")!.Click();

        Column(cut, "a").QuerySelectorAll("[data-testid='parallel-attempt-vote-rationale']").Should().BeEmpty();
        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-vote']")!.TextContent.Should()
            .Contain("Abstained");
    }

    [Fact]
    public void AnAttemptWithoutVotes_SaysSoInsteadOfHidingTheSection()
    {
        var cut = RenderAttempts([Attempt("a", "Draft A", 0.5)]);

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-votes-toggle']")!.TextContent.Should()
            .Contain("Votes (0)");

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-votes-toggle']")!.Click();

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-votes']")!.TextContent.Should()
            .Contain("No votes recorded");
    }

    // ── The comparison table ─────────────────────────────────────────────────

    [Fact]
    public void TheComparisonTable_CarriesTheSameNumbersAsText()
    {
        var cut = RenderAttempts();

        var table = cut.Find("[data-testid='parallel-attempts-table']");
        table.TagName.Should().Be("TABLE");
        table.QuerySelector("caption")!.TextContent.Trim().Should().Be("Attempts compared");
        table.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()).Should()
            .Equal(new[] { "Attempt", "State", "Score", "Votes", "Best" });

        var rows = table.QuerySelectorAll("tbody tr");
        rows.Should().HaveCount(3);
        rows[1].QuerySelectorAll("th, td").Select(cell => cell.TextContent.Trim()).Should()
            .Equal(new[] { "Draft B", "Succeeded", "0.93", "2", "Yes" });
        rows[0].QuerySelectorAll("th, td").Select(cell => cell.TextContent.Trim()).Should()
            .Equal(new[] { "Draft A", "Succeeded", "0.61", "1", "No" });
    }

    [Fact]
    public void TheComparisonTable_CanBeTurnedOff()
    {
        RenderAttempts(showComparison: false).FindAll("[data-testid='parallel-attempts-table']").Should().BeEmpty();
    }

    [Fact]
    public void TheScores_AreFormattedWithTheHostsCulture()
    {
        var cut = Render<TmParallelAttempts>(parameters => parameters
            .Add(component => component.Attempts, [Attempt("a", "Draft A", 0.615)])
            .Add(component => component.Culture, CultureInfo.GetCultureInfo("cs-CZ")));

        Column(cut, "a").QuerySelector("[data-testid='parallel-attempt-score']")!.TextContent.Trim()
            .Should().Be("0,615");
    }

    // ── The surface contract ─────────────────────────────────────────────────

    [Fact]
    public void TheSurfaceCarries_OneLiveRegion_AndTheHostsClass()
    {
        var cut = Render<TmParallelAttempts>(parameters => parameters
            .Add(component => component.Attempts, Attempts())
            .Add(component => component.Class, "my-attempts")
            .Add(component => component.DataTestId, "attempts-here"));

        var root = cut.Find("[data-testid='attempts-here']");
        root.ClassList.Should().Contain("tm-ai-parallel-attempts");
        root.ClassList.Should().Contain("my-attempts");

        cut.FindAll("[aria-live]").Should().ContainSingle(
            "one polite region per surface: a second island would announce every change twice");
    }
}
