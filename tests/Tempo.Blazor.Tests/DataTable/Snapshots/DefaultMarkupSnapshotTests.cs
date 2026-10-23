using System.Text;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable.Snapshots;

/// <summary>
/// Compares the current default markup of the data-table family with the markup captured
/// from commit 259f900a (the state before the redesign).
/// </summary>
/// <remarks>
/// The comparison target is <c>golden/&lt;scenario&gt;.approved.html</c> when that file exists,
/// otherwise <c>golden/&lt;scenario&gt;.html</c>. Every edit of an <c>.approved.html</c> file MUST
/// have a matching entry in the CHANGELOG.md "Breaking / Migration" section, marked with
/// <c>&lt;!-- snapshot:&lt;scenario&gt; --&gt;</c>. An approved file may contain only the a11y
/// deviations listed in user decision 1 of the review-fix plan; visual and copy changes belong
/// back in the default markup, not in the approved overlay.
/// </remarks>
public class DefaultMarkupSnapshotTests : LocalizationTestBase
{
    public static IEnumerable<object[]> ScenarioNames()
        => DefaultMarkupScenarios.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void DefaultMarkup_MatchesGoldenSnapshot(string scenario)
    {
        var actual = MarkupSnapshot.Normalize(DefaultMarkupScenarios.Render(this, scenario));
        var expected = MarkupSnapshot.NormalizeVoidElements(File.ReadAllText(ExpectedPath(scenario)));

        if (actual == expected)
        {
            return;
        }

        var diff = UnifiedDiff(expected, actual);
        actual.Should().Be(expected, "default markup of {0} drifted from the pre-redesign snapshot:\n{1}", scenario, diff);
    }

    /// <summary>
    /// Every approved overlay must be traceable to a CHANGELOG Breaking/Migration entry.
    /// The marker itself is inserted next to that entry in phase 3, so this stays red until then
    /// for any <c>.approved.html</c> that exists.
    /// </summary>
    [Fact]
    public void ApprovedSnapshots_AreListedInChangelog()
    {
        var goldenDir = GoldenDirectory();
        var approved = Directory.Exists(goldenDir)
            ? Directory.GetFiles(goldenDir, "*.approved.html")
            : [];

        approved.Should().NotBeEmpty("decision 1 keeps a11y deviations as an approved overlay");

        var changelog = File.ReadAllText(Path.Combine(RepoRoot(), "CHANGELOG.md"));
        var missing = approved
            .Select(path => Path.GetFileName(path)[..^".approved.html".Length])
            .Where(scenario => !changelog.Contains($"<!-- snapshot:{scenario} -->", StringComparison.Ordinal))
            .ToArray();

        missing.Should().BeEmpty(
            "each .approved.html edit must be recorded in CHANGELOG Breaking/Migration with <!-- snapshot:<scenario> -->");
    }

    private static string ExpectedPath(string scenario)
    {
        var approved = Path.Combine(GoldenDirectory(), scenario + ".approved.html");
        return File.Exists(approved)
            ? approved
            : Path.Combine(GoldenDirectory(), scenario + ".html");
    }

    private static string GoldenDirectory()
        => Path.Combine(
            RepoRoot(),
            "tests", "Tempo.Blazor.Tests", "DataTable", "Snapshots", "golden");

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }

    /// <summary>First 40 lines of a unified diff, so a red run is readable in the test output.</summary>
    private static string UnifiedDiff(string expected, string actual)
    {
        var expectedLines = expected.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var actualLines = actual.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var builder = new StringBuilder();
        builder.AppendLine("--- golden");
        builder.AppendLine("+++ actual");

        var limit = Math.Max(expectedLines.Length, actualLines.Length);
        var emitted = 0;
        for (var i = 0; i < limit && emitted < 40; i++)
        {
            var left = i < expectedLines.Length ? expectedLines[i] : null;
            var right = i < actualLines.Length ? actualLines[i] : null;
            if (left == right)
            {
                continue;
            }

            if (left is not null)
            {
                builder.Append("- ").AppendLine(Truncate(left));
                emitted++;
            }

            if (right is not null && emitted < 40)
            {
                builder.Append("+ ").AppendLine(Truncate(right));
                emitted++;
            }
        }

        return builder.ToString();
    }

    private static string Truncate(string line)
        => line.Length <= 240 ? line : line[..240] + "…";
}
