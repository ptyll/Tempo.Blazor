using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Token gate for the Tempo.Blazor.PdfViewer package (PdfViewer redesign, phase 2): every scoped
/// stylesheet reads the core semantic tokens, so the palette, the indigo theme and dark mode
/// propagate into the PDF components. The package historically invented undefined aliases with
/// hex fallbacks (<c>--tm-primary</c>, <c>--tm-border</c>, <c>--tm-surface-elevated</c>,
/// <c>--tm-*-dark</c>…), which pinned every colour to the fallback and silently ignored every
/// theme — the fallback of an undefined <c>var()</c> is what renders.
/// </summary>
/// <remarks>
/// Mirrors the strict audit (<c>scripts/audit-css-strict.mjs</c>) for this package but asserts the
/// ZERO state directly: no undefined alias, no colour literal outside a data-palette definition,
/// no manual dark block. The package baseline in <c>scripts/css-token-baseline.json</c> must stay
/// at 0 for these files.
/// </remarks>
public sealed class PdfViewerCssTokenTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex TokenUsage =
        new(@"var\(\s*(--tm-[a-zA-Z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TokenDefinition =
        new(@"(--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Same literal detector as the strict audit, including the named colours.</summary>
    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])",
            RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// A custom-property definition that is a data palette entry — a literal there is a data
    /// value (e.g. the annotation colour palette, the redaction cover), not drift. Mirrors the
    /// strict audit's exemption.
    /// </summary>
    private static readonly Regex DataPaletteDefinition =
        new(@"^\s*--tm-[a-z0-9-]+-(palette|option|role|annotation|category)-[a-z0-9-]+\s*:",
            RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>The aliases the analyses kept inventing for tokens that already exist (docs/css-token-aliases.md).</summary>
    private static readonly string[] UndefinedAliases =
    [
        "--tm-primary",
        "--tm-border",
        "--tm-surface-elevated",
        "--tm-hover",
        "--tm-text",
        "--tm-on-primary",
        "--tm-radius",
        "--tm-warning",
        "--tm-bg-subtle",
    ];

    [Fact]
    public void PackageCss_HasNoUndefinedTokenAliases()
    {
        var defined = CoreTokenDefinitions();
        defined.UnionWith(PackageCss()
            .SelectMany(pair => TokenDefinition.Matches(Strip(pair.Css)).Select(match => match.Groups[1].Value))
            .ToHashSet());

        var offences = new List<string>();
        foreach (var (file, css) in PackageCss())
        {
            foreach (Match usage in TokenUsage.Matches(Strip(css)))
            {
                var token = usage.Groups[1].Value;
                if (UndefinedAliases.Contains(token))
                {
                    offences.Add($"{file}: banned alias {token}");
                }
                else if (!defined.Contains(token))
                {
                    offences.Add($"{file}: undefined token {token}");
                }
            }
        }

        offences.Should().BeEmpty(
            "every PdfViewer var(--tm-*) must resolve to a core token (tokens.css / tokens-dark.css "
            + "/ theme-*.css) or to a local token defined on the component root — an undefined alias "
            + "renders its hex fallback forever and ignores the palette, the indigo theme and dark "
            + "mode. Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void PackageCss_HasNoHexOutsideAnnotationPalette()
    {
        var offences = new List<string>();
        foreach (var (file, css) in PackageCss())
        {
            var lines = Strip(css).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (ColorLiteral.IsMatch(lines[index]) && !DataPaletteDefinition.IsMatch(lines[index]))
                {
                    offences.Add($"{file}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        offences.Should().BeEmpty(
            "PdfViewer stylesheets paint with tokens only; the single exception is a data-palette "
            + "custom-property definition (annotation colours, the redaction cover), which is data. "
            + "Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void PackageCss_HasNoManualDarkThemeBlocks()
    {
        var offences = new List<string>();
        foreach (var (file, css) in PackageCss())
        {
            foreach (Match match in ManualDarkBlock.Matches(Strip(css)))
            {
                offences.Add($"{file}: {match.Value}");
            }
        }

        offences.Should().BeEmpty(
            "dark mode is tokens-dark.css's job — a [data-theme=dark]/.tm-dark block in a component "
            + "stylesheet duplicates the theme and drifts. Offences:\n" + string.Join("\n", offences));
    }

    private static string Strip(string css) => Comment.Replace(css, " ");

    /// <summary>Every scoped stylesheet of the package, keyed by repo-relative path.</summary>
    private static IReadOnlyList<(string File, string Css)> PackageCss()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var directory = Path.Combine(root, "src", "Tempo.Blazor.PdfViewer");
        Directory.Exists(directory).Should().BeTrue("the PdfViewer package must exist");

        return Directory
            .EnumerateFiles(directory, "*.razor.css", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
    }

    /// <summary>
    /// Tokens the core defines: every non-bundled stylesheet under the core css root (the bundle
    /// is generated and counts each definition twice). Runtime <c>setProperty</c> definitions are
    /// irrelevant here — the package sets no tokens from script.
    /// </summary>
    private static HashSet<string> CoreTokenDefinitions()
    {
        var root = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css");
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".bundled.css", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in TokenDefinition.Matches(Strip(File.ReadAllText(file))))
            {
                defined.Add(match.Groups[1].Value);
            }
        }

        return defined;
    }
}
