using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 6 — the <c>AutonomyLevel</c> / <c>AutonomyLock</c> models behind <c>TmAutonomyDial</c>.
/// <para>
/// The level is a VALUE the host composes: it carries the numeric ceiling key, the label a reader sees and the sentence
/// that says what the agent may do at that stop. A lock is a constraint somebody ELSE enforces (a tenant policy, an
/// administrator), so it always names the action, the source that enforces it and, when known, the reason — a lock
/// without a source would be an unexplained disabled control.
/// </para>
/// </summary>
public class AutonomyLevelTests
{
    // ── AutonomyLevel ────────────────────────────────────────────────────────

    [Fact]
    public void ALevel_KeepsTheValueItWasBuiltWith_AndHasNoLabelOrDescriptionByDefault()
    {
        var level = new AutonomyLevel(2);

        level.Value.Should().Be(2);
        level.Label.Should().BeEmpty();
        level.Description.Should().BeEmpty();
        level.HasLabel.Should().BeFalse();
        level.HasDescription.Should().BeFalse();
    }

    [Fact]
    public void ALevel_CarriesItsLabelAndDescription()
    {
        var level = new AutonomyLevel(1) { Label = "Ask when risky", Description = "Risky actions wait for approval." };

        level.HasLabel.Should().BeTrue();
        level.HasDescription.Should().BeTrue();
        level.Label.Should().Be("Ask when risky");
        level.Description.Should().Be("Risky actions wait for approval.");
    }

    [Fact]
    public void AWhitespaceLabel_CountsAsNoLabel()
    {
        var level = new AutonomyLevel(0) { Label = "   ", Description = "\t" };

        level.HasLabel.Should().BeFalse("the surface renders its own fallback instead of a blank gap");
        level.HasDescription.Should().BeFalse();
    }

    [Fact]
    public void ALevel_IsAValue_SoARecomposedStopIsStillEqual()
    {
        var level = new AutonomyLevel(1) { Label = "Ask when risky" };
        var copy = level with { Description = "Risky actions wait for approval." };

        copy.Should().NotBe(level, "the description is part of the value");
        (level with { }).Should().Be(level);
        copy.Value.Should().Be(1);
    }

    // ── AutonomyLock ─────────────────────────────────────────────────────────

    [Fact]
    public void ALock_NamesItsAction_AndHasNoSourceOrReasonByDefault()
    {
        var lockItem = new AutonomyLock("Send external email");

        lockItem.ActionLabel.Should().Be("Send external email");
        lockItem.Source.Should().BeEmpty();
        lockItem.Reason.Should().BeNull();
        lockItem.HasSource.Should().BeFalse();
        lockItem.HasReason.Should().BeFalse();
    }

    [Fact]
    public void ALock_WithASource_IsEnforcedBySomebody()
    {
        var lockItem = new AutonomyLock("Send external email")
        {
            Source = "Tenant policy",
            Reason = "Outbound mail needs a human sign-off.",
        };

        lockItem.HasSource.Should().BeTrue();
        lockItem.HasReason.Should().BeTrue();
        lockItem.Source.Should().Be("Tenant policy");
        lockItem.Reason.Should().Be("Outbound mail needs a human sign-off.");
    }

    [Fact]
    public void AWhitespaceSourceOrReason_CountsAsNotReported()
    {
        var lockItem = new AutonomyLock("Delete a file") { Source = "  ", Reason = " " };

        lockItem.HasSource.Should().BeFalse("an empty source is not a source the surface can print");
        lockItem.HasReason.Should().BeFalse();
    }

    [Fact]
    public void TheTwoPresentations_AreDialAndSegmented_AndDialIsTheDefaultValue()
    {
        AutonomyDialPresentation.Dial.Should().Be(default(AutonomyDialPresentation),
            "an unset presentation must land on the dial rather than on a cast integer");

        Enum.GetValues<AutonomyDialPresentation>().Should().BeEquivalentTo(
            new[] { AutonomyDialPresentation.Dial, AutonomyDialPresentation.Segmented });
    }
}
