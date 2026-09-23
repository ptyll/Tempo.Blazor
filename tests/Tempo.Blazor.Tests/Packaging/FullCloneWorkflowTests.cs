using FluentAssertions;
using FluentAssertions.Execution;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// <c>fetch-depth: 0</c> was an unguarded promise (N208): every git-facing guard in this suite —
/// <see cref="ChangelogReferencesExistingCommitsTests"/>, the tag guard — silently SKIPped on a
/// shallow checkout, so deleting the line from both workflows left the gate green while it
/// measured nothing. Two holes are closed here: the checkout shape itself is asserted per job,
/// and <c>TEMPO_REQUIRE_FULL_CLONE=1</c> makes <see cref="FullCloneFactAttribute"/> turn a
/// would-be skip into a failure inside CI instead of reporting a clean skip.
/// </summary>
public sealed class FullCloneWorkflowTests
{
    /// <summary>
    /// A checkout step's <c>fetch-depth: 0</c>, anchored as a full line — <c>fetch-depth: 1</c>
    /// is a different answer, not a weaker one, and must be named rather than counted.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex FullDepthIsSetHere = new(
        @"^[ \t]*fetch-depth:[ \t]*0[ \t]*$",
        System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// Names of the checkout blocks (as <c>&lt;job&gt;#&lt;index&gt;</c>) that lack a
    /// <c>fetch-depth: 0</c> line before the next step begins. A checkout whose <c>with:</c>
    /// carries no depth fetches the default depth 1 — the shape <c>fetch-depth: 1</c> spells
    /// explicitly and this guard treats identically.
    /// </summary>
    internal static IReadOnlyList<string> CheckoutsWithoutFullHistory(string workflowCode)
    {
        List<string> offenders = [];
        foreach ((string name, string body) in ReleaseGateFilterTests.JobSegments(workflowCode))
        {
            var uses = System.Text.RegularExpressions.Regex.Matches(
                body, @"uses:[ \t]*actions/checkout@");
            for (int index = 0; index < uses.Count; index++)
            {
                string tail = body[uses[index].Index..];
                var nextStep = System.Text.RegularExpressions.Regex.Match(
                    tail, @"^[ \t]*-[ \t]*name:",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                string block = nextStep.Success ? tail[..nextStep.Index] : tail;
                if (!FullDepthIsSetHere.IsMatch(block))
                {
                    offenders.Add($"{name}#{index}");
                }
            }
        }

        return offenders;
    }

    [Fact]
    public void BothPublishWorkflowsCheckoutFullHistory()
    {
        foreach (string relative in ReleaseGateFilterTests.WorkflowRelativePaths)
        {
            string code = ReleaseGateFilterTests.ReadWorkflowCode(relative);
            IReadOnlyList<string> offenders = CheckoutsWithoutFullHistory(code);

            using (new AssertionScope())
            {
                // Population first: each workflow checks out twice (build-and-test, publish); a
                // segmentation that found no checkout would report an empty offender list out of
                // an empty list — silence, not evidence.
                ReleaseGateFilterTests.JobSegments(code)
                    .Count(segment => segment.Body.Contains("actions/checkout@", StringComparison.Ordinal))
                    .Should().BeGreaterThanOrEqualTo(
                        2,
                        $"{relative} must check out in at least the build-and-test and publish "
                        + "jobs; fewer means the guard is measuring a missing checkout, not a "
                        + "full-history one");

                offenders.Should().BeEmpty(
                    $"{relative}: every actions/checkout must carry fetch-depth: 0 — at depth 1 "
                    + "the suite's git-facing guards (changelog commit ids, the tag guard, the "
                    + "release-evidence ancestry check) silently cannot answer (N208)");
            }
        }
    }

    /// <summary>
    /// Mutation proof over the same evaluation: dropping the depth from either checkout, lowering
    /// it to 1, or commenting it out must each name an offender — and the healthy file stays
    /// green.
    /// </summary>
    [Fact]
    public void TheCheckoutGuard_DetectsItsLoss_InEitherJob()
    {
        string healthy = ReleaseGateFilterTests.ReadWorkflowCode(
            ReleaseGateFilterTests.WorkflowRelativePaths[1]); // publish-nuget-org.yml
        const string depthLine = "          fetch-depth: 0\n";

        using (new AssertionScope())
        {
            CheckoutsWithoutFullHistory(healthy).Should().BeEmpty(
                "positive control: the committed workflows fetch full history, or the reds below "
                + "say nothing");

            // First checkout belongs to build-and-test, second to publish — remove each in turn.
            string withoutFirst = healthy.Remove(healthy.IndexOf(depthLine, StringComparison.Ordinal), depthLine.Length);
            CheckoutsWithoutFullHistory(withoutFirst).Should().BeEquivalentTo(
                ["build-and-test#0"],
                "deleting fetch-depth from the build-and-test checkout must name it");

            int second = healthy.IndexOf(depthLine, StringComparison.Ordinal);
            second = healthy.IndexOf(depthLine, second + depthLine.Length, StringComparison.Ordinal);
            string withoutSecond = healthy.Remove(second, depthLine.Length);
            CheckoutsWithoutFullHistory(withoutSecond).Should().BeEquivalentTo(
                ["publish#0"],
                "deleting fetch-depth from the publish checkout must name it — the publish job "
                + "needs the same ref store for the release-evidence ancestry check");

            CheckoutsWithoutFullHistory(
                    healthy.Replace("          fetch-depth: 0\n", "          fetch-depth: 1\n",
                        StringComparison.Ordinal))
                .Should().NotBeEmpty(
                    "fetch-depth: 1 is the shallow default spelled out — the guard reads the "
                    + "value, not just the key's presence");

            CheckoutsWithoutFullHistory(
                    ReleaseGateFilterTests.StripYamlComments(
                        ReleaseGateFilterTests.CommentOutLinesContaining(healthy, "fetch-depth: 0")))
                .Should().HaveCount(
                    2,
                    "a commented-out depth fetches depth 1 on BOTH checkouts — the 'delete the "
                    + "code, keep the prose' hole");
        }
    }

    /// <summary>
    /// CI must not get a silent skip: both workflows export <c>TEMPO_REQUIRE_FULL_CLONE=1</c> on
    /// the job that runs the suite, so a shallow checkout turns the probe's would-be skip into a
    /// thrown <see cref="InvalidOperationException"/> — which the
    /// <see cref="ProbeDecidedFactAttribute.Skip"/> getter reads as "run the member", where the
    /// member's own git calls then fail with the real evidence instead of a clean skip.
    /// </summary>
    [Fact]
    public void BothPublishWorkflows_RequireFullCloneInTheTestJob()
    {
        foreach (string relative in ReleaseGateFilterTests.WorkflowRelativePaths)
        {
            string code = ReleaseGateFilterTests.ReadWorkflowCode(relative);
            (string Name, string Body) buildAndTest = ReleaseGateFilterTests
                .JobSegments(code)
                .Single(segment => segment.Name == "build-and-test");

            ReleaseGateFilterTests.JobLevelEnvBlock(buildAndTest.Body)
                .Should().Contain(
                    "TEMPO_REQUIRE_FULL_CLONE",
                    $"{relative}: the build-and-test job must export TEMPO_REQUIRE_FULL_CLONE=1 so "
                    + "a regression to a shallow checkout fails the suite rather than skipping "
                    + "every git-facing guard green (N208)");
        }
    }

    /// <summary>
    /// The attribute half, exercised against a REAL shallow clone in <c>$TEMP</c>: without the
    /// variable the probe answers the ordinary skip reason (today's behaviour, unchanged outside
    /// CI); with <c>TEMPO_REQUIRE_FULL_CLONE=1</c> the same probe throws — the throw is what the
    /// <c>Skip</c> getter's <c>catch (Exception)</c> converts into "run the member", and the
    /// member then fails on its own git calls. A green skip over a shallow clone is exactly the
    /// silence N208 names.
    /// </summary>
    [BashScriptFact]
    public void RequireFullCloneEnv_TurnsAShallowCloneSkip_IntoAThrow()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        string cloneParent = Path.Combine(Path.GetTempPath(), $"tm-shallow-{Guid.NewGuid():N}");
        string clonePath = Path.Combine(cloneParent, "shallow");
        Directory.CreateDirectory(cloneParent);

        try
        {
            (int exit, _, string stderr) = FullCloneFactAttribute.RunGit(
                cloneParent, "clone", "--depth", "1", "--no-tags",
                "file://" + root, clonePath);
            exit.Should().Be(0, $"fixture shallow clone must succeed (stderr: {stderr})");

            string? withoutEnv = FullCloneFactAttribute.IncompleteCloneSkipReason(clonePath);
            withoutEnv.Should().NotBeNull(
                "a depth-1 clone must report a skip reason — that is today's documented behaviour "
                + "outside CI and it must not change");

            string? previous = Environment.GetEnvironmentVariable("TEMPO_REQUIRE_FULL_CLONE");
            try
            {
                Environment.SetEnvironmentVariable("TEMPO_REQUIRE_FULL_CLONE", "1");
                Action probe = () => FullCloneFactAttribute.IncompleteCloneSkipReason(clonePath);
                probe.Should().Throw<InvalidOperationException>()
                    .WithMessage("*shallow*",
                        "with the CI contract set, the same shallow clone must throw rather than "
                        + "answer a skip reason — the Skip getter reads any probe failure as "
                        + "'run the member', where the member's own git calls then fail red");
            }
            finally
            {
                Environment.SetEnvironmentVariable("TEMPO_REQUIRE_FULL_CLONE", previous);
            }

            // And the honest counter-arm: the real repository answers null either way — the
            // variable only sharpens a clone that genuinely cannot answer.
            FullCloneFactAttribute.IncompleteCloneSkipReason(root).Should().BeNull(
                "the suite's own clone is full; the probe must not invent a reason over it");
        }
        finally
        {
            ReleaseScriptInputReadTests.TryDeleteDir(cloneParent);
        }
    }
}
