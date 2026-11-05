using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 5 — the <c>FallbackAttempt</c> / <c>FallbackChainEntry</c> models and the resolution policy.
/// <para>
/// These facts pin the rules <c>TmFallbackChain</c> reads: an unclassified attempt is <c>NotTried</c> (never
/// "succeeded"), an unmeasured latency stays unmeasured instead of becoming zero, a blank model id is generated, the
/// label falls back to the id so no branch is blank, and an unset policy is a PIN — an unset value must never silently
/// fall back to another model.
/// </para>
/// </summary>
public class FallbackAttemptTests
{
    private static readonly DateTimeOffset Ten = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void ABlankModelId_IsGenerated_AndASuppliedOneIsKept()
    {
        new FallbackAttempt(" ").ModelId.Should().NotBeNullOrWhiteSpace();
        new FallbackAttempt("gpt-4o").ModelId.Should().Be("gpt-4o");
    }

    [Fact]
    public void TheDisplayName_IsTheLabel_OrTheModelId()
    {
        new FallbackAttempt("gpt-4o").DisplayName.Should().Be("gpt-4o");
        new FallbackAttempt("gpt-4o").HasLabel.Should().BeFalse();

        var labelled = new FallbackAttempt("gpt-4o") { Label = "GPT-4o" };
        labelled.DisplayName.Should().Be("GPT-4o");
        labelled.HasLabel.Should().BeTrue();
    }

    // ── Status and defaults ──────────────────────────────────────────────────

    [Fact]
    public void AnUnclassifiedAttempt_IsNotTried()
    {
        var attempt = new FallbackAttempt("gpt-4o");

        attempt.Status.Should().Be(FallbackAttemptStatus.NotTried);
        attempt.Status.Should().Be(default(FallbackAttemptStatus));
        attempt.Succeeded.Should().BeFalse("an unset status must never claim an answer was produced");
        attempt.Failed.Should().BeFalse();
        attempt.Skipped.Should().BeFalse();
        attempt.NotTried.Should().BeTrue();
        attempt.IsPassive.Should().BeTrue();
        attempt.HasKnownStatus.Should().BeTrue();
    }

    [Fact]
    public void TheStatusFlags_AreExclusive()
    {
        var succeeded = new FallbackAttempt("m") { Status = FallbackAttemptStatus.Succeeded };
        var failed = new FallbackAttempt("m") { Status = FallbackAttemptStatus.Failed };
        var skipped = new FallbackAttempt("m") { Status = FallbackAttemptStatus.Skipped };

        succeeded.Succeeded.Should().BeTrue();
        succeeded.IsPassive.Should().BeFalse();
        failed.Failed.Should().BeTrue();
        failed.IsPassive.Should().BeFalse();
        skipped.Skipped.Should().BeTrue();
        skipped.IsPassive.Should().BeTrue("nothing was spent on a skipped attempt");
    }

    [Fact]
    public void AnUnknownStatus_IsNotDeclared()
    {
        (new FallbackAttempt("m") { Status = (FallbackAttemptStatus)42 }).HasKnownStatus.Should().BeFalse();
    }

    // ── Latency and reason ───────────────────────────────────────────────────

    [Fact]
    public void AnUnmeasuredLatency_StaysUnmeasured()
    {
        var attempt = new FallbackAttempt("m");

        attempt.HasLatency.Should().BeFalse();
        attempt.Latency.Should().BeNull();
        (attempt with { LatencyMs = -5 }).HasLatency.Should().BeFalse(
            "a duration below zero is not a fact a surface can print");
        (attempt with { LatencyMs = -5 }).Latency.Should().BeNull();
    }

    [Fact]
    public void AMeasuredLatency_IsADuration()
    {
        var attempt = new FallbackAttempt("m") { LatencyMs = 1450 };

        attempt.HasLatency.Should().BeTrue();
        attempt.Latency.Should().Be(TimeSpan.FromMilliseconds(1450));
        (attempt with { LatencyMs = 0 }).HasLatency.Should().BeTrue("zero milliseconds is a real measurement");
    }

    [Fact]
    public void TheFailureReason_IsPrintedOnlyWhenItSaysSomething()
    {
        var failed = new FallbackAttempt("m")
        {
            Status = FallbackAttemptStatus.Failed,
            FailureReason = "rate limited",
            At = Ten,
        };

        failed.HasFailureReason.Should().BeTrue();
        failed.At.Should().Be(Ten);
        (failed with { FailureReason = "  " }).HasFailureReason.Should().BeFalse();
        new FallbackAttempt("m").At.Should().BeNull();
    }

    // ── The chain entry ──────────────────────────────────────────────────────

    [Fact]
    public void AChainEntry_NamesItsModel_AndFallsBackToTheId()
    {
        new FallbackChainEntry("claude", "Claude 3.5 Sonnet").DisplayName.Should().Be("Claude 3.5 Sonnet");
        new FallbackChainEntry("claude").DisplayName.Should().Be("claude");
        new FallbackChainEntry("claude").HasLabel.Should().BeFalse();
        new FallbackChainEntry(" ").ModelId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void AChainEntry_IsBuiltFromACatalogueModel()
    {
        var entry = FallbackChainEntry.FromModel(new AiModelInfo("gpt-4o", "GPT-4o"));

        entry.Should().NotBeNull();
        entry!.ModelId.Should().Be("gpt-4o");
        entry.Label.Should().Be("GPT-4o");
        FallbackChainEntry.FromModel(null).Should().BeNull();
    }

    [Fact]
    public void AnUnsetPolicy_IsAPin_SoNothingFallsBackByAccident()
    {
        var chain = new FallbackChainEntry("a");

        chain.Should().NotBeNull();
        ((FallbackResolutionPolicy)0).Should().Be(FallbackResolutionPolicy.Pin);
        FallbackResolutionPolicy.Pin.Should().Be(default(FallbackResolutionPolicy));
    }
}
