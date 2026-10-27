using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// CSP guard: the Modeling package must never reach JavaScript through
/// <c>InvokeVoidAsync("eval", …)</c> / <c>InvokeAsync("eval", …)</c>. Evaluated strings break any
/// host that ships a Content-Security-Policy without <c>unsafe-eval</c>, and the package instead
/// talks to the ES module <c>_content/Tempo.Blazor.Modeling/js/modeling-editor.js</c> imported via
/// <c>IJSObjectReference</c> (plus plain <c>ElementReference.FocusAsync()</c> where Blazor alone
/// suffices).
/// </summary>
public sealed class ModelingNoEvalInteropTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex EvalInterop =
        new(
            @"(InvokeVoidAsync|InvokeAsync)\s*(?:<[^(]*>)?\s*\(\s*""eval""",
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
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
        || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
