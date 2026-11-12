using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The main-area surface contract (plan 92e96b25, phase 7 / TG-008): the content panel's background is
/// a catalogued token (<c>--tm-app-shell-main-bg</c>, defaulting to <c>var(--tm-bg-surface)</c> so the
/// Inset and Flat variants keep today's look), and the new <c>AppShellVariant.Workspace</c> drops the
/// panel entirely — a transparent <c>main</c> over <c>--tm-bg-workspace</c>, no border, no shadow — so
/// the page content lies on the workspace and cards stay white. The token is THEME-RELATIVE: because
/// <c>--tm-bg-surface</c> is re-declared on the dark roots, the alias is repeated in tokens-dark.css
/// (both the dark block and the light island) — a :root-only alias would freeze the light value.
/// </summary>
public class TmAppShellVariantCssTests
{
    private const string Token = "--tm-app-shell-main-bg";

    private static string Sheet()
        => File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "components", "_app-shell.css"));

    private static string Tokens()
        => File.ReadAllText(ThemeCss.CssPath("tokens.css"));

    private static string DarkTokens()
        => File.ReadAllText(ThemeCss.CssPath("tokens-dark.css"));

    private static string Bundle()
        => File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css"));

    [Fact]
    public void TokensCss_CataloguesTheMainBackground_AliasOfTheSurface()
    {
        var declared = ThemeCss.Declarations(ThemeCss.CssPath("tokens.css"));

        declared.Should().ContainKey(Token);
        declared[Token].Should().Be("var(--tm-bg-surface)",
            "the default is today's look: the content panel keeps painting the surface colour");
    }

    [Fact]
    public void DarkTokens_RedeclareTheAlias_OnTheDarkBlockAndTheLightIsland()
    {
        // A custom property substitutes its var() where it is DECLARED, so a :root-only alias computes
        // with the light --tm-bg-surface and a nested dark region inherits that finished colour.
        var dark = ThemeCss.Declarations(ThemeCss.CssPath("tokens-dark.css"));
        dark.Should().ContainKey(Token, "the alias must re-compute against the dark --tm-bg-surface");
        dark[Token].Should().Be("var(--tm-bg-surface)",
            "the same expression as :root — the drift gate treats any other value as an undocumented difference");

        var css = ThemeCss.StripComments(DarkTokens());
        var islandStart = css.IndexOf("[data-theme=\"light\"]", StringComparison.Ordinal);
        islandStart.Should().BeGreaterThan(0, "the light island block exists");
        css[(css.IndexOf("[data-theme=\"dark\"]", StringComparison.Ordinal))..islandStart]
            .Should().Contain(Token, "the dark block re-declares the alias");
        css[islandStart..]
            .Should().Contain(Token,
                "the light island resets exactly the names the dark block flips — a missing one leaks the dark value in");
    }

    [Fact]
    public void TheMain_PaintsThroughTheCataloguedToken()
    {
        var css = ThemeCss.StripComments(Sheet());

        var rule = Rule(css, ".tm-app-shell__main");
        rule.Should().Contain($"background-color: var({Token})",
            "the Inset and Flat variants resolve the token, which defaults to the surface");
    }

    [Fact]
    public void TheWorkspaceVariant_DropsThePanel_TransparentOverTheWorkspace()
    {
        var css = ThemeCss.StripComments(Sheet());

        var rule = Rule(css, ".tm-app-shell--workspace .tm-app-shell__main");
        rule.Should().Contain("background-color: transparent",
            "the page content lies on the workspace background, not on a surface panel");
        rule.Should().NotContain("border", "no frame around the workspace content");
        rule.Should().NotContain("box-shadow", "no card shadow — the variant is chrome-less by design");
        rule.Should().NotContain("border-radius", "nothing to round without a panel");
    }

    [Fact]
    public void TheInsetAndFlatVariants_KeepTodaysPanelTreatment()
    {
        var css = ThemeCss.StripComments(Sheet());

        var inset = Rule(css, ".tm-app-shell--inset .tm-app-shell__main");
        inset.Should().Contain("border: 1px solid var(--tm-border-color)")
            .And.Contain("border-radius: var(--tm-radius-card)")
            .And.Contain("box-shadow: var(--tm-shadow-card)",
                "the default variant must render exactly the frame it always has");

        var flat = Rule(css, ".tm-app-shell--flat .tm-app-shell__main");
        flat.Should().Contain("padding: var(--tm-space-4)",
            "Flat stays the edge-to-edge, hairline-only treatment");
        flat.Should().NotContain("box-shadow");
    }

    [Fact]
    public void TheWorkspaceClasses_ShipInTheBundle()
    {
        var bundle = Bundle();

        bundle.Should().Contain(".tm-app-shell--workspace",
            "a host reading only the bundle must see the workspace modifier (rebuild regenerates it)");
        bundle.Should().Contain(Token, "the catalogued token ships in the bundle");
    }

    /// <summary>The declaration block of the first rule whose selector contains <paramref name="selector"/>.</summary>
    private static string Rule(string css, string selector)
    {
        var index = css.IndexOf(selector, StringComparison.Ordinal);
        index.Should().BeGreaterThanOrEqualTo(0, $"the stylesheet declares a rule for {selector}");

        var open = css.IndexOf('{', index);
        var depth = 0;
        for (var close = open; close < css.Length; close++)
        {
            depth += css[close] == '{' ? 1 : css[close] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return css[open..close];
            }
        }

        throw new InvalidOperationException($"unbalanced rule for {selector}");
    }
}
