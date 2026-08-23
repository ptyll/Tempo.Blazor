using FluentAssertions.Execution;
using System.Reflection;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// The <c>FactAttribute</c> subclasses in this folder decide their skip from a probe. This is the
/// needle over the OTHER half of that property: an explicitly written
/// <c>[FeedReachableFact(Skip = "flaky, see #123")]</c> must actually skip.
/// <para>
/// THE DEFECT THIS WAS WRITTEN AGAINST, read as a red before it was fixed: both getters returned their
/// probe's verdict and never read <c>base.Skip</c> back, so the reason the setter stored was dropped on
/// the floor. Measured on the unfixed code — <c>StagedPackagesFactAttribute</c> answered
/// <c>"[ReleaseContract]…"</c> where <c>"x"</c> was written, and <c>FeedReachableFactAttribute</c>
/// answered <c>&lt;null&gt;</c>, which is the same defect wearing the shape of "no skip requested".
/// </para>
/// <para>
/// EVERY ATTRIBUTE IS MEASURED IN TWO CELLS, and that is not thoroughness for its own sake. A needle
/// that only asked "an unset Skip is null" would have been GREEN on the unfixed feed attribute, because
/// online its probe returns null too — one cell cannot tell "the probe decided" apart from "the written
/// reason was swallowed". The pair has to disagree: written reason in, written reason out; nothing
/// written, probe verdict out.
/// </para>
/// <para>
/// AND THE UNSET ARM OF <see cref="ReleaseContractTests.FeedReachableFactAttribute"/> IS MEASURED
/// WITHOUT TOUCHING THE NETWORK — deliberately, because the honest expectation for a live probe is
/// whatever a second live probe says, and two probes can disagree about a world that moved between
/// them. It is covered instead through the shape both attributes now share:
/// <see cref="SharedShape_IsWhereEveryProbeDecidedAttributeGetsItsGetter"/> pins that none of them
/// declares a <c>Skip</c> of its own, and <see cref="UnsetSkip_ReturnsTheProbeVerdict_OnTheSharedShape"/> measures
/// both cells over a probe whose verdict is known. What is NOT claimed: that the feed attribute's own
/// probe was exercised here.
/// </para>
/// </summary>
public sealed class ProbeDecidedSkipTests
{
    /// <summary>The reason a human writes at the call site. No probe can ever produce this string.</summary>
    private const string WrittenReason = "x";

    /// <summary>What the stand-in probe below answers — a verdict that is neither null nor
    /// <see cref="WrittenReason"/>, so the two cells cannot be satisfied by one constant.</summary>
    private const string StandInVerdict = "the stand-in probe decided to skip";

    [Fact]
    public void ExplicitSkip_WinsOverTheProbe_OnTheStagedFact()
    {
        new ReleaseContractTests.StagedPackagesFactAttribute { Skip = WrittenReason }.Skip
            .Should().Be(
                WrittenReason,
                "a reason written at the call site is the one thing the runner must honour; a getter "
                + "that returns only its own probe verdict swallows it and runs the test anyway");
    }

    [Fact]
    public void ExplicitSkip_WinsOverTheProbe_OnTheFeedFact()
    {
        new ReleaseContractTests.FeedReachableFactAttribute { Skip = WrittenReason }.Skip
            .Should().Be(
                WrittenReason,
                "same shape, second copy: [FeedReachableFact(Skip = \"...\")] is silently ignored when "
                + "the getter never reads what the setter stored");
    }

    /// <summary>
    /// The OTHER cell, and the reason this is a pair rather than a single assertion: a needle that only
    /// asked "unset means null" would pass over the broken getter on any run where the probe happened to
    /// return null. The unset arm must still be the PROBE's verdict, so the two cells have to disagree.
    /// </summary>
    [Fact]
    public void UnsetSkip_StillReturnsTheProbeVerdict_OnTheStagedFact()
    {
        var probeVerdict = ReleaseContractTests.ReleaseStagingSurvey.Take().NothingCouldBeOpened;

        using (new AssertionScope())
        {
            new ReleaseContractTests.StagedPackagesFactAttribute().Skip.Should().Be(
                probeVerdict,
                "with no reason written at the call site the attribute must still decide from the "
                + "staging survey; a getter reduced to base.Skip would return null here and run the "
                + "guard over an empty population");

            new ReleaseContractTests.StagedPackagesFactAttribute().Skip.Should().NotBe(
                WrittenReason,
                "off-diagonal: the two cells must differ, or a getter that returns the written reason "
                + "unconditionally would satisfy both");
        }
    }

