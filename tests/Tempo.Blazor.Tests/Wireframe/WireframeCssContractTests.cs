using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// CSS contract of the wireframe editor (wireframe redesign plan ce19aa8c, phase 2 — tokenisation).
/// The editor chrome reads the core design tokens (dark mode, the opt-in indigo theme and consumer
/// overrides all flow through them): no hex or rgb literal, no local dark-mode block, and every
/// <c>--tm-wd-*</c> property is an alias of a core token instead of a palette of its own.
/// </summary>
public sealed class WireframeCssContractTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex ColourLiteral =
        new(@"#[0-9a-fA-F]{3,8}\b|rgba?\(|hsla?\(", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex WdDeclaration =
        new(@"(?<name>--tm-wd-[\w-]+)\s*:\s*(?<value>[^;{}]+);", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex VarReference =
        new(@"var\(\s*(?<token>--tm-[\w-]+)", RegexOptions.Compiled, RegexTimeout);

    /// <summary>Literals the editor stylesheet may still carry. Empty on purpose: the strict audit baseline for this file is 0.</summary>
    private static readonly string[] Allowlist = [];

    private static string RepoPath(params string[] parts) =>
        Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(parts).ToArray());

    private static string EditorCss() =>
        ThemeCss.StripComments(File.ReadAllText(RepoPath(
            "src", "Tempo.Blazor.Wireframe", "wwwroot", "css", "components", "_wireframe-editor.css")));

    [Fact]
    public void WireframeCss_HasNoHexOutsideAllowlist()
    {
        var css = EditorCss();

        var literals = ColourLiteral.Matches(css)
            .Select(match => match.Value)
            .Where(value => !Allowlist.Contains(value, StringComparer.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        literals.Should().BeEmpty(
            "the editor chrome takes every colour from a --tm-* token (a hex or rgba() here is a theme hack)");
    }

    [Fact]
    public void WireframeCss_HasNoLocalDarkBlockAndNoWhiteFillHack()
    {
        var css = EditorCss();

        css.Should().NotContain("[data-theme=\"dark\"]", "dark mode comes from tokens-dark.css");
        css.Should().NotContain(".tm-dark", "dark mode comes from tokens-dark.css");
        css.Should().NotContain("rect[fill=\"white\"]", "the canvas paints its page through a token, no attribute hack");
    }

    [Fact]
    public void WireframeCss_TmWdVariables_AreAliasesOfCoreTokens()
    {
        var css = EditorCss();
        var declarations = WdDeclaration.Matches(css)
            .Select(match => (Name: match.Groups["name"].Value, Value: match.Groups["value"].Value.Trim()))
            .ToList();

        declarations.Should().NotBeEmpty("the editor still exposes its --tm-wd-* surface");

        var coreTokens = ThemeCss.TokenGraph(dark: false).Keys
            .Concat(ThemeCss.TokenGraph(dark: true).Keys)
            .ToHashSet(StringComparer.Ordinal);
        var ownTokens = declarations.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);

        foreach (var (name, value) in declarations)
        {
            var references = VarReference.Matches(value).Select(m => m.Groups["token"].Value).ToList();

            references.Should().NotBeEmpty(
                "{0} must be an alias of a core token (var(--tm-…)), not a literal: '{1}'", name, value);

            foreach (var token in references)
            {
                (coreTokens.Contains(token) || ownTokens.Contains(token)).Should().BeTrue(
                    "{0} points at {1}, which is defined nowhere", name, token);
            }
        }
    }

    [Fact]
    public void WireframeSources_TmWdVariablesInUse_AreAllDefined()
    {
        var defined = WdDeclaration.Matches(EditorCss())
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var sourceRoot = RepoPath("src", "Tempo.Blazor.Wireframe");
        var used = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal)
                        || path.EndsWith(".cs", StringComparison.Ordinal)
                        || path.EndsWith(".js", StringComparison.Ordinal))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), @"var\(\s*(--tm-wd-[\w-]+)", RegexOptions.None, RegexTimeout)
                .Select(match => (File: Path.GetFileName(path), Token: match.Groups[1].Value)))
            .ToList();

        used.Where(item => !defined.Contains(item.Token))
            .Select(item => $"{item.File}: {item.Token}")
            .Distinct()
            .Should().BeEmpty("a --tm-wd-* variable read by markup or script must be declared in _wireframe-editor.css");
    }

    [Fact]
    public void WireframeSources_CarryNoHexFallbackInTmWdVariableReads()
    {
        // `var(--tm-wd-x, #hex)` is the same hack as a hex: the fallback hides an undefined alias.
        var sourceRoot = RepoPath("src", "Tempo.Blazor.Wireframe");
        var offenders = Directory.EnumerateFiles(sourceRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(path => path.EndsWith(".razor", StringComparison.Ordinal) || path.EndsWith(".js", StringComparison.Ordinal))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path),
                    @"var\(\s*--tm-wd-[\w-]+\s*,\s*(#[0-9a-fA-F]{3,8}|rgba?\()", RegexOptions.None, RegexTimeout)
                .Select(match => $"{Path.GetFileName(path)}: {match.Value}"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void CanvasElements_PaintingWithATmWdVariable_AreFlaggedForTheExportBake()
    {
        // exportSvg -> _bakePaints rewrites ONLY nodes flagged data-wd-paint into explicit colours. A node that
        // paints through var(--tm-wd-*) but is not flagged would leak a custom-property reference into the
        // standalone SVG/PNG/PDF (a connector waypoint circle exists only while its connector is selected, so
        // no browser smoke test would notice it).
        var canvas = File.ReadAllText(RepoPath(
            "src", "Tempo.Blazor.Wireframe", "Components", "Wireframe", "TmWireframeDesignerCanvas.razor"));

        var unflagged = Regex.Matches(canvas, @"<(?:rect|path|circle|ellipse|line|text|polygon)\b[^<>]*?var\(--tm-wd-[^<>]*?>",
                RegexOptions.Singleline, RegexTimeout)
            .Select(match => match.Value)
            .Where(markup => !markup.Contains("data-wd-paint", StringComparison.Ordinal))
            .Select(markup => markup.Length > 90 ? markup[..90] + "…" : markup)
            .ToList();

        unflagged.Should().BeEmpty("every var()-painted canvas node needs data-wd-paint so the export bakes it");
    }

    [Fact]
    public void WireframeCss_TmWdAliases_AreNotDeclaredOnInEditorSelectors()
    {
        // `.my-editor .tm-wd-editor { --tm-wd-accent: … }` must reach the toolbox, props, layers, minimap and
        // rulers. A custom property substitutes its var() where it is declared, so an alias block re-declared on
        // one of those descendants would shadow the consumer's override with the :root value. The aliases may only
        // live on :root, on a theme boundary and on the portaled export dialog.
        var allowedSelectors = new HashSet<string>(StringComparer.Ordinal)
        {
            ":root", "[data-theme]", "[class~=\"tm-dark\"]", "[data-tm-theme]", ".tm-wd-export-dialog"
        };

        var css = EditorCss();
        var offenders = new List<string>();
        foreach (Match rule in Regex.Matches(css, @"(?<selectors>[^{}]+)\{(?<body>[^{}]*)\}", RegexOptions.None, RegexTimeout))
        {
            var declaresAlias = Regex.IsMatch(rule.Groups["body"].Value, @"(^|[;\s])--tm-wd-[\w-]+\s*:", RegexOptions.None, RegexTimeout);
            if (!declaresAlias)
            {
                continue;
            }

            foreach (var selector in rule.Groups["selectors"].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
            {
                if (!allowedSelectors.Contains(selector))
                {
                    offenders.Add(selector);
                }
            }
        }

        offenders.Should().BeEmpty(
            "--tm-wd-* aliases may only be declared on :root, theme boundaries and .tm-wd-export-dialog, so a consumer " +
            "override on .tm-wd-editor propagates to every in-editor surface");
        allowedSelectors.Should().Contain(":root");
    }

    [Fact]
    public void WireframeCss_HasNoHardcodedPixelSizesInLayoutProperties()
    {
        // Sizes come from --tm-space-* / --tm-font-size-* tokens. 1px hairlines, the 2px focus outline and the
        // component-intrinsic 500/220/160/80/60 extents are not covered by a token and stay literal on purpose.
        var offenders = Regex.Matches(EditorCss(),
                @"^\s*(?:padding|margin|gap|font-size|width|min-width)\s*:[^;{}]*\b(?:4|6|8|12|14|40|52|100|130)px\b",
                RegexOptions.Multiline, RegexTimeout)
            .Select(match => match.Value.Trim())
            .ToList();

        offenders.Should().BeEmpty("spacing, font sizes and widths read the design tokens");
    }

    [Fact]
    public void PropertiesPanel_HasNoInlineStyles()
    {
        var panel = File.ReadAllText(RepoPath(
            "src", "Tempo.Blazor.Wireframe", "Components", "Wireframe", "TmWireframePropertiesPanel.razor"));

        Regex.Matches(panel, @"\sstyle\s*=\s*""[^""]*""", RegexOptions.None, RegexTimeout)
            .Select(match => match.Value.Trim())
            .Should().BeEmpty("layout comes from tm-wd-props__* classes that read the spacing tokens");
    }}
