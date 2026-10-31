using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.AI.Markdown;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Token gate for <c>Tempo.Blazor.AI</c> (plan 7f21f26b, phase 2): every rule of the package stylesheet reads
/// a declared core token, no colour literal sneaks in, no component-owned dark block appears, and every
/// <c>tm-markdown-view*</c> class the renderer actually emits has a rule. The package is a split package
/// with its own entry stylesheet, so the core sweeps (which only read <c>src/Tempo.Blazor</c>) do not cover
/// it.
/// </summary>
/// <remarks>
/// Mirrors <c>PdfViewerCssTokenTests</c> and the strict audit (<c>scripts/audit-css-strict.mjs</c>) but asserts
/// the ZERO state directly: the package starts clean, so its baseline in
/// <c>scripts/css-token-baseline.json</c> never has to grow.
/// </remarks>
public sealed class AiMarkdownViewCssTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex Comment = new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    private static readonly Regex TokenUsage = new(@"var\(\s*(--tm-[a-zA-Z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TokenDefinition = new(@"(--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(|(?<![\w-])(?:white|black)(?![\w-])", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// One step of a colour palette — the light-ramp literals the semantic tokens are built from. A
    /// component or package stylesheet reads the semantic token, never the step.
    /// </summary>
    private static readonly Regex RawPaletteStep = new(
        @"--tm-color-(?:primary|secondary|gray|danger|success|warning|info)-\d{2,3}\b",
        RegexOptions.Compiled,
        RegexTimeout);

    private static readonly Regex CssClassSelector = new(@"\.(tm-[a-zA-Z][\w-]*)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex HtmlClassAttribute = new(@"class\s*=\s*[""']([^""']*)[""']", RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Core-owned utility classes the renderer reuses instead of re-declaring. <c>tm-sr-only</c> is the
    /// package's only one: the visually-hidden helper is styled once by the core stylesheet (which every Tempo
    /// host loads), so a package copy would be a second, drifting definition.
    /// </summary>
    private static readonly string[] CoreOwnedClasses = ["tm-sr-only"];

    /// <summary>
    /// RUNTIME knobs — custom properties a package module writes into an element's inline style and the
    /// stylesheet only READS through a <c>var()</c> fallback. They are deliberately never declared by CSS: a
    /// declared value would outrank a host override, and the whole point of a runtime knob is that the module
    /// (the virtual-keyboard inset) or the caret measurement owns the value. They are allowlisted here so the
    /// alias sweep keeps finding real typos, and <see cref="RuntimeKnobs_AreDeclaredByNoStylesheet"/> pins that
    /// they stay undeclared.
    /// </summary>
    private static readonly string[] RuntimeKnobs =
    [
        "--tm-ai-composer-keyboard-inset",
        "--tm-ai-composer-caret-inline-start",
        "--tm-ai-composer-caret-block-end",
        "--tm-ai-composer-list-max-block-size",
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
                if (!defined.Contains(token) && !RuntimeKnobs.Contains(token, StringComparer.Ordinal))
                {
                    offences.Add($"{file}: undefined token {token}");
                }
            }
        }

        offences.Should().BeEmpty(
            "every AI package var(--tm-*) must resolve to a core token or to a package-local definition — an "
            + "undefined alias paints nothing and ignores light/dark and a host palette. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void PackageCss_PaintsWithTokensOnly()
    {
        var offences = new List<string>();
        foreach (var (file, css) in PackageCss())
        {
            var lines = Strip(css).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (ColorLiteral.IsMatch(lines[index]))
                {
                    offences.Add($"{file}:{index + 1}: {lines[index].Trim()}");
                }
            }
        }

        offences.Should().BeEmpty(
            "AI package stylesheets paint with --tm-* tokens only; a colour literal pins one theme forever. "
            + "Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void RuntimeKnobs_AreDeclaredByNoStylesheet()
    {
        var offences = PackageCss()
            .SelectMany(pair => TokenDefinition.Matches(Strip(pair.Css))
                .Select(match => match.Groups[1].Value)
                .Where(token => RuntimeKnobs.Contains(token, StringComparer.Ordinal))
                .Select(token => $"{pair.File}: declares the runtime knob {token}"))
            .ToList();

        offences.Should().BeEmpty(
            "a runtime knob is written only by the package module (and therefore must keep a var() fallback): "
            + "declaring it in CSS outranks a host override and turns the runtime value into a fixed default. "
            + "Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void PackageCss_HasNoManualDarkThemeBlocks()
    {
        var offences = PackageCss()
            .SelectMany(pair => ManualDarkBlock.Matches(Strip(pair.Css)).Select(match => $"{pair.File}: {match.Value}"))
            .ToList();

        offences.Should().BeEmpty(
            "dark mode is tokens-dark.css's job. Offences:\n" + string.Join("\n", offences));
    }

    /// <summary>
    /// A raw palette step (<c>--tm-color-primary-50</c>, <c>--tm-color-gray-200</c>, …) is ONE colour
    /// of one palette: <c>tokens-dark.css</c> re-declares the semantic tokens, not every step, so a
    /// surface or ink painted with a raw step keeps its light value in the dark theme. The citation
    /// chip did exactly that on hover — <c>--tm-color-primary-50</c> stayed a near-white pill under
    /// the darkened <c>--tm-color-primary-text</c> and the label fell under AA. Semantic tokens only:
    /// <c>--tm-color-primary-subtle</c>, <c>--tm-color-primary</c>, <c>--tm-border-color-focus</c>,
    /// <c>--tm-color-text*</c>, <c>--tm-bg-*</c>. A colour that has no semantic role yet gets one in
    /// <c>tokens.css</c> + <c>tokens-dark.css</c>, not a palette step here.
    /// </summary>
    [Fact]
    public void PackageCss_UsesNoRawPaletteSteps()
    {
        var offences = PackageCss()
            .SelectMany(pair => RawPaletteStep.Matches(Strip(pair.Css))
                .Select(match => $"{pair.File}: {match.Value}"))
            .ToList();

        offences.Should().BeEmpty(
            "an AI package stylesheet must paint with semantic tokens; a raw palette step has no dark "
            + "counterpart, so it pins one theme and is exactly how the citation-chip hover lost its "
            + "contrast. Offences:\n" + string.Join("\n", offences));
    }

    [Fact]
    public void EveryClassTheRendererEmits_HasARule()
    {
        var renderer = new AiMarkdownRenderer();
        var html = renderer.Render(
            "# Title\n\n| a | b |\n| --- | --- |\n| 1 | 2 |\n\n```cs\nvar x = 1;\n```\n\nAnswer [1](cite:s).\n\n- [ ] task",
            new AiMarkdownRenderOptions
            {
                CopyCodeLabel = "Copy code",
                CopyCodeWithLanguageLabel = "Copy {0} code",
                CopiedLabel = "Copied",
                CitationLabelFormat = "Source {0}",
            });

        var emitted = HtmlClassAttribute.Matches(html)
            .SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(name => name.StartsWith("tm-", StringComparison.Ordinal))
            .Where(name => !CoreOwnedClasses.Contains(name, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var styled = PackageCss()
            .SelectMany(pair => CssClassSelector.Matches(Strip(pair.Css)).Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        emitted.Should().NotBeEmpty("the renderer emits the package's own classes");
        emitted.Should().BeSubsetOf(styled,
            "a class the markdown renderer emits without a rule ships unstyled. Missing: {0}",
            string.Join(", ", emitted.Except(styled)));
    }

    private static string Strip(string css) => Comment.Replace(css, " ");

    /// <summary>Every stylesheet of the package, keyed by repo-relative path.</summary>
    private static IReadOnlyList<(string File, string Css)> PackageCss()
    {
        var root = ThemeCss.RepositoryRoot().FullName;
        var directory = Path.Combine(root, "src", "Tempo.Blazor.AI", "wwwroot", "css");
        Directory.Exists(directory).Should().BeTrue("the AI package must publish its own stylesheet");

        return Directory
            .EnumerateFiles(directory, "*.css", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
            .ToList();
    }

    /// <summary>Every token the core declares (the generated bundle is skipped — it would double-count).</summary>
    private static HashSet<string> CoreTokenDefinitions()
    {
        var root = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css");
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
