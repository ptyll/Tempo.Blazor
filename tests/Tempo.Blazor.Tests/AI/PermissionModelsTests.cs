using FluentAssertions;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.AI;

/// <summary>
/// Unit contract of the permission models in <c>Tempo.Blazor.Abstractions/AI/Permissions</c> (plan
/// 10b3966f, phase 5): a <see cref="PermissionGrant"/> is the permission the reader actually gave (a
/// scope plus, for a timed grant, both ends of its window), a <see cref="PermissionDecision"/> is the
/// answer the host receives, and both refuse the combinations that have no meaning — so a component
/// can never build a grant that claims to expire without an expiry, or a denial that grants
/// something.
/// </summary>
public class PermissionModelsTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    // ── PermissionGrant ──────────────────────────────────────────────────────

    [Fact]
    public void ATimedGrant_KeepsBothEndsOfItsWindow()
    {
        var grant = new PermissionGrant(PermissionScope.ForDuration, Start.AddHours(1), Start);

        grant.Scope.Should().Be(PermissionScope.ForDuration);
        grant.ExpiresAt.Should().Be(Start.AddHours(1));
        grant.GrantedAt.Should().Be(Start);
        grant.Duration.Should().Be(TimeSpan.FromHours(1),
            "the granted window is what the collapsed row reads back to the reader");
    }

    [Fact]
    public void ATimedGrant_WithoutAnExpiry_IsRejected()
    {
        var act = () => new PermissionGrant(PermissionScope.ForDuration);

        act.Should().Throw<ArgumentException>(
            "a grant that says 'for a while' without saying until when cannot be honoured");
    }

    [Fact]
    public void AGrantWithoutADuration_RejectsAnExpiry()
    {
        var act = () => new PermissionGrant(PermissionScope.Once, Start.AddMinutes(5));

        act.Should().Throw<ArgumentException>(
            "'allow once' never expires — an expiry would make the same grant mean two things");
    }

    [Fact]
    public void ASessionGrant_MayCarryTheEndOfTheSession()
    {
        var grant = new PermissionGrant(PermissionScope.Session, Start.AddHours(8));

        grant.ExpiresAt.Should().Be(Start.AddHours(8));
        grant.Duration.Should().BeNull("no granted-at was supplied, so only the end is known");
    }

    [Fact]
    public void TheWindowAndTheRemainingTime_AreComputedFromTheInjectedClock()
    {
        var grant = new PermissionGrant(PermissionScope.ForDuration, Start.AddHours(1), Start);

        grant.IsExpired(Start.AddMinutes(59)).Should().BeFalse();
        grant.Remaining(Start.AddMinutes(18)).Should().Be(TimeSpan.FromMinutes(42));
        grant.IsExpired(Start.AddHours(1)).Should().BeTrue();
        grant.Remaining(Start.AddHours(2)).Should().Be(TimeSpan.Zero,
            "a passed deadline never produces a negative countdown");
        new PermissionGrant(PermissionScope.Always).IsExpired(Start.AddYears(10)).Should().BeFalse();
    }

    [Fact]
    public void AGrantThatExpiresBeforeItWasGiven_IsRejected()
    {
        var act = () => new PermissionGrant(PermissionScope.ForDuration, Start.AddMinutes(-1), Start);

        act.Should().Throw<ArgumentException>();
    }

    // ── PermissionDecision ───────────────────────────────────────────────────

    [Fact]
    public void ADenial_GrantsNothing()
    {
        var decision = PermissionDecision.Deny(Start);

        decision.IsGranted.Should().BeFalse();
        decision.IsDenied.Should().BeTrue();
        decision.Grant.Should().BeNull();
        decision.DecidedAt.Should().Be(Start);
    }

    [Fact]
    public void AGrantDecision_CarriesItsGrant()
    {
        var grant = new PermissionGrant(PermissionScope.Always);
        var decision = PermissionDecision.Allow(grant, Start);

        decision.IsGranted.Should().BeTrue();
        decision.IsDenied.Should().BeFalse();
        decision.Grant.Should().BeSameAs(grant);
        decision.Scope.Should().Be(PermissionScope.Always,
            "the scope of a granted decision is the scope of the grant it carries");
    }

    [Fact]
    public void AGrantWithoutAGrant_IsRejected()
    {
        var act = () => PermissionDecision.Allow(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ADenialThatGrantsSomething_IsRejected()
    {
        var act = () => new PermissionDecision(
            isGranted: false,
            grant: new PermissionGrant(PermissionScope.Once));

        act.Should().Throw<ArgumentException>();
    }
}
