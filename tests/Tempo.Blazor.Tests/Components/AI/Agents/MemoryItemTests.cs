using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 6 — the <c>MemoryItem</c> model and the memory review / bulk contracts behind
/// <c>TmMemoryPanel</c>.
/// <para>
/// The facts pin what the surface is allowed to claim: an unclassified memory is ACTIVE (the normal state of a stored
/// fact, never "forgotten"), a missing confidence or source is not printed at all instead of becoming zero or blank, a
/// confidence outside <c>0…1</c> is not a ratio the surface can format, a blank identity is REJECTED (an id invented per
/// construction would change on every rebuild and silently drop the row's selection, edit and undo state) and a blank
/// category is filled so one unnamed row cannot merge with another.
/// </para>
/// </summary>
public class MemoryItemTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    // ── Identity and text ────────────────────────────────────────────────────

    [Fact]
    public void ABlankId_IsRejected_SoTheIdentityNeverChangesBetweenRebuilds()
    {
        var blank = () => new MemoryItem("  ", "Preferences", "Prefers metric units.");
        var missing = () => new MemoryItem(null!, "Preferences", "Prefers metric units.");

        blank.Should().Throw<ArgumentException>(
            "an id invented per construction changes on every rebuild and silently drops the row's state");
        missing.Should().Throw<ArgumentException>();
        new MemoryItem("m-1", "Preferences", "Prefers metric units.").Id.Should().Be("m-1");
    }

    [Fact]
    public void ABlankCategory_IsReportedAsNoCategory_SoTheSurfaceNamesTheGroupItself()
    {
        var first = new MemoryItem("m-1", " ", "Prefers metric units.");
        var second = new MemoryItem("m-2", "", "Prefers metric units.");

        first.HasCategory.Should().BeFalse();
        second.HasCategory.Should().BeFalse();
        first.Category.Should().Be(" ", "the host's value is kept verbatim, never rewritten");
        second.Category.Should().BeEmpty();
    }

    [Fact]
    public void ACategory_WithText_IsReported()
    {
        var item = new MemoryItem("m-1", "Preferences", "Prefers metric units.");

        item.HasCategory.Should().BeTrue();
        item.Category.Should().Be("Preferences");
        new MemoryItem("m-1", "\t", "t").HasCategory.Should().BeFalse();
    }

    [Fact]
    public void TheText_IsKeptVerbatim_AndBlankTextIsEmpty()
    {
        new MemoryItem("m-1", "Preferences", "  Prefers metric units.  ").Text
            .Should().Be("  Prefers metric units.  ", "the host's wording is never re-formatted");
        new MemoryItem("m-1", "Preferences", " ").Text.Should().BeEmpty();
    }

    // ── Status ───────────────────────────────────────────────────────────────

    [Fact]
    public void AnUnclassifiedMemory_IsActive()
    {
        var item = new MemoryItem("m-1", "Preferences", "Prefers metric units.");

        item.Status.Should().Be(MemoryItemStatus.Active);
        item.Status.Should().Be(default(MemoryItemStatus),
            "an unset status must never claim the memory was forgotten");
        item.IsActive.Should().BeTrue();
        item.IsPaused.Should().BeFalse();
        item.IsForgotten.Should().BeFalse();
        item.IsPendingReview.Should().BeFalse();
        item.HasKnownStatus.Should().BeTrue();
    }

    [Fact]
    public void TheStatusFlags_AreExclusive()
    {
        new MemoryItem("m-1", "C", "t") { Status = MemoryItemStatus.Paused }.IsPaused.Should().BeTrue();
        new MemoryItem("m-1", "C", "t") { Status = MemoryItemStatus.Forgotten }.IsForgotten.Should().BeTrue();
        new MemoryItem("m-1", "C", "t") { Status = MemoryItemStatus.PendingReview }.IsPendingReview.Should().BeTrue();

        var pending = new MemoryItem("m-1", "C", "t") { Status = MemoryItemStatus.PendingReview };
        pending.IsActive.Should().BeFalse();
        pending.IsPaused.Should().BeFalse();
        pending.IsForgotten.Should().BeFalse();
    }

    [Fact]
    public void AStatusOutsideTheDeclaredRange_IsNotKnown()
    {
        var item = new MemoryItem("m-1", "C", "t") { Status = (MemoryItemStatus)42 };

        item.HasKnownStatus.Should().BeFalse("a cast integer is not a state the surface can paint");
        item.IsActive.Should().BeFalse();
    }

    // ── Confidence and source ────────────────────────────────────────────────

    [Fact]
    public void AConfidence_IsARatioAndOnlyAUsableOneIsReported()
    {
        new MemoryItem("m-1", "C", "t").HasConfidence.Should().BeFalse();
        new MemoryItem("m-1", "C", "t") { Confidence = 0.82 }.HasConfidence.Should().BeTrue();
        new MemoryItem("m-1", "C", "t") { Confidence = 0 }.HasConfidence.Should().BeTrue();
        new MemoryItem("m-1", "C", "t") { Confidence = 1 }.HasConfidence.Should().BeTrue();

        new MemoryItem("m-1", "C", "t") { Confidence = -0.1 }.HasConfidence.Should().BeFalse();
        new MemoryItem("m-1", "C", "t") { Confidence = 1.4 }.HasConfidence.Should().BeFalse(
            "a value above one is not a ratio the surface can print as a percentage");
        new MemoryItem("m-1", "C", "t") { Confidence = double.NaN }.HasConfidence.Should().BeFalse();
    }

    [Fact]
    public void ASource_IsOnlyReportedWhenItCarriesText()
    {
        new MemoryItem("m-1", "C", "t").HasSource.Should().BeFalse();
        new MemoryItem("m-1", "C", "t") { Source = "  " }.HasSource.Should().BeFalse();
        new MemoryItem("m-1", "C", "t") { Source = "Support ticket #4211" }.HasSource.Should().BeTrue();
    }

    [Fact]
    public void TheTimestamps_AreWhatTheHostRecorded_AndUpdatedIsOptional()
    {
        var item = new MemoryItem("m-1", "C", "t") { CreatedAt = Ten };

        item.CreatedAt.Should().Be(Ten);
        item.UpdatedAt.Should().BeNull();
        (item with { UpdatedAt = Ten.AddHours(2) }).UpdatedAt.Should().Be(Ten.AddHours(2));
    }

    // ── Review and bulk contracts ────────────────────────────────────────────

    [Fact]
    public void AReview_NamesTheMemoryAndTheDecision()
    {
        var item = new MemoryItem("m-1", "Preferences", "Prefers metric units.");
        var review = new MemoryReview(item, MemoryReviewDecision.Approve);

        review.Item.Should().BeSameAs(item);
        review.Decision.Should().Be(MemoryReviewDecision.Approve);
        new MemoryReview(item, MemoryReviewDecision.Reject).Decision.Should().Be(MemoryReviewDecision.Reject);
        Enum.GetValues<MemoryReviewDecision>().Should().BeEquivalentTo(
            new[] { MemoryReviewDecision.Approve, MemoryReviewDecision.Reject });
    }

    [Fact]
    public void ABulkRequest_CopiesTheItems_SoALaterHostMutationCannotChangeWhatWasReported()
    {
        var items = new List<MemoryItem> { new("m-1", "C", "one") };
        var request = new MemoryBulkActionRequest(items, MemoryBulkAction.Pause);

        items.Add(new MemoryItem("m-2", "C", "two"));

        request.Items.Should().HaveCount(1, "the request is a snapshot of what the reader acted on");
        request.Action.Should().Be(MemoryBulkAction.Pause);
        Enum.GetValues<MemoryBulkAction>().Should().BeEquivalentTo(
            new[] { MemoryBulkAction.Pause, MemoryBulkAction.Resume, MemoryBulkAction.Forget });
    }

    [Fact]
    public void TheForgetMode_SaysWhetherAnUndoWindowIsOffered_AndTheWindowIsTheDefault()
    {
        MemoryForgetMode.UndoWindow.Should().Be(default(MemoryForgetMode),
            "losing a memory without a way back must never be the unset default");
        Enum.GetValues<MemoryForgetMode>().Should().BeEquivalentTo(
            new[] { MemoryForgetMode.UndoWindow, MemoryForgetMode.Immediate });
        Enum.GetValues<MemoryStatusFilter>().Should().BeEquivalentTo(
            new[]
            {
                MemoryStatusFilter.All, MemoryStatusFilter.Active, MemoryStatusFilter.Paused,
                MemoryStatusFilter.PendingReview, MemoryStatusFilter.Forgotten,
            });
    }
}
