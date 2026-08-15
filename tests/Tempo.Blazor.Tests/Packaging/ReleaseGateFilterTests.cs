using FluentAssertions;
using FluentAssertions.Execution;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// The two publish workflows are the Tempo release gate. Their <c>--filter</c> is the list of
/// named exceptions, not a convenience. A clause added by hand is a silent hole; a clause
/// removed by hand is a red CI that nobody can satisfy. This guard reads both YAML files and
/// compares the filter to the documented exceptions.
/// </summary>
/// <remarks>
/// Fáze 12: Demo.Api including the two smtp4dev tests IS in the gate (CI starts smtp4dev).
/// <c>Tempo.Blazor.E2E</c> and <c>Tempo.ReportServer.Api.Tests.MsSql</c> stay out until their
/// cancellation conditions fire. The full solution suite is not the gate — see
/// <c>DEC-TEMPO-RELEASE-GATE</c>.
/// </remarks>
public sealed class ReleaseGateFilterTests
{
    private static readonly string[] WorkflowRelativePaths =
    [
        Path.Combine(".github", "workflows", "publish-nuget.yml"),
        Path.Combine(".github", "workflows", "publish-nuget-org.yml"),
    ];

    /// <summary>
    /// Named exceptions that stay out of the CI release gate. Exact set: a new exclusion without
    /// a register entry must fail here, and dropping one of these without meeting its cancellation
    /// condition must fail here too.
    /// </summary>
    internal static readonly string[] NamedExceptions =
    [
        "Tempo.Blazor.E2E",
        "Tempo.ReportServer.Api.Tests.MsSql",
    ];

    [Fact]
    public void BothPublishWorkflows_UseTheSameReleaseGateFilter()
    {
        IReadOnlyList<string> filters = [.. WorkflowRelativePaths.Select(ReadFilter)];

        filters.Should().HaveCount(2);
        filters[0].Should().Be(
            filters[1],
            "publish-nuget.yml and publish-nuget-org.yml must share one filter; two different "
            + "filters make one CI permanently red and then nobody reads either");
    }

    [Fact]
    public void ReleaseGateFilter_IsExactlyTheNamedExceptions()
    {
        IReadOnlyList<string> exclusions = ParseExclusions(ReadFilter(WorkflowRelativePaths[0]));

        using (new AssertionScope())
        {
            exclusions.Except(NamedExceptions, StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Should().BeEmpty(
                    "a new FullyQualifiedName!~ clause is a silent hole in the release gate; "
                    + "name it in NamedExceptions and in the exception register, or do not add it");

            NamedExceptions.Except(exclusions, StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Should().BeEmpty(
                    "a named exception missing from the CI filter is being run as a release "
                    + "condition; drop it from NamedExceptions only when its cancellation condition fired");
        }

        exclusions.Should().NotContain(
            "Smtp4Dev",
            "the two Demo.Api smtp4dev tests are in the gate; CI starts smtp4dev as a service");
    }

    [Fact]
    public void BothPublishWorkflows_RunSmtp4DevAsAService()
    {
        foreach (string relative in WorkflowRelativePaths)
        {
            string text = ReadRepoFile(relative);
            using (new AssertionScope())
            {
                text.Should().Contain(
                    "rnwood/smtp4dev",
                    $"{relative} must start smtp4dev as a service so EmailTemplateSmtp4DevTests "
                    + "are a release condition, not a comment about a missing container");
                text.Should().Contain("2525:25", $"{relative} must publish SMTP on 2525");
                text.Should().Contain("5000:80", $"{relative} must publish smtp4dev REST on 5000");
            }
        }
    }

    /// <summary>
    /// Mutation: the comparison above is green on the healthy files. Feed it the two shapes
    /// that used to be the gate (Smtp4Dev still excluded, MsSql dropped) and both must be named.
    /// </summary>
    [Fact]
    public void TheGuard_DetectsASilentExclusionAndADroppedException()
    {
        EvaluateDrift(
                "FullyQualifiedName!~Tempo.Blazor.E2E&FullyQualifiedName!~Smtp4Dev&FullyQualifiedName!~Tempo.ReportServer.Api.Tests.MsSql")
            .Should().Contain(
                "Smtp4Dev",
                "putting Smtp4Dev back into the filter must be visible; otherwise the 2/206 hole returns");

        EvaluateDrift("FullyQualifiedName!~Tempo.Blazor.E2E")
            .Should().Contain(
                "Tempo.ReportServer.Api.Tests.MsSql",
                "dropping the MsSql exclusion without SQL Server in CI is a permanently red gate");

        EvaluateDrift("")
            .Should().NotBeEmpty("an empty filter is not 'no exceptions', it is an unreadable gate");
    }

    internal static IReadOnlyList<string> EvaluateDrift(string filter)
    {
        IReadOnlyList<string> exclusions = ParseExclusions(filter);
        List<string> drift = [];
        drift.AddRange(exclusions.Except(NamedExceptions, StringComparer.Ordinal));
        drift.AddRange(NamedExceptions.Except(exclusions, StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(filter))
        {
            drift.Add("empty-filter");
        }

        return drift;
    }

    internal static IReadOnlyList<string> ParseExclusions(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return [];
        }

        return [.. filter
            .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(clause => clause.Trim())
            .Where(clause => clause.StartsWith("FullyQualifiedName!~", StringComparison.Ordinal))
            .Select(clause => clause["FullyQualifiedName!~".Length..])];
    }

    private static string ReadFilter(string relativePath)
    {
        string text = ReadRepoFile(relativePath);
        var match = System.Text.RegularExpressions.Regex.Match(
            text,
            @"--filter\s+""(?<filter>[^""]+)""");
        match.Success.Should().BeTrue(
            $"{relativePath} must contain a --filter \"…\" on the Test step; without it this "
            + "guard cannot see the exceptions and would treat a missing gate as no exceptions");
        return match.Groups["filter"].Value;
    }

    private static string ReadRepoFile(string relativePath)
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, relativePath);
        File.Exists(path).Should().BeTrue($"release-gate workflow must exist at {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Could not locate the Tempo.Blazor repository root.");
    }
}
