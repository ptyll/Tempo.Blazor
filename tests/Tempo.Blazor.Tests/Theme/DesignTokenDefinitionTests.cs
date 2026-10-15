using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Guards the design-token contract: every <c>var(--tm-*)</c> a component reaches for must actually
/// be declared in the token files. A phantom token is invisible in code review and in bUnit tests —
/// CSS silently drops the declaration, so a border falls back to <c>currentColor</c> and a background
/// to <c>transparent</c>. That is how <c>--tm-color-error-500</c> and the whole
/// <c>--tm-color-neutral-*</c> family shipped broken.
/// </summary>
public class DesignTokenDefinitionTests
{
    private static readonly Regex TokenUsage = new(@"var\(\s*(--tm-[\w-]+)\s*(?<fallback>,)?", RegexOptions.Compiled);
    private static readonly Regex TokenDeclaration = new(@"^\s*(--tm-[\w-]+)\s*:", RegexOptions.Compiled | RegexOptions.Multiline);

    private static DirectoryInfo RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }
            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    /// <summary>Global design tokens — the themeable contract declared in the token files.</summary>
    private static HashSet<string> DeclaredTokens()
    {
        var root = RepositoryRoot().FullName;
        var declared = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in new[] { "tokens.css", "tokens-dark.css" })
        {
            var path = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css", file);
            foreach (Match match in TokenDeclaration.Matches(File.ReadAllText(path)))
            {
                declared.Add(match.Groups[1].Value);
            }
        }

        return declared;
    }

    /// <summary>
    /// Component-local custom properties (e.g. <c>--tm-timeline-dot-color</c>): a component declares
    /// them itself, or a caller sets them through an inline style. They are a per-component knob, not
    /// a global design token, so they are legitimately absent from the token files.
    /// </summary>
    private static HashSet<string> LocallyDeclaredProperties()
    {
        var root = RepositoryRoot().FullName;
        var local = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stylesheet in ComponentStylesheets())
        {
            foreach (Match match in TokenDeclaration.Matches(File.ReadAllText(stylesheet.FullName)))
            {
                local.Add(match.Groups[1].Value);
            }
        }

        var componentsDir = new DirectoryInfo(Path.Combine(root, "src", "Tempo.Blazor", "Components"));
        var inlineProperty = new Regex(@"(--tm-[\w-]+)\s*:", RegexOptions.Compiled);
        // Code-behind partials count too: TmDashboard's grid variables (--tm-w-*) are emitted
        // from the .razor.cs half of the component (F2 moved the @code there for the 1200-line
        // ceiling), and the declaration is just as real.
        foreach (var razor in componentsDir.EnumerateFiles("*.razor", SearchOption.AllDirectories))
        {
            foreach (Match match in inlineProperty.Matches(File.ReadAllText(razor.FullName)))
            {
                local.Add(match.Groups[1].Value);
            }
        }

        foreach (var codeBehind in componentsDir.EnumerateFiles("*.razor.cs", SearchOption.AllDirectories))
        {
            foreach (Match match in inlineProperty.Matches(File.ReadAllText(codeBehind.FullName)))
            {
                local.Add(match.Groups[1].Value);
            }
        }

        return local;
    }

    private static IEnumerable<FileInfo> ComponentStylesheets() =>
        new DirectoryInfo(Path.Combine(
                RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css", "components"))
            .EnumerateFiles("*.css");

    [Fact]
    public void TokenFiles_DeclareTheDocumentedFamilies()
    {
        var declared = DeclaredTokens();

        declared.Should().Contain("--tm-color-gray-500", "the neutral scale is the gray-* family");
        declared.Should().Contain("--tm-color-danger", "the error colour is danger, not error-500");
        declared.Should().Contain("--tm-border-color");
        declared.Should().Contain("--tm-text-primary");
        declared.Should().NotContain("--tm-color-neutral-500", "there is no neutral-* family — it is gray-*");
    }

    /// <summary>Every <c>var(--tm-*)</c> in a component stylesheet that resolves to nothing.</summary>
    private static List<string> PhantomTokenUsages()
    {
        var declared = DeclaredTokens();
        var local = LocallyDeclaredProperties();
        var offenders = new List<string>();

        foreach (var stylesheet in ComponentStylesheets())
        {
            var text = File.ReadAllText(stylesheet.FullName);
            foreach (Match match in TokenUsage.Matches(text))
            {
                var token = match.Groups[1].Value;
                if (declared.Contains(token) || local.Contains(token))
                {
                    continue;
                }

                offenders.Add($"{stylesheet.Name}: {token}");
            }
        }

        return offenders.Distinct(StringComparer.Ordinal).ToList();
    }

    [Fact]
    public void ComponentStylesheets_DoNotUseThePhantomColourScales()
    {
        // --tm-color-neutral-* and --tm-color-error-* were never declared anywhere. Components that
        // reached for them got currentColor / transparent instead, and no theme could reach them.
        var offenders = PhantomTokenUsages()
            .Where(o => o.Contains("--tm-color-neutral-", StringComparison.Ordinal)
                     || o.Contains("--tm-color-error-", StringComparison.Ordinal))
            .ToList();

        offenders.Should().BeEmpty(
            "the neutral/error scales do not exist — use the gray scale and the semantic tokens (--tm-border-color, "
            + "--tm-text-*, --tm-bg-*, --tm-color-danger)");
    }

    [Fact]
    public void ComponentStylesheets_HaveNoUndeclaredTokenDebt()
    {
        // The alias debt (--tm-spacing-* vs --tm-space-*, --tm-bg-hover, --tm-color-danger-600,
        // the rich-editor colour ramps, …) has been retired: every component token now resolves.
        var offenders = PhantomTokenUsages();

        offenders.Should().BeEmpty(
            "every component must style itself with tokens declared in tokens.css/tokens-dark.css "
            + "(or a component-local custom property)");
    }
}
