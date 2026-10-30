using System.Text.RegularExpressions;
using FluentAssertions;
using Tempo.Blazor.Components.Signing;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Signing;

/// <summary>
/// Signing package CSS hygiene (Signing redesign phase 1, "Vlna 0"). Every rule below is a gate the
/// package stylesheets must keep holding:
/// <list type="bullet">
/// <item>every <c>var(--tm-*)</c> reference resolves against the core token set plus the package's own
/// declarations (or one of the runtime-injected locals), so no component can silently paint with an
/// undefined alias;</item>
/// <item>no hex or <c>rgb()</c> colour literal outside the <c>--tm-signing-role-*</c> palette
/// definitions, so dark mode and host rebranding reach every surface;</item>
/// <item>exactly one border alias — <c>--tm-border-color</c> — replacing the scattered legacy
/// <c>--tm-color-border</c> / <c>--tm-border-default</c> / <c>--tm-border-hover</c> aliases;</item>
/// <item>no width <c>@media</c> queries: responsiveness is container-driven
/// (<c>@container</c>), so a designer embedded in a narrow inspector still collapses while the
/// viewport stays wide (<c>pointer</c>/<c>hover</c> media stay allowed);</item>
/// <item>no <c>--tm-color-white</c> text on a primary fill — text on a coloured action derives from
/// <c>--tm-color-on-primary</c> / <c>--tm-text-inverse</c>, which flip in dark mode;</item>
/// <item>a sticky element never rides <c>--tm-z-modal</c>; sticky surfaces use
/// <c>--tm-z-sticky</c>, leaving the modal band to real modals.</item>
/// </list>
/// </summary>
public class SigningCssTokenHygieneTests
{
    // Signing redesign phase 4: _signing-steps.css split out of _signing-step-shell.css — 15 → 16.
    private const int ComponentStylesheetCount = 16;

    /// <summary>Custom properties injected at runtime (inline style / JS), never declared in CSS.</summary>
    private static readonly string[] RuntimeLocalCustomProperties =
    [
        "--tm-document-page-scale",
        "--tm-signing-role-color"
    ];