    /// <summary>
    /// Both cells over the shared getter, with a probe whose verdict is known and no network in reach.
    /// This is what carries the unset arm for the feed attribute, which cannot be measured directly
    /// without a second live request.
    /// </summary>
    [Fact]
    public void UnsetSkip_ReturnsTheProbeVerdict_OnTheSharedShape()
    {
        using (new AssertionScope())
        {
            new StandInProbeFactAttribute().Skip.Should().Be(
                StandInVerdict,
                "with nothing written at the call site the shared getter must hand back what the probe "
                + "said; returning null here would run every probe-gated guard over a world it never "
                + "asked about");

            new StandInProbeFactAttribute { Skip = WrittenReason }.Skip.Should().Be(
                WrittenReason,
                "and the written reason must win over that same probe — the two cells disagree, so no "
                + "single hardcoded answer satisfies both");

            new StandInProbeFactAttribute { Skip = null }.Skip.Should().Be(
                StandInVerdict,
                "null written at the call site is not a reason, it is the absence of one, so the probe "
                + "still decides; otherwise Skip = null would silently force a test to run");

            new ThrowingProbeFactAttribute().Skip.Should().BeNull(
                "a probe that throws must NOT become a skip: the test is handed to the runner, where "
                + "the same exception is thrown again in the body and reported with its stack, instead "
                + "of every future breakage looking like the ordinary state the probe recognises");
        }
    }

    /// <summary>
    /// The reason the cells above are allowed to stand in for the real attributes: the getter they
    /// measure is the one those attributes use. A copy re-declared on any of them would move the
    /// declaring type and turn this red — which is precisely how the original defect spread, by copy.
    /// <para>
    /// THE LIST IS THE POPULATION, and it is enumerated rather than discovered on purpose: a reflective
    /// sweep for every <see cref="ProbeDecidedFactAttribute"/> subclass would silently include the
    /// stand-ins declared in this file, whose whole job is to have known verdicts, and would report a
    /// green over a list nobody chose. What this cell therefore does NOT say is that these are all the
    /// probe-decided attributes that exist — an attribute added without being named here is not
    /// measured by it.
    /// </para>
    /// </summary>
    [Fact]
    public void SharedShape_IsWhereEveryProbeDecidedAttributeGetsItsGetter()
    {
        using (new AssertionScope())
        {
            foreach (var attributeType in new[]
                     {
                         typeof(ReleaseContractTests.StagedPackagesFactAttribute),
                         typeof(ReleaseContractTests.FeedReachableFactAttribute),
                         typeof(BashScriptFactAttribute),
                         typeof(BashScriptFeedReachableFactAttribute),
                     })
            {
                attributeType.Should().BeDerivedFrom<ProbeDecidedFactAttribute>(
                    "the probe-gated fact shape lives in one place so a defect in it cannot be fixed in "
                    + "one copy and left in the other");

                attributeType.GetProperty("Skip", BindingFlags.Public | BindingFlags.Instance)
                    !.DeclaringType.Should().Be(
                        typeof(ProbeDecidedFactAttribute),
                        $"{attributeType.Name} must not declare a Skip of its own; a re-declared getter "
                        + "would be exactly the copy that duplicated the swallowed-skip defect in the "
                        + "first place, and the cells over the shared shape would stay green over it");
            }
        }
    }

    /// <summary>A probe with a known verdict, so both cells of the shared getter are exact.</summary>
    private sealed class StandInProbeFactAttribute : ProbeDecidedFactAttribute
    {
        protected override string? ProbeSkipReason() => StandInVerdict;
    }

    /// <summary>A probe that fails the way a broken instrument fails.</summary>
    private sealed class ThrowingProbeFactAttribute : ProbeDecidedFactAttribute
    {
        protected override string? ProbeSkipReason() =>
            throw new InvalidOperationException("the probe itself is broken");
    }
}
