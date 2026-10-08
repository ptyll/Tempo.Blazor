using FluentAssertions;
using FluentAssertions.Execution;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// CF19f — source-level pin for the SQL Server fixture's CREATE DATABASE denied detection.
/// <c>tests/Tempo.ReportServer.Web.Tests/SqlServerCacheFixture.cs</c> used to match
/// <c>ex.Message.Contains("permission") || ex.Message.Contains("denied")</c> — a substring pair
/// broad enough to mislabel any transient failure whose message happens to carry either word
/// (e.g. "permission to access the endpoint was denied" from a proxy, or a German locale where
/// the phrase differs entirely) as a missing CREATE DATABASE grant. The fixture now delegates
/// to the same canonical shape <c>MsSqlTestDatabase.IsCreateDatabaseDenied</c> uses: error
/// number 262 (locale-independent, including inside multi-error batches) plus the canonical
/// English phrase. This test reads the fixture source — a SqlException cannot be constructed
/// with a chosen <c>Number</c>, so behavioural coverage is impossible and the source pin is the
/// honest guard.
/// </summary>
public sealed class SqlFixtureSourceTests
{
    private static string FixturePath => Path.Combine(
        ReleaseScriptInputReadTests.FindRepoRoot(),
        "tests", "Tempo.ReportServer.Web.Tests", "SqlServerCacheFixture.cs");

    [Fact]
    public void SqlServerCacheFixture_UsesCanonicalCreateDatabaseDeniedMatch()
    {
        string source = File.ReadAllText(FixturePath);

        using (new AssertionScope())
        {
            source.Should().Contain(
                "IsCreateDatabaseDenied",
                "the fixture must delegate to a named canonical predicate, not an inline "
                + "substring guess");
            source.Should().Contain(
                "error.Number == 262",
                "error 262 is CREATE DATABASE permission denied — matching by number is "
                + "locale-independent and covers errors inside a batch");
            source.Should().Contain(
                "CREATE DATABASE permission denied",
                "the canonical English phrase stays for drivers that only surface text");
            source.Should().NotContain(
                "ex.Message.Contains(\"permission\"",
                "the broad substring match mislabels unrelated transient failures as a "
                + "missing CREATE DATABASE grant (CF19f)");
        }
    }
}
