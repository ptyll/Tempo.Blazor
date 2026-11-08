using System.Text.RegularExpressions;
using FluentAssertions;

namespace Tempo.Blazor.Tests.Theme;

/// <summary>
/// Token gate for the NotionEditor package (redesign Fáze 2). Every scoped sheet and every
/// <c>_notion-*.css</c> reads a core token or a <c>--tm-notion-*</c> knob the package declares.
/// A colour literal is allowed only as a data-palette entry
/// (<c>--tm-notion-option-*</c> / <c>--tm-notion-option-text-*</c>) in the two palette files.
/// </summary>
public sealed class NotionCssTokenTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex TokenUsage =
        new(@"var\(\s*(--tm-[a-zA-Z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex TokenDefinition =
        new(@"(--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ColorLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ManualDarkBlock =
        new(@"\[data-theme\s*=\s*[""']dark[""']\]|\.tm-dark\b", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CssComment =
        new(@"/\*.*?\*/", RegexOptions.Compiled | RegexOptions.Singleline, RegexTimeout);

    // An HTML entity (&#115;) is not a colour, so a '#' that opens one is skipped.
    private static readonly Regex HexInCSharp =
        new(@"(?<!&)#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled, RegexTimeout);

    /// <summary>A data-palette definition. Same shape the strict audit exempts, plus the colour family.</summary>
    private static readonly Regex DataPaletteDefinition =
        new(@"^\s*--tm-notion-(?:option|color)-[a-z0-9-]+\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly string[] PaletteFiles = ["_notion-variables.css", "_notion-dark.css"];

    /// <summary>
    /// A hex in C# is a pinned colour. Three files may keep one, each for a stated reason:
    /// the page-header cover gradients (a stored image, not a theme colour), the standalone HTML
    /// export (no design tokens in a downloaded file) and <c>NotionColorTokens</c> (the one
    /// allowlisted legacy pair <c>execCommand</c> can keep).
    /// </summary>
    private static readonly string[] HexAllowlist =
    [
        "Components/NotionEditor/Page/TmNotionPageHeader.razor.cs",
        "Components/NotionEditor/Services/NotionHtmlExporter.cs",
        "Components/NotionEditor/Services/NotionColorTokens.cs",
    ];

    [Fact]
    public void AllTmVariables_AreDefined_EvenWithFallback()
    {
        var defined = CoreDefinitions();
        defined.UnionWith(PackageFiles().SelectMany(file =>
            TokenDefinition.Matches(Strip(file.Css)).Select(match => match.Groups[1].Value)));

        var offences = PackageFiles()
            .SelectMany(file => TokenUsage.Matches(Strip(file.Css))
                .Select(match => match.Groups[1].Value)
                .Where(token => !defined.Contains(token))
                .Select(token => $"{file.Name}: undefined token {token}"))
            .Distinct()
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToList();

        offences.Should().BeEmpty(
            "a var(--tm-*) with a fallback still renders the fallback forever when the token is undefined. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void NoHexOrRgbaOutsideAllowlist()
    {
        var offences = new List<string>();
        foreach (var file in PackageFiles())
        {
            var lines = Strip(file.Css).Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                if (!ColorLiteral.IsMatch(lines[index]))
                {
                    continue;
                }

                if (PaletteFiles.Contains(file.Name, StringComparer.Ordinal)
                    && DataPaletteDefinition.IsMatch(lines[index]))
                {
                    continue;
                }

                offences.Add($"{file.Name}:{index + 1}: {lines[index].Trim()}");
            }
        }

        offences.Should().BeEmpty(
            "a colour literal outside the data palettes pins the package off the theme. Offences:\n"
            + string.Join("\n", offences));
    }

    /// <summary>
    /// <c>--tm-bg-overlay</c> is the modal scrim. A wash, chip or hover that borrowed it
    /// paints a page dimmer instead of the semantic colour.
    /// </summary>
    [Fact]
    public void BgOverlay_OnlyOnBackdropScrimOrOverlay()
    {
        var selector = new Regex(@"backdrop|scrim|overlay", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        var offences = new List<string>();
        foreach (var file in PackageFiles())
        {
            foreach (Match rule in Regex.Matches(Strip(file.Css), @"([^{}]+)\{([^{}]*)\}", RegexOptions.None, RegexTimeout))
            {
                if (!rule.Groups[2].Value.Contains("--tm-bg-overlay", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!selector.IsMatch(rule.Groups[1].Value))
                {
                    offences.Add($"{file.Name}: {rule.Groups[1].Value.Trim()}");
                }
            }
        }

        offences.Should().BeEmpty(
            "--tm-bg-overlay is the modal scrim and belongs only on backdrop, scrim or overlay selectors. Offences:\n"
            + string.Join("\n", offences));
    }

    /// <summary>
    /// A floating panel surface (turn-into, inline toolbar, menus) rendered with the constant
    /// <c>--tm-color-white</c> keeps the light background in dark mode while its text switches,
    /// making items unreadable (F-02). A <c>color-mix</c> wash is fine; a plain surface fill must
    /// read <c>--tm-bg-elevated</c>.
    /// </summary>
    [Fact]
    public void FloatingPanelSurfaces_DoNotUseColorWhiteAsBackground()
    {
        var offences = new List<string>();
        foreach (var file in PackageFiles())
        {
            foreach (Match rule in Regex.Matches(Strip(file.Css), @"([^{}]+)\{([^{}]*)\}", RegexOptions.None, RegexTimeout))
            {
                var body = rule.Groups[2].Value;
                if (!body.Contains("background:", StringComparison.Ordinal)
                    || body.Contains("color-mix", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match declaration in Regex.Matches(body, @"background\s*:\s*([^;]+);", RegexOptions.None, RegexTimeout))
                {
                    if (declaration.Groups[1].Value.Contains("--tm-color-white", StringComparison.Ordinal))
                    {
                        offences.Add($"{file.Name}: {rule.Groups[1].Value.Trim()}");
                    }
                }
            }
        }

        offences.Should().BeEmpty(
            "a floating panel filled with --tm-color-white stays white in dark mode with dark text on top. Offences:\n"
            + string.Join("\n", offences));
    }

    [Fact]
    public void NoManualDarkThemeBlocksInComponents()
    {
        var offences = ComponentFiles()
            .Where(file => ManualDarkBlock.IsMatch(Strip(file.Css)))
            .Select(file => file.Name)
            .ToList();

        offences.Should().BeEmpty(
            "a component stylesheet must not re-declare dark colours — the theme tokens already switch. Offences: "
            + string.Join(", ", offences));
    }

    [Fact]
    public void NoHexColorConstantsInCSharp()
    {
        var root = Path.Combine(PackageRoot(), "Components", "NotionEditor");
        var offences = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".razor", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(PackageRoot(), file).Replace('\\', '/'))
            .Where(relative => !HexAllowlist.Contains(relative, StringComparer.Ordinal))
            .SelectMany(relative => HexInCSharp.Matches(File.ReadAllText(Path.Combine(PackageRoot(), relative.Replace('/', Path.DirectorySeparatorChar))))
                .Select(match => $"{relative}: {match.Value}"))
            .ToList();

        offences.Should().BeEmpty(
            "a Razor or C# colour must name a CSS token. Hex stays only on the cover gradients, the HTML export and NotionColorTokens.InlineHex. Offences: "
            + string.Join(", ", offences));
    }

    /// <summary>
    /// Nested dark (the demo puts <c>data-theme</c> on a wrapper, not on <c>html</c>). A
    /// <c>var()</c>-valued <c>--tm-notion-*</c> alias whose chain reaches a token the core dark/indigo
    /// theme or <c>_notion-dark.css</c> re-declares computes on <c>:root</c> with the LIGHT value
    /// unless the same alias is also declared on <c>.tm-notion-editor</c> or repeated in the dark block.
    /// </summary>
    [Fact]
    public void VarAliases_WhoseChainReachesAThemedToken_RecomputeInsideTheEditor()
    {
        var variables = ThemeCss.Declarations(NotionCss("components", "_notion-variables.css"));
        var dark = ThemeCss.Declarations(NotionCss("components", "_notion-dark.css"));
        var redeclared = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css")).Keys
            .Concat(ThemeCss.Declarations(ThemeCss.CssPath("theme-indigo.css")).Keys)
            .Concat(dark.Keys)
            .ToHashSet(StringComparer.Ordinal);

        var graph = new Dictionary<string, string>(ThemeCss.TokenGraph(dark: false), StringComparer.Ordinal);
        foreach (var (name, value) in variables)
        {
            graph[name] = value;
        }

        var variablesCss = ThemeCss.StripComments(File.ReadAllText(NotionCss("components", "_notion-variables.css")));
        var onEditor = SelectorsDeclaring(variablesCss, ".tm-notion-editor");

        var missing = variables
            .Where(pair => pair.Key.StartsWith("--tm-notion-", StringComparison.Ordinal))
            .Where(pair => pair.Value.Contains("var(", StringComparison.Ordinal))
            .Where(pair => Closure(pair.Key, graph).Any(token => token != pair.Key && redeclared.Contains(token)))
            .Select(pair => pair.Key)
            .Where(token => !dark.ContainsKey(token) && !onEditor.Contains(token))
            .OrderBy(token => token, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "these aliases compute on :root with the light value, so a nested dark editor inherits the wrong colour: {0}",
            string.Join(", ", missing));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NotionColorPalette_HoldsAa_OnTheDarkPage(bool indigo)
    {
        var tokens = NotionGraph(dark: true, indigo);
        var page = ThemeCss.ResolveColour("var(--tm-notion-bg)", tokens);

        foreach (var name in new[] { "gray", "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown" })
        {
            var ink = ThemeCss.ResolveColour($"var(--tm-notion-option-text-{name}-ink)", tokens);
            ThemeCss.Contrast(ink, page).Should().BeGreaterThanOrEqualTo(
                4.5,
                "--tm-notion-option-text-{0}-ink on --tm-notion-bg must hold AA in dark ({1})",
                name, indigo ? "indigo" : "default");
        }
    }

    private static HashSet<string> Closure(string token, Dictionary<string, string> graph)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(token);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current) || !graph.TryGetValue(current, out var value))
            {
                continue;
            }

            foreach (Match reference in TokenUsage.Matches(value))
            {
                pending.Push(reference.Groups[1].Value);
            }
        }

        return seen;
    }

    /// <summary>Custom properties declared by a selector that names <paramref name="needle"/>.</summary>
    private static HashSet<string> SelectorsDeclaring(string css, string needle)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match rule in Regex.Matches(css, @"([^{}]+)\{([^{}]*)\}", RegexOptions.None, RegexTimeout))
        {
            if (!rule.Groups[1].Value.Contains(needle, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match property in TokenDefinition.Matches(rule.Groups[2].Value))
            {
                declared.Add(property.Groups[1].Value);
            }
        }

        return declared;
    }

    private static Dictionary<string, string> NotionGraph(bool dark, bool indigo)
    {
        var tokens = ThemeCss.TokenGraph(dark);
        if (indigo)
        {
            foreach (var (name, value) in ThemeCss.Declarations(ThemeCss.CssPath("theme-indigo.css")))
            {
                tokens[name] = value;
            }
        }

        foreach (var (name, value) in ThemeCss.Declarations(NotionCss("components", "_notion-variables.css")))
        {
            tokens[name] = value;
        }

        if (dark)
        {
            foreach (var (name, value) in ThemeCss.Declarations(NotionCss("components", "_notion-dark.css")))
            {
                tokens[name] = value;
            }
        }

        return tokens;
    }

    private static HashSet<string> CoreDefinitions()
    {
        var defined = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "tokens.css", "tokens-dark.css", "theme-indigo.css" })
        {
            defined.UnionWith(TokenDefinition.Matches(ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath(file))))
                .Select(match => match.Groups[1].Value));
        }

        return defined;
    }

    private static IEnumerable<(string Name, string Css)> PackageFiles() =>
        ComponentFiles().Concat(GlobalFiles());

    private static IEnumerable<(string Name, string Css)> ComponentFiles()
    {
        var root = Path.Combine(PackageRoot(), "Components", "NotionEditor");
        return Directory.EnumerateFiles(root, "*.razor.css", SearchOption.AllDirectories)
            .Select(file => (Path.GetFileName(file), File.ReadAllText(file)));
    }

    private static IEnumerable<(string Name, string Css)> GlobalFiles()
    {
        var root = Path.Combine(PackageRoot(), "wwwroot", "css", "components");
        return Directory.EnumerateFiles(root, "_notion-*.css")
            .Select(file => (Path.GetFileName(file), File.ReadAllText(file)));
    }

    private static string PackageRoot() =>
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.NotionEditor");

    private static string NotionCss(params string[] parts) =>
        Path.Combine(new[] { PackageRoot(), "wwwroot", "css" }.Concat(parts).ToArray());

    private static string Strip(string css) => CssComment.Replace(css, " ");
}
