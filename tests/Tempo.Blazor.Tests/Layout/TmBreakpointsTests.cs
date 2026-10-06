using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The responsive convention is a documented set of literal widths. CSS custom properties cannot
/// appear in an <c>@media</c> or <c>@container</c> condition, so the literals and
/// <see cref="TmBreakpoints"/> must be proven to be the same numbers.
/// </summary>
public class TmBreakpointsTests
{
    private static readonly int[] Allowed = [640, 768, 1024, 1280];

    public static TheoryData<string> ConventionFiles => new()
    {
        Path.Combine("src", "Tempo.Blazor", "wwwroot", "css", "breakpoints.css"),
        Path.Combine("src", "Tempo.Blazor", "wwwroot", "css", "components", "_dashboard.css"),
    };

    [Fact]
    public void Constants_AreTheDocumentedSet()
    {
        TmBreakpoints.Sm.Should().Be(640);
        TmBreakpoints.Md.Should().Be(768);
        TmBreakpoints.Lg.Should().Be(1024);
        TmBreakpoints.Xl.Should().Be(1280);
        TmBreakpoints.All.Should().Equal(Allowed);
    }

    [Theory]
    [MemberData(nameof(ConventionFiles))]
    public void CssLiteralsMatchConstants(string relativePath)
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));

        var literals = MediaAndContainerWidths(css);

        literals.Should().NotBeEmpty($"{relativePath} must declare at least one @media or @container width");
        literals.Should().OnlyContain(
            width => Allowed.Contains(width),
            $"{relativePath} uses a width outside TmBreakpoints ({string.Join(", ", Allowed)})");
    }

    [Fact]
    public void BreakpointsCss_DeclaresEveryConstantAsALiteral()
    {
        var css = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "Tempo.Blazor", "wwwroot", "css", "breakpoints.css"));

        var literals = MediaAndContainerWidths(css);

        literals.Should().Contain(Allowed);
    }

    /// <summary>Widths that actually decide a query: the condition of an <c>@media</c> or <c>@container</c> rule.</summary>
    internal static IReadOnlyList<int> MediaAndContainerWidths(string css)
    {
        var withoutComments = Regex.Replace(css, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        var conditions = Regex.Matches(
            withoutComments,
            @"@(?:media|container)\b(?<condition>[^{]*)\{",
            RegexOptions.IgnoreCase);

        return conditions
            .SelectMany(match => WidthLiterals(match.Groups["condition"].Value))
            .ToList();
    }

    private static IEnumerable<int> WidthLiterals(string condition) =>
        Regex.Matches(condition, @"\b(?<value>\d+(?:\.\d+)?)px\b", RegexOptions.IgnoreCase)
            .Select(match => (int)Math.Round(double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture)));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the repository root should be discoverable from the test output directory");
        return directory!.FullName;
    }
}
