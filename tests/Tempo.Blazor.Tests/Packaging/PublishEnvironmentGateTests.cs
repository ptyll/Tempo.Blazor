using FluentAssertions;
using FluentAssertions.Execution;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// DEC-TEMPO-NUGET-ENVIRONMENT (F4): the job that pushes packages to a feed must run inside the
/// GitHub Environment <c>nuget-org</c>, so a tag push or a workflow_dispatch can never reach
/// <c>eng/push-nuget-packages.sh</c> without the owner's required-reviewer approval.
/// <para>
/// WHAT A GREEN HERE PROVES — the same admitted limit its siblings state: the guard measures that
/// the declaration is PRESENT at job level in the one job that can push, in both workflows. That
/// the environment exists in GitHub UI with required reviewer <c>ptyll</c> is an out-of-repo
/// condition (N206) — until it is created, GitHub fails the job with "Environment not found",
/// which is fail-CLOSED, so this YAML names the requirement before the UI can honour it.
/// </para>
/// </summary>
public sealed class PublishEnvironmentGateTests
{
    /// <summary>
    /// The job-level declaration, anchored at the four-space indent of a job key — a step-level
    /// <c>environment:</c> (deeper indent) does not put the JOB inside the environment, and an
    /// indented substring match would read one as if it did.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex NugetOrgEnvironmentDeclared = new(
        @"^    environment:[ \t]*nuget-org[ \t]*$",
        System.Text.RegularExpressions.RegexOptions.Multiline);

    /// <summary>
    /// Names of the jobs that invoke <c>eng/push-nuget-packages.sh</c> without declaring the
    /// <c>nuget-org</c> environment at job level. The population is the jobs that can push — the
    /// only jobs where a missing environment is a hole; <c>build-and-test</c> pushes nothing and
    /// is deliberately not in it.
    /// </summary>
    internal static IReadOnlyList<string> PushJobsWithoutTheNugetOrgEnvironment(string workflowCode)
    {
        List<string> offenders = [];
        foreach ((string name, string body) in ReleaseGateFilterTests.JobSegments(workflowCode))
        {
            if (!body.Contains("bash eng/push-nuget-packages.sh", StringComparison.Ordinal))
            {
                continue;
            }

            if (!NugetOrgEnvironmentDeclared.IsMatch(body))
            {
                offenders.Add(name);
            }
        }

        return offenders;
    }

    [Fact]
    public void PublishJobsThatPushPackagesDeclareTheNugetOrgEnvironment()
    {
        foreach (string relative in ReleaseGateFilterTests.WorkflowRelativePaths)
        {
            string code = ReleaseGateFilterTests.ReadWorkflowCode(relative);
            IReadOnlyList<string> offenders = PushJobsWithoutTheNugetOrgEnvironment(code);

            using (new AssertionScope())
            {
                code.Should().Contain(
                    "bash eng/push-nuget-packages.sh",
                    $"{relative} must contain a job that pushes packages; if the push step is gone "
                    + "the environment assertion measures an empty population, not evidence");

                offenders.Should().BeEmpty(
                    $"{relative}: every job running bash eng/push-nuget-packages.sh must declare "
                    + "'environment: nuget-org' at job level (DEC-TEMPO-NUGET-ENVIRONMENT, F4) — "
                    + "without it a tag push or workflow_dispatch reaches the feed with no "
                    + "required-reviewer approval");
            }
        }
    }

    /// <summary>
    /// Five-step mutation proof over the SAME evaluation the live assertion uses: delete the
    /// declaration, rename the environment, comment it out, and move it onto a job that pushes
    /// nothing must each turn the guard red — and the healthy file must stay green.
    /// </summary>
    [Fact]
    public void TheEnvironmentGuard_DetectsItsLoss_AndOnlyThePushJobCounts()
    {
        string healthy = ReleaseGateFilterTests.ReadWorkflowCode(
            ReleaseGateFilterTests.WorkflowRelativePaths[1]); // publish-nuget-org.yml
        const string declaration = "    environment: nuget-org\n";

        using (new AssertionScope())
        {
            PushJobsWithoutTheNugetOrgEnvironment(healthy).Should().BeEmpty(
                "the positive control: with the environment declared the guard has to be green, "
                + "or the reds below say nothing");

            string deleted = healthy.Replace(declaration, "", StringComparison.Ordinal);
            deleted.Should().NotBe(healthy, "the mutation must actually change the text");
            PushJobsWithoutTheNugetOrgEnvironment(deleted).Should().BeEquivalentTo(
                ["publish"],
                "deleting the declaration must name the push job");

            PushJobsWithoutTheNugetOrgEnvironment(
                    healthy.Replace(
                        "    environment: nuget-org\n",
                        "    environment: nuget-staging\n",
                        StringComparison.Ordinal))
                .Should().BeEquivalentTo(
                    ["publish"],
                    "a different environment name is not the nuget-org gate — staging approval "
                    + "does not approve nuget.org");

            PushJobsWithoutTheNugetOrgEnvironment(
                    ReleaseGateFilterTests.StripYamlComments(
                        ReleaseGateFilterTests.CommentOutLinesContaining(
                            healthy, "environment: nuget-org")))
                .Should().BeEquivalentTo(
                    ["publish"],
                    "a commented-out declaration deploys to no environment — the 'delete the "
                    + "code, keep the prose' hole the sibling guards already name");

            string movedToTestJob = healthy
                .Replace(declaration, "", StringComparison.Ordinal)
                .Replace(
                    "  build-and-test:\n",
                    "  build-and-test:\n    environment: nuget-org\n",
                    StringComparison.Ordinal);
            movedToTestJob.Split("    environment: nuget-org", StringSplitOptions.None)
                .Should().HaveCount(
                    2,
                    "the mutation must move the line, not duplicate it — exactly one "
                    + "environment: nuget-org line remains, now on build-and-test");
            PushJobsWithoutTheNugetOrgEnvironment(movedToTestJob).Should().BeEquivalentTo(
                ["publish"],
                "the declaration on a job that pushes nothing covers nothing — the guard reads "
                + "the segment that invokes the push script, not the file");
        }
    }
}
