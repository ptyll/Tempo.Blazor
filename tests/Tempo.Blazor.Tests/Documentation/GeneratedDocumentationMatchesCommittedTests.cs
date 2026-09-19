using FluentAssertions;
using JsonDocumentationGenerator;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Whole-file freshness guard for the generated <c>tempo-*.json</c> bundles committed at the
/// repository root.
/// <para>
/// <see cref="ComponentDocumentationFreshnessTests"/> compares the committed documentation
/// against the reflected API — it answers "is the parameter surface documented". This guard asks
/// the stronger question: does re-running the generator produce EXACTLY the text that is
/// committed? A source change that keeps the parameter set identical — a renamed description,
/// a moved file, a changed csproj property — still drifts the bundle, and until now nothing
/// noticed. Regeneration is a manual step; the guard makes the drift a failing test instead of a
/// stale release artifact.
/// </para>
/// <para>
/// The generator runs into a temporary output directory and every produced file is compared
/// against the committed counterpart. Line endings are normalised before the comparison so a
/// checkout with <c>core.autocrlf</c> cannot fail on EOL alone — but every CONTENT difference
/// fails, and the failure message names the first differing line. Nothing here may be skipped:
/// a generator that exits non-zero, throws, or produces a different file set is a failure, not
/// a "not applicable".
/// </para>
/// </summary>
public sealed class GeneratedDocumentationMatchesCommittedTests
{
    private static string RepoRoot => ThemeCss.RepositoryRoot().FullName;

    private static string JsonDocumentationDir => Path.Combine(RepoRoot, "JsonDocumentation");

    [Fact]
    public void GeneratedDocumentation_MatchesCommitted()
    {
        var output = Path.Combine(Path.GetTempPath(), $"tempo-docgen-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            var exit = PackageDocumentationGenerator.Run(
                [JsonDocumentationDir, "generate", "--output-dir", output]);
            exit.Should().Be(0,
                "a failed generation produces no output to compare — that is a broken pipeline, " +
                "not a skipped test");

            var committed = Directory.GetFiles(RepoRoot, "tempo-*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            committed.Should().NotBeEmpty(
                "the repository ships tempo-*.json bundles at its root — an empty set means the " +
                "guard is comparing nothing, which is exactly the silent hole it exists to close");

            var generated = Directory.GetFiles(output, "tempo-*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            generated.Should().BeEquivalentTo(committed,
                "the generator must emit exactly the committed file set — a missing bundle is a " +
                "dropped package, an extra one is output the repository does not track");

            foreach (var name in committed)
            {
                var expected = File.ReadAllText(Path.Combine(RepoRoot, name!))
                    .Replace("\r\n", "\n", StringComparison.Ordinal);
                var actual = File.ReadAllText(Path.Combine(output, name!))
                    .Replace("\r\n", "\n", StringComparison.Ordinal);
                if (!string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    var (line, expectedLine, actualLine) = FirstDifference(expected, actual);
                    actual.Should().Be(expected,
                        $"{name} is stale — first difference at line {line}: committed " +
                        $"'{expectedLine}' vs generated '{actualLine}'. Regenerate the " +
                        "JsonDocumentation bundles and commit them together with the source change.");
                }
            }
        }
        finally
        {
            try
            {
                Directory.Delete(output, recursive: true);
            }
            catch (IOException)
            {
                // A temp dir that will not delete is not a test outcome — the comparison already
                // ran and said what it had to say.
            }
        }
    }

    [Fact]
    public void A_Missing_Configured_Component_Root_Fails_Loudly()
    {
        // The generator used to answer a moved source tree with "zero components" and a clean
        // exit — a documentation set silently stripped of its package surface. The guard proves
        // the new contract: a configured input that is absent throws, naming what is missing.
        var fake = Path.Combine(Path.GetTempPath(), $"tempo-docgen-missing-{Guid.NewGuid():N}");
        var fakeJsonDoc = Path.Combine(fake, "JsonDocumentation");
        try
        {
            Directory.CreateDirectory(fakeJsonDoc);
            File.Copy(Path.Combine(JsonDocumentationDir, "packages.json"),
                Path.Combine(fakeJsonDoc, "packages.json"));
            File.Copy(Path.Combine(JsonDocumentationDir, "gettingStarted.json"),
                Path.Combine(fakeJsonDoc, "gettingStarted.json"));
            File.Copy(Path.Combine(JsonDocumentationDir, "libraryExamples.json"),
                Path.Combine(fakeJsonDoc, "libraryExamples.json"));

            // Minimal scaffolding so Compose reaches the component-root check: a parseable source
            // project and the documentation root the first configured package points at.
            var fakeSrc = Path.Combine(fake, "src", "Tempo.Blazor");
            Directory.CreateDirectory(fakeSrc);
            File.WriteAllText(Path.Combine(fakeSrc, "Tempo.Blazor.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Directory.CreateDirectory(Path.Combine(fakeJsonDoc, "Components"));

            var act = () => PackageDocumentationGenerator.Run(
                [fakeJsonDoc, "generate", "--output-dir", Path.Combine(fake, "out")]);

            act.Should().Throw<DirectoryNotFoundException>()
                .WithMessage("*component root*",
                    "a configured component root that does not exist must be reported by name — " +
                    "silently emitting an empty package document is the failure this closes");
        }
        finally
        {
            try
            {
                Directory.Delete(fake, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static (int Line, string Expected, string Actual) FirstDifference(string expected, string actual)
    {
        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');
        for (var i = 0; i < Math.Max(expectedLines.Length, actualLines.Length); i++)
        {
            var e = i < expectedLines.Length ? expectedLines[i] : "<missing>";
            var a = i < actualLines.Length ? actualLines[i] : "<missing>";
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                return (i + 1, Truncate(e), Truncate(a));
            }
        }

        return (0, string.Empty, string.Empty);
    }

    private static string Truncate(string line) =>
        line.Length <= 120 ? line : line[..117] + "...";
}
