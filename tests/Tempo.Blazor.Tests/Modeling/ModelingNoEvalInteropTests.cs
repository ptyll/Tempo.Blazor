using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// CSP guard: the Modeling package must never reach JavaScript through
/// <c>InvokeVoidAsync("eval", …)</c> / <c>InvokeAsync("eval", …)</c>, and its shipped
/// <c>wwwroot/**/*.js</c> must not contain string-compiled code either. Evaluated strings break
/// any host that ships a Content-Security-Policy without <c>unsafe-eval</c>, and the package
/// instead talks to the ES module <c>_content/Tempo.Blazor.Modeling/js/modeling-editor.js</c>
/// imported via <c>IJSObjectReference</c> (plus plain <c>ElementReference.FocusAsync()</c> where
/// Blazor alone suffices).
/// </summary>
public sealed class ModelingNoEvalInteropTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex EvalInterop =
        new(
            @"(InvokeVoidAsync|InvokeAsync)\s*(?:<[^(]*>)?\s*\(\s*""eval""",
            RegexOptions.Compiled,
            RegexTimeout);

    // String-compiled / timer-as-code patterns in shipped JS: eval(, new Function("…"), and a
    // setTimeout/setInterval whose first argument is a STRING literal (implicit eval, still
    // refused by a CSP without unsafe-eval). A leading \b keeps `\beval` from matching
    // identifiers ending in "eval" (e.g. `revalidate(`).
    private static readonly Regex EvalInJs =
        new(
            @"\beval\s*\(|new\s+Function\s*\(|set(?:Timeout|Interval)\s*\(\s*['""`]",
            RegexOptions.Compiled,
            RegexTimeout);

    [Fact]
    public void Modeling_package_does_not_use_eval_interop()
    {
        var root = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Modeling");
        Directory.Exists(root).Should().BeTrue($"the Modeling package is expected at {root}");

        var offenders = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".razor", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .SelectMany(path => EvalInterop.Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(root, path)}:{match.Index}"))
            .ToList();

        offenders.Should().BeEmpty(
            "eval-based JS interop breaks hosts with a CSP that does not allow unsafe-eval; "
            + "use the modeling-editor.js ES module or ElementReference.FocusAsync() instead. Offenders: "
            + string.Join(", ", offenders));

        // The JS side of the same contract: the shipped wwwroot scripts must not string-compile
        // code either (eval, new Function, string-argument timers). Without this scan the guard
        // only covered the C# call sites, and a hand-edited wwwroot/*.js could reintroduce
        // unsafe-eval code unnoticed.
        var jsFiles = Directory
            .EnumerateFiles(Path.Combine(root, "wwwroot"), "*.js", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();
        jsFiles.Should().NotBeEmpty("the Modeling package is expected to ship wwwroot JS assets");

        var jsOffenders = jsFiles
            .SelectMany(path => EvalInJs.Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(root, path)}:{match.Index} ({match.Value})"))
            .ToList();

        jsOffenders.Should().BeEmpty(
            "string-compiled JS (eval / new Function / string setTimeout) is refused by a CSP "
            + "without unsafe-eval just like InvokeVoidAsync(\"eval\") — keep everything in the "
            + "modeling-editor.js ES module. Offenders: " + string.Join(", ", jsOffenders));
    }

    [Fact]
    public void The_scan_really_covers_both_sides_of_the_boundary()
    {
        // The two assertions above only mean anything if their file filters still match: a
        // renamed folder or a changed extension would silently scan NOTHING. Assert the
        // surfaces the guard claims to protect still exist.
        var root = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Modeling");

        var razorCs = Directory
            .EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".razor.cs", StringComparison.Ordinal))
            .Where(path => !IsBuildOutput(path))
            .ToList();
        razorCs.Should().NotBeEmpty("expected at least one .razor.cs under src/Tempo.Blazor.Modeling");

        var js = Directory
            .EnumerateFiles(Path.Combine(root, "wwwroot"), "*.js", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();
        js.Should().NotBeEmpty("expected at least one .js under src/Tempo.Blazor.Modeling/wwwroot");
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