    /// <summary>Legacy border aliases; the canonical Signing border token is <c>--tm-border-color</c>.</summary>
    private static readonly string[] ForbiddenBorderAliases =
    [
        "--tm-color-border",
        "--tm-border-default",
        "--tm-border-hover"
    ];

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    private static readonly Regex VarReference = new(
        @"var\(\s*(?<name>--tm-[a-zA-Z0-9-]+)", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex CustomPropertyDeclaration = new(
        @"(?<name>--tm-[a-zA-Z0-9-]+)\s*:", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex ColourLiteral = new(
        @"(?<literal>#[0-9a-fA-F]{3,8}\b|\brgba?\()", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex WidthMedia = new(
        @"@media[^{]*\(\s*(min|max)-width", RegexOptions.Compiled, RegexTimeout);

    private static readonly Regex RuleBlock = new(
        @"(?<selector>[^{}@][^{}]*)\{(?<body>[^{}]*)\}", RegexOptions.Compiled, RegexTimeout);

    private static string SigningCssRoot() => Path.Combine(
        ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Signing", "wwwroot", "css");

    private static (string RelativePath, string Css)[] SigningStylesheets()
    {
        var root = SigningCssRoot();
        return Directory.EnumerateFiles(root, "*.css", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (
                RelativePath: Path.GetRelativePath(root, path).Replace('\\', '/'),
                Css: ThemeCss.StripComments(File.ReadAllText(path))))
            .ToArray();
    }

    private static HashSet<string> CollectDefinitions()
    {
        var definitions = new HashSet<string>(StringComparer.Ordinal);
        var coreCssRoot = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor", "wwwroot", "css");
        var coreFiles = Directory.EnumerateFiles(coreCssRoot, "*.css", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Equals("tempo-blazor.bundled.css", StringComparison.Ordinal));
        foreach (var file in coreFiles.Concat(Directory.EnumerateFiles(SigningCssRoot(), "*.css", SearchOption.AllDirectories)))
        {
            foreach (Match match in CustomPropertyDeclaration.Matches(File.ReadAllText(file)))
            {
                definitions.Add(match.Groups["name"].Value);
            }
        }

        foreach (var local in RuntimeLocalCustomProperties)
        {
            definitions.Add(local);
        }

        return definitions;
    }

    [Fact]
    public void SigningPackage_LoadsEveryComponentStylesheet()
    {
        var componentSheets = Directory.EnumerateFiles(
                Path.Combine(SigningCssRoot(), "components"), "_*.css")
            .ToArray();

        componentSheets.Should().HaveCount(ComponentStylesheetCount,
            "a new _*.css component stylesheet must be added to this gate (and to tempo-blazor-signing.css)");
    }

    [Fact]
    public void SigningEntryCss_ImportsEveryStylesheet()
    {
        var entry = File.ReadAllText(Path.Combine(SigningCssRoot(), "tempo-blazor-signing.css"));
        var expected = Directory.EnumerateFiles(Path.Combine(SigningCssRoot(), "components"), "_*.css")
            .Select(path => $"components/{Path.GetFileName(path)}")
            .Append("tokens-signing.css")
            .OrderBy(entry2 => entry2, StringComparer.Ordinal)
            .ToArray();

        foreach (var import in expected)
        {
            entry.Should().Contain(
                $"""@import "{import}";""",
                "tempo-blazor-signing.css is the package's single entry point — a stylesheet missing from it never ships");
        }
    }

    [Fact]
    public void TokensSigning_RoleTokensEqualDefaultColorsInOrder()
    {
        var css = ThemeCss.StripComments(File.ReadAllText(Path.Combine(SigningCssRoot(), "tokens-signing.css")));
        var rootBlock = RootBlock(css);

        var declared = Regex.Matches(rootBlock, @"--tm-signing-role-(?<index>\d+)\s*:\s*(?<value>#[0-9a-fA-F]{3,8})")
            .Select(match => (Index: int.Parse(match.Groups["index"].Value), Value: match.Groups["value"].Value))
            .OrderBy(entry => entry.Index)
            .Select(entry => entry.Value)
            .ToList();

        declared.Should().HaveCount(SigningRolePalette.DefaultColors.Count,
            "the stylesheet declares one --tm-signing-role-N token per default palette colour");
        declared.Should().Equal(
            SigningRolePalette.DefaultColors,
            "SigningRolePalette.DefaultColors and the :root role tokens in tokens-signing.css must stay " +
            "the same colours in the same order — templates serialize the hex, the stylesheet derives " +
            "border/background from the tokens");
    }

    /// <summary>The body of the first <c>:root &#123; … &#125;</c> block (the package's light-theme tokens).</summary>
    private static string RootBlock(string css)
    {
        var start = css.IndexOf(":root", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "tokens-signing.css must declare its light palette on :root");
        var open = css.IndexOf('{', start);
        var close = css.IndexOf('}', open);
        return css[(open + 1)..close];
    }

    [Fact]
    public void SigningCss_EveryTokenReference_IsDefinedOrWhitelisted()
    {
        var definitions = CollectDefinitions();
        var undefined = new List<string>();

        foreach (var (relativePath, css) in SigningStylesheets())
        {
            foreach (Match match in VarReference.Matches(css))
            {
                var token = match.Groups["name"].Value;
                if (!definitions.Contains(token))
                {
                    undefined.Add($"{relativePath}: var({token})");
                }
            }
        }

        undefined.Should().BeEmpty(
            "every var(--tm-*) in the Signing stylesheets must be declared by the core tokens, the " +
            "package itself, or the runtime-local whitelist ({0}); define the token or fix the reference",
            string.Join(", ", RuntimeLocalCustomProperties));
    }

    [Fact]
    public void SigningCss_NoColourLiteralsOutsideTheRolePalette()
    {
        var violations = new List<string>();

        foreach (var (relativePath, css) in SigningStylesheets())
        {
            foreach (var line in css.Split('\n'))
            {
                var match = ColourLiteral.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var declaresRoleColour = CustomPropertyDeclaration
                    .Matches(line)
                    .Any(declaration => declaration.Groups["name"].Value.StartsWith("--tm-signing-role-", StringComparison.Ordinal));
                if (!declaresRoleColour)
                {
                    violations.Add($"{relativePath}: '{ThemeCss.Normalise(line)}' uses {match.Groups["literal"].Value}");
                }
            }
        }

        violations.Should().BeEmpty(
            "the Signing stylesheets must paint through --tm-* tokens (or the --tm-signing-role-* palette " +
            "definitions); the only allowed colour literals are the role palette values themselves");
    }

    [Fact]
    public void SigningCss_UsesExactlyOneBorderAlias()
    {
        var violations = new List<string>();

        foreach (var (relativePath, css) in SigningStylesheets())
        {
            foreach (var alias in ForbiddenBorderAliases)
            {
                if (css.Contains(alias, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: {alias}");
                }
            }
        }

        violations.Should().BeEmpty(
            "the only border colour alias in the Signing stylesheets is --tm-border-color; " +
            "replace the legacy aliases ({0}) with it", string.Join(", ", ForbiddenBorderAliases));
    }

    [Fact]
    public void SigningCss_NoWidthMediaQueries()
    {
        var violations = new List<string>();

        foreach (var (relativePath, css) in SigningStylesheets())
        {
            if (WidthMedia.IsMatch(css))
            {
                violations.Add(relativePath);
            }
        }

        violations.Should().BeEmpty(
            "Signing components respond to their CONTAINER, not the viewport: convert width @media " +
            "queries to @container queries (640/768/1024/1280 px breakpoints); pointer/hover media stay");
    }

    [Fact]
    public void SigningCss_NoWhiteTextOnPrimaryFill()
    {
        var violations = new List<string>();

        foreach (var (relativePath, css) in SigningStylesheets())
        {
            foreach (Match rule in RuleBlock.Matches(css))
            {
                var body = rule.Groups["body"].Value;
                if (!Declares(body, "color", "--tm-color-white"))
                {
                    continue;
                }

                if (BodyReferencesPrimaryFill(body))
                {
                    violations.Add($"{relativePath}: '{ThemeCss.Normalise(rule.Groups["selector"].Value)}'");
                }
            }
        }

        violations.Should().BeEmpty(
            "text on a primary (or success/danger/warning/info) fill must use --tm-color-on-primary or " +
            "--tm-text-inverse, which flip in dark mode — raw --tm-color-white stays white on a light " +
            "primary fill and vanishes on the dark-theme primary");
    }

    [Fact]
    public void SigningCss_StickyElementsDoNotRideTheModalBand()
    {
        var violations = new List<string>();

        foreach (var (relativePath, css) in SigningStylesheets())
        {
            foreach (Match rule in RuleBlock.Matches(css))
            {
                var body = rule.Groups["body"].Value;
                if (Declares(body, "position", "sticky") && Declares(body, "z-index", "--tm-z-modal"))
                {
                    violations.Add($"{relativePath}: '{ThemeCss.Normalise(rule.Groups["selector"].Value)}'");
                }
            }
        }

        violations.Should().BeEmpty(
            "a sticky element must stack with --tm-z-sticky; --tm-z-modal belongs to real modals only");
    }

    /// <summary>Whether <paramref name="body"/> declares <paramref name="property"/> containing <paramref name="fragment"/>.</summary>
    private static bool Declares(string body, string property, string fragment)
    {
        return body.Split(';').Any(declaration =>
        {
            var separator = declaration.IndexOf(':');
            return separator > 0
                && declaration[..separator].Trim().Equals(property, StringComparison.OrdinalIgnoreCase)
                && declaration[(separator + 1)..].Contains(fragment, StringComparison.Ordinal);
        });
    }

    /// <summary>Whether any fill declaration of <paramref name="body"/> rides a primary-family token.</summary>
    private static bool BodyReferencesPrimaryFill(string body)
    {
        return body.Split(';').Any(declaration =>
        {
            var separator = declaration.IndexOf(':');
            if (separator <= 0)
            {
                return false;
            }

            var property = declaration[..separator].Trim();
            if (property is not ("background" or "background-color"))
            {
                return false;
            }

            return declaration[(separator + 1)..].Contains("--tm-color-primary", StringComparison.Ordinal)
                || declaration[(separator + 1)..].Contains("--tm-color-success", StringComparison.Ordinal)
                || declaration[(separator + 1)..].Contains("--tm-color-danger", StringComparison.Ordinal)
                || declaration[(separator + 1)..].Contains("--tm-color-warning", StringComparison.Ordinal)
                || declaration[(separator + 1)..].Contains("--tm-color-info", StringComparison.Ordinal);
        });
    }
}
