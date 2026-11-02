using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Models.Approvals;
using Tempo.Blazor.Components.Approvals;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Approvals;

/// <summary>
/// Phase 2 of plan 10b3966f — <c>TmApprovalCard</c>, the human-in-the-loop approval request. The
/// card is a <c>role="group"</c> region that shows a localized risk badge (never colour alone), the
/// request details as a definition list, a countdown driven by the injected
/// <see cref="TimeProvider"/>, and Approve / Reject actions with an optional rejection note. A
/// decision collapses the card into a row that names who decided and when, and — with
/// <c>UndoWindow</c> — offers Undo until the window elapses. Exactly one live region carries every
/// announcement, focus moves with the collapse (and comes back on undo), and the Ctrl/Cmd shortcuts
/// only ever fire for a card that has focus and never from inside a text field.
/// </summary>
public class TmApprovalCardTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly string FocusInvocation = "Blazor._internal.domWrapper.focus";

    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    private static Task DrainAsync<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => cut.InvokeAsync(() => { });

    private static IReadOnlyList<KeyValuePair<string, string>> Details()
        =>
        [
            new("Requester", "Jane Doe"),
            new("Amount", "12 500 CZK"),
        ];

    /// <summary>Whether the element sits in the collapsed half of the card. The inactive half is
    /// marked <c>inert</c> (out of the tab order and out of the accessibility tree) and the CSS
    /// additionally hides it with <c>visibility: hidden</c>.</summary>
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

    private static IElement FindVisible(IRenderedComponent<TmApprovalCard> cut, string testId)
        => cut.FindAll($"[data-testid='{testId}']").Single(element => !IsHidden(element));

    private static bool HasVisible(IRenderedComponent<TmApprovalCard> cut, string testId)
        => cut.FindAll($"[data-testid='{testId}']").Any(element => !IsHidden(element));

    private IRenderedComponent<TmApprovalCard> RenderCard(
        Action<ComponentParameterCollectionBuilder<TmApprovalCard>>? configure = null)
        => Render<TmApprovalCard>(parameters =>
        {
            parameters
                .Add(card => card.Title, "Release the invoice")
                .Add(card => card.Description, "The requester wants the invoice sent today.");
            configure?.Invoke(parameters);
        });

    // ── Content ──────────────────────────────────────────────────────────────

    [Fact]
    public void RendersTitleDescriptionAndALocalizedRiskBadge()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Risk, ApprovalRisk.High)
            .Add(card => card.Details, Details()));

        cut.Find(".tm-approval-card").GetAttribute("role").Should().Be("group");
        cut.Find("[data-testid='approval-title']").TextContent.Trim().Should().Be("Release the invoice");
        cut.Find("[data-testid='approval-description']").TextContent.Trim()
            .Should().Be("The requester wants the invoice sent today.");

        var risk = cut.Find("[data-testid='approval-risk']");
        risk.TextContent.Trim().Should().Be("High risk",
            "the risk is a localized label, not colour alone (WCAG 1.4.1)");
        risk.QuerySelector(".tm-badge").Should().NotBeNull("the risk is presented as a badge");
        cut.Find(".tm-approval-card").ClassList.Should().Contain("tm-approval-card--risk-high");
    }

    [Theory]
    [InlineData(ApprovalRisk.Low, "Low risk", "tm-approval-card--risk-low")]
    [InlineData(ApprovalRisk.Medium, "Medium risk", "tm-approval-card--risk-medium")]
    [InlineData(ApprovalRisk.High, "High risk", "tm-approval-card--risk-high")]
    [InlineData(ApprovalRisk.Critical, "Critical risk", "tm-approval-card--risk-critical")]
    public void EveryRiskLevelHasItsOwnLocalizedLabelAndModifier(
        ApprovalRisk risk, string expectedLabel, string expectedClass)
    {
        var cut = RenderCard(parameters => parameters.Add(card => card.Risk, risk));

        cut.Find("[data-testid='approval-risk']").TextContent.Trim().Should().Be(expectedLabel);
        cut.Find(".tm-approval-card").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void RendersDetailsAsADefinitionListInOrder()
    {
        var cut = RenderCard(parameters => parameters.Add(card => card.Details, Details()));

        var list = cut.Find("[data-testid='approval-details']");
        list.TagName.Should().Be("DL");
        list.GetAttribute("aria-label").Should().Be("Request details");
        list.QuerySelectorAll("dt").Select(term => term.TextContent.Trim())
            .Should().Equal("Requester", "Amount");
        list.QuerySelectorAll("dd").Select(value => value.TextContent.Trim())
            .Should().Equal("Jane Doe", "12 500 CZK");
    }

    [Fact]
    public void RendersThePreviewSlotAndOmitsEmptyDetails()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.PreviewContent, builder => builder.AddMarkupContent(0, "<p>Draft body</p>")));

        cut.Find(".tm-approval-card__preview").TextContent.Trim().Should().Be("Draft body");
        cut.FindAll("[data-testid='approval-details']").Should().BeEmpty(
            "there are no details to list");
    }

    // ── Countdown ────────────────────────────────────────────────────────────

    [Fact]
    public void CountdownUsesMinutesAndSecondsUnderAnHour()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddMinutes(4).AddSeconds(35)));

        var countdown = cut.Find("[data-testid='approval-countdown']");
        countdown.TextContent.Should().Contain("Expires in");
        cut.Find(".tm-approval-card__countdown").TextContent.Trim().Should().Be("04:35");
        cut.Find(".tm-approval-card__countdown").GetAttribute("aria-live").Should().Be("off",
            "the ticking value must never be announced on every second");
        cut.Find(".tm-approval-card__countdown").GetAttribute("datetime")
            .Should().Be(Start.AddMinutes(4).AddSeconds(35).ToString("o",
                System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void CountdownUsesHoursWhenMoreThanAnHourRemains()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddHours(1).AddMinutes(4).AddSeconds(35)));

        cut.Find(".tm-approval-card__countdown").TextContent.Trim().Should().Be("1:04:35");
    }

    [Fact]
    public async Task CountdownTicksAndAnnouncesTheLastMinuteOnce()
    {
        var time = UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddMinutes(4)));

        cut.Find(".tm-approval-card__countdown").TextContent.Trim().Should().Be("04:00");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim().Should().BeEmpty(
            "nothing has happened yet");

        time.Advance(TimeSpan.FromSeconds(1));
        await DrainAsync(cut);
        cut.Find(".tm-approval-card__countdown").TextContent.Trim().Should().Be("03:59");

        time.Advance(TimeSpan.FromSeconds(180));
        await DrainAsync(cut);
        cut.Find(".tm-approval-card__countdown").TextContent.Trim().Should().Be("00:59");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("Less than a minute remains to decide.");

        time.Advance(TimeSpan.FromSeconds(10));
        await DrainAsync(cut);
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("Less than a minute remains to decide.",
                "the warning is announced once, not on every tick");
    }

    [Fact]
    public async Task ExpiredStateDisablesTheActionsAndAnnouncesIt()
    {
        var time = UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddSeconds(5)));

        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeFalse();

        time.Advance(TimeSpan.FromSeconds(6));
        await DrainAsync(cut);

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("expired");
        cut.Find(".tm-approval-card").ClassList.Should().Contain("tm-approval-card--expired");
        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='approval-reject']").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-approval-card__countdown").TextContent.Trim().Should().Be("Expired");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("This request has expired.");
    }

    [Fact]
    public void AnAlreadyElapsedExpiryStartsExpired()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddMinutes(-1)));

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("expired");
        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void AnExpiredStateParameterDisablesTheActionsToo()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.State, ApprovalState.Expired));

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("expired");
        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='approval-reject']").HasAttribute("disabled").Should().BeTrue();
    }

    // ── Decisions ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveRaisesOnDecisionWithAnApprovalAndCollapsesToASummaryRow()
    {
        UseFakeClock();
        ApprovalDecision? received = null;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, decision =>
            {
                received = decision;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        received.Should().NotBeNull();
        received!.State.Should().Be(ApprovalState.Approved);
        received.IsApproved.Should().BeTrue();
        received.Note.Should().BeNull();

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("approved");
        cut.Find(".tm-approval-card").ClassList.Should().Contain("tm-approval-card--collapsed");
        IsHidden(cut.Find("[data-testid='approval-approve']")).Should().BeTrue(
            "the collapsed body is no longer exposed");
        cut.Find("[data-testid='approval-summary-text']").TextContent.Trim()
            .Should().StartWith("Approved — Unknown, ");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim().Should().Be("Approved.");
    }

    [Fact]
    public async Task TheSummaryNamesWhoDecidedAndWhen()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ => Task.CompletedTask));

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());
        cut.Render(parameters => parameters
            .Add(card => card.State, ApprovalState.Approved)
            .Add(card => card.DecidedBy, "Jane Doe")
            .Add(card => card.DecidedAt, Start.AddMinutes(2)));

        var summary = cut.Find("[data-testid='approval-summary-text']").TextContent.Trim();
        summary.Should().StartWith("Approved — Jane Doe, ");
        summary.Should().Contain("2026", "the decision timestamp is shown");
    }

    [Fact]
    public async Task RejectsWithTheTypedNoteAndRaisesStateChanged()
    {
        UseFakeClock();
        ApprovalDecision? received = null;
        ApprovalState? reported = null;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, decision =>
            {
                received = decision;
                return Task.CompletedTask;
            })
            .Add(card => card.StateChanged, state => reported = state));

        cut.Find("[data-testid='approval-note']").Input("Duplicate of INV-2");
        await cut.Find("[data-testid='approval-reject']").ClickAsync(new MouseEventArgs());

        received!.State.Should().Be(ApprovalState.Rejected);
        received.Note.Should().Be("Duplicate of INV-2");
        reported.Should().Be(ApprovalState.Rejected);
        cut.Find("[data-testid='approval-summary-text']").TextContent.Trim()
            .Should().StartWith("Rejected — ");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim().Should().Be("Rejected.");
    }

    [Fact]
    public async Task TheActionsAreDisabledAndTheCardBusyWhileTheDecisionIsInFlight()
    {
        UseFakeClock();
        var completion = new TaskCompletionSource();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ => completion.Task));

        var click = cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='approval-reject']").HasAttribute("disabled").Should().BeTrue();
        cut.Find(".tm-approval-card").GetAttribute("aria-busy").Should().Be("true");

        completion.SetResult();
        await click;

        cut.Find(".tm-approval-card").GetAttribute("aria-busy").Should().BeNull();
        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("approved");
    }

    [Fact]
    public async Task ARenderWhileTheDecisionIsInFlightNeverExpiresTheCard()
    {
        var time = UseFakeClock();
        var completion = new TaskCompletionSource();
        var reported = new List<ApprovalState>();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddSeconds(5))
            .Add(card => card.OnDecision, _ => completion.Task)
            .Add(card => card.StateChanged, state => reported.Add(state)));

        var click = cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        // The parent re-renders while the save is still running and the deadline passes meanwhile.
        time.Advance(TimeSpan.FromSeconds(6));
        await DrainAsync(cut);
        cut.Render(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddSeconds(5))
            .Add(card => card.OnDecision, _ => completion.Task)
            .Add(card => card.StateChanged, state => reported.Add(state)));

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("pending",
            "a decision that is being persisted owns the state until it completes or throws");
        reported.Should().NotContain(ApprovalState.Expired);

        completion.SetResult();
        await click;

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("approved");
        reported.Should().Equal(
            [ApprovalState.Approved],
            "the host hears ONE transition per request, never Expired followed by Approved");
    }

    [Fact]
    public async Task AFailedDecisionOnARequestThatRanOutOfTimeStillExpiresIt()
    {
        var time = UseFakeClock();
        var completion = new TaskCompletionSource();
        var reported = new List<ApprovalState>();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddSeconds(5))
            .Add(card => card.OnDecision, _ => completion.Task)
            .Add(card => card.StateChanged, state => reported.Add(state)));

        var click = cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        time.Advance(TimeSpan.FromSeconds(6));
        completion.SetException(new InvalidOperationException("boom"));
        await click;

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("expired",
            "the deadline is honoured even when the save that was in flight fails: a request that "
            + "ran out of time must not stay decidable");
        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("This request has expired.",
                "the reader hears the state that now holds, not 'try again' over a passed deadline");
        reported.Should().Equal([ApprovalState.Expired]);
    }

    [Fact]
    public async Task AFailedDecisionInTheLastMinuteStillAnnouncesTheSaveError()
    {
        var time = UseFakeClock();
        var completion = new TaskCompletionSource();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddSeconds(30))
            .Add(card => card.OnDecision, _ => completion.Task));

        var click = cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        // The deadline is still ahead, but the one-minute warning became due while the save ran.
        time.Advance(TimeSpan.FromSeconds(29));
        completion.SetException(new InvalidOperationException("boom"));
        await click;

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("pending");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.",
                "the warning that became due during the save must not replace the outcome of the click");
    }

    [Fact]
    public async Task AFailedDecisionReturnsToPendingAndShowsTheLocalizedAlert()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ => Task.FromException(new InvalidOperationException("boom"))));

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("pending");
        cut.Find("[data-testid='approval-approve']").HasAttribute("disabled").Should().BeFalse();
        var alert = cut.Find("[data-testid='approval-error']");
        alert.TextContent.Should().Contain("The decision could not be saved. Try again.");
        alert.QuerySelector(".tm-alert").Should().NotBeNull("the error reuses the TmAlert surface");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("The decision could not be saved. Try again.");
    }

    // ── Note validation ──────────────────────────────────────────────────────

    [Fact]
    public async Task RejectWithoutANoteIsRefusedWhenTheNoteIsRequired()
    {
        UseFakeClock();
        var called = 0;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.RequireNoteOnReject, true)
            .Add(card => card.OnDecision, _ =>
            {
                called++;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='approval-reject']").ClickAsync(new MouseEventArgs());

        called.Should().Be(0, "the decision never leaves the card without the note");
        var note = cut.Find("[data-testid='approval-note']");
        note.GetAttribute("aria-invalid").Should().Be("true");
        note.GetAttribute("aria-describedby").Should().Be(cut.Find("[data-testid='approval-note-error']").Id);
        cut.Find("[data-testid='approval-note-error']").TextContent.Trim()
            .Should().Be("A note is required to reject this request.");
        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("pending");
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("A note is required to reject this request.");

        note.Input("Duplicate of INV-2");
        cut.Find("[data-testid='approval-note']").GetAttribute("aria-invalid").Should().BeNull(
            "typing clears the invalid state");

        await cut.Find("[data-testid='approval-reject']").ClickAsync(new MouseEventArgs());
        called.Should().Be(1);
    }

    [Fact]
    public async Task ANoteIsOptionalByDefault()
    {
        UseFakeClock();
        var called = 0;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ =>
            {
                called++;
                return Task.CompletedTask;
            }));

        await cut.Find("[data-testid='approval-reject']").ClickAsync(new MouseEventArgs());

        called.Should().Be(1);
        cut.Find("[data-testid='approval-note']").GetAttribute("aria-invalid").Should().BeNull();
    }

    [Fact]
    public void TheNoteFieldCanBeTurnedOff()
    {
        var cut = RenderCard(parameters => parameters.Add(card => card.AllowNote, false));

        cut.FindAll("[data-testid='approval-note']").Should().BeEmpty();
    }

    // ── Undo ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndoIsOfferedInsideTheWindowAndRestoresThePendingCard()
    {
        UseFakeClock();
        var undone = 0;
        var reported = new List<ApprovalState>();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnDecision, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () =>
            {
                undone++;
                return Task.CompletedTask;
            })
            .Add(card => card.StateChanged, state => reported.Add(state)));

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        FindVisible(cut, "approval-undo").Should().NotBeNull("the undo window is open");
        await cut.Find("[data-testid='approval-undo']").ClickAsync(new MouseEventArgs());

        undone.Should().Be(1);
        reported.Should().Contain(ApprovalState.Pending);
        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("pending");
        IsHidden(cut.Find("[data-testid='approval-approve']")).Should().BeFalse();
        cut.Find("[data-testid='approval-announcements']").TextContent.Trim()
            .Should().Be("Decision undone. The request is pending again.");
    }

    [Fact]
    public async Task UndoDisappearsWhenTheWindowElapses()
    {
        var time = UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnDecision, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => Task.CompletedTask));

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());
        HasVisible(cut, "approval-undo").Should().BeTrue();

        time.Advance(TimeSpan.FromSeconds(31));
        await DrainAsync(cut);

        HasVisible(cut, "approval-undo").Should().BeFalse("the undo window has elapsed");
        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("approved",
            "the decision itself stands");
    }

    [Fact]
    public async Task WithoutAnUndoWindowNoUndoIsOffered()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => Task.CompletedTask));

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        HasVisible(cut, "approval-undo").Should().BeFalse();
    }

    // ── Keyboard ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CtrlEnterApprovesAndCtrlBackspaceRejects()
    {
        UseFakeClock();
        var decisions = new List<ApprovalDecision>();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnUndo, () => Task.CompletedTask)
            .Add(card => card.OnDecision, decision =>
            {
                decisions.Add(decision);
                return Task.CompletedTask;
            }));

        cut.Find(".tm-approval-card").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        decisions.Should().HaveCount(1);
        decisions[0].State.Should().Be(ApprovalState.Approved);
        cut.Find("[data-testid='approval-approve']").GetAttribute("aria-keyshortcuts")
            .Should().Be("Control+Enter Meta+Enter");

        await cut.Find("[data-testid='approval-undo']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-approval-card").KeyDown(new KeyboardEventArgs { Key = "Backspace", MetaKey = true });

        decisions.Should().HaveCount(2);
        decisions[1].State.Should().Be(ApprovalState.Rejected);
        cut.Find("[data-testid='approval-reject']").GetAttribute("aria-keyshortcuts")
            .Should().Be("Control+Backspace Meta+Backspace");
    }

    [Fact]
    public async Task ShortcutsDoNothingWhileTheCardIsBusyOrDecided()
    {
        UseFakeClock();
        var calls = 0;
        var completion = new TaskCompletionSource();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ =>
            {
                calls++;
                return completion.Task;
            }));

        var click = cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-approval-card").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });
        calls.Should().Be(1, "a decision is already in flight");

        completion.SetResult();
        await click;

        cut.Find(".tm-approval-card").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        calls.Should().Be(1, "a decided card has nothing left to decide");
        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("approved");
    }

    [Fact]
    public void TheShortcutsCanBeTurnedOff()
    {
        UseFakeClock();
        var called = 0;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.EnableShortcuts, false)
            .Add(card => card.OnDecision, _ =>
            {
                called++;
                return Task.CompletedTask;
            }));

        cut.Find(".tm-approval-card").KeyDown(new KeyboardEventArgs { Key = "Enter", CtrlKey = true });

        called.Should().Be(0);
        cut.Find("[data-testid='approval-approve']").HasAttribute("aria-keyshortcuts").Should().BeFalse();
    }

    [Fact]
    public void TheShortcutNeverFiresFromInsideTheNoteField()
    {
        UseFakeClock();
        var called = 0;
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ =>
            {
                called++;
                return Task.CompletedTask;
            }));

        cut.Find("[data-testid='approval-note']").KeyDown(new KeyboardEventArgs { Key = "Backspace", CtrlKey = true });

        called.Should().Be(0, "a text field owns its own keys");
        cut.Find(".tm-approval-card").GetAttribute("data-state").Should().Be("pending");
    }

    // ── Focus management ─────────────────────────────────────────────────────

    [Fact]
    public async Task DecidingMovesFocusToTheUndoActionAndUndoReturnsItToApprove()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30))
            .Add(card => card.OnDecision, _ => Task.CompletedTask)
            .Add(card => card.OnUndo, () => Task.CompletedTask));

        var approveId = cut.Find("[data-testid='approval-approve']").GetAttribute("blazor:elementReference");
        approveId.Should().NotBeNullOrEmpty();

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        var undoId = cut.Find("[data-testid='approval-undo']").GetAttribute("blazor:elementReference");
        undoId.Should().NotBeNullOrEmpty();
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(undoId,
            "the focused approve button collapsed away, so focus lands on the surviving action");

        var beforeUndo = JSInterop.Invocations.Count;
        await cut.Find("[data-testid='approval-undo']").ClickAsync(new MouseEventArgs());

        focus = JSInterop.Invocations.Skip(beforeUndo).Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(approveId,
            "undo re-expands the card, so focus comes back to the primary action");
    }

    [Fact]
    public async Task DecidingWithoutAnUndoWindowFocusesTheCardItself()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.OnDecision, _ => Task.CompletedTask));

        var rootId = cut.Find(".tm-approval-card").GetAttribute("blazor:elementReference");
        rootId.Should().NotBeNullOrEmpty("the card root is the focus fallback (tabindex=-1)");

        await cut.Find("[data-testid='approval-approve']").ClickAsync(new MouseEventArgs());

        cut.Find(".tm-approval-card").GetAttribute("tabindex").Should().Be("-1");
        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(rootId);
    }

    [Fact]
    public async Task ARefusedRejectMovesFocusToTheNoteField()
    {
        UseFakeClock();
        var cut = RenderCard(parameters => parameters
            .Add(card => card.RequireNoteOnReject, true)
            .Add(card => card.OnDecision, _ => Task.CompletedTask));

        var noteId = cut.Find("[data-testid='approval-note']").GetAttribute("blazor:elementReference");
        noteId.Should().NotBeNullOrEmpty();

        await cut.Find("[data-testid='approval-reject']").ClickAsync(new MouseEventArgs());

        var focus = JSInterop.Invocations.Last(invocation => invocation.Identifier == FocusInvocation);
        ((ElementReference)focus.Arguments[0]).Id.Should().Be(noteId);
    }

    // ── Collapse / compact ───────────────────────────────────────────────────

    [Fact]
    public void CompactRendersASingleRowWithoutDetailsOrPreview()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.Compact, true)
            .Add(card => card.ExpiresAt, Start.AddMinutes(5))
            .Add(card => card.Details, Details())
            .Add(card => card.PreviewContent, builder => builder.AddMarkupContent(0, "<p>Draft body</p>")));

        cut.Find(".tm-approval-card").ClassList.Should().Contain("tm-approval-card--compact");
        cut.FindAll("[data-testid='approval-details']").Should().BeEmpty();
        cut.FindAll(".tm-approval-card__preview").Should().BeEmpty();
        cut.FindAll("[data-testid='approval-description']").Should().BeEmpty();
        cut.FindAll("[data-testid='approval-approve']").Should().ContainSingle();
        cut.Find("[data-testid='approval-countdown']").Should().NotBeNull();
    }

    [Fact]
    public void TheCollapsedRowIsRenderedForTheTransitionButInert()
    {
        var cut = RenderCard();

        cut.Find(".tm-approval-card__summary").HasAttribute("inert").Should().BeTrue(
            "the decided half is mounted so the collapse can transition the height, but it is inert");
        cut.Find(".tm-approval-card__body").HasAttribute("inert").Should().BeFalse();
        cut.FindAll(".tm-approval-card__collapse").Should().HaveCount(2,
            "both halves are mounted so the collapse can transition the height");
    }

    [Fact]
    public void ACancelledCardCollapsesWithoutAnUndoOffer()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.State, ApprovalState.Cancelled)
            .Add(card => card.UndoWindow, TimeSpan.FromSeconds(30)));

        cut.Find("[data-testid='approval-summary-text']").TextContent.Trim().Should().Be("Cancelled");
        HasVisible(cut, "approval-undo").Should().BeFalse();
    }

    // ── Accessibility ────────────────────────────────────────────────────────

    [Fact]
    public void TheCardOwnsExactlyOneLiveRegion()
    {
        var cut = RenderCard(parameters => parameters
            .Add(card => card.ExpiresAt, Start.AddMinutes(5))
            .Add(card => card.RequireNoteOnReject, true)
            .Add(card => card.OnDecision, _ => Task.CompletedTask));

        var live = cut.FindAll("[aria-live]")
            .Where(element => element.GetAttribute("aria-live") != "off")
            .ToList();

        live.Should().ContainSingle("duplicate announcements are worse than none");
        live[0].GetAttribute("data-testid").Should().Be("approval-announcements");
        live[0].GetAttribute("aria-atomic").Should().Be("true");
        cut.FindAll("[role='status']").Should().ContainSingle()
            .Which.GetAttribute("data-testid").Should().Be("approval-announcements");
    }

    [Fact]
    public void TheGroupIsNamedAfterItsTitle()
    {
        var cut = RenderCard();

        cut.Find(".tm-approval-card").GetAttribute("aria-label").Should().Be("Release the invoice");
        cut.FindAll(".tm-approval-card[aria-labelledby]").Should().BeEmpty(
            "the name is an aria-label: a labelledby target inside the collapsed half would be a "
            + "dangling reference once the card decides");
    }

    [Fact]
    public void ATitlelessCardFallsBackToTheLocalizedGroupLabel()
    {
        var cut = Render<TmApprovalCard>();

        cut.Find(".tm-approval-card").GetAttribute("aria-label").Should().Be("Approval request");
        cut.FindAll("[data-testid='approval-title']").Should().BeEmpty();
    }

    [Fact]
    public void TheActionButtonsCarryLocalizedNames()
    {
        var cut = RenderCard();

        cut.Find("[data-testid='approval-approve']").TextContent.Trim().Should().Be("Approve");
        cut.Find("[data-testid='approval-reject']").TextContent.Trim().Should().Be("Reject");
        cut.Find("[data-testid='approval-note']").GetAttribute("aria-label").Should().BeNull();
        cut.Find("label[for]" ).TextContent.Trim().Should().Be("Note");
    }

    [Fact]
    public void TestIdPrefixNamespacesEveryInternalTestId()
    {
        var cut = Render<TmApprovalCard>(parameters => parameters
            .Add(card => card.TestIdPrefix, "first")
            .Add(card => card.Title, "Release the invoice"));

        cut.Find("[data-testid='first-approval-card']").Should().NotBeNull();
        cut.Find("[data-testid='first-approval-approve']").Should().NotBeNull();
        cut.Find("[data-testid='first-approval-announcements']").Should().NotBeNull();
    }
}
