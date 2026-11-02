using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Submission status timeline (Signing redesign phase 7, S1-1): the continuous axis layout, the
/// soft/solid marker split, the new <see cref="TmSubmissionStatusTimeline.SortDirection"/> contract
/// (default Descending — Q-S9), the <c>HeaderContent</c> slot (Q-S10 — the document summary card is
/// host content) and the "Recipient:" prefix.
/// </summary>
public class TmSubmissionStatusTimelineTests : LocalizationTestBase
{
    [Fact]
    public void Timeline_SortDirection_DefaultDescending_AscendingOptional()
    {
        var first = CreateEvent(SigningSubmissionStatusEventType.Sent, "Alex", createdAt: new DateTimeOffset(2026, 5, 1, 9, 0, 0, TimeSpan.Zero));
        var second = CreateEvent(SigningSubmissionStatusEventType.Opened, "Alex", createdAt: new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero));

        var cut = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events, [first, second]));

        // Default (no SortDirection parameter) keeps the newest-first order.
        OrderedTitles(cut).Should().Equal("Opened", "Sent");

        cut.FindAll("time[datetime]").Should().HaveCount(2);
        var times = cut.FindAll("time[datetime]").Select(time => time.GetAttribute("datetime")).ToList();
        times.Should().BeInDescendingOrder("the default Descending order lists the newest event first");

        var ascending = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events, [first, second])
            .Add(p => p.SortDirection, SigningTimelineSortDirection.Ascending));
        var ascendingTimes = ascending.FindAll("time[datetime]").Select(time => time.GetAttribute("datetime")).ToList();
        ascendingTimes.Should().BeInAscendingOrder("SortDirection.Ascending lists the oldest event first");
    }

    [Fact]
    public void Timeline_NeutralEvents_HaveSoftMarkerClass()
    {
        var cut = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events,
            [
                CreateEvent(SigningSubmissionStatusEventType.Opened, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.Sent, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.Completed, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.Declined, "Nora")
            ]));

        // Neutral/informational events paint the SOFT marker (tinted disc, coloured glyph);
        // success/danger events keep the SOLID marker (filled disc, on-colour glyph).
        var markers = cut.FindAll(".tm-submission-status-timeline__marker");
        markers.Should().HaveCount(4);
        markers.Count(marker => marker.ClassList.Contains("tm-submission-status-timeline__marker--soft"))
            .Should().Be(1, "only the neutral/informational event (Opened) carries the soft marker");
        markers.Count(marker => marker.ClassList.Contains("tm-submission-status-timeline__marker--soft") is false)
            .Should().Be(3, "sent/completed/declined keep the solid marker");

        cut.Find(".tm-submission-status-timeline__item--danger").Should().NotBeNull();
    }

    [Fact]
    public void Timeline_HeaderContentSlot_Renders()
    {
        var cut = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events, [CreateEvent(SigningSubmissionStatusEventType.Sent, "Alex")])
            .Add(p => p.HeaderContent, "<div class=\"timeline-host-header\">Contract · sent to 2 signers</div>"));

        cut.Find(".timeline-host-header").TextContent.Should().Contain("Contract · sent to 2 signers");

        // The host header renders BEFORE the event items (the document summary card sits above
        // the ceremony timeline in the desktop reference).
        var markup = cut.Markup;
        markup.IndexOf("timeline-host-header", StringComparison.Ordinal)
            .Should().BeLessThan(markup.IndexOf("tm-submission-status-timeline__items", StringComparison.Ordinal));
    }

    [Fact]
    public void Timeline_RecipientPrefix()
    {
        var cut = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events, [CreateEvent(SigningSubmissionStatusEventType.Opened, "Alex")]));

        var recipient = cut.Find(".tm-submission-status-timeline__recipient");
        recipient.TextContent.Should().StartWith("Recipient:")
            .And.Contain("Alex");
    }

    [Fact]
    public void Render_MapsCoreLifecycleEvents()
    {
        var cut = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events,
            [
                CreateEvent(SigningSubmissionStatusEventType.Sent, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.Opened, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.Completed, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.Declined, "Nora")
            ]));

        cut.Markup.Should().Contain("Sent");
        cut.Markup.Should().Contain("Opened");
        cut.Markup.Should().Contain("Completed");
        cut.Markup.Should().Contain("Declined");
        cut.FindAll(".tm-submission-status-timeline__item--danger").Should().HaveCount(1);
    }

    [Fact]
    public void Render_EmailAndVerificationEventsWithMetadata()
    {
        var cut = Render<TmSubmissionStatusTimeline>(parameters => parameters
            .Add(p => p.Events,
            [
                CreateEvent(SigningSubmissionStatusEventType.EmailBounced, "Alex", new Dictionary<string, string> { ["SMTP"] = "550" }),
                CreateEvent(SigningSubmissionStatusEventType.EmailComplaint, "Nora"),
                CreateEvent(SigningSubmissionStatusEventType.VerificationCompleted, "Alex"),
                CreateEvent(SigningSubmissionStatusEventType.KbaCompleted, "Alex")
            ]));

        cut.Markup.Should().Contain("Email bounced");
        cut.Markup.Should().Contain("Email complaint");
        cut.Markup.Should().Contain("Verification completed");
        cut.Markup.Should().Contain("KBA completed");
        cut.Markup.Should().Contain("550");
    }

    private static IReadOnlyList<string> OrderedTitles(Bunit.IRenderedComponent<TmSubmissionStatusTimeline> cut)
    {
        return cut.FindAll(".tm-submission-status-timeline__title")
            .Select(title => title.TextContent.Trim())
            .ToList();
    }

    private static SigningSubmissionStatusEvent CreateEvent(
        SigningSubmissionStatusEventType type,
        string recipient,
        IReadOnlyDictionary<string, string>? metadata = null,
        DateTimeOffset? createdAt = null)
    {
        return new SigningSubmissionStatusEvent
        {
            Type = type,
            RecipientName = recipient,
            RecipientEmail = $"{recipient.ToLowerInvariant()}@example.test",
            CreatedAt = createdAt ?? DateTimeOffset.Now,
            Metadata = metadata ?? new Dictionary<string, string>()
        };
    }
}
